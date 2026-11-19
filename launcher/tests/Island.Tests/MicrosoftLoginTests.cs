using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Island.Core;

internal static class MicrosoftLoginTests
{
    private static readonly string ClientId = Guid.NewGuid().ToString();
    private static Dictionary<string, string> Form(string value) => value.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Split('=', 2)).ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1].Replace('+', ' ')));
    private static string State(BrowserLogin login) => Form(login.AuthorizationUri.Query)["state"];
    private static async Task<HttpResponseMessage> Callback(BrowserLogin login, string query, string? path = null, string? host = null, HttpMethod? method = null)
    {
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(5) };
        using var request = new HttpRequestMessage(method ?? HttpMethod.Get, new Uri(login.RedirectUri, (path ?? "/") + query));
        if (host is not null) request.Headers.Host = host;
        return await client.SendAsync(request);
    }
    private static Task<bool> IsClosed(BrowserLogin login) => Task.FromResult(!login.IsListening);
    private static async Task<Exception> Failure(Task task)
    {
        try { await task; } catch (Exception ex) { return ex; }
        throw new Exception("The invalid login unexpectedly succeeded.");
    }
    private sealed class Service(string mode = "normal") : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public Dictionary<string, string>? Grant { get; private set; }
        public bool AuthorizedEntitlements { get; private set; }
        public bool AuthorizedProfile { get; private set; }
        public bool XboxRequestValid { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath; Paths.Add(path);
            string data;
            switch (path)
            {
                case "/consumers/oauth2/v2.0/token":
                    Grant = Form(await request.Content!.ReadAsStringAsync(cancellationToken));
                    if (mode == "oauth-denied") return new(HttpStatusCode.BadRequest) { Content = new StringContent("secret-response-contents") };
                    data = mode == "missing-token" ? "{}" : mode == "refresh-no-rotation" ? """{"access_token":"msa"}""" : """{"access_token":"msa","refresh_token":"refresh"}"""; break;
                case "/user/authenticate":
                    data = """{"Token":"xbox","DisplayClaims":{"xui":[{"uhs":"123"}]}}"""; break;
                case "/xsts/authorize":
                    data = mode == "wrong-user" ? """{"Token":"xsts","DisplayClaims":{"xui":[{"uhs":"456"}]}}""" : """{"Token":"xsts","DisplayClaims":{"xui":[{"uhs":"123"}]}}"""; break;
                case "/authentication/login_with_xbox":
                    var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
                    XboxRequestValid = body["identityToken"]?.GetValue<string>() == "XBL3.0 x=123;xsts" && body.Count == 1;
                    data = """{"access_token":"minecraft"}"""; break;
                case "/entitlements/mcstore":
                    AuthorizedEntitlements = request.Method == HttpMethod.Get && request.Headers.Authorization?.Parameter == "minecraft";
                    if (mode == "entitlements-denied") return new(HttpStatusCode.Forbidden) { Content = new StringContent("{}")} ;
                    data = mode == "bad-entitlements" ? "{}" : """{"items":[]}"""; break;
                case "/minecraft/profile":
                    AuthorizedProfile = request.Headers.Authorization?.Parameter == "minecraft";
                    if (mode == "no-profile") return new(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
                    data = """{"id":"123456781234123412341234567890ab","name":"AuthTester"}"""; break;
                default: throw new Exception("Unexpected auth destination: " + request.RequestUri);
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(data) };
        }
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        check(Guid.TryParse(MicrosoftApplication.ClientId, out _) && MicrosoftApplication.ResolveClientId(null) == MicrosoftApplication.ClientId
            && MicrosoftApplication.ResolveClientId(" ") == MicrosoftApplication.ClientId, "Empty legacy settings use the built-in ISLAND application identifier");
        check(MicrosoftApplication.ResolveClientId(" " + ClientId + " ") == ClientId, "Explicit advanced client-ID overrides remain compatible");
        try { MicrosoftApplication.ResolveClientId("invalid"); throw new Exception("Invalid client override accepted."); }
        catch (ArgumentException) { check(true, "Invalid advanced client overrides fail before opening a login flow"); }
        check(BrowserLogin.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk") == "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", "PKCE S256 matches the RFC 7636 test vector");
        using var service = new Service(); using var auth = new MicrosoftAuth(service);
        using var login = auth.BeginBrowserLogin(ClientId); using var other = auth.BeginBrowserLogin(ClientId);
        var parameters = Form(login.AuthorizationUri.Query);
        check(login.AuthorizationUri.Host == "login.microsoftonline.com" && parameters["response_type"] == "code" && parameters["response_mode"] == "query"
            && parameters["code_challenge_method"] == "S256" && parameters["prompt"] == "select_account" && parameters["scope"] == "XboxLive.signin offline_access"
            && parameters["client_id"] == ClientId && parameters["redirect_uri"] == login.RedirectUri.AbsoluteUri, "System browser authorization uses the own public client, account picker and registered localhost callback");
        check(login.CodeVerifier.Length is >= 43 and <= 128 && parameters["code_challenge"] == BrowserLogin.Challenge(login.CodeVerifier)
            && !login.AuthorizationUri.AbsoluteUri.Contains(login.CodeVerifier) && !parameters.ContainsKey("client_secret")
            && State(login) != State(other) && login.CodeVerifier != other.CodeVerifier && login.RedirectUri.Port != other.RedirectUri.Port,
            "Each attempt gets independent cryptographic state, verifier and reserved loopback port; no secret or verifier is sent to the browser");
        var completion = auth.CompleteBrowserAsync(login, default);
        using (var wrong = await Callback(login, "?code=untrusted&state=wrong")) check(wrong.StatusCode == HttpStatusCode.BadRequest && !completion.IsCompleted && service.Paths.Count == 0, "Unmatched state cannot redeem a code or cancel the legitimate attempt");
        using (var missing = await Callback(login, "?code=untrusted")) check(missing.StatusCode == HttpStatusCode.BadRequest && !completion.IsCompleted, "Missing state is rejected");
        using (var duplicate = await Callback(login, "?code=a&code=b&state=" + State(login))) check(duplicate.StatusCode == HttpStatusCode.BadRequest && !completion.IsCompleted, "Duplicate callback fields cannot override the authorization code");
        using (var host = await Callback(login, "?code=a&state=" + State(login), host: "external.example")) check(host.StatusCode == HttpStatusCode.BadRequest && !completion.IsCompleted, "Local callback rejects a foreign Host header");
        using (var method = await Callback(login, "?code=a&state=" + State(login), method: HttpMethod.Post)) check(method.StatusCode == HttpStatusCode.BadRequest && !completion.IsCompleted, "Local callback accepts only the requested GET query response");
        using (var route = await Callback(login, "?code=a&state=" + State(login), path: "/favicon.ico")) check(route.StatusCode == HttpStatusCode.NotFound && !completion.IsCompleted, "Unrelated browser routes leave the login pending");
        using (var response = await Callback(login, "?code=received-code&state=" + State(login)))
        {
            var page = await response.Content.ReadAsStringAsync();
            check(response.StatusCode == HttpStatusCode.OK && response.Headers.CacheControl?.NoStore == true && !page.Contains("received-code")
                && !page.Contains(State(login)) && !page.Contains(login.CodeVerifier) && !page.Contains("<script"), "Callback page never echoes codes, state or tokens and forbids caching");
        }
        var result = await completion;
        check(service.Grant is { } grant && grant["grant_type"] == "authorization_code" && grant["client_id"] == ClientId && grant["code"] == "received-code"
            && grant["code_verifier"] == login.CodeVerifier && grant["redirect_uri"] == login.RedirectUri.AbsoluteUri && !grant.ContainsKey("client_secret"), "Token redemption uses the same redirect URI and PKCE verifier without a client secret");
        check(service.XboxRequestValid && service.AuthorizedEntitlements && service.AuthorizedProfile && service.Paths.TakeLast(2).SequenceEqual(new[] { "/entitlements/mcstore", "/minecraft/profile" }), "Browser login verifies Xbox, XSTS, Minecraft token, store entitlements and Java profile in order");
        check(result.Identity is { Microsoft: true, Name: "AuthTester", AccessToken: "minecraft" } && result.Account.ClientId == ClientId
            && result.Account.ProtectedRefreshToken != "refresh" && SecretProtection.Unprotect(result.Account.ProtectedRefreshToken) == "refresh", "Verified account keeps only DPAPI protected refresh credentials; an empty store list still permits a valid subscription profile");
        check(await IsClosed(login), "Successful authorization immediately closes the callback listener");
        check(await Failure(login.WaitForCodeAsync(default)) is InvalidOperationException, "A completed authorization attempt cannot be reused");

        foreach (var mode in new[] { "oauth-denied", "missing-token", "wrong-user", "entitlements-denied", "bad-entitlements", "no-profile" })
        {
            using var failedService = new Service(mode); using var failedAuth = new MicrosoftAuth(failedService); using var failedLogin = failedAuth.BeginBrowserLogin(ClientId);
            var pending = failedAuth.CompleteBrowserAsync(failedLogin, default);
            using var response = await Callback(failedLogin, "?code=code&state=" + State(failedLogin));
            var error = await Failure(pending);
            check(error is InvalidOperationException or InvalidDataException && !error.Message.Contains("secret-response-contents"), "Invalid authentication stops without exposing response credentials: " + mode);
            if (mode is "entitlements-denied" or "bad-entitlements") check(!failedService.Paths.Contains("/minecraft/profile"), "Failed entitlement validation cannot fall through to a profile: " + mode);
            if (mode == "wrong-user") check(!failedService.Paths.Contains("/authentication/login_with_xbox"), "Mismatched Xbox user identities never obtain a Minecraft token");
        }
        using (var declined = auth.BeginBrowserLogin(ClientId))
        {
            var pending = auth.CompleteBrowserAsync(declined, default); var requestCount = service.Paths.Count;
            using var response = await Callback(declined, "?error=access_denied&error_description=sensitive&state=" + State(declined));
            check(await Failure(pending) is InvalidOperationException denied && denied.Message.Contains("已取消") && !denied.Message.Contains("sensitive") && service.Paths.Count == requestCount, "A valid refusal stops cleanly without exchanging tokens or displaying untrusted descriptions");
        }
        using (var canceled = auth.BeginBrowserLogin(ClientId))
        using (var cancellation = new CancellationTokenSource())
        {
            var pending = auth.CompleteBrowserAsync(canceled, cancellation.Token); cancellation.Cancel();
            check(await Failure(pending) is OperationCanceledException && await IsClosed(canceled), "Canceling a pending login releases its loopback socket");
        }
        using (var closed = auth.BeginBrowserLogin(ClientId))
        {
            var pending = closed.WaitForCodeAsync(default); closed.Dispose();
            check(await Failure(pending) is OperationCanceledException && await IsClosed(closed), "Closing the login session cancels pending callback work");
        }
        using (var expired = new BrowserLogin(ClientId, "https://login.microsoftonline.com/consumers/oauth2/v2.0/", "XboxLive.signin offline_access", TimeSpan.FromMilliseconds(100)))
            check(await Failure(expired.WaitForCodeAsync(default)) is TimeoutException && await IsClosed(expired), "A login without a callback times out and closes the reserved port");
        using (var refreshService = new Service("refresh-no-rotation"))
        using (var refreshAuth = new MicrosoftAuth(refreshService))
        {
            var refreshed = await refreshAuth.RefreshAsync(result.Account, default);
            check(refreshService.Grant!["grant_type"] == "refresh_token" && refreshed.Account.Id == result.Account.Id
                && SecretProtection.Unprotect(refreshed.Account.ProtectedRefreshToken) == "refresh" && refreshService.AuthorizedEntitlements,
                "Existing device-code or browser accounts refresh through the same verified chain and keep non-rotated credentials");
        }
    }
}
