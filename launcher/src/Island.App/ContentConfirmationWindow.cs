using System.Windows;
using Island.Core;

namespace Island.App;

public sealed class ContentConfirmationWindow : FormWindow
{
    public ContentPlan? SelectedPlan { get; private set; }
    public ContentConfirmationWindow(Window owner, ContentPlan plan) : base(owner, "确认资源安装", plan.Name, 820)
    {
        var confirmation = new ContentInstallConfirmation(plan);
        Actions.Children.Add(Button("取消", (_, _) => DialogResult = false));
        var confirm = Button("确认并加入队列", (_, _) => { if (!confirmation.Ready) return; SelectedPlan = confirmation.SelectedPlan(); DialogResult = true; }, true);
        confirm.IsEnabled = false; confirmation.Changed += (_, _) => confirm.IsEnabled = confirmation.Ready;
        Actions.Children.Add(confirm); KeepActionsVisible(); SetWorkspace(confirmation);
        SizeToContent = SizeToContent.Manual; Height = Math.Min(700, MaxHeight);
        Closed += (_, _) => confirmation.Close();
    }
}
