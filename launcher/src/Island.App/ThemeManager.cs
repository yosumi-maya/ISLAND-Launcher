using System.Windows;
using System.Windows.Media;
using Island.Core;

namespace Island.App;

public static class ThemeManager
{
    public static Color Mix(Color first, Color second, double amount) => Color.FromRgb((byte)Math.Round(first.R + (second.R - first.R) * amount), (byte)Math.Round(first.G + (second.G - first.G) * amount), (byte)Math.Round(first.B + (second.B - first.B) * amount));
    public static double Contrast(Color first, Color second)
    {
        static double Luminance(Color color) => .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        var a = Luminance(first); var b = Luminance(second); return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
    public static Color Readable(Color proposed, Color background, double ratio = 4.5)
    {
        var destination = Contrast(Colors.White, background) > Contrast(Colors.Black, background) ? Colors.White : Colors.Black;
        for (var step = 0; step <= 20; step++)
        {
            var candidate = Mix(proposed, destination, step / 20d);
            if (Contrast(candidate, background) >= ratio) return candidate;
        }
        return destination;
    }
    public static void Apply(string value, ResourceDictionary? resources = null, bool dark = false)
    {
        var accent = (Color)ColorConverter.ConvertFromString(AppearanceSettings.NormalizeColor(value));
        Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);
        var surface = Hex(dark ? "#1D2D32" : "#FFFFFF");
        if (dark && Contrast(accent, surface) < 2) accent = Mix(accent, Colors.White, .45);
        var ink = Hex(dark ? "#EDF4F1" : "#22352E");
        var muted = Hex(dark ? "#B0C2BC" : "#52645C");
        var panel = Hex(dark ? "#192A2E" : "#F3F4F6");
        var neutralSelection = Hex(dark ? "#293B40" : "#ECF3F1");
        var neutralFocus = Hex(dark ? "#82A49A" : "#718F85");
        var neutralHover = Hex(dark ? "#31434A" : "#E6EAEC");
        var soft = dark ? Mix(surface, accent, .18) : Mix(accent, Colors.White, .91);
        var hover = dark ? Mix(surface, accent, .26) : Mix(accent, Colors.White, .85);
        var primaryHover = Mix(accent, dark ? Colors.White : Colors.Black, .09);
        var primaryPressed = Mix(accent, dark ? Colors.White : Colors.Black, .16);
        var onAccent = Contrast(Colors.White, accent) > Contrast(Colors.Black, accent) ? Colors.White : Colors.Black;
        var accentSelection = Mix(panel, accent, .16);
        var hero = soft;
        var heroSurface = surface;
        var colors = new Dictionary<string, Color>
        {
            ["Accent"] = accent, ["OnAccent"] = onAccent, ["AccentInk"] = Readable(accent, soft),
            ["Ink"] = ink, ["Muted"] = muted, ["DangerInk"] = Readable(Hex(dark ? "#FF9B94" : "#B42318"), surface), ["Line"] = Hex(dark ? "#3A5055" : "#D3DFD8"),
            ["WindowBackground"] = Hex(dark ? "#142227" : "#FFFFFF"), ["SidebarBackground"] = panel, ["ContentBackground"] = panel,
            ["Surface"] = surface, ["AccentSoft"] = soft, ["AccentHover"] = hover,
            ["AccentSelection"] = accentSelection, ["AccentSelectionInk"] = Readable(accent, accentSelection),
            ["SelectionBackground"] = neutralSelection, ["HoverBackground"] = neutralHover,
            ["SelectionBorder"] = neutralFocus, ["Focus"] = neutralFocus, ["SelectionFill"] = Mix(neutralFocus, surface, .55),
            ["StatusAccent"] = Readable(Hex(dark ? "#87C8B4" : AppearanceSettings.DefaultColor), panel),
            ["PrimaryHover"] = primaryHover, ["PrimaryPressed"] = primaryPressed,
            ["OnPrimaryHover"] = Readable(onAccent, primaryHover), ["OnPrimaryPressed"] = Readable(onAccent, primaryPressed),
            ["NoticeBackground"] = Hex(dark ? "#243A40" : "#E8F0ED"), ["NoticeInk"] = Hex(dark ? "#C5DDD3" : "#3B594E"),
            ["HeroBackground"] = hero, ["HeroSurface"] = heroSurface,
            ["HeroBorder"] = Hex(dark ? "#3A5055" : "#D3DFD8"), ["HeroText"] = ink, ["HeroMuted"] = muted,
            ["ScrollThumb"] = Hex(dark ? "#56746B" : "#A7BCB0"), ["ScrollHover"] = Hex(dark ? "#8EAD9F" : "#718F7D")
        };
        var target = resources ?? Application.Current.Resources;
        foreach (var (key, color) in colors) target[key] = new SolidColorBrush(color);
    }
    private static double Linear(byte channel) { var value = channel / 255d; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
}
