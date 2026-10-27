using System.Text.RegularExpressions;

namespace BlockDock.Core;

public static class SafePaths
{
    public static string Under(string root, string relative)
    {
        relative = relative.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(relative) || relative.StartsWith('/') || relative.Contains(':') || relative.Contains('\0') ||
            relative.Split('/').Any(p => p is ".." or "." || p.EndsWith(' ') || p.EndsWith('.')))
            throw new InvalidDataException($"不安全的文件路径：{relative}");
        var absoluteRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(absoluteRoot, relative));
        if (!path.StartsWith(absoluteRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"文件路径超出安装目录：{relative}");
        // Refuse junctions / symbolic links already present in the destination chain.
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"目标路径包含符号链接：{current}");
            if (string.Equals(current.TrimEnd(Path.DirectorySeparatorChar), absoluteRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) break;
        }
        return path;
    }

    public static string InstanceName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || name != name.Trim() || name.EndsWith('.') || name.Contains('/') || name.Contains('\\') ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name is "." or ".." || Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])($|\.)", RegexOptions.IgnoreCase))
            throw new ArgumentException("版本名称不能包含路径、特殊字符、结尾空格或 Windows 保留名称。");
        return name;
    }

    public static string SuggestedName(string name)
    {
        var clean = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim().TrimEnd('.');
        if (clean.Length > 90) clean = clean[..90];
        try { return InstanceName(clean); } catch (ArgumentException) { return "Minecraft-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"); }
    }

    public static string Maven(string coordinate)
    {
        var extensionParts = coordinate.Split('@');
        var parts = extensionParts[0].Split(':');
        if (parts.Length is < 3 or > 4) throw new InvalidDataException($"无效的 Maven 坐标：{coordinate}");
        var extension = extensionParts.Length > 1 ? extensionParts[1] : "jar";
        return $"{parts[0].Replace('.', '/')}/{parts[1]}/{parts[2]}/{parts[1]}-{parts[2]}{(parts.Length == 4 ? "-" + parts[3] : "")}.{extension}";
    }
}
