using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Island.App;

// Each row owns its image and fallback; only frozen decoded artwork is shared.
public sealed class PackArtwork : Grid
{
    public static readonly DependencyProperty IconUrlProperty = DependencyProperty.Register(nameof(IconUrl), typeof(string), typeof(PackArtwork), new PropertyMetadata(null, Changed));
    public static readonly DependencyProperty BadgeProperty = DependencyProperty.Register(nameof(Badge), typeof(string), typeof(PackArtwork), new PropertyMetadata("", Changed));
    public string? IconUrl { get => (string?)GetValue(IconUrlProperty); set => SetValue(IconUrlProperty, value); }
    public string Badge { get => (string)GetValue(BadgeProperty); set => SetValue(BadgeProperty, value); }
    private readonly TextBlock initials = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 16, FontWeight = FontWeights.SemiBold };
    private readonly Image artwork = new() { Stretch = Stretch.Uniform };
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly SemaphoreSlim Slots = new(4);
    private static readonly Dictionary<string, Task<BitmapSource?>> Cache = new(StringComparer.Ordinal);
    private int revision;
    public PackArtwork()
    {
        SetResourceReference(BackgroundProperty, "AccentSoft"); initials.SetResourceReference(TextBlock.ForegroundProperty, "AccentInk");
        Children.Add(initials); Children.Add(artwork); ClipToBounds = true;
        SizeChanged += (_, _) => Clip = new RectangleGeometry(new Rect(RenderSize), 8, 8);
    }
    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        var icon = (PackArtwork)target;
        if (args.Property == BadgeProperty) icon.initials.Text = icon.Badge;
        else _ = icon.RefreshAsync();
    }
    private async Task RefreshAsync()
    {
        var current = ++revision; artwork.Source = null; initials.Visibility = Visibility.Visible;
        if (!Uri.TryCreate(IconUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return;
        Task<BitmapSource?> pending;
        lock (Cache)
        {
            if (!Cache.TryGetValue(uri.AbsoluteUri, out pending!))
            {
                if (Cache.Count >= 128) Cache.Remove(Cache.Keys.First());
                Cache[uri.AbsoluteUri] = pending = LoadAsync(uri);
            }
        }
        var bitmap = await pending;
        if (current != revision || bitmap is null) return;
        artwork.Source = bitmap; initials.Visibility = Visibility.Collapsed;
    }
    private static async Task<BitmapSource?> LoadAsync(Uri uri)
    {
        await Slots.WaitAsync().ConfigureAwait(false);
        try
        {
            using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 4 * 1024 * 1024) return null;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var data = new MemoryStream(); var buffer = new byte[8192]; int read;
            while ((read = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (data.Length + read > 4 * 1024 * 1024) return null;
                data.Write(buffer, 0, read);
            }
            data.Position = 0;
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 96; bitmap.StreamSource = data; bitmap.EndInit(); bitmap.Freeze(); return bitmap;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or ArgumentException or InvalidOperationException or NotSupportedException or FormatException) { return null; }
        finally { Slots.Release(); }
    }
}
