using System.IO;
using System.Windows;
using System.Windows.Controls;
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
    private readonly CancellationTokenSource lifetime = new();
    private bool closed;
    public PackReleaseWindow(Window owner, CatalogProject project, PackSearchFilter filter, AppSettings settings, ILauncherDownloads service,
        Action<InstallRequest, AppSettings, PackInfo> enqueue) : base(owner, project.Title, "", 900)
    {
        var browser = new CatalogReleaseBrowser(project, filter.GameVersion, showFilters: false);
        Actions.Children.Add(Button("取消", (_, _) => Close()));
        var install = Button("下一步  →", async (_, _) =>
        {
            if (!browser.CanInstall || browser.SelectedVersion is not { } selected) return;
            browser.SetBusy(true, "正在下载并识别整合包清单…");
            try
            {
                var pack = await service.PrepareAsync(settings, project, selected, lifetime.Token); lifetime.Token.ThrowIfCancellationRequested();
                DownloadAvailability.RequireSupported(pack);
                var confirmation = new PackConfirmationWindow(this, pack, settings);
                if (confirmation.ShowDialog() == true) { enqueue(confirmation.Request!, confirmation.Settings, pack); DialogResult = true; }
                else browser.ReleaseStatus.Text = "可继续选择版本，或再次确认安装。";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!closed) browser.ReleaseStatus.Text = ex.Message; }
            finally { if (!closed) browser.SetBusy(false); }
        }, true);
        install.IsEnabled = false; browser.SelectionChanged += (_, _) => install.IsEnabled = browser.CanInstall;
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
