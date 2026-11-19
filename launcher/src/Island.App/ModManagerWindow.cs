using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Shell;
using BlockDock.Core;
using Island.Core;
using Microsoft.Win32;

namespace Island.App;

public sealed class ModManagerWindow : FormWindow
{
    private readonly string directory;
    private readonly ListView mods = new() { Name = "ModsList", SelectionMode = SelectionMode.Extended };
    private readonly TextBox search = new() { Name = "ModSearchBox", ToolTip = "搜索模组文件名", Padding = new Thickness(12, 8, 12, 8) };
    private readonly ComboBox filter = new() { Name = "ModFilterBox", ItemsSource = new[] { "全部模组", "已启用", "已停用" }, SelectedIndex = 0, MinWidth = 115 };
    private readonly TextBlock summary = new() { Name = "ModSummary", Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBlock status = new() { Name = "ModStatus", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly GridViewColumn fileColumn;
    private IReadOnlyList<ModFile> all = [];
    public ModManagerWindow(Window owner, InstanceCard card) : base(owner, "模组管理", card.DisplayName + " · " + (card.Runtime.Isolated ? "独立目录" : "共享目录") + "\n" + card.RuntimeDirectory, 920)
    {
        directory = card.RuntimeDirectory;
        Body.Children.Add(new TextBlock { Text = DownloadAvailability.CurseForgeNotice + " 本地 JAR 的添加与管理可正常使用；Modrinth 在线下载请前往「下载中心 → Mod」。", Style = (Style)FindResource("Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        SizeToContent = SizeToContent.Manual; Height = 690; MinWidth = 740; MinHeight = 500; MaxHeight = SystemParameters.WorkArea.Height;
        ResizeMode = ResizeMode.CanResize; WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 44, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0) });
        var page = new Grid { Margin = new Thickness(28, 8, 28, 10) };
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto }) page.RowDefinitions.Add(new RowDefinition { Height = height });
        var heading = new StackPanel(); foreach (var child in Body.Children.Cast<UIElement>().ToArray()) { Body.Children.Remove(child); heading.Children.Add(child); }
        if (!card.Runtime.Isolated) heading.Children.Add(new TextBlock { Text = "这里的模组与使用相同根目录的版本共享。", Margin = new Thickness(0, 0, 0, 12) });
        page.Children.Add(heading);
        var toolbar = new Grid { Margin = new Thickness(0, 0, 0, 14) }; toolbar.ColumnDefinitions.Add(new ColumnDefinition()); toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var searchPanel = new Grid { Margin = new Thickness(0, 0, 12, 0) }; searchPanel.Children.Add(search);
        var placeholder = new TextBlock { Text = "搜索模组文件名…", IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(13, 0, 0, 0) }; placeholder.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); searchPanel.Children.Add(placeholder);
        toolbar.Children.Add(searchPanel); Grid.SetColumn(filter, 1); toolbar.Children.Add(filter); Grid.SetRow(toolbar, 1); page.Children.Add(toolbar);
        var view = new GridView { AllowsColumnReorder = false };
        var fileCell = new FrameworkElementFactory(typeof(TextBlock)); fileCell.SetBinding(TextBlock.TextProperty, new Binding("Name")); fileCell.SetBinding(ToolTipProperty, new Binding("Path")); fileCell.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); fileCell.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap); fileCell.SetValue(MarginProperty, new Thickness(8, 0, 8, 0));
        fileColumn = new GridViewColumn { Header = "模组文件", CellTemplate = new DataTemplate { VisualTree = fileCell }, Width = 500 }; view.Columns.Add(fileColumn);
        view.Columns.Add(new GridViewColumn { Header = "大小", DisplayMemberBinding = new Binding("SizeLabel"), Width = 92 }); view.Columns.Add(new GridViewColumn { Header = "状态", DisplayMemberBinding = new Binding("Status"), Width = 100 }); mods.View = view;
        ScrollViewer.SetHorizontalScrollBarVisibility(mods, ScrollBarVisibility.Disabled); VirtualizingPanel.SetIsVirtualizing(mods, true); VirtualizingPanel.SetVirtualizationMode(mods, VirtualizationMode.Recycling);
        Grid.SetRow(mods, 2); page.Children.Add(mods); Grid.SetRow(summary, 3); page.Children.Add(summary); Grid.SetRow(status, 4); page.Children.Add(status);
        search.TextChanged += (_, _) => { placeholder.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; ApplyFilter(); }; filter.SelectionChanged += (_, _) => ApplyFilter();
        mods.SizeChanged += (_, _) => FitColumns();
        Actions.HorizontalAlignment = HorizontalAlignment.Stretch; Actions.Orientation = Orientation.Vertical;
        var footer = new Grid(); footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tools = new StackPanel { Orientation = Orientation.Horizontal }; footer.Children.Add(tools);
        tools.Children.Add(Button("添加模组", (_, _) => Try(() =>
        {
            var picker = new OpenFileDialog { Title = "添加模组", Filter = "Java 模组|*.jar", Multiselect = true };
            if (picker.ShowDialog(this) == true) { ModFiles.Add(directory, picker.FileNames); Reload(); }
        })));
        tools.Children.Add(Button("启用所选", (_, _) => SetEnabled(true))); tools.Children.Add(Button("停用所选", (_, _) => SetEnabled(false)));
        tools.Children.Add(Button("打开模组目录", (_, _) => Try(() => { var path = SafePaths.Under(directory, "mods"); Directory.CreateDirectory(path); MainWindow.OpenPath(path); })));
        var close = Button("关闭", (_, _) => Close(), true); Grid.SetColumn(close, 1); footer.Children.Add(close); Actions.Children.Add(footer);
        KeepActionsVisible(); SetWorkspace(page); Loaded += (_, _) => FitColumns(); Reload();
    }
    private void FitColumns() => fileColumn.Width = Math.Max(260, mods.ActualWidth - 208);
    private void Reload() { all = ModFiles.List(directory); ApplyFilter(); }
    private void ApplyFilter()
    {
        var selected = mods.SelectedItems.Cast<ModFile>().Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visible = all.Where(file => file.Name.Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase) && (filter.SelectedIndex == 0 || file.Enabled == (filter.SelectedIndex == 1))).ToArray(); mods.ItemsSource = visible;
        foreach (var file in visible.Where(file => selected.Contains(file.Path))) mods.SelectedItems.Add(file);
        summary.Text = $"显示 {visible.Length} / 共 {all.Count} 个模组 · {all.Count(file => file.Enabled)} 个已启用 · 按 Ctrl / Shift 可多选";
    }
    private void SetEnabled(bool enabled)
    {
        var selected = mods.SelectedItems.Cast<ModFile>().ToArray();
        if (selected.Length == 0) { status.Text = "请先选择要操作的模组。"; return; }
        Try(() => { foreach (var file in selected.Where(file => file.Enabled != enabled)) ModFiles.Toggle(directory, file); status.Text = $"已{(enabled ? "启用" : "停用")} {selected.Length} 个所选模组。"; }); Reload();
    }
    private void Try(Action action) { try { action(); } catch (Exception ex) { status.Text = ex.Message; } }
}
