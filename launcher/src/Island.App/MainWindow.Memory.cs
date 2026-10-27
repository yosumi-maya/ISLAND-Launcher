using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Island.Core;

namespace Island.App;

public partial class MainWindow
{
    private readonly MenuItem dollCleanMemoryItem = new() { Name = "DollCleanMemoryMenuItem", Header = "清理内存" };
    private readonly DispatcherTimer dollMemoryFeedbackTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    internal Popup DollMemoryFeedback { get; } = new() { Placement = PlacementMode.Right, StaysOpen = true,
        AllowsTransparency = true, PopupAnimation = PopupAnimation.None, HorizontalOffset = 8 };

    private void InitializeDollMemoryMenu()
    {
        var menu = SidebarDoll.ContextMenu!;
        menu.Items.Add(new Separator()); menu.Items.Add(dollCleanMemoryItem);
        dollCleanMemoryItem.Click += CleanMemoryNow;
        dollMemoryFeedbackTimer.Tick += (_, _) => { dollMemoryFeedbackTimer.Stop(); DollMemoryFeedback.IsOpen = false; };
        Closed += (_, _) => { dollMemoryFeedbackTimer.Stop(); DollMemoryFeedback.IsOpen = false; };
    }

    private void ShowDollMemoryFeedback(string text) => ShowRuntimeFeedback(text, false);

    private void ShowRuntimeFeedback(string text, bool beforeLaunch, bool dismiss = true)
    {
        if (!IsVisible || WindowState == WindowState.Minimized) return;
        DollMemoryFeedback.Placement = beforeLaunch ? PlacementMode.Center : PlacementMode.Right;
        DollMemoryFeedback.PlacementTarget = beforeLaunch ? MainContentSurface : SidebarDoll.IsVisible ? SidebarDoll : SidebarAccountButton;
        var label = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontFamily = FontFamily, FontSize = 12 };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        var surface = new Border { Child = label, MaxWidth = 320, Padding = new Thickness(12), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        surface.SetResourceReference(Border.BackgroundProperty, "Surface"); surface.SetResourceReference(Border.BorderBrushProperty, "Line");
        DollMemoryFeedback.Child = surface;
        DollMemoryFeedback.IsOpen = true; dollMemoryFeedbackTimer.Stop(); if (dismiss) dollMemoryFeedbackTimer.Start();
    }

    private void ToggleLaunchMemoryCleanup(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        var previous = state.CleanMemoryBeforeLaunch;
        state.CleanMemoryBeforeLaunch = CleanMemoryBeforeLaunchBox.IsChecked == true;
        try { store.Save(state); MemoryCleanupStatus.Visibility = Visibility.Visible; MemoryCleanupStatus.Text = state.CleanMemoryBeforeLaunch ? "已启用：每次启动先清理内存，完成后再启动 Minecraft。" : "已关闭启动前内存清理。"; }
        catch (Exception ex) { state.CleanMemoryBeforeLaunch = previous; CleanMemoryBeforeLaunchBox.IsChecked = previous; Report(ex); }
    }

    private async void CleanMemoryNow(object sender, RoutedEventArgs e)
    {
        if (operation is not null || game is not null) return;
        var fromDoll = ReferenceEquals(sender, dollCleanMemoryItem);
        await RunOperationAsync(async token =>
        {
            try
            {
                var result = await CleanMemoryAsync(token, beforeLaunch: false);
                StatusLabel.Text = result.Outcome switch
                {
                    MemoryCleanupOutcome.Completed => "●  内存清理完成",
                    MemoryCleanupOutcome.WorkingSetsOnly or MemoryCleanupOutcome.StandbyOnly => "●  内存清理部分完成",
                    MemoryCleanupOutcome.AuthorizationDeclined or MemoryCleanupOutcome.PermissionDenied => "●  已跳过内存清理",
                    _ => "●  内存清理未完成，请查看结果"
                };
                if (fromDoll) ShowDollMemoryFeedback(MemoryCleanupStatus.Text);
            }
            catch (OperationCanceledException)
            {
                if (fromDoll) ShowDollMemoryFeedback("已取消内存清理任务。");
                throw;
            }
        }, "正在清理内存…");
    }

    private async Task<MemoryCleanupResult> CleanMemoryAsync(CancellationToken token, bool beforeLaunch)
    {
        OperationLabel.Text = "正在清理内存（Windows 可能请求管理员授权）…";
        MemoryCleanupStatus.Visibility = Visibility.Visible;
        MemoryCleanupStatus.Text = "正在回收系统工作集和备用内存…";
        CleanMemoryNowButton.Content = "清理中…";
        dollCleanMemoryItem.Header = "清理中…";
        dollMemoryFeedbackTimer.Stop(); DollMemoryFeedback.IsOpen = false;
        if (beforeLaunch) ShowRuntimeFeedback("正在清理内存…\n清理结束后再启动 Minecraft。", true, false);
        Log("内存清理：" + MemoryCleanupStatus.Text);
        try
        {
            var before = AvailableMemory();
            MemoryCleanupResult result;
            try { result = await cleanMemory(token); }
            catch (Exception ex) when (ex is not OperationCanceledException) { result = new(MemoryCleanupOutcome.Failed, ex.Message); }
            token.ThrowIfCancellationRequested();
            Log("内存清理：" + (beforeLaunch ? result.Message : result.Summary));
            var after = AvailableMemory();
            var readings = before is not null && after is not null ? $"系统可用内存：{before / 1024d:0.00} → {after / 1024d:0.00} GB（实时读数）" : "";
            if (readings.Length > 0) Log(readings);
            MemoryCleanupStatus.Text = result.Report(before, after);
            if (beforeLaunch) ShowRuntimeFeedback(MemoryCleanupStatus.Text, true);
            GlobalMemoryEditor.Refresh();
            if (!beforeLaunch && SettingsPage.IsVisible)
            {
                MemoryCleanupStatus.UpdateLayout(); MemoryCleanupStatus.BringIntoView();
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            MemoryCleanupStatus.Text = "已取消内存清理任务。";
            if (beforeLaunch) ShowRuntimeFeedback("已取消内存清理，本次未启动 Minecraft。", true);
            throw;
        }
        finally { CleanMemoryNowButton.Content = "立即清理"; dollCleanMemoryItem.Header = "清理内存"; }
    }
}
