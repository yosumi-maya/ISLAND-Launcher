using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Island.Core;

namespace Island.App;

public sealed class ColorPalette : StackPanel
{
    private readonly List<Button> presets = [];
    private readonly Border picker;
    private readonly Button toggle;
    private readonly Grid spectrum = new() { Name = "ColorSpectrum", MinHeight = 156, Focusable = true, ClipToBounds = true, Cursor = Cursors.Cross };
    private readonly Canvas markerLayer = new() { IsHitTestVisible = false };
    private readonly Border spectrumFocus = new() { IsHitTestVisible = false };
    private readonly Border marker = new() { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), BorderBrush = Brushes.Black, BorderThickness = new Thickness(1),
        Child = new Ellipse { Stroke = Brushes.White, StrokeThickness = 2 } };
    private readonly Slider hue = Slider("PaletteHue", 360), saturation = Slider("PaletteSaturation", 100), brightness = Slider("PaletteBrightness", 100);
    private readonly TextBlock hueLabel = new(), saturationLabel = new(), brightnessLabel = new();
    private bool updating, custom;
    public string SelectedColor { get; private set; } = "";
    public event Action<string>? ColorSelected;

    public ColorPalette()
    {
        Margin = new Thickness(0, 10, 0, 0);
        Children.Add(new TextBlock { Text = "常用颜色", FontSize = 12, Margin = new Thickness(0, 0, 0, 7) });
        var choices = new WrapPanel(); Children.Add(choices);
        foreach (var (name, hex) in new[] {
            ("纯白", "#FFFFFF"), ("浅灰", "#F3F4F6"), ("石墨灰", "#52616B"), ("墨黑", "#17202A"),
            ("森林绿", "#32634D"), ("薄荷绿", "#75BFA2"), ("海洋蓝", "#32699B"), ("天蓝", "#72B4E0"),
            ("鸢尾紫", "#7856A4"), ("玫瑰红", "#AD526E"), ("珊瑚红", "#D66A5E"), ("暖琥珀", "#9B6C29") })
        {
            var dot = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Brush(hex), BorderThickness = new Thickness(1) };
            dot.SetResourceReference(Border.BorderBrushProperty, "Line");
            var button = new Button { Name = "SurfaceColorPreset", Tag = hex, Content = dot, Width = 36, Height = 36, Padding = new Thickness(5),
                Margin = new Thickness(0, 0, 6, 6), ToolTip = name + " · " + hex };
            AutomationProperties.SetName(button, name + " " + hex);
            button.Click += (_, _) => { SetColor(hex, true); ColorSelected?.Invoke(hex); };
            presets.Add(button); choices.Children.Add(button);
        }
        var toggleContent = new StackPanel { Orientation = Orientation.Horizontal };
        toggleContent.Children.Add(new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = HueBrush(), Margin = new Thickness(0, 0, 7, 0) });
        toggleContent.Children.Add(new TextBlock { Text = "调色盘", VerticalAlignment = VerticalAlignment.Center });
        toggle = new Button { Name = "ColorPaletteToggle", Content = toggleContent, Padding = new Thickness(10, 7, 10, 7), MinHeight = 36, Margin = new Thickness(0, 0, 0, 6) };
        AutomationProperties.SetName(toggle, "展开调色盘"); choices.Children.Add(toggle);

        var layout = new Grid(); layout.ColumnDefinitions.Add(new ColumnDefinition()); layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        spectrum.Children.Add(new Border { Background = new LinearGradientBrush(Colors.White, Colors.Transparent, 0) });
        spectrum.Children.Add(new Border { Background = new LinearGradientBrush(Colors.Transparent, Colors.Black, 90) });
        spectrumFocus.SetResourceReference(Border.BorderBrushProperty, "Focus"); spectrum.Children.Add(spectrumFocus);
        markerLayer.Children.Add(marker); spectrum.Children.Add(markerLayer); layout.Children.Add(spectrum);
        AutomationProperties.SetName(spectrum, "饱和度与亮度调色区域");
        spectrum.ToolTip = "拖动选择颜色；方向键微调，按住 Shift 加快。也可使用右侧滑块。";
        spectrum.SizeChanged += (_, _) => UpdateMarker();
        spectrum.MouseLeftButtonDown += (_, e) => { spectrum.Focus(); spectrum.CaptureMouse(); PickPoint(e.GetPosition(spectrum)); e.Handled = true; };
        spectrum.MouseMove += (_, e) => { if (spectrum.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) PickPoint(e.GetPosition(spectrum)); };
        spectrum.MouseLeftButtonUp += (_, e) => { if (spectrum.IsMouseCaptured) { PickPoint(e.GetPosition(spectrum)); spectrum.ReleaseMouseCapture(); e.Handled = true; } };
        spectrum.KeyDown += (_, e) =>
        {
            var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            if (e.Key == Key.Left) saturation.Value -= step;
            else if (e.Key == Key.Right) saturation.Value += step;
            else if (e.Key == Key.Up) brightness.Value += step;
            else if (e.Key == Key.Down) brightness.Value -= step;
            else return;
            e.Handled = true;
        };
        spectrum.GotKeyboardFocus += (_, _) => spectrumFocus.BorderThickness = new Thickness(2);
        spectrum.LostKeyboardFocus += (_, _) => spectrumFocus.BorderThickness = new Thickness(0);
        var sliders = new StackPanel { Margin = new Thickness(14, 0, 0, 0) }; Grid.SetColumn(sliders, 1); layout.Children.Add(sliders);
        AddSlider(sliders, hueLabel, hue, HueBrush()); AddSlider(sliders, saturationLabel, saturation); AddSlider(sliders, brightnessLabel, brightness);
        picker = new Border { Name = "ColorPalettePanel", Child = layout, CornerRadius = new CornerRadius(8), Padding = new Thickness(12), BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 2, 0, 4), Visibility = Visibility.Collapsed };
        picker.SetResourceReference(Border.BorderBrushProperty, "Line"); picker.SetResourceReference(Border.BackgroundProperty, "Surface"); Children.Add(picker);
        toggle.Click += (_, _) =>
        {
            var open = picker.Visibility != Visibility.Visible;
            picker.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            ((TextBlock)toggleContent.Children[1]).Text = open ? "收起调色盘" : "调色盘";
            AutomationProperties.SetName(toggle, open ? "收起调色盘" : "展开调色盘");
            toggle.SetResourceReference(Control.BorderBrushProperty, open ? "Accent" : "Line");
            if (open) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                if (picker.Visibility == Visibility.Visible) picker.BringIntoView();
            }));
        };
        hue.ValueChanged += (_, _) => SelectHsv(); saturation.ValueChanged += (_, _) => SelectHsv(); brightness.ValueChanged += (_, _) => SelectHsv();
        SetColor(AppearanceSettings.DefaultColor, false);
    }
    public void SetColor(string value, bool isCustom)
    {
        var hex = AppearanceSettings.NormalizeColor(value); custom = isCustom;
        if (hex != SelectedColor)
        {
            var rgb = (Color)ColorConverter.ConvertFromString(hex);
            var r = rgb.R / 255d; var g = rgb.G / 255d; var b = rgb.B / 255d;
            var max = Math.Max(r, Math.Max(g, b)); var delta = max - Math.Min(r, Math.Min(g, b));
            updating = true;
            if (delta > 0)
            {
                var h = max == r ? (g - b) / delta : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4;
                hue.Value = (h * 60 + 360) % 360;
            }
            saturation.Value = max == 0 ? 0 : delta / max * 100; brightness.Value = max * 100;
            updating = false; SelectedColor = hex;
        }
        UpdateVisuals();
    }
    private void SelectHsv()
    {
        if (updating) return;
        SelectedColor = Hex(FromHsv(hue.Value, saturation.Value / 100, brightness.Value / 100)); custom = true;
        UpdateVisuals(); ColorSelected?.Invoke(SelectedColor);
    }
    private void PickPoint(Point point)
    {
        if (spectrum.ActualWidth <= 0 || spectrum.ActualHeight <= 0) return;
        updating = true;
        saturation.Value = Math.Clamp(point.X / spectrum.ActualWidth, 0, 1) * 100;
        brightness.Value = (1 - Math.Clamp(point.Y / spectrum.ActualHeight, 0, 1)) * 100;
        updating = false; SelectHsv();
    }
    private void UpdateVisuals()
    {
        spectrum.Background = new SolidColorBrush(FromHsv(hue.Value, 1, 1));
        hueLabel.Text = $"色相  {hue.Value:0}°"; saturationLabel.Text = $"饱和度  {saturation.Value:0}%"; brightnessLabel.Text = $"亮度  {brightness.Value:0}%";
        foreach (var preset in presets)
        {
            var selected = custom && preset.Tag as string == SelectedColor;
            preset.BorderThickness = new Thickness(selected ? 2 : 1);
            preset.SetResourceReference(Control.BorderBrushProperty, selected ? "Accent" : "Line");
            preset.SetResourceReference(Control.BackgroundProperty, selected ? "AccentSoft" : "Surface");
            AutomationProperties.SetItemStatus(preset, selected ? "已选择" : "未选择");
        }
        UpdateMarker();
    }
    private void UpdateMarker()
    {
        Canvas.SetLeft(marker, saturation.Value / 100 * spectrum.ActualWidth - marker.Width / 2);
        Canvas.SetTop(marker, (1 - brightness.Value / 100) * spectrum.ActualHeight - marker.Height / 2);
    }
    private static Color FromHsv(double hue, double saturation, double value)
    {
        var chroma = value * saturation; var h = hue / 60; var x = chroma * (1 - Math.Abs(h % 2 - 1)); var m = value - chroma;
        var (r, g, b) = h switch { < 1 => (chroma, x, 0d), < 2 => (x, chroma, 0d), < 3 => (0d, chroma, x), < 4 => (0d, x, chroma), < 5 => (x, 0d, chroma), _ => (chroma, 0d, x) };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
    private static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    private static Brush Brush(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    private static LinearGradientBrush HueBrush() => new(new GradientStopCollection {
        new(Colors.Red, 0), new(Colors.Yellow, 1d / 6), new(Colors.Lime, 2d / 6), new(Colors.Cyan, .5), new(Colors.Blue, 4d / 6), new(Colors.Magenta, 5d / 6), new(Colors.Red, 1) }, 0);
    private static Slider Slider(string name, double max)
    {
        var slider = new Slider { Name = name, Minimum = 0, Maximum = max, SmallChange = 1, LargeChange = 10, MinHeight = 24, IsMoveToPointEnabled = true };
        AutomationProperties.SetName(slider, name switch { "PaletteHue" => "色相", "PaletteSaturation" => "饱和度", _ => "亮度" }); return slider;
    }
    private static void AddSlider(Panel parent, TextBlock label, Slider slider, Brush? background = null)
    {
        label.FontSize = 12; label.Margin = new Thickness(0, 0, 0, 2); parent.Children.Add(label);
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        if (background is not null) row.Children.Add(new Border { Background = background, Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(5, 0, 5, 0) });
        row.Children.Add(slider); parent.Children.Add(row);
    }
}
