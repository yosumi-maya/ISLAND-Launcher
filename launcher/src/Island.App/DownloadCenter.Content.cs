using System.Windows;
using System.Windows.Controls;
using BlockDock.App;
using BlockDock.Core;
using Island.Core;

namespace Island.App;

public partial class DownloadCenter
{
    private IContentDownloads contentService = new ContentDownloads();
    private Func<IReadOnlyList<ContentTarget>> targets = () => [];
    private Func<string?> preferredTarget = () => null;
    private ContentKind? contentKind;
    private ContentFilter? appliedContentFilter;
    private bool updatingContent;
    private string catalogPage = "Packs";
    private string CatalogLabel => contentKind is { } kind ? ContentDownloads.Label(kind) : "整合包";
    private void SelectCatalog(string page)
    {
        var changed = catalogPage != page;
        catalogPage = page; contentKind = page switch { "Mods" => ContentKind.Mod, "Shaders" => ContentKind.Shader, "Resources" => ContentKind.ResourcePack, _ => null };
        ContentTargetPanel.Visibility = contentKind is null ? Visibility.Collapsed : Visibility.Visible;
        ChoosePackButton.Content = contentKind is null ? "选择版本并安装  →" : "选择版本并下载  →";
        PackQuery.ToolTip = CatalogLabel + "名称";
        CatalogSearchLabel.Text = "搜索" + CatalogLabel;
        System.Windows.Automation.AutomationProperties.SetName(PackQuery, CatalogSearchLabel.Text);
        PackQueryPlaceholder.Text = "输入" + CatalogLabel + "名称，或直接浏览全部…";
        if (changed)
        {
            PackLoader.ItemsSource = contentKind switch
            {
                ContentKind.Mod => new[] { "全部加载器", "Forge", "Fabric", "NeoForge", "Quilt" },
                ContentKind.Shader => new[] { "全部光影格式", "Iris", "OptiFine" },
                ContentKind.ResourcePack => new[] { "不适用" },
                _ => new[] { "全部加载器", "Forge", "Fabric", "NeoForge" }
            };
            PackLoader.SelectedIndex = 0; PackLoader.IsEnabled = contentKind != ContentKind.ResourcePack;
            CatalogLoaderLabel.Text = contentKind == ContentKind.Shader ? "光影格式" : "加载器";
            ResetSearch(this, new RoutedEventArgs());
        }
        if (contentKind is not null) RefreshContentTargets();
    }
    private void RefreshContentTargets()
    {
        var previous = (ContentTargetBox.SelectedItem as ContentTarget)?.Key ?? preferredTarget();
        var available = targets();
        updatingContent = true;
        try { ContentTargetBox.ItemsSource = available; ContentTargetBox.SelectedItem = available.FirstOrDefault(target => target.Key == previous) ?? available.FirstOrDefault(); }
        finally { updatingContent = false; }
        ApplyContentTarget();
    }
    private void ContentTargetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingContent || contentKind is null) return;
        searchCancellation?.Cancel(); searchCancellation = null;
        PackResults.ItemsSource = null; PackEmpty.Visibility = Visibility.Visible; PackEmpty.Text = "已切换目标实例，请重新搜索。";
        hasNext = false; pageIndex = 0; SetSearchBusy(false);
        ApplyContentTarget();
    }
    private void ApplyContentTarget()
    {
        if (ContentTargetBox.SelectedItem is not ContentTarget target)
        {
            ContentTargetHint.Text = "还没有已安装的实例。可先浏览资源，安装前需创建或导入实例。"; return;
        }
        if (contentKind == ContentKind.Mod)
            PackLoader.SelectedItem = PackLoader.Items.Cast<string>().FirstOrDefault(value => value.Equals(target.ModLoader, StringComparison.OrdinalIgnoreCase)) ?? PackLoader.Items[0];
        ContentTargetBox.ToolTip = ContentTargetHint.ToolTip = target.RuntimeDirectory;
        ContentTargetHint.Text = (GameFolder.SamePath(target.RuntimeDirectory, target.GameDirectory) ? "共享目录，会影响使用此目录的其他实例。" : "独立目录，仅为所选实例添加内容。")
            + (contentKind == ContentKind.Shader ? " 光影需 Iris / OptiFine 等组件支持，下载后在游戏内启用。" : contentKind == ContentKind.ResourcePack ? " 下载后在游戏内启用资源包。" : target.ModLoader.Length == 0 ? " 原版实例需先安装 Mod 加载器。" : " 必需前置会在安装前列出。");
    }
    private ContentFilter ReadContentFilter() => new(contentKind ?? ContentKind.Mod, PackQuery.Text.Trim(), VersionFilter.Value(PackGameVersion.Text),
        PackLoader.SelectedIndex > 0 ? (PackLoader.SelectedItem as string ?? "").ToLowerInvariant() : "", (PackSort)PackSortBox.SelectedIndex);
    private void OpenContent(CatalogProject project)
    {
        if (ContentTargetBox.SelectedItem is not ContentTarget target) { PackSummary.Text = "请先创建或导入实例，再选择安装目标。"; return; }
        if (target.Minecraft == "未知版本") { PackSummary.Text = "无法识别目标实例的 Minecraft 版本，请检查或重新导入该实例。"; return; }
        if (!canInstall()) { PackSummary.Text = "请等待当前启动任务或游戏结束后再安装资源。"; return; }
        var filter = ReadContentFilter() with { Minecraft = target.Minecraft, Loader = contentKind == ContentKind.Mod ? target.ModLoader : contentKind == ContentKind.Shader ? ReadContentFilter().Loader : "" };
        if (filter.Kind == ContentKind.Mod && target.ModLoader is not ("forge" or "fabric" or "neoforge" or "quilt")) { PackSummary.Text = "该实例需要先安装 Forge、Fabric、NeoForge 或 Quilt 加载器。"; return; }
        var snapshot = DownloadAvailability.Snapshot(settings());
        new ContentReleaseWindow(OwnerWindow, project, filter, target, snapshot, contentService, plan => EnqueueContent(plan, snapshot)).ShowDialog();
    }
    internal DownloadTask EnqueueContent(ContentPlan plan, AppSettings snapshot)
    {
        if (!canInstall()) throw new InvalidOperationException("请等待当前启动任务或游戏结束后再添加下载任务。");
        var task = Queue.EnqueueWork(DownloadAvailability.Snapshot(snapshot), new(plan.Name,
            plan.Target.Display + $" · {plan.Files.Count} 个资源文件", plan.TargetFiles,
            (options, progress, token) => contentService.InstallAsync(options, plan, progress, token), plan.IconUrl));
        TasksTab.IsChecked = true; return task;
    }
}

public sealed class ContentReleaseWindow : FormWindow
{
    private readonly ListBox releases = new() { Name = "ContentReleaseList", Height = 210, DisplayMemberPath = "Display" };
    private readonly TextBox search = new() { ToolTip = "筛选发布名称或版本号" };
    private readonly ComboBox channel = new() { ItemsSource = new[] { "全部通道", "正式版", "测试版", "预览版" }, SelectedIndex = 0 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private readonly Button install = new();
    private readonly CancellationTokenSource lifetime = new();
    private IReadOnlyList<CatalogVersion> all = [];
    private bool closed;
    public ContentReleaseWindow(Window owner, CatalogProject project, ContentFilter filter, ContentTarget target, AppSettings settings,
        IContentDownloads service, Action<ContentPlan> enqueue) : base(owner, "选择" + ContentDownloads.Label(filter.Kind) + "版本", project.Title + "\n" + target.Display + "\n" + target.RuntimeDirectory, 820)
    {
        Label("筛选兼容发布"); Body.Children.Add(search); channel.Margin = new Thickness(0, 8, 0, 10); Body.Children.Add(channel);
        Body.Children.Add(releases); Body.Children.Add(status);
        var note = new TextBlock { Text = filter.Kind == ContentKind.Shader ? "光影需 Iris / OptiFine 等组件支持；下载后请在游戏内启用。" : filter.Kind == ContentKind.ResourcePack ? "资源包保留 ZIP 格式，下载后请在游戏内启用。" : "将解析并列出必需前置，确认后加入下载队列。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Style = (Style)FindResource("Hint") }; Body.Children.Add(note);
        Actions.Children.Add(Button("取消", (_, _) => Close()));
        install = Button("查看安装清单  →", async (_, _) =>
        {
            if (releases.SelectedItem is not CatalogVersion selected) return;
            install.IsEnabled = releases.IsEnabled = search.IsEnabled = channel.IsEnabled = false; status.Text = "正在解析资源与必需前置…";
            try
            {
                var plan = await service.PrepareAsync(settings, project, selected, filter, target, lifetime.Token); lifetime.Token.ThrowIfCancellationRequested();
                var confirmation = new ContentConfirmationWindow(this, plan);
                if (confirmation.ShowDialog() == true) { enqueue(plan); DialogResult = true; }
                else status.Text = "可继续选择版本。";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!closed) status.Text = ex.Message; }
            finally { if (!closed) { releases.IsEnabled = search.IsEnabled = channel.IsEnabled = true; install.IsEnabled = releases.SelectedItem is not null; } }
        }, true);
        install.IsEnabled = false; Actions.Children.Add(install); KeepActionsVisible();
        search.TextChanged += (_, _) => Filter(); channel.SelectionChanged += (_, _) => Filter();
        Loaded += async (_, _) =>
        {
            status.Text = "正在读取兼容发布…";
            try { all = await service.GetVersionsAsync(settings, project, filter, lifetime.Token); lifetime.Token.ThrowIfCancellationRequested(); Filter(); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!closed) status.Text = ex.Message; }
        };
        Closed += (_, _) => { closed = true; lifetime.Cancel(); };
    }
    private void Filter()
    {
        var selected = (releases.SelectedItem as CatalogVersion)?.Id;
        var visible = all.Where(version => (version.Name + " " + version.Id).Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase) && (channel.SelectedIndex <= 0 || version.Channel == channel.SelectedItem as string)).ToArray();
        releases.ItemsSource = visible; releases.SelectedItem = visible.FirstOrDefault(item => item.Id == selected) ?? visible.FirstOrDefault();
        status.Text = visible.Length == 0 ? "没有兼容的发布版本。可检查目标实例、光影格式或筛选条件。" : $"显示 {visible.Length} / 共 {all.Count} 个兼容发布";
        install.IsEnabled = visible.Length > 0;
    }
}

public sealed class ContentConfirmationWindow : FormWindow
{
    public ContentConfirmationWindow(Window owner, ContentPlan plan) : base(owner, "确认资源安装", plan.Name, 760)
    {
        Body.Children.Add(new TextBlock { Text = plan.Summary, TextWrapping = TextWrapping.Wrap });
        Body.Children.Add(new TextBlock { Text = "将校验下载文件并保留原文件。若发现旧版本或同名内容冲突，将停止安装并提示处理。", Style = (Style)FindResource("Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 15, 0, 0) });
        Actions.Children.Add(Button("取消", (_, _) => DialogResult = false));
        Actions.Children.Add(Button("确认并加入队列", (_, _) => DialogResult = true, true)); KeepActionsVisible();
    }
}
