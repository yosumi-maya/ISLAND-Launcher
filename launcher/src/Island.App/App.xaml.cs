using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Island.Core;

namespace Island.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args is [MemoryCleanupProcess.HelperArgument])
        {
            // Do not construct a launcher window or read the user's configuration.
            try { Shutdown((int)MemoryCleanup.RunNative().Outcome); }
            catch { Shutdown((int)MemoryCleanupOutcome.Failed); }
            return;
        }
        var captureIndex = Array.IndexOf(e.Args, "--capture");
        var dataIndex = Array.IndexOf(e.Args, "--data-dir");
        var dataPath = dataIndex >= 0 && dataIndex + 1 < e.Args.Length ? e.Args[dataIndex + 1] : null;
        var capturePath = captureIndex >= 0 && captureIndex + 1 < e.Args.Length ? Path.GetFullPath(e.Args[captureIndex + 1]) : null;
        DispatcherUnhandledException += (_, args) =>
        {
            if (capturePath is not null) { Directory.CreateDirectory(capturePath); File.WriteAllText(Path.Combine(capturePath, "error.txt"), args.Exception.ToString()); Shutdown(1); }
            else MessageWindow.Inform(Windows.OfType<Window>().FirstOrDefault(w => w.IsActive), args.Exception.Message);
            args.Handled = true;
        };
        var window = new MainWindow(new LauncherStore(dataPath));
        if (capturePath is not null)
        {
            window.Left = -16000; window.Top = 0; window.WindowStartupLocation = WindowStartupLocation.Manual; window.ShowInTaskbar = false;
            window.Loaded += async (_, _) =>
            {
                try
                {
                    Directory.CreateDirectory(capturePath); await Task.Delay(250);
                    var presetIndex = Array.IndexOf(e.Args, "--capture-preset");
                    if (presetIndex >= 0)
                    {
                        if (dataPath is null || presetIndex + 1 >= e.Args.Length) throw new ArgumentException("预设截图需要独立 --data-dir 和预设颜色。");
                        var buttons = (WrapPanel)window.FindName("ThemePresetButtons");
                        var preset = buttons.Children.OfType<Button>().Single(button => string.Equals(button.Tag as string, e.Args[presetIndex + 1], StringComparison.OrdinalIgnoreCase));
                        preset.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                        await Task.Delay(180);
                    }
                    foreach (var size in new[] { (1240d, 830d), (1020d, 700d) })
                    {
                        window.Width = size.Item1; window.Height = size.Item2;
                        foreach (var page in new[] { "NavLibrary", "NavAccounts", "NavSettings", "NavLogs" })
                        {
                            ((RadioButton)window.FindName(page)).IsChecked = true; window.UpdateLayout(); await Task.Delay(120);
                            SaveCapture((FrameworkElement)window.Content, Path.Combine(capturePath, page + "-" + (int)size.Item1 + ".png"));
                            if (page == "NavSettings")
                            {
                                ((Button)window.FindName("OpenAppearanceButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                                window.UpdateLayout(); await Task.Delay(120);
                                SaveCapture((FrameworkElement)window.Content, Path.Combine(capturePath, "Appearance-" + (int)size.Item1 + ".png"));
                            }
                        }
                    }
                }
                catch (Exception ex) { File.WriteAllText(Path.Combine(capturePath, "error.txt"), ex.ToString()); }
                finally { window.Close(); }
            };
        }
        window.Show();
    }
    public static void SaveCapture(FrameworkElement visual, string path)
    {
        visual.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var backdrop = new DrawingVisual();
        using (var context = backdrop.RenderOpen())
        {
            if (visual.Clip is { } clip) context.PushClip(clip);
            context.DrawRectangle(Window.GetWindow(visual)?.Background ?? Brushes.White, null, new Rect(0, 0, visual.ActualWidth, visual.ActualHeight));
            if (visual.Clip is not null) context.Pop();
        }
        bitmap.Render(backdrop);
        bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
