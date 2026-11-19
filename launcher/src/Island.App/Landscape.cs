using System.Windows;
using System.Windows.Media;

namespace Island.App;

/// <summary>Original vector scenery. Drawn natively, with no remote artwork or bitmap dependencies.</summary>
public sealed class Landscape : FrameworkElement
{
    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(nameof(AccentBrush), typeof(SolidColorBrush), typeof(Landscape), new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(50, 99, 77)), FrameworkPropertyMetadataOptions.AffectsRender));
    public SolidColorBrush AccentBrush { get => (SolidColorBrush)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    public static readonly DependencyProperty BackgroundBrushProperty = DependencyProperty.Register(nameof(BackgroundBrush), typeof(SolidColorBrush), typeof(Landscape), new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(23, 61, 50)), FrameworkPropertyMetadataOptions.AffectsRender));
    public SolidColorBrush BackgroundBrush { get => (SolidColorBrush)GetValue(BackgroundBrushProperty); set => SetValue(BackgroundBrushProperty, value); }
    private Color Tint(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex); var accent = AccentBrush.Color;
        static double Light(Color value) => (.2126 * value.R + .7152 * value.G + .0722 * value.B) / 255;
        var level = Light(color); var basis = Light(accent);
        return level <= basis ? ThemeManager.Mix(accent, Colors.Black, 1 - level / Math.Max(.001, basis)) : ThemeManager.Mix(accent, Colors.White, (level - basis) / Math.Max(.001, 1 - basis));
    }
    private SolidColorBrush B(string color) { var brush = new SolidColorBrush(Tint(color)); brush.Freeze(); return brush; }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        dc.PushTransform(new ScaleTransform(ActualWidth / 600, ActualHeight / 250));
        dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, 600, 250));
        dc.DrawEllipse(B("#DCDCB0"), null, new Point(449, 64), 29 * (ActualHeight / 250) / (ActualWidth / 600), 29);
        Poly(dc, "#315748", (0,157),(86,114),(124,125),(215,71),(259,94),(305,83),(407,139),(482,107),(600,144),(600,250),(0,250));
        Poly(dc, "#46705A", (210,181),(301,125),(349,143),(402,105),(446,133),(500,112),(600,151),(600,250),(210,250));
        Poly(dc, "#78906D", (282,250),(352,186),(435,176),(486,153),(544,169),(600,156),(600,250));
        Poly(dc, "#A5B389", (371,250),(410,205),(460,190),(486,193),(442,216),(420,250));
        // Cubic islands and block-like trees nod to Minecraft without using game assets.
        for (var i = 0; i < 12; i++)
        {
            var x = 235 + i * 34; var y = 169 + (i % 3) * 19; var s = i < 4 ? 0.7 : 1.0;
            Tree(dc, x, y, s);
        }
        Poly(dc, "#294D3D", (0,221),(112,185),(207,211),(289,198),(360,250),(0,250));
        Tree(dc, 188, 200, 1.5); Tree(dc, 85, 204, 1.8); Tree(dc, 561, 216, 1.7);
        var fade = BackgroundBrush.Color; dc.DrawRectangle(new LinearGradientBrush(fade,Color.FromArgb(0,fade.R,fade.G,fade.B),0),null,new Rect(0,0,330,250));
        dc.Pop(); dc.Pop();
    }
    private void Tree(DrawingContext dc, double x, double y, double scale)
    {
        dc.DrawRectangle(B("#6C7050"), null, new Rect(x - 2 * scale, y, 4 * scale, 17 * scale));
        Poly(dc, "#1C4836", (x-17*scale,y+3*scale),(x,y-12*scale),(x+17*scale,y-4*scale),(x,y+10*scale));
        Poly(dc, "#2F5E42", (x-15*scale,y-9*scale),(x,y-23*scale),(x+15*scale,y-13*scale),(x,y));
        Poly(dc, "#4E7850", (x-12*scale,y-19*scale),(x,y-35*scale),(x+12*scale,y-23*scale),(x,y-12*scale));
    }
    private void Poly(DrawingContext dc, string color, params (double X, double Y)[] points)
    {
        var shape = new StreamGeometry();
        using (var g = shape.Open()) { g.BeginFigure(new Point(points[0].X, points[0].Y),true,true); g.PolyLineTo(points.Skip(1).Select(p=>new Point(p.X,p.Y)).ToArray(),true,false); }
        shape.Freeze(); dc.DrawGeometry(B(color), null, shape);
    }
}
