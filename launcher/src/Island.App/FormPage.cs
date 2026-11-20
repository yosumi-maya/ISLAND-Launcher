using System.Windows;
using System.Windows.Controls;

namespace Island.App;

public class FormPage : UserControl, IDisposable
{
    protected Window HostWindow { get; }
    public string Title { get; }
    public Button BackButton { get; }
    public event EventHandler? CloseRequested;
    public event EventHandler? Closed;
    public bool IsDisposed { get; private set; }
    protected StackPanel Body { get; } = new();
    protected StackPanel Actions { get; } = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Grid layout = new();
    private readonly ScrollViewer scroll;

    protected FormPage(Window owner, string title, string description, string section)
    {
        HostWindow = owner; Title = title;
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        BackButton = Button("←  我的实例", (_, _) => Close()); BackButton.Name = "ManagementBackButton";
        BackButton.Style = (Style)FindResource("Quiet"); BackButton.HorizontalAlignment = HorizontalAlignment.Left;
        BackButton.Margin = new Thickness(-8, 0, 0, 14); header.Children.Add(BackButton);
        var caption = new TextBlock { Text = section, FontSize = 11 }; caption.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); header.Children.Add(caption);
        header.Children.Add(new TextBlock { Text = title, FontSize = 23, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, ToolTip = title });
        header.Children.Add(new TextBlock { Text = description, Style = (Style)FindResource("Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        layout.Children.Add(header);
        scroll = new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); layout.Children.Add(scroll); Content = layout;
    }
    public void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true; Closed?.Invoke(this, EventArgs.Empty); PageTransitions.Reset(this);
        GC.SuppressFinalize(this);
    }
    protected void Label(string text) => Body.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 8) });
    protected void KeepActionsVisible()
    {
        Body.Children.Remove(Actions);
        if (layout.Children.Contains(Actions)) return;
        Actions.Margin = new Thickness(0, 16, 0, 0); Grid.SetRow(Actions, 2); layout.Children.Add(Actions);
    }
    protected void SetWorkspace(UIElement content)
    {
        layout.Children.Remove(scroll); Grid.SetRow(content, 1); layout.Children.Add(content);
    }
    protected Button Button(string text, RoutedEventHandler clicked, bool primary = false)
    {
        var button = new Button { Content = text, Margin = new Thickness(8, 0, 0, 0), Style = (Style)FindResource(primary ? "Primary" : typeof(Button)) };
        button.Click += clicked; return button;
    }
}
