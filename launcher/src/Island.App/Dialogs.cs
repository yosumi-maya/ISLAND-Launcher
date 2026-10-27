using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
using BlockDock.Core;
using Island.Core;

namespace Island.App;

public class FormWindow : Window
{
    protected StackPanel Body { get; } = new() { Margin = new Thickness(28) };
    protected StackPanel Actions { get; } = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
    private readonly Grid layout = new();
    public FormWindow(Window? owner, string title, string description, double width = 530)
    {
        if (owner is { IsVisible: true }) Owner = owner;
        Title = title; Width = width; SizeToContent = SizeToContent.Height; MaxHeight = Math.Min(780, SystemParameters.WorkArea.Height - 60);
        Style = (Style)FindResource(typeof(Window));
        WindowStartupLocation = Owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize; WindowStyle = WindowStyle.None; ShowInTaskbar = false;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 44, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(0) });
        SetResourceReference(BackgroundProperty, "WindowBackground");
        Body.Margin = new Thickness(28, 8, 28, 26);
        Body.Children.Add(new TextBlock { Text = title, FontSize = 23, FontWeight = FontWeights.SemiBold });
        Body.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 10, 0, 18) });
        var root = layout; root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) }); root.RowDefinitions.Add(new RowDefinition());
        var heading = new Grid { Margin = new Thickness(18, 0, 8, 0) };
        var caption = new TextBlock { Text = "岛屿  /  ISLAND", FontSize = 10, VerticalAlignment = VerticalAlignment.Center }; caption.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); heading.Children.Add(caption);
        var close = new Button { Content = "×", FontSize = 20, Width = 34, Height = 30, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right, Style = (Style)FindResource("Quiet"), ToolTip = "关闭", IsCancel = true };
        close.Click += (_, _) => Close(); WindowChrome.SetIsHitTestVisibleInChrome(close, true); heading.Children.Add(close); root.Children.Add(heading);
        var scroll = new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        var frame = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Child = root };
        frame.SetResourceReference(Border.BackgroundProperty, "WindowBackground"); frame.SetResourceReference(Border.BorderBrushProperty, "Line"); Content = frame;
    }
    protected void Label(string text) => Body.Children.Add(new TextBlock { Text = text, Margin = new Thickness(0, 14, 0, 8) });
    protected void KeepActionsVisible()
    {
        Body.Children.Remove(Actions);
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Grid.SetRow(Actions, 2);
        Actions.Margin = new Thickness(28, 12, 28, 20); layout.Children.Add(Actions);
    }
    protected void SetWorkspace(UIElement content)
    {
        var scroll = layout.Children.OfType<ScrollViewer>().Single(); layout.Children.Remove(scroll);
        Grid.SetRow(content, 1); layout.Children.Add(content);
    }
    protected Button Button(string text, RoutedEventHandler clicked, bool primary = false)
    {
        var button = new Button { Content = text, Margin = new Thickness(8, 0, 0, 0), Style = (Style)FindResource(primary ? "Primary" : typeof(Button)) };
        button.Click += clicked; return button;
    }
    public static string? Prompt(Window owner, string title, string description, string value = "")
    {
        var dialog = new FormWindow(owner, title, description);
        var input = new TextBox { Text = value }; dialog.Body.Children.Add(input);
        dialog.Actions.Children.Add(dialog.Button("取消", (_, _) => dialog.DialogResult = false));
        var confirm = dialog.Button("确定", (_, _) => dialog.DialogResult = true, true); confirm.IsDefault = true;
        dialog.Actions.Children.Add(confirm); dialog.Body.Children.Add(dialog.Actions);
        dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }
}

public sealed class MessageWindow : FormWindow
{
    public MessageWindow(Window? owner, string title, string message, string confirmText = "知道了", bool confirmation = false) : base(owner, title, message)
    {
        if (confirmation)
        {
            var cancel = Button("取消", (_, _) => DialogResult = false); cancel.IsCancel = true; cancel.IsDefault = true; Actions.Children.Add(cancel);
        }
        var confirm = Button(confirmText, (_, _) => DialogResult = true, true); confirm.IsDefault = !confirmation; Actions.Children.Add(confirm);
        Body.Children.Add(Actions);
    }
    public static void Inform(Window? owner, string message, string title = "岛屿启动器") => new MessageWindow(owner, title, message).ShowDialog();
    public static bool Confirm(Window owner, string title, string message, string confirmText) => new MessageWindow(owner, title, message, confirmText, true).ShowDialog() == true;
}

public sealed class NewInstanceWindow : FormWindow
{
    private readonly ComboBox versions = new() { IsTextSearchEnabled = true, DisplayMemberPath = "Id", MaxDropDownHeight = 260 };
    private readonly ComboBox loaders = new() { ItemsSource = Enum.GetValues<LoaderKind>() };
    private readonly ComboBox loaderVersions = new() { DisplayMemberPath = "Display", MaxDropDownHeight = 230 };
    private readonly TextBox name = new();
    private readonly TextBlock status = new() { Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox snapshots = new() { Content = "同时显示快照与历史版本" };
    private readonly CancellationTokenSource lifetime = new();
    private readonly DownloadClient network;
    private readonly MinecraftInstaller installer;
    private IReadOnlyList<GameVersion> allVersions = [];
    private readonly Button confirm;
    private int revision;
    private bool nameEdited;
    public InstallRequest? Request { get; private set; }
    public NewInstanceWindow(Window owner, AppSettings settings) : base(owner, "创建一个新世界", "选择游戏版本和加载器，安装到当前游戏目录。版本隔离可在文件夹设置中调整。")
    {
        network = new(settings); installer = new(settings, network);
        Label("Minecraft 版本"); Body.Children.Add(versions); Body.Children.Add(snapshots);
        Label("模组加载器"); Body.Children.Add(loaders);
        Label("加载器版本"); Body.Children.Add(loaderVersions);
        Label("实例名称"); Body.Children.Add(name); Body.Children.Add(status);
        Actions.Children.Add(Button("取消", (_, _) => DialogResult = false));
        confirm = Button("创建并安装  →", Confirm, true); confirm.IsEnabled = false;
        Actions.Children.Add(confirm); Body.Children.Add(Actions);
        name.TextChanged += (_, _) => { if (name.IsKeyboardFocusWithin) nameEdited = true; };
        versions.SelectionChanged += async (_, _) => await LoadLoadersAsync();
        loaders.SelectionChanged += async (_, _) => await LoadLoadersAsync();
        snapshots.Checked += (_, _) => FilterVersions(); snapshots.Unchecked += (_, _) => FilterVersions();
        loaders.SelectedItem = LoaderKind.Vanilla;
        Loaded += async (_, _) =>
        {
            try { status.Text = "正在获取官方版本列表…"; allVersions = await installer.GetVersionsAsync(lifetime.Token); FilterVersions(); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { status.Text = "版本列表读取失败：" + ex.Message; }
        };
        Closed += (_, _) => { lifetime.Cancel(); network.Dispose(); };
    }
    private void FilterVersions()
    {
        var selected = (versions.SelectedItem as GameVersion)?.Id;
        var filtered = allVersions.Where(v => snapshots.IsChecked == true || v.Type == "release").ToArray();
        versions.ItemsSource = filtered;
        versions.SelectedItem = filtered.FirstOrDefault(v => v.Id == selected) ?? filtered.FirstOrDefault();
    }
    private async Task LoadLoadersAsync()
    {
        if (versions.SelectedItem is not GameVersion game || loaders.SelectedItem is not LoaderKind loader) return;
        var currentRevision = ++revision;
        confirm.IsEnabled = false; loaderVersions.ItemsSource = null; loaderVersions.IsEnabled = loader != LoaderKind.Vanilla;
        if (!nameEdited) name.Text = game.Id + (loader == LoaderKind.Vanilla ? "-我的世界" : "-" + loader);
        if (loader == LoaderKind.Vanilla) { status.Text = "原版 Minecraft · 无模组加载器"; confirm.IsEnabled = true; return; }
        try
        {
            status.Text = "正在查询兼容的加载器版本…";
            var data = await installer.GetLoaderVersionsAsync(loader, game.Id, lifetime.Token);
            if (currentRevision != revision || lifetime.IsCancellationRequested) return;
            loaderVersions.ItemsSource = data; loaderVersions.SelectedItem = data.FirstOrDefault(v => v.Stable) ?? data.FirstOrDefault();
            confirm.IsEnabled = data.Count > 0; status.Text = data.Count == 0 ? "此 Minecraft 版本没有可用的该加载器，请切换版本。" : $"找到 {data.Count} 个加载器版本。";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (currentRevision == revision) status.Text = "加载器查询失败：" + ex.Message; }
    }
    private void Confirm(object sender, RoutedEventArgs e)
    {
        try
        {
            if (versions.SelectedItem is not GameVersion game || loaders.SelectedItem is not LoaderKind loader) return;
            var id = SafePaths.InstanceName(name.Text.Trim());
            var loaderVersion = (loaderVersions.SelectedItem as LoaderVersion)?.Version;
            if (loader != LoaderKind.Vanilla && loaderVersion is null) throw new ArgumentException("请选择加载器版本。");
            Request = new(game.Id, id, loader, loaderVersion); DialogResult = true;
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }
}

public sealed class DeviceLoginWindow : FormWindow
{
    public AuthResult? Result { get; private set; }
    public DeviceLoginWindow(Window owner, MicrosoftAuth auth, string clientId, DeviceLogin device, CancellationToken external) : base(owner, "登录 Microsoft", "在浏览器中完成官方登录。启动器不会收集你的密码。")
    {
        var code = new TextBox { Text = device.UserCode, IsReadOnly = true, FontSize = 29, FontFamily = new FontFamily("Consolas"), HorizontalContentAlignment = HorizontalAlignment.Center };
        Body.Children.Add(code);
        var status = new TextBlock { Text = "复制设备码，然后打开 Microsoft 登录页。", Margin = new Thickness(0, 15, 0, 0), TextWrapping = TextWrapping.Wrap };
        Body.Children.Add(status);
        Actions.Children.Add(Button("复制设备码", (_, _) => { Clipboard.SetText(device.UserCode); status.Text = "设备码已复制。"; }));
        Actions.Children.Add(Button("打开登录页  ↗", (_, _) => MainWindow.OpenPath(device.VerificationUrl), true));
        Body.Children.Add(Actions);
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(external);
        var closed = false;
        Loaded += async (_, _) =>
        {
            try { var result = await auth.CompleteAsync(clientId, device, lifetime.Token); lifetime.Token.ThrowIfCancellationRequested(); if (!closed) { Result = result; DialogResult = true; } }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!closed) status.Text = ex.Message + "  关闭窗口后可重试。"; }
        };
        Closed += (_, _) => { closed = true; lifetime.Cancel(); };
    }
}
