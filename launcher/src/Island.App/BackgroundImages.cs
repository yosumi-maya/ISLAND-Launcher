using System.Security.Cryptography;
using System.Windows.Media.Imaging;
using Island.Core;

namespace Island.App;

public static class BackgroundImages
{
    public const long MaximumBytes = 30 * 1024 * 1024;
    private static readonly Dictionary<string, BitmapSource> Cache = new(StringComparer.OrdinalIgnoreCase);
    public static BitmapSource Load(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("背景图片不存在，请重新选择或使用纯色。", path);
        if (file.Length > MaximumBytes) throw new ArgumentException("背景图片不能超过 30 MB。");
        var key = file.FullName + "|" + file.LastWriteTimeUtc.Ticks + "|" + file.Length;
        if (Cache.TryGetValue(key, out var cached)) return cached;
        using var stream = File.OpenRead(file.FullName);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        if (decoder is not (PngBitmapDecoder or JpegBitmapDecoder or BmpBitmapDecoder)) throw new ArgumentException("请选择 PNG、JPEG 或 BMP 图片。");
        var frame = decoder.Frames[0];
        if (frame.PixelWidth > 16384 || frame.PixelHeight > 16384 || (long)frame.PixelWidth * frame.PixelHeight > 64_000_000)
            throw new ArgumentException("背景图片尺寸过大，请缩小到 6400 万像素以内，单边不超过 16384 像素。");
        stream.Position = 0;
        var image = new BitmapImage();
        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream;
        if (frame.PixelWidth > 1920) image.DecodePixelWidth = 1920;
        if (frame.PixelHeight > 1920 && frame.PixelHeight > frame.PixelWidth) image.DecodePixelHeight = 1920;
        image.EndInit(); image.Freeze();
        if (Cache.Count >= 12) Cache.Clear();
        Cache[key] = image; return image;
    }
    public static LauncherAppearance Persist(LauncherAppearance appearance, string settingsRoot)
    {
        var result = appearance.Normalize();
        var directory = Path.GetFullPath(Path.Combine(settingsRoot, "Backgrounds"));
        foreach (var area in new[] { SurfaceArea.Window, SurfaceArea.Hero, SurfaceArea.RecentMap })
        {
            var style = result.For(area);
            if (style.ImagePath.Length == 0) continue;
            var source = Path.GetFullPath(style.ImagePath);
            if (string.Equals(Path.GetDirectoryName(source), directory, StringComparison.OrdinalIgnoreCase)) continue;
            Load(source);
            using var input = File.OpenRead(source);
            var hash = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
            var extension = Path.GetExtension(source).ToLowerInvariant();
            if (extension is not (".png" or ".jpg" or ".jpeg" or ".bmp")) extension = ".img";
            Directory.CreateDirectory(directory);
            var destination = Path.Combine(directory, hash + extension);
            if (!File.Exists(destination))
            {
                var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.Copy(source, temporary); File.Move(temporary, destination, true); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            style.ImagePath = destination;
        }
        return result;
    }
}
