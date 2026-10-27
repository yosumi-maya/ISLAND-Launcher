using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Island.Core;

namespace Island.App;

// An app-local glass approximation: rounded-rectangle lensing, frosted backdrop,
// luminance compression and directional edge light. All work is cached by the
// render context; moving the mouse / animating a skin does not rebuild the bitmap.
internal static class GlassMaterial
{
    internal static BitmapSource Snapshot(Size size, int padding, Action<DrawingContext> draw)
    {
        var width = size.Width + padding * 2; var height = size.Height + padding * 2;
        var scale = Math.Min(1, 1440 / Math.Max(width, height));
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scale, scale)); dc.PushTransform(new TranslateTransform(padding, padding));
            draw(dc); dc.Pop(); dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(width * scale)), Math.Max(1, (int)Math.Ceiling(height * scale)), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
    internal static BitmapSource Render(BitmapSource source, Size size, int padding, double radius, SurfaceAppearance style, Color tint)
    {
        var scale = source.PixelWidth / (size.Width + padding * 2);
        var width = Math.Max(1, (int)Math.Round(size.Width * scale)); var height = Math.Max(1, (int)Math.Round(size.Height * scale));
        var stride = source.PixelWidth * 4;
        var sharp = new byte[stride * source.PixelHeight]; source.CopyPixels(sharp, stride, 0);
        var soft = sharp;
        if (style.BlurRadius > 0)
        {
            var visual = new DrawingVisual { Effect = new BlurEffect { Radius = Math.Max(.1, style.BlurRadius * scale), KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality } };
            using (var dc = visual.RenderOpen()) dc.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
            var blurred = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Pbgra32); blurred.Render(visual);
            soft = new byte[sharp.Length]; blurred.CopyPixels(soft, stride, 0);
        }
        var result = new byte[width * height * 4];
        var halfWidth = size.Width / 2; var halfHeight = size.Height / 2;
        radius = Math.Clamp(radius, 0, Math.Min(halfWidth, halfHeight));
        var bevel = Math.Clamp(Math.Min(size.Width, size.Height) * .075, 6, 18);
        // Compress brightness toward the selected surface tone, preserving the wallpaper's
        // hue. The veil provides legibility without painting an opaque theme-colored card.
        var veil = .14 + .55 * style.ImageTint;
        var tone = (tint.R * .2126 + tint.G * .7152 + tint.B * .0722) / 255;
        var luminanceTarget = tone > .55 ? 220d : 55d;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var px = (x + .5) / scale - halfWidth; var py = (y + .5) / scale - halfHeight;
                var qx = Math.Abs(px) - (halfWidth - radius); var qy = Math.Abs(py) - (halfHeight - radius);
                var ox = Math.Max(qx, 0); var oy = Math.Max(qy, 0); var length = Math.Sqrt(ox * ox + oy * oy);
                var inside = radius - length - Math.Min(Math.Max(qx, qy), 0);
                double nx, ny;
                if (length > .001) { nx = Math.Sign(px) * ox / length; ny = Math.Sign(py) * oy / length; }
                else { nx = qx > qy ? Math.Sign(px) : 0; ny = qy >= qx ? Math.Sign(py) : 0; }
                var edge = Math.Clamp(1 - inside / bevel, 0, 1);
                var bend = edge * edge * (bevel * .55);
                var sx = (x + .5) + (padding - nx * bend) * scale;
                var sy = (y + .5) + (padding - ny * bend) * scale;
                var b = Sample(soft, sx, sy, 0); var g = Sample(soft, sx, sy, 1); var r = Sample(soft, sx, sy, 2);
                // The bevel scatters less than the body of the glass, revealing a narrow,
                // displaced band of the backdrop instead of uniformly magnifying the card.
                var clearEdge = edge * edge * .45;
                b += (Sample(sharp, sx, sy, 0) - b) * clearEdge;
                g += (Sample(sharp, sx, sy, 1) - g) * clearEdge;
                r += (Sample(sharp, sx, sy, 2) - r) * clearEdge;
                var lum = r * .2126 + g * .7152 + b * .0722;
                var lift = (luminanceTarget - lum) * .22;
                // Saturation restores some color lost by Gaussian scattering.
                r = lum + (r - lum) * 1.15 + lift; g = lum + (g - lum) * 1.15 + lift; b = lum + (b - lum) * 1.15 + lift;
                r += (tint.R - r) * veil; g += (tint.G - g) * veil; b += (tint.B - b) * veil;
                var light = (-nx * .6 - ny * .8) * edge * edge * .14;
                var index = (y * width + x) * 4;
                result[index] = Channel(b, light); result[index + 1] = Channel(g, light); result[index + 2] = Channel(r, light); result[index + 3] = 255;
            }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, result, width * 4); bitmap.Freeze(); return bitmap;

        double Sample(byte[] pixels, double x, double y, int channel)
        {
            x = Math.Clamp(x - .5, 0, source.PixelWidth - 1); y = Math.Clamp(y - .5, 0, source.PixelHeight - 1);
            var x0 = (int)x; var y0 = (int)y; var x1 = Math.Min(x0 + 1, source.PixelWidth - 1); var y1 = Math.Min(y0 + 1, source.PixelHeight - 1);
            var fx = x - x0; var fy = y - y0;
            var top = pixels[y0 * stride + x0 * 4 + channel] * (1 - fx) + pixels[y0 * stride + x1 * 4 + channel] * fx;
            var bottom = pixels[y1 * stride + x0 * 4 + channel] * (1 - fx) + pixels[y1 * stride + x1 * 4 + channel] * fx;
            return top * (1 - fy) + bottom * fy;
        }
    }
    private static byte Channel(double value, double light) => (byte)Math.Clamp(Math.Round(light >= 0 ? value + (255 - value) * light : value * (1 + light)), 0, 255);
}
