using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Island.Core;

namespace Island.App;

public sealed class AppearancePreview : Grid
{
    private readonly List<Button> regions = [];
    private readonly Func<SurfaceArea, bool> select;
    private readonly Canvas canvas = new() { Width = 920, Height = 370 };
    private readonly SkinPreview3D doll = new() { Width = 100, Height = 84, IsHitTestVisible = false, FollowPointer = false };
    public AppearanceRenderContext RenderContext { get; } = new();
    public SurfaceArea SelectedArea { get; private set; }
    public static string AreaName(SurfaceArea area) => area switch
    {
        SurfaceArea.Window => "窗口外框与背景", SurfaceArea.Hero => "上次游玩卡片", SurfaceArea.Instances => "实例卡片",
        SurfaceArea.Panels => "设置与账号卡片", SurfaceArea.Sidebar => "左侧导航模块", SurfaceArea.LaunchBar => "底部启动栏",
        SurfaceArea.Content => "右侧内容模块", _ => "上次游玩的地图卡片"
    };
    public AppearancePreview(Func<SurfaceArea, bool> onSelect)
    {
        select = onSelect;
        Resources = RenderContext.ThemeResources;
        SetValue(MaterialBorder.RenderContextProperty, RenderContext);
        canvas.Clip = new RectangleGeometry(new Rect(0, 0, 920, 370), 10, 10);
        Background = CheckerboardBackground();
        var root = Surface(SurfaceArea.Window, "WindowBackground", 0, 0, 920, 370); root.IsPreviewRoot = true;
        var scene = new Canvas { Width = 920, Height = 370 }; root.Child = scene; canvas.Children.Add(root);
        var content = Surface(SurfaceArea.Content, "WindowBackground", 174, 28, 736, 332); scene.Children.Add(content);
        var contentBody = new Canvas { Width = 736, Height = 332 }; content.Child = contentBody;
        var side = Surface(SurfaceArea.Sidebar, "SidebarBackground", 10, 28, 154, 332);
        var sideContent = new StackPanel { Margin = new Thickness(18), Width = 118 };
        sideContent.Children.Add(Text("ISLAND", 22, true));
        sideContent.Children.Add(Text("Launcher", 11, false, "Muted", new Thickness(0, 3, 0, 32)));
        sideContent.Children.Add(Text("⌂  我的实例", 15, true));
        sideContent.Children.Add(Text("♙  玩家账号", 14, false, "Muted", new Thickness(0, 22, 0, 0)));
        sideContent.Children.Add(Text("⚙  启动器设置", 14, false, "Muted", new Thickness(0, 22, 0, 0)));
        var sidebarBody = new Canvas { Width = 154, Height = 332 }; side.Child = sidebarBody;
        sidebarBody.Children.Add(sideContent); scene.Children.Add(side);
        Add(sidebarBody, doll, 27, 192);
        var trafficLights = new StackPanel { Orientation = Orientation.Horizontal, IsHitTestVisible = false };
        foreach (var (fill, edge) in new[] { ("#FF5F57", "#CD4944"), ("#FEBC2E", "#D69B25"), ("#28C840", "#20A834") })
            trafficLights.Children.Add(new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(4.5), Margin = new Thickness(0, 0, 6, 0), BorderThickness = new Thickness(.6),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fill)), BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(edge)) });
        Add(scene, trafficLights, 16, 9);
        Add(scene, Text("MINECRAFT JAVA EDITION", 10, false, "Muted"), 72, 8);
        Add(scene, Text("─    □    ×", 13, false, "Muted"), 829, 6);
        var hero = Surface(SurfaceArea.Hero, "HeroBackground", 16, 16, 704, 110);
        var heroCanvas = new Canvas { Width = 704, Height = 110 };
        var heroText = new StackPanel { Margin = new Thickness(20, 12, 0, 0) };
        heroText.Children.Add(Text("上次游玩", 12, false, "HeroMuted"));
        heroText.Children.Add(Text("继续你的世界", 23, true, "HeroText", new Thickness(0, 5, 0, 0)));
        var continueAction = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 5, 12, 5), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 7, 0, 0) };
        continueAction.SetResourceReference(Border.BackgroundProperty, "Accent"); continueAction.Child = Text("▶  一键继续", 12, true, "OnAccent");
        heroText.Children.Add(continueAction); heroCanvas.Children.Add(heroText);
        var map = Surface(SurfaceArea.RecentMap, "HeroSurface", 466, 16, 218, 78);
        var mapText = new StackPanel { Margin = new Thickness(14, 10, 0, 0) };
        mapText.Children.Add(Text("上次游玩的地图", 11, false, "HeroMuted"));
        mapText.Children.Add(Text("林间小屋", 17, true, "HeroText", new Thickness(0, 8, 0, 0))); map.Child = mapText;
        heroCanvas.Children.Add(map); hero.Child = heroCanvas; contentBody.Children.Add(hero);
        Add(contentBody, Text("☆ 收藏     默认目录", 12, true, "AccentInk"), 23, 140);
        Add(contentBody, Text("搜索世界…                    ＋ 添加实例", 11, false, "Muted"), 473, 140);
        for (int i = 0; i < 2; i++)
        {
            var card = Surface(SurfaceArea.Instances, "Surface", 16 + i * 224, 166, 210, 96);
            var body = new StackPanel { Margin = new Thickness(15, 10, 0, 0) };
            body.Children.Add(Text(i == 0 ? "▣                         ☆" : "◆                         ☆", 16, false, "AccentInk"));
            body.Children.Add(Text(i == 0 ? "冒险世界" : "模组生存", 17, true, margin: new Thickness(0, 8, 0, 0)));
            body.Children.Add(Text(i == 0 ? "Minecraft · 原版" : "Minecraft · Fabric", 11, false, "Muted", new Thickness(0, 5, 0, 0))); card.Child = body; contentBody.Children.Add(card);
        }
        var panels = Surface(SurfaceArea.Panels, "Surface", 464, 166, 256, 96);
        var panelText = new StackPanel { Margin = new Thickness(16, 14, 0, 0) };
        panelText.Children.Add(Text("设置与账号卡片", 17, true));
        panelText.Children.Add(Text("界面设置、玩家信息等", 12, false, "Muted", new Thickness(0, 12, 0, 0))); panels.Child = panelText; contentBody.Children.Add(panels);
        var footer = Surface(SurfaceArea.LaunchBar, "AccentSoft", 16, 278, 704, 46);
        var launchText = new Grid { Margin = new Thickness(15, 8, 12, 8) }; launchText.Children.Add(Text("冒险世界", 14, true));
        var launch = new Border { CornerRadius = new CornerRadius(6), Width = 124, HorizontalAlignment = HorizontalAlignment.Right }; launch.SetResourceReference(Border.BackgroundProperty, "Accent");
        launch.Child = Text("▶  启动游戏", 13, true, "OnAccent"); ((TextBlock)launch.Child).HorizontalAlignment = HorizontalAlignment.Center;
        launchText.Children.Add(launch); footer.Child = launchText; contentBody.Children.Add(footer);
        var account = new Border { CornerRadius = new CornerRadius(7), Width = 126, Height = 43 };
        account.SetResourceReference(Border.BackgroundProperty, "AccentSoft"); account.Child = Text("♙  Player", 13, true); ((TextBlock)account.Child).Margin = new Thickness(12, 0, 0, 0); Add(sidebarBody, account, 14, 282);
        AddRegion(canvas, SurfaceArea.Window, 0, 0, 920, 370);
        AddRegion(canvas, SurfaceArea.Content, 174, 28, 736, 332);
        AddRegion(canvas, SurfaceArea.Sidebar, 10, 28, 154, 332);
        AddRegion(canvas, SurfaceArea.Hero, 190, 44, 704, 110);
        AddRegion(canvas, SurfaceArea.Instances, 190, 194, 210, 96);
        AddRegion(canvas, SurfaceArea.Instances, 414, 194, 210, 96);
        AddRegion(canvas, SurfaceArea.Panels, 638, 194, 256, 96);
        AddRegion(canvas, SurfaceArea.LaunchBar, 190, 306, 704, 46);
        AddRegion(canvas, SurfaceArea.RecentMap, 656, 60, 218, 78);
        Children.Add(new Viewbox { Child = canvas, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center }); Highlight(SurfaceArea.Window);
    }
    public void Update(LauncherAppearance options, string themeColor)
    {
        RenderContext.Apply(options, themeColor);
        canvas.Opacity = options.WindowOpacity;
        doll.Visibility = options.HideSidebarDoll ? Visibility.Collapsed : Visibility.Visible;
    }
    public void SetSkin(byte[]? png, string variant) => doll.SetSkin(png, variant);
    public void Highlight(SurfaceArea area)
    {
        SelectedArea = area;
        foreach (var button in regions)
        {
            button.BorderThickness = new Thickness((SurfaceArea)button.Tag == area ? 3 : button.IsMouseOver || button.IsKeyboardFocused ? 1 : 0);
            button.SetResourceReference(Control.BorderBrushProperty, "Focus");
            button.ToolTip = AreaName((SurfaceArea)button.Tag) + ((SurfaceArea)button.Tag == area ? " · 正在编辑" : " · 点击编辑");
        }
    }
    private static DrawingBrush CheckerboardBackground()
    {
        const double square = 12;
        var gray = new SolidColorBrush(Color.FromRgb(230, 230, 230));
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, square * 2, square * 2));
            context.DrawRectangle(gray, null, new Rect(0, 0, square, square));
            context.DrawRectangle(gray, null, new Rect(square, square, square, square));
        }
        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, square * 2, square * 2)
        };
        brush.Freeze();
        return brush;
    }
    private void AddRegion(Canvas parent, SurfaceArea area, double x, double y, double width, double height)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding(nameof(Button.BorderBrush)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding(nameof(Button.BorderThickness)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var button = new Button { Name = "Preview" + area, Tag = area, Width = width, Height = height, Padding = new Thickness(0), Background = Brushes.Transparent,
            Cursor = Cursors.Hand, Template = new ControlTemplate(typeof(Button)) { VisualTree = border } };
        System.Windows.Automation.AutomationProperties.SetName(button, "预览：" + AreaName(area));
        button.Click += (_, _) => { if (select(area)) Highlight(area); };
        button.MouseEnter += (_, _) => Highlight(SelectedArea); button.MouseLeave += (_, _) => Highlight(SelectedArea);
        button.GotKeyboardFocus += (_, _) => Highlight(SelectedArea); button.LostKeyboardFocus += (_, _) => Highlight(SelectedArea);
        regions.Add(button); Add(parent, button, x, y);
    }
    private static MaterialBorder Surface(SurfaceArea area, string brush, double x, double y, double width, double height)
    {
        var surface = new MaterialBorder { Area = area, Width = width, Height = height, CornerRadius = new CornerRadius(area == SurfaceArea.Window ? 10 : 8), IsHitTestVisible = false };
        surface.SetResourceReference(Border.BackgroundProperty, brush); Canvas.SetLeft(surface, x); Canvas.SetTop(surface, y); return surface;
    }
    private static TextBlock Text(string text, double size, bool bold, string? resource = null, Thickness? margin = null)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap, Margin = margin ?? new Thickness(0) };
        if (resource is not null) block.SetResourceReference(TextBlock.ForegroundProperty, resource); return block;
    }
    private static void Add(Canvas canvas, UIElement element, double x, double y) { Canvas.SetLeft(element, x); Canvas.SetTop(element, y); canvas.Children.Add(element); }
}
