using BlockDock.Core;

namespace Island.Core;

public static class DownloadAvailability
{
    public const string CurseForgeNotice = "暂不支持 CurseForge 整合包与 Mod 在线下载：尚未申请 API 访问资格。";
    public static void RequireSupported(PackInfo? pack)
    {
        if (pack?.Format == PackFormat.CurseForge) throw new InvalidOperationException(CurseForgeNotice);
    }
    public static AppSettings Snapshot(AppSettings settings) => new()
    {
        GameDirectory = Path.GetFullPath(settings.GameDirectory), JavaPath = settings.JavaPath, Source = settings.Source,
        ParallelDownloads = settings.ParallelDownloads, IncludeOptionalMods = settings.IncludeOptionalMods,
        CurseForgeApiKey = ""
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
    private static void RequireModrinth(PackPlatform platform)
    { if (platform != PackPlatform.Modrinth) throw new InvalidOperationException(DownloadAvailability.CurseForgeNotice); }
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
        RequireModrinth(filter.Platform); settings = DownloadAvailability.Snapshot(settings);
        using var network = Network(settings); return await new ModpackCatalog(settings, network).SearchAsync(filter, page, token);
    }
    public async Task<CatalogVersionPage> GetReleasesAsync(AppSettings settings, CatalogProject project, PackSearchFilter filter, CancellationToken token)
    {
        RequireModrinth(project.Platform); settings = DownloadAvailability.Snapshot(settings);
        using var network = Network(settings); return await new ModpackCatalog(settings, network).GetVersionsAsync(project, filter with { Platform = PackPlatform.Modrinth }, 0, token);
    }
    public async Task<PackInfo> PrepareAsync(AppSettings settings, CatalogProject project, CatalogVersion release, CancellationToken token)
    {
        RequireModrinth(project.Platform); settings = DownloadAvailability.Snapshot(settings);
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
        DownloadAvailability.RequireSupported(pack); settings = DownloadAvailability.Snapshot(settings);
        using var network = Network(settings);
        return await Task.Run(() => new MinecraftInstaller(settings, network).InstallAsync(request, progress, token, pack), token);
    }
}
