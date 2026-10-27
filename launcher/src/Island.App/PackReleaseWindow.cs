using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using BlockDock.Core;
using Island.Core;

namespace Island.App;

public sealed class PackConfirmationWindow : FormWindow
{
    private readonly TextBox name = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    public InstallRequest? Request { get; private set; }
    public AppSettings Settings { get; }
    public PackConfirmationWindow(Window owner, PackInfo pack, AppSettings settings) : base(owner, "确认安装整合包", $"{pack.Name}\nMinecraft {pack.MinecraftVersion} · {pack.Loader} · {pack.FormatLabel}\n安装到：{settings.GameDirectory}")
    {
        DownloadAvailability.RequireSupported(pack); Settings = DownloadAvailability.Snapshot(settings);
        Label("新实例名称"); name.Text = SafePaths.SuggestedName(pack.Name); Body.Children.Add(name);
        var optional = new CheckBox { Content = $"下载可选模组（{pack.OptionalCount} 项）", IsChecked = settings.IncludeOptionalMods, IsEnabled = pack.OptionalCount > 0, Margin = new Thickness(0, 14, 0, 0) }; Body.Children.Add(optional); Body.Children.Add(status);
        Actions.Children.Add(Button("取消", (_, _) => Close()));
        Actions.Children.Add(Button("确认并加入队列", (_, _) =>
        {
            try { Request = RequestFor(pack, Settings, name.Text); Settings.IncludeOptionalMods = optional.IsChecked == true; DialogResult = true; }
            catch (Exception ex) { status.Text = ex.Message; }
        }, true)); Body.Children.Add(Actions);
    }
    internal static InstallRequest RequestFor(PackInfo pack, AppSettings settings, string name)
    {
        DownloadAvailability.RequireSupported(pack); var id = SafePaths.InstanceName(name.Trim());
        var target = SafePaths.Under(settings.GameDirectory, "versions/" + id);
        if (Directory.Exists(target) || File.Exists(target)) throw new IOException("已有同名实例，请修改名称。");
        if (pack.Loader != LoaderKind.Vanilla && id.Equals(pack.MinecraftVersion, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("加载器实例不能与原版版本号同名。");
        return new(pack.MinecraftVersion, id, pack.Loader, pack.LoaderVersion);
    }
}

public sealed class PackReleaseWindow : FormWindow
{
    private readonly ListBox releases = new() { Height = 250, DisplayMemberPath = "Display" };
    private readonly TextBox search = new();
    private readonly ComboBox gameVersion = new() { IsEditable = true, IsTextSearchEnabled = true };
    private readonly ComboBox channel = new() { ItemsSource = new[] { "全部通道", "正式版", "测试版", "预览版" }, SelectedIndex = 0 };
    private readonly ComboBox loader = new() { ItemsSource = new[] { "全部加载器", "Forge", "Fabric", "NeoForge" } };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly Button install = new();
    private readonly CancellationTokenSource lifetime = new();
    private IReadOnlyList<CatalogVersion> all = [];
    private bool closed;
    public PackReleaseWindow(Window owner, CatalogProject project, PackSearchFilter filter, AppSettings settings, ILauncherDownloads service,
        Action<InstallRequest, AppSettings, PackInfo> enqueue) : base(owner, "选择整合包版本", project.Title + " · Modrinth\n安装到：" + settings.GameDirectory, 820)
    {
        Label("筛选发布名称 / 版本号"); Body.Children.Add(search);
        var row = new Grid { Margin = new Thickness(0, 10, 0, 10) }; row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = new GridLength(12) }); row.ColumnDefinitions.Add(new());
        row.ColumnDefinitions.Add(new() { Width = new GridLength(12) }); row.ColumnDefinitions.Add(new());
        var game = new StackPanel(); game.Children.Add(new TextBlock { Text = "Minecraft 版本", Margin = new Thickness(0, 0, 0, 5) }); game.Children.Add(gameVersion); row.Children.Add(game);
        var loaders = new StackPanel(); loaders.Children.Add(new TextBlock { Text = "加载器", Margin = new Thickness(0, 0, 0, 5) }); loaders.Children.Add(loader); Grid.SetColumn(loaders, 2); row.Children.Add(loaders);
        var channels = new StackPanel(); channels.Children.Add(new TextBlock { Text = "发布通道", Margin = new Thickness(0, 0, 0, 5) }); channels.Children.Add(channel); Grid.SetColumn(channels, 4); row.Children.Add(channels); Body.Children.Add(row);
        Body.Children.Add(releases); Body.Children.Add(status); gameVersion.Text = VersionFilter.Display(filter.GameVersion);
        VersionFilter.SetVersions(gameVersion, []);
        gameVersion.LostKeyboardFocus += (_, _) => { if (VersionFilter.Value(gameVersion.Text).Length == 0) VersionFilter.SelectAll(gameVersion); };
        search.TextChanged += (_, _) => Filter(); gameVersion.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Filter())); channel.SelectionChanged += (_, _) => Filter();
        loader.SelectedIndex = filter.Loader switch { LoaderKind.Forge => 1, LoaderKind.Fabric => 2, LoaderKind.NeoForge => 3, _ => 0 }; loader.SelectionChanged += (_, _) => Filter();
        Actions.Children.Add(Button("取消", (_, _) => Close()));
        install = Button("下一步  →", async (_, _) =>
        {
            if (releases.SelectedItem is not CatalogVersion selected) { status.Text = "请先选择一个发布版本。"; return; }
            install.IsEnabled = false; releases.IsEnabled = false; status.Text = "正在下载并识别整合包清单…";
            search.IsEnabled = gameVersion.IsEnabled = loader.IsEnabled = channel.IsEnabled = false;
            try
            {
                var pack = await service.PrepareAsync(settings, project, selected, lifetime.Token); lifetime.Token.ThrowIfCancellationRequested();
                DownloadAvailability.RequireSupported(pack);
                var confirmation = new PackConfirmationWindow(this, pack, settings);
                if (confirmation.ShowDialog() == true) { enqueue(confirmation.Request!, confirmation.Settings, pack); DialogResult = true; }
                else status.Text = "可继续选择版本，或再次确认安装。";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!closed) status.Text = ex.Message; }
            finally { if (!closed) { search.IsEnabled = gameVersion.IsEnabled = loader.IsEnabled = channel.IsEnabled = releases.IsEnabled = true; Filter(); } }
        }, true); install.IsEnabled = false; Actions.Children.Add(install); Body.Children.Add(Actions);
        Loaded += async (_, _) =>
        {
            status.Text = "正在读取发布列表…";
            try
            {
                // Fetch the full Modrinth list, then allow local version/channel filtering without repeated requests.
                var page = await service.GetReleasesAsync(settings, project, filter with { GameVersion = "", Loader = null }, lifetime.Token);
                lifetime.Token.ThrowIfCancellationRequested(); all = page.Versions;
                VersionFilter.SetVersions(gameVersion, all.SelectMany(v => v.GameVersions)); Filter();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!closed) status.Text = ex.Message; }
        };
        Closed += (_, _) => { closed = true; lifetime.Cancel(); };
    }
    private void Filter()
    {
        var selected = (releases.SelectedItem as CatalogVersion)?.Id;
        var minecraft = VersionFilter.Value(gameVersion.Text);
        var list = all.Where(v => (search.Text.Trim().Length == 0 || (v.Name + " " + v.Id).Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase))
            && (minecraft.Length == 0 || v.GameVersions.Contains(minecraft))
            && (loader.SelectedIndex <= 0 || v.Loaders.Contains(((string)loader.SelectedItem).ToLowerInvariant(), StringComparer.OrdinalIgnoreCase))
            && (channel.SelectedIndex == 0 || v.Channel == channel.SelectedItem as string)).ToArray();
        releases.ItemsSource = list; releases.SelectedItem = list.FirstOrDefault(v => v.Id == selected) ?? list.FirstOrDefault();
        status.Text = $"显示 {list.Length} / 共 {all.Count} 个发布版本";
        install.IsEnabled = list.Length > 0;
    }
}
