using System.Text.Json.Nodes;

namespace BlockDock.Core;

public enum DownloadSource { Auto, Official, Mirror }
public enum LoaderKind { Vanilla, Forge, Fabric, NeoForge }

public sealed class AppSettings
{
    public string GameDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, ".minecraft");
    public DownloadSource Source { get; set; } = DownloadSource.Auto;
    public int ParallelDownloads { get; set; } = 8;
    public string JavaPath { get; set; } = "";
    public string CurseForgeApiKey { get; set; } = "";
    public bool IncludeOptionalMods { get; set; } = true;
}

public sealed record GameVersion(string Id, string Type, string Url, string? Sha1, DateTimeOffset Released)
{
    public string Display => Id;
    public string Detail => $"{(Type == "release" ? "正式版" : Type == "snapshot" ? "快照版" : "历史版")} · {Released:yyyy/MM/dd}";
}
public sealed record LoaderVersion(string Version, string MavenVersion, bool Stable = true)
{
    public string Display => Version + (Stable ? "" : " · 测试版");
}
public sealed record InstallRequest(string MinecraftVersion, string InstanceName, LoaderKind Loader = LoaderKind.Vanilla, string? LoaderVersion = null);
public sealed record TransferFile(string Path, IReadOnlyList<string> Urls, long? Size = null, string? Sha1 = null, string? Sha512 = null);
public sealed record InstallProgress(string Stage, string Detail, int Completed = 0, int Total = 0, long Bytes = 0, double BytesPerSecond = 0)
{
    public double Percent => Total > 0 ? Math.Clamp(100d * Completed / Total, 0, 100) : 0;
}
public sealed record InstalledInstance(string Name, string Minecraft, string Loader, string Directory, DateTime Created)
{
    public string? IconUrl { get; init; }
    public IReadOnlyList<string> LocalIconPaths => new[] { "icon.png", "instance.png", "PCL/Logo.png", "icon.jpg", "icon.jpeg", "icon.bmp", "icon.gif", "icon.apng" }
        .Select(name => Path.Combine(Directory, name)).Where(File.Exists).ToArray();
    public string PackName { get; init; } = "";
    public LoaderKind IconLoader => Enum.TryParse<LoaderKind>(Loader, true, out var loader) && Enum.IsDefined(loader) ? loader : LoaderKind.Vanilla;
    public string LoaderLabel => Loader == "Vanilla" ? "原版" : Loader;
    public string Summary => $"Minecraft {Minecraft}  ·  {LoaderLabel}" + (PackName.Length > 0 && PackName != Name ? "  ·  " + PackName : "");
}
public enum PackFormat { Modrinth, CurseForge, MultiMC, Raw }
public sealed class PackInfo
{
    public required string ArchivePath { get; init; }
    public required string Name { get; init; }
    public required string MinecraftVersion { get; init; }
    public required PackFormat Format { get; init; }
    public LoaderKind Loader { get; init; }
    public string? LoaderVersion { get; init; }
    public string? IconUrl { get; set; }
    public string Prefix { get; init; } = "";
    public string Overrides { get; init; } = "overrides";
    public int FileCount { get; init; }
    public int OptionalCount { get; init; }
    public required JsonObject Manifest { get; init; }
    public string FormatLabel => Format switch { PackFormat.Modrinth => "Modrinth", PackFormat.CurseForge => "CurseForge", PackFormat.MultiMC => "MultiMC / HMCL", _ => "原生游戏目录" };
}

public static class JsonExt
{
    public static string Str(this JsonNode? node, string key, string fallback = "") => node?[key]?.GetValue<string>() ?? fallback;
    public static long? Long(this JsonNode? node, string key) => node?[key]?.GetValue<long>();
    public static JsonArray Array(this JsonNode? node, string key) => node?[key] as JsonArray ?? [];
    public static JsonObject Object(this JsonNode? node, string key) => node?[key] as JsonObject ?? new();
}
