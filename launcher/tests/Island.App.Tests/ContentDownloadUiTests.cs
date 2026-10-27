using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using BlockDock.App;
using BlockDock.Core;
using Island.App;
using Island.Core;

internal static class ContentDownloadUiTests
{
    private static T Find<T>(FrameworkElement parent, string name) => (T)parent.FindName(name);
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); if (child is T item) yield return item;
            foreach (var nested in Children<T>(child)) yield return nested;
        }
    }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static async Task Wait(Func<bool> predicate)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!predicate() && DateTime.UtcNow < until) await Task.Delay(20);
        if (!predicate()) throw new Exception("Resource UI did not reach the expected state.");
    }
    private sealed class Service : IContentDownloads
    {
        public ContentFilter? LastFilter;
        public int LastPage;
        public readonly List<ContentPlan> Runs = [];
        public TaskCompletionSource? InstallGate;
        public TaskCompletionSource<CatalogSearchPage>? SearchGate;
        public Task<CatalogSearchPage> SearchAsync(AppSettings settings, ContentFilter filter, int page, CancellationToken token)
        {
            LastFilter = filter; LastPage = page;
            return SearchGate?.Task ?? Task.FromResult(new CatalogSearchPage([new("fixture", ContentDownloads.Label(filter.Kind) + " Fixture", "A fictional downloadable resource used to verify native launcher installation.", PackPlatform.Modrinth, 153400, DateTimeOffset.Now.AddDays(-3), DateTimeOffset.Now, ["1.20.1"], [filter.Loader], "https://modrinth.com/mod/fixture") { Categories = ["fabric", "neoforge", "quilt", "technology", "optimization"] }], page, 42, page < 2, []));
        }
        public Task<IReadOnlyList<CatalogVersion>> GetVersionsAsync(AppSettings settings, CatalogProject project, ContentFilter filter, CancellationToken token)
        { LastFilter = filter; return Task.FromResult<IReadOnlyList<CatalogVersion>>([new("release", "Fixture compatible release", [filter.Minecraft], [filter.Loader], DateTimeOffset.Now, "正式版", new())]); }
        public Task<ContentPlan> PrepareAsync(AppSettings settings, CatalogProject project, CatalogVersion version, ContentFilter filter, ContentTarget target, CancellationToken token)
        {
            var file = new ContentFile(project.Id, version.Id, filter.Kind, project.Title, "fixture-" + ContentDownloads.Slug(filter.Kind) + (filter.Kind == ContentKind.Mod ? ".jar" : ".zip"), "https://fixture.invalid/file", 10, new('a', 40), new('b', 128));
            return Task.FromResult(new ContentPlan(target, project.Title, null, [file]));
        }
        public async Task<string> InstallAsync(AppSettings settings, ContentPlan plan, IProgress<InstallProgress> progress, CancellationToken token)
        {
            Runs.Add(plan); progress.Report(new("Resource fixture", plan.Name, 1, 2, 100, 50));
            if (InstallGate is not null) await InstallGate.Task.WaitAsync(token);
            foreach (var path in plan.TargetFiles) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllTextAsync(path, "owned resource fixture", token); }
            return plan.Target.RuntimeDirectory;
        }
    }
    public static async Task RunLiveAsync(Window owner, string root, Action<bool, string> check, Action<Window, string> capture)
    {
        var data = Path.Combine(root, "live-content"); var game = Path.Combine(data, "game"); var versionFolder = Path.Combine(game, "versions", "ContentPreview"); Directory.CreateDirectory(versionFolder);
        File.WriteAllText(Path.Combine(versionFolder, "ContentPreview.json"), "{}");
        File.WriteAllText(Path.Combine(versionFolder, "blockdock-instance.json"), "{\"minecraft\":\"1.20.1\",\"loader\":\"Fabric\"}");
        var store = new LauncherStore(Path.Combine(data, "settings")); store.Save(new LauncherState { GameDirectory = game });
        var cache = new JavaRuntimeCache(store.DirectoryPath, (_, _, _) => Task.FromResult<IReadOnlyList<JavaInstallation>>([]));
        var window = new MainWindow(store, javaRuntimes: cache, downloadService: new DownloadCenterUiTests.Service())
        { Owner = owner, Left = -16000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
        window.Show(); var center = Find<DownloadCenter>(window, "DownloadCenterView"); var service = new ContentDownloads();
        try
        {
            Find<RadioButton>(window, "NavDownloads").IsChecked = true;
            foreach (var (tab, kind) in new[] { ("ModsTab", ContentKind.Mod), ("ShadersTab", ContentKind.Shader), ("ResourcesTab", ContentKind.ResourcePack) })
            {
                Find<RadioButton>(center, tab).IsChecked = true;
                await center.SearchAsync(0); window.UpdateLayout();
                var results = Find<ListBox>(center, "PackResults");
                check(results.Items.Count > 0, "Real Modrinth search returns " + ContentDownloads.Label(kind));
                var project = results.Items.Cast<PackSearchCard>().First().Project;
                var target = (ContentTarget)Find<ComboBox>(center, "ContentTargetBox").SelectedItem;
                var filter = new ContentFilter(kind, Minecraft: target.Minecraft, Loader: kind == ContentKind.Mod ? target.ModLoader : "");
                var versions = await service.GetVersionsAsync(new() { GameDirectory = game }, project, filter, default);
                check(versions.Count > 0, "Live resource release list has instance-compatible versions: " + kind);
                var plan = await service.PrepareAsync(new() { GameDirectory = game }, project, versions[0], filter, target, default);
                check(plan.Files.Count > 0 && plan.Files.All(file => file.Sha512.Length == 128), "Live resource planning resolves verified files and required dependencies: " + kind);
                if (kind == ContentKind.Mod && plan.Files.Sum(file => file.Size) <= 8 * 1024 * 1024)
                {
                    await service.InstallAsync(new() { GameDirectory = game }, plan, new Progress<InstallProgress>(), default);
                    check(plan.TargetFiles.All(File.Exists), "A real Modrinth mod installs all files from its resolved plan into an owned disposable preview instance");
                }
                var until = DateTime.UtcNow.AddSeconds(20);
                while (Children<PackArtwork>(results).SelectMany(Children<Image>).Any(image => image.Source is null) && DateTime.UtcNow < until) await Task.Delay(100);
                foreach (var size in new[] { (1240d, 830d), (1020d, 700d) })
                {
                    window.Width = size.Item1; window.Height = size.Item2; window.UpdateLayout(); await Task.Delay(200);
                    capture(window, "Live-Content-" + kind + "-" + (int)size.Item1);
                }
            }
        }
        finally { await center.ShutdownAsync(); window.Close(); }
    }

    public static async Task RunAsync(Window owner, string root, Action<bool, string> check, Action<Window, string> capture)
    {
        var data = Path.Combine(root, "content-ui"); var game = Path.Combine(data, "game");
        foreach (var (id, loader) in new[] { ("Fabric fixture", "Fabric"), ("Vanilla fixture", "Vanilla") })
        {
            var folder = Path.Combine(game, "versions", id); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, id + ".json"), "{}");
            File.WriteAllText(Path.Combine(folder, "blockdock-instance.json"), new JsonObject { ["minecraft"] = "1.20.1", ["loader"] = loader }.ToJsonString());
        }
        var store = new LauncherStore(Path.Combine(data, "settings")); var state = new LauncherState { GameDirectory = game }; state.EnsureGameFolders(); store.Save(state);
        var service = new Service(); var cache = new JavaRuntimeCache(store.DirectoryPath, (_, _, _) => Task.FromResult<IReadOnlyList<JavaInstallation>>([]));
        var window = new MainWindow(store, javaRuntimes: cache, downloadService: new DownloadCenterUiTests.Service(), contentService: service)
        { Owner = owner, Left = -16000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
        window.Show(); var center = Find<DownloadCenter>(window, "DownloadCenterView");
        try
        {
            Find<RadioButton>(window, "NavDownloads").IsChecked = true; Find<RadioButton>(center, "ModsTab").IsChecked = true;
            var targetBox = Find<ComboBox>(center, "ContentTargetBox");
            check(targetBox.Items.Count == 2, "Content downloads offer installed instances across registered folders");
            targetBox.SelectedItem = targetBox.Items.Cast<ContentTarget>().Single(target => target.Loader == "Fabric");
            var versionFilter = Find<ComboBox>(center, "PackGameVersion");
            check(versionFilter.Text == "全部", "Selecting a resource target does not auto-fill its version or instance name into the search filter");
            await center.SearchAsync(0); window.UpdateLayout();
            check(service.LastFilter is { Kind: ContentKind.Mod, Minecraft: "", Loader: "fabric" }
                && Find<ListBox>(center, "PackResults").Items.Cast<PackSearchCard>().Single().Tags.SequenceEqual(new[] { "科技", "性能优化" }), "Mod search defaults to all Minecraft versions while preserving loader and category filters");
            versionFilter.Text = "1.19.2";
            targetBox.SelectedItem = targetBox.Items.Cast<ContentTarget>().Single(target => target.Loader == "Vanilla");
            check(versionFilter.Text == "1.19.2", "Changing the target instance preserves an explicitly entered search version");
            targetBox.SelectedItem = targetBox.Items.Cast<ContentTarget>().Single(target => target.Loader == "Fabric");
            versionFilter.SelectedItem = "全部";
            Click(Find<Button>(center, "NextPage")); await Wait(() => service.LastPage == 1);
            check(service.LastFilter is { Kind: ContentKind.Mod, Loader: "fabric" }, "Content pagination preserves the applied type and compatibility filters");
            foreach (var size in new[] { (1240d, 830d), (1020d, 700d) })
            {
                window.Width = size.Item1; window.Height = size.Item2; window.UpdateLayout();
                var button = Find<Button>(center, "ChoosePackButton");
                check(button.TransformToAncestor(window).TransformBounds(new Rect(button.RenderSize)).Right < window.ActualWidth && Find<ListBox>(center, "PackResults").ActualHeight > 65,
                    "Content instance selection, rows and install action remain visible at " + (int)size.Item1);
                capture(window, "Content-Mods-" + (int)size.Item1);
            }
            var originalTarget = (ContentTarget)targetBox.SelectedItem;
            service.InstallGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Exception? modalError = null;
            _ = window.Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await Wait(() => Application.Current.Windows.OfType<ContentReleaseWindow>().Any());
                    var release = Application.Current.Windows.OfType<ContentReleaseWindow>().Single();
                    await Wait(() => Children<Button>(release).Any(button => button.Content as string == "查看安装清单  →" && button.IsEnabled));
                    check(service.LastFilter is { Minecraft: "1.20.1", Loader: "fabric" }, "Resource release selection is bound to the target instance compatibility");
                    capture(release, "Content-Releases");
                    _ = release.Dispatcher.BeginInvoke(new Action(() => Click(Children<Button>(release).Single(button => button.Content as string == "查看安装清单  →"))));
                    await Wait(() => Application.Current.Windows.OfType<ContentConfirmationWindow>().Any());
                    var confirm = Application.Current.Windows.OfType<ContentConfirmationWindow>().Single(); capture(confirm, "Content-Confirmation");
                    check(Children<TextBlock>(confirm).Any(text => text.Text.Contains("fixture-mod.jar") && text.Text.Contains(originalTarget.RuntimeDirectory)), "Resource confirmation lists actual filenames and the frozen target directory");
                    Click(Children<Button>(confirm).Single(button => button.Content as string == "确认并加入队列"));
                }
                catch (Exception ex)
                {
                    modalError = ex;
                    foreach (var dialog in Application.Current.Windows.OfType<Window>().Where(dialog => dialog is ContentReleaseWindow or ContentConfirmationWindow).ToArray()) dialog.Close();
                }
            }));
            Click(Find<Button>(center, "ChoosePackButton")); if (modalError is not null) throw modalError;
            await Wait(() => service.Runs.Count > 0); var task = center.Queue.Items.Single();
            check(task.Work is not null && task.Subtitle.Contains("Fabric") && !Find<Button>(window, "LaunchButton").IsEnabled, "Resource tasks share the launcher queue and block conflicting game launch preparation");
            center.Queue.Pause(task); await Wait(() => task.State == DownloadTaskState.Paused);
            Find<RadioButton>(center, "ResourcesTab").IsChecked = true;
            check(versionFilter.Text == "全部", "Resource packs default to the visible All versions option");
            targetBox.SelectedItem = targetBox.Items.Cast<ContentTarget>().Single(target => target.Loader == "Vanilla");
            center.Queue.Resume(task); await Wait(() => service.Runs.Count == 2);
            check(service.Runs.Last().Target.Key == originalTarget.Key, "Pausing, changing browsing target and resuming preserves the task's original instance");
            service.InstallGate.SetResult(); await Wait(() => task.State == DownloadTaskState.Completed);
            check(File.Exists(Path.Combine(originalTarget.RuntimeDirectory, "mods", "fixture-mod.jar")) && InstanceCatalog.ListInstalled(game).Count == 2,
                "Completed resource tasks install into the selected instance without creating phantom game instances");
            await center.SearchAsync(0);
            check(service.LastFilter is { Kind: ContentKind.ResourcePack, Loader: "" } && !Find<ComboBox>(center, "PackLoader").IsEnabled,
                "Resource packs use game-version filtering without applying a mod-loader facet");
            capture(window, "Content-ResourcePacks-1020");
            Find<RadioButton>(center, "ShadersTab").IsChecked = true; Find<ComboBox>(center, "PackLoader").SelectedItem = "Iris";
            check(versionFilter.Text == "全部", "Shaders default to the visible All versions option");
            await center.SearchAsync(0);
            check(service.LastFilter is { Kind: ContentKind.Shader, Loader: "iris" } && Find<TextBlock>(center, "ContentTargetHint").Text.Contains("游戏内启用"), "Shader downloads filter Iris and explain the in-game enable step");
            capture(window, "Content-Shaders-1020");
            var gate = service.SearchGate = new(TaskCreationOptions.RunContinuationsAsynchronously); var late = center.SearchAsync(0);
            Find<RadioButton>(center, "ResourcesTab").IsChecked = true; service.SearchGate = null;
            await center.SearchAsync(0); gate.SetResult(new([], 0, 0, false, [])); await late;
            check(Find<ListBox>(center, "PackResults").Items.Cast<PackSearchCard>().Single().Project.Title.StartsWith("资源包"), "A late search from another content tab cannot replace current results");
            Find<RadioButton>(center, "PacksTab").IsChecked = true;
            check(Find<StackPanel>(center, "ContentTargetPanel").Visibility == Visibility.Collapsed && Find<ComboBox>(center, "PackLoader").Items.Count == 4 && versionFilter.Text == "全部",
                "Returning to modpacks restores its original filters and hides the content target selector");
        }
        finally { await center.ShutdownAsync(); window.Close(); }
    }
}
