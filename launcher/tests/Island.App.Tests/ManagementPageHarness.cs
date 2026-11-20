using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Island.App;
using Island.Core;

// Standalone component hosts for existing editor tests. Production navigation uses FormPage only.
internal class ManagementPageHarness : Window
{
    protected void Attach(FormPage page)
    {
        Content = page; Title = page.Title; Height = 690; ShowInTaskbar = false;
        page.CloseRequested += (_, _) => Close(); Closed += (_, _) => page.Dispose();
        Loaded += (_, _) =>
        {
            UpdateLayout();
            foreach (var button in Descendants<Button>(page).Where(button => button.Content as string == "返回")) button.Content = "关闭";
        };
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T item) yield return item;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
internal sealed class InstanceWindow : ManagementPageHarness
{
    public InstanceWindow(Window owner, InstanceCard card, LauncherState state, Action save)
    {
        Owner = owner; Width = 700;
        Attach(new InstancePage(owner, card, state, save, () => new ModManagerWindow(this, card).ShowDialog()));
    }
}
internal sealed class ModManagerWindow : ManagementPageHarness
{
    public ModManagerWindow(Window owner, InstanceCard card)
    {
        Owner = owner; Width = 920;
        Attach(new ModManagerPage(owner, card));
    }
}
