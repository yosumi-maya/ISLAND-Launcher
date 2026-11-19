using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using BlockDock.Core;

internal static class DownloadAccelerationTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    private sealed class Trickle(byte[] payload) : MemoryStream(payload)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        { await Task.Delay(25, token); return await base.ReadAsync(buffer[..Math.Min(buffer.Length, 256)], token); }
    }
    public static async Task RunAsync(string root, Action<bool, string> check)
    {
        var directory = Path.Combine(root, "acceleration"); Directory.CreateDirectory(directory);
        var payload = Encoding.UTF8.GetBytes(new string('x', 8192));
        var sha = Convert.ToHexString(SHA1.HashData(payload));
        var officialUrl = "https://edge.forgecdn.net/files/4029/209/mod.jar";
        var mirrorUrl = "https://mod.mcimirror.top/files/4029/209/mod.jar";
        TransferFile FileFor(string name) => new(Path.Combine(directory, name), [officialUrl], payload.Length, sha);
        static Task<HttpResponseMessage> Ok(byte[] data) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) });
        using (var auto = new DownloadClient(new()))
        using (var mirror = new DownloadClient(new() { Source = DownloadSource.Mirror }))
        using (var official = new DownloadClient(new() { Source = DownloadSource.Official }))
        {
            check(auto.Candidates(officialUrl).SequenceEqual(new[] { officialUrl, mirrorUrl })
                && mirror.Candidates(officialUrl).SequenceEqual(new[] { mirrorUrl, officialUrl })
                && official.Candidates(officialUrl).SequenceEqual(new[] { officialUrl }), "Auto, mirror and official choices produce the intended mod download source order");
            check(auto.Candidates("https://api.curseforge.com/v1/mods/1").Count() == 1
                && mirror.Candidates("https://api.modrinth.com/v2/search").Count() == 1, "Mod metadata APIs always remain official, including mirror mode");
            check(auto.Candidates("https://edge.forgecdn.net.evil.invalid/files/a").Count() == 1
                && auto.Candidates("https://edge.forgecdn.net:8443/files/a").Count() == 1, "Mirror rewriting matches exact HTTPS origins only");
            check(mirror.Candidates("https://meta.fabricmc.net/v2/versions/loader").First() == "https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader", "Fabric metadata can use the HMCL-compatible BMCLAPI route");
        }
        var seen = new List<string>(); var credentialsSafe = true;
        using (var client = new DownloadClient(new() { CurseForgeApiKey = "fixture-key" }, new Handler((request, token) =>
        {
            var host = request.RequestUri!.Host; seen.Add(host);
            if (host == "edge.forgecdn.net")
            {
                credentialsSafe &= request.Headers.Contains("x-api-key");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }
            credentialsSafe &= !request.Headers.Contains("x-api-key"); return Ok(payload);
        })))
        {
            await client.DownloadAsync(FileFor("fallback.jar"), default);
            check(seen.SequenceEqual(new[] { "edge.forgecdn.net", "mod.mcimirror.top" }), "A failed source switches to its mirror immediately instead of retrying three times");
            seen.Clear(); await client.DownloadAsync(FileFor("learned.jar"), default);
            check(seen.SequenceEqual(new[] { "mod.mcimirror.top" }), "Auto mode remembers a working source and cools down the failing host for subsequent files");
            check(credentialsSafe, "CurseForge requests carry credentials and third-party mirrors never receive the API key");
        }
        seen.Clear();
        using (var client = new DownloadClient(new() { Source = DownloadSource.Mirror }, new Handler((request, token) =>
        {
            seen.Add(request.RequestUri!.Host);
            return request.RequestUri.Host == "mod.mcimirror.top" ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)) : Ok(payload);
        })))
        {
            await client.DownloadAsync(FileFor("official-fallback.jar"), default);
            check(seen.SequenceEqual(new[] { "mod.mcimirror.top", "edge.forgecdn.net" }), "An unavailable mirror falls back to the official download without changing the selected mode");
        }
        var timing = new DownloadTiming(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20), 32 * 1024);
        seen.Clear(); long? resumed = null;
        using (var client = new DownloadClient(new(), new Handler((request, token) =>
        {
            seen.Add(request.RequestUri!.Host);
            if (request.RequestUri.Host == "edge.forgecdn.net") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new Trickle(payload)) });
            resumed = request.Headers.Range?.Ranges.Single().From;
            var response = new HttpResponseMessage(resumed > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = new ByteArrayContent(payload[(int)(resumed ?? 0)..]) };
            if (resumed > 0) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(resumed.Value, payload.Length - 1, payload.Length);
            return Task.FromResult(response);
        }), timing))
        {
            var file = FileFor("slow.jar"); await client.DownloadAsync(file, default);
            check(seen.SequenceEqual(new[] { "edge.forgecdn.net", "mod.mcimirror.top" }) && resumed > 0
                && System.IO.File.ReadAllBytes(file.Path).SequenceEqual(payload), "A trickling source switches early, resumes the verified partial file and passes the final hash");
        }
        seen.Clear();
        using (var client = new DownloadClient(new(), new Handler(async (request, token) =>
        {
            seen.Add(request.RequestUri!.Host);
            if (request.RequestUri.Host == "edge.forgecdn.net") await Task.Delay(Timeout.Infinite, token);
            return await Ok(payload);
        }), timing))
        {
            await client.DownloadAsync(FileFor("stalled.jar"), default);
            check(seen.SequenceEqual(new[] { "edge.forgecdn.net", "mod.mcimirror.top" }), "A stalled source times out promptly before trying the alternative");
        }
        seen.Clear();
        using (var client = new DownloadClient(new(), new Handler((request, token) =>
        {
            seen.Add(request.RequestUri!.Host);
            return request.RequestUri.Host == "edge.forgecdn.net" ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new Trickle(payload)) }) : Ok(payload);
        }), timing))
        using (var canceled = new CancellationTokenSource())
        {
            var canceledCorrectly = false; var file = FileFor("cancel.jar");
            try { await client.DownloadAsync(file, canceled.Token, _ => canceled.Cancel()); } catch (OperationCanceledException) { canceledCorrectly = true; }
            check(canceledCorrectly && seen.SequenceEqual(new[] { "edge.forgecdn.net" }) && !System.IO.File.Exists(file.Path), "User cancellation stops downloading and never starts a fallback request");
        }
        using (var client = new DownloadClient(new() { Source = DownloadSource.Official }, new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)))))
        {
            var meaningful = false;
            try { await client.DownloadManyAsync([FileFor("failed.jar")], "fixture", null, default); }
            catch (IOException ex) { meaningful = ex.Message.Contains("下载失败") && ex.Message.Contains("404"); }
            check(meaningful, "Parallel download failure preserves its original error instead of reporting canceled");
        }
    }
}
