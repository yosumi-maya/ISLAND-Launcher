using System.Windows.Media;
using System.Windows.Media.Imaging;
using Island.Core;

namespace Island.App;

internal sealed record ModListRow(ModFile File, string Title, string VersionLabel, string Details, ImageSource? Icon)
{
    public static ModListRow Read(ModFile file)
    {
        var metadata = ModMetadataReader.Read(file.Path, includeIcons: true)
            .FirstOrDefault(mod => mod.Id.Length > 0 || mod.Name.Length > 0);
        var title = Clean(metadata?.Name);
        var version = Clean(metadata?.Version);
        var description = Clean(metadata?.Description);
        return new(file, title.Length > 0 ? title : Path.GetFileNameWithoutExtension(file.Name),
            version.Length > 0 && !version.StartsWith('$') ? "  |  " + version : "",
            description.Length > 0 ? file.Name + " · " + description : file.Name, Decode(metadata?.Icon));
    }

    public static ModListRow Fallback(ModFile file) => new(file, Path.GetFileNameWithoutExtension(file.Name), "", file.Name, null);

    private static string Clean(string? value) => value is null ? "" :
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static ImageSource? Decode(byte[]? bytes)
    {
        if (bytes is null) return null;
        try
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 96;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or InvalidOperationException or FormatException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}
