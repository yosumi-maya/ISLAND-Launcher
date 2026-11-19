using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using BlockDock.Core;
using Island.App;
using Island.Core;

internal static class SelectionContinueUiTests
{
    public static async Task RunAsync(Window owner, string root, Action<bool, string> check, Action<Window, string> capture)
    {
        var directory = Path.Combine(root, "continue-selection"); Directory.CreateDirectory(directory);
        var candidates = await JavaDiscovery.DiscoverAsync(JavaSearchPaths.ForGameDirectory(directory, ""), CancellationToken.None);
        var java = candidates.First(candidate => candidate.Is64Bit && candidate.Major >= 17 && File.Exists(Path.Combine(Path.GetDirectoryName(candidate.Path)!, "javac.exe")));
        var source = Path.Combine(directory, "ContinueFixture.java");
        File.WriteAllText(source, """
            public class ContinueFixture {
                public static void main(String[] args) {
                    System.out.println("DIR=" + System.getProperty("user.dir"));
                    for (String arg : args) System.out.println("ARG=" + arg);
                    System.out.println("HAS_AGENT=" + java.lang.management.ManagementFactory.getRuntimeMXBean().getInputArguments().stream().anyMatch(a -> a.contains("island-quick-play")));
                }
            }
            """, new UTF8Encoding(false));
        var compile = new ProcessStartInfo(Path.Combine(Path.GetDirectoryName(java.Path)!, "javac.exe")) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "--release", "8", "-encoding", "UTF-8", source }) compile.ArgumentList.Add(arg);
        using (var process = Process.Start(compile)!) { await process.WaitForExitAsync(); check(process.ExitCode == 0, "Compile selection/continuation JVM fixture"); }
        var gameRoot = Path.Combine(directory, "game");
        foreach (var id in new[] { "Alpha", "Beta", "Legacy" })
        {
            var version = Path.Combine(gameRoot, "versions", id); Directory.CreateDirectory(version);
            var profile = JsonNode.Parse("""{"mainClass":"ContinueFixture","arguments":{"game":[]}}""")!.AsObject(); profile["id"] = id;
            if (id != "Legacy") profile["arguments"]!["game"]!.AsArray().Add(JsonNode.Parse("""{"rules":[{"action":"allow","features":{"is_quick_play_singleplayer":true}}],"value":["--quickPlaySingleplayer","${quickPlaySingleplayer}"]}"""));
            File.WriteAllText(Path.Combine(version, id + ".json"), profile.ToJsonString());
            using var jar = ZipFile.Open(Path.Combine(version, id + ".jar"), ZipArchiveMode.Create);
            jar.CreateEntryFromFile(Path.Combine(directory, "ContinueFixture.class"), "ContinueFixture.class");
        }
        var alpha = Path.Combine(gameRoot, "versions", "Alpha");
        FolderAndWorldTests.WriteWorld(alpha, "Old Save", "旧世界", DateTimeOffset.Now.AddDays(-1));
        FolderAndWorldTests.WriteWorld(alpha, "Latest Save", "最新世界", DateTimeOffset.Now);
        FolderAndWorldTests.WriteWorld(Path.Combine(gameRoot, "versions", "Legacy"), "旧版 存档", "旧版世界", DateTimeOffset.Now);
        var state = new LauncherState { GameDirectory = gameRoot, JavaPath = java.Path, AutoMemory = false, MemoryMb = 512 };
        state.EnsureGameFolders(); state.ActiveGameFolder.Isolation = VersionIsolation.Enabled;
        var player = PlayerAccount.Offline("ContinueTester"); state.Accounts.Add(player); state.SelectedAccountId = player.Id;
        var store = new LauncherStore(Path.Combine(directory, "settings")); store.Save(state);
        var cache = new JavaRuntimeCache(store.DirectoryPath, (_, _, _) => Task.FromResult<IReadOnlyList<JavaInstallation>>([java]));
        var window = new MainWindow(store, javaRuntimes: cache) { Owner = owner, Left = -16000, Top = 0, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        try
        {
            window.Show(); window.UpdateLayout();
            var list = (ListBox)window.FindName("InstanceList"); var log = (TextBox)window.FindName("LogBox");
            var next = (Button)window.FindName("ContinueButton"); var launch = (Button)window.FindName("LaunchButton");
            void Select(string id) => list.SelectedItem = list.Items.Cast<InstanceCard>().Single(card => card.Id == id);
            async Task<string> Continue()
            {
                log.Clear(); next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while ((!log.Text.Contains("游戏已退出") || !launch.IsEnabled) && DateTime.UtcNow < deadline) await Task.Delay(50);
                check(log.Text.Contains("退出码 0") && launch.IsEnabled, "Continue starts a real JVM and restores controls after exit");
                return log.Text;
            }
            Select("Alpha");
            check(((TextBlock)window.FindName("RecentInstanceName")).Text == "Alpha" && ((TextBlock)window.FindName("RecentWorldName")).Text == "最新世界", "Hero follows selection and chooses the latest valid world");
            var output = await Continue();
            check(output.Contains("DIR=" + alpha) && output.Contains("ARG=Latest Save") && !output.Contains("ARG=Old Save"), "Continue launches the selected instance with its latest world in Quick Play");
            Select("Beta"); output = await Continue();
            check(output.Contains("DIR=" + Path.Combine(gameRoot, "versions", "Beta")) && !output.Contains("ARG=--quickPlaySingleplayer") && output.Contains("HAS_AGENT=false"), "A selected instance without saves starts normally and never inherits the previous save");
            Select("Legacy"); output = await Continue();
            check(output.Contains("HAS_AGENT=true"), "Continue installs the legacy helper for an existing old-version save");
            Select("Alpha"); Directory.Delete(Path.Combine(alpha, "saves", "Latest Save"), true); output = await Continue();
            check(!output.Contains("ARG=--quickPlaySingleplayer") && !output.Contains("ARG=Old Save"), "A save deleted after selection falls back to normal startup without opening a different world");
            window.UpdateLayout(); capture(window, "Selection-Continue-Library");
        }
        finally { window.Close(); }
    }
}
