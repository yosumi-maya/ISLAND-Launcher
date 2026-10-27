using System.IO;
using System.Windows;
using System.Windows.Controls;
using Island.App;
using Island.Core;

internal static class InstanceContextMenuUiTests
{
    public static async Task RunAsync(Window owner, string root, Action<bool, string> check, Action<Window, string> capture, string? captures)
    {
        var isolated = Path.Combine(root, "instance-menu", "isolated"); var shared = Path.Combine(root, "instance-menu", "shared");
        foreach (var (directory, id) in new[] { (isolated, "Alpha"), (isolated, "Beta"), (shared, "Beta") })
        {
            var version = Path.Combine(directory, "versions", id); Directory.CreateDirectory(version);
            File.WriteAllText(Path.Combine(version, id + ".json"), "{\"id\":\"" + id + "\",\"mainClass\":\"Main\",\"arguments\":{\"game\":[]}}");
        }
        var state = new LauncherState { GameDirectory = isolated, ShowFavorites = true }; state.EnsureGameFolders();
        state.ActiveGameFolder.Isolation = VersionIsolation.Enabled;
        state.AddGameFolder(shared, "共享目录").Isolation = VersionIsolation.Disabled;
        foreach (var (directory, id) in new[] { (isolated, "Alpha"), (isolated, "Beta"), (shared, "Beta") }) state.For(directory, id).Favorite = true;
        var store = new LauncherStore(Path.Combine(root, "instance-menu-state")); store.Save(state);
        Directory.CreateDirectory(Path.Combine(shared, "mods")); File.WriteAllText(Path.Combine(shared, "mods", "shared-beta.jar"), "fixture");
        var opened = new List<string>();
        var window = new MainWindow(store, instanceDirectoryOpener: opened.Add) { Owner = owner, Left = -16000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
        try
        {
            window.Show(); window.UpdateLayout(); await Task.Delay(70);
            var list = (ListBox)window.FindName("InstanceList");
            ((RadioButton)window.FindName("FilterFavorites")).IsChecked = true; window.UpdateLayout();
            InstanceCard Card(string directory, string id) => list.Items.Cast<InstanceCard>().Single(card => card.Id == id && GameFolder.SamePath(card.GameDirectory, directory));
            ContextMenu Open(InstanceCard card)
            {
                card = list.Items.Cast<InstanceCard>().Single(current => current.Key == card.Key);
                list.ScrollIntoView(card); window.UpdateLayout();
                var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(card);
                var menu = container.ContextMenu; menu.PlacementTarget = container; menu.IsOpen = true; menu.UpdateLayout(); return menu;
            }
            static void Click(ContextMenu menu, string label) => menu.Items.OfType<MenuItem>().Single(item => item.Header as string == label).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            list.SelectedItem = Card(isolated, "Alpha"); var target = Card(shared, "Beta");
            var menu = Open(target); await Task.Delay(60);
            check(menu.Items.OfType<MenuItem>().Select(item => item.Header as string).SequenceEqual(new[] { "实例管理", "Mod 管理", "游戏目录", "存档", "资源包", "光影" }), "Instance context menu exposes the six requested actions in order");
            check(((InstanceCard)list.SelectedItem).Key == target.Key && ((InstanceCard)menu.Tag).Key == target.Key, "Opening an unselected card's menu selects and targets that exact instance across folders");
            check(window.FindName("ManageButton") is null && ((Button)window.FindName("LaunchButton")).IsVisible, "The bottom launch bar keeps its launch button and removes the old management button");
            if (captures is not null) App.SaveCapture(menu, Path.Combine(captures, "Instance-ContextMenu.png")); menu.IsOpen = false;
            foreach (var (label, relative) in new[] { ("游戏目录", ""), ("存档", "saves"), ("资源包", "resourcepacks"), ("光影", "shaderpacks") }) Click(Open(target), label);
            check(opened.SequenceEqual(new[] { shared, Path.Combine(shared, "saves"), Path.Combine(shared, "resourcepacks"), Path.Combine(shared, "shaderpacks") }, StringComparer.OrdinalIgnoreCase)
                && opened.All(Directory.Exists), "Shared-instance folder shortcuts use its real runtime root and create missing subfolders without opening another root");
            opened.Clear(); var independent = Card(isolated, "Beta"); Click(Open(independent), "存档");
            check(opened.Single() == Path.Combine(isolated, "versions", "Beta", "saves"), "Isolated-instance shortcuts resolve inside that version instead of the shared game root");
            var modChecked = false;
            _ = window.Dispatcher.BeginInvoke(new Action(() =>
            {
                var dialog = Application.Current.Windows.OfType<ModManagerWindow>().Single();
                try { modChecked = ((ListView)LogicalTreeHelper.FindLogicalNode(dialog, "ModsList")).Items.Cast<ModFile>().Single().Name == "shared-beta.jar"; capture(dialog, "Instance-Context-Mods"); }
                finally { dialog.Close(); }
            }));
            Click(Open(target), "Mod 管理");
            check(modChecked, "Mod management opens directly for the right-clicked instance and reads that instance's mods");
            var managementChecked = false;
            _ = window.Dispatcher.BeginInvoke(new Action(() =>
            {
                var dialog = Application.Current.Windows.OfType<InstanceWindow>().Single();
                try { managementChecked = dialog.Title == target.DisplayName; }
                finally { dialog.Close(); }
            }));
            Click(Open(target), "实例管理"); check(managementChecked, "Instance management remains reachable through the card context menu");
            window.Width = 1020; window.Height = 700; window.UpdateLayout(); capture(window, "Instance-Context-Library-1020");
        }
        finally { window.Close(); }
    }
}
