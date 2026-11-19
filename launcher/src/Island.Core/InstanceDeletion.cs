using BlockDock.Core;

namespace Island.Core;

public static class InstanceDeletion
{
    public static string Target(string root, string id, string expectedDirectory, IEnumerable<string> registeredRoots)
    {
        root = GameFolder.NormalizePath(root);
        var target = SafePaths.Under(root, "versions/" + SafePaths.InstanceName(id));
        if (!GameFolder.SamePath(target, expectedDirectory)) throw new InvalidDataException("实例目录与所选版本不一致，已停止删除。");
        if (!Directory.Exists(target)) throw new DirectoryNotFoundException("实例目录已不存在，请刷新实例列表。");
        for (var current = target; current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("实例路径包含链接或联接点，已停止删除：" + current);
        foreach (var folder in registeredRoots)
        {
            var path = GameFolder.NormalizePath(folder);
            if (GameFolder.SamePath(target, path) || path.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("实例内包含已登记的游戏目录，请先移除该目录记录。");
        }
        var pending = new Stack<string>(); pending.Push(target);
        while (pending.TryPop(out var directory))
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("实例内包含链接或联接点，请先手动处理：" + entry);
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
            }
        return target;
    }
}
