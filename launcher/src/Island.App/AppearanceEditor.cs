using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Island.Core;
using Microsoft.Win32;

namespace Island.App;

public sealed class AppearanceEditor : StackPanel
{
    private readonly TextBlock title = new() { Name = "SelectedSurfaceTitle", FontSize = 18, FontWeight = FontWeights.SemiBold };
    private readonly List<RadioButton> materials = [];
    private readonly CheckBox followTheme = new() { Name = "FollowSurfaceTheme", Content = "跟随主题颜色", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox color = new() { Name = "SurfaceColorBox", MaxLength = 7, ToolTip = "背景颜色 #RRGGBB", Width = 150 };
    private readonly CheckBox autoForeground = new() { Name = "AutoSurfaceForeground", Content = "文字自动适配背景", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox foreground = new() { Name = "SurfaceForegroundBox", MaxLength = 7, ToolTip = "文字颜色 #RRGGBB", Width = 150 };
    private readonly StackPanel foregroundOptions = new() { Name = "SurfaceForegroundOptions", Margin = new Thickness(0, 12, 0, 0) };
    private readonly ColorPalette palette = new();
    private readonly Slider opacity = MakeSlider("SurfaceOpacity", 0, 100), blur = MakeSlider("SurfaceBlur", 0, 64), tint = MakeSlider("ImageTint", 0, 100), windowOpacity = MakeSlider("WindowOpacity", 40, 100);
    private readonly TextBlock opacityValue = new(), blurValue = new(), tintValue = new(), windowValue = new();
    private readonly StackPanel images = new(), wholeWindow = new(), materialTint = new();
    private readonly TextBlock imageName = new() { TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBlock status = new() { Name = "AppearanceStatus", Margin = new Thickness(0, 12, 0, 0) };
    private readonly Func<string?>? picker;
    private readonly Action changed;
    private LauncherAppearance draft;
    private SurfaceArea current;
    private bool loading;
    private string? validationError;
    private ResourceDictionary? previewTheme;
    public SurfaceArea SelectedArea => current;
    public AppearanceEditor(LauncherAppearance initial, Action onChanged, Action onSave, Func<string?>? imagePicker = null)
    {
        draft = initial.Normalize(); changed = onChanged; picker = imagePicker;
        System.Windows.Automation.AutomationProperties.SetName(foreground, "地图卡片文字颜色");
        Children.Add(title);
        Hint("点击上方预览切换区域。背景透明度和模糊仅影响底色，文字与图标保持清晰。");
        Children.Add(wholeWindow); AddSlider(wholeWindow, "窗口整体不透明度", windowOpacity, windowValue);
        windowOpacity.Value = draft.WindowOpacity * 100; windowValue.Text = $"{windowOpacity.Value:0}%";
        Label(this, "材质");
        var choices = new WrapPanel();
        foreach (var (name, value) in new[] { ("纯色 / 图片", SurfaceMaterial.Solid), ("高斯模糊", SurfaceMaterial.Gaussian), ("液态玻璃", SurfaceMaterial.LiquidGlass) })
        {
            var option = new RadioButton { Content = name, Tag = value, GroupName = "SurfaceMaterials", Style = (Style)FindResource("Filter"), MinWidth = 98 };
            option.Checked += (_, _) => Change(); materials.Add(option); choices.Children.Add(option);
        }
        Children.Add(choices);
        var colors = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(color, Dock.Right); colors.Children.Add(color); colors.Children.Add(followTheme); Children.Add(colors);
        Children.Add(palette);
        Label(foregroundOptions, "地图卡片文字");
        var textColors = new DockPanel(); DockPanel.SetDock(foreground, Dock.Right); textColors.Children.Add(foreground); textColors.Children.Add(autoForeground); foregroundOptions.Children.Add(textColors);
        var textChoices = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        AddButton(textChoices, "白色文字", (_, _) => SetForeground("#FFFFFF"));
        AddButton(textChoices, "深色文字", (_, _) => SetForeground("#22352E")); foregroundOptions.Children.Add(textChoices); Children.Add(foregroundOptions);
        palette.ColorSelected += selected =>
        {
            loading = true; followTheme.IsChecked = false; color.Text = selected; loading = false; Change();
        };
        AddSlider(this, "背景不透明度", opacity, opacityValue); AddSlider(this, "高斯模糊强度", blur, blurValue);
        Children.Add(images); Label(images, "背景图片"); images.Children.Add(imageName);
        var buttons = new WrapPanel(); AddButton(buttons, "选择背景图片", ChooseImage);
        AddButton(buttons, "使用纯色", (_, _) => { draft.For(current).ImagePath = ""; draft.For(current).DefaultIllustration = false; LoadSurface(); Notify(); });
        images.Children.Add(buttons); Children.Add(materialTint); AddSlider(materialTint, "图片 / 材质蒙层浓度", tint, tintValue);
        var reset = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
        AddButton(reset, "重置此区域", (_, _) => { draft.Set(current, new LauncherAppearance().For(current)); validationError = null; LoadSurface(); Notify(); });
        AddButton(reset, "重置全部背景与材质", (_, _) => { draft = new(); loading = true; windowOpacity.Value = 100; loading = false; validationError = null; LoadSurface(); Notify(); });
        AddButton(reset, "保存外观设置", (_, _) => onSave());
        ((Button)reset.Children[reset.Children.Count - 1]).Style = (Style)FindResource("Primary"); Children.Add(reset);
        status.Style = (Style)FindResource("Hint"); Children.Add(status);
        Hint("高斯模糊与液态玻璃使用应用内背景。液态玻璃包含磨砂透色、边缘折射与高光，可单独调整透明度。");
        followTheme.Checked += (_, _) => Change(); followTheme.Unchecked += (_, _) => Change(); color.TextChanged += (_, _) => Change();
        autoForeground.Checked += (_, _) => Change(); autoForeground.Unchecked += (_, _) => Change(); foreground.TextChanged += (_, _) => Change();
        opacity.ValueChanged += (_, _) => Change(); blur.ValueChanged += (_, _) => Change(); tint.ValueChanged += (_, _) => Change();
        windowOpacity.ValueChanged += (_, _) => { windowValue.Text = $"{windowOpacity.Value:0}%"; UpdateLabels(); if (!loading) { draft.WindowOpacity = windowOpacity.Value / 100; Notify(); } };
        LoadSurface();
    }
    public bool SelectArea(SurfaceArea area)
    {
        if (!StoreSurface()) return false;
        current = area; LoadSurface(); return true;
    }
    public LauncherAppearance Read()
    {
        if (!StoreSurface()) throw new ArgumentException(validationError);
        return draft.Normalize();
    }
    public void SetStatus(string description) { if (validationError is null) status.Text = description; }
    public void Load(LauncherAppearance appearance)
    {
        draft = appearance.Normalize(); validationError = null; loading = true; windowOpacity.Value = draft.WindowOpacity * 100; loading = false; LoadSurface();
    }
    private SurfaceMaterial Material => materials.FirstOrDefault(button => button.IsChecked == true)?.Tag is SurfaceMaterial value ? value : SurfaceMaterial.Solid;
    private void LoadSurface()
    {
        loading = true; title.Text = AppearancePreview.AreaName(current);
        var style = draft.For(current); foreach (var option in materials) option.IsChecked = (SurfaceMaterial)option.Tag == style.Material;
        followTheme.IsChecked = style.Color.Length == 0; color.Text = style.Color.Length == 0 ? ThemeSurfaceColor() : style.Color; color.IsEnabled = style.Color.Length > 0;
        palette.SetColor(color.Text, style.Color.Length > 0);
        autoForeground.IsChecked = style.ForegroundColor.Length == 0;
        foreground.Text = style.ForegroundColor.Length > 0 ? style.ForegroundColor : DefaultForeground(); foreground.IsEnabled = style.ForegroundColor.Length > 0;
        foregroundOptions.Visibility = current == SurfaceArea.RecentMap ? Visibility.Visible : Visibility.Collapsed;
        opacity.Value = style.Opacity * 100; blur.Value = style.BlurRadius; tint.Value = style.ImageTint * 100;
        wholeWindow.Visibility = current == SurfaceArea.Window ? Visibility.Visible : Visibility.Collapsed;
        images.Visibility = current is SurfaceArea.Window or SurfaceArea.Hero or SurfaceArea.RecentMap ? Visibility.Visible : Visibility.Collapsed;
        materialTint.Visibility = current is SurfaceArea.Window or SurfaceArea.Hero or SurfaceArea.RecentMap || Material == SurfaceMaterial.LiquidGlass ? Visibility.Visible : Visibility.Collapsed;
        imageName.Text = style.ImagePath.Length == 0 ? current == SurfaceArea.Hero && style.DefaultIllustration ? "默认山林插画" : "未选择图片，使用主题底色" : Path.GetFileName(style.ImagePath);
        imageName.ToolTip = style.ImagePath; UpdateLabels(); loading = false;
    }
    private bool StoreSurface()
    {
        if (loading) return true;
        var style = draft.For(current);
        try
        {
            var normalized = followTheme.IsChecked == true ? "" : AppearanceSettings.NormalizeColor(color.Text);
            if (current == SurfaceArea.RecentMap) style.ForegroundColor = autoForeground.IsChecked == true ? "" : AppearanceSettings.NormalizeColor(foreground.Text);
            style.Color = normalized; style.Material = Material; style.Opacity = opacity.Value / 100; style.BlurRadius = blur.Value; style.ImageTint = tint.Value / 100;
            validationError = null; return true;
        }
        catch (ArgumentException ex) { validationError = ex.Message; status.Text = ex.Message; return false; }
    }
    private void Change()
    {
        if (loading) return;
        color.IsEnabled = followTheme.IsChecked != true; UpdateLabels();
        foreground.IsEnabled = autoForeground.IsChecked != true;
        if (!StoreSurface()) return;
        if (followTheme.IsChecked == true) RefreshThemeColor(); else palette.SetColor(color.Text, true);
        Notify();
    }
    private string DefaultForeground()
    {
        var background = (Color)ColorConverter.ConvertFromString(color.Text.Length == 7 ? color.Text : "#243241");
        var candidate = ((previewTheme?["HeroText"] ?? TryFindResource("HeroText")) as SolidColorBrush)?.Color ?? Colors.White;
        var result = ThemeManager.Readable(candidate, background); return $"#{result.R:X2}{result.G:X2}{result.B:X2}";
    }
    private void SetForeground(string value)
    {
        loading = true; foreground.Text = value; autoForeground.IsChecked = false; loading = false; Change();
    }
    public void RefreshThemeColor(ResourceDictionary? theme = null)
    {
        if (theme is not null) previewTheme = theme;
        if (followTheme.IsChecked != true) return;
        var wasLoading = loading; loading = true;
        color.Text = ThemeSurfaceColor(); palette.SetColor(color.Text, false); loading = wasLoading;
    }
    private string ThemeSurfaceColor()
    {
        var resource = current switch { SurfaceArea.Window or SurfaceArea.Content => "WindowBackground", SurfaceArea.Hero => "HeroBackground", SurfaceArea.Sidebar => "SidebarBackground",
            SurfaceArea.LaunchBar => "AccentSoft", SurfaceArea.RecentMap => "HeroSurface", _ => "Surface" };
        var rgb = ((previewTheme?[resource] ?? TryFindResource(resource)) as SolidColorBrush)?.Color ?? Colors.White;
        return $"#{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}";
    }
    private void Notify() { if (!loading) changed(); }
    private void UpdateLabels()
    {
        opacityValue.Text = $"{opacity.Value:0}%"; blurValue.Text = $"{blur.Value:0}"; tintValue.Text = $"{tint.Value:0}%";
        materialTint.Visibility = current is SurfaceArea.Window or SurfaceArea.Hero or SurfaceArea.RecentMap || Material == SurfaceMaterial.LiquidGlass ? Visibility.Visible : Visibility.Collapsed;
        blur.IsEnabled = Material is SurfaceMaterial.Gaussian or SurfaceMaterial.LiquidGlass;
    }
    private void ChooseImage(object sender, RoutedEventArgs e)
    {
        if (!StoreSurface()) return;
        try
        {
            string? path;
            if (picker is not null) path = picker();
            else { var dialog = new OpenFileDialog { Title = "选择背景图片", Filter = "背景图片|*.png;*.jpg;*.jpeg;*.bmp" }; path = dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FileName : null; }
            if (path is null) return;
            BackgroundImages.Load(path); draft.For(current).ImagePath = Path.GetFullPath(path); validationError = null; LoadSurface(); Notify();
        }
        catch (Exception ex) { validationError = ex.Message; status.Text = ex.Message; }
    }
    private static Slider MakeSlider(string name, double min, double max) => new() { Name = name, Minimum = min, Maximum = max, TickFrequency = 1, IsSnapToTickEnabled = true, MinHeight = 24, VerticalAlignment = VerticalAlignment.Center };
    private static void Label(Panel panel, string text) => panel.Children.Add(new TextBlock { Text = text, Margin = new Thickness(0, 10, 0, 8) });
    private void Hint(string text) => Children.Add(new TextBlock { Text = text, Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 6, 0, 4) });
    private static void AddSlider(Panel panel, string text, Slider slider, TextBlock value)
    {
        Label(panel, text); var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        grid.Children.Add(slider); Grid.SetColumn(value, 1); value.HorizontalAlignment = HorizontalAlignment.Right; value.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(value); panel.Children.Add(grid);
    }
    private void AddButton(Panel panel, string text, RoutedEventHandler action)
    {
        var button = new Button { Content = text, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 8, 6) }; button.Click += action; panel.Children.Add(button);
    }
}
