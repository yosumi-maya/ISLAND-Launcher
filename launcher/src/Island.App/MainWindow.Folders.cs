using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BlockDock.Core;
using Island.Core;

namespace Island.App;

public partial class MainWindow
{
    private bool updatingFolders;
    private void RefreshFolderTabs()
    {
        var previous = updatingFolders; updatingFolders = true;
        try
        {
            FilterFavorites.IsChecked = state.ShowFavorites;
            FolderTabs.Items.Clear();
            foreach (var folder in state.GameFolders)
            {
                var tab = new RadioButton
                {
                    Content = new TextBlock { Text = folder.Name, MaxWidth = 155, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis },
                    Tag = folder.Id, ToolTip = folder.Directory, GroupName = "LibraryFolders", Style = (Style)FindResource("Filter"),
                    IsChecked = !state.ShowFavorites && folder.Id == state.SelectedGameFolderId
                };
                tab.Checked += ChangeGameFolder; FolderTabs.Items.Add(tab);
            }
        }
        finally { updatingFolders = previous; }
    }
    private void ChangeGameFolder(object sender, RoutedEventArgs e)
    {
        if (!ready || updatingFolders || sender is not RadioButton { Tag: string id }) return;
        var folder = state.GameFolders.First(folder => folder.Id == id);
        if (operation is not null && game is null) { RefreshFolderTabs(); return; }
        try { ActivateGameFolder(folder.Id); StatusLabel.Text = game is not null ? "●  游戏运行中 · 当前浏览：" + folder.Name : "●  当前游戏目录：" + folder.Name; }
        catch (Exception ex) { RefreshFolderTabs(); Report(ex); }
    }
    private void ShowFavoriteFolders(object sender, RoutedEventArgs e)
    {
        if (!ready || updatingFolders) return;
        state.ShowFavorites = true; RefreshFolderTabs(); ApplyFilter();
    }
    private void ScrollFolderTabs(object sender, MouseWheelEventArgs e)
    {
        if (FolderTabsScroll.ScrollableWidth <= 0) return;
        FolderTabsScroll.ScrollToHorizontalOffset(FolderTabsScroll.HorizontalOffset - e.Delta); e.Handled = true;
    }
    private void ActivateGameFolder(string id)
    {
        updatingFolders = true;
        try
        {
            state.SelectGameFolder(id); state.ShowFavorites = false; RootBox.Text = state.GameDirectory;
            SearchBox.Clear();
        }
        finally { updatingFolders = false; }
        ReloadLibrary(false); store.Save(state);
    }
    private static GameFolderDraft DescribeFolder(GameFolder folder)
    {
        var count = -1;
        try { if (Directory.Exists(folder.Directory)) count = MinecraftInstaller.ListInstalled(folder.Directory).Count; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return new(folder.Directory, folder.Name, count, true, folder.Isolation);
    }
    private void ManageGameFolders(object sender, RoutedEventArgs e)
    {
        if (operation is not null || game is not null) return;
        var dialog = new GameFoldersWindow(this, state.GameFolders.Select(DescribeFolder), false);
        if (dialog.ShowDialog() != true) return;
        try
        {
            foreach (var edit in dialog.Result)
            {
                var folder = state.GameFolders.First(folder => GameFolder.SamePath(folder.Directory, edit.Directory));
                folder.Name = edit.Name; folder.Isolation = edit.Isolation;
            }
            store.Save(state); ReloadLibrary(); StatusLabel.Text = "●  目录设置已保存";
        }
        catch (Exception ex) { Report(ex); }
    }
    private void CommitFolderImports(IReadOnlyList<GameFolderDraft> imports)
    {
        if (imports.Count == 0) return;
        foreach (var folder in imports) GameFolder.ValidateName(folder.Name);
        GameFolder? first = null;
        foreach (var folder in imports)
        {
            var registered = state.AddGameFolder(folder.Directory, folder.Name);
            registered.Name = GameFolder.ValidateName(folder.Name); registered.Isolation = folder.Isolation; first ??= registered;
        }
        ActivateGameFolder(first!.Id);
        Log($"已连接 {imports.Count} 个游戏目录，当前：{state.ActiveGameFolder.Name}");
        StatusLabel.Text = $"●  已连接 {imports.Count} 个目录 · 当前：{state.ActiveGameFolder.Name}";
    }
}
