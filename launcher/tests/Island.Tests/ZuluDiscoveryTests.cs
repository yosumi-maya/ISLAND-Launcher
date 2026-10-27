using BlockDock.Core;

internal static class ZuluDiscoveryTests
{
    public static void Run(string root, Action<bool, string> check)
    {
        var nearby = Path.Combine(root, "portable-java");
        var game = Path.Combine(nearby, ".minecraft"); Directory.CreateDirectory(game);
        string Runtime(string home)
        {
            var bin = Path.Combine(home, "bin"); Directory.CreateDirectory(bin);
            var path = Path.Combine(bin, "java.exe"); File.WriteAllText(path, "candidate only; never executed"); return path;
        }
        var sibling = Runtime(Path.Combine(nearby, "zulu25-win_x64"));
        var inside = Runtime(Path.Combine(game, "zulu8.90.0.19-ca-jre8.0.472-win_x64"));
        var azul = Runtime(Path.Combine(game, "Azul-25.0.3"));
        var jdk = Runtime(Path.Combine(nearby, "jdk-21.0.9"));
        var renamedZulu8 = Runtime(Path.Combine(nearby, "zulujava8"));
        File.WriteAllText(Path.Combine(nearby, "zulujava8", "release"), "JAVA_VERSION=\"1.8.0_462\"\nIMPLEMENTOR=\"Azul Systems, Inc.\"");
        var renamed = Runtime(Path.Combine(game, "我的运行环境"));
        File.WriteAllText(Path.Combine(game, "我的运行环境", "release"), "JAVA_VERSION=\"21.0.9\"");
        var legacy = Runtime(Path.Combine(nearby, "旧版运行环境"));
        Directory.CreateDirectory(Path.Combine(nearby, "旧版运行环境", "lib"));
        File.WriteAllText(Path.Combine(nearby, "旧版运行环境", "lib", "rt.jar"), "legacy runtime marker");
        var unrelated = Runtime(Path.Combine(nearby, "JavaWeb"));
        var hidden = Runtime(Path.Combine(game, "mods", "zulu-21"));
        var paths = JavaSearchPaths.ForGameDirectory(game, "");
        var candidates = JavaDiscovery.CollectCandidates(paths, default);
        bool Contains(string path) => candidates.Contains(path, StringComparer.OrdinalIgnoreCase);
        check(Contains(sibling), "Portable Zulu beside the game directory is discovered without PATH or registry installation");
        check(Contains(inside) && Contains(azul), "Vendor-named Zulu JRE and Azul distributions inside the game directory are discovered");
        check(Contains(jdk), "Versioned portable JDK folders remain discoverable beside the game directory");
        check(Contains(renamedZulu8), "Renamed zulujava8 is discovered from its Java layout instead of the vendor-name suffix");
        check(Contains(renamed) && Contains(legacy), "Arbitrarily named modern and legacy Java homes are discovered from runtime markers");
        check(!Contains(unrelated) && !Contains(hidden), "Portable discovery does not traverse unrelated JavaWeb folders or game mods");
        var configured = JavaDiscovery.CollectCandidates(JavaSearchPaths.ForGameDirectory(game, sibling), default);
        check(configured.Count(path => path.Equals(sibling, StringComparison.OrdinalIgnoreCase)) == 1, "Configured and portable scan paths identify one Zulu candidate");
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        check(paths.Roots.Contains(Path.Combine(programData, "Programs/Zulu"), StringComparer.OrdinalIgnoreCase), "Per-user Zulu installation directories are included in system discovery");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { JavaDiscovery.CollectCandidates(paths, cancellation.Token); throw new Exception("Canceled collection accepted"); }
        catch (OperationCanceledException) { check(true, "Portable Zulu candidate collection honors cancellation"); }
    }
}
