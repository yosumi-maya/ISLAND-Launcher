using System.Windows;
using System.Windows.Controls;

namespace Island.App;

public partial class MainWindow
{
    private readonly Stack<FormPage> managementPages = new();
    private FrameworkElement? currentPage;
    internal FormPage? ActiveManagementPage => managementPages.TryPeek(out var page) ? page : null;
    private IEnumerable<FrameworkElement> MainPages => new FrameworkElement[] { LibraryPage, AccountsPage, DownloadCenterView, MultiplayerPage, LogsPage, SettingsPage, AppearancePage, ManagementPageHost };

    private void ShowPage(FrameworkElement target)
    {
        if (ReferenceEquals(currentPage, target) && target.Visibility == Visibility.Visible) return;
        foreach (var page in MainPages)
        {
            PageTransitions.Reset(page); page.Visibility = ReferenceEquals(page, target) ? Visibility.Visible : Visibility.Collapsed;
        }
        currentPage = target; PageTransitions.Enter(target);
    }
    private void ClearManagement()
    {
        ManagementPageHost.Content = null;
        while (managementPages.TryPop(out var page)) page.Dispose();
        ManagementPageHost.Visibility = Visibility.Collapsed;
    }
    private void ShowManagement(FormPage page)
    {
        page.CloseRequested += (_, _) => { if (ReferenceEquals(ActiveManagementPage, page)) ReturnFromManagement(); };
        managementPages.Push(page); ManagementPageHost.Content = page;
        if (ReferenceEquals(currentPage, ManagementPageHost) && ManagementPageHost.Visibility == Visibility.Visible) PageTransitions.Enter(page);
        else ShowPage(ManagementPageHost);
    }
    private void ReturnFromManagement()
    {
        if (managementPages.TryPop(out var page)) page.Dispose();
        ManagementPageHost.Content = ActiveManagementPage;
        if (ActiveManagementPage is { } previous)
        {
            if (previous is InstancePage instance) instance.ReloadMods();
            PageTransitions.Enter(previous);
        }
        else { ShowPage(LibraryPage); ReloadLibrary(); }
    }
    private void ReturnToLibrary(object sender, RoutedEventArgs e)
    {
        if (!ready || ActiveManagementPage is null) return;
        ClearManagement(); ShowPage(LibraryPage); ReloadLibrary();
    }
    private void OpenModManagement(InstanceCard card, bool fromInstance = false)
    {
        if (operation is not null || game is not null) return;
        var page = new ModManagerPage(this, card);
        if (!fromInstance) { NavLibrary.IsChecked = true; ClearManagement(); }
        if (fromInstance) page.BackButton.Content = "←  实例管理";
        ShowManagement(page);
    }
}
