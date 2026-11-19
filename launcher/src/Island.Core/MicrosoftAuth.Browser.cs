using BlockDock.Core;

namespace Island.Core;

public sealed partial class MicrosoftAuth
{
    public BrowserLogin BeginBrowserLogin(string clientId) => new(clientId, Authority, Scope);

    public async Task<AuthResult> CompleteBrowserAsync(BrowserLogin login, CancellationToken token)
    {
        var code = await login.WaitForCodeAsync(token).ConfigureAwait(false);
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = login.ClientId, ["grant_type"] = "authorization_code", ["code"] = code,
            ["redirect_uri"] = login.RedirectUri.AbsoluteUri, ["code_verifier"] = login.CodeVerifier, ["scope"] = Scope
        });
        using var response = await http.PostAsync(Authority + "token", content, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // Do not surface the response body: it can contain user identifiers and OAuth credentials.
            throw new InvalidOperationException($"Microsoft 授权码兑换失败（HTTP {(int)response.StatusCode}）。请在 Azure 的“移动和桌面应用程序”中添加 http://localhost，允许个人账号；也可改用设备码登录。无需客户端密钥。");
        }
        var data = await ReadAsync(response, "兑换 Microsoft 授权码", token).ConfigureAwait(false);
        if (data.Str("error").Length > 0) throw new InvalidOperationException("Microsoft 授权码已失效或应用配置不正确，请重新登录。");
        return await ExchangeAsync(login.ClientId, data, token).ConfigureAwait(false);
    }
}
