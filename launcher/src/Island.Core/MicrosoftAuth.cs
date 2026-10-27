using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;
using BlockDock.Core;

namespace Island.Core;

public sealed record DeviceLogin(string DeviceCode, string UserCode, string VerificationUrl, int Interval, int ExpiresIn);
public sealed record AuthResult(PlayerAccount Account, LaunchIdentity Identity);

public sealed partial class MicrosoftAuth : IDisposable
{
    private readonly HttpClient http;
    private const string Authority = "https://login.microsoftonline.com/consumers/oauth2/v2.0/";
    private const string Scope = "XboxLive.signin offline_access";
    public MicrosoftAuth(HttpMessageHandler? handler = null, bool verifyCertificates = true)
    {
        http = new HttpClient(handler ?? CreateHandler(verifyCertificates));
        http.Timeout = TimeSpan.FromSeconds(45);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("IslandLauncher/0.10.23");
    }
    internal static HttpClientHandler CreateHandler(bool verifyCertificates)
    {
        var handler = new HttpClientHandler();
        if (!verifyCertificates) handler.ServerCertificateCustomValidationCallback = ValidateCompatibilityCertificate;
        return handler;
    }
    internal static bool ValidateCompatibilityCertificate(HttpRequestMessage request, X509Certificate2? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        if (errors == SslPolicyErrors.None) return true;
        if (certificate is null || request.RequestUri?.Scheme != Uri.UriSchemeHttps) return false;
        return request.RequestUri.Host.ToLowerInvariant() is "login.microsoftonline.com" or "user.auth.xboxlive.com" or "xsts.auth.xboxlive.com" or "api.minecraftservices.com";
    }
    public async Task<DeviceLogin> BeginAsync(string clientId, CancellationToken token)
    {
        if (!Guid.TryParse(clientId, out _)) throw new ArgumentException("请先在设置中填写此启动器自己的 Microsoft 应用 Client ID。");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = clientId, ["scope"] = Scope });
        using var response = await http.PostAsync(Authority + "devicecode", content, token);
        var data = await ReadAsync(response, "请求 Microsoft 设备码", token);
        var url = data.Str("verification_uri");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !(uri.Host == "microsoft.com" || uri.Host.EndsWith(".microsoft.com", StringComparison.OrdinalIgnoreCase) || uri.Host == "login.live.com"))
            throw new InvalidDataException("Microsoft 返回了无法识别的登录地址。");
        return new(data.Str("device_code"), data.Str("user_code"), url, Math.Max(5, data["interval"]?.GetValue<int>() ?? 5), data["expires_in"]?.GetValue<int>() ?? 900);
    }
    public async Task<AuthResult> CompleteAsync(string clientId, DeviceLogin device, CancellationToken token)
    {
        var expires = DateTimeOffset.UtcNow.AddSeconds(device.ExpiresIn); var interval = device.Interval;
        while (DateTimeOffset.UtcNow < expires)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), token);
            using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = clientId, ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code", ["device_code"] = device.DeviceCode });
            using var response = await http.PostAsync(Authority + "token", content, token);
            var data = JsonNode.Parse(await response.Content.ReadAsStringAsync(token)) as JsonObject ?? throw new InvalidDataException("Microsoft 登录响应无效。");
            var error = data.Str("error");
            if (error == "authorization_pending") continue;
            if (error == "slow_down") { interval += 5; continue; }
            if (!response.IsSuccessStatusCode || error.Length > 0) throw new InvalidOperationException(error is "expired_token" or "authorization_declined" ? "登录已过期或被取消，请重新开始。" : "Microsoft 登录未完成（" + error + "）。请检查应用是否允许个人账号和公共客户端设备登录。");
            return await ExchangeAsync(clientId, data, token);
        }
        throw new TimeoutException("设备码已过期，请重新登录。");
    }
    public async Task<AuthResult> RefreshAsync(PlayerAccount account, CancellationToken token)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        { ["client_id"] = account.ClientId, ["grant_type"] = "refresh_token", ["refresh_token"] = SecretProtection.Unprotect(account.ProtectedRefreshToken), ["scope"] = Scope });
        using var response = await http.PostAsync(Authority + "token", content, token);
        var data = await ReadAsync(response, "刷新 Microsoft 登录（失败时请重新添加账号）", token);
        if (data.Str("refresh_token").Length == 0) data["refresh_token"] = SecretProtection.Unprotect(account.ProtectedRefreshToken);
        var result = await ExchangeAsync(account.ClientId, data, token);
        result.Account.Id = account.Id;
        if (result.Account.SkinUrl == account.SkinUrl) result.Account.SkinPng = account.SkinPng;
        return result;
    }
    private async Task<AuthResult> ExchangeAsync(string clientId, JsonObject oauth, CancellationToken token)
    {
        if (oauth.Str("access_token").Length == 0 || oauth.Str("refresh_token").Length == 0)
            throw new InvalidDataException("Microsoft 未返回完整登录凭据，请重新授权并允许 offline_access。");
        var xbox = await PostAsync("https://user.auth.xboxlive.com/user/authenticate", new JsonObject
        {
            ["Properties"] = new JsonObject { ["AuthMethod"] = "RPS", ["SiteName"] = "user.auth.xboxlive.com", ["RpsTicket"] = "d=" + oauth.Str("access_token") },
            ["RelyingParty"] = "http://auth.xboxlive.com", ["TokenType"] = "JWT"
        }, "Xbox Live 身份验证", token);
        var xboxHash = xbox["DisplayClaims"]?["xui"]?[0].Str("uhs") ?? "";
        if (xbox.Str("Token").Length == 0 || xboxHash.Length == 0) throw new InvalidDataException("Xbox Live 返回了不完整的身份凭据。");
        var xsts = await PostAsync("https://xsts.auth.xboxlive.com/xsts/authorize", new JsonObject
        {
            ["Properties"] = new JsonObject { ["SandboxId"] = "RETAIL", ["UserTokens"] = new JsonArray(xbox.Str("Token")) },
            ["RelyingParty"] = "rp://api.minecraftservices.com/", ["TokenType"] = "JWT"
        }, "Xbox 游戏授权", token);
        var hash = xsts["DisplayClaims"]?["xui"]?[0].Str("uhs") ?? "";
        if (hash != xboxHash || xsts.Str("Token").Length == 0) throw new InvalidDataException("Xbox 游戏授权的用户标识不匹配或凭据不完整。");
        var minecraft = await PostAsync("https://api.minecraftservices.com/authentication/login_with_xbox", new JsonObject
        { ["identityToken"] = "XBL3.0 x=" + hash + ";" + xsts.Str("Token") }, "Minecraft 游戏授权（应用须具备 Minecraft API 访问资格）", token);
        var access = minecraft.Str("access_token");
        if (access.Length == 0) throw new InvalidDataException("Minecraft 未返回访问凭据。");
        using var ownershipRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/entitlements/mcstore");
        ownershipRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        using var ownershipResponse = await http.SendAsync(ownershipRequest, token);
        var ownership = await ReadAsync(ownershipResponse, "验证 Minecraft 游戏资格", token);
        if (ownership["items"] is not JsonArray) throw new InvalidDataException("Minecraft 游戏资格响应无效。");
        // An empty store list is possible for subscriptions. Like HMCL, require a successful
        // entitlement request AND a valid Java profile rather than rejecting Game Pass here.
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        using var profileResponse = await http.SendAsync(request, token);
        if (profileResponse.StatusCode == System.Net.HttpStatusCode.NotFound) throw new InvalidOperationException("账号没有可用的 Minecraft Java 玩家档案。请确认拥有游戏并已在官方启动器完成玩家名设置。");
        var profile = await ReadAsync(profileResponse, "读取 Minecraft 玩家档案", token);
        if (profile.Str("name").Length == 0 || !Guid.TryParse(profile.Str("id"), out _)) throw new InvalidDataException("Minecraft 玩家档案无效。");
        var account = new PlayerAccount { Name = profile.Str("name"), Uuid = profile.Str("id"), Microsoft = true, ClientId = clientId, ProtectedRefreshToken = SecretProtection.Protect(oauth.Str("refresh_token")) };
        PlayerSkins.ReadProfile(account, profile);
        return new(account, new(account.Name, account.Uuid, access, true));
    }
    public async Task<byte[]> DownloadSkinAsync(string url, CancellationToken token)
    {
        using var response = await http.GetAsync(PlayerSkins.TextureUri(url), HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > PlayerSkins.MaximumBytes) throw new InvalidDataException("皮肤文件过大。");
        await using var input = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[8192]; int read;
        while ((read = await input.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + read > PlayerSkins.MaximumBytes) throw new InvalidDataException("皮肤文件过大。");
            output.Write(buffer, 0, read);
        }
        var png = output.ToArray(); PlayerSkins.Validate(png); return png;
    }
    public async Task SetSkinAsync(PlayerAccount account, LaunchIdentity identity, byte[]? png, string variant, CancellationToken token)
    {
        if (!account.Microsoft || !identity.Microsoft || account.Uuid != identity.Uuid) throw new ArgumentException("请使用此 Microsoft 账号的登录凭据。");
        if (png is not null) PlayerSkins.Validate(png, variant);
        using var request = new HttpRequestMessage(png is null ? HttpMethod.Delete : HttpMethod.Post,
            "https://api.minecraftservices.com/minecraft/profile/skins" + (png is null ? "/active" : ""));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identity.AccessToken);
        if (png is not null)
        {
            var form = new MultipartFormDataContent();
            form.Add(new StringContent(variant), "variant");
            var image = new ByteArrayContent(png); image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(image, "file", "skin.png"); request.Content = form;
        }
        using var response = await http.SendAsync(request, token);
        if (png is null && response.StatusCode == System.Net.HttpStatusCode.NoContent)
        { account.SkinUrl = ""; account.SkinVariant = "classic"; account.SkinPng = null; return; }
        var profile = await ReadAsync(response, png is null ? "恢复默认皮肤" : "上传玩家皮肤", token);
        PlayerSkins.ReadProfile(account, profile); account.SkinPng = png;
    }
    private async Task<JsonObject> PostAsync(string url, JsonObject data, string stage, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(data) };
        if (new Uri(url).Host.EndsWith("xboxlive.com", StringComparison.Ordinal)) request.Headers.Add("x-xbl-contract-version", "1");
        using var response = await http.SendAsync(request, token);
        return await ReadAsync(response, stage, token);
    }
    private static async Task<JsonObject> ReadAsync(HttpResponseMessage response, string stage, CancellationToken token)
    {
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{stage}失败（HTTP {(int)response.StatusCode}）。请检查账号、网络及应用授权。");
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(token)) as JsonObject ?? throw new InvalidDataException(stage + "返回了无效数据。");
    }
    public void Dispose() => http.Dispose();
}
