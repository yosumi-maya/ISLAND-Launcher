using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using BlockDock.Core;
using Island.App;
using Island.Core;

internal static class DesignPolishUiTests
{
    private static T Find<T>(FrameworkElement root, string name) => (T)root.FindName(name);
    private static Color ColorOf(ResourceDictionary resources, string key) => ((SolidColorBrush)resources[key]).Color;
    public static async Task RunAsync(Window owner, string root, Action<bool, string> check, Action<Window, string> capture)
    {
        check(Math.Abs(ThemeManager.Contrast(Colors.Black, Colors.White) - 21) < .001, "Theme contrast calculation matches the canonical black and white reference");
        foreach (var preset in AppearancePresets.All)
        {
            var resources = new ResourceDictionary(); ThemeManager.Apply(preset.Color, resources, preset.Dark);
            var pairs = new[] { ("Ink", "Surface"), ("Muted", "Surface"), ("Muted", "WindowBackground"), ("AccentInk", "AccentSoft"), ("AccentSelectionInk", "AccentSelection"), ("OnAccent", "Accent"),
                ("OnPrimaryHover", "PrimaryHover"), ("OnPrimaryPressed", "PrimaryPressed"), ("HeroText", "HeroSurface"), ("HeroMuted", "HeroSurface"), ("NoticeInk", "NoticeBackground") };
            var failed = pairs.Where(pair => ThemeManager.Contrast(ColorOf(resources, pair.Item1), ColorOf(resources, pair.Item2)) < 4.49).ToArray();
            check(failed.Length == 0, "Theme text and button states retain readable contrast: " + preset.Name + " " + string.Join(",", failed.Select(pair => pair.Item1)));
        }
        var directory = Path.Combine(root, "design-polish"); var game = Path.Combine(directory, "game");
        foreach (var id in new[] { "山林探险", "海边小屋" })
        {
            var version = Path.Combine(game, "versions", id); Directory.CreateDirectory(version);
            File.WriteAllText(Path.Combine(version, id + ".json"), "{\"clientVersion\":\"1.20.1\",\"mainClass\":\"Fixture\"}");
        }
        var alternate = Path.Combine(directory, "other-game");
        Directory.CreateDirectory(alternate);
        var state = new LauncherState { GameDirectory = game };
        state.EnsureGameFolders();
        state.AddGameFolder(alternate, "空目录");
        state.For("山林探险").Favorite = true;
        var store = new LauncherStore(Path.Combine(directory, "settings")); store.Save(state);
        var cache = new JavaRuntimeCache(store.DirectoryPath, (_, _, _) => Task.FromResult<IReadOnlyList<JavaInstallation>>([]));
        var window = new MainWindow(store, javaRuntimes: cache, downloadService: new DownloadCenterUiTests.Service())
            { Owner = owner, Left = -16000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
        window.Show();
        try
        {
            window.UpdateLayout();
            await Task.Delay(220);
            var results = Find<Grid>(window, "InstanceResults");
            var list = Find<ListBox>(window, "InstanceList");
            var initialCards = list.Items.Cast<InstanceCard>().ToArray();
            var folders = Find<ItemsControl>(window, "FolderTabs").Items.OfType<RadioButton>().ToArray();
            var added = Path.Combine(alternate, "versions", "new-instance");
            Directory.CreateDirectory(added);
            File.Copy(Path.Combine(game, "versions", "山林探险", "山林探险.json"), Path.Combine(added, "new-instance.json"));
            folders[1].IsChecked = true;
            check(list.Items.Count == 0
                && (!SystemParameters.ClientAreaAnimation || results.RenderTransform is TranslateTransform),
                "A directory tab uses the existing catalog and starts its own short transition");
            Find<RadioButton>(window, "FilterFavorites").IsChecked = true;
            folders[0].IsChecked = true;
            await Task.Delay(230);
            check(results.Opacity == 1 && list.Items.Cast<InstanceCard>().SequenceEqual(initialCards) && folders[0].IsChecked == true
                && ReferenceEquals(folders[0], Find<ItemsControl>(window, "FolderTabs").Items[0]),
                "Rapid favorite and directory navigation reuses the loaded cards and tab controls");
            folders[1].IsChecked = true;
            Find<Button>(window, "RefreshLibraryButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            check(list.Items.Cast<InstanceCard>().Single().Id == "new-instance",
                "Manual refresh picks up instances added outside the launcher");
            folders[0].IsChecked = true;
            var neutralKeys = new[] { "WindowBackground", "SidebarBackground", "ContentBackground", "Surface", "SelectionBackground", "HoverBackground", "Focus", "StatusAccent" };
            var neutralColors = neutralKeys.Select(key => ColorOf(Application.Current.Resources, key)).ToArray();
            check(neutralColors[0] == Colors.White && neutralColors[1] == (Color)ColorConverter.ConvertFromString("#F3F4F6") && neutralColors[1] == neutralColors[2]
                && ColorOf(Application.Current.Resources, "HeroBackground") == (Color)ColorConverter.ConvertFromString("#ECF3F1"),
                "Default window, panels and current-instance card use the reference screenshot colors");
            capture(window, "Design-Reference-Default");

            foreach (var (name, color) in new[] { ("Forest", "#32634D"), ("Rose", "#AD526E"), ("Night", "#5DAF98") })
            {
                Find<RadioButton>(window, "NavSettings").IsChecked = true;
                var dark = name == "Night";
                var accentBefore = ColorOf(Application.Current.Resources, "AccentSelection");
                var heroBefore = ColorOf(Application.Current.Resources, "HeroBackground");
                var button = Find<WrapPanel>(window, dark ? "AppearanceModeButtons" : "ThemePresetButtons").Children.OfType<Button>().Single(item => item.Tag as string == color);
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Task.Delay(220);
                check(store.Load().Appearance.DarkTheme == dark && AppearanceRenderer.Current.DarkTheme == dark
                    && ((AppearancePreview)Find<ContentControl>(window, "AppearancePreviewHost").Content).RenderContext.Current.DarkTheme == dark
                    && ((SolidColorBrush)Find<Button>(window, "LaunchButton").Background).Color == ColorOf(Application.Current.Resources, "Accent"),
                    "Preset mode is saved and shared by the launcher and its appearance preview: " + name);
                if (!dark)
                    check(neutralKeys.Select(key => ColorOf(Application.Current.Resources, key)).SequenceEqual(neutralColors)
                        && ColorOf(Application.Current.Resources, "AccentSelection") != accentBefore
                        && ColorOf(Application.Current.Resources, "HeroBackground") != heroBefore,
                        "Color preset changes emphasized elements while neutral surfaces keep the screenshot palette: " + name);
                foreach (var size in new[] { (1240d, 830d), (1020d, 700d) })
                {
                    window.Width = size.Item1; window.Height = size.Item2;
                    Find<RadioButton>(window, "NavLibrary").IsChecked = true; window.UpdateLayout(); await Task.Delay(120);
                    if (size.Item1 == 1240)
                    {
                        var cards = list.Items.Cast<InstanceCard>()
                            .Select(card => (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(card)).ToArray();
                        MaterialBorder Tile(ListBoxItem card) => (MaterialBorder)card.Template.FindName("B", card);
                        check(((SolidColorBrush)Tile(cards.Single(card => card.IsSelected)).Background).Color == ColorOf(Application.Current.Resources, "AccentSoft")
                            && ((SolidColorBrush)Tile(cards.Single(card => !card.IsSelected)).Background).Color == ColorOf(Application.Current.Resources, "Surface")
                            && cards.All(card => card.ActualHeight <= 146),
                            "Only the selected compact instance card follows the preset tint: " + name);
                    }
                    var surface = Find<MaterialBorder>(window, "WindowSurface");
                    var shell = Find<Grid>(window, "ShellModules");
                    var bounds = shell.TransformToAncestor(surface).TransformBounds(new Rect(shell.RenderSize));
                    Console.WriteLine($"FRAME_GAPS {name} {size.Item1}: left={bounds.Left:0.0}, top={bounds.Top:0.0}, right={surface.ActualWidth-bounds.Right:0.0}, bottom={surface.ActualHeight-bounds.Bottom:0.0}");
                    capture(window, "Design-" + name + "-Library-" + (int)size.Item1);
                    Find<RadioButton>(window, "NavDownloads").IsChecked = true;
                    var center = Find<DownloadCenter>(window, "DownloadCenterView"); Find<RadioButton>(center, "PacksTab").IsChecked = true;
                    await center.SearchAsync(0); window.UpdateLayout(); await Task.Delay(120);
                    capture(window, "Design-" + name + "-Downloads-" + (int)size.Item1);
                }
            }
            var modal = new FormWindow(window, "界面提示", "弹窗、下拉菜单和表单沿用所选深色配色。") { ShowInTaskbar = false, Left = -16000, WindowStartupLocation = WindowStartupLocation.Manual };
            modal.Show(); modal.UpdateLayout(); capture(modal, "Design-Night-Dialog");
            check(ThemeManager.Contrast(((SolidColorBrush)modal.Foreground).Color, ((SolidColorBrush)((Border)modal.Content).Background).Color) >= 4.5,
                "Dialogs inherit readable dark-theme foreground and background"); modal.Close();
            check(Find<Button>(window, "LaunchButton").Template.FindName("KeyboardRing", Find<Button>(window, "LaunchButton")) is null,
                "Primary buttons no longer add an outer keyboard focus frame");
        }
        finally { await Find<DownloadCenter>(window, "DownloadCenterView").ShutdownAsync(); window.Close(); }
        var reopened = new MainWindow(store, javaRuntimes: cache, downloadService: new DownloadCenterUiTests.Service())
            { Owner = owner, Left = -16000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
        reopened.Show(); reopened.UpdateLayout();
        check(AppearanceRenderer.Current.DarkTheme && ThemeManager.Contrast(ColorOf(Application.Current.Resources, "Ink"), ColorOf(Application.Current.Resources, "Surface")) >= 4.5,
            "A reopened launcher restores the complete dark scheme");
        await Find<DownloadCenter>(reopened, "DownloadCenterView").ShutdownAsync(); reopened.Close();
        ThemeManager.Apply(AppearanceSettings.DefaultColor); AppearanceRenderer.Apply(new LauncherAppearance(), AppearanceSettings.DefaultColor);
    }
}
