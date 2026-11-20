using System.Diagnostics;
using System.Windows;
using Microsoft.VisualBasic.FileIO;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using BlockDock.Core;
using Island.Core;
using Microsoft.Win32;

namespace Island.App;

public sealed class ModManagerPage : FormPage
{
    private readonly string directory;
    private readonly bool sharedDirectory;
    private CancellationTokenSource? downloadLookup;
    private readonly ListView mods = new() { Name = "ModsList", SelectionMode = SelectionMode.Extended };
    private readonly TextBox search = new() { Name = "ModSearchBox", ToolTip = "搜索模组名称、版本或文件名", Padding = new Thickness(12, 8, 12, 8) };
    private readonly ComboBox filter = new() { Name = "ModFilterBox", ItemsSource = new[] { "全部模组", "已启用", "已停用" }, SelectedIndex = 0, MinWidth = 115 };
    private readonly TextBlock summary = new() { Name = "ModSummary", Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBlock status = new() { Name = "ModStatus", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button allMods;
    private readonly TextBlock empty = new() { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private CancellationTokenSource? metadataScan;
    private IReadOnlyList<ModListRow> all = [];
    public ModManagerPage(Window owner, InstanceCard card) : base(owner, "模组管理", card.DisplayName + " · " + (card.Runtime.Isolated ? "独立目录" : "共享目录") + "\n" + card.RuntimeDirectory, "本地模组")
    {
        directory = card.RuntimeDirectory;
        sharedDirectory = !card.Runtime.Isolated;
        var page = new Grid();
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
            page.RowDefinitions.Add(new RowDefinition { Height = height });
        if (!card.Runtime.Isolated)
        {
            var shared = new TextBlock { Text = "这里的模组与使用相同根目录的版本共享。", Margin = new Thickness(0, 0, 0, 12) };
            shared.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            page.Children.Add(shared);
        }
        var toolbar = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition());
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        allMods = Button("全部 (0)", (_, _) => { search.Clear(); filter.SelectedIndex = 0; }, primary: true);
        allMods.Margin = new Thickness(0, 0, 12, 0);
        allMods.ToolTip = "清除搜索与状态筛选，显示全部模组";
        toolbar.Children.Add(allMods);
        var searchPanel = new Grid { Margin = new Thickness(0, 0, 12, 0) };
        searchPanel.Children.Add(search);
        var placeholder = new TextBlock
        {
            Text = "搜索模组名称、版本或文件名…", IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(13, 0, 0, 0)
        };
        placeholder.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        searchPanel.Children.Add(placeholder);
        Grid.SetColumn(searchPanel, 1);
        toolbar.Children.Add(searchPanel);
        Grid.SetColumn(filter, 2);
        toolbar.Children.Add(filter);
        Grid.SetRow(toolbar, 1);
        page.Children.Add(toolbar);

        mods.ItemContainerStyle = (Style)FindResource("InstalledModRow");
        mods.ItemTemplate = (DataTemplate)FindResource("InstalledModTemplate");
        mods.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(ModRowAction));
        mods.Background = System.Windows.Media.Brushes.Transparent;
        mods.BorderThickness = new Thickness(0);
        ScrollViewer.SetHorizontalScrollBarVisibility(mods, ScrollBarVisibility.Disabled);
        ScrollViewer.SetCanContentScroll(mods, true);
        VirtualizingPanel.SetIsVirtualizing(mods, true);
        VirtualizingPanel.SetVirtualizationMode(mods, VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(mods, ScrollUnit.Pixel);
        Grid.SetRow(mods, 2);
        page.Children.Add(mods);
        empty.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        Grid.SetRow(empty, 2);
        page.Children.Add(empty);
        Grid.SetRow(summary, 3);
        page.Children.Add(summary);
        Grid.SetRow(status, 4);
        page.Children.Add(status);
        search.TextChanged += (_, _) =>
        {
            placeholder.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            ApplyFilter();
        };
        filter.SelectionChanged += (_, _) => ApplyFilter();
        Actions.HorizontalAlignment = HorizontalAlignment.Stretch; Actions.Orientation = Orientation.Vertical;
        var footer = new Grid(); footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tools = new StackPanel { Orientation = Orientation.Horizontal }; footer.Children.Add(tools);
        tools.Children.Add(Button("添加模组", (_, _) => Try(() =>
        {
            var picker = new OpenFileDialog { Title = "添加模组", Filter = "Java 模组|*.jar", Multiselect = true };
            if (picker.ShowDialog(HostWindow) == true) { ModFiles.Add(directory, picker.FileNames); Reload(); }
        })));
        tools.Children.Add(Button("启用所选", (_, _) => SetEnabled(true))); tools.Children.Add(Button("停用所选", (_, _) => SetEnabled(false)));
        tools.Children.Add(Button("打开模组目录", (_, _) => Try(() => { var path = SafePaths.Under(directory, "mods"); Directory.CreateDirectory(path); MainWindow.OpenPath(path); })));
        var close = Button("返回", (_, _) => Close(), true); Grid.SetColumn(close, 1); footer.Children.Add(close); Actions.Children.Add(footer);
        KeepActionsVisible();
        SetWorkspace(page);
        Closed += (_, _) => { metadataScan?.Cancel(); downloadLookup?.Cancel(); };
        Reload();
    }
    private void Reload()
    {
        var files = ModFiles.List(directory);
        metadataScan?.Cancel();
        all = files.Select(ModListRow.Fallback).ToArray();
        ApplyFilter();
        if (files.Count > 0) _ = ReadMetadataAsync(files);
    }
    private async Task ReadMetadataAsync(IReadOnlyList<ModFile> files)
    {
        using var scan = new CancellationTokenSource();
        metadataScan = scan;
        try
        {
            var rows = await Task.Run(() => files.Select(file =>
            {
                scan.Token.ThrowIfCancellationRequested();
                return ModListRow.Read(file);
            }).ToArray(), scan.Token);
            if (scan.IsCancellationRequested) return;
            all = rows.OrderBy(row => row.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
            ApplyFilter();
        }
        catch (OperationCanceledException) { }
        finally { if (metadataScan == scan) metadataScan = null; }
    }
    private void ApplyFilter()
    {
        var selected = mods.SelectedItems.Cast<ModListRow>().Select(row => row.File.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var query = search.Text.Trim();
        var visible = all.Where(row =>
            (filter.SelectedIndex == 0 || row.File.Enabled == (filter.SelectedIndex == 1))
            && (row.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || row.File.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || row.VersionLabel.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
        mods.ItemsSource = visible;
        foreach (var row in visible.Where(row => selected.Contains(row.File.Path))) mods.SelectedItems.Add(row);
        allMods.Content = $"全部 ({all.Count})";
        summary.Text = $"显示 {visible.Length} / 共 {all.Count} 个模组 · {all.Count(row => row.File.Enabled)} 个已启用 · 按 Ctrl / Shift 可多选";
        empty.Text = all.Count == 0 ? "此实例还没有模组" : "没有符合条件的模组";
        empty.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void SetEnabled(bool enabled)
    {
        var selected = mods.SelectedItems.Cast<ModListRow>().Select(row => row.File).Where(file => file.Enabled != enabled).ToArray();
        if (selected.Length == 0) { status.Text = enabled ? "请选择要启用的模组。" : "请选择要停用的模组。"; return; }
        Try(() =>
        {
            foreach (var file in selected) ModFiles.Toggle(directory, file);
            status.Text = $"已{(enabled ? "启用" : "停用")} {selected.Length} 个所选模组。";
        });
        Reload();
    }
    private async void ModRowAction(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button { Tag: string action, DataContext: ModListRow row }) return;
        e.Handled = true;
        switch (action)
        {
            case "download":
                await OpenDownloadPageAsync(row);
                break;
            case "locate":
                Try(() => LocateMod(row.File));
                break;
            case "toggle":
                Try(() =>
                {
                    ModFiles.Toggle(directory, row.File);
                    Reload();
                    status.Text = (row.File.Enabled ? "已停用 " : "已启用 ") + row.Title;
                });
                break;
            case "delete":
                Try(() => DeleteMod(row));
                break;
        }
    }

    private async Task OpenDownloadPageAsync(ModListRow row)
    {
        if (downloadLookup is not null) return;
        using var lookup = new CancellationTokenSource();
        downloadLookup = lookup;
        status.Text = $"正在查找「{row.Title}」的下载页…";
        try
        {
            // Hash only the clicked file. Release the file handle before any network request.
            var page = await Task.Run(() => ModDownloadPage.FindAsync(directory, row.File, lookup.Token), lookup.Token);
            lookup.Token.ThrowIfCancellationRequested();
            if (page is null)
            {
                status.Text = $"未找到「{row.Title}」的下载页。此文件可能没有公开的项目来源。";
                return;
            }

            MainWindow.OpenPath(page);
            status.Text = "";
        }
        catch (OperationCanceledException) when (lookup.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!lookup.IsCancellationRequested) status.Text = "无法打开模组下载页：" + ex.Message;
        }
        finally { downloadLookup = null; }
    }

    private void LocateMod(ModFile file)
    {
        var path = ModFiles.ExistingPath(directory, file);
        using var explorer = Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = "/select,\"" + path + "\"", UseShellExecute = false });
    }

    private void DeleteMod(ModListRow row)
    {
        var path = ModFiles.ExistingPath(directory, row.File);
        var sharedNote = sharedDirectory ? "\n\n其他使用此共享目录的实例也会受到影响。" : "";
        if (!MessageWindow.Confirm(HostWindow, "删除模组", $"将「{row.Title}」移动到回收站？\n\n{Path.GetFileName(path)}{sharedNote}", "删除模组"))
            return;
        path = ModFiles.ExistingPath(directory, row.File);
        try { FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException); }
        catch (OperationCanceledException) { return; }
        if (File.Exists(path)) throw new IOException("模组文件未删除。");
        Reload();
        status.Text = $"已将「{row.Title}」移到回收站。";
    }

    private void Try(Action action) { try { action(); } catch (Exception ex) { status.Text = ex.Message; } }
}
