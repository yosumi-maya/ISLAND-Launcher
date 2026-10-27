using System.Text.Json;
using Island.Core;

internal static class RuntimeSettingsTests
{
    public static void Run(string root, Action<bool, string> check)
    {
        var game = Path.Combine(root, "运行目录", ".minecraft");
        var version = Path.Combine(game, "versions", "Shared"); Directory.CreateDirectory(version);
        check(GameDirectories.Resolve(game, "Shared").Directory == version, "Empty game folder defaults to isolated version directory");
        Directory.CreateDirectory(Path.Combine(game, "mods")); Directory.CreateDirectory(Path.Combine(version, "saves"));
        check(GameDirectories.Resolve(game, "Shared").Isolated, "Empty folders do not change automatic directory detection");
        File.WriteAllText(Path.Combine(game, "mods", "shared.jar"), "fixture");
        check(GameDirectories.Resolve(game, "Shared").Directory == game, "Auto detects shared root mods when the version has no game data");
        check(GameDirectories.Resolve(game, "Shared", VersionIsolation.Enabled).Directory == version, "Forced isolation overrides existing shared data");
        File.WriteAllText(Path.Combine(version, "options.txt"), "fixture");
        check(GameDirectories.Resolve(game, "Shared").Directory == version, "Version data takes priority when root and version both contain game data");
        check(GameDirectories.Resolve(game, "Shared", VersionIsolation.Disabled).Directory == game, "Disabled isolation always uses root even when version data exists");
        var installed = Path.Combine(game, "versions", "Installed"); Directory.CreateDirectory(installed);
        File.WriteAllText(Path.Combine(installed, "blockdock-instance.json"), "{}");
        check(GameDirectories.Resolve(game, "Installed").Directory == installed, "New installer-created instances stay isolated beside shared game data");
        var otherVersion = Path.Combine(game, "versions", "Other"); Directory.CreateDirectory(otherVersion);
        check(GameDirectories.Resolve(game, "Other").Directory == game, "Automatic isolation resolves separately for each version in one game folder");
        try { GameDirectories.Resolve(game, "../escape"); throw new Exception("Traversal accepted"); }
        catch (ArgumentException) { check(true, "Runtime directory resolver rejects version path traversal"); }

        var state = new LauncherState { GameDirectory = game, JavaPath = "global-java.exe" }; state.EnsureGameFolders();
        var first = state.ActiveGameFolder; first.Isolation = VersionIsolation.Disabled;
        var second = state.AddGameFolder(Path.Combine(root, "另一个游戏", ".minecraft"), "另一个游戏"); second.Isolation = VersionIsolation.Enabled;
        state.For(game, "Shared").JavaPath = "version-java.exe";
        check(state.DownloadSettings(game, "Shared").JavaPath == "version-java.exe" && state.DownloadSettings(second.Directory, "Shared").JavaPath == "global-java.exe", "Per-version Java overrides are scoped to their game folder");
        state.For(second.Directory, "Shared").JavaPath = "";
        check(state.DownloadSettings(second.Directory, "Shared").JavaPath == "" && state.DownloadSettings().JavaPath == "global-java.exe", "Explicit automatic Java bypasses the global path without changing installation settings");
        check(state.DownloadSettings(game, "Other").JavaPath == "global-java.exe", "Unset per-version Java inherits global setting");
        var store = new LauncherStore(Path.Combine(root, "runtime-settings")); store.Save(state);
        var restored = store.Load();
        check(restored.GameFolders.First(folder => folder.Id == first.Id).Isolation == VersionIsolation.Disabled && restored.GameFolders.First(folder => folder.Id == second.Id).Isolation == VersionIsolation.Enabled,
            "Each game folder persists its own isolation mode");
        check(restored.For(game, "Shared").JavaPath == "version-java.exe" && restored.For(second.Directory, "Shared").JavaPath == "" && restored.For(game, "Other").JavaPath is null,
            "Custom, automatic and inherited per-version Java survive a settings roundtrip");
        var legacy = JsonSerializer.Deserialize<LauncherState>(JsonSerializer.Serialize(new { GameDirectory = game }))!; legacy.EnsureGameFolders();
        check(legacy.ActiveGameFolder.Isolation == VersionIsolation.Auto && legacy.For("Shared").JavaPath is null, "Older settings gain auto isolation and inherited Java defaults");
        state.ThemeColor = "#7856a4"; store.Save(state);
        check(store.Load().ThemeColor == "#7856A4", "Custom launcher theme color persists and normalizes its hex value");
        try { AppearanceSettings.NormalizeColor("#12345Z"); throw new Exception("Invalid color accepted"); }
        catch (ArgumentException) { check(true, "Invalid theme colors are rejected before saving"); }
        state.ThemeColor = "invalid"; store.Save(state);
        check(store.Load().ThemeColor == AppearanceSettings.DefaultColor && store.Load().GameFolders.Count == 2, "Invalid saved theme falls back without losing other launcher settings");
    }
}
