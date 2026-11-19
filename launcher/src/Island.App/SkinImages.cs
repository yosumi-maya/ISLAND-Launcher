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
    public static BitmapSource Portrait(byte[]? png, string variant) => Render(TextureOrDefault(png), false, png is not null && variant == "slim");
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
    private static readonly Lazy<BitmapSource> steve = new(() =>
    {
        using var source = typeof(SkinImages).Assembly.GetManifestResourceStream("Island.App.Steve.png")
            ?? throw new InvalidDataException("缺少内置史蒂夫皮肤。");
        using var output = new MemoryStream(); source.CopyTo(output);
        return Decode(output.ToArray());
    });
    private static BitmapSource DefaultTexture() => steve.Value;
}
