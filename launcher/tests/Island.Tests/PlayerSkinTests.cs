using System.Buffers.Binary;
using System.Net;
using System.Text.Json.Nodes;
using Island.Core;

internal static class PlayerSkinTests
{
    public static async Task RunAsync(string root, Action<bool, string> check)
    {
        byte[] Png(int height = 64)
        {
            var png = new byte[33]; new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(png, 0);
            "IHDR"u8.CopyTo(png.AsSpan(12)); BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), 64); BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20), height); return png;
        }
        void Reject(Action action, string description)
        {
            try { action(); } catch (ArgumentException) { check(true, description); return; }
            throw new Exception(description);
        }
        var png = Png(); PlayerSkins.Validate(png, "slim"); PlayerSkins.Validate(Png(32));
        check(true, "Skin metadata accepts modern PNG and legacy classic dimensions");
        Reject(() => PlayerSkins.Validate(Png(32), "slim"), "Legacy skins cannot use slim geometry");
        Reject(() => PlayerSkins.Validate(png, "invalid"), "Reject unsupported skin models");
        Reject(() => PlayerSkins.Validate(new byte[PlayerSkins.MaximumBytes + 1]), "Reject oversized skin files");
        Reject(() => PlayerSkins.TextureUri("https://textures.minecraft.net.evil.test/texture/a"), "Only download skin textures from the official host");
        Reject(() => PlayerSkins.TextureUri("file:///C:/skin.png"), "Reject local or non-HTTP texture URLs");
        check(PlayerSkins.TextureUri("http://textures.minecraft.net/texture/a").Scheme == "https", "Upgrade official texture requests to HTTPS");
        var account = PlayerAccount.Offline("SkinTester"); account.Microsoft = true;
        account.SkinPng = png; account.SkinVariant = "slim"; account.SkinUrl = "https://textures.minecraft.net/texture/old";
        var store = new LauncherStore(Path.Combine(root, "skin-settings"));
        var state = store.Load(); state.Accounts.Add(account); store.Save(state);
        check(store.Load().Accounts.Single().SkinPng!.SequenceEqual(png) && store.Load().Accounts.Single().SkinVariant == "slim", "Skin image and model survive settings reload");
        var identity = new LaunchIdentity(account.Name, account.Uuid, "skin-access", true);
        int uploads = 0, resets = 0;
        using var auth = new MicrosoftAuth(new SkinHandler(async request =>
        {
            if (request.RequestUri!.Host == "textures.minecraft.net")
            {
                check(request.Headers.Authorization is null, "Texture downloads never receive the Minecraft access token");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png) };
            }
            check(request.Headers.Authorization?.Parameter == "skin-access", "Skin API uses the selected account bearer token");
            if (request.Method == HttpMethod.Post)
            {
                uploads++;
                var multipart = request.Content as MultipartFormDataContent ?? throw new Exception("Expected skin multipart upload");
                var parts = multipart.ToArray();
                check(await parts[0].ReadAsStringAsync() == "slim" && parts[1].Headers.ContentType?.MediaType == "image/png"
                    && (await parts[1].ReadAsByteArrayAsync()).SequenceEqual(png), "Skin upload sends model and original PNG as multipart data");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"skins":[{"state":"ACTIVE","url":"https://textures.minecraft.net/texture/new","variant":"SLIM"}]}""") };
            }
            resets++; check(request.Method == HttpMethod.Delete && request.RequestUri.AbsolutePath.EndsWith("/skins/active"), "Reset skin uses the official active-skin endpoint");
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }));
        await auth.SetSkinAsync(account, identity, png, "slim", CancellationToken.None);
        check(uploads == 1 && account.SkinUrl.EndsWith("/new") && account.SkinVariant == "slim", "Successful upload updates the cached skin profile");
        check((await auth.DownloadSkinAsync(account.SkinUrl, CancellationToken.None)).SequenceEqual(png), "Download bounded official texture data");
        await auth.SetSkinAsync(account, identity, null, "classic", CancellationToken.None);
        check(resets == 1 && account.SkinPng is null && account.SkinUrl == "", "Successful 204 reset clears the cached skin");
        using var denied = new MicrosoftAuth(new SkinHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden))));
        account.SkinPng = png;
        bool failed = false;
        try { await denied.SetSkinAsync(account, identity, null, "classic", CancellationToken.None); }
        catch (InvalidOperationException) { failed = true; }
        check(failed && account.SkinPng == png, "Failed skin request preserves the previous skin");
        failed = false;
        try { await auth.SetSkinAsync(account, identity with { Uuid = Guid.NewGuid().ToString("N") }, png, "slim", CancellationToken.None); }
        catch (ArgumentException) { failed = true; }
        check(failed && uploads == 1, "Reject skin credentials from a different player before sending");
    }
    private sealed class SkinHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
