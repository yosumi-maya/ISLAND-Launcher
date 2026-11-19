using System.Windows.Media.Imaging;

namespace Island.App;

internal static class WorldThumbnail
{
    public static BitmapSource? Load(string? path)
    {
        if (path is null) return null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > 4 * 1024 * 1024) return null;
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            if (frame.PixelWidth > 4096 || frame.PixelHeight > 4096) return null;
            stream.Position = 0;
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 320; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FileFormatException or System.Runtime.InteropServices.COMException) { return null; }
    }
}
