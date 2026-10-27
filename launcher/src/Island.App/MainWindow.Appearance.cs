using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Island.Core;

namespace Island.App;

public partial class MainWindow
{
    private bool updatingTheme;
    private AppearanceEditor? appearanceEditor;
    private AppearancePreview? appearancePreview;
    private readonly Dictionary<Button, AppearancePreset> themeButtons = [];
    private static readonly (CardAnimationMode Mode, string Name, string Description)[] cardAnimations = [
        (CardAnimationMode.Off, "关", "保持卡片静止。"),
        (CardAnimationMode.FloatTilt, "悬浮跟随", "卡片轻微抬升、倾斜并跟随鼠标，保留现有动效。"),
        (CardAnimationMode.Lift, "轻盈上浮", "卡片平稳上浮，带有柔和阴影。"),
        (CardAnimationMode.Zoom, "柔和缩放", "从中心缓慢放大，不发生位移或倾斜。"),
        (CardAnimationMode.Spring, "弹性跃动", "卡片弹起后轻轻回落，带有弹性过渡。"),
        (CardAnimationMode.Magnetic, "磁吸跟随", "卡片在原位附近向鼠标方向轻移。")
    ];
    private readonly DispatcherTimer appearanceTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private void InitializeAppearance(Func<string?>? backgroundPicker)
    {
        updatingTheme = true;
        foreach (var choice in AppearancePresets.All)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            Brush swatch = choice.Glass ? new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#E74D8B"), (Color)ColorConverter.ConvertFromString(choice.Color), 45)
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString(choice.Color));
            content.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = swatch, Margin = new Thickness(0, 0, 7, 0) });
            content.Children.Add(new TextBlock { Text = choice.Name });
            var button = new Button { Name = "ThemePresetButton", Content = content, Tag = choice.Color, ToolTip = choice.Name + " · 点击应用并保存整套外观", Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 7, 7) };
            button.Click += (_, _) => ApplyAppearancePreset(choice);
            themeButtons.Add(button, choice); ThemePresetButtons.Children.Add(button);
        }
        ThemeColorBox.Text = state.ThemeColor; ThemeColorSample.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(state.ThemeColor)); UpdateThemeButtons();
        HideSidebarDollBox.IsChecked = state.Appearance.HideSidebarDoll;
        LaunchBarAlwaysBox.IsChecked = state.Appearance.LaunchBarMode == LaunchBarMode.Always;
        LaunchBarApproachBox.IsChecked = state.Appearance.LaunchBarMode == LaunchBarMode.OnApproach;
        foreach (var (mode, name, description) in cardAnimations)
        {
            var button = new RadioButton { Content = name, Tag = mode, GroupName = "CardAnimation", ToolTip = description,
                Style = (Style)FindResource("Filter"), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 8, 6) };
            button.Checked += ChangeLibraryEffects; CardAnimationChoices.Children.Add(button);
        }
        SelectCardAnimation(state.Appearance.CardAnimation);
        LaunchBarHost.Attach(LibraryPage, LibraryPage.RowDefinitions[3]);
        appearanceEditor = new AppearanceEditor(state.Appearance, QueueAppearancePreview, SaveAppearance, backgroundPicker);
        BackgroundAppearanceHost.Content = appearanceEditor;
        appearancePreview = new AppearancePreview(SelectAppearanceArea); AppearancePreviewHost.Content = appearancePreview;
        appearanceTimer.Tick += (_, _) => { appearanceTimer.Stop(); RefreshAppearancePreview(); };
        SourceInitialized += (_, _) => { ApplySavedAppearance(); RefreshAppearancePreview(); };
        Closed += (_, _) => appearanceTimer.Stop();
        updatingTheme = false;
    }
    private void UpdateThemeButtons()
    {
        foreach (var (button, choice) in themeButtons)
        {
            var selected = choice.Matches(state.Appearance, state.ThemeColor);
            button.BorderThickness = new Thickness(selected ? 2 : 1);
            if (selected) button.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(choice.Color));
            else button.SetResourceReference(Control.BorderBrushProperty, "Line");
        }
    }
    private void PreviewThemeColor(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (updatingTheme || ThemeColorStatus is null) return;
        try
        {
            var color = AppearanceSettings.NormalizeColor(ThemeColorBox.Text); RefreshAppearancePreview();
            ThemeColorSample.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            ThemeColorStatus.Text = "主题颜色仅在上方预览，保存后应用。";
            QuickAppearanceStatus.Text = "自定义微调尚未应用，点击外观页面的保存按钮后生效。";
        }
        catch (ArgumentException ex) { ThemeColorStatus.Text = ex.Message; QuickAppearanceStatus.Text = ex.Message; }
    }
    private void QueueAppearancePreview()
    {
        if (updatingTheme) return;
        appearanceTimer.Stop(); appearanceTimer.Start();
    }
    private void ApplySavedAppearance()
    {
        ThemeManager.Apply(state.ThemeColor, dark: state.Appearance.DarkTheme);
        var result = WindowMaterials.Apply(this, state.Appearance);
        AppearanceRenderer.NativeWindow = result.Native;
        AppearanceRenderer.Apply(state.Appearance, state.ThemeColor);
        WindowMaterials.ApplyCornerPreference(this);
        SidebarDoll.Visibility = state.Appearance.HideSidebarDoll ? Visibility.Collapsed : Visibility.Visible;
        LaunchBarHost.Apply(state.Appearance.LaunchBarMode);
        CardMotion.SetMode(InstanceList, state.Appearance.CardAnimation);
    }
    private void RefreshAppearancePreview()
    {
        try
        {
            var color = AppearanceSettings.NormalizeColor(ThemeColorBox.Text);
            var options = ReadAppearance();
            appearancePreview?.Update(options, color);
            appearanceEditor?.RefreshThemeColor(appearancePreview?.Resources);
            var warning = "";
            foreach (var path in new[] { options.Window.ImagePath, options.Hero.ImagePath, options.RecentMap.ImagePath }.Where(path => path.Length > 0))
                try { BackgroundImages.Load(path); }
                catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException or InvalidOperationException or FormatException or UnauthorizedAccessException) { warning = " 背景图片不可用，已使用主题背景。"; }
            appearanceEditor?.SetStatus("仅在上方预览，点击「保存外观设置」后应用到启动器。" + warning);
        }
        catch (ArgumentException) { }
    }
    private void SaveAppearance()
    {
        try
        {
            var color = AppearanceSettings.NormalizeColor(ThemeColorBox.Text);
            var appearance = BackgroundImages.Persist(ReadAppearance(), store.DirectoryPath);
            CommitAppearance(appearance, color); LoadAppearanceDraft();
            appearanceEditor?.SetStatus("外观设置已应用并保存，下次启动继续使用。");
            ThemeColorStatus.Text = "主题与外观设置已保存。";
            QuickAppearanceStatus.Text = "自定义外观已应用并保存。预设主题可点击直接切换。";
        }
        catch (Exception ex) { Report(ex); }
    }
    private void CommitAppearance(LauncherAppearance appearance, string color)
    {
        var previous = state.Appearance; var previousColor = state.ThemeColor;
        state.Appearance = appearance; state.ThemeColor = color;
        try { store.Save(state); }
        catch { state.Appearance = previous; state.ThemeColor = previousColor; throw; }
        appearanceTimer.Stop(); ApplySavedAppearance(); UpdateThemeButtons();
    }
    private void LoadAppearanceDraft()
    {
        updatingTheme = true;
        try
        {
            ThemeColorBox.Text = state.ThemeColor;
            ThemeColorSample.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(state.ThemeColor));
            HideSidebarDollBox.IsChecked = state.Appearance.HideSidebarDoll;
            LaunchBarAlwaysBox.IsChecked = state.Appearance.LaunchBarMode == LaunchBarMode.Always;
            LaunchBarApproachBox.IsChecked = state.Appearance.LaunchBarMode == LaunchBarMode.OnApproach;
            SelectCardAnimation(state.Appearance.CardAnimation);
            appearanceEditor?.Load(state.Appearance);
        }
        finally { updatingTheme = false; }
        RefreshAppearancePreview();
    }
    private void ApplyAppearancePreset(AppearancePreset preset)
    {
        try
        {
            CommitAppearance(preset.Create(store.DirectoryPath, state.Appearance.HideSidebarDoll, state.Appearance.LaunchBarMode, state.Appearance.CardAnimation), preset.Color);
            LoadAppearanceDraft();
            QuickAppearanceStatus.Text = "已启用并保存「" + preset.Name + "」。自定义微调可打开外观设置。";
            ThemeColorStatus.Text = "预设已应用，可继续微调，保存后生效。";
        }
        catch (Exception ex) { QuickAppearanceStatus.Text = "预设未应用：" + ex.Message; Report(ex); }
    }
    private bool SelectAppearanceArea(SurfaceArea area)
    {
        if (appearanceEditor?.SelectArea(area) != true) return false;
        ThemeSettingsSection.Visibility = area == SurfaceArea.Window ? Visibility.Visible : Visibility.Collapsed;
        AppearanceSettingsScroll.ScrollToTop(); return true;
    }
    private void OpenAppearance(object sender, RoutedEventArgs e)
    {
        NavSettings.IsChecked = true;
        LibraryPage.Visibility = AccountsPage.Visibility = LogsPage.Visibility = SettingsPage.Visibility = Visibility.Collapsed;
        AppearancePage.Visibility = Visibility.Visible;
        AppearanceSettingsScroll.ScrollToTop();
    }
    private void EditRecentMapAppearance(object sender, RoutedEventArgs e)
    {
        OpenAppearance(sender, e);
        if (SelectAppearanceArea(SurfaceArea.RecentMap)) appearancePreview?.Highlight(SurfaceArea.RecentMap);
    }
    private void ReturnToSettings(object sender, RoutedEventArgs e)
    {
        AppearancePage.Visibility = Visibility.Collapsed; SettingsPage.Visibility = Visibility.Visible; NavSettings.IsChecked = true;
    }
    private void SaveAppearanceClick(object sender, RoutedEventArgs e) => SaveAppearance();
    private LauncherAppearance ReadAppearance()
    {
        var appearance = appearanceEditor?.Read() ?? state.Appearance.Normalize();
        appearance.HideSidebarDoll = HideSidebarDollBox.IsChecked == true;
        appearance.LaunchBarMode = state.Appearance.LaunchBarMode; appearance.CardAnimation = state.Appearance.CardAnimation; return appearance;
    }
    private void SelectCardAnimation(CardAnimationMode mode)
    {
        foreach (var button in CardAnimationChoices.Children.OfType<RadioButton>()) button.IsChecked = (CardAnimationMode)button.Tag == mode;
        CardAnimationDescription.Text = cardAnimations.Single(choice => choice.Mode == mode).Description;
    }
    private void ChangeLibraryEffects(object sender, RoutedEventArgs e)
    {
        if (updatingTheme || CardAnimationChoices is null || LaunchBarHost is null || !ready) return;
        try
        {
            var appearance = state.Appearance.Normalize();
            appearance.CardAnimation = CardAnimationChoices.Children.OfType<RadioButton>().Where(button => button.IsChecked == true)
                .Select(button => (CardAnimationMode)button.Tag).DefaultIfEmpty(state.Appearance.CardAnimation).First();
            appearance.LaunchBarMode = LaunchBarApproachBox.IsChecked == true ? LaunchBarMode.OnApproach : LaunchBarMode.Always;
            CommitAppearance(appearance, state.ThemeColor); RefreshAppearancePreview();
            CardAnimationDescription.Text = cardAnimations.Single(choice => choice.Mode == appearance.CardAnimation).Description;
            QuickAppearanceStatus.Text = "启动栏与卡片动画已应用并保存。";
        }
        catch (Exception ex)
        {
            updatingTheme = true;
            SelectCardAnimation(state.Appearance.CardAnimation);
            LaunchBarAlwaysBox.IsChecked = state.Appearance.LaunchBarMode == LaunchBarMode.Always;
            LaunchBarApproachBox.IsChecked = state.Appearance.LaunchBarMode == LaunchBarMode.OnApproach;
            updatingTheme = false; Report(ex);
        }
    }
    private void ChangeSidebarDollVisibility(object sender, RoutedEventArgs e)
    {
        if (SidebarDoll is null || HideSidebarDollBox is null) return;
        if (!updatingTheme && QuickAppearanceStatus is not null)
        {
            try
            {
                var appearance = state.Appearance.Normalize(); appearance.HideSidebarDoll = HideSidebarDollBox.IsChecked == true;
                CommitAppearance(appearance, state.ThemeColor); RefreshAppearancePreview();
                QuickAppearanceStatus.Text = "纸娃娃显示设置已应用并保存。";
            }
            catch (Exception ex)
            {
                updatingTheme = true; HideSidebarDollBox.IsChecked = state.Appearance.HideSidebarDoll; updatingTheme = false;
                QuickAppearanceStatus.Text = "显示设置未应用：" + ex.Message; Report(ex);
            }
        }
    }
}
