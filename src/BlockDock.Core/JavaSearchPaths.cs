using Microsoft.Win32;
using System.Runtime.Versioning;

namespace BlockDock.Core;

public sealed record JavaSearchPaths(IReadOnlyList<string> DirectCandidates, IReadOnlyList<string> Roots)
{
    public static JavaSearchPaths ForGameDirectory(string root, string configured)
    {
        var direct = new List<string> { configured };
        var roots = new List<string>();
        foreach (var variable in new[] { "JAVA_HOME", "JDK_HOME", "JRE_HOME" })
            direct.Add(Environment.GetEnvironmentVariable(variable) ?? "");
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            direct.Add(folder.Trim().Trim('"'));
        foreach (var parent in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) })
        {
            if (parent.Length == 0) continue;
            foreach (var vendor in new[] { "Java", "Eclipse Adoptium", "Eclipse Foundation", "AdoptOpenJDK", "Microsoft", "Zulu", "Azul", "Azul Systems", "BellSoft", "Amazon Corretto", "Semeru", "Programs/Java", "Programs/Eclipse Adoptium", "Programs/Zulu", "Programs/Azul" })
                roots.Add(Path.Combine(parent, vendor));
        }
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var relative in new[] { ".jdks", ".jre", ".jabba/jdk", ".scoop/apps/temurin-lts-jdk", "scoop/apps/temurin-lts-jdk", "scoop/apps/openjdk", "AppData/Roaming/.minecraft/runtime", "AppData/Roaming/PrismLauncher/java", "AppData/Local/Packages/Microsoft.4297127D64EC6_8wekyb3d8bbwe/LocalCache/Local/runtime" })
            if (user.Length > 0) roots.Add(Path.Combine(user, relative));
        foreach (var parent in new[] { Path.GetFullPath(root), Path.GetDirectoryName(Path.GetFullPath(root)), AppContext.BaseDirectory })
        {
            if (parent is null) continue;
            direct.Add(parent);
            foreach (var relative in new[] { "runtime", "java", "jre", "jdk", "zulu", "azul", "PCL/Java", "HMCL/java", "PrismLauncher/java", "java-runtime" })
                roots.Add(Path.Combine(parent, relative));
            AddPortableRoots(parent, roots);
        }
        if (OperatingSystem.IsWindows()) AddRegistryPaths(direct);
        return new(direct.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    // ZIP distributions keep their vendor/version folder names. Inspect nearby folder
    // names only; the bounded discovery walk still skips game content and junctions.
    private static void AddPortableRoots(string parent, List<string> roots)
    {
        try
        {
            foreach (var child in Directory.EnumerateDirectories(parent))
            {
                var name = Path.GetFileName(child);
                if (new[] { "java", "jdk", "jre", "zulu", "azul" }.Any(prefix =>
                    name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && (name.Length == prefix.Length || char.IsDigit(name[prefix.Length]) || name[prefix.Length] is '-' or '_' or '.' or ' ')))
                {
                    roots.Add(child);
                    if (roots.Count >= 512) break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
    }

    [SupportedOSPlatform("windows")]
    private static void AddRegistryPaths(List<string> paths)
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var registry = RegistryKey.OpenBaseKey(hive, view);
                foreach (var name in new[] { @"SOFTWARE\JavaSoft", @"SOFTWARE\AdoptOpenJDK", @"SOFTWARE\Eclipse Adoptium", @"SOFTWARE\Eclipse Foundation", @"SOFTWARE\Azul Systems\Zulu", @"SOFTWARE\Azul Systems\Zulu 32-bit" })
                {
                    using var vendor = registry.OpenSubKey(name);
                    if (vendor is not null) ReadKey(vendor, 0, paths);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ReadKey(RegistryKey key, int depth, List<string> paths)
    {
        foreach (var name in new[] { "JavaHome", "InstallationPath", "Path" })
            if (key.GetValue(name) is string value && !string.IsNullOrWhiteSpace(value)) paths.Add(value);
        if (depth >= 4) return;
        foreach (var name in key.GetSubKeyNames().Take(100))
        {
            try { using var child = key.OpenSubKey(name); if (child is not null) ReadKey(child, depth + 1, paths); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        }
    }
}
