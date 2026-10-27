using System.Reflection;
using System.Security.Cryptography;
using Island.Core;

namespace Island.App;

public sealed record AppearancePreset(string Name, string Color, bool Glass = false, bool Dark = false)
{
    public LauncherAppearance Create(string settingsRoot, bool hideDoll, LaunchBarMode launchBarMode = LaunchBarMode.Always, CardAnimationMode cardAnimation = CardAnimationMode.FloatTilt)
        => Blueprint(hideDoll, Glass ? AppearancePresets.InstallGlassBackground(settingsRoot) : "") with { LaunchBarMode = launchBarMode, CardAnimation = cardAnimation };

    public bool Matches(LauncherAppearance appearance, string color)
    {
        if (!string.Equals(Color, color, StringComparison.OrdinalIgnoreCase)) return false;
        if (Glass && !string.Equals(Path.GetFileName(appearance.Window.ImagePath), AppearancePresets.GlassBackgroundName, StringComparison.OrdinalIgnoreCase)) return false;
        return appearance.Normalize() == (Blueprint(appearance.HideSidebarDoll, appearance.Window.ImagePath) with { LaunchBarMode = appearance.LaunchBarMode, CardAnimation = appearance.CardAnimation }).Normalize();
    }
    private LauncherAppearance Blueprint(bool hideDoll, string imagePath)
    {
        var result = new LauncherAppearance { HideSidebarDoll = hideDoll, DarkTheme = Dark };
        if (!Glass) return result;
        result.Window = new() { Color = "#244652", ImagePath = imagePath, ImageTint = .02 };
        result.Content = new() { Color = "#243241", Material = SurfaceMaterial.LiquidGlass, Opacity = .18, ImageTint = .05, BlurRadius = 12 };
        foreach (var area in new[] { SurfaceArea.Hero, SurfaceArea.Instances, SurfaceArea.LaunchBar, SurfaceArea.RecentMap, SurfaceArea.Panels, SurfaceArea.Sidebar })
            result.Set(area, new SurfaceAppearance { Color = "#243241", Material = SurfaceMaterial.LiquidGlass, Opacity = .86, ImageTint = .16, BlurRadius = 24 });
        result.Hero.DefaultIllustration = false;
        return result;
    }
}

public static class AppearancePresets
{
    public static IReadOnlyList<AppearancePreset> All { get; } = Array.AsReadOnly<AppearancePreset>([
        new("苔原青", "#2D7568"), new("暮海蓝", "#3B6387"), new("海岛夜色", "#5DAF98", Dark: true),
        new("森林绿", "#32634D"), new("海洋蓝", "#32699B"), new("鸢尾紫", "#7856A4"), new("玫瑰红", "#AD526E"), new("暖琥珀", "#9B6C29"), new("石墨灰", "#52616B"),
        new("纯红", "#F44336"), new("纯蓝", "#2563EB"), new("纯粉", "#EC4899"), new("纯紫", "#9333EA"), new("鲜橙", "#F97316"),
        new("明黄", "#EAB308"), new("草绿", "#22C55E"), new("青蓝", "#06B6D4"), new("樱花粉", "#F3A5C2"), new("曜石黑", "#262626"), new("流光玻璃", "#00AAC8", true)
    ]);
    private static readonly Lazy<byte[]> GlassBackground = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Island.App.PrismaticGlass.png")
            ?? throw new InvalidOperationException("内置玻璃背景无法读取。");
        using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray();
    });
    public static string GlassBackgroundName => "builtin-glass-" + Convert.ToHexString(SHA256.HashData(GlassBackground.Value)).ToLowerInvariant() + ".png";
    internal static string InstallGlassBackground(string settingsRoot)
    {
        var directory = Path.Combine(settingsRoot, "Backgrounds"); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, GlassBackgroundName);
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(GlassBackground.Value)) return path;
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, GlassBackground.Value); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return path;
    }
}
