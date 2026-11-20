using System.IO;
using System.Windows;
using System.Windows.Controls;
using BlockDock.App;
using BlockDock.Core;
using Island.Core;

namespace Island.App;

public sealed class PackConfirmationWindow : FormWindow
{
    private readonly TextBox name = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    public InstallRequest? Request { get; private set; }
    public AppSettings Settings { get; }
    public PackConfirmationWindow(Window owner, PackInfo pack, AppSettings settings)
        : this(owner, pack.Name, $"Minecraft {pack.MinecraftVersion} · {pack.Loader} · {pack.FormatLabel}", settings,
            pack.OptionalCount, (snapshot, name) => RequestFor(pack, snapshot, name)) { }

    internal PackConfirmationWindow(Window owner, CatalogProject project, CatalogVersion release, AppSettings settings)
        : this(owner, project.Title, $"{release.Name} · {project.SourceLabel}", settings, null,
            (snapshot, name) => new("", InstanceCatalog.RequireAvailableName(snapshot.GameDirectory, name))) { }

    private PackConfirmationWindow(Window owner, string packName, string details, AppSettings settings, int? optionalCount,
        Func<AppSettings, string, InstallRequest> requestFor)
        : base(owner, "确认安装整合包", $"{packName}\n{details}\n安装到：{settings.GameDirectory}")
    {
        Settings = DownloadAvailability.Snapshot(settings);
        Label("新实例名称"); name.Text = SafePaths.SuggestedName(packName); Body.Children.Add(name);
        var optional = new CheckBox
        {
            Content = optionalCount is { } count ? $"下载可选模组（{count} 项）" : "下载可选模组",
            IsChecked = settings.IncludeOptionalMods, IsEnabled = optionalCount != 0, Margin = new Thickness(0, 14, 0, 0)
        };
        Body.Children.Add(optional); Body.Children.Add(status);
        Actions.Children.Add(Button("取消", (_, _) => Close()));
        var confirm = Button("确认并加入队列", (_, _) =>
        {
            try { Request = requestFor(Settings, name.Text); Settings.IncludeOptionalMods = optional.IsChecked == true; DialogResult = true; }
            catch (Exception ex) { status.Text = ex.Message; }
        }, true);
        void ValidateName()
        {
            try { requestFor(Settings, name.Text); status.Text = ""; confirm.IsEnabled = true; }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            { status.Text = ex.Message; confirm.IsEnabled = false; }
        }
        name.TextChanged += (_, _) => ValidateName();
        Actions.Children.Add(confirm); Body.Children.Add(Actions); ValidateName();
    }
    internal static InstallRequest RequestFor(PackInfo pack, AppSettings settings, string name)
    {
        DownloadAvailability.RequireSupported(pack); var id = InstanceCatalog.RequireAvailableName(settings.GameDirectory, name);
        if (pack.Loader != LoaderKind.Vanilla && id.Equals(pack.MinecraftVersion, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("加载器实例不能与原版版本号同名。");
        return new(pack.MinecraftVersion, id, pack.Loader, pack.LoaderVersion);
    }
}

public sealed class PackReleaseWindow : FormWindow
{
    private readonly CancellationTokenSource lifetime = new();
    private bool closed;
    public PackReleaseWindow(Window owner, CatalogProject project, PackSearchFilter filter, AppSettings settings, ILauncherDownloads service,
        Action<InstallRequest, AppSettings, PackReleaseDownload> enqueue) : base(owner, project.Title, "", 900)
    {
        var browser = new CatalogReleaseBrowser(project, filter.GameVersion, showFilters: false);
        Actions.Children.Add(Button("取消", (_, _) => Close()));
        void AddToQueue(bool customize)
        {
            if (!browser.CanInstall || browser.SelectedVersion is not { } selected) return;
            try
            {
                var request = new InstallRequest("", SafePaths.SuggestedName(project.Title));
                var snapshot = settings;
                if (customize)
                {
                    var confirmation = new PackConfirmationWindow(this, project, selected, settings);
                    if (confirmation.ShowDialog() != true) return;
                    request = confirmation.Request!;
                    snapshot = confirmation.Settings;
                }
                enqueue(request, snapshot, new(project, selected));
                DialogResult = true;
            }
            catch (Exception ex) { if (!closed) browser.ReleaseStatus.Text = ex.Message; }
        }
        var options = Button("安装选项", (_, _) => AddToQueue(true));
        var install = Button("加入队列", (_, _) => AddToQueue(false), true);
        options.IsEnabled = install.IsEnabled = false;
        browser.SelectionChanged += (_, _) => options.IsEnabled = install.IsEnabled = browser.CanInstall;
        Actions.Children.Add(options);
        Actions.Children.Add(install); KeepActionsVisible(); SetWorkspace(browser);
        SizeToContent = SizeToContent.Manual; Height = Math.Min(760, MaxHeight);
        Loaded += async (_, _) =>
        {
            try
            {
                var page = await service.GetReleasesAsync(settings, project, filter with { GameVersion = "", Loader = null }, lifetime.Token);
                lifetime.Token.ThrowIfCancellationRequested(); browser.SetVersions(page.Versions);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!closed) browser.ShowError(ex.Message); }
        };
        Closed += (_, _) => { closed = true; lifetime.Cancel(); };
    }
}
