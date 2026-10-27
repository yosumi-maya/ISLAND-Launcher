using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Island.App;
using Island.Core;

internal static class LaunchOptionsUiTests
{
    public static async Task Run(MainWindow owner, string root, Action<bool, string> check, Action<Window, string> capture, string? captures)
    {
        var settings = new JvmSettings { ExtraArguments = "-Dexample=value", ExtraGameArguments = "--server \"中文 地址\"", PreLaunchCommand = "echo ready",
            GarbageCollector = GarbageCollectorMode.PreferGenerationalZ, DisableJavaLaunchWrapper = true, DisableLwjglUnsafeAgent = true, HighPerformanceGpu = true, VerifyLoginCertificates = false };
        var editor = new JvmSettingsEditor(); editor.Load(settings);
        var form = new Window { Owner = owner, Title = "启动设置核对", Content = new ScrollViewer { Content = editor, Padding = new Thickness(24), VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            Width = 740, Height = 940, WindowStyle = WindowStyle.None, Left = -16000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
        try
        {
            form.Show(); form.UpdateLayout();
            check(editor.Read() == settings, "Launch settings editor roundtrips game arguments, pre-command, GC and all compatibility / GPU / SSL options");
            var combo = Children<ComboBox>(editor).Single(box => box.Name == "GarbageCollectorBox");
            check(combo.Items.Count == 5 && combo.SelectedValue is GarbageCollectorMode.PreferGenerationalZ, "Collector dropdown exposes all five requested modes");
            combo.BringIntoView(); form.UpdateLayout(); combo.IsDropDownOpen = true; await Task.Delay(60);
            var popup = (System.Windows.Controls.Primitives.Popup)combo.Template.FindName("PART_Popup", combo);
            check(popup.IsOpen && ((FrameworkElement)popup.Child).ActualWidth > 400, "The themed collector dropdown opens with readable full labels");
            if (captures is not null) App.SaveCapture((FrameworkElement)popup.Child, Path.Combine(captures, "GarbageCollector-Dropdown.png")); combo.IsDropDownOpen = false;
            capture(form, "AdvancedLaunchSettings");
            var omit = Children<CheckBox>(editor).Single(box => box.Name == "OmitJvmOptimizationsBox"); omit.IsChecked = true;
            check(!combo.IsEnabled, "Optimization opt-out visibly disables the automatic collector preset");
            editor.Load(new()); check(Children<CheckBox>(editor).Single(box => box.Name == "VerifyLoginCertificatesBox").IsChecked == true && combo.IsEnabled, "Default launch UI enables certificate verification and automatic GC presets");
        }
        finally { form.Close(); }

        var directory = Path.Combine(root, "thumbnail-fixture", ".minecraft"); var version = Path.Combine(directory, "versions", "WorldFixture"); Directory.CreateDirectory(version);
        File.WriteAllText(Path.Combine(version, "WorldFixture.json"), """{"id":"WorldFixture","mainClass":"Main","arguments":{"game":[]}}""");
        var metadata = FolderAndWorldTests.WriteWorld(version, "Cottage", "最近世界封面", DateTimeOffset.Now.AddMinutes(-1));
        var icon = Path.Combine(Path.GetDirectoryName(metadata)!, "icon.png");
        var pixels = new byte[64 * 64 * 4];
        for (var y = 0; y < 64; y++) for (var x = 0; x < 64; x++)
        {
            var offset = (y * 64 + x) * 4; pixels[offset] = y < 28 ? (byte)210 : (byte)50; pixels[offset + 1] = y < 28 ? (byte)175 : (byte)120; pixels[offset + 2] = y < 28 ? (byte)100 : (byte)65; pixels[offset + 3] = 255;
        }
        var image = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, pixels, 64 * 4);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image)); using (var stream = File.Create(icon)) png.Save(stream);
        var store = new LauncherStore(Path.Combine(root, "thumbnail-state")); store.Save(new() { GameDirectory = directory });
        var window = new MainWindow(store) { Owner = owner, Left = -16000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
        try
        {
            window.Show(); window.UpdateLayout(); await Task.Delay(80);
            var thumbnail = (Image)window.FindName("RecentWorldThumbnail"); var name = (TextBlock)window.FindName("RecentWorldName");
            var thumbnailFrame = (FrameworkElement)window.FindName("RecentWorldThumbnailFrame");
            check(thumbnail.Source is not null && name.Text == "最近世界封面" && thumbnailFrame.IsVisible, "Recent-world card displays the selected world's actual icon.png in its own frame");
            foreach (var size in new[] { (1240d, 830d), (1020d, 700d) })
            {
                window.Width = size.Item1; window.Height = size.Item2; window.UpdateLayout(); await Task.Delay(60);
                var card = (FrameworkElement)window.FindName("RecentMapSurface");
                var imageBounds = thumbnailFrame.TransformToAncestor(card).TransformBounds(new Rect(thumbnailFrame.RenderSize));
                var titleBounds = name.TransformToAncestor(card).TransformBounds(new Rect(name.RenderSize));
                check(card.ActualHeight <= 116.1 && card.Clip is not null && thumbnailFrame.Clip is not null && name.IsVisible
                    && Math.Abs(thumbnailFrame.ActualWidth - 64) < 0.1 && Math.Abs(thumbnailFrame.ActualHeight - 64) < 0.1
                    && titleBounds.Right < imageBounds.Left && imageBounds.Right < card.ActualWidth,
                    $"World thumbnail is a separate rounded square on the right with text beside it at {(int)size.Item1}px");
                capture(window, "RecentWorldThumbnail-" + (int)size.Item1);
            }
            File.WriteAllText(icon, "invalid png");
            var refresh = Children<Button>(window).Single(button => button.Content as string == "↻"); refresh.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            check(thumbnail.Source is null && name.Text == "最近世界封面" && !thumbnailFrame.IsVisible, "A corrupt world icon hides the square without losing metadata or the configured map background");
            File.Delete(icon); refresh.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            check(thumbnail.Source is null && name.Text == "最近世界封面", "A missing world icon remains a valid recent-world record");
        }
        finally { window.Close(); }
    }
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); if (child is T typed) yield return typed;
            foreach (var descendant in Children<T>(child)) yield return descendant;
        }
    }
}
