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
            var pairs = new[] { ("Ink", "Surface"), ("Muted", "Surface"), ("Muted", "WindowBackground"), ("AccentInk", "AccentSoft"), ("OnAccent", "Accent"),
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
        var store = new LauncherStore(Path.Combine(directory, "settings")); store.Save(new LauncherState { GameDirectory = game });
        var cache = new JavaRuntimeCache(store.DirectoryPath, (_, _, _) => Task.FromResult<IReadOnlyList<JavaInstallation>>([]));
        var window = new MainWindow(store, javaRuntimes: cache, downloadService: new DownloadCenterUiTests.Service())
            { Owner = owner, Left = -16000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
        window.Show();
        try
        {
            foreach (var (name, color) in new[] { ("Forest", "#32634D"), ("Rose", "#AD526E"), ("Night", "#5DAF98") })
            {
                Find<RadioButton>(window, "NavSettings").IsChecked = true;
                var button = Find<WrapPanel>(window, "ThemePresetButtons").Children.OfType<Button>().Single(item => item.Tag as string == color);
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Task.Delay(220);
                var dark = name == "Night";
                check(store.Load().Appearance.DarkTheme == dark && AppearanceRenderer.Current.DarkTheme == dark
                    && ((AppearancePreview)Find<ContentControl>(window, "AppearancePreviewHost").Content).RenderContext.Current.DarkTheme == dark
                    && ((SolidColorBrush)Find<Button>(window, "LaunchButton").Background).Color == ColorOf(Application.Current.Resources, "Accent"),
                    "Preset mode is saved and shared by the launcher and its appearance preview: " + name);
                foreach (var size in new[] { (1240d, 830d), (1020d, 700d) })
                {
                    window.Width = size.Item1; window.Height = size.Item2;
                    Find<RadioButton>(window, "NavLibrary").IsChecked = true; window.UpdateLayout(); await Task.Delay(120);
                    capture(window, "Design-" + name + "-Library-" + (int)size.Item1);
                    Find<RadioButton>(window, "NavDownloads").IsChecked = true;
                    var center = Find<DownloadCenter>(window, "DownloadCenterView"); Find<RadioButton>(center, "PacksTab").IsChecked = true;
                    await center.SearchAsync(0); window.UpdateLayout(); await Task.Delay(120);
                    capture(window, "Design-" + name + "-Downloads-" + (int)size.Item1);
                }
            }
            var modal = new FormWindow(window, "界面提示", "弹窗、下拉菜单和表单沿用所选深色配色。") { ShowInTaskbar = false, Left = -16000, WindowStartupLocation = WindowStartupLocation.Manual };
            modal.Show(); modal.UpdateLayout(); capture(modal, "Design-Night-Dialog");
            check(ThemeManager.Contrast(((SolidColorBrush)modal.Foreground).Color, ((SolidColorBrush)modal.Background).Color) >= 4.5,
                "Dialogs inherit readable dark-theme foreground and background"); modal.Close();
            check(Find<Button>(window, "LaunchButton").Template.FindName("KeyboardRing", Find<Button>(window, "LaunchButton")) is Border,
                "Primary buttons provide an independent keyboard focus ring without changing padding");
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
