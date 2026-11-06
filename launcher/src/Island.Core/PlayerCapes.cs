using System.Net.Http.Headers;
using System.Net.Http.Json;
using BlockDock.Core;
namespace Island.Core;
public sealed record PlayerCape(string Id, string Name, string Url);
public sealed partial class MicrosoftAuth
{
    public async Task<byte[]> DownloadCapeAsync(string url, CancellationToken token)
    {
        using var response = await http.GetAsync(PlayerSkins.TextureUri(url), HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[8192]; int read;
        while ((read = await input.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + read > PlayerSkins.MaximumBytes) throw new InvalidDataException("披风文件过大。");
            output.Write(buffer, 0, read);
        }
        var png = output.ToArray(); PlayerSkins.Validate(png); return png;
    }
    public async Task SetCapeAsync(PlayerAccount account, LaunchIdentity identity, string id, CancellationToken token)
    {
        if (!account.Microsoft || !identity.Microsoft || account.Uuid != identity.Uuid) throw new ArgumentException("请使用此账号的登录凭据。");
        if (id.Length > 0 && !account.Capes.Any(c => c.Id == id)) throw new ArgumentException("此账号未拥有该披风。");
        using var request = new HttpRequestMessage(id.Length == 0 ? HttpMethod.Delete : HttpMethod.Put, "https://api.minecraftservices.com/minecraft/profile/capes/active");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identity.AccessToken);
        if (id.Length > 0) request.Content = JsonContent.Create(new { capeId = id });
        using var response = await http.SendAsync(request, token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"切换披风失败（HTTP {(int)response.StatusCode}）。");
        account.ActiveCapeId = id; account.CapePng = null;
    }
}
