using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using BlockDock.Core;

namespace Island.App;

public partial class CatalogReleaseBrowser : UserControl
{
    private readonly CatalogProject project;
    private readonly Func<CatalogVersion, bool> compatible;
    private IReadOnlyList<CatalogVersion> all = [];
    private string family = "";
    private bool updating, busy;
    private int filteredCount;
    public CatalogVersion? SelectedVersion => ReleaseList.SelectedItem as CatalogVersion;
    public bool CanInstall => !busy && SelectedVersion is { } version && compatible(version);
    public event EventHandler? SelectionChanged;

    public CatalogReleaseBrowser(CatalogProject project, string minecraft = "", string loader = "", Func<CatalogVersion, bool>? compatible = null, bool showFilters = true)
    {
        this.project = project; this.compatible = compatible ?? (_ => true);
        InitializeComponent(); DataContext = new PackSearchCard(project);
        ReleaseFilters.Visibility = showFilters ? Visibility.Visible : Visibility.Collapsed;
        ProjectArtwork.IconUrl = project.IconUrl; ProjectArtwork.Badge = project.Badge;
        ProjectWebsiteButton.Content = "转到 " + project.SourceLabel;
        ReleaseListPresentation.Apply(ReleaseList);
        ChannelFilter.ItemsSource = new[] { "全部通道", "正式版", "测试版", "预览版" }; ChannelFilter.SelectedIndex = 0;
        ExactGameVersion.Text = VersionFilter.Display(minecraft); VersionFilter.SetVersions(ExactGameVersion, []);
        LoaderFilter.ItemsSource = new[] { "全部加载器" }.Concat(project.Loaders).Concat(loader.Length > 0 ? [loader] : []).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        LoaderFilter.SelectedItem = LoaderFilter.Items.Cast<string>().FirstOrDefault(item => item.Equals(loader, StringComparison.OrdinalIgnoreCase)) ?? LoaderFilter.Items[0];
        ReleaseSearch.TextChanged += (_, _) => Filter();
        ExactGameVersion.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => { if (!updating) { family = ""; UpdateChipSelection(); Filter(); } }));
        LoaderFilter.SelectionChanged += (_, _) => Filter(); ChannelFilter.SelectionChanged += (_, _) => Filter();
        ReleaseList.SelectionChanged += (_, _) => { UpdateSelectionStatus(); SelectionChanged?.Invoke(this, EventArgs.Empty); };
        Loaded += (_, _) => { if (showFilters) ReleaseSearch.Focus(); else VersionChips.Children.OfType<RadioButton>().FirstOrDefault()?.Focus(); };
        SetBusy(true, "正在读取发布列表…");
    }

    public void SetVersions(IReadOnlyList<CatalogVersion> versions)
    {
        updating = true;
        try
        {
            all = versions;
            VersionFilter.SetVersions(ExactGameVersion, all.SelectMany(item => item.GameVersions).Distinct().OrderByDescending(VersionKey));
            var selectedLoader = LoaderFilter.SelectedItem as string;
            LoaderFilter.ItemsSource = new[] { "全部加载器" }.Concat(all.SelectMany(item => item.Loaders).Where(value => value.Length > 0).Select(LoaderName).Distinct(StringComparer.OrdinalIgnoreCase).Order()).ToArray();
            LoaderFilter.SelectedItem = LoaderFilter.Items.Cast<string>().FirstOrDefault(value => value.Equals(selectedLoader, StringComparison.OrdinalIgnoreCase)) ?? LoaderFilter.Items[0];
            BuildChips();
        }
        finally { updating = false; }
        SetBusy(false); Filter();
    }
    public void SetBusy(bool value, string? message = null)
    {
        busy = value; ReleaseFilters.IsEnabled = VersionChips.IsEnabled = ReleaseList.IsEnabled = !value;
        if (message is not null) { ReleaseStatus.Text = message; ReleaseEmpty.Text = message; ReleaseEmpty.Visibility = ReleaseList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed; }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    public void ShowError(string message)
    {
        SetBusy(false, message); ReleaseStatus.ToolTip = message;
        ReleaseEmpty.Text = "暂时无法读取版本，请关闭后重新打开。"; ReleaseEmpty.Visibility = Visibility.Visible;
    }
    private void BuildChips()
    {
        VersionChips.Children.Clear();
        foreach (var name in new[] { "全部" }.Concat(all.SelectMany(item => item.GameVersions).Select(Family).Distinct().OrderByDescending(VersionKey)))
        {
            var chip = new RadioButton { Content = name, Tag = name == "全部" ? "" : name, GroupName = "ReleaseFamilies", Style = (Style)FindResource("VersionChip") };
            chip.Checked += (_, _) =>
            {
                if (updating) return;
                family = (string)chip.Tag;
                updating = true; try { VersionFilter.SelectAll(ExactGameVersion); } finally { updating = false; }
                Filter();
            };
            VersionChips.Children.Add(chip);
        }
        UpdateChipSelection();
    }
    private void UpdateChipSelection()
    {
        var previous = updating; updating = true;
        try
        {
            var active = family.Length > 0 ? family : VersionFilter.Value(ExactGameVersion.Text) is { Length: > 0 } exact ? Family(exact) : "";
            foreach (var chip in VersionChips.Children.OfType<RadioButton>()) chip.IsChecked = (string)chip.Tag == active;
        }
        finally { updating = previous; }
    }
    private void Filter()
    {
        if (updating || busy) return;
        var previous = SelectedVersion?.Id;
        var exact = VersionFilter.Value(ExactGameVersion.Text);
        var loader = LoaderFilter.SelectedIndex > 0 ? LoaderFilter.SelectedItem as string : null;
        var visible = all.Where(version => (version.Name + " " + version.Id).Contains(ReleaseSearch.Text.Trim(), StringComparison.OrdinalIgnoreCase)
            && (exact.Length == 0 || version.GameVersions.Contains(exact, StringComparer.OrdinalIgnoreCase))
            && (family.Length == 0 || version.GameVersions.Any(value => Family(value) == family))
            && (loader is null || version.Loaders.Any(value => LoaderName(value).Equals(loader, StringComparison.OrdinalIgnoreCase)))
            && (ChannelFilter.SelectedIndex <= 0 || version.Channel == ChannelFilter.SelectedItem as string)).OrderByDescending(version => version.Published).ToArray();
        filteredCount = visible.Length;
        var view = new ListCollectionView(visible);
        view.GroupDescriptions.Add(new ReleaseGroups(version =>
        {
            var loaders = loader is null ? version.Loaders.Where(value => value.Length > 0).Select(LoaderName).Distinct() : [loader];
            if (!loaders.Any()) loaders = ["Minecraft"];
            var games = version.GameVersions.Where(value => exact.Length == 0 || value.Equals(exact, StringComparison.OrdinalIgnoreCase))
                .Distinct().Where(value => family.Length == 0 || Family(value) == family).OrderByDescending(VersionKey);
            if (!games.Any()) return loaders.Select(value => value + " · 其他版本").ToList();
            return loaders.SelectMany(name => games.Select(value => name + " " + value)).ToList();
        }));
        ReleaseList.ItemsSource = view;
        ReleaseList.SelectedItem = visible.FirstOrDefault(version => version.Id == previous) ?? visible.FirstOrDefault(compatible) ?? visible.FirstOrDefault();
        ReleaseEmpty.Text = "没有匹配的版本，试试“全部”或清空筛选。";
        ReleaseEmpty.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ReleaseStatus.ToolTip = null; UpdateSelectionStatus();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => ScrollToStart(ReleaseList)));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    private static bool ScrollToStart(DependencyObject parent)
    {
        if (parent is ScrollViewer scroll) { scroll.ScrollToTop(); return true; }
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            if (ScrollToStart(VisualTreeHelper.GetChild(parent, index))) return true;
        return false;
    }
    private void UpdateSelectionStatus()
    {
        if (busy) return;
        ReleaseStatus.Text = SelectedVersion is { } selected && !compatible(selected)
            ? "所选版本不兼容目标实例，请选择匹配的 Minecraft 版本与加载器。"
            : $"显示 {filteredCount} / 共 {all.Count} 个发布版本";
    }
    internal static string Family(string version) => Regex.Match(version, @"^\d+\.\d+").Value is { Length: > 0 } value ? value : "快照 / 其他";
    private static Version VersionKey(string value) => Version.TryParse(value, out var parsed) ? parsed : new Version(0, 0);
    private static string LoaderName(string value) => value.ToLowerInvariant() switch { "fabric" => "Fabric", "forge" => "Forge", "neoforge" => "NeoForge", "quilt" => "Quilt", "iris" => "Iris", "optifine" => "OptiFine", _ => value };
    private void OpenWebsite(object sender, RoutedEventArgs e) => OpenExternal(project.Website);
    private void OpenWiki(object sender, RoutedEventArgs e) => OpenExternal("https://search.mcmod.cn/s?key=" + Uri.EscapeDataString(project.Title));
    private void OpenExternal(string address)
    {
        try
        {
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != "https") throw new InvalidOperationException("项目链接不可用。");
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) { ReleaseStatus.Text = "无法打开页面：" + ex.Message; }
    }
    private void CopyName(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(project.Title); ReleaseStatus.Text = "已复制项目名称。"; }
        catch (Exception ex) { ReleaseStatus.Text = "无法复制名称：" + ex.Message; }
    }
    private sealed class ReleaseGroups(Func<CatalogVersion, List<string>> names) : GroupDescription
    {
        public override object GroupNameFromItem(object item, int level, CultureInfo culture) => names((CatalogVersion)item);
    }
}
