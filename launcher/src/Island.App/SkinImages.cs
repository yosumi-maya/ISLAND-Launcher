using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Island.Core;

namespace Island.App;

public sealed record AccountCard(PlayerAccount Account, ImageSource Avatar, bool Current)
{
    public string Name => Account.Name;
    public string Description => (Account.Microsoft ? "Microsoft 正版账号" : "本地离线账号") + (Current ? "  ·  当前玩家" : "");
}

public static class SkinImages
{
    public static BitmapSource Decode(byte[] png)
    {
        PlayerSkins.Validate(png);
        using var stream = new MemoryStream(png, false);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var image = decoder.Frames[0];
        if (image.PixelWidth != 64 || image.PixelHeight is not (32 or 64)) throw new ArgumentException("皮肤尺寸无效。");
        image.Freeze(); return image;
    }
    public static BitmapSource TextureOrDefault(byte[]? png)
    {
        if (png is not null)
            try { return Decode(png); }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or InvalidOperationException) { }
        return DefaultTexture();
    }
    public static BitmapSource Avatar(byte[]? png) => Render(TextureOrDefault(png), true, false);
    public static BitmapSource Portrait(byte[]? png, string variant) => Render(TextureOrDefault(png), false, variant == "slim");
    private static BitmapSource Render(BitmapSource texture, bool head, bool slim)
    {
        var visual = new DrawingVisual(); RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (var dc = visual.RenderOpen())
        {
            void Part(int x, int y, int w, int h, int dx, int dy) =>
                dc.DrawImage(new CroppedBitmap(texture, new Int32Rect(x, y, w, h)), new Rect(dx, dy, w, h));
            Part(8, 8, 8, 8, head ? 0 : 4, 0);
            Part(40, 8, 8, 8, head ? 0 : 4, 0);
            if (!head)
            {
                var arm = slim ? 3 : 4;
                Part(20, 20, 8, 12, 4, 8); Part(44, 20, arm, 12, 4 - arm, 8);
                Part(4, 20, 4, 12, 4, 20);
                Part(texture.PixelHeight == 64 ? 36 : 44, texture.PixelHeight == 64 ? 52 : 20, arm, 12, 12, 8);
                Part(texture.PixelHeight == 64 ? 20 : 4, texture.PixelHeight == 64 ? 52 : 20, 4, 12, 8, 20);
                if (texture.PixelHeight == 64)
                {
                    Part(20, 36, 8, 12, 4, 8); Part(44, 36, arm, 12, 4 - arm, 8);
                    Part(52, 52, arm, 12, 12, 8); Part(4, 36, 4, 12, 4, 20); Part(4, 52, 4, 12, 8, 20);
                }
            }
        }
        var result = new RenderTargetBitmap(head ? 8 : 16, head ? 8 : 32, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual); result.Freeze(); return result;
    }
    private static BitmapSource DefaultTexture()
    {
        var pixels = new byte[64 * 64 * 4];
        void Fill(int x, int y, int w, int h, byte r, byte g, byte b)
        {
            for (var row = y; row < y + h; row++)
                for (var col = x; col < x + w; col++)
                { var i = (row * 64 + col) * 4; pixels[i] = b; pixels[i + 1] = g; pixels[i + 2] = r; pixels[i + 3] = 255; }
        }
        void Box(int u, int v, int w, int h, int d, byte r, byte g, byte b)
        {
            Fill(u, v + d, d * 2 + w * 2, h, r, g, b);
            Fill(u + d, v, w * 2, d, r, g, b);
        }
        Box(0, 0, 8, 8, 8, 202, 160, 123); Box(16, 16, 8, 12, 4, 67, 109, 83);
        Box(40, 16, 4, 12, 4, 202, 160, 123); Box(32, 48, 4, 12, 4, 202, 160, 123);
        Box(0, 16, 4, 12, 4, 61, 71, 76); Box(16, 48, 4, 12, 4, 61, 71, 76);
        Fill(8, 0, 16, 8, 68, 53, 43); Fill(24, 8, 8, 8, 68, 53, 43);
        Fill(0, 8, 8, 2, 68, 53, 43); Fill(16, 8, 8, 2, 68, 53, 43);
        Fill(8, 8, 8, 8, 202, 160, 123); Fill(8, 8, 8, 2, 68, 53, 43); Fill(8, 10, 1, 2, 68, 53, 43);
        Fill(9, 11, 2, 1, 242, 235, 213); Fill(13, 11, 2, 1, 242, 235, 213);
        Fill(10, 11, 1, 1, 52, 88, 74); Fill(13, 11, 1, 1, 52, 88, 74); Fill(11, 14, 2, 1, 127, 82, 64);
        Fill(20, 20, 8, 12, 67, 109, 83); Fill(44, 20, 4, 12, 202, 160, 123); Fill(36, 52, 4, 12, 202, 160, 123);
        Fill(44, 20, 4, 4, 67, 109, 83); Fill(36, 52, 4, 4, 67, 109, 83);
        Fill(4, 20, 4, 12, 61, 71, 76); Fill(20, 52, 4, 12, 61, 71, 76);
        Fill(4, 30, 4, 2, 45, 48, 45); Fill(20, 62, 4, 2, 45, 48, 45);
        var image = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, pixels, 64 * 4);
        image.Freeze(); return image;
    }
}
