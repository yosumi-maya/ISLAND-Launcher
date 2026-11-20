using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Island.App;
using Island.Core;
using BlockDock.Core;

internal static class NavigationUiTests
{
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); if (child is T found) yield return found;
            foreach (var nested in Children<T>(child)) yield return nested;
        }
    }
    private static Button Button(DependencyObject root, string label) => Children<Button>(root).Single(button => button.Content as string == label);
    private static void Click(System.Windows.Controls.Primitives.ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    public static async Task RunAsync(Window owner, string root, Action<bool, string> check, Action<Window, string> capture)
    {
        var gameRoot = Path.Combine(root, "navigation", "game"); var version = Path.Combine(gameRoot, "versions", "Alpha"); Directory.CreateDirectory(version);
        File.WriteAllText(Path.Combine(version, "Alpha.json"), """{"id":"Alpha","mainClass":"Main","arguments":{"game":[]}}""");
        Directory.CreateDirectory(Path.Combine(version, "mods"));
        using (var archive = ZipFile.Open(Path.Combine(version, "mods", "example.jar"), ZipArchiveMode.Create))
        {
            using (var manifest = new StreamWriter(archive.CreateEntry("fabric.mod.json").Open()))
                manifest.Write("""{"id":"example","name":"Example Fabric Mod","version":"2.3.4","description":"Local mod description","icon":"icon.png"}""");
            using var icon = archive.CreateEntry("icon.png").Open();
            icon.Write(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jR1sAAAAASUVORK5CYII="));
        }
        var state = new LauncherState { GameDirectory = gameRoot }; state.EnsureGameFolders(); state.ActiveGameFolder.Isolation = VersionIsolation.Enabled;
        var store = new LauncherStore(Path.Combine(root, "navigation", "state")); store.Save(state);
        var cache = new JavaRuntimeCache(store.DirectoryPath, (_, _, _) => Task.FromResult<IReadOnlyList<JavaInstallation>>([]));
        var window = new MainWindow(store, javaRuntimes: cache) { Owner = owner, Left = -16000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
        try
        {
            window.Show(); window.UpdateLayout(); await Task.Delay(200);
            var windows = Application.Current.Windows.Count;
            var selected = ((InstanceCard)((ListBox)window.FindName("InstanceList")).SelectedItem).Key;
            Click((Button)window.FindName("ManageButton")); window.UpdateLayout(); await Task.Delay(190);
            var instance = (InstancePage)window.ActiveManagementPage!;
            check(Application.Current.Windows.Count == windows && Window.GetWindow(instance) == window && instance.IsVisible,
                "Instance management is hosted by the original main window without another window");
            var name = Children<TextBox>(instance).Single(box => box.Text == "Alpha"); name.Text = "Alpha renamed";
            var independent = Children<CheckBox>(instance).Single(box => box.Name == "IndependentRuntimeBox"); independent.IsChecked = true;
            var memoryRow = Children<InstanceSettingRow>(instance).Single(row => row.Name == "InstanceMemorySetting"); memoryRow.Expanded = true; window.UpdateLayout();
            var memory = Children<MemoryAllocationEditor>(instance).Single(); memory.Automatic = false; memory.ManualMemoryMb = 2048;
            foreach (var size in new[] { (1240d, 830d), (1020d, 700d) })
            {
                window.Width = size.Item1; window.Height = size.Item2; window.UpdateLayout(); await Task.Delay(190);
                var host = (FrameworkElement)window.FindName("ManagementPageHost");
                var save = Button(instance, "保存设置"); var bounds = save.TransformToAncestor(host).TransformBounds(new Rect(save.RenderSize));
                check(instance.ActualWidth >= 650 && bounds.Right <= host.ActualWidth + 1 && bounds.Bottom <= host.ActualHeight + 1,
                    $"Inline instance settings keep footer actions inside the main panel at {(int)size.Item1}px");
                capture(window, "Inline-Instance-" + (int)size.Item1);
            }
            Click(Button(instance, "管理模组")); window.UpdateLayout(); await Task.Delay(190);
            var mods = (ModManagerPage)window.ActiveManagementPage!;
            var list = Children<ListView>(mods).Single();
            var metadataDeadline = DateTime.UtcNow.AddSeconds(5);
            while (list.Items.Cast<ModListRow>().Single().Title != "Example Fabric Mod" && DateTime.UtcNow < metadataDeadline)
                await Task.Delay(20);
            var entry = list.Items.Cast<ModListRow>().Single();
            check(entry.Title == "Example Fabric Mod" && entry.VersionLabel.Contains("2.3.4") && entry.Details.Contains("Local mod description") && entry.Icon is not null,
                "Installed mod rows read a local JAR's name, version, description and icon");
            var search = Children<TextBox>(mods).Single(box => box.Name == "ModSearchBox");
            search.Text = "2.3.4";
            check(list.Items.Count == 1, "Local mod search matches its actual version");
            search.Text = "unknown";
            Click(Button(mods, "全部 (1)"));
            check(search.Text.Length == 0 && list.Items.Count == 1, "The all count clears search and restores the full mod list");
            Button RowButton(string buttonName)
            {
                window.UpdateLayout();
                return Children<Button>(list).Single(button => button.Name == buttonName);
            }
            check(new[] { "ModDownloadButton", "ModLocateButton", "ModToggleButton", "ModDeleteButton" }
                .All(buttonName => RowButton(buttonName).IsVisible), "Each installed mod row displays the four shortcut icons");
            list.SelectedIndex = -1;
            Click(RowButton("ModToggleButton"));
            check(Application.Current.Windows.Count == windows && !ModFiles.List(version).Single().Enabled, "The row toggle disables the clicked mod without selecting it first");
            check((string)RowButton("ModToggleButton").ToolTip == "启用模组", "Disabled mod row offers re-enable");
            Click(RowButton("ModToggleButton"));
            check(ModFiles.List(version).Single().Enabled, "The same row shortcut re-enables the mod");
            var prompted = false;
            _ = window.Dispatcher.BeginInvoke(new Action(() =>
            {
                var confirmation = Application.Current.Windows.OfType<Window>().FirstOrDefault(dialog => dialog.Title == "删除模组");
                if (confirmation is null) return;
                prompted = true;
                Click(Button(confirmation, "取消"));
            }), System.Windows.Threading.DispatcherPriority.Background);
            Click(RowButton("ModDeleteButton"));
            check(prompted && ModFiles.List(version).Count == 1, "Canceling the row delete confirmation preserves the mod");
            foreach (var size in new[] { (1240d, 830d), (1020d, 700d) })
            {
                window.Width = size.Item1; window.Height = size.Item2; window.UpdateLayout(); await Task.Delay(190);
                check(list.ActualHeight > 180 && list.View is null && list.ItemTemplate is not null,
                    $"Inline mod list fills the main panel at {(int)size.Item1}px");
                capture(window, "Inline-Mods-" + (int)size.Item1);
            }
            Click(mods.BackButton); window.UpdateLayout(); await Task.Delay(190);
            check(ReferenceEquals(window.ActiveManagementPage, instance) && name.Text == "Alpha renamed" && memory.ManualMemoryMb == 2048 && !instance.IsDisposed && mods.IsDisposed,
                "Returning from mods preserves the instance settings draft and disposes the mod page");
            check(store.Load().For("Alpha").DisplayName != "Alpha renamed", "Navigating to mods does not silently save the instance draft");
            Click(Button(instance, "保存设置")); window.UpdateLayout(); await Task.Delay(190);
            check(instance.IsDisposed && window.ActiveManagementPage is null && store.Load().For("Alpha").DisplayName == "Alpha renamed" && store.Load().For("Alpha").MemoryMb == 2048,
                "Saving inline instance settings persists preferences and returns to the instance list");
            check(((InstanceCard)((ListBox)window.FindName("InstanceList")).SelectedItem).Key == selected && ((TextBlock)window.FindName("RecentInstanceName")).Text == "Alpha renamed",
                "Returning keeps the same selected instance and updates its hero after rename");
            Click((Button)window.FindName("ManageButton")); var discarded = window.ActiveManagementPage!;
            Click((RadioButton)window.FindName("NavLibrary"));
            check(window.ActiveManagementPage is null && discarded.IsDisposed && ((FrameworkElement)window.FindName("LibraryPage")).IsVisible,
                "Clicking the already selected My Instances tab returns from management");
            Click((Button)window.FindName("ManageButton")); var leave = window.ActiveManagementPage!;
            ((RadioButton)window.FindName("NavAccounts")).IsChecked = true;
            check(leave.IsDisposed && window.ActiveManagementPage is null && ((FrameworkElement)window.FindName("AccountsPage")).IsVisible,
                "Sidebar navigation disposes the management session and switches the main panel");
            for (var i = 0; i < 12; i++) ((RadioButton)window.FindName(i % 2 == 0 ? "NavLibrary" : "NavSettings")).IsChecked = true;
            await Task.Delay(230); var settings = (FrameworkElement)window.FindName("SettingsPage");
            check(settings.IsVisible && settings.Opacity == 1 && ((TranslateTransform)settings.RenderTransform).Y == 0
                && !((FrameworkElement)window.FindName("LibraryPage")).IsVisible,
                "Rapid page switching settles on the latest page without a blank or transparent panel");
            PageTransitions.Enter(settings, true); await Task.Delay(35);
            check(settings.Opacity is > 0 and < 1, "The short navigation transition animates visible content");
            PageTransitions.Enter(settings, false);
            check(settings.Opacity == 1 && ((TranslateTransform)settings.RenderTransform).Y == 0,
                "Reduced-motion switching immediately restores the final opacity and position");
        }
        finally { window.Close(); }
    }
}
