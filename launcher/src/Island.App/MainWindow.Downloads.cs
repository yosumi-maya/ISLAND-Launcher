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
        DragOver += (_, e) =>
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            e.Effects = e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length == 1 && Archive(files[0]) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        Drop += async (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } files || !Archive(files[0])) return;
            e.Handled = true; NavDownloads.IsChecked = true; await DownloadCenterView.ImportAsync(files[0]);
        };
    }
    private static bool Archive(string path) => Path.GetExtension(path).ToLowerInvariant() is ".zip" or ".mrpack";
}
