using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
namespace Island.App;
public partial class InstanceSettingRow : UserControl
{
    public InstanceSettingRow(string title, UIElement editor, string description = "")
    {
        InitializeComponent(); TitleText.Text = title; EditorHost.Content = editor;
        DescriptionText.Text = description; DescriptionText.Visibility = description.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(Toggle, title);
    }
    public string Summary { get => SummaryText.Text; set { SummaryText.Text = value; SummaryText.ToolTip = value; } }
    public bool Expanded { get => Toggle.IsChecked == true; set => Toggle.IsChecked = value; }
    private void ToggleChanged(object sender, RoutedEventArgs e)
    {
        if (Details is null) return;
        Details.Visibility = Toggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        Arrow.Text = Toggle.IsChecked == true ? "⌃" : "⌄";
    }
}
