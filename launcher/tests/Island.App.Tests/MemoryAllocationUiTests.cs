using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Island.App;
using Island.Core;

internal static class MemoryAllocationUiTests
{
    public static async Task Run(MainWindow window, LauncherStore store, Action<bool, string> check, Action<Window, string> capture, string? captures)
    {
        var snapshot = new MemorySnapshot(14029, 3277);
        var sample = new MemoryAllocationEditor(() => snapshot);
        sample.Load(true, 4096);
        var optimization = new Grid(); optimization.ColumnDefinitions.Add(new ColumnDefinition()); optimization.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        optimization.Children.Add(new CheckBox { Content = "启动游戏前进行内存优化", IsChecked = true, Margin = new Thickness(0), VerticalAlignment = VerticalAlignment.Center });
        var clean = new Button { Content = "立即清理", Padding = new Thickness(12, 5, 12, 5) }; Grid.SetColumn(clean, 1); optimization.Children.Add(clean); sample.OptimizationContent = optimization;
        var frame = new Border { Padding = new Thickness(16, 6, 16, 14), Margin = new Thickness(17, 0, 5, 3), Child = sample, Background = Brushes.White };
        var canvas = new Grid(); canvas.Children.Add(frame);
        var host = new Window { Content = canvas, Width = 625, SizeToContent = SizeToContent.Height, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            Left = -16000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
        ThemeManager.Apply("#136CC7", host.Resources);
        try
        {
            host.Show(); host.UpdateLayout(); await Task.Delay(60);
            check(sample.Automatic && !sample.MemorySlider.IsEnabled && sample.DisplayMemoryMb == MemoryAllocator.Recommend(snapshot, 0), "Automatic radio disables the slider and shows a current recommended allocation");
            check(((TextBlock)sample.FindName("UsedValue")).Text == "10.5 GB / 13.7 GB" && ((TextBlock)sample.FindName("AllocationValue")).Text.Contains("可用 3.2 GB"), "Used, total and available readings use live values in GB");
            if (captures is not null) App.SaveCapture((FrameworkElement)host.Content, Path.Combine(captures, "MemoryAllocation-Reference.png"));
            sample.CustomOption.IsChecked = true; sample.MemorySlider.Value = 6144;
            check(!sample.Automatic && sample.ManualMemoryMb == 6144 && sample.DisplayMemoryMb == 6144 && sample.MemorySlider.IsEnabled, "Custom radio enables the slider and immediately updates the displayed game budget");
            sample.AutomaticOption.IsChecked = true; sample.Refresh(); sample.CustomOption.IsChecked = true;
            check(sample.ManualMemoryMb == 6144 && sample.MemorySlider.Value == 6144 && sample.AutomaticOption.IsChecked != true, "Switching through automatic mode preserves the custom limit and maintains exclusive radio selection");
            sample.MemorySlider.Focus(); var beforeKey = sample.MemorySlider.Value;
            sample.MemorySlider.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(sample.MemorySlider)!, Environment.TickCount, Key.Right) { RoutedEvent = Keyboard.KeyDownEvent });
            check(sample.MemorySlider.Value == beforeKey + 256, "The memory slider supports 256 MB keyboard steps");
            check(sample.MemorySlider.IsKeyboardFocused && ((SolidColorBrush)sample.MemorySlider.Background).Color == ((SolidColorBrush)host.Resources["AccentSoft"]).Color, "Keyboard focus gives the custom memory slider a visible theme-colored highlight");
            var track = (Track)sample.MemorySlider.Template.FindName("PART_Track", sample.MemorySlider);
            var beforeDrag = sample.ManualMemoryMb;
            track.Thumb.RaiseEvent(new DragDeltaEventArgs(60, 0) { RoutedEvent = Thumb.DragDeltaEvent });
            check(sample.ManualMemoryMb > beforeDrag && sample.ManualMemoryMb <= sample.MemorySlider.Maximum, "Dragging the real slider thumb updates the bounded custom memory budget");
            sample.MemorySlider.Value = beforeDrag;
            snapshot = new(14029, 2048); sample.Refresh();
            var used = (ColumnDefinition)sample.FindName("UsedColumn"); var game = (ColumnDefinition)sample.FindName("GameColumn"); var free = (ColumnDefinition)sample.FindName("FreeColumn");
            check(used.Width.Value == 11981 && game.Width.Value == 2048 && free.Width.Value == 0 && ((TextBlock)sample.FindName("AllocationValue")).Text.Contains("6.3 GB"), "The bar fits physical memory when a custom budget exceeds availability while retaining the requested limit");
            sample.Load(true, 4096); snapshot = new(14029, 400); sample.Refresh();
            check(sample.DisplayMemoryMb == 0 && ((TextBlock)sample.FindName("AllocationValue")).Text.Contains("无法自动分配"), "Insufficient memory shows a real unavailable allocation rather than inventing a recommendation");
            sample.Load(false, 65536); snapshot = new(14029, 3277); sample.Refresh();
            check(sample.ManualMemoryMb == 65536 && sample.MemorySlider.Maximum == 65536, "Opening an existing high manual limit does not silently reduce saved settings");
        }
        finally { host.Close(); }

        ((RadioButton)window.FindName("NavSettings")).IsChecked = true;
        var editor = (MemoryAllocationEditor)window.FindName("GlobalMemoryEditor");
        var originalAuto = editor.Automatic; var originalMemory = editor.ManualMemoryMb;
        editor.CustomOption.IsChecked = true; editor.MemorySlider.Value = 6144;
        check(store.Load().MemoryMb != 6144 && !store.Load().AutoMemory, "Dragging the global memory slider leaves persisted settings unchanged until Save");
        foreach (var size in new[] { (1240d, 830d), (1020d, 700d) })
        {
            window.Width = size.Item1; window.Height = size.Item2; window.UpdateLayout(); editor.BringIntoView(); window.UpdateLayout(); await Task.Delay(60);
            var slider = editor.MemorySlider; var custom = editor.CustomOption;
            check(slider.ActualWidth > 260 && custom.TranslatePoint(new Point(custom.ActualWidth, 0), editor).X <= slider.TranslatePoint(new Point(0, 0), editor).X,
                $"Memory options and slider retain separate readable columns at {(int)size.Item1}px");
            capture(window, "MemoryAllocation-Custom-" + (int)size.Item1);
            editor.AutomaticOption.IsChecked = true; window.UpdateLayout(); capture(window, "MemoryAllocation-Auto-" + (int)size.Item1); editor.CustomOption.IsChecked = true;
        }
        editor.ManualMemoryMb = originalMemory; editor.Automatic = originalAuto; window.Width = 1240; window.Height = 830;
        ((RadioButton)window.FindName("NavLibrary")).IsChecked = true;
    }
}
