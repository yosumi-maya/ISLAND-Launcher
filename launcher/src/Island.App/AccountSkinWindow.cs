using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Island.Core;
using Microsoft.Win32;

namespace Island.App;

public sealed class AccountSkinWindow : FormWindow
{
    private readonly SkinPreview3D modelPreview = new() { Name = "SkinModelPreview", Height = 330 };
    private readonly Image avatar = new() { Width = 56, Height = 56, Margin = new Thickness(0, 0, 0, 14) };
    private readonly ComboBox variant = new() { Name = "SkinVariantBox", DisplayMemberPath = "Name", SelectedValuePath = "Value" };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private readonly StackPanel controls = new();
    private readonly Button save;
    private readonly CancellationTokenSource lifetime = new();
    private byte[]? png;
    private bool busy, changed, closeAfterCancel;
    public AccountSkinWindow(Window owner, PlayerAccount account, Func<byte[]?, string, CancellationToken, Task> apply,
        Func<CancellationToken, Task<PlayerAccount>>? synchronize = null, Func<string?>? picker = null)
        : base(owner, account.Name + " · 玩家皮肤", account.Microsoft
            ? "选择皮肤并保存，会上传至此 Microsoft 账号的 Minecraft Java 档案。"
            : "保存为此玩家的启动器皮肤与头像。离线游戏内显示需要额外的皮肤模组支持。", 780)
    {
        png = account.SkinPng;
        RenderOptions.SetBitmapScalingMode(avatar, BitmapScalingMode.NearestNeighbor);
        var preview = new Grid(); preview.ColumnDefinitions.Add(new ColumnDefinition()); preview.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        var stage = new StackPanel(); stage.Children.Add(modelPreview);
        var viewTools = new DockPanel { Margin = new Thickness(12, 0, 12, 10) };
        var resetView = Button("重置视角", (_, _) => modelPreview.ResetView()); resetView.Padding = new Thickness(10, 6, 10, 6);
        DockPanel.SetDock(resetView, Dock.Right); viewTools.Children.Add(resetView);
        viewTools.Children.Add(new TextBlock { Text = "拖动旋转 · 滚轮缩放", Style = (Style)FindResource("Hint"), VerticalAlignment = VerticalAlignment.Center }); stage.Children.Add(viewTools);
        var scene = new Border { Child = stage, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 20, 0), VerticalAlignment = VerticalAlignment.Top };
        scene.SetResourceReference(Border.BackgroundProperty, "AccentSoft"); preview.Children.Add(scene);
        Grid.SetColumn(controls, 1); controls.VerticalAlignment = VerticalAlignment.Center; preview.Children.Add(controls); Body.Children.Add(preview);
        avatar.HorizontalAlignment = HorizontalAlignment.Left; controls.Children.Add(avatar);
        controls.Children.Add(new TextBlock { Text = "玩家模型", Margin = new Thickness(0, 0, 0, 8) });
        variant.ItemsSource = new[] { new { Name = "标准 · Classic", Value = "classic" }, new { Name = "纤细 · Slim", Value = "slim" } };
        variant.SelectedValue = account.SkinVariant; if (variant.SelectedIndex < 0) variant.SelectedIndex = 0;
        variant.SelectionChanged += (_, _) => { changed = true; UpdatePreview(); }; controls.Children.Add(variant);
        controls.Children.Add(new TextBlock { Text = "预览动作", Margin = new Thickness(0, 14, 0, 8) });
        var poses = new WrapPanel(); var poseGroup = "SkinPoses" + Guid.NewGuid().ToString("N");
        foreach (var pose in Enum.GetValues<SkinPose>())
        {
            var option = new RadioButton { Content = SkinPreview3D.PoseName(pose), Tag = pose, GroupName = poseGroup, Style = (Style)FindResource("Filter"), Margin = new Thickness(0, 0, 6, 6), IsChecked = pose == SkinPose.Standing };
            option.Checked += (_, _) => modelPreview.SetPose(pose); poses.Children.Add(option);
        }
        controls.Children.Add(poses);
        var outer = new CheckBox { Name = "ShowSkinOuterLayer", Content = "显示外层（帽子与衣物）", IsChecked = true, Margin = new Thickness(0, 10, 0, 0) };
        outer.Checked += (_, _) => modelPreview.ShowOuterLayer = true; outer.Unchecked += (_, _) => modelPreview.ShowOuterLayer = false; controls.Children.Add(outer);
        var look = new CheckBox { Name = "SkinFollowPointer", Content = "头部跟随鼠标", IsChecked = true, Margin = new Thickness(0, 9, 0, 0) };
        look.Checked += (_, _) => modelPreview.FollowPointer = true; look.Unchecked += (_, _) => modelPreview.FollowPointer = false; controls.Children.Add(look);
        var import = Button("选择 PNG 皮肤", (_, _) =>
        {
            try
            {
                var path = picker is null ? PickSkin() : picker(); if (path is null) return;
                if (new FileInfo(path).Length > PlayerSkins.MaximumBytes) throw new ArgumentException("请选择不超过 1 MB 的 PNG 皮肤文件。");
                var candidate = File.ReadAllBytes(path); var decoded = SkinImages.Decode(candidate);
                png = candidate;
                if (decoded.PixelHeight == 32) variant.SelectedValue = "classic";
                changed = true; status.Text = Path.GetFileName(path) + " · " + decoded.PixelWidth + " × " + decoded.PixelHeight;
                UpdatePreview();
            }
            catch (Exception ex) { status.Text = ex.Message; }
        });
        import.Margin = new Thickness(0, 14, 0, 0); controls.Children.Add(import);
        var reset = Button("恢复默认皮肤", (_, _) => { png = null; variant.SelectedValue = "classic"; changed = true; status.Text = "保存后恢复默认皮肤。"; UpdatePreview(); });
        reset.Margin = new Thickness(0, 8, 0, 0); controls.Children.Add(reset);
        if (synchronize is not null)
        {
            var sync = Button("同步正版皮肤", async (_, _) =>
            {
                await Work(async () =>
                {
                    var updated = await synchronize(lifetime.Token); png = updated.SkinPng; variant.SelectedValue = updated.SkinVariant;
                    changed = false; status.Text = "已同步账号当前皮肤。"; UpdatePreview();
                });
            });
            sync.Margin = new Thickness(0, 8, 0, 0); controls.Children.Add(sync);
        }
        status.Style = (Style)FindResource("Hint"); Body.Children.Add(status);
        status.Text = png is null ? "尚未配置皮肤。支持 64 × 64 或旧版 64 × 32 PNG。" : "已保存的玩家皮肤，可更换或恢复默认。";
        Actions.Children.Add(Button("取消", (_, _) => { if (busy) { closeAfterCancel = true; lifetime.Cancel(); } else Close(); }));
        save = Button(account.Microsoft ? "保存并上传" : "保存皮肤", async (_, _) =>
        {
            if (!changed) { DialogResult = true; return; }
            if (await Work(async () =>
            {
                var model = (string)variant.SelectedValue;
                if (png is not null) { PlayerSkins.Validate(png, model); SkinImages.Decode(png); }
                await apply(png, model, lifetime.Token);
            })) DialogResult = true;
        }, true);
        Actions.Children.Add(save); Body.Children.Add(Actions); KeepActionsVisible(); UpdatePreview();
        Closing += (_, e) =>
        {
            if (busy) { e.Cancel = true; closeAfterCancel = true; lifetime.Cancel(); }
        };
        Closed += (_, _) => lifetime.Cancel();
    }
    private async Task<bool> Work(Func<Task> task)
    {
        if (busy) return false;
        busy = true; controls.IsEnabled = false; save.IsEnabled = false; status.Text = "正在处理皮肤…";
        try { await task(); return !closeAfterCancel; }
        catch (OperationCanceledException) { status.Text = "已取消。"; }
        catch (Exception ex) { status.Text = ex.Message; }
        finally
        {
            busy = false; controls.IsEnabled = true; save.IsEnabled = true;
            if (closeAfterCancel) Close();
        }
        return false;
    }
    private void UpdatePreview()
    {
        avatar.Source = SkinImages.Avatar(png);
        var model = variant.SelectedValue as string ?? "classic";
        if (png is not null && SkinImages.Decode(png).PixelHeight == 32 && model == "slim")
        {
            variant.SelectedValue = "classic"; status.Text = "旧版 64 × 32 皮肤使用标准模型；保留帽子外层。"; return;
        }
        modelPreview.SetSkin(png, model);
    }
    private string? PickSkin()
    {
        var dialog = new OpenFileDialog { Title = "选择 Minecraft PNG 皮肤", Filter = "PNG 皮肤|*.png" };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }
}
