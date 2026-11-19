using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.ComponentModel;
using BlockDock.Core;
using Island.Core;

namespace Island.App;

public sealed class InstanceCard(InstalledInstance instance, InstancePreferences preferences, string folderName = "", VersionIsolation isolation = VersionIsolation.Auto) : INotifyPropertyChanged
{
    public InstalledInstance Instance { get; } = instance;
    public InstancePreferences Preferences { get; } = preferences;
    public string Id => Instance.Name;
    public string GameDirectory => Path.GetDirectoryName(Path.GetDirectoryName(Instance.Directory))!;
    public string FolderName { get; } = folderName;
    public VersionIsolation Isolation { get; } = isolation;
    public GameDirectoryResolution Runtime { get; } = GameDirectories.Resolve(Path.GetDirectoryName(Path.GetDirectoryName(instance.Directory))!, instance.Name, isolation);
    public string RuntimeDirectory => Runtime.Directory;
    public string Key => GameFolder.NormalizePath(GameDirectory).ToUpperInvariant() + "|" + Id;
    public string DisplayName => string.IsNullOrWhiteSpace(Preferences.DisplayName) ? Instance.Name : Preferences.DisplayName;
    public string VersionLine => "Minecraft " + Instance.Minecraft;
    public string CardSubtitle => VersionLine + (FolderName.Length > 0 ? " · " + FolderName : "");
    public string Loader => Instance.LoaderLabel;
    public string Star => Preferences.Favorite ? "★" : "";
    public bool IsFavorite => Preferences.Favorite;
    public string FavoriteGlyph => IsFavorite ? "★" : "☆";
    public string FavoriteTip => IsFavorite ? "取消收藏" : "收藏此实例";
    public event PropertyChangedEventHandler? PropertyChanged;
    public void SetFavorite(bool favorite)
    {
        Preferences.Favorite = favorite;
        foreach (var name in new[] { nameof(Star), nameof(IsFavorite), nameof(FavoriteGlyph), nameof(FavoriteTip) }) PropertyChanged?.Invoke(this, new(name));
    }
    public string LastPlayed => Preferences.LastPlayed is { } date ? "上次游玩 " + date.ToLocalTime().ToString("MM.dd HH:mm") : "一个崭新的世界";
    public ImageSource Icon { get; } = ResolveIcon(instance, preferences);
    private static ImageSource ResolveIcon(InstalledInstance instance, InstancePreferences preferences)
    {
        if (preferences.IconPng is not null)
            try { return InstanceIcons.Decode(preferences.IconPng); } catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException or InvalidOperationException or FileFormatException or System.Runtime.InteropServices.COMException) { }
        if (InstanceIcons.Contains(preferences.IconKey)) return InstanceIcons.Builtin(preferences.IconKey);
        return LoadIcon(instance) ?? InstanceIcons.Default(instance.Loader);
    }
    private static ImageSource? LoadIcon(InstalledInstance instance)
    {
        foreach (var path in instance.LocalIconPaths)
        {
            try
            {
                using var stream = File.OpenRead(path);
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 96; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or System.Runtime.InteropServices.COMException or FileFormatException or UnauthorizedAccessException or ArgumentException) { }
        }
        return null;
    }
}
