using BlockDock.Core;

namespace Island.Core;

public enum VersionIsolation { Auto, Enabled, Disabled }

public sealed record GameDirectoryResolution(string Directory, bool Isolated, string Reason);

public static class GameDirectories
{
    public static GameDirectoryResolution Resolve(string root, string id, VersionIsolation isolation = VersionIsolation.Auto)
    {
        root = GameFolder.NormalizePath(root);
        var version = SafePaths.Under(root, "versions/" + SafePaths.InstanceName(id));
        if (isolation == VersionIsolation.Enabled) return new(version, true, "已开启版本隔离");
        if (isolation == VersionIsolation.Disabled) return new(root, false, "已关闭版本隔离，共享根目录");
        // Installer-created instances are isolated even before their first launch creates game data.
        if (File.Exists(Path.Combine(version, "blockdock-instance.json"))) return new(version, true, "自动检测：此版本由下载器或启动器独立安装");
        if (HasGameData(version)) return new(version, true, "自动检测：版本目录中有游戏数据");
        if (HasGameData(root)) return new(root, false, "自动检测：根目录中有共享游戏数据");
        return new(version, true, "自动检测：暂无游戏数据，使用独立版本目录");
    }

    private static bool HasGameData(string directory)
    {
        foreach (var name in new[] { "options.txt", "optionsof.txt", "servers.dat" })
            if (File.Exists(Path.Combine(directory, name))) return true;
        foreach (var name in new[] { "mods", "saves", "config", "defaultconfigs", "resourcepacks", "shaderpacks", "scripts", "kubejs" })
        {
            var path = Path.Combine(directory, name);
            try { if (System.IO.Directory.Exists(path) && System.IO.Directory.EnumerateFileSystemEntries(path).Any()) return true; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return false;
    }
}
