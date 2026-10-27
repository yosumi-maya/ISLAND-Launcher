using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using BlockDock.Core;
using Island.App;
using Island.Core;

internal static class JavaCacheUiTests
{
    private static T Find<T>(MainWindow window, string name) => (T)window.FindName(name);
    private static IEnumerable<T> Children<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); if (child is T item) yield return item;
            foreach (var nested in Children<T>(child)) yield return nested;
        }
    }
    private static async Task Wait(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (!predicate() && DateTime.UtcNow < deadline) await Task.Delay(30);
        if (!predicate()) throw new Exception("Java cache UI did not reach the expected state.");
    }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static MainWindow Window(Window owner, LauncherStore store, JavaRuntimeCache cache) => new(store, javaRuntimes: cache)
        { Owner = owner, Left = -16000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };

    public static async Task RunAsync(Window owner, string root, Action<bool, string> check, Action<Window, string> capture)
    {
        var directory = Path.Combine(root, "java-cache-ui"); var game = Path.Combine(directory, "game");
        var version = Path.Combine(game, "versions", "Sample"); Directory.CreateDirectory(version);
        File.WriteAllText(Path.Combine(version, "Sample.json"), """{"id":"Sample","mainClass":"Main"}""");
        var oldPath = Path.Combine(directory, "old", "java.exe"); var newPath = Path.Combine(directory, "new", "java.exe");
        foreach (var path in new[] { oldPath, newPath }) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "metadata fixture"); }
        var oldJava = new JavaInstallation(oldPath, "21.0.1", 21, "x64", "Fixture"); var newJava = oldJava with { Path = newPath };
        IReadOnlyList<JavaInstallation> installed = [oldJava]; var scans = 0; TaskCompletionSource? detection = null;
        async Task<IReadOnlyList<JavaInstallation>> Discover(string _, string __, CancellationToken token) { scans++; if (detection is not null) await detection.Task.WaitAsync(token); return installed; }
        var store = new LauncherStore(Path.Combine(directory, "settings"));
        var state = new LauncherState { GameDirectory = game, AutoMemory = false, MemoryMb = 512 }; state.EnsureGameFolders(); store.Save(state);
        var seed = new JavaRuntimeCache(store.DirectoryPath, Discover); await seed.GetAvailableAsync(game, "", default);
        var first = Window(owner, store, new JavaRuntimeCache(store.DirectoryPath, Discover));
        try
        {
            first.Show(); first.UpdateLayout(); await Wait(() => Find<Button>(first, "DetectJavaButton").IsEnabled);
            check(scans == 1 && Find<ComboBox>(first, "JavaBox").Items.Count == 2 && Find<ComboBox>(first, "JavaBox").SelectedIndex == 0
                && Find<TextBlock>(first, "JavaStatus").Text.Contains("上次检测"), "A fresh launcher restores its Java dropdown and automatic selection without another scan");
            var instanceState = store.Load(); var card = new InstanceCard(Find<ListBox>(first, "InstanceList").Items.Cast<InstanceCard>().Single().Instance, instanceState.For("Sample"));
            var dialog = new InstanceWindow(first, card, instanceState, () => store.Save(instanceState)) { Left = -16000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
            dialog.Show(); dialog.UpdateLayout();
            check(Children<ComboBox>(dialog).Single(box => box.Name == "InstanceJavaBox").Items.Count == 3 && scans == 1 && !card.Preferences.HasIndependentSettings,
                "Instance settings reuse the same saved Java list while retaining global inheritance"); dialog.Close();
        }
        finally { first.Close(); }
        state = store.Load(); state.JavaPath = oldPath; store.Save(state); File.Delete(oldPath); installed = [newJava];
        detection = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var recovered = Window(owner, store, new JavaRuntimeCache(store.DirectoryPath, Discover));
        try
        {
            recovered.Show(); Find<RadioButton>(recovered, "NavSettings").IsChecked = true; recovered.UpdateLayout();
            Find<ComboBox>(recovered, "JavaBox").BringIntoView(); recovered.UpdateLayout();
            await Wait(() => scans == 2);
            check(!Find<Button>(recovered, "DetectJavaButton").IsEnabled && Find<TextBlock>(recovered, "JavaStatus").Text.Contains("未找到该路径的 Java：" + oldPath)
                && recovered.DollMemoryFeedback.IsOpen, "A deleted saved Java shows its exact missing path and non-blocking feedback while automatic redetection runs");
            recovered.UpdateLayout(); Find<TextBlock>(recovered, "JavaStatus").BringIntoView(); recovered.UpdateLayout(); await Task.Delay(60);
            capture(recovered, "Java-Missing-Redetecting");
            Find<MemoryAllocationEditor>(recovered, "GlobalMemoryEditor").ManualMemoryMb = 7168;
            detection.SetResult(); await Wait(() => Find<Button>(recovered, "DetectJavaButton").IsEnabled); detection = null;
            check(store.Load().JavaPath == "" && Find<ComboBox>(recovered, "JavaBox").Items.Count == 2 && Find<ComboBox>(recovered, "JavaBox").SelectedIndex == 0
                && Find<TextBlock>(recovered, "JavaStatus").Text.Contains("已自动重新检测"), "Automatic recovery removes the deleted Java, persists automatic selection and displays the refreshed list");
            check(store.Load().MemoryMb == 512 && Find<MemoryAllocationEditor>(recovered, "GlobalMemoryEditor").ManualMemoryMb == 7168,
                "Java recovery does not commit or discard an unfinished memory-setting draft");
            recovered.UpdateLayout(); Find<TextBlock>(recovered, "JavaStatus").BringIntoView(); recovered.UpdateLayout(); await Task.Delay(60); capture(recovered, "Java-Missing-Recovered");
            var instanceState = store.Load(); var prefs = instanceState.For("Sample"); prefs.JavaPath = oldPath; prefs.MemoryMb = 1024; store.Save(instanceState);
            var card = new InstanceCard(Find<ListBox>(recovered, "InstanceList").Items.Cast<InstanceCard>().Single().Instance, prefs);
            var dialog = new InstanceWindow(recovered, card, instanceState, () => store.Save(instanceState)) { Left = -16000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
            dialog.Show(); dialog.UpdateLayout(); await Wait(() => Children<TextBlock>(dialog).Single(label => label.Name == "InstanceJavaStatus").Text.Contains("此实例已改为自动选择"));
            check(store.Load().For("Sample") is { JavaPath: "", MemoryMb: 1024, HasIndependentSettings: true } && store.Load().JavaPath == "",
                "A missing independent Java recovers within its own instance without losing the independent memory setting"); dialog.Close();
            var previous = scans;
            Find<ComboBox>(recovered, "JavaBox").SelectedItem = null; Find<ComboBox>(recovered, "JavaBox").Text = Path.Combine(directory, "unfinished", "java.exe");
            Click(Find<Button>(recovered, "DetectJavaButton")); await Wait(() => Find<Button>(recovered, "DetectJavaButton").IsEnabled);
            check(scans == previous + 1 && Find<ComboBox>(recovered, "JavaBox").Text.Contains("unfinished") && store.Load().JavaPath == "" && store.Load().MemoryMb == 512,
                "Manual redetection refreshes the saved catalog while preserving unsaved Java and memory fields");
        }
        finally { detection?.TrySetResult(); recovered.Close(); }
        var restart = Window(owner, store, new JavaRuntimeCache(store.DirectoryPath, Discover)); var scansBeforeRestart = scans;
        try
        {
            restart.Show(); restart.UpdateLayout(); await Wait(() => Find<Button>(restart, "DetectJavaButton").IsEnabled);
            check(scans == scansBeforeRestart && Find<ComboBox>(restart, "JavaBox").Items.Count == 2, "Restarting after recovery reuses the refreshed catalog instead of repeating detection");
        }
        finally { restart.Close(); }
    }

    public static async Task VerifyLaunchAsync(Window owner, string root, JavaInstallation java, string fixtureJar, Action<bool, string> check, Action<Window, string> capture)
    {
        var directory = Path.Combine(root, "java-recovery-launch"); var game = Path.Combine(directory, "game"); var version = Path.Combine(game, "versions", "Sample"); Directory.CreateDirectory(version);
        File.Copy(fixtureJar, Path.Combine(version, "Sample.jar"));
        File.WriteAllText(Path.Combine(version, "Sample.json"), new JsonObject { ["id"] = "Sample", ["mainClass"] = "IslandUiFixture" }.ToJsonString());
        var deleted = Path.Combine(directory, "removed", "java.exe"); Directory.CreateDirectory(Path.GetDirectoryName(deleted)!); File.WriteAllText(deleted, "never executed");
        var state = new LauncherState { GameDirectory = game, JavaPath = deleted, AutoMemory = false, MemoryMb = 512 }; state.EnsureGameFolders();
        var player = PlayerAccount.Offline("CacheTester"); state.Accounts.Add(player); state.SelectedAccountId = player.Id;
        var store = new LauncherStore(Path.Combine(directory, "settings")); store.Save(state);
        var scans = 0;
        Task<IReadOnlyList<JavaInstallation>> Discover(string _, string __, CancellationToken token)
        { scans++; return Task.FromResult<IReadOnlyList<JavaInstallation>>(File.Exists(deleted) ? [java with { Path = deleted }, java] : [java]); }
        var cache = new JavaRuntimeCache(store.DirectoryPath, Discover); await cache.GetAvailableAsync(game, "", default);
        var window = Window(owner, store, cache);
        try
        {
            window.Show(); window.UpdateLayout(); await Wait(() => Find<Button>(window, "DetectJavaButton").IsEnabled);
            File.Delete(deleted); Click(Find<Button>(window, "LaunchButton"));
            await Wait(() => Find<TextBox>(window, "LogBox").Text.Contains("ISLAND_UI_PROCESS_OK"));
            await Wait(() => Find<Border>(window, "OperationPanel").Visibility == Visibility.Collapsed);
            check(scans == 2 && store.Load().JavaPath == "" && store.Load().For("Sample").AutoSelectedJavaPath == java.Path
                && Find<TextBox>(window, "LogBox").Text.Contains("未找到该路径的 Java：" + deleted),
                "Deleting Java after the launcher opens reports the path, rescans once, saves the fallback and starts a real JVM successfully");
            check(Find<TextBlock>(window, "JavaStatus").Text.Contains(java.Path) && Find<ComboBox>(window, "JavaBox").SelectedIndex == 0,
                "Launch-time recovery also refreshes the global Java control and leaves automatic selection visible");
            capture(window, "Java-Recovery-RealJvm");
        }
        finally { window.Close(); }
    }
}
