using BlockDock.Core;

namespace Island.Core;

public sealed record ModFile(string Name, string Path, bool Enabled, long Bytes)
{
    public string SizeLabel => Bytes >= 1048576 ? $"{Bytes / 1048576d:0.0} MB" : $"{Math.Max(1, Bytes / 1024):N0} KB";
    public string Status => Enabled ? "已启用" : "已停用";
}

public static class ModFiles
{
    public static IReadOnlyList<ModFile> List(string instance)
    {
        var folder = SafePaths.Under(instance, "mods");
        if (!Directory.Exists(folder)) return [];
        return Directory.EnumerateFiles(folder).Where(p => p.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase))
            .Select(p => new ModFile(Path.GetFileName(p).Replace(".disabled", ""), p, !p.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase), new FileInfo(p).Length))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
    public static void Toggle(string instance, ModFile mod)
    {
        var folder = SafePaths.Under(instance, "mods");
        var path = SafePaths.Under(folder, Path.GetFileName(mod.Path));
        if (!string.Equals(Path.GetFullPath(mod.Path), path, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("模组路径超出实例目录。");
        var target = mod.Enabled ? path + ".disabled" : path[..^9];
        File.Move(path, target); // Refuse to overwrite another copy.
    }
    public static void Add(string instance, IEnumerable<string> paths)
    {
        var folder = SafePaths.Under(instance, "mods"); Directory.CreateDirectory(folder);
        foreach (var path in paths)
        {
            if (!path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("请选择 .jar 模组文件。");
            File.Copy(path, SafePaths.Under(folder, Path.GetFileName(path)), false);
        }
    }
}
