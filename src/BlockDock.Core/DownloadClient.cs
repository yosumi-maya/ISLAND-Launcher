using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace BlockDock.Core;

public sealed class DownloadClient : IDisposable
{
    private readonly HttpClient http;
    private readonly AppSettings settings;
    private long bytes;
    private readonly Stopwatch watch = Stopwatch.StartNew();
    public long Bytes => Interlocked.Read(ref bytes);
    public double Speed => Bytes / Math.Max(1, watch.Elapsed.TotalSeconds);
    public DownloadClient(AppSettings settings, HttpMessageHandler? handler = null)
    {
        this.settings = settings;
        http = handler is null ? new(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All, ConnectTimeout = TimeSpan.FromSeconds(12), MaxConnectionsPerServer = 16 }) : new(handler);
        http.Timeout = Timeout.InfiniteTimeSpan;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("BlockDock/1.1 (Minecraft Java downloader)");
    }

    public IEnumerable<string> Candidates(string url)
    {
        var uri = new Uri(url);
        var mirror = uri.Host switch
        {
            "piston-meta.mojang.com" or "launchermeta.mojang.com" or "piston-data.mojang.com" or "launcher.mojang.com" => "https://bmclapi2.bangbang93.com" + uri.PathAndQuery,
            "resources.download.minecraft.net" => "https://bmclapi2.bangbang93.com/assets" + uri.PathAndQuery,
            "libraries.minecraft.net" => "https://bmclapi2.bangbang93.com/maven" + uri.PathAndQuery,
            "maven.minecraftforge.net" => "https://bmclapi2.bangbang93.com/maven" + uri.PathAndQuery,
            _ => null
        };
        if (settings.Source == DownloadSource.Mirror && mirror is not null) { yield return mirror; yield return url; }
        else { yield return url; if (settings.Source == DownloadSource.Auto && mirror is not null) yield return mirror; }
    }

    public async Task<string> GetTextAsync(string url, CancellationToken token, string? apiKey = null)
    {
        var errors = new List<string>();
        foreach (var candidate in Candidates(url))
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(30));
                    using var request = new HttpRequestMessage(HttpMethod.Get, candidate);
                    if (!string.IsNullOrEmpty(apiKey)) request.Headers.Add("x-api-key", apiKey);
                    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync(timeout.Token);
                }
                catch (Exception ex) when ((ex is HttpRequestException or IOException or InvalidDataException or OperationCanceledException) && !token.IsCancellationRequested)
                { errors.Add($"{new Uri(candidate).Host}: {ex.Message}"); }
            }
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
        foreach (var url in file.Urls.SelectMany(Candidates).Distinct())
        {
            var uri = new Uri(url);
            if (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) throw new InvalidDataException("只允许 HTTPS 下载链接。");
            for (var attempt = 0; attempt < 3; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var offset = File.Exists(part) ? new FileInfo(part).Length : 0;
                    if (file.Size.HasValue && offset >= file.Size) { File.Delete(part); offset = 0; }
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(35));
                    using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                    if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
                    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                    if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable) { File.Delete(part); continue; }
                    response.EnsureSuccessStatusCode();
                    bool append = offset > 0 && response.StatusCode == HttpStatusCode.PartialContent && response.Content.Headers.ContentRange?.From == offset;
                    if (response.StatusCode == HttpStatusCode.PartialContent && !append) { File.Delete(part); continue; }
                    await using (var output = new FileStream(part, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
                    {
                        var buffer = new byte[81920];
                        while (true)
                        {
                            timeout.CancelAfter(TimeSpan.FromSeconds(35));
                            int read = await input.ReadAsync(buffer, timeout.Token);
                            if (read == 0) break;
                            await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                            Interlocked.Add(ref bytes, read);
                            onBytes?.Invoke(read);
                        }
                    }
                    if (!await ValidAsync(part, file, token)) { File.Delete(part); throw new InvalidDataException("文件大小或哈希校验失败。"); }
                    File.Move(part, file.Path, true);
                    return;
                }
                catch (Exception ex) when ((ex is HttpRequestException or IOException or InvalidDataException or OperationCanceledException) && !token.IsCancellationRequested)
                {
                    failures.Add($"{uri.Host}: {ex.Message}");
                    if (attempt < 2) await Task.Delay(300 * (attempt + 1), token);
                }
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
        await Parallel.ForEachAsync(list, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(settings.ParallelDownloads, 1, 32), CancellationToken = failureCancellation.Token }, async (file, ct) =>
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
            catch { await failureCancellation.CancelAsync(); throw; }
        });
    }
    public void Dispose() => http.Dispose();
}
