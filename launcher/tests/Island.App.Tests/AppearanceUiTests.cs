using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Island.App;
using Island.Core;

internal static class AppearanceUiTests
{
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (var next in Children<T>(child)) yield return next;
        }
    }
    private static T Control<T>(DependencyObject root, string name) where T : FrameworkElement => Children<T>(root).Single(control => control.Name == name);
    private static void Click(DependencyObject root, string text) =>
        Children<Button>(root).First(button => button.Content as string == text).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static async Task Preview(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!predicate() && DateTime.UtcNow < deadline) await Task.Delay(30);
        if (!predicate()) throw new Exception("Appearance preview did not finish");
    }
    public static string MakeImage(string root)
    {
        var image = new DrawingVisual();
        using (var dc = image.RenderOpen())
            for (var y = 0; y < 120; y += 8)
                for (var x = 0; x < 160; x += 8)
                    dc.DrawRectangle(((x + y) / 8 % 2) == 0 ? Brushes.CornflowerBlue : Brushes.LightGoldenrodYellow, null, new Rect(x, y, 8, 8));
        var bitmap = new RenderTargetBitmap(160, 120, 96, 96, PixelFormats.Pbgra32); bitmap.Render(image);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(root, "背景 测试.png"); using var file = File.Create(path); encoder.Save(file); return path;
    }
    public static async Task Run(MainWindow window, LauncherStore store, string sourceImage, Action<bool, string> check, Action<Window, string> capture)
    {
        ((RadioButton)window.FindName("NavSettings")).IsChecked = true; window.UpdateLayout();
        var settings = (ScrollViewer)window.FindName("SettingsPage");
        check(!Children<AppearanceEditor>(settings).Any() && window.FindName("OpenAppearanceButton") is Button && Children<WrapPanel>(settings).Any(panel => panel.Name == "ThemePresetButtons"),
            "Launcher settings contain theme presets and the entry to the full appearance editor");
        await VerifyQuickSettings(window, store, check, capture);
        Click(settings, "打开外观设置  →"); window.UpdateLayout();
        var page = (Grid)window.FindName("AppearancePage");
        check(page.Visibility == Visibility.Visible && settings.Visibility == Visibility.Collapsed && !Children<Button>(page).Any(button => button.Name == "ThemePresetButton"),
            "Appearance button opens the dedicated editor without duplicating quick theme presets");
        var editor = (AppearanceEditor)((ContentControl)window.FindName("BackgroundAppearanceHost")).Content;
        var preview = (AppearancePreview)((ContentControl)window.FindName("AppearancePreviewHost")).Content;
        var draft = preview.RenderContext;
        void Select(SurfaceArea area)
        {
            var button = Children<Button>(preview).First(button => button.Tag is SurfaceArea tag && tag == area);
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); window.UpdateLayout();
        }
        void Material(SurfaceMaterial value) => Children<RadioButton>(editor).Single(button => button.Tag is SurfaceMaterial tag && tag == value).IsChecked = true;
        var committedBackground = AppearanceRenderer.Current.Window.Normalize();
        var committedTheme = AppearanceRenderer.ThemeColor;
        Select(SurfaceArea.Window);
        Control<CheckBox>(editor, "FollowSurfaceTheme").IsChecked = false;
        Control<TextBox>(editor, "SurfaceColorBox").Text = "#72B4E0"; Material(SurfaceMaterial.Gaussian);
        Control<Slider>(editor, "SurfaceBlur").Value = 64;
        Control<Slider>(editor, "WindowOpacity").Value = 70;
        await Preview(() => draft.Current.Window.Color == "#72B4E0" && draft.Current.WindowOpacity == .7);
        check(AppearanceRenderer.Current.Window == committedBackground && AppearanceRenderer.ThemeColor == committedTheme
            && Math.Abs(WindowMaterials.ReadOpacity(window) - 1) < .01
            && Children<MaterialBorder>(preview).All(border => ReferenceEquals(border.RenderContext, draft)),
            "A blue blurred draft and 70% window alpha stay inside the miniature's independent rendering context");
        foreach (var size in new[] { (1020d, 700d), (1240d, 830d) })
        {
            window.Width = size.Item1; window.Height = size.Item2; window.UpdateLayout();
            capture(window, "UnsavedPreview-" + (int)size.Item1);
        }
        var saveFailureShown = false;
        using (var lockedSettings = new FileStream(Path.Combine(store.DirectoryPath, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            _ = window.Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var notice in Application.Current.Windows.OfType<MessageWindow>().Where(dialog => dialog.Owner == window).ToArray())
                { saveFailureShown = true; notice.Close(); }
            }));
            Click(editor, "保存外观设置");
        }
        check(saveFailureShown && AppearanceRenderer.Current.Window == committedBackground && store.Load().Appearance.Window == committedBackground
            && Math.Abs(WindowMaterials.ReadOpacity(window) - 1) < .01,
            "A failed settings write keeps the previous live appearance and saved configuration");
        ((RadioButton)window.FindName("NavLibrary")).IsChecked = true; window.UpdateLayout(); await Task.Delay(180);
        check(AppearanceRenderer.Current.Window == committedBackground && draft.Current.Window.Color == "#72B4E0",
            "Leaving the editor retains the draft without applying it to the launcher");
        Click(settings, "打开外观设置  →"); window.UpdateLayout();
        Click(editor, "重置全部背景与材质");
        await Preview(() => draft.Current.Window.Material == SurfaceMaterial.Solid && draft.Current.WindowOpacity == 1);
        Select(SurfaceArea.Instances);
        var palette = Children<ColorPalette>(editor).Single();
        Children<Button>(palette).Single(button => button.Name == "SurfaceColorPreset" && button.Tag as string == "#AD526E").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Preview(() => draft.Current.Instances.Color == "#AD526E");
        check(AppearanceRenderer.Current.Instances.Color == store.Load().Appearance.Instances.Color
            && AppearanceRenderer.Current.Instances.Color != "#AD526E", "Picking a surface color changes the miniature while leaving the launcher and saved settings untouched");
        check(Control<CheckBox>(editor, "FollowSurfaceTheme").IsChecked == false && Control<TextBox>(editor, "SurfaceColorBox").Text == "#AD526E",
            "Picking a common color leaves theme-following mode and updates the preview surface");
        Control<Button>(palette, "ColorPaletteToggle").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); window.UpdateLayout();
        Control<Slider>(palette, "PaletteHue").Value = 210; Control<Slider>(palette, "PaletteSaturation").Value = 80; Control<Slider>(palette, "PaletteBrightness").Value = 80;
        await Preview(() => draft.Current.Instances.Color == "#297ACC");
        check(palette.SelectedColor == "#297ACC" && Control<TextBox>(editor, "SurfaceColorBox").Text == "#297ACC", "Color palette controls synchronize with the hex field and preview");
        var spectrum = Control<Grid>(palette, "ColorSpectrum");
        spectrum.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(spectrum)!, Environment.TickCount, System.Windows.Input.Key.Up)
            { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
        await Preview(() => draft.Current.Instances.Color == palette.SelectedColor);
        check(Control<Slider>(palette, "PaletteBrightness").Value == 81, "Color spectrum supports keyboard adjustment without dragging");
        Control<Slider>(palette, "PaletteBrightness").Value = 0;
        Control<Slider>(palette, "PaletteHue").Value = 120; Control<Slider>(palette, "PaletteSaturation").Value = 100;
        Control<Slider>(palette, "PaletteBrightness").Value = 100;
        await Preview(() => draft.Current.Instances.Color == "#00FF00");
        check(palette.SelectedColor == "#00FF00", "Hue and saturation remain adjustable while brightness is black");
        Control<TextBox>(editor, "SurfaceColorBox").Text = "#38bdf8";
        await Preview(() => palette.SelectedColor == "#38BDF8" && draft.Current.Instances.Color == "#38BDF8");
        Select(SurfaceArea.Hero); Select(SurfaceArea.Instances);
        check(palette.SelectedColor == "#38BDF8", "Switching preview regions restores the palette for that region");
        foreach (var size in new[] { (1020d, 700d), (1240d, 830d) })
        {
            window.Width = size.Item1; window.Height = size.Item2;
            window.UpdateLayout(); Control<Border>(palette, "ColorPalettePanel").BringIntoView(); window.UpdateLayout();
            var spectrumEdge = spectrum.TransformToAncestor(editor).Transform(new Point(spectrum.ActualWidth, 0)).X;
            var settingsScroll = (ScrollViewer)window.FindName("AppearanceSettingsScroll");
            var paletteBottom = spectrum.TransformToAncestor(settingsScroll).Transform(new Point(0, spectrum.ActualHeight)).Y;
            check(spectrum.ActualWidth > 200 && spectrumEdge < editor.ActualWidth && Control<Slider>(palette, "PaletteBrightness").ActualWidth > 100,
                $"Expanded color palette fits the settings column at {(int)size.Item1}px");
            check(paletteBottom <= settingsScroll.ActualHeight, $"Color palette can be brought fully into view at {(int)size.Item1}px");
            capture(window, "ColorPalette-" + (int)size.Item1);
        }
        Click(editor, "保存外观设置");
        check(store.Load().Appearance.Instances.Color == "#38BDF8" && store.Load().Appearance.Hero.Color.Length == 0, "Saving a palette color changes only the selected surface");
        Control<CheckBox>(editor, "FollowSurfaceTheme").IsChecked = true;
        await Preview(() => draft.Current.Instances.Color.Length == 0);
        check(Control<TextBox>(editor, "SurfaceColorBox").Text == "#FFFFFF" && !Control<TextBox>(editor, "SurfaceColorBox").IsEnabled,
            "Following the theme restores the actual surface color instead of a fixed green value");
        Click(editor, "重置此区域"); Click(editor, "保存外观设置");
        Control<Button>(palette, "ColorPaletteToggle").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); window.UpdateLayout();
        check(!Children<ComboBox>(page).Any() && Children<Button>(preview).Select(button => button.Tag).Distinct().Count() == 8 && Children<RadioButton>(editor).Count(button => button.Tag is SurfaceMaterial) == 3,
            "Appearance uses eight clickable preview regions and three material buttons without dropdowns");
        foreach (var area in Enum.GetValues<SurfaceArea>())
        {
            Select(area);
            check(editor.SelectedArea == area && preview.SelectedArea == area && Control<TextBlock>(editor, "SelectedSurfaceTitle").Text == AppearancePreview.AreaName(area),
                "Preview selection shows the matching settings: " + area);
        }
        Select(SurfaceArea.Content); Material(SurfaceMaterial.Gaussian);
        Control<Slider>(editor, "SurfaceOpacity").Value = 82;
        await Preview(() => draft.Current.Content.Material == SurfaceMaterial.Gaussian && draft.Current.Content.Opacity == .82);
        check(AppearanceRenderer.Current.Content.Material == SurfaceMaterial.Solid && AppearanceRenderer.Current.Window == committedBackground,
            "The content module has an independent unsaved material without changing the outer window background");
        Click(editor, "保存外观设置");
        check(store.Load().Appearance.Content.Material == SurfaceMaterial.Gaussian && AppearanceRenderer.Current.Content.Opacity == .82,
            "Saving applies and persists the content module independently");
        Click(editor, "重置此区域"); Click(editor, "保存外观设置");
        var mapButton = Children<Button>(preview).Single(button => button.Tag is SurfaceArea.RecentMap);
        var hitPoint = mapButton.TransformToAncestor(preview).Transform(new Point(mapButton.ActualWidth / 2, mapButton.ActualHeight / 2));
        var hit = preview.InputHitTest(hitPoint) as DependencyObject;
        while (hit is not null && hit is not Button) hit = VisualTreeHelper.GetParent(hit);
        check(hit == mapButton, "Nested map preview receives clicks ahead of the hero and window backdrop");
        foreach (var size in new[] { (1020d, 700d), (1240d, 830d) })
        {
            window.Width = size.Item1; window.Height = size.Item2; window.UpdateLayout();
            check(Children<RadioButton>(editor).Where(button => button.Tag is SurfaceMaterial).All(button =>
                button.TransformToAncestor(editor).Transform(new Point(button.ActualWidth, 0)).X <= editor.ActualWidth + 1),
                $"All material choices wrap within the settings column at {(int)size.Item1}px");
            var save = Children<Button>(page).First(button => button.Content as string == "保存外观设置");
            check(((ScrollViewer)window.FindName("AppearanceSettingsScroll")).ActualHeight > 180 &&
                save.TransformToAncestor(window).Transform(new Point(0, save.ActualHeight)).Y < window.ActualHeight &&
                preview.ActualHeight > 200, $"Appearance preview and save action stay visible at {(int)size.Item1}px");
            capture(window, "PreviewSelection-" + (int)size.Item1);
        }
        var previewBounds = (FrameworkElement)window.FindName("AppearancePreviewHost");
        var previewPosition = previewBounds.TransformToAncestor(window).Transform(new Point());
        ((ScrollViewer)window.FindName("AppearanceSettingsScroll")).ScrollToBottom(); window.UpdateLayout();
        check(previewBounds.TransformToAncestor(window).Transform(new Point()) == previewPosition, "Scrolling lower settings keeps the clickable preview visible");
        Select(SurfaceArea.Window); Click(editor, "选择背景图片");
        await Preview(() => draft.Current.Window.ImagePath == sourceImage);
        Select(SurfaceArea.Hero); Material(SurfaceMaterial.Gaussian);
        Control<Slider>(editor, "SurfaceBlur").Value = 16;
        Click(editor, "选择背景图片");
        await Preview(() => draft.Current.Hero.ImagePath == sourceImage && draft.Current.Hero.BlurRadius == 16);
        Select(SurfaceArea.Instances);
        Control<CheckBox>(editor, "FollowSurfaceTheme").IsChecked = false;
        Control<TextBox>(editor, "SurfaceColorBox").Text = "#243241";
        Control<Slider>(editor, "SurfaceOpacity").Value = 62; Material(SurfaceMaterial.Gaussian);
        await Preview(() => draft.Current.Instances.Opacity == .62 && draft.Current.Instances.Material == SurfaceMaterial.Gaussian);
        check(draft.Current.Hero.Material == SurfaceMaterial.Gaussian && draft.Current.Window.Material == SurfaceMaterial.Solid,
            "Region changes preview independently without overwriting other surfaces");
        Select(SurfaceArea.Window); Control<Slider>(editor, "WindowOpacity").Value = 65;
        await Preview(() => draft.Current.WindowOpacity == .65);
        check(Math.Abs(WindowMaterials.ReadOpacity(window) - 1) < .01 && AppearanceRenderer.Current.Window.ImagePath.Length == 0
            && store.Load().Appearance.WindowOpacity == 1 && Children<Canvas>(preview).First().Opacity == .65,
            "Unsaved window alpha fades only the miniature, and unsaved images do not reach the live launcher");
        Click(editor, "保存外观设置");
        await Preview(() => Math.Abs(WindowMaterials.ReadOpacity(window) - .65) < .01);
        check(AppearanceRenderer.Current.Window.ImagePath.Length > 0, "Saving applies the draft images and native window alpha together");
        window.Width = 1020; window.Height = 700; window.UpdateLayout(); await Task.Delay(100);
        check(Math.Abs(WindowMaterials.ReadOpacity(window) - .65) < .01, "Resizing the WPF window preserves the native transparency setting");
        check(WindowOutlineUiTests.HasRoundedRegion(window), "Whole-window transparency and resizing preserve the rounded native window region");
        Click(editor, "保存外观设置");
        var saved = store.Load().Appearance;
        check(saved.WindowOpacity == .65 && saved.Instances.Opacity == .62 && saved.Instances.Color == "#243241", "Dedicated appearance save persists opacity and per-region colors without runtime form validation");
        check(saved.Window.ImagePath != sourceImage && File.Exists(saved.Window.ImagePath) && saved.Hero.ImagePath == saved.Window.ImagePath,
            "Saving imports background images into the launcher cache and deduplicates identical files");
        check(File.ReadAllBytes(saved.Window.ImagePath).SequenceEqual(File.ReadAllBytes(sourceImage)), "Imported background retains the original image bytes");
        File.Move(sourceImage, sourceImage + ".moved");
        check(BackgroundImages.Load(saved.Window.ImagePath).PixelWidth == 160, "Moving the original image leaves the saved background usable");
        Select(SurfaceArea.Panels); Control<Slider>(editor, "SurfaceOpacity").Value = 45;
        Material(SurfaceMaterial.LiquidGlass);
        check(Control<Slider>(editor, "SurfaceBlur").IsEnabled, "Glass exposes adjustable blur");
        Select(SurfaceArea.Sidebar); Material(SurfaceMaterial.Gaussian); Control<Slider>(editor, "SurfaceOpacity").Value = 70;
        check(Control<Slider>(editor, "SurfaceBlur").IsEnabled, "Gaussian exposes adjustable blur");
        Select(SurfaceArea.LaunchBar); Material(SurfaceMaterial.LiquidGlass); Control<Slider>(editor, "SurfaceOpacity").Value = 60;
        check(Control<Slider>(editor, "SurfaceBlur").IsEnabled, "Liquid glass exposes adjustable blur");
        Select(SurfaceArea.RecentMap); Material(SurfaceMaterial.Gaussian);
        Control<Slider>(editor, "SurfaceOpacity").Value = 75;
        Click(editor, "保存外观设置");
        saved = store.Load().Appearance;
        check(saved.Panels.Material == SurfaceMaterial.LiquidGlass && saved.Sidebar.Material == SurfaceMaterial.Gaussian && saved.Sidebar.Opacity == .7
            && saved.LaunchBar.Material == SurfaceMaterial.LiquidGlass && saved.LaunchBar.Opacity == .6 && saved.RecentMap.Opacity == .75,
            "Settings cards, sidebar, launch bar and recent-map card each retain their own material / opacity");
        ((ScrollViewer)window.FindName("AppearanceSettingsScroll")).ScrollToTop(); window.UpdateLayout(); capture(window, "BackgroundSettings");
        ((RadioButton)window.FindName("NavLibrary")).IsChecked = true; window.UpdateLayout(); await Task.Delay(180); capture(window, "BackgroundLibrary");
        var list = (ListBox)window.FindName("InstanceList");
        var renderedCards = Children<MaterialBorder>(list).Where(border => border.Area == SurfaceArea.Instances).ToArray();
        check(renderedCards.Length > 0 && renderedCards.All(border => border.Opacity == 1 && border.Effect is null), "Card material changes only the background and preserves foreground opacity / sharpness");
        var restored = new MainWindow(store) { Left = -16000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
        restored.Show(); restored.UpdateLayout();
        check(AppearanceRenderer.Current.Hero.ImagePath == saved.Hero.ImagePath && Math.Abs(WindowMaterials.ReadOpacity(restored) - .65) < .01
            && AppearanceRenderer.Current.Panels.Material == SurfaceMaterial.LiquidGlass && AppearanceRenderer.Current.Sidebar.Material == SurfaceMaterial.Gaussian
            && AppearanceRenderer.Current.LaunchBar.Material == SurfaceMaterial.LiquidGlass, "A new launcher window restores images, new materials and native opacity");
        restored.Close();
        ((RadioButton)window.FindName("NavSettings")).IsChecked = true; window.UpdateLayout();
        Click(settings, "打开外观设置  →"); window.UpdateLayout(); Select(SurfaceArea.Window);
        Control<Slider>(editor, "WindowOpacity").Value = 100;
        Click(editor, "使用纯色");
        Material(SurfaceMaterial.Gaussian);
        await Preview(() => draft.Current.Window.Material == SurfaceMaterial.Gaussian && draft.Current.Window.ImagePath.Length == 0);
        check(Math.Abs(WindowMaterials.ReadOpacity(window) - .65) < .01, "An unsaved material still preserves real window opacity");
        foreach (var material in new[] { SurfaceMaterial.Solid, SurfaceMaterial.Gaussian, SurfaceMaterial.LiquidGlass })
        {
            Material(material);
            foreach (var alpha in new[] { 0d, 50d, 100d })
            {
                Control<Slider>(editor, "SurfaceOpacity").Value = alpha;
                await Preview(() => draft.Current.Window.Material == material && draft.Current.Window.Opacity == alpha / 100);
            }
            Control<Slider>(editor, "WindowOpacity").Value = 70;
            await Preview(() => draft.Current.WindowOpacity == .7);
            Click(editor, "保存外观设置");
            await Preview(() => Math.Abs(WindowMaterials.ReadOpacity(window) - .7) < .01);
            check(WindowMaterials.ReadBackdrop(window) is 0 or 1 && !AppearanceRenderer.NativeWindow, "Supported material preserves adjustable alpha and clears removed native effects: " + material);
            Control<Slider>(editor, "WindowOpacity").Value = 100;
        }
        Material(SurfaceMaterial.Gaussian); Control<Slider>(editor, "WindowOpacity").Value = 70;
        Click(editor, "保存外观设置");
        Click(editor, "重置全部背景与材质");
        await Preview(() => draft.Current.WindowOpacity == 1 && draft.Current.Window.Material == SurfaceMaterial.Solid);
        check(Math.Abs(WindowMaterials.ReadOpacity(window) - .7) < .01 && AppearanceRenderer.Current.Window.Material == SurfaceMaterial.Gaussian,
            "Reset changes only the draft until save is clicked");
        Click(editor, "保存外观设置");
        check(store.Load().Appearance.WindowOpacity == 1 && store.Load().Appearance.Hero.ImagePath.Length == 0 && WindowMaterials.ReadBackdrop(window) is 0 or 1,
            "Reset restores opaque defaults and clears native backdrops / custom images");
        VerifyRendering(saved.Window.ImagePath, check);
        MaterialRenderingTests.Run(saved.Window.ImagePath, check);
        AppearanceRenderer.Apply(store.Load().Appearance, store.Load().ThemeColor);
        ((ScrollViewer)window.FindName("AppearanceSettingsScroll")).ScrollToTop(); window.UpdateLayout(); capture(window, "AppearanceDefaults");
        Click(page, "← 返回设置"); check(page.Visibility == Visibility.Collapsed && settings.Visibility == Visibility.Visible, "Back returns to launcher settings");
        Click(settings, "打开外观设置  →"); window.UpdateLayout(); Select(SurfaceArea.Instances);
        Control<CheckBox>(editor, "FollowSurfaceTheme").IsChecked = false; Control<TextBox>(editor, "SurfaceColorBox").Text = "#bad";
        Select(SurfaceArea.Hero);
        check(editor.SelectedArea == SurfaceArea.Instances && preview.SelectedArea == SurfaceArea.Instances, "Invalid color keeps the preview selection and unsaved field together");
        Control<TextBox>(editor, "SurfaceColorBox").Text = "#243241"; Click(editor, "重置此区域"); Click(editor, "保存外观设置");
        File.WriteAllText(sourceImage, "not an image");
        bool invalid = false; try { BackgroundImages.Load(sourceImage); } catch (Exception) { invalid = true; }
        check(invalid, "Reject an invalid background image without changing saved appearance");
        var damaged = new LauncherAppearance(); damaged.Window.ImagePath = sourceImage;
        AppearanceRenderer.Apply(damaged, store.Load().ThemeColor);
        var fallback = new MaterialBorder { Area = SurfaceArea.Window, Background = Brushes.White, Width = 160, Height = 120 };
        fallback.Measure(new Size(160, 120)); fallback.Arrange(new Rect(0, 0, 160, 120));
        var fallbackImage = new RenderTargetBitmap(160, 120, 96, 96, PixelFormats.Pbgra32); fallbackImage.Render(fallback);
        check(AppearanceRenderer.ImageWarning.Length > 0, "Corrupt saved backgrounds render a safe fallback instead of crashing");
        damaged.Window.ImagePath = sourceImage + ".missing";
        AppearanceRenderer.Apply(damaged, store.Load().ThemeColor); fallback.InvalidateVisual(); fallback.UpdateLayout(); fallbackImage.Render(fallback);
        check(AppearanceRenderer.ImageWarning.Length > 0, "Missing saved backgrounds render a safe fallback");
        AppearanceRenderer.Apply(store.Load().Appearance, store.Load().ThemeColor);
    }
    public static async Task VerifyQuickSettings(MainWindow window, LauncherStore store, Action<bool, string> check, Action<Window, string> capture)
    {
        var initial = store.Load(); var settings = (ScrollViewer)window.FindName("SettingsPage"); settings.ScrollToTop(); window.UpdateLayout();
        var presets = (WrapPanel)window.FindName("ThemePresetButtons");
        var hide = (CheckBox)window.FindName("HideSidebarDollBox"); var doll = (SkinPreview3D)window.FindName("SidebarDoll");
        var preview = (AppearancePreview)((ContentControl)window.FindName("AppearancePreviewHost")).Content;
        var editor = (AppearanceEditor)((ContentControl)window.FindName("BackgroundAppearanceHost")).Content;
        void Preset(string hex) => Children<Button>(presets).Single(button => button.Tag as string == hex).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        check(presets.Children.Count == AppearancePresets.All.Count && presets.Children.Count == 20 && window.FindName("SaveQuickAppearanceButton") is null, "Twenty one-click presets include curated light and dark schemes without a redundant confirmation button");
        foreach (var hex in new[] { "#F44336", "#2563EB", "#EC4899", "#EAB308" })
        {
            Preset(hex); window.UpdateLayout();
            check(preview.RenderContext.ThemeColor == hex && ((SolidColorBrush)((Button)window.FindName("LaunchButton")).Background).Color == (Color)ColorConverter.ConvertFromString(hex)
                && store.Load().ThemeColor == hex, "Clicking a preset immediately applies and persists it: " + hex);
        }
        check(((SolidColorBrush)preview.Resources["OnAccent"]).Color == Colors.Black && ((SolidColorBrush)Application.Current.Resources["OnAccent"]).Color == Colors.Black,
            "The applied bright yellow preset keeps its foreground readable");
        var memory = (MemoryAllocationEditor)window.FindName("GlobalMemoryEditor"); var originalMemory = memory.ManualMemoryMb; memory.ManualMemoryMb = 7168;
        Preset("#EC4899"); hide.IsChecked = true; window.UpdateLayout(); memory.ManualMemoryMb = originalMemory;
        check(store.Load().Appearance.HideSidebarDoll && store.Load().ThemeColor == "#EC4899" && doll.Visibility == Visibility.Collapsed && !doll.IsAnimating
            && ((Button)window.FindName("SidebarAccountButton")).IsVisible && ((Image)window.FindName("SidebarAvatar")).Source is not null,
            "Presets and the paper doll switch apply without saving or validating unfinished runtime settings");

        ((TextBox)window.FindName("ThemeColorBox")).Text = "#C026D3";
        check(editor.SelectArea(SurfaceArea.Instances), "Custom tuning can start from an applied preset");
        Control<Slider>(editor, "SurfaceOpacity").Value = 31;
        await Preview(() => preview.RenderContext.Current.Instances.Opacity == .31 && preview.RenderContext.ThemeColor == "#C026D3");
        hide.IsChecked = false; window.UpdateLayout();
        check(!store.Load().Appearance.HideSidebarDoll && doll.IsVisible && store.Load().ThemeColor == "#EC4899" && store.Load().Appearance.Instances.Opacity == 1
            && preview.RenderContext.Current.Instances.Opacity == .31 && preview.RenderContext.ThemeColor == "#C026D3",
            "The immediate paper doll switch preserves pending custom tuning without committing it");

        var failureShown = false;
        using (var locked = new FileStream(Path.Combine(store.DirectoryPath, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            _ = window.Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var notice in Application.Current.Windows.OfType<MessageWindow>().Where(dialog => dialog.Owner == window).ToArray()) { failureShown = true; notice.Close(); }
            }));
            Preset("#2563EB");
        }
        check(failureShown && store.Load().ThemeColor == "#EC4899" && AppearanceRenderer.ThemeColor == "#EC4899"
            && preview.RenderContext.Current.Instances.Opacity == .31 && ((TextBox)window.FindName("ThemeColorBox")).Text == "#C026D3",
            "A preset save failure keeps the live theme, persisted state and unfinished custom draft unchanged");
        failureShown = false;
        using (var locked = new FileStream(Path.Combine(store.DirectoryPath, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            _ = window.Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var notice in Application.Current.Windows.OfType<MessageWindow>().Where(dialog => dialog.Owner == window).ToArray()) { failureShown = true; notice.Close(); }
            }));
            hide.IsChecked = true;
        }
        check(failureShown && hide.IsChecked == false && doll.IsVisible && !store.Load().Appearance.HideSidebarDoll,
            "A paper doll save failure restores the switch and leaves the actual model visible");

        Preset("#00AAC8"); window.UpdateLayout();
        var glass = store.Load(); var background = glass.Appearance.Window.ImagePath;
        check(glass.ThemeColor == "#00AAC8" && File.Exists(background) && Path.GetDirectoryName(background) == Path.Combine(store.DirectoryPath, "Backgrounds")
            && BackgroundImages.Load(background).PixelWidth == 1400 && BackgroundImages.Load(background).PixelHeight == 900,
            "The glass preset installs its embedded colorful background into the user configuration without a test-file dependency");
        check(Enum.GetValues<SurfaceArea>().Where(area => area != SurfaceArea.Window).All(area => glass.Appearance.For(area).Material == SurfaceMaterial.LiquidGlass)
            && glass.Appearance.Content.Opacity == .18 && glass.Appearance.Sidebar.Opacity == .86 && glass.Appearance.Window.ImageTint == .02
            && AppearanceRenderer.Current == glass.Appearance && ((TextBox)window.FindName("ThemeColorBox")).Text == "#00AAC8",
            "One click applies the complete glass composition and replaces the pending custom draft");
        check(glass.SelectedAccountId == initial.SelectedAccountId && glass.Accounts.Count == initial.Accounts.Count && glass.MemoryMb == initial.MemoryMb,
            "Applying a theme preserves player accounts and runtime preferences");
        capture(window, "GlassPresetSettings");

        File.WriteAllBytes(background, [1, 2, 3]); Preset("#00AAC8");
        check(BackgroundImages.Load(background).PixelWidth == 1400, "Choosing the embedded preset repairs its missing or damaged owned background asset");
        var restored = new MainWindow(store) { Left = -16000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
        restored.Show(); restored.UpdateLayout();
        check(AppearanceRenderer.Current.Content.Opacity == .18 && ((SolidColorBrush)((Button)restored.FindName("LaunchButton")).Background).Color == (Color)ColorConverter.ConvertFromString("#00AAC8")
            && Children<Button>((WrapPanel)restored.FindName("ThemePresetButtons")).Single(button => button.Tag as string == "#00AAC8").BorderThickness.Left == 2,
            "A fresh window restores the immediately saved glass theme and its active preset indicator"); restored.Close();
        Preset("#EC4899"); window.UpdateLayout();
        check(store.Load().Appearance.Window.ImagePath.Length == 0 && store.Load().Appearance.Instances.Material == SurfaceMaterial.Solid && store.Load().Appearance.Content.Opacity == 1,
            "Switching from glass to a plain preset restores the whole plain appearance");
        foreach (var size in new[] { (1020d, 700d), (1240d, 830d) })
        {
            window.Width = size.Item1; window.Height = size.Item2; settings.ScrollToTop(); window.UpdateLayout();
            check(Children<Button>(presets).All(button => button.TransformToAncestor(presets).Transform(new Point(button.ActualWidth, 0)).X <= presets.ActualWidth + 1) &&
                hide.TransformToAncestor(window).Transform(new Point(0, hide.ActualHeight)).Y < window.ActualHeight,
                $"Seventeen presets and the immediate display switch fit at {(int)size.Item1}px");
            capture(window, "QuickAppearance-" + (int)size.Item1);
        }
        editor.Load(initial.Appearance); hide.IsChecked = initial.Appearance.HideSidebarDoll; ((TextBox)window.FindName("ThemeColorBox")).Text = initial.ThemeColor;
        Click(editor, "保存外观设置"); window.UpdateLayout();
        check(store.Load().Appearance == initial.Appearance && store.Load().ThemeColor == initial.ThemeColor, "Custom save remains available for further tuning after preset selection");
    }
    private static void VerifyRendering(string imagePath, Action<bool, string> check)
    {
        var options = new LauncherAppearance(); options.Window.ImagePath = imagePath;
        options.Instances.Opacity = .45; options.Instances.Material = SurfaceMaterial.Gaussian; options.Instances.BlurRadius = 24;
        AppearanceRenderer.Apply(options, AppearanceSettings.DefaultColor);
        var card = new MaterialBorder { Area = SurfaceArea.Instances, Width = 160, Height = 120, Background = Brushes.White,
            Child = new Border { Width = 12, Height = 12, Background = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        card.Measure(new Size(160, 120)); card.Arrange(new Rect(0, 0, 160, 120)); card.UpdateLayout();
        var bitmap = new RenderTargetBitmap(160, 120, 96, 96, PixelFormats.Pbgra32); bitmap.Render(card);
        byte[] pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect(80, 60, 1, 1), pixel, 4, 0);
        check(pixel[3] == 255 && pixel[0] == 0, "Blurred translucent card retains an opaque, crisp foreground");
        bitmap.CopyPixels(new Int32Rect(40, 40, 1, 1), pixel, 4, 0);
        check(pixel[3] is >= 113 and <= 116, "Rendered card background honors the chosen alpha independently of its content");
        bitmap.CopyPixels(new Int32Rect(42, 42, 1, 1), pixel, 4, 0); var first = pixel.ToArray();
        bitmap.CopyPixels(new Int32Rect(50, 42, 1, 1), pixel, 4, 0);
        check(Math.Abs(first[0] - pixel[0]) < 12 && Math.Abs(first[2] - pixel[2]) < 12, "Gaussian material blurs contrasting background tiles");
        options.Window.ImageTint = 1; AppearanceRenderer.Apply(options, AppearanceSettings.DefaultColor);
        var tinted = new MaterialBorder { Area = SurfaceArea.Window, Background = Brushes.Red, Width = 160, Height = 120 };
        tinted.Measure(new Size(160, 120)); tinted.Arrange(new Rect(0, 0, 160, 120));
        var tintBitmap = new RenderTargetBitmap(160, 120, 96, 96, PixelFormats.Pbgra32); tintBitmap.Render(tinted);
        tintBitmap.CopyPixels(new Int32Rect(40, 40, 1, 1), pixel, 4, 0);
        check(pixel[2] == 255 && pixel[0] == 0, "Image tint strength reaches the chosen solid color at its maximum");
        options.Hero.DefaultIllustration = false; AppearanceRenderer.Apply(options, AppearanceSettings.DefaultColor);
        var plainHero = new MaterialBorder { Area = SurfaceArea.Hero, Background = Brushes.Red, Width = 160, Height = 120 };
        plainHero.Measure(new Size(160, 120)); plainHero.Arrange(new Rect(0, 0, 160, 120));
        var plainImage = new RenderTargetBitmap(160, 120, 96, 96, PixelFormats.Pbgra32); plainImage.Render(plainHero);
        plainImage.CopyPixels(new Int32Rect(140, 100, 1, 1), pixel, 4, 0);
        check(pixel[2] == 255 && pixel[0] == 0 && pixel[1] == 0, "Plain recent-play mode removes the default illustration");
    }
}
