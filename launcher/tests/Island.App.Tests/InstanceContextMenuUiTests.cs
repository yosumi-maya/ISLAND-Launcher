using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using Island.App;
using Island.Core;

internal static class InstanceContextMenuUiTests
{
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T item) yield return item;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
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
            check(((TextBlock)window.FindName("RecentInstanceName")).Text == "Alpha" && ((Button)window.FindName("ContinueButton")).IsEnabled,
                "Selecting a never-played instance updates the hero and permits ordinary continuation");
            var saved = FolderAndWorldTests.WriteWorld(shared, "Existing World", "共享世界", DateTimeOffset.Now);
            var menu = Open(target); await Task.Delay(60);
            check(((TextBlock)window.FindName("RecentInstanceName")).Text == target.DisplayName
                && ((TextBlock)window.FindName("RecentWorldName")).Text == "共享世界",
                "Selecting another folder's card updates the hero and its own runtime save");
            list.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent });
            check(!Application.Current.Windows.OfType<InstanceWindow>().Any(), "Double-clicking a card no longer opens instance management");
            check(menu.Items.OfType<MenuItem>().Select(item => item.Header as string).SequenceEqual(new[] { "实例管理", "Mod 管理", "游戏目录", "存档", "资源包", "光影", "删除实例" }), "Instance context menu places deletion after the shader folder");
            menu.UpdateLayout();
            var separators = menu.Items.OfType<Separator>().ToArray();
            check(separators.Length == 2 && separators.All(line => line.ActualWidth >= menu.ActualWidth - 24), "Both menu separators span the available content width");
            var delete = menu.Items.OfType<MenuItem>().Single(item => item.Header as string == "删除实例");
            check(((SolidColorBrush)delete.Foreground).Color.R > ((SolidColorBrush)delete.Foreground).Color.G, "The delete action uses the red destructive foreground");
            check(((InstanceCard)list.SelectedItem).Key == target.Key && ((InstanceCard)menu.Tag).Key == target.Key, "Opening an unselected card's menu selects and targets that exact instance across folders");
            check(window.FindName("ManageButton") is Button { IsVisible: true, IsEnabled: true } && ((Button)window.FindName("LaunchButton")).IsVisible, "The bottom launch bar shows both management and launch actions for the selected instance");
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
                try
                {
                    dialog.UpdateLayout(); managementChecked = dialog.Title == target.DisplayName;
                    var rows = Descendants<InstanceSettingRow>(dialog).ToArray();
                    check(rows.Length == 3 && rows.All(row => !row.Expanded), "Instance runtime settings start as three compact collapsed rows");
                    check(rows[0].Summary.Contains("全局") && rows[1].Summary.Contains("自动"), "Collapsed settings show inherited Java and memory summaries");
                    capture(dialog, "Instance-Settings-Collapsed");
                    rows[1].Expanded = true; dialog.UpdateLayout();
                    check(Descendants<MemoryAllocationEditor>(dialog).Single().IsVisible, "Memory settings expand inside their row");
                    capture(dialog, "Instance-Settings-Memory");
                    var independent = Descendants<CheckBox>(dialog).Single(box => box.Name == "IndependentRuntimeBox"); independent.IsChecked = true;
                    var memory = Descendants<MemoryAllocationEditor>(dialog).Single(); memory.Automatic = false; memory.ManualMemoryMb = 6144;
                    check(memory.IsEnabled && rows[1].Summary.Contains("6.0"), "Independent memory edits update the compact summary");
                    rows[1].Expanded = false; rows[2].Expanded = true; dialog.UpdateLayout(); capture(dialog, "Instance-Settings-Advanced");
                    rows[2].Expanded = false; rows[0].Expanded = true; dialog.UpdateLayout(); capture(dialog, "Instance-Settings-Java");
                    independent.IsChecked = false;
                    check(!memory.IsEnabled && rows[1].Summary.Contains("全局"), "Returning to global settings restores the inherited summary");
                }
                finally { dialog.Close(); }
            }));
            Click(Open(target), "实例管理"); check(managementChecked, "Instance management remains reachable through the card context menu");
            var managedFromButton = false;
            _ = window.Dispatcher.BeginInvoke(new Action(() =>
            {
                var dialog = Application.Current.Windows.OfType<InstanceWindow>().Single();
                managedFromButton = dialog.Title == ((InstanceCard)list.SelectedItem).DisplayName;
                dialog.Close();
            }));
            ((Button)window.FindName("ManageButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(managedFromButton, "The bottom management button opens the currently selected instance");
            list.SelectedItem = Card(isolated, "Alpha");
            check(((TextBlock)window.FindName("RecentInstanceName")).Text == "Alpha"
                && ((TextBlock)window.FindName("RecentWorldName")).Text == "尚未发现单人存档",
                "Selecting an instance without saves clears the previous instance's save from the hero");
            window.Width = 1020; window.Height = 700; window.UpdateLayout(); capture(window, "Instance-Context-Library-1020");
        }
        finally { window.Close(); }
    }
}
