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
    private string CatalogLabel => contentKind == ContentKind.Mod ? "模组" : contentKind is { } kind ? ContentDownloads.Label(kind) : "整合包";
    private void SelectCatalog(string page)
    {
        var changed = catalogPage != page;
        catalogPage = page; contentKind = page switch { "Mods" => ContentKind.Mod, "Shaders" => ContentKind.Shader, "Resources" => ContentKind.ResourcePack, _ => null };
        ContentTargetPanel.Visibility = contentKind is null ? Visibility.Collapsed : Visibility.Visible;
        ChoosePackButton.Content = "查看详情与版本  →";
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
        if (searchCancellation is null) _ = SearchAsync(pageIndex);
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
            ContentTargetBox.ToolTip = "还没有已安装的实例，请先创建或导入实例。"; return;
        }
        if (contentKind == ContentKind.Mod)
            PackLoader.SelectedItem = PackLoader.Items.Cast<string>().FirstOrDefault(value => value.Equals(target.ModLoader, StringComparison.OrdinalIgnoreCase)) ?? PackLoader.Items[0];
        ContentTargetBox.ToolTip = target.RuntimeDirectory;
    }
    private ContentFilter ReadContentFilter() => new(contentKind ?? ContentKind.Mod, PackQuery.Text.Trim(), VersionFilter.Value(PackGameVersion.Text),
        PackLoader.SelectedIndex > 0 ? (PackLoader.SelectedItem as string ?? "").ToLowerInvariant() : "", (PackSort)PackSortBox.SelectedIndex);
    private void OpenContent(CatalogProject project)
    {
        if (ContentTargetBox.SelectedItem is not ContentTarget target) { SetCatalogStatus("请先创建或导入实例，再选择安装目标。"); return; }
        if (target.Minecraft == "未知版本") { SetCatalogStatus("无法识别目标实例的 Minecraft 版本，请检查或重新导入该实例。"); return; }
        if (!canInstall()) { SetCatalogStatus("请等待当前启动任务或游戏结束后再安装资源。"); return; }
        var filter = ReadContentFilter() with { Minecraft = target.Minecraft, Loader = contentKind == ContentKind.Mod ? target.ModLoader : contentKind == ContentKind.Shader ? ReadContentFilter().Loader : "" };
        if (filter.Kind == ContentKind.Mod && target.ModLoader is not ("forge" or "fabric" or "neoforge" or "quilt")) { SetCatalogStatus("该实例需要先安装 Forge、Fabric、NeoForge 或 Quilt 加载器。"); return; }
        var snapshot = DownloadAvailability.Snapshot(settings());
        new ContentReleaseWindow(OwnerWindow, project, filter, target, snapshot, contentService, plan => EnqueueContent(plan, snapshot)).ShowDialog();
    }
    internal DownloadTask EnqueueContent(ContentPlan plan, AppSettings snapshot)
    {
        if (!canInstall()) throw new InvalidOperationException("请等待当前启动任务或游戏结束后再添加下载任务。");
        var task = Queue.EnqueueWork(DownloadAvailability.Snapshot(snapshot), new(plan.Name,
            plan.Target.Display + $" · {plan.InstallFiles.Count} 个资源文件", plan.TargetFiles,
            (options, progress, token) => contentService.InstallAsync(options, plan, progress, token), plan.IconUrl));
        TasksTab.IsChecked = true; return task;
    }
}

public sealed class ContentReleaseWindow : FormWindow
{
    private readonly CancellationTokenSource lifetime = new();
    private bool closed;
    public ContentReleaseWindow(Window owner, CatalogProject project, ContentFilter filter, ContentTarget target, AppSettings settings,
        IContentDownloads service, Action<ContentPlan> enqueue) : base(owner, project.Title, "", 900)
    {
        bool Compatible(CatalogVersion version) => version.GameVersions.Contains(target.Minecraft, StringComparer.OrdinalIgnoreCase)
            && (filter.Kind != ContentKind.Mod || version.Loaders.Contains(target.ModLoader, StringComparer.OrdinalIgnoreCase))
            && (filter.Kind != ContentKind.Shader || filter.Loader.Length == 0 || version.Loaders.Contains(filter.Loader, StringComparer.OrdinalIgnoreCase));
        var browser = new CatalogReleaseBrowser(project, target.Minecraft, filter.Kind == ContentKind.Mod ? target.ModLoader : filter.Loader, Compatible);
        Actions.Children.Add(Button("取消", (_, _) => Close()));
        var install = Button("查看安装清单  →", async (_, _) =>
        {
            if (!browser.CanInstall || browser.SelectedVersion is not { } selected) return;
            browser.SetBusy(true, "正在解析资源与必需前置…");
            try
            {
                var plan = await service.PrepareAsync(settings, project, selected, filter, target, lifetime.Token); lifetime.Token.ThrowIfCancellationRequested();
                var confirmation = new ContentConfirmationWindow(this, plan);
                if (confirmation.ShowDialog() == true) { enqueue(confirmation.SelectedPlan!); DialogResult = true; }
                else browser.ReleaseStatus.Text = "可继续选择版本。";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!closed) browser.ReleaseStatus.Text = ex.Message; }
            finally { if (!closed) browser.SetBusy(false); }
        }, true);
        install.IsEnabled = false; browser.SelectionChanged += (_, _) => install.IsEnabled = browser.CanInstall;
        var destination = new TextBlock { Text = "安装到：" + target.Display, ToolTip = target.RuntimeDirectory,
            TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap,
            Style = (Style)FindResource("Hint"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), MaxWidth = 230 };
        Actions.Children.Insert(0, destination); Actions.Children.Add(install); KeepActionsVisible(); SetWorkspace(browser);
        SizeToContent = SizeToContent.Manual; Height = Math.Min(760, MaxHeight);
        Loaded += async (_, _) =>
        {
            try
            {
                var versions = await service.GetVersionsAsync(settings, project, filter with { Minecraft = "", Loader = "" }, lifetime.Token);
                lifetime.Token.ThrowIfCancellationRequested(); browser.SetVersions(versions);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!closed) browser.ShowError(ex.Message); }
        };
        Closed += (_, _) => { closed = true; lifetime.Cancel(); };
    }
}

