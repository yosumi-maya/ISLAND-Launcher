using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BlockDock.Core;

namespace Island.Core;

internal static class ForgeLaunchRepair
{
    internal static async Task VerifyLocalLibrariesAsync(string root, ResolvedVersion version, IEnumerable<TransferFile> libraries,
        LaunchFileVerifier verifier, DownloadClient network, string? javaPath, IProgress<InstallProgress>? progress, CancellationToken token)
    {
        var request = await ReadRequestAsync(root, version, token);
        var installer = request is null ? null : new ForgeInstaller(new AppSettings { GameDirectory = root, JavaPath = javaPath ?? "" }, network);
        var cached = installer is not null && request is not null && version.Profile.Array("islandGeneratedLibraries").Count == 0
            ? await installer.ReadCachedGeneratedLibrariesAsync(request, token)
            : [];
        var files = libraries.Concat(cached).ToArray();
        try { await verifier.EnsureManyAsync(files, "检查本地游戏依赖", progress, token); }
        catch (LaunchFileVerifier.InvalidLocalFile ex) when (installer is not null && request is not null)
        {
            var belongsToLoader = cached.Any(file => SamePath(file.Path, ex.FilePath)) ||
                LaunchLibraries.Files(root, version.Chain[^1]).Any(file => file.Urls.All(string.IsNullOrWhiteSpace) && SamePath(file.Path, ex.FilePath));
            if (!belongsToLoader) throw;
            progress?.Report(new("修复加载器生成文件", Path.GetFileName(ex.FilePath)));
            var work = SafePaths.Under(root, ".island/loader-repair/" + Guid.NewGuid().ToString("N"));
            var log = Path.Combine(work, "processors.log");
            var lockPath = SafePaths.Under(root, ".blockdock/install.lock");
            Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
            await using var installLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var restored = false;
            try
            {
                await installer.InstallAsync(request, version.Chain[0], work, progress, token);
                restored = true;
            }
            catch (Exception repairError) when (repairError is not OperationCanceledException && File.Exists(log))
            {
                throw new IOException($"自动修复加载器失败：{repairError.Message}。处理器日志：{log}", repairError);
            }
            finally
            {
                var temporary = !restored && !token.IsCancellationRequested && File.Exists(log) ? Path.Combine(work, "forge-data") : work;
                try { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
                catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException) { }
            }
            await verifier.EnsureManyAsync(files, "复查本地游戏依赖", progress, token);
        }

        static bool SamePath(string first, string second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<InstallRequest?> ReadRequestAsync(string root, ResolvedVersion version, CancellationToken token)
    {
        if (version.Chain.Count != 2 || version.ClientDownload is null) return null;
        var markerPath = SafePaths.Under(root, $"versions/{version.Id}/blockdock-instance.json");
        if (!File.Exists(markerPath)) return null;
        JsonObject? marker;
        try { marker = JsonNode.Parse(await File.ReadAllTextAsync(markerPath, token)) as JsonObject; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
        string MarkerValue(string name) => marker?[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";

        var vanilla = version.Chain[0];
        var minecraft = version.Chain[1].Str("inheritsFrom");
        var loaderVersion = MarkerValue("loaderVersion");
        if (minecraft.Length == 0 || MarkerValue("minecraft") != minecraft || vanilla.Str("id") != minecraft ||
            !string.Equals(version.ClientJar, SafePaths.Under(root, $"versions/{minecraft}/{minecraft}.jar"), StringComparison.OrdinalIgnoreCase) ||
            !Regex.IsMatch(loaderVersion, @"\A[0-9][0-9A-Za-z.+_-]{0,99}\z"))
            return null;
        var loader = MarkerValue("loader") switch
        {
            nameof(LoaderKind.Forge) => LoaderKind.Forge,
            nameof(LoaderKind.NeoForge) => LoaderKind.NeoForge,
            _ => LoaderKind.Vanilla
        };
        return loader == LoaderKind.Vanilla ? null : new(minecraft, version.Id, loader, loaderVersion);
    }
}
