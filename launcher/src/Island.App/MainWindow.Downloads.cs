using System.IO;
using System.Windows;
using System.Windows.Input;
using BlockDock.App;
using Island.Core;

namespace Island.App;

public partial class MainWindow
{
    private Func<bool>? confirmDownloadClose;
    private bool downloadsStopped, stoppingDownloads;
    private void InitializeDownloads(ILauncherDownloads? service, DownloadQueue? queue, Func<bool>? confirmation, IContentDownloads? contentService)
    {
        confirmDownloadClose = confirmation;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.F || Keyboard.Modifiers != ModifierKeys.Control) return;
            if (DownloadCenterView.IsVisible && DownloadCenterView.FocusSearch()) e.Handled = true;
            else if (LibraryPage.IsVisible) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
        };
        DownloadCenterView.Configure(() => state.DownloadSettings(), Path.Combine(store.DirectoryPath, "logs", "downloads"), task =>
        {
            try
            {
                if (task.Work is null) { state.RememberInstance(task.Name, task.Settings.GameDirectory); store.Save(state); }
                ReloadLibrary();
                if (operation is null && game is null) StatusLabel.Text = "●  已安装：" + task.Name;
            }
            catch (Exception ex) { Log("下载已完成，刷新实例列表失败：" + ex.Message); }
        }, service, queue, () => operation is null && game is null, contentService,
            () => cards.Select(card => new ContentTarget(card.GameDirectory, card.Id,
                card.DisplayName + " · " + card.FolderName, card.Instance.Minecraft, card.Instance.Loader, card.RuntimeDirectory, card.Isolation)).ToArray(),
            () => Selected?.Key);
        DownloadCenterView.QueueChanged += (_, _) =>
        {
            if (!ready) return;
            var count = DownloadCenterView.Queue.Items.Count(t => !t.IsEnded);
            DownloadNavLabel.Text = count == 0 ? "下载中心" : $"下载中心 · {count}";
            UpdateSelected();
        };
        AllowDrop = true;
        PreviewDragEnter += PreviewArchiveDrag;
        PreviewDragOver += PreviewArchiveDrag;
        PreviewDragLeave += (_, e) =>
        {
            var point = e.GetPosition(this);
            if (point.X <= 0 || point.Y <= 0 || point.X >= ActualWidth || point.Y >= ActualHeight)
                ArchiveDropHint.Visibility = Visibility.Collapsed;
        };
        PreviewDrop += DropArchive;
        Deactivated += (_, _) => ArchiveDropHint.Visibility = Visibility.Collapsed;
    }
    private bool CanDropArchive => ready && operation is null && game is null && DownloadCenterView.CanImportArchive;
    private static string? DroppedArchive(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop) || data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } files) return null;
        var path = files[0];
        return Path.GetExtension(path).ToLowerInvariant() is ".zip" or ".mrpack" && File.Exists(path) ? path : null;
    }
    private void PreviewArchiveDrag(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var accepted = CanDropArchive && (e.AllowedEffects & DragDropEffects.Copy) != 0 && DroppedArchive(e.Data) is not null;
        e.Effects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        ArchiveDropHint.Visibility = accepted ? Visibility.Visible : Visibility.Collapsed;
    }
    private async Task ImportLocalArchiveAsync(string path)
    {
        var import = DownloadCenterView.ImportAsync(path);
        NavDownloads.IsChecked = true;
        await import;
    }
    private async void DropArchive(object sender, DragEventArgs e)
    {
        ArchiveDropHint.Visibility = Visibility.Collapsed;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Handled = true; e.Effects = DragDropEffects.None;
        if (!CanDropArchive || (e.AllowedEffects & DragDropEffects.Copy) == 0 || DroppedArchive(e.Data) is not { } path) return;
        e.Effects = DragDropEffects.Copy;
        await ImportLocalArchiveAsync(path);
    }
}
