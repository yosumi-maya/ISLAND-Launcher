using System.IO;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
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
        public static readonly byte[] ExistingDependency = Encoding.UTF8.GetBytes("owned already installed prerequisite fixture");
        public ContentFilter? LastFilter;
        public int LastPage;
        public int SearchRequests;
        public readonly List<ContentPlan> Runs = [];
        public TaskCompletionSource? InstallGate;
        public TaskCompletionSource<CatalogSearchPage>? SearchGate;
        public Task<CatalogSearchPage> SearchAsync(AppSettings settings, ContentFilter filter, int page, CancellationToken token)
        {
            SearchRequests++; LastFilter = filter; LastPage = page;
            return SearchGate?.Task ?? Task.FromResult(new CatalogSearchPage([new("fixture", ContentDownloads.Label(filter.Kind) + " Fixture", "A fictional downloadable resource used to verify native launcher installation.", PackPlatform.Modrinth, 153400, DateTimeOffset.Now.AddDays(-3), DateTimeOffset.Now, ["1.20.1"], [filter.Loader], "https://modrinth.com/mod/fixture") { Categories = ["fabric", "neoforge", "quilt", "technology", "optimization"] }], page, 42, page < 2, []));
        }
        public Task<IReadOnlyList<CatalogVersion>> GetVersionsAsync(AppSettings settings, CatalogProject project, ContentFilter filter, CancellationToken token)
        { LastFilter = filter; return Task.FromResult<IReadOnlyList<CatalogVersion>>([
            new("release", "Fixture compatible release", ["1.20.1"], [filter.Kind == ContentKind.Shader ? "iris" : filter.Kind == ContentKind.ResourcePack ? "minecraft" : "fabric"], DateTimeOffset.Now, "正式版", new()),
            new("incompatible", "Another Minecraft release", ["1.21.1"], ["neoforge"], DateTimeOffset.Now.AddDays(-1), "正式版", new())]); }
        public Task<ContentPlan> PrepareAsync(AppSettings settings, CatalogProject project, CatalogVersion version, ContentFilter filter, ContentTarget target, CancellationToken token)
        {
            var file = new ContentFile(project.Id, version.Id, filter.Kind, project.Title, "fixture-" + ContentDownloads.Slug(filter.Kind) + (filter.Kind == ContentKind.Mod ? ".jar" : ".zip"), "https://fixture.invalid/file", 10, new('a', 40), new('b', 128));
            var files = filter.Kind != ContentKind.Mod ? new[] { file } : new[]
            {
                new ContentFile("manual-dependency", "dep-release", ContentKind.Mod, "自定义前置", "manual-dependency.jar", "https://fixture.invalid/manual", 10, new('c', 40), new('d', 128)) { IsDependency = true },
                new ContentFile("known-dependency", "known-release", ContentKind.Mod, "已安装的语言支持前置", "language-support.jar", "https://fixture.invalid/known", ExistingDependency.Length,
                    Convert.ToHexString(SHA1.HashData(ExistingDependency)), Convert.ToHexString(SHA512.HashData(ExistingDependency))) { IsDependency = true }, file
            };
            return Task.FromResult(new ContentPlan(target, project.Title, null, files));
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
            await Wait(() => Find<ListBox>(center, "PackResults").Items.Count > 0);
            check(service.SearchRequests == 1 && service.LastFilter?.Kind == ContentKind.Mod, "Entering Mods automatically loads the resource catalog once");
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
            Directory.CreateDirectory(Path.Combine(originalTarget.RuntimeDirectory, "mods"));
            await File.WriteAllBytesAsync(Path.Combine(originalTarget.RuntimeDirectory, "mods", "[前置]用户自定义名字.jar"), Service.ExistingDependency);
            service.InstallGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Exception? modalError = null;
            _ = window.Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await Wait(() => Application.Current.Windows.OfType<ContentReleaseWindow>().Any());
                    var release = Application.Current.Windows.OfType<ContentReleaseWindow>().Single();
                    await Wait(() => Children<Button>(release).Any(button => button.Content as string == "查看安装清单  →" && button.IsEnabled));
                    var browser = Children<CatalogReleaseBrowser>(release).Single();
                    check(service.LastFilter is { Minecraft: "", Loader: "" } && browser.ExactGameVersion.Text == "1.20.1"
                        && browser.CanInstall, "Resource details fetch all releases and initially select versions compatible with the frozen target");
                    browser.ExactGameVersion.Text = "全部"; browser.LoaderFilter.SelectedIndex = 0; release.UpdateLayout();
                    browser.ReleaseList.SelectedItem = browser.ReleaseList.Items.Cast<CatalogVersion>().Single(version => version.Id == "incompatible");
                    check(!Children<Button>(release).Single(button => button.Content as string == "查看安装清单  →").IsEnabled
                        && browser.ReleaseStatus.Text.Contains("不兼容"), "Browsing incompatible resource versions disables installation and explains why");
                    browser.ExactGameVersion.Text = "1.20.1"; browser.LoaderFilter.SelectedItem = "Fabric"; release.UpdateLayout();
                    capture(release, "Content-Releases");
                    _ = release.Dispatcher.BeginInvoke(new Action(() => Click(Children<Button>(release).Single(button => button.Content as string == "查看安装清单  →"))));
                    await Wait(() => Application.Current.Windows.OfType<ContentConfirmationWindow>().Any());
                    var confirm = Application.Current.Windows.OfType<ContentConfirmationWindow>().Single();
                    var confirmation = Children<ContentInstallConfirmation>(confirm).Single(); await Wait(() => confirmation.Ready); confirm.UpdateLayout();
                    check(Children<TextBlock>(confirm).Any(text => text.Text.Contains("fixture-mod.jar"))
                        && confirmation.InstallDestination.Text == Path.Combine(originalTarget.RuntimeDirectory, "mods"), "Resource confirmation shows the actual mods directory and filenames in separate readable fields");
                    var main = confirmation.Choices.Single(choice => !choice.CanToggle);
                    var known = confirmation.Choices.Single(choice => choice.File.ProjectId == "known-dependency");
                    var manual = confirmation.Choices.Single(choice => choice.File.ProjectId == "manual-dependency");
                    check(main.Role == "本体" && main.Install && known.Role == "前置" && !known.Install && known.Detection.Contains("内容校验一致")
                        && known.ExistingLabel.Contains("[前置]用户自定义名字.jar") && manual.Install, "Confirmation identifies main/dependency roles and automatically unchecks hash-confirmed renamed prerequisites");
                    main.Install = false; manual.Install = false;
                    check(main.Install && confirmation.SelectedPlan().InstallFiles.Count == 1, "The main mod stays selected while undetected dependencies can be manually excluded");
                    Click(confirmation.DetectAgain); await Wait(() => confirmation.Ready);
                    check(!manual.Install, "Rerunning automatic detection preserves a user's manual prerequisite choice");
                    capture(confirm, "Content-Confirmation");
                    confirm.Width = 680; confirm.Height = 620; confirm.UpdateLayout(); capture(confirm, "Content-Confirmation-Compact");
                    var context = new AppearanceRenderContext(); context.Apply(new LauncherAppearance { DarkTheme = true }, "#2463EB");
                    confirm.SetValue(MaterialBorder.RenderContextProperty, context); ThemeManager.Apply("#2463EB", confirm.Resources, true); confirm.UpdateLayout();
                    check(ThemeManager.Contrast(((SolidColorBrush)confirm.FindResource("Ink")).Color, ((SolidColorBrush)confirm.FindResource("WindowBackground")).Color) >= 4.5,
                        "The installation confirmation retains readable text in the dark theme");
                    capture(confirm, "Content-Confirmation-Dark");
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
            check(service.Runs[0].InstallFiles.Count == 1 && service.Runs[0].SkippedDependencies.Count == 2,
                "Confirmed per-dependency choices reach the queued installation plan");
            check(task.Work is not null && task.Subtitle.Contains("Fabric") && !Find<Button>(window, "LaunchButton").IsEnabled, "Resource tasks share the launcher queue and block conflicting game launch preparation");
            center.Queue.Pause(task); await Wait(() => task.State == DownloadTaskState.Paused);
            Find<RadioButton>(center, "ResourcesTab").IsChecked = true;
            check(service.LastFilter?.Kind == ContentKind.ResourcePack, "Entering resource packs automatically refreshes the correct catalog");
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
            Find<RadioButton>(center, "ShadersTab").IsChecked = true;
            check(service.LastFilter?.Kind == ContentKind.Shader, "Entering shaders automatically refreshes the correct catalog");
            Find<ComboBox>(center, "PackLoader").SelectedItem = "Iris";
            check(versionFilter.Text == "全部", "Shaders default to the visible All versions option");
            await center.SearchAsync(0);
            check(service.LastFilter is { Kind: ContentKind.Shader, Loader: "iris" } && center.FindName("ContentTargetHint") is null, "Shader downloads filter Iris without the removed target explanation");
            capture(window, "Content-Shaders-1020");
            var gate = service.SearchGate = new(TaskCreationOptions.RunContinuationsAsynchronously); var late = center.SearchAsync(0);
            Find<RadioButton>(center, "ResourcesTab").IsChecked = true; service.SearchGate = null;
            await center.SearchAsync(0); gate.SetResult(new([], 0, 0, false, [])); await late;
            check(Find<ListBox>(center, "PackResults").Items.Cast<PackSearchCard>().Single().Project.Title.StartsWith("资源包"), "A late search from another content tab cannot replace current results");
            foreach (var kind in Enum.GetValues<ContentKind>())
            {
                var project = new CatalogProject("detail-fixture", ContentDownloads.Label(kind) + " 项目预览", "项目介绍、兼容版本与下载列表使用统一布局。", PackPlatform.Modrinth,
                    153400, DateTimeOffset.Now.AddDays(-3), DateTimeOffset.Now, ["1.20.1", "1.21.1"],
                    kind == ContentKind.Mod ? ["Fabric", "NeoForge"] : kind == ContentKind.Shader ? ["Iris", "OptiFine"] : ["Minecraft"], "https://modrinth.com/");
                var detailFilter = new ContentFilter(kind, Minecraft: "1.20.1", Loader: kind == ContentKind.Mod ? "fabric" : kind == ContentKind.Shader ? "iris" : "");
                var dialog = new ContentReleaseWindow(window, project, detailFilter, originalTarget, state.DownloadSettings(), service, _ => throw new Exception("Preview must not enqueue."))
                    { ShowInTaskbar = false };
                try
                {
                    dialog.Show(); await Wait(() => Children<CatalogReleaseBrowser>(dialog).Single().CanInstall); dialog.UpdateLayout();
                    var detail = Children<CatalogReleaseBrowser>(dialog).Single();
                    check(Children<Expander>(detail.ReleaseList).Any() && detail.ExactGameVersion.Text == "1.20.1", "Unified resource detail cards, version chips and release groups work for " + kind);
                    capture(dialog, "Pcl-Details-" + kind);
                    dialog.Width = 680; dialog.Height = 620; dialog.UpdateLayout();
                    var install = Children<Button>(dialog).Single(button => button.Content as string == "查看安装清单  →");
                    check(install.TransformToAncestor(dialog).TransformBounds(new Rect(install.RenderSize)).Right < dialog.ActualWidth && detail.ReleaseList.ActualHeight > 80,
                        "Resource detail actions and grouped versions fit compact windows: " + kind);
                    capture(dialog, "Pcl-Details-" + kind + "-Compact");
                }
                finally { dialog.Close(); }
            }
            Find<RadioButton>(center, "PacksTab").IsChecked = true;
            check(Find<StackPanel>(center, "ContentTargetPanel").Visibility == Visibility.Collapsed && Find<ComboBox>(center, "PackLoader").Items.Count == 4 && versionFilter.Text == "全部",
                "Returning to modpacks restores its original filters and hides the content target selector");
        }
        finally { await center.ShutdownAsync(); window.Close(); }
    }
}
