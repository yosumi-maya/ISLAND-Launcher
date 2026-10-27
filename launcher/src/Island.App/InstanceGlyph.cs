using System.Windows;
using System.Windows.Media;

namespace Island.App;

public sealed class InstanceGlyph : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string), typeof(InstanceGlyph), new FrameworkPropertyMetadata("原版", FrameworkPropertyMetadataOptions.AffectsRender));
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); dc.PushTransform(new ScaleTransform(ActualWidth / 24, ActualHeight / 24));
        if (Kind is "Fabric" or "Quilt")
        {
            var pen = new Pen(Brush("#9A855E"), 1.4);
            dc.DrawRoundedRectangle(Brush("#DACDB0"), pen, new Rect(4, 5, 16, 14), 2, 2);
            for (var i = 0; i < 3; i++) { dc.DrawLine(pen, new Point(7, 8 + i * 4), new Point(17, 8 + i * 4)); dc.DrawLine(pen, new Point(8 + i * 4, 5), new Point(8 + i * 4, 19)); }
        }
        else if (Kind is "Forge" or "NeoForge" or "Cleanroom")
        {
            var color = Kind switch { "Forge" => "#708496", "Cleanroom" => "#4F8074", _ => "#C18E47" };
            Polygon(dc, color, (2,5),(22,5),(18,10),(14,10),(14,16),(19,19),(19,21),(5,21),(5,19),(10,16),(10,10),(6,10));
        }
        else
        {
            Polygon(dc, "#92B276", (12,2),(22,7),(12,12),(2,7));
            Polygon(dc, "#668D51", (2,7),(12,12),(12,23),(2,17));
            Polygon(dc, "#7FA566", (12,12),(22,7),(22,17),(12,23));
            var pen = new Pen(Brush("#DFEBD4"), 0.8); dc.DrawLine(pen,new Point(12,12),new Point(12,22));
        }
        dc.Pop();
    }
    private static void Polygon(DrawingContext dc, string color, params (double X, double Y)[] points)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open()) { context.BeginFigure(new Point(points[0].X, points[0].Y), true, true); context.PolyLineTo(points.Skip(1).Select(p => new Point(p.X,p.Y)).ToArray(),true,false); }
        dc.DrawGeometry(Brush(color),null,geometry);
    }
}
