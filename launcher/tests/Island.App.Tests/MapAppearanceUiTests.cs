using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BlockDock.Core;
using Island.App;
using Island.Core;

internal static class MapAppearanceUiTests
{
    private static T Find<T>(FrameworkElement parent, string name) => (T)parent.FindName(name);
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); if (child is T found) yield return found;
            foreach (var nested in Children<T>(child)) yield return nested;
        }
    }
    private static T Control<T>(DependencyObject root, string name) where T : FrameworkElement => Children<T>(root).Single(item => item.Name == name);
    private static void Click(DependencyObject root, string label) => Children<Button>(root).First(button => button.Content as string == label).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static async Task Wait(Func<bool> predicate)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!predicate() && DateTime.UtcNow < until) await Task.Delay(30);
        if (!predicate()) throw new Exception("Map appearance preview did not reach the expected state.");
    }
    private static string MakeImage(string directory)
    {
        Directory.CreateDirectory(directory); var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(Brushes.IndianRed, null, new Rect(0, 0, 64, 64));
        var bitmap = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(directory, "map-card-background.png"); using var output = File.Create(path); encoder.Save(output); return path;
    }
    public static async Task RunAsync(Window owner, string root, Action<bool, string> check, Action<Window, string> capture)
    {
        var initialLive = AppearanceRenderer.Current.Normalize(); var initialColor = AppearanceRenderer.ThemeColor;
        var directory = Path.Combine(root, "map-appearance"); var imagePath = MakeImage(directory);
        var store = new LauncherStore(Path.Combine(directory, "settings")); store.Save(new LauncherState { GameDirectory = Path.Combine(directory, "game") });
        var cache = new JavaRuntimeCache(store.DirectoryPath, (_, _, _) => Task.FromResult<IReadOnlyList<JavaInstallation>>([]));
        var window = new MainWindow(store, backgroundPicker: () => imagePath, javaRuntimes: cache)
        { Owner = owner, Left = -16000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
        window.Show(); window.UpdateLayout();
        try
        {
            var heroBefore = store.Load().Appearance.Hero.Normalize(); var windowBefore = store.Load().Appearance.Window.Normalize();
            var map = Find<MaterialBorder>(window, "RecentMapSurface"); var worldName = Find<TextBlock>(window, "RecentWorldName").Text;
            map.ContextMenu.Items.OfType<MenuItem>().Single().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); window.UpdateLayout();
            var editor = (AppearanceEditor)Find<ContentControl>(window, "BackgroundAppearanceHost").Content;
            var preview = (AppearancePreview)Find<ContentControl>(window, "AppearancePreviewHost").Content;
            check(Find<Grid>(window, "AppearancePage").IsVisible && editor.SelectedArea == SurfaceArea.RecentMap && preview.SelectedArea == SurfaceArea.RecentMap,
                "The actual map card context menu opens its independent appearance settings");
            var heroRegion = Children<Button>(preview).Single(button => button.Tag is SurfaceArea.Hero);
            heroRegion.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Find<Button>(window, "EditMapAppearanceButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); window.UpdateLayout();
            check(editor.SelectedArea == SurfaceArea.RecentMap && Control<StackPanel>(editor, "SurfaceForegroundOptions").IsVisible,
                "The explicit map settings button selects the nested region and exposes independent text controls");
            Control<CheckBox>(editor, "FollowSurfaceTheme").IsChecked = false;
            Control<TextBox>(editor, "SurfaceColorBox").Text = "#315648";
            Control<Slider>(editor, "SurfaceOpacity").Value = 82;
            Click(editor, "白色文字"); Click(editor, "选择背景图片"); Control<Slider>(editor, "ImageTint").Value = 0;
            await Wait(() => preview.RenderContext.Current.RecentMap.ForegroundColor == "#FFFFFF" && preview.RenderContext.Current.RecentMap.ImagePath == imagePath);
            check(store.Load().Appearance.RecentMap.ForegroundColor == "" && AppearanceRenderer.Current.RecentMap.ImagePath == ""
                && preview.RenderContext.Current.Hero == heroBefore, "Map appearance edits remain in the miniature until saved and do not change the parent hero");
            Click(editor, "保存外观设置");
            var ownedImage = store.Load().Appearance.RecentMap.ImagePath;
            check(store.Load().Appearance.RecentMap is { ForegroundColor: "#FFFFFF", Opacity: .82 } && Path.GetDirectoryName(ownedImage) == Path.Combine(store.DirectoryPath, "Backgrounds")
                && File.ReadAllBytes(ownedImage).SequenceEqual(File.ReadAllBytes(imagePath))
                && store.Load().Appearance.Hero == heroBefore && store.Load().Appearance.Window == windowBefore,
                "Saving persists only map-card overrides and copies its image into the owned background directory");
            File.Delete(imagePath);
            foreach (var material in new[] { SurfaceMaterial.Solid, SurfaceMaterial.Gaussian, SurfaceMaterial.LiquidGlass })
            {
                Children<RadioButton>(editor).Single(button => button.Tag is SurfaceMaterial value && value == material).IsChecked = true;
                Click(editor, "保存外观设置"); Find<RadioButton>(window, "NavLibrary").IsChecked = true; window.UpdateLayout(); await Task.Delay(120);
                var rootVisual = (FrameworkElement)window.Content;
                var bitmap = new RenderTargetBitmap((int)rootVisual.ActualWidth, (int)rootVisual.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(rootVisual);
                var point = map.TransformToAncestor(rootVisual).Transform(new Point(map.ActualWidth - 20, map.ActualHeight - 20));
                var sample = new byte[4]; bitmap.CopyPixels(new Int32Rect((int)point.X, (int)point.Y, 1, 1), sample, 4, 0);
                capture(window, "Map-Appearance-" + material);
                check(sample[2] > sample[0] + 20 && ((SolidColorBrush)Find<TextBlock>(window, "RecentWorldName").Foreground).Color == Colors.White
                    && Find<TextBlock>(window, "RecentWorldName").Text == worldName,
                    "The map card uses its own image and sharp chosen text without changing world metadata: " + material
                    + $" (BGRA={string.Join(',', sample)}, text={((SolidColorBrush)Find<TextBlock>(window, "RecentWorldName").Foreground).Color}, name={Find<TextBlock>(window, "RecentWorldName").Text})");
                Find<Button>(window, "EditMapAppearanceButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); window.UpdateLayout();
            }
            foreach (var size in new[] { (1240d, 830d), (1020d, 700d) })
            {
                window.Width = size.Item1; window.Height = size.Item2; window.UpdateLayout(); capture(window, "Map-Editor-" + (int)size.Item1);
                check(Find<Button>(window, "EditMapAppearanceButton").IsVisible && Find<ScrollViewer>(window, "AppearanceSettingsScroll").ActualHeight > 180,
                    "Independent map controls and the preview entry fit at " + (int)size.Item1);
            }
            Control<TextBox>(editor, "SurfaceForegroundBox").Text = "#bad";
            heroRegion.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            check(editor.SelectedArea == SurfaceArea.RecentMap && preview.SelectedArea == SurfaceArea.RecentMap,
                "An invalid map text color cannot silently discard the draft when selecting another region");
            Control<TextBox>(editor, "SurfaceForegroundBox").Text = "#FFFFFF";
            Control<CheckBox>(editor, "AutoSurfaceForeground").IsChecked = true;
            await Wait(() => preview.RenderContext.Current.RecentMap.ForegroundColor == "");
            check(!Control<TextBox>(editor, "SurfaceForegroundBox").IsEnabled, "Map text can independently return to automatic theme contrast");
            Click(editor, "白色文字"); Click(editor, "保存外观设置");
            var restored = new MainWindow(store, javaRuntimes: cache) { Owner = owner, Left = -16000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
            restored.Show(); restored.UpdateLayout();
            check(AppearanceRenderer.Current.RecentMap.ForegroundColor == "#FFFFFF" && AppearanceRenderer.Current.RecentMap.ImagePath == ownedImage
                && ((SolidColorBrush)Find<TextBlock>(restored, "RecentWorldName").Foreground).Color == Colors.White,
                "A reopened launcher restores the map-card text and owned image after the original image is removed"); restored.Close();
            Click(editor, "重置此区域"); Click(editor, "保存外观设置");
            check(store.Load().Appearance.RecentMap == new SurfaceAppearance() && store.Load().Appearance.Hero == heroBefore,
                "Resetting the map card clears only its independent overrides");
        }
        finally
        {
            window.Close(); ThemeManager.Apply(initialColor, dark: initialLive.DarkTheme); AppearanceRenderer.Apply(initialLive, initialColor);
        }
    }
}
