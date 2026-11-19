using BlockDock.Core;

namespace Island.Core;

public static class DownloadAvailability
{
    public const string CurseForgeNotice = "CurseForge 下载服务暂不可用，请更新启动器后重试。";
    public static void RequireSupported(PackInfo? pack, AppSettings? settings = null)
    {
        if (pack is not null && !Enum.IsDefined(pack.Format)) throw new InvalidOperationException("不支持此整合包格式。");
        if (pack?.Format == PackFormat.CurseForge && settings is not null && string.IsNullOrWhiteSpace(settings.CurseForgeApiKey))
            throw new InvalidOperationException(CurseForgeNotice);
    }
    public static AppSettings Snapshot(AppSettings settings) => new()
    {
        GameDirectory = Path.GetFullPath(settings.GameDirectory), JavaPath = settings.JavaPath, Source = settings.Source,
        ParallelDownloads = settings.ParallelDownloads, IncludeOptionalMods = settings.IncludeOptionalMods,
        CurseForgeApiKey = settings.CurseForgeApiKey.Trim()
    };
}

public interface ILauncherDownloads
{
    Task<IReadOnlyList<GameVersion>> GetGamesAsync(AppSettings settings, CancellationToken token);
    Task<IReadOnlyList<LoaderVersion>> GetLoadersAsync(AppSettings settings, LoaderKind loader, string version, CancellationToken token);
    Task<CatalogSearchPage> SearchAsync(AppSettings settings, PackSearchFilter filter, int page, CancellationToken token);
    Task<CatalogVersionPage> GetReleasesAsync(AppSettings settings, CatalogProject project, PackSearchFilter filter, CancellationToken token);
    Task<PackInfo> PrepareAsync(AppSettings settings, CatalogProject project, CatalogVersion release, CancellationToken token);
    Task<PackInfo> InspectAsync(string file, CancellationToken token);
    Task<string> InstallAsync(AppSettings settings, InstallRequest request, PackInfo? pack, IProgress<InstallProgress> progress, CancellationToken token);
}

public sealed class LauncherDownloads(Func<AppSettings, DownloadClient>? networkFactory = null) : ILauncherDownloads
{
    private DownloadClient Network(AppSettings settings) => networkFactory?.Invoke(settings) ?? new(settings);
    public async Task<IReadOnlyList<GameVersion>> GetGamesAsync(AppSettings settings, CancellationToken token)
    {
        using var network = Network(settings); return await new MinecraftInstaller(settings, network).GetVersionsAsync(token);
    }
    public async Task<IReadOnlyList<LoaderVersion>> GetLoadersAsync(AppSettings settings, LoaderKind loader, string version, CancellationToken token)
    {
        using var network = Network(settings); return await new MinecraftInstaller(settings, network).GetLoaderVersionsAsync(loader, version, token);
    }
    public async Task<CatalogSearchPage> SearchAsync(AppSettings settings, PackSearchFilter filter, int page, CancellationToken token)
    {
        settings = DownloadAvailability.Snapshot(settings);
        using var network = Network(settings); return await new ModpackCatalog(settings, network).SearchAsync(filter, page, token);
    }
    public async Task<CatalogVersionPage> GetReleasesAsync(AppSettings settings, CatalogProject project, PackSearchFilter filter, CancellationToken token)
    {
        settings = DownloadAvailability.Snapshot(settings);
        using var network = Network(settings);
        var catalog = new ModpackCatalog(settings, network);
        var versions = new List<CatalogVersion>();
        int? offset = 0;
        while (offset is { } current)
        {
            var page = await catalog.GetVersionsAsync(project, filter with { Platform = project.Platform }, current, token);
            versions.AddRange(page.Versions); offset = page.NextOffset;
        }
        return new(versions.DistinctBy(version => version.Id).ToArray(), null);
    }
    public async Task<PackInfo> PrepareAsync(AppSettings settings, CatalogProject project, CatalogVersion release, CancellationToken token)
    {
        settings = DownloadAvailability.Snapshot(settings);
        using var network = Network(settings);
        var path = await new ModpackCatalog(settings, network).DownloadArchiveAsync(project, release, SafePaths.Under(settings.GameDirectory, ".blockdock/packdownloads"), token);
        var pack = await InspectAsync(path, token); pack.IconUrl = project.IconUrl; return pack;
    }
    public async Task<PackInfo> InspectAsync(string file, CancellationToken token)
    {
        var pack = await Task.Run(() => ModpackInstaller.InspectAsync(file, token), token); DownloadAvailability.RequireSupported(pack); return pack;
    }
    public async Task<string> InstallAsync(AppSettings settings, InstallRequest request, PackInfo? pack, IProgress<InstallProgress> progress, CancellationToken token)
    {
        DownloadAvailability.RequireSupported(pack, settings); settings = DownloadAvailability.Snapshot(settings);
        using var network = Network(settings);
        return await Task.Run(() => new MinecraftInstaller(settings, network).InstallAsync(request, progress, token, pack), token);
    }
}
