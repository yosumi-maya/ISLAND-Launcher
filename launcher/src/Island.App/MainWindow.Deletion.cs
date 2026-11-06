using System.Windows;
using Island.Core;
using Microsoft.VisualBasic.FileIO;

namespace Island.App;

public partial class MainWindow
{
    private readonly Action<string> recycleInstance;
    private static void RecycleInstanceDirectory(string path) => FileSystem.DeleteDirectory(path,
        UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
    internal bool DeleteInstance(InstanceCard card, Window owner)
    {
        void RequireIdle()
        {
            if (operation is not null || game is not null || DownloadCenterView.Queue.HasUnfinishedTasks)
                throw new InvalidOperationException("请先结束游戏和当前任务，并完成或取消下载，再删除实例。");
        }
        string Target() => InstanceDeletion.Target(card.GameDirectory, card.Id, card.Instance.Directory, state.GameFolders.Select(folder => folder.Directory));
        RequireIdle(); var target = Target();
        if (!MessageWindow.Confirm(owner, "删除实例", $"确定删除「{card.DisplayName}」？\n\n删除目录：\n{target}\n\n此版本目录及其中的模组、存档和配置将移至回收站。共享根目录与其他实例保留。", "删除实例")) return false;
        RequireIdle(); target = Target();
        try { recycleInstance(target); }
        catch (OperationCanceledException) { return false; }
        if (Directory.Exists(target)) throw new IOException("实例目录未删除，已保留实例记录。");
        state.ForgetInstance(card.GameDirectory, card.Id);
        try { store.Save(state); }
        catch (Exception ex) { MessageWindow.Inform(owner, "实例目录已删除，但记录保存失败：" + ex.Message); }
        ReloadLibrary(); StatusLabel.Text = "●  已删除实例：" + card.DisplayName;
        return true;
    }
}
