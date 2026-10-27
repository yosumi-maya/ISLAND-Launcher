using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using BlockDock.App;
using BlockDock.Core;
using Island.Core;
using Microsoft.Win32;

namespace Island.App;

public partial class DownloadCenter : UserControl
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private ILauncherDownloads service = new LauncherDownloads();
    private Func<AppSettings> settings = () => new();
    private Func<bool> canInstall = () => true;
    private Action<DownloadTask>? installed;
    private DownloadQueue? queue;
    private CancellationTokenSource? gamesCancellation, loaderCancellation, searchCancellation;
    private IReadOnlyList<GameVersion> versions = [];
    private PackSearchFilter appliedFilter = new(Platform: PackPlatform.Modrinth);
    private int loaderRevision, pageIndex;
    private bool initialized, closed, nameEdited, preparing, hasNext;
    public DownloadQueue Queue => queue ?? throw new InvalidOperationException("下载中心尚未初始化。");
    public event EventHandler? QueueChanged;
    private Window OwnerWindow => Window.GetWindow(this) ?? throw new InvalidOperationException("下载中心没有所属窗口。");

    public DownloadCenter()
    {
        InitializeComponent(); AvailabilityNotice.Text = DownloadAvailability.CurseForgeNotice;
        VersionFilter.SetVersions(PackGameVersion, []);
        PackGameVersion.LostKeyboardFocus += (_, _) => { if (VersionFilter.Value(PackGameVersion.Text).Length == 0) VersionFilter.SelectAll(PackGameVersion); };
        GameLoader.ItemsSource = Enum.GetValues<LoaderKind>(); GameLoader.SelectedIndex = 0;
        PackLoader.ItemsSource = new[] { "全部加载器", "Forge", "Fabric", "NeoForge" }; PackLoader.SelectedIndex = 0;
        PackSortBox.ItemsSource = new[] { "综合排序", "下载量", "最近更新", "最新发布" }; PackSortBox.SelectedIndex = 0;
        SetSearchBusy(false);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && searchCancellation is not null) { e.Handled = true; searchCancellation.Cancel(); } };
        timer.Tick += (_, _) => queue?.FlushProgress();
    }
    public void Configure(Func<AppSettings> settings, string logDirectory, Action<DownloadTask> installed,
        ILauncherDownloads? service = null, DownloadQueue? queue = null, Func<bool>? canInstall = null,
        IContentDownloads? contentService = null, Func<IReadOnlyList<ContentTarget>>? targets = null, Func<string?>? preferredTarget = null)
    {
        if (initialized) throw new InvalidOperationException("下载中心不可重复初始化。");
        this.settings = settings; this.installed = installed; this.service = service ?? new LauncherDownloads();
        this.canInstall = canInstall ?? (() => true);
        this.contentService = contentService ?? new ContentDownloads(); this.targets = targets ?? (() => []); this.preferredTarget = preferredTarget ?? (() => null);
        this.queue = queue ?? new DownloadQueue(async (task, progress, token) =>
        {
            if (task.LogPath is null)
            {
                Directory.CreateDirectory(logDirectory); task.LogPath = Path.Combine(logDirectory, task.Id.ToString("N") + ".log");
                await File.WriteAllTextAsync(task.LogPath, task.LogText, token);
            }
            return task.Work is { } work ? await work.Run(task.Settings, progress, token)
                : await this.service.InstallAsync(task.Settings, task.Request, task.Pack, progress, token);
        });
        this.queue.Changed += Changed; this.queue.TaskSettled += Settled;
        TaskList.ItemsSource = this.queue.Items; initialized = true; UpdateQueue(); timer.Start(); RefreshDestination();
    }
    public void Activate(bool showGames = false)
    {
        RefreshDestination(); if (showGames) GamesTab.IsChecked = true;
        if (contentKind is not null) RefreshContentTargets();
        if (GamesTab.IsChecked == true && versions.Count == 0 && gamesCancellation is null) _ = LoadGamesAsync();
    }
    private void RefreshDestination()
    {
        if (!initialized || closed) return;
        var snapshot = DownloadAvailability.Snapshot(settings()); Destination.Text = "新任务安装到：" + snapshot.GameDirectory;
        Destination.ToolTip = snapshot.GameDirectory;
    }
    private void ChangeTab(object sender, RoutedEventArgs e)
    {
        if (!initialized || sender is not RadioButton tab) return;
        GamesPage.Visibility = tab.Tag as string == "Games" ? Visibility.Visible : Visibility.Collapsed;
        var page = tab.Tag as string;
        var catalog = page is "Packs" or "Mods" or "Shaders" or "Resources";
        PacksPage.Visibility = catalog ? Visibility.Visible : Visibility.Collapsed;
        TasksPage.Visibility = tab.Tag as string == "Tasks" ? Visibility.Visible : Visibility.Collapsed;
        if (catalog) SelectCatalog(page!);
        if ((GamesTab.IsChecked == true || catalog) && versions.Count == 0 && gamesCancellation is null) _ = LoadGamesAsync();
    }
    private async void ReloadGames(object sender, RoutedEventArgs e) => await LoadGamesAsync();
    internal async Task LoadGamesAsync()
    {
        if (closed) return;
        gamesCancellation?.Cancel();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); cancellation.CancelAfter(TimeSpan.FromSeconds(45));
        gamesCancellation = cancellation; ReloadGamesButton.IsEnabled = false; InstallGameButton.IsEnabled = false; GameStatus.Text = "正在读取 Minecraft 版本列表…";
        try
        {
            var found = await service.GetGamesAsync(DownloadAvailability.Snapshot(settings()), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested(); versions = found; FilterGames(this, new RoutedEventArgs());
            VersionFilter.SetVersions(PackGameVersion, found.Where(v => v.Type == "release").Select(v => v.Id));
        }
        catch (OperationCanceledException) { if (!closed && gamesCancellation == cancellation) GameStatus.Text = "版本查询已取消或超时，请刷新重试。"; }
        catch (Exception ex) { if (!closed && gamesCancellation == cancellation) GameStatus.Text = "无法读取版本列表：" + ex.Message; }
        finally { if (gamesCancellation == cancellation) { gamesCancellation = null; ReloadGamesButton.IsEnabled = !closed; } }
    }
    private void FilterGames(object sender, RoutedEventArgs e)
    {
        if (!initialized) return;
        var previous = (GameVersions.SelectedItem as GameVersion)?.Id;
        var found = versions.Where(v => ShowSnapshots.IsChecked == true || v.Type == "release").ToArray();
        GameVersions.ItemsSource = found; GameVersions.SelectedItem = found.FirstOrDefault(v => v.Id == previous) ?? found.FirstOrDefault();
        if (found.Length == 0) GameStatus.Text = "没有可用版本，请刷新列表或启用快照与历史版本。";
    }
    private async void GameSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized || closed) return;
        var revision = ++loaderRevision; InstallGameButton.IsEnabled = false; loaderCancellation?.Cancel(); LoaderVersions.ItemsSource = null;
        if (GameVersions.SelectedItem is not GameVersion game || GameLoader.SelectedItem is not LoaderKind loader) return;
        if (!nameEdited) NewInstanceName.Text = game.Id + (loader == LoaderKind.Vanilla ? "-我的世界" : "-" + loader);
        LoaderVersions.IsEnabled = loader != LoaderKind.Vanilla;
        if (loader == LoaderKind.Vanilla) { GameStatus.Text = "原版 Minecraft · 无模组加载器"; InstallGameButton.IsEnabled = true; return; }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); cancellation.CancelAfter(TimeSpan.FromSeconds(45));
        loaderCancellation = cancellation; GameStatus.Text = "正在查询加载器版本…";
        try
        {
            var found = await service.GetLoadersAsync(DownloadAvailability.Snapshot(settings()), loader, game.Id, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested(); if (revision != loaderRevision) return;
            LoaderVersions.ItemsSource = found; LoaderVersions.SelectedItem = found.FirstOrDefault(v => v.Stable) ?? found.FirstOrDefault();
            InstallGameButton.IsEnabled = found.Count > 0; GameStatus.Text = found.Count > 0 ? $"找到 {found.Count} 个加载器版本。" : "该游戏版本没有可用的加载器版本。";
        }
        catch (OperationCanceledException) { if (!closed && revision == loaderRevision) GameStatus.Text = "加载器查询已取消或超时，请重新选择。"; }
        catch (Exception ex) { if (!closed && revision == loaderRevision) GameStatus.Text = "无法读取加载器版本：" + ex.Message; }
        finally { if (loaderCancellation == cancellation) loaderCancellation = null; }
    }
    private void NameChanged(object sender, TextChangedEventArgs e) { if (NewInstanceName.IsKeyboardFocusWithin) nameEdited = true; }
    private void InstallGame(object sender, RoutedEventArgs e)
    {
        try
        {
            if (GameVersions.SelectedItem is not GameVersion game || GameLoader.SelectedItem is not LoaderKind loader) throw new ArgumentException("请先选择游戏版本。");
            var loaderVersion = (LoaderVersions.SelectedItem as LoaderVersion)?.Version;
            if (loader != LoaderKind.Vanilla && loaderVersion is null) throw new ArgumentException("请先选择加载器版本。");
            Enqueue(new(game.Id, NewInstanceName.Text.Trim(), loader, loaderVersion), DownloadAvailability.Snapshot(settings()));
        }
        catch (Exception ex) { GameStatus.Text = ex.Message; }
    }
    internal DownloadTask Enqueue(InstallRequest request, AppSettings snapshot, PackInfo? pack = null)
    {
        if (!canInstall()) throw new InvalidOperationException("请等待当前启动任务或游戏结束后再添加下载任务。");
        DownloadAvailability.RequireSupported(pack);
        var task = Queue.Enqueue(request, DownloadAvailability.Snapshot(snapshot), pack); TasksTab.IsChecked = true; return task;
    }
    private PackSearchFilter ReadFilter() => new(PackQuery.Text.Trim(), VersionFilter.Value(PackGameVersion.Text), PackPlatform.Modrinth,
        PackLoader.SelectedIndex <= 0 ? null : PackLoader.SelectedIndex switch { 1 => LoaderKind.Forge, 2 => LoaderKind.Fabric, _ => LoaderKind.NeoForge }, (PackSort)PackSortBox.SelectedIndex);
    private async void SearchClick(object sender, RoutedEventArgs e) => await SearchAsync(0);
    private async void SearchKey(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; await SearchAsync(0); } }
    public bool FocusSearch()
    {
        if (PacksPage.Visibility != Visibility.Visible) return false;
        PackQuery.Focus(); PackQuery.SelectAll(); return true;
    }
    private void CancelSearch(object sender, RoutedEventArgs e) => searchCancellation?.Cancel();
    private void PackSelectionChanged(object sender, SelectionChangedEventArgs e)
    { if (ChoosePackButton is not null) ChoosePackButton.IsEnabled = !closed && searchCancellation is null && PackResults.SelectedItem is not null; }
    private void SetSearchBusy(bool busy)
    {
        SearchButton.IsEnabled = !closed && !busy; SearchButton.Content = busy ? "搜索中…" : "搜索";
        CancelSearchButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        BrowseCatalogButton.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        BrowseCatalogButton.Content = "浏览内容";
        PreviousPage.IsEnabled = !closed && !busy && pageIndex > 0;
        NextPage.IsEnabled = !closed && !busy && hasNext;
        ChoosePackButton.IsEnabled = !closed && !busy && PackResults.SelectedItem is not null;
    }
    private void ResetSearch(object sender, RoutedEventArgs e)
    {
        searchCancellation?.Cancel(); searchCancellation = null;
        PackQuery.Clear(); VersionFilter.SelectAll(PackGameVersion); PackLoader.SelectedIndex = 0; PackSortBox.SelectedIndex = 0;
        PackResults.ItemsSource = null; PackEmpty.Visibility = Visibility.Visible; PackEmpty.Text = "搜索 Modrinth 上的" + CatalogLabel;
        PackSummary.Text = "搜索条件已重置。"; hasNext = false; pageIndex = 0; SetSearchBusy(false);
        PackSummary.ToolTip = null;
        CatalogEmptyHint.Text = "输入名称搜索，或保留全部版本，先浏览热门内容。";
        PackPageLabel.Text = "第 1 页";
        if (contentKind is not null) ApplyContentTarget();
    }
    private async void PreviousSearch(object sender, RoutedEventArgs e) => await SearchAsync(pageIndex - 1, appliedFilter, appliedContentFilter);
    private async void NextSearch(object sender, RoutedEventArgs e) => await SearchAsync(pageIndex + 1, appliedFilter, appliedContentFilter);
    internal async Task SearchAsync(int page, PackSearchFilter? filter = null, ContentFilter? resourceFilter = null)
    {
        if (closed || page < 0) return;
        searchCancellation?.Cancel();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); cancellation.CancelAfter(TimeSpan.FromSeconds(45));
        searchCancellation = cancellation; SetSearchBusy(true);
        PackEmpty.Visibility = PackResults.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PackEmpty.Text = "正在查询 Modrinth…"; CatalogEmptyHint.Text = "正在读取项目与版本信息，可按 Esc 取消。";
        PackSummary.Text = "正在搜索" + CatalogLabel + "…";
        PackSummary.ToolTip = null;
        var query = filter ?? ReadFilter();
        var resourceQuery = contentKind is null ? null : resourceFilter ?? ReadContentFilter();
        try
        {
            var snapshot = DownloadAvailability.Snapshot(settings());
            var request = resourceQuery is null ? service.SearchAsync(snapshot, query, page, cancellation.Token)
                : contentService.SearchAsync(snapshot, resourceQuery, page, cancellation.Token);
            var result = await request.WaitAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            appliedFilter = query; pageIndex = result.Page; hasNext = result.HasNext;
            appliedContentFilter = resourceQuery;
            var cards = result.Projects.Select(project => new PackSearchCard(project)).ToArray();
            PackResults.ItemsSource = cards; PackResults.SelectedItem = cards.FirstOrDefault();
            PackEmpty.Visibility = result.Projects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            PackEmpty.Text = result.Notices.Count > 0 ? string.Join("\n", result.Notices) : "没有匹配结果，请调整筛选条件。";
            CatalogEmptyHint.Text = "试试简短的名称、选择全部版本，或重置筛选条件。";
            PackSummary.Text = $"Modrinth · 本页 {result.Projects.Count} 项 / 共 {result.Total:N0} 项" + (result.Notices.Count > 0 ? " · " + string.Join("；", result.Notices) : "");
            PackPageLabel.Text = $"第 {pageIndex + 1} 页";
        }
        catch (OperationCanceledException)
        {
            if (!closed && searchCancellation == cancellation)
            {
                PackEmpty.Text = "搜索已取消或超时"; CatalogEmptyHint.Text = "检查网络后重新搜索，或调整筛选条件。";
                PackSummary.Text = PackResults.Items.Count > 0 ? "搜索已取消，保留上次结果。" : "搜索已取消，可重新搜索。";
            }
        }
        catch (Exception ex)
        {
            if (!closed && searchCancellation == cancellation)
            {
                PackEmpty.Text = "暂时无法完成搜索"; CatalogEmptyHint.Text = "请检查网络后重新搜索。";
                PackSummary.Text = "搜索未完成，请检查网络后重试。" + (PackResults.Items.Count > 0 ? "（保留上次结果）" : "");
                PackSummary.ToolTip = ex.Message;
            }
        }
        finally
        {
            if (searchCancellation == cancellation)
            {
                searchCancellation = null; SetSearchBusy(false);
                if (PackEmpty.Visibility == Visibility.Visible) BrowseCatalogButton.Content = "重新搜索";
            }
        }
    }
    private void OpenPack(object sender, RoutedEventArgs e)
    {
        if (searchCancellation is not null) return;
        if (PackResults.SelectedItem is not PackSearchCard card) { PackSummary.Text = "请先选择一个" + CatalogLabel + "。"; return; }
        if (contentKind is not null) { OpenContent(card.Project); return; }
        var project = card.Project;
        var dialog = new PackReleaseWindow(OwnerWindow, project, appliedFilter, DownloadAvailability.Snapshot(settings()), service, (request, snapshot, pack) => Enqueue(request, snapshot, pack));
        dialog.ShowDialog();
    }
    private void OpenPackDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject element && ItemsControl.ContainerFromElement(PackResults, element) is ListBoxItem) OpenPack(sender, e);
    }
    private void PackKey(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; OpenPack(sender, e); } }
    private async void ImportClick(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "导入本地整合包", Filter = "整合包|*.mrpack;*.zip" };
        if (picker.ShowDialog(OwnerWindow) == true) await ImportAsync(picker.FileName);
    }
    internal async Task ImportAsync(string path)
    {
        if (preparing || closed) return;
        preparing = true; var snapshot = DownloadAvailability.Snapshot(settings());
        try
        {
            var pack = await service.InspectAsync(path, lifetime.Token); lifetime.Token.ThrowIfCancellationRequested();
            DownloadAvailability.RequireSupported(pack);
            var confirmation = new PackConfirmationWindow(OwnerWindow, pack, snapshot);
            if (confirmation.ShowDialog() == true) Enqueue(confirmation.Request!, confirmation.Settings, pack);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!closed) MessageWindow.Inform(OwnerWindow, ex.Message, "整合包导入未完成"); }
        finally { preparing = false; }
    }
    private void Changed(object? sender, EventArgs e) { if (!closed) { UpdateQueue(); QueueChanged?.Invoke(this, EventArgs.Empty); } }
    private void Settled(DownloadTask task) { if (!closed && task.State == DownloadTaskState.Completed) installed?.Invoke(task); }
    private void UpdateQueue()
    {
        if (queue is null) return;
        var tasks = queue.Items; TasksEmpty.Visibility = tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TasksSummary.Text = tasks.Count == 0 ? "无下载任务" : $"共 {tasks.Count} 项 · {tasks.Count(t => t.State == DownloadTaskState.Running)} 项下载中 · {tasks.Count(t => t.State == DownloadTaskState.Queued)} 项等待";
        TasksTab.Content = tasks.Count == 0 ? "下载任务" : $"下载任务（{tasks.Count}）";
    }
    private void ToggleTask(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: DownloadTask task }) return;
        if (task.ShowPause) Queue.Pause(task);
        else if (canInstall()) Queue.Resume(task);
        else MessageWindow.Inform(OwnerWindow, "请等待当前启动任务或游戏结束后再继续下载。");
    }
    private void CancelTask(object sender, RoutedEventArgs e) { if (sender is Button { DataContext: DownloadTask task }) Queue.Cancel(task); }
    private void ClearTasks(object sender, RoutedEventArgs e) => Queue.ClearEnded();
    public Task ShutdownAsync()
    {
        if (!closed) { closed = true; lifetime.Cancel(); timer.Stop(); }
        return queue?.ShutdownAsync() ?? Task.CompletedTask;
    }
}
