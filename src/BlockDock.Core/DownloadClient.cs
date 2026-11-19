using System.Diagnostics;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace BlockDock.Core;

public sealed class CurseForgeRequestException(HttpStatusCode status) : IOException(
    status == HttpStatusCode.TooManyRequests ? "CurseForge 请求过于频繁，请稍后重试。"
    : $"CurseForge 返回 {(int)status}，下载服务或项目访问受限，请稍后重试。")
{
    public HttpStatusCode Status { get; } = status;
}

internal sealed record DownloadTiming(TimeSpan AlternateTimeout, TimeSpan SlowWindow, int MinimumBytesPerSecond)
{
    public static DownloadTiming Default { get; } = new(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(8), 32 * 1024);
}

public sealed class DownloadClient : IDisposable
{
    private static readonly SemaphoreSlim curseForgeGate = new(1, 1);
    private readonly HttpClient http;
    private readonly AppSettings settings;
    private readonly DownloadTiming timing;
    private readonly ConcurrentDictionary<string, long> cooldown = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, double> throughput = new(StringComparer.OrdinalIgnoreCase);
    private long bytes;
    private readonly Stopwatch watch = Stopwatch.StartNew();
    public long Bytes => Interlocked.Read(ref bytes);
    public double Speed => Bytes / Math.Max(1, watch.Elapsed.TotalSeconds);
    public DownloadClient(AppSettings settings, HttpMessageHandler? handler = null) : this(settings, handler, DownloadTiming.Default) { }
    internal DownloadClient(AppSettings settings, HttpMessageHandler? handler, DownloadTiming timing)
    {
        this.settings = settings; this.timing = timing;
        http = handler is null ? new(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All, AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(12), MaxConnectionsPerServer = Math.Clamp(settings.ParallelDownloads, 4, 32) }) : new(handler);
        http.Timeout = Timeout.InfiniteTimeSpan;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ISLANDLauncher/1.0.0");
    }

    private static bool CurseForgeHost(Uri uri) => uri.Scheme == "https" && uri.Port == 443
        && uri.UserInfo.Length == 0 && uri.Host is "api.curseforge.com" or "edge.forgecdn.net" or "mediafilez.forgecdn.net";

    private async Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken token, string? apiKey = null, long offset = 0)
    {
        var metadata = uri.Host == "api.curseforge.com";
        if (metadata) await curseForgeGate.WaitAsync(token);
        try
        {
            for (var redirects = 0; redirects <= 5; redirects++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
                // Explicitly discard credentials before contacting a mirror or redirected third party.
                var requestKey = CurseForgeHost(uri) ? apiKey ?? settings.CurseForgeApiKey : null;
                if (!string.IsNullOrWhiteSpace(requestKey)) request.Headers.Add("x-api-key", requestKey.Trim());
                var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                if (CurseForgeHost(uri) && response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    var status = response.StatusCode; response.Dispose(); throw new CurseForgeRequestException(status);
                }
                if (CurseForgeHost(uri) && response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var delay = response.Headers.RetryAfter?.Delta
                        ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromSeconds(2);
                    response.Dispose();
                    if (redirects > 0 || delay > TimeSpan.FromSeconds(30)) throw new CurseForgeRequestException(HttpStatusCode.TooManyRequests);
                    await Task.Delay(delay < TimeSpan.Zero ? TimeSpan.Zero : delay, token); continue;
                }
                if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod
                    or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect) || response.Headers.Location is null)
                    return response;
                var next = new Uri(uri, response.Headers.Location); response.Dispose();
                if (next.Scheme != "https" && !(next.IsLoopback && next.Scheme == "http"))
                    throw new InvalidDataException("下载重定向必须使用 HTTPS。");
                uri = next;
            }
            throw new IOException("下载重定向次数过多。");
        }
        finally { if (metadata) curseForgeGate.Release(); }
    }

    public IEnumerable<string> Candidates(string url)
    {
        var uri = new Uri(url);
        var mirror = uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length > 0 ? null : uri.Host switch
        {
            "piston-meta.mojang.com" or "launchermeta.mojang.com" or "piston-data.mojang.com" or "launcher.mojang.com" => "https://bmclapi2.bangbang93.com" + uri.PathAndQuery,
            "resources.download.minecraft.net" => "https://bmclapi2.bangbang93.com/assets" + uri.PathAndQuery,
            "libraries.minecraft.net" => "https://bmclapi2.bangbang93.com/maven" + uri.PathAndQuery,
            "maven.minecraftforge.net" or "maven.fabricmc.net" or "maven.neoforged.net" => "https://bmclapi2.bangbang93.com/maven" + uri.PathAndQuery,
            "meta.fabricmc.net" => "https://bmclapi2.bangbang93.com/fabric-meta" + uri.PathAndQuery,
            // API requests remain official; only downloadable files use MCIM.
            "cdn.modrinth.com" or "edge.forgecdn.net" or "mediafilez.forgecdn.net" => "https://mod.mcimirror.top" + uri.PathAndQuery,
            _ => null
        };
        if (settings.Source == DownloadSource.Mirror && mirror is not null) { yield return mirror; yield return url; }
        else { yield return url; if (settings.Source == DownloadSource.Auto && mirror is not null) yield return mirror; }
    }

    private string[] OrderCandidates(IEnumerable<string> urls)
    {
        var expanded = urls.SelectMany(Candidates).Distinct(StringComparer.Ordinal).ToArray();
        return expanded.OrderBy(url => cooldown.TryGetValue(new Uri(url).Host, out var until) && until > Environment.TickCount64 ? 1 : 0)
            .ThenBy(url => settings.Source == DownloadSource.Mirror && new Uri(url).Host is not ("mod.mcimirror.top" or "bmclapi2.bangbang93.com") ? 1 : 0)
            .ThenByDescending(url => settings.Source == DownloadSource.Auto ? throughput.GetValueOrDefault(new Uri(url).Host) : 0).ToArray();
    }
    private void Failed(Uri uri) => cooldown[uri.Host] = Environment.TickCount64 + 60_000;
    private void Succeeded(Uri uri, long transferred, TimeSpan elapsed)
    {
        cooldown.TryRemove(uri.Host, out _);
        var speed = transferred / Math.Max(.001, elapsed.TotalSeconds);
        throughput.AddOrUpdate(uri.Host, speed, (_, old) => old * .5 + speed * .5);
    }
    public async Task<string> GetTextAsync(string url, CancellationToken token, string? apiKey = null)
    {
        var errors = new List<string>();
        var candidates = OrderCandidates([url]);
        for (var attempt = 0; attempt < 2; attempt++)
        foreach (var candidate in candidates)
        {
            token.ThrowIfCancellationRequested();
            var uri = new Uri(candidate);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(candidates.Length > 1 && attempt == 0 ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(30));
                using var response = await SendAsync(uri, timeout.Token, CurseForgeHost(uri) ? apiKey : null);
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadAsStringAsync(timeout.Token);
                cooldown.TryRemove(uri.Host, out _);
                return result;
            }
            catch (Exception ex) when (ex is not CurseForgeRequestException && (ex is HttpRequestException or IOException or InvalidDataException or OperationCanceledException) && !token.IsCancellationRequested)
            { Failed(uri); errors.Add($"{uri.Host}: {ex.Message}"); }
        }
        throw new IOException("无法读取下载列表。" + string.Join(" / ", errors.Distinct()));
    }

    public async Task<JsonObject> GetJsonAsync(string url, CancellationToken token, string? apiKey = null)
        => JsonNode.Parse(await GetTextAsync(url, token, apiKey)) as JsonObject ?? throw new InvalidDataException("服务器返回了无效的 JSON 对象。");

    public async Task DownloadAsync(TransferFile file, CancellationToken token, Action<long>? onBytes = null)
    {
        if (await ValidAsync(file.Path, file, token)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(file.Path)!);
        var part = file.Path + ".part";
        var failures = new List<string>();
        var candidates = OrderCandidates(file.Urls);
        for (var attempt = 0; attempt < 3; attempt++)
        foreach (var url in candidates)
        {
            var uri = new Uri(url);
            if (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) throw new InvalidDataException("只允许 HTTPS 下载链接。");
            token.ThrowIfCancellationRequested();
            try
            {
                // Cross-source resume requires a final cryptographic identity check.
                if (candidates.Length > 1 && file.Sha1 is null && file.Sha512 is null && File.Exists(part)) File.Delete(part);
                var offset = File.Exists(part) ? new FileInfo(part).Length : 0;
                if (file.Size.HasValue && offset >= file.Size) { File.Delete(part); offset = 0; }
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                var alternate = candidates.Length > 1 && attempt < 2;
                timeout.CancelAfter(alternate ? timing.AlternateTimeout : TimeSpan.FromSeconds(35));
                var transferWatch = Stopwatch.StartNew();
                using var response = await SendAsync(uri, timeout.Token, offset: offset);
                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable) { File.Delete(part); continue; }
                response.EnsureSuccessStatusCode();
                bool append = offset > 0 && response.StatusCode == HttpStatusCode.PartialContent && response.Content.Headers.ContentRange?.From == offset;
                if (response.StatusCode == HttpStatusCode.PartialContent && !append) { File.Delete(part); continue; }
                await using (var output = new FileStream(part, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
                {
                    var buffer = new byte[81920];
                    long received = 0, windowBytes = 0;
                    var windowStart = transferWatch.Elapsed;
                    while (true)
                    {
                        timeout.CancelAfter(alternate ? timing.AlternateTimeout : TimeSpan.FromSeconds(35));
                        int read = await input.ReadAsync(buffer, timeout.Token);
                        if (read == 0) break;
                        await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                        Interlocked.Add(ref bytes, read);
                        onBytes?.Invoke(read);
                        token.ThrowIfCancellationRequested();
                        received += read;
                        var window = transferWatch.Elapsed - windowStart;
                        if (alternate && window >= timing.SlowWindow)
                        {
                            if ((received - windowBytes) / window.TotalSeconds < timing.MinimumBytesPerSecond)
                                throw new IOException("下载源持续低速，正在切换备用地址。");
                            windowBytes = received; windowStart = transferWatch.Elapsed;
                        }
                    }
                }
                if (!await ValidAsync(part, file, token)) { File.Delete(part); throw new InvalidDataException("文件大小或哈希校验失败。"); }
                File.Move(part, file.Path, true);
                Succeeded(uri, new FileInfo(file.Path).Length - offset, transferWatch.Elapsed);
                return;
            }
            catch (Exception ex) when ((ex is HttpRequestException or IOException or InvalidDataException or OperationCanceledException) && !token.IsCancellationRequested)
            {
                Failed(uri); failures.Add($"{uri.Host}: {ex.Message}");
                if (candidates.Length == 1 && attempt < 2) await Task.Delay(300 * (attempt + 1), token);
            }
        }
        throw new IOException($"下载失败：{Path.GetFileName(file.Path)}\n" + string.Join(" / ", failures.Distinct()));
    }

    public static async Task<bool> ValidAsync(string path, TransferFile file, CancellationToken token)
    {
        if (!File.Exists(path)) return false;
        if (file.Size.HasValue && new FileInfo(path).Length != file.Size.Value) return false;
        if (file.Sha512 is not null)
        {
            await using var stream = File.OpenRead(path);
            if (!Convert.ToHexString(await SHA512.HashDataAsync(stream, token)).Equals(file.Sha512, StringComparison.OrdinalIgnoreCase)) return false;
        }
        if (file.Sha1 is not null)
        {
            await using var stream = File.OpenRead(path);
            if (!Convert.ToHexString(await SHA1.HashDataAsync(stream, token)).Equals(file.Sha1, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return new FileInfo(path).Length > 0 || file.Size == 0;
    }

    public async Task DownloadManyAsync(IEnumerable<TransferFile> files, string stage, IProgress<InstallProgress>? progress, CancellationToken token)
    {
        var list = files.GroupBy(f => Path.GetFullPath(f.Path), StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        int done = 0;
        using var failureCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        ExceptionDispatchInfo? failure = null;
        try { await Parallel.ForEachAsync(list, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(settings.ParallelDownloads, 1, 32), CancellationToken = failureCancellation.Token }, async (file, ct) =>
        {
            long last = 0;
            try
            {
                await DownloadAsync(file, ct, _ =>
                {
                    var now = Environment.TickCount64;
                    if (now - Interlocked.Read(ref last) > 160) { Interlocked.Exchange(ref last, now); progress?.Report(new(stage, Path.GetFileName(file.Path), Volatile.Read(ref done), list.Count, Bytes, Speed)); }
                });
                progress?.Report(new(stage, Path.GetFileName(file.Path), Interlocked.Increment(ref done), list.Count, Bytes, Speed));
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested && ex is not OperationCanceledException)
                    Interlocked.CompareExchange(ref failure, ExceptionDispatchInfo.Capture(ex), null);
                await failureCancellation.CancelAsync(); throw;
            }
        }); }
        catch when (!token.IsCancellationRequested && failure is not null) { failure.Throw(); throw; }
    }
    public void Dispose() => http.Dispose();
}
