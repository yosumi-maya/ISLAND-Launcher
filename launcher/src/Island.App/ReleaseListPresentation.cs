using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using BlockDock.Core;

namespace Island.App;

internal static class ReleaseListPresentation
{
    public static void Apply(ListBox list)
    {
        var rows = new Style(typeof(ListBoxItem), (Style)list.FindResource("CatalogResultRow"));
        rows.Setters.Add(new Setter(AutomationProperties.NameProperty, new Binding("Display")));
        list.ItemContainerStyle = rows;
        list.SetResourceReference(ItemsControl.ItemTemplateProperty, "CatalogReleaseTemplate");
        list.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        list.SetValue(ScrollViewer.CanContentScrollProperty, true);
        list.SetValue(VirtualizingPanel.IsVirtualizingProperty, true);
        list.SetValue(VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);
        list.SetValue(VirtualizingPanel.ScrollUnitProperty, ScrollUnit.Pixel);
        AutomationProperties.SetName(list, "可选发布版本");
    }
}

public sealed class ReleaseDetailsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is CatalogVersion release
        ? $"{release.Channel}  ·  Minecraft {string.Join(", ", release.GameVersions)}  ·  {string.Join(" / ", release.Loaders)}" : "";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
