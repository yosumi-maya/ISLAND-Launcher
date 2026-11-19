using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using BlockDock.Core;

namespace Island.Core;

public enum JavaVendor { Temurin, Zulu }
public enum JavaDownloadSource { Auto, Tsinghua, Nanjing, CstCloud, Official }

public sealed class MissingJavaException(JavaRequirement requirement) : InvalidOperationException(
    $"未找到可用的 64 位 Java {requirement.MinimumMajor} 或更高版本。可在启动器设置中下载 Java，或手动选择 java.exe。");

public sealed class JavaDownloads(string directory)
{
    private const long MaxArchive = 512L * 1024 * 1024;
    private const long MaxExtracted = 1024L * 1024 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(18);
    private static readonly (JavaDownloadSource Source, string Url)[] Mirrors =
    [
        (JavaDownloadSource.Tsinghua, "https://mirrors.tuna.tsinghua.edu.cn/Adoptium/"),
        (JavaDownloadSource.Nanjing, "https://mirror.nju.edu.cn/adoptium/"),
        (JavaDownloadSource.CstCloud, "https://mirrors.cstcloud.cn/Adoptium/")
    ];

    private sealed record Package(string Name, string Url, string Sha256, long Size, string Kind);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string root = Path.Combine(Path.GetFullPath(directory), "java");

    internal static IEnumerable<string> InstalledPaths(string directory)
    {
        var root = Path.Combine(directory, "java");
        if (!Directory.Exists(root) || IsLink(root)) yield break;
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            if (IsLink(folder)) continue;
            var bin = Path.Combine(folder, "bin");
            var java = Path.Combine(bin, "java.exe");
            if (Directory.Exists(bin) && !IsLink(bin) && File.Exists(java) && !IsLink(java)) yield return java;
        }
    }

    public async Task<JavaInstallation> InstallAsync(int major, JavaVendor vendor, JavaDownloadSource source,
        IProgress<InstallProgress>? progress, CancellationToken token)
    {
        if (major is < 8 or > 100 || !Enum.IsDefined(vendor) || !Enum.IsDefined(source))
            throw new ArgumentException("请选择有效的 Java 版本、供应商和下载源。");
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => InstallCoreAsync(major, vendor, source, progress, token), token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    private async Task<JavaInstallation> InstallCoreAsync(int major, JavaVendor vendor, JavaDownloadSource source,
        IProgress<InstallProgress>? progress, CancellationToken token)
    {
        var label = $"Java {major} · {vendor}";
        var prefix = $"{vendor.ToString().ToLowerInvariant()}-{major}-";
        foreach (var path in InstalledPaths(directory).Where(path =>
            Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path)))!.StartsWith(prefix, StringComparison.Ordinal)))
        {
            if (await JavaDiscovery.ProbeAsync(path, token) is { Architecture: "x64" } existing && existing.Major == major)
                return existing;
        }

        Directory.CreateDirectory(root);
        if (IsLink(root)) throw new IOException("Java 安装目录是链接，请检查启动器数据目录。");
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan, MaxResponseContentBufferSize = 2 * 1024 * 1024 };
        progress?.Report(new("正在查询 Java 下载", label));
        Package package;
        try
        {
            package = await ResolvePackageAsync(http, major, vendor, token);
        }
        catch (Exception ex) when (!token.IsCancellationRequested && ex is HttpRequestException or OperationCanceledException or JsonException
            or InvalidOperationException or KeyNotFoundException)
        {
            throw new IOException($"无法查询 {label} 的官方发行信息。请检查网络，或在设置中选择另一供应商。", ex);
        }

        var work = Path.Combine(Path.GetDirectoryName(root)!, ".java-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var archive = Path.Combine(work, "runtime.zip");
            var failures = new List<string>();
            var downloaded = false;
            foreach (var url in DownloadUrls(major, vendor, source, package))
            {
                token.ThrowIfCancellationRequested();
                var host = new Uri(url).Host;
                try
                {
                    await DownloadAsync(http, url, archive, package, label, progress, token);
                    downloaded = true;
                    break;
                }
                catch (Exception ex) when (!token.IsCancellationRequested && ex is HttpRequestException or IOException or InvalidDataException or OperationCanceledException)
                {
                    failures.Add(host + "（" + (ex is OperationCanceledException ? "连接或读取超时" : ex.Message) + "）");
                }
            }
            token.ThrowIfCancellationRequested();
            if (!downloaded) throw new IOException($"{label} 下载失败。可在设置中切换来源后重试。\n" + string.Join("\n", failures));

            progress?.Report(new("正在解压 Java", label));
            var expanded = Path.Combine(work, "expanded");
            Directory.CreateDirectory(expanded);
            await ExtractAsync(archive, expanded, token);
            var homes = Directory.EnumerateDirectories(expanded).Prepend(expanded)
                .Where(home => File.Exists(Path.Combine(home, "bin", "java.exe"))).ToArray();
            if (homes.Length != 1) throw new InvalidDataException("Java 压缩包中没有唯一可用的运行环境。");
            var java = await JavaDiscovery.ProbeAsync(Path.Combine(homes[0], "bin", "java.exe"), token);
            if (java is not { Architecture: "x64" } || java.Major != major)
                throw new InvalidDataException($"下载的 Java 无法运行，或与所选 Java {major} x64 不匹配。");
            token.ThrowIfCancellationRequested();
            var name = prefix + package.Sha256[..16].ToLowerInvariant();
            var target = SafePaths.Under(root, name);
            if (Directory.Exists(target))
            {
                var installed = SafePaths.Under(root, name + "/bin/java.exe");
                if (await JavaDiscovery.ProbeAsync(installed, token) is { Architecture: "x64" } concurrent && concurrent.Major == major)
                    return concurrent;
                target = SafePaths.Under(root, name + "-" + Guid.NewGuid().ToString("N"));
            }
            token.ThrowIfCancellationRequested();
            try { Directory.Move(homes[0], target); }
            catch (IOException) when (Directory.Exists(target))
            {
                var installed = SafePaths.Under(root, Path.GetFileName(target) + "/bin/java.exe");
                if (await JavaDiscovery.ProbeAsync(installed, token) is { Architecture: "x64" } concurrent && concurrent.Major == major)
                    return concurrent;
                throw;
            }
            return java with { Path = Path.Combine(target, "bin", "java.exe") };
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static async Task<Package> ResolvePackageAsync(HttpClient http, int major, JavaVendor vendor, CancellationToken token)
    {
        async Task<JsonElement> Get(string url)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(RequestTimeout);
            return await http.GetFromJsonAsync<JsonElement>(url, deadline.Token);
        }

        foreach (var kind in new[] { "jre", "jdk" })
        {
            if (vendor == JavaVendor.Temurin)
            {
                JsonElement list;
                try { list = await Get($"https://api.adoptium.net/v3/assets/latest/{major}/hotspot?architecture=x64&image_type={kind}&os=windows&vendor=eclipse"); }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { continue; }
                foreach (var entry in list.EnumerateArray())
                {
                    var binary = entry.GetProperty("binary");
                    if (binary.GetProperty("architecture").GetString() != "x64" || binary.GetProperty("os").GetString() != "windows"
                        || binary.GetProperty("image_type").GetString() != kind || entry.GetProperty("version").GetProperty("major").GetInt32() != major) continue;
                    var file = binary.GetProperty("package");
                    return ReadPackage(file, "link", "checksum", "github.com", kind);
                }
            }
            else
            {
                var list = await Get($"https://api.azul.com/metadata/v1/zulu/packages/?java_version={major}&os=windows&arch=x86&hw_bitness=64&archive_type=zip&java_package_type={kind}&release_status=ga&availability_types=CA&javafx_bundled=false&latest=true");
                var candidate = list.EnumerateArray().FirstOrDefault(entry =>
                    entry.GetProperty("name").GetString()?.EndsWith("-win_x64.zip", StringComparison.Ordinal) == true);
                if (candidate.ValueKind == JsonValueKind.Undefined) continue;
                var id = candidate.GetProperty("package_uuid").GetGuid();
                var file = await Get($"https://api.azul.com/metadata/v1/zulu/packages/{id:D}");
                if (file.GetProperty("hw_bitness").GetInt32() != 64 || file.GetProperty("java_version")[0].GetInt32() != major
                    || file.GetProperty("os").GetString() != "windows" || file.GetProperty("java_package_type").GetString() != kind) continue;
                return ReadPackage(file, "download_url", "sha256_hash", "cdn.azul.com", kind);
            }
        }
        throw new IOException($"供应商未提供 Java {major} 的 Windows x64 便携包。可在设置中切换供应商或手动指定 Java。");
    }

    private static Package ReadPackage(JsonElement file, string urlProperty, string hashProperty, string host, string kind)
    {
        var name = file.GetProperty("name").GetString() ?? "";
        var hash = file.GetProperty(hashProperty).GetString() ?? "";
        var size = file.GetProperty("size").GetInt64();
        var link = file.GetProperty(urlProperty).GetString();
        if (name.Length > 160 || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
            || hash.Length != 64 || !hash.All(Uri.IsHexDigit) || size is <= 0 or > MaxArchive
            || !Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || uri.Host != host || uri.UserInfo.Length != 0 || Uri.UnescapeDataString(uri.Segments[^1]) != name)
            throw new InvalidDataException("Java 供应商返回了无效的压缩包信息或校验值。");
        return new(name, uri.AbsoluteUri, hash, size, kind);
    }

    private static IEnumerable<string> DownloadUrls(int major, JavaVendor vendor, JavaDownloadSource source, Package package)
    {
        if (vendor == JavaVendor.Temurin && source != JavaDownloadSource.Official)
            foreach (var mirror in Mirrors.OrderBy(mirror => mirror.Source == source ? 0 : 1))
                yield return $"{mirror.Url}{major}/{package.Kind}/x64/windows/{package.Name}";
        yield return package.Url;
    }

    private static async Task DownloadAsync(HttpClient http, string url, string destination, Package package,
        string label, IProgress<InstallProgress>? progress, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(RequestTimeout);
        var host = new Uri(url).Host;
        progress?.Report(new("正在连接 Java 下载源", label + " · " + host));
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("下载源重定向到非 HTTPS 地址。");
        var length = response.Content.Headers.ContentLength ?? package.Size;
        if (length is <= 0 or > MaxArchive) throw new InvalidDataException("Java 压缩包大小超出下载限制。");
        await using var input = await response.Content.ReadAsStreamAsync(deadline.Token);
        await using var output = File.Create(destination);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        long received = 0;
        var lastPercent = -1;
        while (true)
        {
            deadline.CancelAfter(RequestTimeout);
            var count = await input.ReadAsync(buffer, deadline.Token);
            if (count == 0) break;
            received += count;
            if (received > MaxArchive) throw new InvalidDataException("Java 压缩包大小超出下载限制。");
            hash.AppendData(buffer, 0, count);
            await output.WriteAsync(buffer.AsMemory(0, count), token);
            var percent = (int)Math.Min(99, received * 100 / length);
            if (percent != lastPercent)
            {
                progress?.Report(new("正在下载 Java", label + $" · {host} · {received / 1048576d:0.0} / {length / 1048576d:0.0} MB", percent, 100));
                lastPercent = percent;
            }
        }
        // Azul's API size can differ from the CDN object even when the published digest matches.
        if (!string.Equals(Convert.ToHexString(hash.GetHashAndReset()), package.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("压缩包未通过官方 SHA-256 校验。");
    }

    private static async Task ExtractAsync(string archive, string directory, CancellationToken token)
    {
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count > 50_000 || zip.Entries.Sum(entry => entry.Length) > MaxExtracted)
            throw new InvalidDataException("Java 压缩包超过解压限制。");
        foreach (var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            if ((entry.ExternalAttributes >> 16 & 0xf000) == 0xa000
                || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Java 压缩包包含链接。");
            var relative = entry.FullName.TrimEnd('/', '\\');
            var destination = SafePaths.Under(directory, relative);
            if (entry.Name.Length == 0) { Directory.CreateDirectory(destination); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var input = entry.Open();
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await input.CopyToAsync(output, token);
        }
    }
}
