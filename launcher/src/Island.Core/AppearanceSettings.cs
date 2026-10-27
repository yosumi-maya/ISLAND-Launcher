using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace Island.Core;

public static class AppearanceSettings
{
    public const string DefaultColor = "#32634D";
    public static string NormalizeColor(string? value)
    {
        var color = value?.Trim() ?? "";
        if (!Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$")) throw new ArgumentException("主题颜色请填写 #RRGGBB，例如 #32634D。");
        return color.ToUpperInvariant();
    }
}

// Values 2-5 remain only for reading old settings and normalize to Gaussian.
public enum SurfaceMaterial { Solid = 0, Gaussian = 1, Acrylic = 2, Mica = 3, MicaAlt = 4, ThinAcrylic = 5, LiquidGlass = 6 }
public enum SurfaceArea { Window, Hero, Instances, Panels, Sidebar, LaunchBar, RecentMap, Content }
public enum LaunchBarMode { Always, OnApproach }
public enum CardAnimationMode { Off, FloatTilt, Lift, Zoom, Spring, Magnetic }

public sealed record SurfaceAppearance
{
    public SurfaceMaterial Material { get; set; }
    public string Color { get; set; } = "";
    public string ForegroundColor { get; set; } = "";
    public double Opacity { get; set; } = 1;
    public double BlurRadius { get; set; } = 24;
    public string ImagePath { get; set; } = "";
    public bool DefaultIllustration { get; set; } = true;
    public double ImageTint { get; set; } = .25;
    public SurfaceAppearance Normalize() => this with
    {
        Material = Material is SurfaceMaterial.Acrylic or SurfaceMaterial.Mica or SurfaceMaterial.MicaAlt or SurfaceMaterial.ThinAcrylic ? SurfaceMaterial.Gaussian
            : Enum.IsDefined(Material) ? Material : SurfaceMaterial.Solid,
        Color = ValidColor(Color),
        ForegroundColor = ValidColor(ForegroundColor),
        Opacity = Clamp(Opacity, 0, 1, 1), BlurRadius = Material is SurfaceMaterial.Mica or SurfaceMaterial.MicaAlt ? 64 : Clamp(BlurRadius, 0, 64, 24),
        ImageTint = Clamp(ImageTint, 0, 1, .25), ImagePath = ImagePath ?? ""
    };
    internal static double Clamp(double value, double min, double max, double fallback) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
    private static string ValidColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        try { return AppearanceSettings.NormalizeColor(value); } catch (ArgumentException) { return ""; }
    }
}

public sealed record LauncherAppearance
{
    public bool DarkTheme { get; set; }
    public bool HideSidebarDoll { get; set; }
    public LaunchBarMode LaunchBarMode { get; set; }
    private CardAnimationMode? cardAnimation;
    public CardAnimationMode CardAnimation { get => cardAnimation ?? CardAnimationMode.FloatTilt; set => cardAnimation = value; }
    // Read the old switch in either JSON property order. Also keep it for older launcher versions.
    [JsonPropertyName("CardHoverEffects")]
    public bool LegacyCardHoverEffects
    {
        get => CardAnimation != CardAnimationMode.Off;
        set { if (cardAnimation is null) cardAnimation = value ? CardAnimationMode.FloatTilt : CardAnimationMode.Off; }
    }
    public double WindowOpacity { get; set; } = 1;
    public SurfaceAppearance Window { get; set; } = new();
    public SurfaceAppearance Hero { get; set; } = new() { ImageTint = .4 };
    public SurfaceAppearance Instances { get; set; } = new();
    public SurfaceAppearance Panels { get; set; } = new();
    public SurfaceAppearance Sidebar { get; set; } = new();
    public SurfaceAppearance LaunchBar { get; set; } = new();
    public SurfaceAppearance RecentMap { get; set; } = new();
    public SurfaceAppearance Content { get; set; } = new();
    public SurfaceAppearance For(SurfaceArea area) => area switch
    {
        SurfaceArea.Window => Window, SurfaceArea.Hero => Hero, SurfaceArea.Instances => Instances,
        SurfaceArea.Panels => Panels, SurfaceArea.Sidebar => Sidebar, SurfaceArea.LaunchBar => LaunchBar,
        SurfaceArea.Content => Content, _ => RecentMap
    };
    public void Set(SurfaceArea area, SurfaceAppearance value)
    {
        switch (area)
        {
            case SurfaceArea.Window: Window = value; break;
            case SurfaceArea.Hero: Hero = value; break;
            case SurfaceArea.Instances: Instances = value; break;
            case SurfaceArea.Panels: Panels = value; break;
            case SurfaceArea.Sidebar: Sidebar = value; break;
            case SurfaceArea.LaunchBar: LaunchBar = value; break;
            case SurfaceArea.RecentMap: RecentMap = value; break;
            case SurfaceArea.Content: Content = value; break;
        }
    }
    public LauncherAppearance Normalize()
    {
        var result = this with { WindowOpacity = SurfaceAppearance.Clamp(WindowOpacity, .4, 1, 1), LaunchBarMode = Enum.IsDefined(LaunchBarMode) ? LaunchBarMode : LaunchBarMode.Always,
            CardAnimation = Enum.IsDefined(CardAnimation) ? CardAnimation : CardAnimationMode.FloatTilt };
        foreach (var area in Enum.GetValues<SurfaceArea>()) result.Set(area, (For(area) ?? new()).Normalize());
        return result;
    }
}
