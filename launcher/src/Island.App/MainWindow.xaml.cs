using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BlockDock.Core;
using Island.Core;
using Microsoft.Win32;

namespace Island.App;

public partial class MainWindow : Window
{
    private readonly LauncherStore store;
    private readonly LauncherState state;
    internal JavaRuntimeCache JavaRuntimes { get; }
    private readonly JvmSettingsEditor globalJvm = new();
    private readonly Func<IReadOnlyList<string>?> selectGameFolders;
    private readonly Func<string?>? selectSkin;
    private readonly Func<CancellationToken, Task<MemoryCleanupResult>> cleanMemory;
    private readonly Func<string, IDisposable> preferHighPerformanceGpu;
    private readonly Action<string> openInstanceDirectory;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? operation;
    private Process? game;
    private Task? runningOperation;
    private bool ready;
    private bool closeAfterOperation;
    private List<InstanceCard> cards = [];
    private string currentLog = "";
    private string sensitiveToken = "";
    private DateTime lastProgress;
    private readonly SemaphoreSlim logGate = new(1, 1);
    private InstanceCard? Selected => InstanceList.SelectedItem as InstanceCard;
    public MainWindow(LauncherStore? launcherStore = null, Func<IReadOnlyList<string>?>? gameFolderPicker = null, Func<string?>? skinPicker = null, Func<string?>? backgroundPicker = null,
        Func<CancellationToken, Task<MemoryCleanupResult>>? memoryCleaner = null, Func<string, IDisposable>? gpuPreferenceFactory = null, Action<string>? instanceDirectoryOpener = null,
        JavaRuntimeCache? javaRuntimes = null, ILauncherDownloads? downloadService = null, BlockDock.App.DownloadQueue? downloadQueue = null,
        Func<bool>? confirmDownloadClose = null, IContentDownloads? contentService = null)
    {
        store = launcherStore ?? new(); state = store.Load(); ThemeManager.Apply(state.ThemeColor, dark: state.Appearance.DarkTheme);
        JavaRuntimes = javaRuntimes ?? new JavaRuntimeCache(store.DirectoryPath);
        AppearanceRenderer.NativeWindow = false; AppearanceRenderer.Apply(state.Appearance, state.ThemeColor);
        InitializeComponent(); InitializeAppearance(backgroundPicker);
        InitializeDownloads(downloadService, downloadQueue, confirmDownloadClose, contentService);
        InitializeInstanceMenus();
        // Register the action controls in the window's XAML namescope before
        // placing their row into the reusable memory editor's content slot.
        ((Panel)MemoryOptimizationRow.Parent).Children.Remove(MemoryOptimizationRow);
        GlobalMemoryEditor.OptimizationContent = MemoryOptimizationRow;
        InitializeDollMemoryMenu();
        selectSkin = skinPicker;
        cleanMemory = memoryCleaner ?? MemoryCleanupProcess.RunAsync;
        preferHighPerformanceGpu = gpuPreferenceFactory ?? GpuPreferenceLease.Acquire;
        openInstanceDirectory = instanceDirectoryOpener ?? OpenPath;
        selectGameFolders = gameFolderPicker ?? (() =>
        {
            var browse = new OpenFolderDialog { Title = "选择一个或多个 .minecraft 游戏根目录（按 Ctrl 可多选）", Multiselect = true };
            return browse.ShowDialog(this) == true ? browse.FolderNames : null;
        });
        SourceBox.ItemsSource = new[] { new { Label = "自动选择（推荐）", Source = DownloadSource.Auto }, new { Label = "Mojang 官方", Source = DownloadSource.Official }, new { Label = "BMCLAPI 镜像优先", Source = DownloadSource.Mirror } };
        RootBox.Text = state.GameDirectory; JavaBox.Text = state.JavaPath;
        GlobalMemoryEditor.Load(state.AutoMemory, state.MemoryMb);
        CleanMemoryBeforeLaunchBox.IsChecked = state.CleanMemoryBeforeLaunch;
        globalJvm.Load(state.Jvm); JvmHost.Content = globalJvm;
        SourceBox.SelectedValue = state.Source;
        DownloadParallelBox.ItemsSource = new[] { 4, 8, 16, 32 }; DownloadParallelBox.SelectedItem = state.DownloadSettings().ParallelDownloads;
        ApplyJavaChoices(JavaRuntimes.Snapshot().Items, state.JavaPath);
        JavaStatus.Text = "正在载入 Java 检测记录…";
        RefreshFolderTabs();
        ready = true; NavLibrary.IsChecked = true;
        Loaded += (_, _) =>
        {
            try { ReloadLibrary(); ReloadAccounts(); Log("岛屿启动器 0.10.22 已就绪。"); if (store.LoadWarning is not null) Log(store.LoadWarning); }
            catch (Exception ex) { Report(ex); }
            _ = RefreshJavaAsync(false);
        };
    }
    private void Navigate(object sender, RoutedEventArgs e)
    {
        if (!ready || sender is not RadioButton button) return;
        var page = button.Tag as string;
        LibraryPage.Visibility = page == "Library" ? Visibility.Visible : Visibility.Collapsed;
        AccountsPage.Visibility = page == "Accounts" ? Visibility.Visible : Visibility.Collapsed;
        LogsPage.Visibility = page == "Logs" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        DownloadCenterView.Visibility = page == "Downloads" ? Visibility.Visible : Visibility.Collapsed;
        if (page == "Downloads") DownloadCenterView.Activate();
        AppearancePage.Visibility = Visibility.Collapsed;
    }
    private void ReloadLibrary(bool preserveSelection = true)
    {
        var key = preserveSelection ? Selected?.Key : null;
        key ??= GameFolder.NormalizePath(state.GameDirectory).ToUpperInvariant() + "|" + state.SelectedInstanceId;
        RefreshFolderTabs(); cards = [];
        foreach (var folder in state.GameFolders)
        {
            try { cards.AddRange(InstanceCatalog.ListInstalled(folder.Directory).Select(instance => new InstanceCard(instance, state.For(folder.Directory, instance.Name), folder.Name, folder.Isolation))); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log("目录暂不可访问：" + folder.Name); }
        }
        ApplyFilter(key); RefreshRecentGame();
    }
    private void ApplyFilter(string? selected = null)
    {
        if (!ready || updatingFolders) return;
        selected ??= Selected?.Key;
        var search = SearchBox.Text.Trim();
        var visible = cards.Where(c => (state.ShowFavorites ? c.Preferences.Favorite : GameFolder.SamePath(c.GameDirectory, state.GameDirectory)) && (search.Length == 0 || (c.DisplayName + " " + c.Id + " " + c.VersionLine + " " + c.Loader + " " + c.FolderName).Contains(search, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(c => c.Preferences.Favorite).ThenByDescending(c => c.Preferences.LastPlayed).ThenBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        InstanceList.ItemsSource = visible;
        InstanceList.SelectedItem = visible.FirstOrDefault(c => c.Key == selected) ?? visible.FirstOrDefault();
        InstanceCount.Text = $"{visible.Count} 个世界";
        EmptyLibrary.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var filtered = state.ShowFavorites || search.Length > 0;
        EmptyTitle.Text = filtered ? "暂时没有匹配的实例" : "给第一个世界留个位置";
        EmptyDescription.Text = filtered ? "试试其他关键词，或在文件夹标签中收藏喜欢的实例。" : "创建一个新实例，或导入下载器 / 其他启动器的 .minecraft 目录。";
        EmptyCreateButton.Visibility = filtered ? Visibility.Collapsed : Visibility.Visible;
        UpdateSelected();
    }
    private void SearchChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void SelectInstance(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || updatingFolders) return;
        if (Selected is { } selected) state.RememberInstance(selected.Id, selected.GameDirectory);
        UpdateSelected();
    }
    private void UpdateSelected()
    {
        var selected = Selected;
        GlobalMemoryEditor.SetContext(selected?.RuntimeDirectory);
        SelectedName.Text = selected?.DisplayName ?? "选择一个实例";
        var runtime = selected is null ? null : state.RuntimeFor(selected.GameDirectory, selected.Id);
        SelectedDetail.Text = selected is null ? "选择实例后可管理 Java、存档与模组" : selected.VersionLine + "  ·  " + selected.Loader + "  ·  " + (runtime!.AutoMemory ? "自动内存" : runtime.MemoryMb + " MB") + "  ·  " + (selected.Runtime.Isolated ? "独立目录" : "共享目录");
        LaunchButton.IsEnabled = selected is not null && operation is null && game is null && !DownloadCenterView.Queue.IsBusy;
        FolderTabs.IsEnabled = operation is null || game is not null;
        ManageFoldersButton.IsEnabled = operation is null && game is null;
        ContinueButton.IsEnabled = recentGame is not null && operation is null && game is null && !DownloadCenterView.Queue.IsBusy;
        AddInstanceButton.IsEnabled = operation is null && game is null;
        CleanMemoryNowButton.IsEnabled = operation is null && game is null;
        dollCleanMemoryItem.IsEnabled = operation is null && game is null;
        LaunchButton.Content = game is null ? "▶  启动游戏" : "游戏运行中";
    }
    private void RefreshLibrary(object sender, RoutedEventArgs e) { try { ReloadLibrary(); StatusLabel.Text = "●  实例列表已刷新"; } catch (Exception ex) { Report(ex); } }
    private void CreateInstance(object sender, RoutedEventArgs e)
    {
        if (operation is not null || game is not null) { Notice("请等待当前任务或游戏结束。"); return; }
        NavDownloads.IsChecked = true; DownloadCenterView.Activate(showGames: true);
    }
    private void AddInstance(object sender, RoutedEventArgs e)
    {
        if (operation is not null || game is not null) { Notice("请等待当前任务或游戏结束。"); return; }
        var choice = new AddInstanceWindow(this);
        if (choice.ShowDialog() != true) return;
        if (choice.Choice == AddInstanceChoice.Download) CreateInstance(sender, e);
        else if (choice.Choice == AddInstanceChoice.Folder) ImportFolder();
        else ImportArchive();
    }
    private async void ImportFolder()
    {
        var selectedFolders = selectGameFolders();
        if (selectedFolders is null || selectedFolders.Count == 0) return;
        await RunOperationAsync(async token =>
        {
            var drafts = await Task.Run(() => selectedFolders.Select(GameFolder.NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase).Select(directory =>
            {
                token.ThrowIfCancellationRequested();
                if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("游戏目录不存在：" + directory);
                var existing = state.GameFolders.FirstOrDefault(folder => GameFolder.SamePath(folder.Directory, directory));
                return new GameFolderDraft(directory, existing?.Name ?? GameFolder.SuggestName(directory), MinecraftInstaller.ListInstalled(directory).Count, existing is not null, existing?.Isolation ?? VersionIsolation.Auto);
            }).ToArray(), token);
            token.ThrowIfCancellationRequested();
            var naming = new GameFoldersWindow(this, drafts, true);
            if (naming.ShowDialog() != true) return;
            token.ThrowIfCancellationRequested(); CommitFolderImports(naming.Result);
        }, "正在读取游戏目录…");
    }
    private async void ImportArchive()
    {
        var file = new OpenFileDialog { Title = "导入整合包", Filter = "整合包|*.mrpack;*.zip" };
        if (file.ShowDialog(this) != true) return;
        NavDownloads.IsChecked = true; await DownloadCenterView.ImportAsync(file.FileName);
    }
    private void EditInstance(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source)
            for (var current = source; current is not null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
                if (current is Button { Name: "CardFavoriteButton" }) { e.Handled = true; return; }
        if (Selected is not { } selected || operation is not null || game is not null) return;
        try { OpenInstanceManagement(selected); }
        catch (Exception ex) { Report(ex); }
    }
    private void ToggleCardFavorite(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: InstanceCard card }) return;
        var selected = Selected?.Key; var previous = card.IsFavorite;
        try { card.SetFavorite(!previous); store.Save(state); ApplyFilter(selected); }
        catch (Exception ex) { card.SetFavorite(previous); Report(ex); }
    }
    private void InstanceKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { LaunchGame(sender, e); e.Handled = true; } }
    private async void LaunchGame(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } selected || game is not null || operation is not null) return;
        await LaunchInstanceAsync(selected);
    }
    private async Task LaunchInstanceAsync(InstanceCard selected, string? world = null)
    {
        if (DownloadCenterView.Queue.IsBusy) { Notice("正在安装实例，请等待下载任务结束或暂停后再启动游戏。"); return; }
        var account = state.Accounts.FirstOrDefault(a => a.Id == state.SelectedAccountId);
        if (account is null) { NavAccounts.IsChecked = true; StatusLabel.Text = "●  请先添加或选择玩家账号"; return; }
        var gameRoot = selected.GameDirectory;
        var launchSettings = state.DownloadSettings(gameRoot, selected.Id);
        var runtime = state.RuntimeFor(gameRoot, selected.Id);
        await RunOperationAsync(async token =>
        {
            var preparation = Stopwatch.StartNew();
            Directory.CreateDirectory(Path.Combine(store.DirectoryPath, "logs"));
            currentLog = Path.Combine(store.DirectoryPath, "logs", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".log");
            await using var outputLog = new StreamWriter(new FileStream(currentLog, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 16384, true), new UTF8Encoding(false));
            async Task Stage(string name)
            {
                var message = $"启动准备 · {name} · 累计 {preparation.Elapsed.TotalSeconds:0.00} 秒";
                Log(message); await outputLog.WriteLineAsync(message); await outputLog.FlushAsync();
            }
            try
            {
            var resolved = await Task.Run(() => VersionProfile.Read(gameRoot, selected.Id, selected.Isolation), token);
            await Stage("版本配置已读取");
            LaunchIdentity identity;
            if (account.Microsoft)
            {
                OperationLabel.Text = "正在验证 Microsoft 玩家身份…";
                using var auth = new MicrosoftAuth(verifyCertificates: runtime.Jvm.VerifyLoginCertificates); var result = await auth.RefreshAsync(account, token);
                var index = state.Accounts.IndexOf(account); state.Accounts[index] = result.Account; identity = result.Identity; store.Save(state); ReloadAccounts();
            }
            else identity = new(account.Name, account.Uuid, "0", false);
            sensitiveToken = identity.Microsoft ? identity.AccessToken : "";
            OperationLabel.Text = "正在匹配 Java…";
            var unavailable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var java = await LauncherJava.ResolveAsync(launchSettings, JavaRequirement.FromMetadata(resolved.Profile), runtime.Jvm, token, selected.Preferences.AutoSelectedJavaPath,
                JavaRuntimes, path => { unavailable.Add(path); ReportJavaUnavailable(path); });
            var recovered = !string.IsNullOrWhiteSpace(launchSettings.JavaPath) && unavailable.Contains(launchSettings.JavaPath);
            if (recovered)
            {
                if (selected.Preferences.JavaPath is { Length: > 0 }) selected.Preferences.JavaPath = "";
                else if (state.JavaPath == launchSettings.JavaPath)
                {
                    state.JavaPath = "";
                    if (ReadJavaValue() == launchSettings.JavaPath) ApplyJavaChoices(JavaRuntimes.Snapshot().Items, "");
                }
            }
            if (recovered || string.IsNullOrWhiteSpace(launchSettings.JavaPath) && selected.Preferences.AutoSelectedJavaPath != java.Path)
            {
                selected.Preferences.AutoSelectedJavaPath = java.Path; store.Save(state);
            }
            if (unavailable.Count > 0)
            {
                ApplyJavaChoices(JavaRuntimes.Snapshot().Items, ReadJavaValue());
                JavaStatus.Text = "未找到或无法运行已保存的 Java，已重新检测并匹配 Java " + java.Version + "。\n" + java.Path;
                Log(JavaStatus.Text); ShowRuntimeFeedback(JavaStatus.Text, true);
            }
            await outputLog.WriteLineAsync($"Island Launcher · {selected.Id} · Java {java.Version}");
            await Stage("Java 已匹配");
            Log($"启动 {selected.DisplayName} · {selected.Loader} · Java {java.Version} ({java.Architecture})");
            Log("游戏目录：" + selected.RuntimeDirectory + " · " + selected.Runtime.Reason);
            using (var network = new DownloadClient(launchSettings)) await GameLauncher.RepairAsync(gameRoot, resolved, network, Progress(), token, selected.Isolation);
            await Stage("依赖与资源已检查");
            if (!string.IsNullOrWhiteSpace(runtime.Jvm.PreLaunchCommand))
            {
                OperationLabel.Text = "正在执行启动前命令…"; Log("正在执行启动前命令…");
                await PreLaunchCommands.RunAsync(runtime.Jvm.PreLaunchCommand, selected.RuntimeDirectory, token, line => Log("[启动前命令] " + line));
                Log("启动前命令执行完成。");
            }
            if (state.CleanMemoryBeforeLaunch)
            {
                await CleanMemoryAsync(token, beforeLaunch: true);
                await outputLog.WriteLineAsync(MemoryCleanupStatus.Text);
                await Stage("内存清理已结束");
            }
            else await Stage("启动前内存清理未启用，可在设置中点击开启");
            var memory = runtime.AutoMemory ? MemoryAllocator.Recommend(MemoryAllocator.ReadSystem(), MemoryAllocator.CountMods(selected.RuntimeDirectory), java.Is64Bit) : runtime.MemoryMb;
            Log($"内存：{memory} MB · " + (runtime.AutoMemory ? "自动分配" : "手动设置"));
            token.ThrowIfCancellationRequested(); OperationLabel.Text = "正在启动 Minecraft…";
            var plan = await Task.Run(() => GameLauncher.Prepare(gameRoot, resolved, java.Path, memory, identity, world, selected.Isolation, runtime.Jvm, java.Major), token);
            IDisposable? gpuPreference = null;
            try
            {
                token.ThrowIfCancellationRequested();
                if (runtime.Jvm.HighPerformanceGpu)
                {
                    try { gpuPreference = preferHighPerformanceGpu(java.Path); Log("已请求所选 Java 使用 Windows 高性能图形偏好，游戏退出后恢复原设置。"); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { Log("无法设置高性能显卡偏好，继续使用系统设置：" + ex.Message); }
                }
                await Stage("启动参数与原生库已准备，正在启动 Minecraft");
                var process = new Process { StartInfo = plan.StartInfo };
                try { if (!process.Start()) throw new InvalidOperationException("Java 进程未能启动。"); }
                catch { process.Dispose(); throw; }
                game = process; selected.Preferences.LastPlayed = DateTimeOffset.Now; state.RememberInstance(selected.Id, gameRoot); store.Save(state);
                UpdateSelected(); CancelButton.Content = "结束游戏"; OperationProgress.IsIndeterminate = true;
                OperationLabel.Text = "Minecraft 运行中 · " + selected.DisplayName; StatusLabel.Text = "●  游戏运行中";
                Log("游戏进程已启动。输出将显示在「运行日志」。");
                var watch = Stopwatch.StartNew();
                using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { } });
                var stdout = ReadOutputAsync(process.StandardOutput, outputLog); var stderr = ReadOutputAsync(process.StandardError, outputLog);
                await process.WaitForExitAsync(CancellationToken.None); await Task.WhenAll(stdout, stderr);
                await outputLog.FlushAsync();
                selected.Preferences.PlayedMinutes += watch.Elapsed.TotalMinutes; store.Save(state);
                var exit = process.ExitCode; Log($"游戏已退出，退出码 {exit}，本次运行 {watch.Elapsed.TotalMinutes:0.0} 分钟。");
                StatusLabel.Text = exit == 0 ? "●  游戏已正常结束" : $"●  游戏退出码 {exit}，请查看运行日志";
                if (exit != 0 && !token.IsCancellationRequested) NavLogs.IsChecked = true;
            }
            finally
            {
                if (game is { HasExited: false }) { try { game.Kill(true); await game.WaitForExitAsync(); } catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { Log("结束游戏进程失败：" + ex.Message); } }
                game?.Dispose(); game = null; sensitiveToken = "";
                try { gpuPreference?.Dispose(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { Log("恢复图形偏好失败：" + ex.Message); }
                if (!await GameLauncher.CleanupNativesAsync(gameRoot, plan.NativesDirectory)) Log("临时原生库仍被系统占用，可在游戏结束后手动清理：" + plan.NativesDirectory);
                ReloadLibrary();
            }
            }
            catch (Exception ex)
            {
                var message = "启动未完成：" + ex.Message;
                if (sensitiveToken.Length > 0) message = message.Replace(sensitiveToken, "[已隐藏凭据]", StringComparison.Ordinal);
                await outputLog.WriteLineAsync(message); await outputLog.FlushAsync(); throw;
            }
            finally { sensitiveToken = ""; }
        }, "正在准备启动…");
    }
    private static long? AvailableMemory()
    {
        try { return MemoryAllocator.ReadSystem().AvailableMb; }
        catch (InvalidOperationException) { return null; }
    }
    private async Task ReadOutputAsync(StreamReader reader, StreamWriter output)
    {
        var formatter = new GameLogFormatter();
        async Task WriteLineAsync(string line)
        {
            // Some loaders print their command line; redact the current access token before disk/UI output.
            if (sensitiveToken.Length > 0) line = line.Replace(sensitiveToken, "[已隐藏凭据]", StringComparison.Ordinal);
            Log(line);
            await logGate.WaitAsync();
            try { if (currentLog.Length > 0) await output.WriteLineAsync(line); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { currentLog = ""; Log("日志文件写入失败：" + ex.Message); }
            finally { logGate.Release(); }
        }
        while (await reader.ReadLineAsync() is { } raw)
            if (formatter.Push(raw) is { } line) await WriteLineAsync(line);
        if (formatter.Flush() is { } remaining) await WriteLineAsync(remaining);
    }
    private IProgress<InstallProgress> Progress() => new Progress<InstallProgress>(progress =>
    {
        if (operation is null) return;
        if (progress.Completed != progress.Total && (DateTime.UtcNow - lastProgress).TotalMilliseconds < 120) return;
        lastProgress = DateTime.UtcNow;
        OperationLabel.Text = progress.Stage + "  ·  " + progress.Detail;
        OperationProgress.IsIndeterminate = progress.Total == 0; OperationProgress.Value = progress.Percent;
    });
    private async Task RunOperationAsync(Func<CancellationToken, Task> action, string label)
    {
        if (operation is not null) { Notice("已有任务正在运行。"); return; }
        operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        OperationPanel.Visibility = Visibility.Visible; OperationLabel.Text = label; OperationProgress.IsIndeterminate = true;
        CancelButton.Content = "取消"; CancelButton.IsEnabled = true; UpdateSelected();
        try { runningOperation = action(operation.Token); await runningOperation; }
        catch (OperationCanceledException) { Log("任务已取消，已下载的缓存可在下次安装时复用。"); StatusLabel.Text = "●  已取消"; }
        catch (Exception ex) { Report(ex); }
        finally
        {
            operation.Dispose(); operation = null; runningOperation = null;
            OperationPanel.Visibility = Visibility.Collapsed; UpdateSelected();
            if (closeAfterOperation) Close();
        }
    }
    private void CancelOperation(object sender, RoutedEventArgs e)
    {
        if (game is not null && !MessageWindow.Confirm(this, "结束 Minecraft", "结束游戏会使未保存的进度丢失。确定结束？", "结束游戏")) return;
        CancelButton.IsEnabled = false; operation?.Cancel();
    }
    private void Log(string text)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => Log(text)); return; }
        if (sensitiveToken.Length > 0) text = text.Replace(sensitiveToken, "[已隐藏凭据]", StringComparison.Ordinal);
        var line = $"[{DateTime.Now:HH:mm:ss}] {text}\n";
        if (LogBox.Text.Length > 240000) LogBox.Text = LogBox.Text[^160000..];
        LogBox.AppendText(line); LogBox.ScrollToEnd();
    }
    private void Report(Exception ex) { Log("错误：" + ex.Message); StatusLabel.Text = "●  操作未完成，请查看提示"; MessageWindow.Inform(this, ex.Message); }
    private void Notice(string text) => MessageWindow.Inform(this, text);
    public static void OpenPath(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    private void OpenLogs(object sender, RoutedEventArgs e) { try { var path = Path.Combine(store.DirectoryPath, "logs"); Directory.CreateDirectory(path); OpenPath(path); } catch (Exception ex) { Report(ex); } }
    private void CopyLogs(object sender, RoutedEventArgs e) { try { Clipboard.SetText(LogBox.Text); StatusLabel.Text = "●  日志已复制"; } catch (Exception ex) { Report(ex); } }
    private void Minimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
    private void UpdateWindowOutline(object sender, SizeChangedEventArgs e) => RefreshWindowOutline();
    private void UpdateWindowState(object? sender, EventArgs e)
    {
        RefreshWindowOutline();
        WindowMaterials.ApplyCornerPreference(this);
    }
    private void RefreshWindowOutline()
    {
        if (WindowSurface is null) return;
        var radius = WindowState == WindowState.Maximized ? 0 : 14;
        WindowSurface.CornerRadius = new CornerRadius(radius);
        var clip = new System.Windows.Media.RectangleGeometry(new Rect(WindowSurface.RenderSize), radius, radius);
        clip.Freeze(); WindowSurface.Clip = clip;
    }
    private void ClipHero(object sender, SizeChangedEventArgs e)
    {
        if (sender is FrameworkElement element) element.Clip = new System.Windows.Media.RectangleGeometry(new Rect(e.NewSize), 14, 14);
    }
    private void AdaptLayout(object sender, SizeChangedEventArgs e)
    {
        if (ready)
        {
            LibraryPage.RowDefinitions[0].Height = new GridLength(e.NewSize.Height < 750 ? 160 : 180);
            AppearancePreviewHost.Height = e.NewSize.Height < 750 ? 226 : 270;
            SidebarDoll.Height = e.NewSize.Height < 750 ? 180 : 220;
        }
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!downloadsStopped && DownloadCenterView.Queue.HasUnfinishedTasks)
        {
            e.Cancel = true; if (stoppingDownloads) return;
            var message = game is null ? "关闭会取消尚未结束的下载任务，已下载缓存会保留。确定关闭？" : "关闭会取消下载并结束 Minecraft，未保存的游戏进度可能丢失。确定关闭？";
            if (!closeAfterOperation && !(confirmDownloadClose?.Invoke() ?? MessageWindow.Confirm(this, "关闭启动器", message, "取消任务并关闭"))) return;
            closeAfterOperation = true; stoppingDownloads = true; operation?.Cancel();
            try { await DownloadCenterView.ShutdownAsync(); }
            finally { downloadsStopped = true; stoppingDownloads = false; }
            if (operation is null) Close();
            return;
        }
        if (operation is not null)
        {
            e.Cancel = true;
            if (closeAfterOperation || MessageWindow.Confirm(this, "关闭启动器", game is null ? "取消当前任务并关闭启动器？" : "关闭将结束正在运行的游戏，未保存进度可能丢失。确定关闭？", "关闭启动器"))
            { closeAfterOperation = true; operation.Cancel(); }
            return;
        }
        lifetime.Cancel();
        if (!downloadsStopped) { downloadsStopped = true; await DownloadCenterView.ShutdownAsync(); }
        try { store.Save(state); } catch (Exception ex) { Log("配置保存失败：" + ex.Message); }
    }
}

public enum AddInstanceChoice { Download, Folder, Archive }
public sealed class AddInstanceWindow : FormWindow
{
    public AddInstanceChoice Choice { get; private set; }
    public AddInstanceWindow(Window owner) : base(owner, "添加实例", "下载一个新世界，连接已有游戏目录，或导入整合包。")
    {
        var download = Button("下载游戏 · 选择版本与加载器  →", (_, _) => { Choice = AddInstanceChoice.Download; DialogResult = true; }, true);
        download.Margin = new Thickness(0, 0, 0, 12); Body.Children.Add(download);
        var directory = Button("添加游戏文件夹（可多选）  →", (_, _) => { Choice = AddInstanceChoice.Folder; DialogResult = true; });
        directory.Margin = new Thickness(0, 0, 0, 14); Body.Children.Add(directory);
        var archive = Button("导入 .mrpack / .zip 整合包  →", (_, _) => { Choice = AddInstanceChoice.Archive; DialogResult = true; });
        archive.Margin = new Thickness(0); Body.Children.Add(archive);
    }
}
