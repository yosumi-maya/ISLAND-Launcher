using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using BlockDock.App;
using BlockDock.Core;
using Island.App;
using Island.Core;

internal static class DownloadCenterUiTests
{
    private static T Find<T>(FrameworkElement parent, string name) => (T)parent.FindName(name);
    private static IEnumerable<T> Children<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); if (child is T item) yield return item;
            foreach (var nested in Children<T>(child)) yield return nested;
        }
    }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static async Task Wait(Func<bool> predicate)
    {
        var end = DateTime.UtcNow.AddSeconds(8);
        while (!predicate() && DateTime.UtcNow < end) await Task.Delay(20);
        if (!predicate()) throw new Exception("Download center did not reach the expected state.");
    }
    internal sealed class Service : ILauncherDownloads
    {
        public int GameRequests;
        public int LastPage;
        public PackSearchFilter? LastFilter;
        public TaskCompletionSource<CatalogSearchPage>? SearchGate;
        public Exception? SearchError;
        public readonly Dictionary<string, TaskCompletionSource> Gates = [];
        public readonly List<string> Runs = [];
        public TaskCompletionSource Gate(string name)
        { if (!Gates.TryGetValue(name, out var gate)) Gates[name] = gate = new(TaskCreationOptions.RunContinuationsAsynchronously); return gate; }
        public Task<IReadOnlyList<GameVersion>> GetGamesAsync(AppSettings settings, CancellationToken token)
        { GameRequests++; return Task.FromResult<IReadOnlyList<GameVersion>>([new("1.20.1", "release", "https://fixture.invalid/version", null, DateTimeOffset.Now), new("fixture-snapshot", "snapshot", "https://fixture.invalid/snapshot", null, DateTimeOffset.Now)]); }
        public Task<IReadOnlyList<LoaderVersion>> GetLoadersAsync(AppSettings settings, LoaderKind loader, string version, CancellationToken token)
            => Task.FromResult<IReadOnlyList<LoaderVersion>>([new("fixture-loader", "fixture-loader")]);
        public Task<CatalogSearchPage> SearchAsync(AppSettings settings, PackSearchFilter filter, int page, CancellationToken token)
        {
            LastFilter = filter; LastPage = page;
            if (SearchError is not null) return Task.FromException<CatalogSearchPage>(SearchError);
            return SearchGate?.Task ?? Task.FromResult(new CatalogSearchPage([
                new("fixture", filter.Name.Length == 0 ? "Fixture Pack" : filter.Name, "A fictional pack used for local UI verification with a long description that should stay on a single line.", PackPlatform.Modrinth, 19357000, DateTimeOffset.Now.AddDays(-4), DateTimeOffset.Now, ["1.20.1"], ["Fabric"], "https://modrinth.com/modpack/fixture") { Categories = ["fabric", "multiplayer", "lightweight", "optimization"] },
                new("second-fixture", "Another pack with a deliberately long title for compact row layout verification", "A separate row must keep its own hover and keyboard selection.", PackPlatform.Modrinth, 1234, DateTimeOffset.MinValue, DateTimeOffset.Now, ["1.19.2", "1.20.1"], ["Forge"], "https://modrinth.com/modpack/fixture-2")
            ], page, 42, page < 2, []));
        }
        public Task<CatalogVersionPage> GetReleasesAsync(AppSettings settings, CatalogProject project, PackSearchFilter filter, CancellationToken token)
            => Task.FromResult(new CatalogVersionPage([new("fixture-release", "Fixture release", ["1.20.1"], ["fabric"], DateTimeOffset.Now, "正式版", new())], null));
        public Task<PackInfo> PrepareAsync(AppSettings settings, CatalogProject project, CatalogVersion release, CancellationToken token)
            => Task.FromResult(new PackInfo { ArchivePath = Path.Combine(settings.GameDirectory, "owned-fixture.mrpack"), Name = "Fixture Pack", MinecraftVersion = "1.20.1", Format = PackFormat.Modrinth, OptionalCount = 2, Manifest = new() });
        public Task<PackInfo> InspectAsync(string file, CancellationToken token) => throw new NotSupportedException();
        public async Task<string> InstallAsync(AppSettings settings, InstallRequest request, PackInfo? pack, IProgress<InstallProgress> progress, CancellationToken token)
        {
            Runs.Add(request.InstanceName); progress.Report(new("Fixture download", "Owned local data", 3, 10, 1024, 512));
            await Gate(request.InstanceName).Task.WaitAsync(token);
            var directory = Path.Combine(settings.GameDirectory, "versions", request.InstanceName); Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, request.InstanceName + ".json"), new JsonObject { ["id"] = request.InstanceName, ["mainClass"] = "Fixture", ["type"] = "release" }.ToJsonString());
            return directory;
        }
    }
    public static async Task RunLiveAsync(Window owner, string root, Action<bool, string> check, Action<Window, string> capture)
    {
        var store = new LauncherStore(Path.Combine(root, "live-catalog-settings"));
        store.Save(new LauncherState { GameDirectory = Path.Combine(root, "live-catalog-game") });
        var cache = new JavaRuntimeCache(store.DirectoryPath, (_, _, _) => Task.FromResult<IReadOnlyList<JavaInstallation>>([]));
        var window = new MainWindow(store, javaRuntimes: cache) { Owner = owner, Left = -16000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
        window.Show();
        var center = Find<DownloadCenter>(window, "DownloadCenterView");
        try
        {
            Find<RadioButton>(window, "NavDownloads").IsChecked = true;
            Find<RadioButton>(center, "PacksTab").IsChecked = true;
            await center.SearchAsync(0); window.UpdateLayout();
            var list = Find<ListBox>(center, "PackResults");
            check(list.Items.Count > 0, "Live Modrinth search returns real pack rows");
            var until = DateTime.UtcNow.AddSeconds(30);
            while (Children<PackArtwork>(list).SelectMany(Children<Image>).Any(image => image.Source is null) && DateTime.UtcNow < until) await Task.Delay(100);
            check(Children<PackArtwork>(list).SelectMany(Children<Image>).Any(image => image.Source is not null), "Live HTTPS pack icons decode and display in native WPF rows");
            check(list.Items.Cast<PackSearchCard>().Any(card => card.Tags.Count > 0), "Real Modrinth categories reach compact result cards");
            foreach (var themeName in new[] { "森林绿", "玫瑰红", "海岛夜色" })
            {
                var choice = Find<WrapPanel>(window, "ThemePresetButtons").Children.OfType<Button>()
                    .Single(button => ((StackPanel)button.Content).Children.OfType<TextBlock>().Single().Text == themeName);
                Click(choice);
                foreach (var size in new[] { (1240d, 830d), (1020d, 700d) })
                {
                    window.Width = size.Item1; window.Height = size.Item2; window.UpdateLayout(); await Task.Delay(250);
                    capture(window, "Live-Packs-" + themeName + "-" + (int)size.Item1);
                }
            }
        }
        finally { await center.ShutdownAsync(); window.Close(); }
    }

    public static async Task RunAsync(Window owner, string root, Action<bool, string> check, Action<Window, string> capture)
    {
        foreach (var test in DownloadQueueTests.Cases()) { test.Run(Path.Combine(root, Guid.NewGuid().ToString("N"))); check(true, "Reused downloader queue: " + test.Name); }
        var data = Path.Combine(root, "download-center-ui"); var firstRoot = Path.Combine(data, "first"); var secondRoot = Path.Combine(data, "second");
        var store = new LauncherStore(Path.Combine(data, "settings")); var state = new LauncherState { GameDirectory = firstRoot, AutoMemory = false, MemoryMb = 512 };
        state.EnsureGameFolders(); store.Save(state); var service = new Service(); var allowClose = false;
        var cache = new JavaRuntimeCache(store.DirectoryPath, (_, _, _) => Task.FromResult<IReadOnlyList<JavaInstallation>>([]));
        var window = new MainWindow(store, javaRuntimes: cache, downloadService: service, confirmDownloadClose: () => allowClose)
            { Owner = owner, Left = -16000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
        try
        {
            window.Show(); window.UpdateLayout();
            check(service.GameRequests == 0, "Launcher startup does not query downloader services before opening the download center");
            Find<RadioButton>(window, "NavDownloads").IsChecked = true; var center = Find<DownloadCenter>(window, "DownloadCenterView");
            await Wait(() => Find<Button>(center, "InstallGameButton").IsEnabled); window.UpdateLayout();
            check(center.IsVisible && Find<TextBlock>(center, "AvailabilityNotice").Text == DownloadAvailability.CurseForgeNotice
                && Find<ComboBox>(center, "GameVersions").Items.Count == 1 && Find<TextBlock>(center, "Destination").Text.Contains(firstRoot), "Native download center shows game selection, the target directory and explicit CurseForge availability");
            Find<CheckBox>(center, "ShowSnapshots").IsChecked = true;
            check(Find<ComboBox>(center, "GameVersions").Items.Count == 2, "Game downloads can include snapshots and historical versions");
            Find<CheckBox>(center, "ShowSnapshots").IsChecked = false;
            foreach (var size in new[] { (1240d, 830d), (1020d, 700d) })
            {
                window.Width = size.Item1; window.Height = size.Item2; window.UpdateLayout();
                var install = Find<Button>(center, "InstallGameButton"); install.BringIntoView(); window.UpdateLayout();
                check(install.ActualWidth > 100 && install.TransformToAncestor(window).TransformBounds(new Rect(install.RenderSize)).Right < window.ActualWidth, "Game download controls stay within the launcher at " + (int)size.Item1);
                capture(window, "Downloads-Games-" + (int)size.Item1);
            }
            window.Width = 1240; window.Height = 830;
            Find<RadioButton>(center, "PacksTab").IsChecked = true;
            var versionFilter = Find<ComboBox>(center, "PackGameVersion");
            check(versionFilter.Text == "全部" && versionFilter.SelectedItem as string == "全部" && !versionFilter.Items.Cast<string>().Contains(""), "Pack version filtering defaults to a visible All option without a blank dropdown item");
            await center.SearchAsync(0);
            check(service.LastFilter?.GameVersion == "", "The visible All version option sends an empty version filter to the catalog");
            check(versionFilter.IsEditable && versionFilter.Items.Cast<string>().Contains("1.20.1") && !versionFilter.Items.Cast<string>().Contains("fixture-snapshot"), "Pack Minecraft filter offers releases in an editable dropdown");
            versionFilter.SelectedItem = "1.20.1";
            check(versionFilter.Text == "1.20.1", "Selecting a Minecraft version updates the editable filter");
            versionFilter.Text = "custom-version";
            await center.SearchAsync(0);
            check(service.LastFilter?.GameVersion == "custom-version", "Pack filtering retains versions typed outside the dropdown choices");
            Find<TextBox>(center, "PackQuery").Text = "Fresh pack"; Find<ComboBox>(center, "PackGameVersion").Text = "1.20.1";
            Find<ComboBox>(center, "PackLoader").SelectedIndex = 2; Find<ComboBox>(center, "PackSortBox").SelectedIndex = 1;
            await center.SearchAsync(0); window.UpdateLayout();
            check(service.LastFilter is { Name: "Fresh pack", GameVersion: "1.20.1", Platform: PackPlatform.Modrinth, Loader: LoaderKind.Fabric, Sort: PackSort.Downloads }
                && Find<ListBox>(center, "PackResults").Items.Count == 2, "Pack search applies the displayed name, version, loader and ordering without querying CurseForge");
            var packList = Find<ListBox>(center, "PackResults");
            var firstCard = (PackSearchCard)packList.Items[0];
            check(firstCard.Tags.SequenceEqual(new[] { "多人", "轻量", "性能优化" }) && firstCard.Downloads == "1935.7 万" && firstCard.Updated == "4 天前", "Compact pack rows show translated category tags, download counts and relative update dates");
            check(!Children<Grid>(packList).Any(CardMotion.GetIsCard) && Children<PackArtwork>(packList).Count() == 2,
                "Download rows have independent artwork and do not inherit instance card motion");
            var secondRow = (ListBoxItem)packList.ItemContainerGenerator.ContainerFromIndex(1);
            packList.SelectedIndex = 1; secondRow.Focus();
            check(secondRow.IsSelected && !((ListBoxItem)packList.ItemContainerGenerator.ContainerFromIndex(0)).IsSelected, "Keyboard focus and selection target only the chosen pack row");
            foreach (var width in new[] { 1240d, 1020d })
            {
                window.Width = width; window.UpdateLayout();
                check(Children<PackArtwork>(packList).All(icon => icon.ActualWidth == 48) && secondRow.ActualHeight < 80,
                    "Pack artwork stays visible and result rows stay compact at " + width);
                capture(window, "Downloads-Compact-Packs-" + (int)width);
            }
            window.Width = 1240; window.UpdateLayout();
            var retained = packList.ItemsSource; var retainedSelection = packList.SelectedItem;
            var canceledGate = service.SearchGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var canceledSearch = center.SearchAsync(0);
            check(ReferenceEquals(packList.ItemsSource, retained) && !Find<Button>(center, "ChoosePackButton").IsEnabled
                && Find<Button>(center, "CancelSearchButton").Visibility == Visibility.Visible, "Loading retains existing results and exposes cancellation while preventing installation from stale selection");
            Click(Find<Button>(center, "CancelSearchButton")); await canceledSearch;
            check(ReferenceEquals(packList.SelectedItem, retainedSelection) && Find<Button>(center, "SearchButton").IsEnabled
                && Find<Button>(center, "CancelSearchButton").Visibility == Visibility.Collapsed, "Canceling a query immediately restores the existing selection even if the provider ignores cancellation");
            service.SearchGate = null; canceledGate.SetResult(new([], 0, 0, false, []));
            var escapeGate = service.SearchGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var escapeSearch = center.SearchAsync(0);
            center.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(center)!, Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            await escapeSearch;
            check(Find<Button>(center, "SearchButton").IsEnabled && ReferenceEquals(packList.ItemsSource, retained), "Escape cancels a pending search without discarding its previous results");
            service.SearchGate = null; escapeGate.SetResult(new([], 0, 0, false, []));
            service.SearchError = new IOException("Fixture connection unavailable"); await center.SearchAsync(0);
            check(ReferenceEquals(packList.ItemsSource, retained) && Find<TextBlock>(center, "PackSummary").Text.Contains("保留上次结果"), "A failed search retains usable previous results and reports recovery inline");
            service.SearchError = null;
            check(center.FocusSearch() && Find<TextBox>(center, "PackQuery").SelectionLength == Find<TextBox>(center, "PackQuery").Text.Length, "Search focus selects the query for efficient keyboard replacement");
            Click(Find<Button>(center, "NextPage")); await Wait(() => service.LastPage == 1 && Find<TextBlock>(center, "PackPageLabel").Text == "第 2 页");
            check(service.LastFilter?.Name == "Fresh pack", "Pack pagination retains the applied filter");
            capture(window, "Downloads-Packs-1240");
            var gate = service.SearchGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var stale = center.SearchAsync(0, new("Old query", Platform: PackPlatform.Modrinth)); service.SearchGate = null;
            await center.SearchAsync(0, new("Latest query", Platform: PackPlatform.Modrinth)); gate.SetResult(new([], 0, 0, false, [])); await stale;
            check(((PackSearchCard)Find<ListBox>(center, "PackResults").Items[0]).Project.Title == "Latest query", "A late canceled search cannot replace the latest pack results");
            Click(Children<Button>(center).Single(b => b.Content as string == "重置"));
            check(Find<TextBox>(center, "PackQuery").Text == "" && versionFilter.Text == "全部" && Find<ListBox>(center, "PackResults").Items.Count == 0 && Find<Button>(center, "SearchButton").IsEnabled, "Reset clears search state, selects All versions and leaves search usable");
            service.SearchError = new IOException("Fixture query failure"); await center.SearchAsync(0);
            check(Find<TextBlock>(center, "PackEmpty").IsVisible && Find<Button>(center, "BrowseCatalogButton").Content as string == "重新搜索"
                && !Find<Button>(center, "ChoosePackButton").IsEnabled, "An empty failed search exposes an actionable retry and disables installation");
            service.SearchError = null;
            await center.SearchAsync(0); service.Gate("Queued pack").SetResult();
            Exception? modalError = null;
            _ = window.Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await Wait(() => Application.Current.Windows.OfType<PackReleaseWindow>().Any());
                    var release = Application.Current.Windows.OfType<PackReleaseWindow>().Single();
                    await Wait(() => Children<Button>(release).Any(b => b.Content as string == "下一步  →" && b.IsEnabled));
                    var releaseGame = Children<ComboBox>(release).Single(box => box.IsEditable);
                    check(releaseGame.Text == "全部" && releaseGame.Items[0] as string == "全部" && !releaseGame.Items.Cast<string>().Contains(""), "Pack release filtering defaults to All instead of an empty dropdown item");
                    check(releaseGame.Items.Cast<string>().Contains("1.20.1"), "Pack release filtering also offers an editable Minecraft version dropdown");
                    releaseGame.Text = "missing-version"; await Task.Delay(30);
                    check(!Children<Button>(release).Single(b => b.Content as string == "下一步  →").IsEnabled, "A typed version with no releases disables installation");
                    releaseGame.Text = "1.20.1"; await Task.Delay(30);
                    capture(release, "Downloads-Pack-Releases");
                    _ = release.Dispatcher.BeginInvoke(new Action(() => Click(Children<Button>(release).Single(b => b.Content as string == "下一步  →"))));
                    await Wait(() => Application.Current.Windows.OfType<PackConfirmationWindow>().Any());
                    var confirmation = Application.Current.Windows.OfType<PackConfirmationWindow>().Single();
                    Children<TextBox>(confirmation).Single().Text = "Queued pack";
                    Children<CheckBox>(confirmation).Single().IsChecked = false; capture(confirmation, "Downloads-Pack-Confirmation");
                    Click(Children<Button>(confirmation).Single(b => b.Content as string == "确认并加入队列"));
                }
                catch (Exception ex)
                {
                    modalError = ex;
                    foreach (var dialog in Application.Current.Windows.OfType<Window>().Where(w => w is PackConfirmationWindow or PackReleaseWindow).ToArray()) dialog.Close();
                }
            }));
            Click(Find<Button>(center, "ChoosePackButton")); if (modalError is not null) throw modalError;
            await Wait(() => center.Queue.Items.Single().State == DownloadTaskState.Completed);
            check(center.Queue.Items.Single().Pack?.Format == PackFormat.Modrinth && !center.Queue.Items.Single().Settings.IncludeOptionalMods
                && File.Exists(Path.Combine(firstRoot, "versions", "Queued pack", "Queued pack.json")), "Selecting a release and confirming its name and optional mods installs through the shared queue");
            center.Queue.ClearEnded();
            Find<RadioButton>(center, "GamesTab").IsChecked = true; Find<TextBox>(center, "NewInstanceName").Text = "Queued A";
            Click(Find<Button>(center, "InstallGameButton")); var first = center.Queue.Items.Single();
            var second = center.Enqueue(new("1.20.1", "Queued B"), state.DownloadSettings()); await Wait(() => first.State == DownloadTaskState.Running); window.UpdateLayout();
            await Wait(() => service.Runs.Contains("Queued A"));
            center.Queue.FlushProgress(); window.UpdateLayout();
            check(second.State == DownloadTaskState.Queued && !Find<Button>(window, "LaunchButton").IsEnabled, "Multiple installs queue sequentially and block conflicting game launch preparation");
            check(first.Percent == 30 && first.Stage == "Fixture download" && Children<ProgressBar>(center).Any(bar => bar.Value == 30), "Task cards show the actual throttled progress through read-only bindings");
            capture(window, "Downloads-Tasks-1240");
            Find<TextBox>(window, "RootBox").Text = secondRoot; Find<ComboBox>(window, "SourceBox").SelectedValue = DownloadSource.Official;
            Find<ComboBox>(window, "DownloadParallelBox").SelectedItem = 16;
            Find<RadioButton>(window, "NavSettings").IsChecked = true;
            window.UpdateLayout();
            Click(Children<Button>(window).Single(b => b.Content as string == "保存设置"));
            check(store.Load().GameDirectory == secondRoot && store.Load().DownloadSettings().ParallelDownloads == 16 && first.Settings.GameDirectory == firstRoot && first.Settings.Source == DownloadSource.Auto && first.Settings.ParallelDownloads == 8, "Changing launcher settings affects new tasks while queued tasks retain their original destination, source and concurrency");
            Find<RadioButton>(window, "NavDownloads").IsChecked = true; window.UpdateLayout();
            Click(Children<Button>(center).Single(b => ReferenceEquals(b.DataContext, first) && b.Content as string == "暂停下载"));
            await Wait(() => first.State == DownloadTaskState.Paused && second.State == DownloadTaskState.Running && service.Runs.Contains("Queued B"));
            check(first.CanToggle && service.Runs.SequenceEqual(new[] { "Queued pack", "Queued A", "Queued B" }), "Pausing a card releases its installer and starts the next queued task");
            center.Queue.Cancel(second); await Wait(() => second.State == DownloadTaskState.Canceled);
            Click(Children<Button>(center).Single(b => ReferenceEquals(b.DataContext, first) && b.Content as string == "继续下载")); await Wait(() => first.State == DownloadTaskState.Running);
            Click(Children<Button>(center).Single(b => b.Content as string == "清除已结束"));
            check(center.Queue.Items.Count == 1 && ReferenceEquals(center.Queue.Items[0], first), "Clearing ended cards preserves the resumed task");
            service.Gate("Queued A").SetResult(); await Wait(() => first.State == DownloadTaskState.Completed);
            check(File.Exists(Path.Combine(firstRoot, "versions", "Queued A", "Queued A.json")) && store.Load().GameDirectory == secondRoot, "Resumed downloads install to the original folder without switching the launcher's current folder");
            Find<RadioButton>(window, "NavLibrary").IsChecked = true;
            var originalFolder = store.Load().GameFolders.Single(f => GameFolder.SamePath(f.Directory, firstRoot));
            ((RadioButton)Find<ItemsControl>(window, "FolderTabs").Items.Cast<RadioButton>().Single(b => b.Tag as string == originalFolder.Id)).IsChecked = true;
            check(Find<ListBox>(window, "InstanceList").Items.Cast<InstanceCard>().Any(c => c.Id == "Queued A"), "Completed downloads appear in the normal instance library");
            Find<RadioButton>(window, "NavDownloads").IsChecked = true;
            var closing = center.Enqueue(new("1.20.1", "Closing fixture"), state.DownloadSettings()); await Wait(() => closing.State == DownloadTaskState.Running);
            window.Close(); check(window.IsVisible && closing.State == DownloadTaskState.Running, "Declining close keeps the running download intact");
            allowClose = true; window.Close(); await Wait(() => !window.IsVisible);
            check(closing.State == DownloadTaskState.Canceled && !center.Queue.IsBusy, "Confirmed close waits for download cancellation before disposing the launcher");
        }
        finally { allowClose = true; if (window.IsVisible) { await Find<DownloadCenter>(window, "DownloadCenterView").ShutdownAsync(); window.Close(); } }
    }
}
