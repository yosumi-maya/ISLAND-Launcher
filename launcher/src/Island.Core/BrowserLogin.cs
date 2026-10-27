using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Island.Core;

/// <summary>A single, short-lived PKCE authorization attempt. Only a loopback socket is opened.</summary>
public sealed class BrowserLogin : IDisposable
{
    private readonly TcpListener listener;
    private readonly CancellationTokenSource lifetime = new();
    private readonly TimeSpan timeout;
    private readonly string state;
    private int waiting;
    private int disposed;
    internal string ClientId { get; }
    internal string CodeVerifier { get; }
    public Uri RedirectUri { get; }
    public Uri AuthorizationUri { get; }
    internal bool IsListening => listener.Server.IsBound;

    internal BrowserLogin(string clientId, string authority, string scope, TimeSpan? timeout = null)
    {
        if (!Guid.TryParse(clientId, out _)) throw new ArgumentException("请先在设置中填写此启动器自己的 Microsoft 应用 Client ID。");
        ClientId = clientId; this.timeout = timeout ?? TimeSpan.FromMinutes(5);
        state = Base64Url(RandomNumberGenerator.GetBytes(32));
        CodeVerifier = Base64Url(RandomNumberGenerator.GetBytes(64));
        listener = new TcpListener(IPAddress.Loopback, 0); listener.Server.ExclusiveAddressUse = true;
        listener.Start(4);
        RedirectUri = new Uri($"http://localhost:{((IPEndPoint)listener.LocalEndpoint).Port}/");
        var values = new Dictionary<string, string>
        {
            ["client_id"] = clientId, ["response_type"] = "code", ["response_mode"] = "query",
            ["redirect_uri"] = RedirectUri.AbsoluteUri, ["scope"] = scope, ["prompt"] = "select_account",
            ["state"] = state, ["code_challenge"] = Challenge(CodeVerifier), ["code_challenge_method"] = "S256"
        };
        AuthorizationUri = new Uri(authority + "authorize?" + string.Join("&", values.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value))));
    }

    internal static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal async Task<string> WaitForCodeAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        if (Interlocked.Exchange(ref waiting, 1) != 0) throw new InvalidOperationException("此登录请求已经使用，请重新开始登录。");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        linked.CancelAfter(timeout);
        try
        {
            while (true)
            {
                using var client = await listener.AcceptTcpClientAsync(linked.Token).ConfigureAwait(false);
                using var requestLifetime = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
                requestLifetime.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    var stream = client.GetStream();
                    var headers = await ReadHeadersAsync(stream, requestLifetime.Token).ConfigureAwait(false);
                    var fields = headers.Split("\r\n"); var first = fields[0].Split(' ');
                    var host = fields.Where(line => line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (first.Length != 3 || first[0] != "GET" || first[2] is not ("HTTP/1.1" or "HTTP/1.0") || host.Length != 1
                        || !string.Equals(host[0][5..].Trim(), RedirectUri.Authority, StringComparison.OrdinalIgnoreCase))
                    { await ReplyAsync(stream, 400, "无效的本机登录请求。", requestLifetime.Token).ConfigureAwait(false); continue; }
                    var target = first[1]; var separator = target.IndexOf('?');
                    if ((separator < 0 ? target : target[..separator]) != "/")
                    { await ReplyAsync(stream, 404, "页面不存在。", requestLifetime.Token).ConfigureAwait(false); continue; }
                    Dictionary<string, string> query;
                    try { query = ParseQuery(separator < 0 ? "" : target[(separator + 1)..]); }
                    catch (Exception ex) when (ex is InvalidDataException or UriFormatException)
                    { await ReplyAsync(stream, 400, "无效的登录回调参数。", requestLifetime.Token).ConfigureAwait(false); continue; }
                    if (!query.TryGetValue("state", out var returnedState) || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(returnedState))
                        || query.ContainsKey("code") == query.ContainsKey("error"))
                    { await ReplyAsync(stream, 400, "登录请求不匹配，请返回启动器重新登录。", requestLifetime.Token).ConfigureAwait(false); continue; }
                    if (query.TryGetValue("error", out var error))
                    {
                        await ReplyAsync(stream, 200, "授权未完成，请返回 ISLAND Launcher。", requestLifetime.Token).ConfigureAwait(false);
                        throw new BrowserAuthorizationException(error == "access_denied" ? "已取消 Microsoft 授权，可重试或改用设备码登录。" : "Microsoft 授权未完成，请检查应用配置或改用设备码登录。");
                    }
                    var code = query["code"];
                    if (string.IsNullOrWhiteSpace(code))
                    { await ReplyAsync(stream, 400, "未收到授权码，请返回启动器重新登录。", requestLifetime.Token).ConfigureAwait(false); continue; }
                    await ReplyAsync(stream, 200, "已收到授权，请返回 ISLAND Launcher 完成游戏资格验证。此页面可以关闭。", requestLifetime.Token).ConfigureAwait(false);
                    return code;
                }
                catch (Exception ex) when ((ex is IOException or SocketException or InvalidDataException or OperationCanceledException) && !linked.IsCancellationRequested)
                { /* Ignore unrelated or incomplete local requests; the original authorization remains pending. */ }
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && !lifetime.IsCancellationRequested)
        { throw new TimeoutException("浏览器登录已超时，请重试或改用设备码登录。"); }
        catch (SocketException) when (linked.IsCancellationRequested)
        { throw new OperationCanceledException(linked.Token); }
        finally { listener.Stop(); }
    }

    private static async Task<string> ReadHeadersAsync(NetworkStream stream, CancellationToken token)
    {
        var bytes = new byte[16 * 1024]; var length = 0;
        while (length < bytes.Length)
        {
            var count = await stream.ReadAsync(bytes.AsMemory(length), token).ConfigureAwait(false);
            if (count == 0) throw new IOException("Incomplete callback request.");
            length += count;
            var headers = Encoding.ASCII.GetString(bytes, 0, length);
            var end = headers.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (end >= 0) return headers[..end];
        }
        throw new InvalidDataException("Callback headers exceed the limit.");
    }

    private static Dictionary<string, string> ParseQuery(string value)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in value.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = part.IndexOf('=');
            var key = Uri.UnescapeDataString((split < 0 ? part : part[..split]).Replace('+', ' '));
            var item = Uri.UnescapeDataString((split < 0 ? "" : part[(split + 1)..]).Replace('+', ' '));
            if (!result.TryAdd(key, item)) throw new InvalidDataException("Duplicate callback parameter.");
        }
        return result;
    }

    private static async Task ReplyAsync(NetworkStream stream, int status, string message, CancellationToken token)
    {
        var body = Encoding.UTF8.GetBytes("<!doctype html><html lang=\"zh-CN\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>ISLAND Launcher</title><h1>ISLAND Launcher</h1><p>" + message + "</p></html>");
        var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {(status == 200 ? "OK" : status == 404 ? "Not Found" : "Bad Request")}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\nCache-Control: no-store\r\nReferrer-Policy: no-referrer\r\nX-Content-Type-Options: nosniff\r\nContent-Security-Policy: default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'\r\n\r\n");
        await stream.WriteAsync(headers, token).ConfigureAwait(false); await stream.WriteAsync(body, token).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel(); listener.Stop();
    }
    private sealed class BrowserAuthorizationException(string message) : InvalidOperationException(message);
}
