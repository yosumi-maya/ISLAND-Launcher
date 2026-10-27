using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Island.App;
using Island.Core;

internal static class RunningGameBrowsingUiTests
{
    public static async Task RunAsync(Window owner, string root, string java, string fixtureJar, Action<bool, string> check, Action<Window, string> capture)
    {
        var first = Path.Combine(root, "running-browse", "first"); var second = Path.Combine(root, "running-browse", "second");
        var empty = Path.Combine(root, "running-browse", "empty"); Directory.CreateDirectory(empty);
        var hold = Path.Combine(root, "running-browse", "hold.flag"); File.WriteAllText(hold, "hold");
        foreach (var directory in new[] { first, second })
        {
            var version = Path.Combine(directory, "versions", "Alpha"); Directory.CreateDirectory(version); File.Copy(fixtureJar, Path.Combine(version, "Alpha.jar"));
            var profile = new JsonObject { ["id"] = "Alpha", ["mainClass"] = "IslandUiFixture", ["arguments"] = new JsonObject { ["game"] = new JsonArray(), ["jvm"] = new JsonArray("-Disland.ui.hold=" + hold) } };
            File.WriteAllText(Path.Combine(version, "Alpha.json"), profile.ToJsonString());
        }
        var state = new LauncherState { GameDirectory = first, JavaPath = java, AutoMemory = false, MemoryMb = 512 }; state.EnsureGameFolders();
        state.ActiveGameFolder.Name = "运行目录"; state.ActiveGameFolder.Isolation = VersionIsolation.Enabled;
        state.AddGameFolder(second, "浏览目录").Isolation = VersionIsolation.Enabled; state.AddGameFolder(empty, "空目录");
        state.For(first, "Alpha").Favorite = state.For(second, "Alpha").Favorite = true;
        state.For(first, "Alpha").DisplayName = "正在运行的实例"; state.For(second, "Alpha").DisplayName = "正在浏览的实例";
        var player = PlayerAccount.Offline("BrowseTester"); state.Accounts.Add(player); state.SelectedAccountId = player.Id;
        var store = new LauncherStore(Path.Combine(root, "running-browse-state")); store.Save(state);
        var window = new MainWindow(store) { Owner = owner, Left = -16000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
        T Find<T>(string name) where T : FrameworkElement => (T)window.FindName(name);
        void Folder(string path) => Find<ItemsControl>("FolderTabs").Items.Cast<RadioButton>().Single(tab => GameFolder.SamePath((string)tab.ToolTip, path)).IsChecked = true;
        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        async Task Wait(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(35);
            while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(40);
            if (!condition()) throw new Exception("Running-game browsing did not reach the expected state.");
        }
        try
        {
            window.Show(); window.UpdateLayout(); Click(Find<Button>("LaunchButton"));
            var log = Find<TextBox>("LogBox"); await Wait(() => log.Text.Contains("ISLAND_UI_PROCESS_OK"));
            check(Find<ItemsControl>("FolderTabs").IsEnabled && !Find<Button>("LaunchButton").IsEnabled, "A real running JVM leaves folder browsing enabled while preventing a second launch");
            Folder(second);
            check(Find<ListBox>("InstanceList").Items.Cast<InstanceCard>().Single().GameDirectory == second && Find<TextBlock>("StatusLabel").Text.Contains("游戏运行中"), "Switching folders during play updates the browser and preserves the running-game status");
            Find<RadioButton>("FilterFavorites").IsChecked = true;
            check(Find<ListBox>("InstanceList").Items.Count == 2, "Favorites across folders remain browsable while the game runs");
            Find<TextBox>("SearchBox").Text = "正在浏览";
            check(Find<ListBox>("InstanceList").Items.Cast<InstanceCard>().Single().GameDirectory == second, "Search can filter a different instance without retargeting the running game");
            Folder(empty);
            check(Find<ListBox>("InstanceList").Items.Count == 0 && Find<StackPanel>("EmptyLibrary").IsVisible && Find<ItemsControl>("FolderTabs").IsEnabled, "An empty folder can be opened and left during play instead of trapping the user in an empty list");
            Folder(second); window.UpdateLayout(); capture(window, "Running-Game-FolderSwitch");
            check(!Find<Button>("ManageFoldersButton").IsEnabled && Find<TextBlock>("OperationLabel").Text.Contains("正在运行的实例"), "Folder editing remains guarded and the task panel still names the original running instance");
            Click(Find<Button>("LaunchButton"));
            check(Directory.GetFiles(Path.Combine(store.DirectoryPath, "logs"), "*.log").Length == 1 && !log.Text.Contains("游戏已退出"), "Attempting to launch the browsed instance does not create a second game process");
            _ = window.Dispatcher.BeginInvoke(new Action(() =>
            {
                var dialog = Application.Current.Windows.OfType<MessageWindow>().Single(); dialog.UpdateLayout();
                Click(Descendants<Button>(dialog).Single(button => button.Content as string == "结束游戏"));
            }));
            Click(Find<Button>("CancelButton"));
            await Wait(() => Find<Border>("OperationPanel").Visibility == Visibility.Collapsed);
            var saved = store.Load();
            check(log.Text.Contains("WORKING_DIRECTORY=" + Path.Combine(first, "versions", "Alpha")) && saved.For(first, "Alpha").LastPlayed is not null && saved.For(second, "Alpha").LastPlayed is null,
                "Ending the game after browsing another folder stops and records the original instance only");
            check(GameFolder.SamePath(saved.GameDirectory, second) && Find<ListBox>("InstanceList").Items.Cast<InstanceCard>().Single().GameDirectory == second && Find<Button>("LaunchButton").IsEnabled,
                "Game exit keeps the user's current browsing directory and restores launch controls");
        }
        finally
        {
            if (File.Exists(hold)) File.Delete(hold);
            await Wait(() => Find<Border>("OperationPanel").Visibility == Visibility.Collapsed);
            window.Close();
        }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); if (child is T value) yield return value;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
