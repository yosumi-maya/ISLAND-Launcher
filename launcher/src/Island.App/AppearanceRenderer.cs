using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Island.Core;

namespace Island.App;

public static class AppearanceRenderer
{
    public static AppearanceRenderContext Live { get; } = new();
    public static LauncherAppearance Current => Live.Current;
    public static string ThemeColor => Live.ThemeColor;
    public static bool NativeWindow { get => Live.NativeWindow; set => Live.NativeWindow = value; }
    public static string ImageWarning => Live.ImageWarning;
    public static void Apply(LauncherAppearance options, string themeColor) => Live.Apply(options, themeColor);
    public static Color ColorFor(SurfaceAppearance style, Brush? fallback) => AppearanceRenderContext.ColorFor(style, fallback);
}

public sealed class AppearanceRenderContext
{
    public LauncherAppearance Current { get; private set; } = new();
    public string ThemeColor { get; private set; } = AppearanceSettings.DefaultColor;
    public bool NativeWindow { get; set; }
    public ResourceDictionary ThemeResources { get; } = new();
    public event Action? Changed;
    private readonly Dictionary<string, BitmapSource> Scenes = [];
    private readonly Dictionary<(BitmapSource, int), BitmapSource> Blurred = [];
    private readonly Dictionary<MaterialBorder, (Rect Bounds, Color Tint, int Layout, BitmapSource Image)> glassSurfaces = [], compositeSurfaces = [];
    public AppearanceRenderContext() => ThemeManager.Apply(ThemeColor, ThemeResources);
    public void Apply(LauncherAppearance options, string themeColor)
    {
        var next = options.Normalize();
        if (ThemeColor != themeColor || Current.Window.ImagePath != next.Window.ImagePath || Current.Hero.ImagePath != next.Hero.ImagePath || Current.RecentMap.ImagePath != next.RecentMap.ImagePath || Current.Hero.Color != next.Hero.Color || Current.Hero.DefaultIllustration != next.Hero.DefaultIllustration)
        { Scenes.Clear(); Blurred.Clear(); }
        var paletteChanged = ThemeColor != themeColor || Current.DarkTheme != next.DarkTheme;
        Current = next;
        glassSurfaces.Clear(); compositeSurfaces.Clear();
        if (paletteChanged) ThemeManager.Apply(themeColor, ThemeResources, next.DarkTheme);
        ThemeColor = themeColor; ImageWarning = ""; Changed?.Invoke();
    }
    public static Color ColorFor(SurfaceAppearance style, Brush? fallback)
    {
        if (style.Color.Length > 0) return (Color)ColorConverter.ConvertFromString(style.Color);
        return (fallback as SolidColorBrush)?.Color ?? Colors.White;
    }
    public void Draw(DrawingContext dc, MaterialBorder element)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0) return;
        var style = Current.For(element.Area);
        var tint = ColorFor(style, element.Background);
        var bounds = new Rect(0, 0, element.ActualWidth, element.ActualHeight);
        dc.PushClip(new RectangleGeometry(bounds, element.CornerRadius.TopLeft, element.CornerRadius.TopLeft));
        dc.PushOpacity(style.Opacity);
        var isNative = element.Area == SurfaceArea.Window && NativeWindow && !element.IsPreviewRoot;
        if (style.Material == SurfaceMaterial.LiquidGlass)
        {
            dc.DrawImage(GlassSurface(element, tint), bounds);
            DrawGlass(dc, bounds, element.CornerRadius.TopLeft);
            dc.Pop(); dc.Pop(); return;
        }
        if (!isNative)
        {
            var fallbackOpacity = style.Material == SurfaceMaterial.Solid || element.Area is SurfaceArea.Window or SurfaceArea.Hero ? 1 : style.Material switch
            {
                SurfaceMaterial.Mica => .86, SurfaceMaterial.MicaAlt => .94,
                SurfaceMaterial.ThinAcrylic => .28, SurfaceMaterial.LiquidGlass => .18, _ => .55
            };
            dc.PushOpacity(fallbackOpacity); dc.DrawRectangle(new SolidColorBrush(tint), null, bounds); dc.Pop();
        }
        var window = Window.GetWindow(element);
        var hero = element.Area == SurfaceArea.Hero ? element : element.Area == SurfaceArea.RecentMap ? FindHero(element) : null;
        var mapImage = element.Area == SurfaceArea.RecentMap && style.ImagePath.Length > 0 ? Scene(SurfaceArea.RecentMap) : null;
        var scene = mapImage ?? Scene(hero is not null);
        if (scene is not null && (mapImage is not null || element.Area is SurfaceArea.Window or SurfaceArea.Hero || style.Material != SurfaceMaterial.Solid))
        {
            var radius = style.Material switch
            {
                SurfaceMaterial.Solid => 0,
                _ => (int)Math.Round(style.BlurRadius)
            };
            var image = radius == 0 ? scene : Blur(scene, radius);
            var anchor = mapImage is not null ? element : hero ?? PreviewRoot(element) ?? window?.Content as FrameworkElement ?? element;
            Point point;
            try { point = element.TransformToAncestor(anchor).Transform(new Point()); }
            catch (InvalidOperationException) { point = new Point(); anchor = element; }
            var width = Math.Max(1, anchor.ActualWidth); var height = Math.Max(1, anchor.ActualHeight);
            var scale = Math.Max(width / image.Width, height / image.Height);
            var cropX = (image.Width - width / scale) / 2; var cropY = (image.Height - height / scale) / 2;
            var viewbox = new Rect(cropX + point.X / scale, cropY + point.Y / scale, bounds.Width / scale, bounds.Height / scale);
            var brush = new ImageBrush(image)
            {
                ViewboxUnits = BrushMappingMode.Absolute, Viewbox = viewbox,
                Stretch = Stretch.Fill, TileMode = TileMode.None
            };
            dc.DrawRectangle(brush, null, bounds);
            var tintAmount = style.Material switch
            {
                SurfaceMaterial.Acrylic => .25 + .65 * style.ImageTint, SurfaceMaterial.Mica => .65 + .3 * style.ImageTint,
                SurfaceMaterial.ThinAcrylic => .08 + .4 * style.ImageTint, SurfaceMaterial.MicaAlt => .8 + .18 * style.ImageTint,
                SurfaceMaterial.LiquidGlass => .06 + .32 * style.ImageTint,
                _ => mapImage is not null || element.Area is SurfaceArea.Window or SurfaceArea.Hero ? style.ImageTint : 0
            };
            if (tintAmount > 0) { dc.PushOpacity(tintAmount); dc.DrawRectangle(new SolidColorBrush(tint), null, bounds); dc.Pop(); }
        }
        else if (isNative)
        {
            dc.PushOpacity(.04 + .45 * style.ImageTint); dc.DrawRectangle(new SolidColorBrush(tint), null, bounds); dc.Pop();
        }
        dc.Pop(); dc.Pop();
    }
    private static Rect Position(MaterialBorder element)
    {
        var root = (Visual?)PreviewRoot(element) ?? Window.GetWindow(element)?.Content as Visual;
        var point = root is null ? new Point() : element.TransformToAncestor(root).Transform(new Point());
        return new Rect(point, element.RenderSize);
    }
    private MaterialBorder? BackgroundParent(MaterialBorder element)
    {
        if (element.IsPreviewRoot) return null;
        for (var parent = VisualTreeHelper.GetParent(element); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is MaterialBorder border && ReferenceEquals(border.RenderContext, this)) return border;
        return null;
    }
    internal int BackdropLayout(MaterialBorder element)
    {
        var hash = new HashCode();
        for (var parent = BackgroundParent(element); parent is not null; parent = BackgroundParent(parent)) hash.Add(Position(parent));
        return hash.ToHashCode();
    }
    private BitmapSource CompositeSurface(MaterialBorder element)
    {
        var bounds = Position(element); var tint = ColorFor(Current.For(element.Area), element.Background);
        var layout = BackdropLayout(element);
        if (compositeSurfaces.TryGetValue(element, out var cached) && cached.Bounds == bounds && cached.Tint == tint && cached.Layout == layout) return cached.Image;
        var parent = BackgroundParent(element);
        var image = GlassMaterial.Snapshot(element.RenderSize, 0, dc =>
        {
            dc.DrawRectangle(new SolidColorBrush(tint), null, new Rect(element.RenderSize));
            if (parent is not null) DrawParent(dc, element, parent);
            Draw(dc, element);
        });
        if (compositeSurfaces.Count >= 16) compositeSurfaces.Clear();
        compositeSurfaces[element] = (bounds, tint, layout, image); return image;
    }
    private void DrawParent(DrawingContext dc, MaterialBorder element, MaterialBorder parent)
    {
        var point = element.TransformToAncestor(parent).Transform(new Point());
        dc.DrawImage(CompositeSurface(parent), new Rect(-point.X, -point.Y, parent.ActualWidth, parent.ActualHeight));
    }
    private BitmapSource GlassSurface(MaterialBorder element, Color tint)
    {
        var bounds = Position(element);
        var layout = BackdropLayout(element);
        if (glassSurfaces.TryGetValue(element, out var cached) && cached.Bounds == bounds && cached.Tint == tint && cached.Layout == layout) return cached.Image;
        var style = Current.For(element.Area);
        // Sample only background layers, never the element's children. Capturing the live
        // visual would feed the glass back into itself and blur its own text on every frame.
        var padding = (int)Math.Ceiling(style.BlurRadius + 24);
        var source = GlassMaterial.Snapshot(element.RenderSize, padding, dc =>
        {
            dc.DrawRectangle(new SolidColorBrush(tint), null, new Rect(-padding, -padding, bounds.Width + padding * 2, bounds.Height + padding * 2));
            var parent = BackgroundParent(element);
            var hero = element.Area == SurfaceArea.Hero;
            var mapImage = element.Area == SurfaceArea.RecentMap && style.ImagePath.Length > 0 ? Scene(SurfaceArea.RecentMap) : null;
            var scene = mapImage ?? (hero || parent is null ? Scene(hero) : null);
            if (parent is not null && ((!hero && mapImage is null) || scene is null)) DrawParent(dc, element, parent);
            else
            {
                if (scene is null) return;
                var anchor = hero || mapImage is not null ? element : PreviewRoot(element) ?? Window.GetWindow(element)?.Content as FrameworkElement ?? element;
                var point = element.TransformToAncestor(anchor).Transform(new Point());
                var scale = Math.Max(Math.Max(1, anchor.ActualWidth) / scene.Width, Math.Max(1, anchor.ActualHeight) / scene.Height);
                dc.DrawImage(scene, new Rect((anchor.ActualWidth - scene.Width * scale) / 2 - point.X,
                    (anchor.ActualHeight - scene.Height * scale) / 2 - point.Y, scene.Width * scale, scene.Height * scale));
            }
        });
        var image = GlassMaterial.Render(source, element.RenderSize, padding, element.CornerRadius.TopLeft, style, tint);
        if (glassSurfaces.Count >= 32) glassSurfaces.Clear();
        glassSurfaces[element] = (bounds, tint, layout, image); return image;
    }
    private static readonly LinearGradientBrush GlassSheen = MakeGlassGradient(false);
    private static readonly LinearGradientBrush GlassRim = MakeGlassGradient(true);
    private static LinearGradientBrush MakeGlassGradient(bool rim)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(rim ? 1 : .3, 1) };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(rim ? (byte)210 : (byte)65, 255, 255, 255), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(rim ? (byte)24 : (byte)5, 255, 255, 255), .48));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(rim ? (byte)120 : (byte)25, 255, 255, 255), 1));
        brush.Freeze(); return brush;
    }
    private static void DrawGlass(DrawingContext dc, Rect bounds, double radius)
    {
        dc.DrawRectangle(GlassSheen, null, bounds);
        // Draw the bevel inside the surface opacity group so zero alpha also hides the highlights.
        bounds.Inflate(-.65, -.65);
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            dc.DrawRoundedRectangle(null, new Pen(GlassRim, 1.3), bounds, Math.Max(0, radius - .65), Math.Max(0, radius - .65));
            bounds.Inflate(-1.1, -1.1);
            if (bounds.Width > 0 && bounds.Height > 0)
                dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(28, 0, 0, 0)), .7), bounds, Math.Max(0, radius - 1.75), Math.Max(0, radius - 1.75));
        }
    }
    public string ImageWarning { get; private set; } = "";
    private BitmapSource? Scene(bool hero) => Scene(hero ? SurfaceArea.Hero : SurfaceArea.Window);
    private BitmapSource? Scene(SurfaceArea area)
    {
        var style = Current.For(area);
        var key = area.ToString();
        if (Scenes.TryGetValue(key, out var cached)) return cached;
        if (style.ImagePath.Length > 0)
        {
            try { var image = BackgroundImages.Load(style.ImagePath); Scenes[key] = image; return image; }
            catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException or InvalidOperationException or FormatException or UnauthorizedAccessException)
            { ImageWarning = "背景图片无法读取，已使用主题背景。"; }
        }
        if (area != SurfaceArea.Hero || !style.DefaultIllustration) return null;
        var accent = (Color)ColorConverter.ConvertFromString(ThemeColor);
        var background = ColorFor(style, new SolidColorBrush(ThemeManager.Mix(accent, Colors.Black, .4)));
        var grid = new Grid { Width = 1000, Height = 200, Background = new SolidColorBrush(background) };
        grid.Children.Add(new Landscape { AccentBrush = new SolidColorBrush(accent), BackgroundBrush = new SolidColorBrush(background), Margin = new Thickness(300, 0, 0, 0), Opacity = .6 });
        grid.Measure(new Size(1000, 200)); grid.Arrange(new Rect(0, 0, 1000, 200));
        var bitmap = new RenderTargetBitmap(1000, 200, 96, 96, PixelFormats.Pbgra32); bitmap.Render(grid); bitmap.Freeze();
        Scenes[key] = bitmap; return bitmap;
    }
    private BitmapSource Blur(BitmapSource scene, int radius)
    {
        if (Blurred.TryGetValue((scene, radius), out var cached)) return cached;
        var width = Math.Min(1280, scene.PixelWidth); var height = Math.Max(1, (int)Math.Round((double)scene.PixelHeight * width / scene.PixelWidth));
        var visual = new DrawingVisual { Effect = new BlurEffect { Radius = radius, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance } };
        using (var dc = visual.RenderOpen()) dc.DrawImage(scene, new Rect(0, 0, width, height));
        var result = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); result.Render(visual); result.Freeze();
        if (Blurred.Count >= 12) Blurred.Clear();
        Blurred[(scene, radius)] = result; return result;
    }
    private static MaterialBorder? FindHero(DependencyObject element)
    {
        for (var parent = VisualTreeHelper.GetParent(element); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is MaterialBorder { Area: SurfaceArea.Hero } hero) return hero;
        return null;
    }
    private static MaterialBorder? PreviewRoot(DependencyObject element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is MaterialBorder { IsPreviewRoot: true } root) return root;
        return null;
    }
    private static readonly DrawingBrush Noise = MakeNoise();
    private static DrawingBrush MakeNoise()
    {
        var drawing = new DrawingGroup(); var random = new Random(318);
        using (var dc = drawing.Open())
            for (var y = 0; y < 32; y++)
                for (var x = 0; x < 32; x++)
                    dc.DrawRectangle(random.Next(2) == 0 ? Brushes.Black : Brushes.White, null, new Rect(x, y, 1, 1));
        var brush = new DrawingBrush(drawing) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 32, 32), Stretch = Stretch.None };
        brush.Freeze(); return brush;
    }
}

public sealed class MaterialBorder : Border
{
    // The miniature inherits a separate renderer, including image / blur caches and theme colors.
    public static readonly DependencyProperty RenderContextProperty = DependencyProperty.RegisterAttached("RenderContext", typeof(AppearanceRenderContext), typeof(MaterialBorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender, ContextChanged));
    public AppearanceRenderContext RenderContext { get => (AppearanceRenderContext?)GetValue(RenderContextProperty) ?? AppearanceRenderer.Live; set => SetValue(RenderContextProperty, value); }
    private AppearanceRenderContext? subscribedContext;
    private static void ContextChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is MaterialBorder border) { if (border.IsLoaded) border.SubscribeContext(); border.Refresh(); }
    }
    private void SubscribeContext()
    {
        if (subscribedContext is not null) subscribedContext.Changed -= Refresh;
        subscribedContext = RenderContext; subscribedContext.Changed += Refresh;
    }
    public bool IsPreviewRoot { get; set; }
    public static readonly DependencyProperty AreaProperty = DependencyProperty.Register(nameof(Area), typeof(SurfaceArea), typeof(MaterialBorder),
        new FrameworkPropertyMetadata(SurfaceArea.Panels, FrameworkPropertyMetadataOptions.AffectsRender));
    public SurfaceArea Area { get => (SurfaceArea)GetValue(AreaProperty); set => SetValue(AreaProperty, value); }
    private Rect lastPosition;
    private int lastBackdropLayout;
    private string palette = "";
    public MaterialBorder()
    {
        Loaded += (_, _) => { SubscribeContext(); LayoutUpdated += TrackPosition; Refresh(); };
        Unloaded += (_, _) => { if (subscribedContext is not null) subscribedContext.Changed -= Refresh; subscribedContext = null; LayoutUpdated -= TrackPosition; };
    }
    private void Refresh() { palette = ""; UpdatePalette(); InvalidateVisual(); }
    private void TrackPosition(object? sender, EventArgs e)
    {
        if (CardMotion.IsMoving(this)) return;
        var window = Window.GetWindow(this);
        if (window?.Content is not Visual root) return;
        try
        {
            var point = TransformToAncestor(root).Transform(new Point()); var rect = new Rect(point, RenderSize);
            var backdropLayout = RenderContext.BackdropLayout(this);
            if (rect == lastPosition && backdropLayout == lastBackdropLayout) return;
            lastPosition = rect; lastBackdropLayout = backdropLayout; InvalidateVisual();
        }
        catch (InvalidOperationException) { }
    }
    protected override void OnRender(DrawingContext dc)
    {
        UpdatePalette();
        RenderContext.Draw(dc, this);
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight); var thickness = BorderThickness;
        if (BorderBrush is null || thickness == new Thickness()) return;
        if (thickness.Left == thickness.Top && thickness.Left == thickness.Right && thickness.Left == thickness.Bottom)
        {
            var half = thickness.Left / 2; bounds.Inflate(-half, -half);
            if (bounds.Width > 0 && bounds.Height > 0) dc.DrawRoundedRectangle(null, new Pen(BorderBrush, thickness.Left), bounds, CornerRadius.TopLeft, CornerRadius.TopLeft);
        }
        else
        {
            if (thickness.Right > 0) dc.DrawLine(new Pen(BorderBrush, thickness.Right), new Point(ActualWidth - thickness.Right / 2, 0), new Point(ActualWidth - thickness.Right / 2, ActualHeight));
            if (thickness.Left > 0) dc.DrawLine(new Pen(BorderBrush, thickness.Left), new Point(thickness.Left / 2, 0), new Point(thickness.Left / 2, ActualHeight));
            if (thickness.Top > 0) dc.DrawLine(new Pen(BorderBrush, thickness.Top), new Point(0, thickness.Top / 2), new Point(ActualWidth, thickness.Top / 2));
            if (thickness.Bottom > 0) dc.DrawLine(new Pen(BorderBrush, thickness.Bottom), new Point(0, ActualHeight - thickness.Bottom / 2), new Point(ActualWidth, ActualHeight - thickness.Bottom / 2));
        }
    }
    private void UpdatePalette()
    {
        var style = RenderContext.Current.For(Area);
        var color = AppearanceRenderer.ColorFor(style, Background);
        var key = color.ToString() + "|" + RenderContext.ThemeColor + "|" + RenderContext.Current.DarkTheme + "|" + style.ForegroundColor;
        if (key == palette) return;
        palette = key;
        foreach (System.Collections.DictionaryEntry entry in RenderContext.ThemeResources) Resources[entry.Key] = entry.Value;
        if (style.Color.Length == 0)
        {
            foreach (var name in new[] { "Ink", "Muted", "AccentInk", "Surface", "AccentSoft", "AccentHover", "HeroText", "HeroMuted" })
                Resources[name] = RenderContext.ThemeResources[name];
            ApplyForeground(style);
            SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Ink"); return;
        }
        var accent = (Color)ColorConverter.ConvertFromString(RenderContext.ThemeColor);
        var light = color.R * .299 + color.G * .587 + color.B * .114 > 145;
        var ink = ThemeManager.Readable(light ? ThemeManager.Mix(accent, Colors.Black, .63) : Color.FromRgb(241, 245, 242), color);
        Resources["Ink"] = new SolidColorBrush(ink);
        Resources["Muted"] = new SolidColorBrush(ThemeManager.Readable(ThemeManager.Mix(ink, color, .35), color));
        Resources["AccentInk"] = new SolidColorBrush(ThemeManager.Readable(accent, ThemeManager.Mix(color, accent, .16)));
        Resources["Surface"] = new SolidColorBrush(ThemeManager.Mix(color, light ? Colors.White : Colors.Black, .12));
        Resources["AccentSoft"] = new SolidColorBrush(ThemeManager.Mix(color, accent, .16));
        Resources["AccentHover"] = new SolidColorBrush(ThemeManager.Mix(color, accent, .24));
        Resources["Focus"] = Resources["SelectionBorder"] = new SolidColorBrush(ThemeManager.Readable(accent, color, 3));
        Resources["SelectionBackground"] = new SolidColorBrush(ThemeManager.Mix(color, accent, .16));
        if (style.Color.Length > 0)
        {
            Resources["HeroText"] = new SolidColorBrush(ink); Resources["HeroMuted"] = new SolidColorBrush(ThemeManager.Mix(ink, color, .3));
        }
        else { Resources.Remove("HeroText"); Resources.Remove("HeroMuted"); }
        ApplyForeground(style);
        SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Ink");
    }
    private void ApplyForeground(SurfaceAppearance style)
    {
        if (style.ForegroundColor.Length == 0) return;
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(style.ForegroundColor));
        foreach (var resource in new[] { "Ink", "Muted", "HeroText", "HeroMuted" }) Resources[resource] = brush;
    }
}
