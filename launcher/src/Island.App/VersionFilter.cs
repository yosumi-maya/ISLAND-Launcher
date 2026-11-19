using System.Windows.Controls;

namespace Island.App;

internal static class VersionFilter
{
    public const string All = "全部";
    public static string Value(string? text) => string.IsNullOrWhiteSpace(text) || text.Trim() == All ? "" : text.Trim();
    public static string Display(string? text) => Value(text) is { Length: > 0 } version ? version : All;
    public static void SetVersions(ComboBox combo, IEnumerable<string> versions)
    {
        var current = Display(combo.Text);
        combo.ItemsSource = new[] { All }.Concat(versions.Select(Value).Where(value => value.Length > 0)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        combo.Text = current;
        if (current == All) combo.SelectedItem = All;
    }
    public static void SelectAll(ComboBox combo) { combo.SelectedItem = All; combo.Text = All; }
}
