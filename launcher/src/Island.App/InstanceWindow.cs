using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BlockDock.Core;
using Island.Core;
using Microsoft.Win32;

namespace Island.App;

public sealed class InstanceWindow : FormWindow
{
    private readonly InstanceCard card;
    private readonly CancellationTokenSource lifetime = new();
    private readonly ComboBox java = new() { Name = "InstanceJavaBox", IsEditable = true, IsTextSearchEnabled = false, DisplayMemberPath = "Display", MinWidth = 160 };
    private readonly TextBlock javaStatus = new() { Name = "InstanceJavaStatus", Text = "可沿用全局设置、自动匹配，或为这个版本单独选择 Java。", TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 7, 0, 0) };
    private readonly TextBlock modSummary = new() { Margin = new Thickness(0, 0, 0, 10) };
    private readonly TextBlock status = new() { Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap };
    public InstanceWindow(Window owner, InstanceCard instance, LauncherState state, Action save) : base(owner, instance.DisplayName, instance.VersionLine + " · " + instance.Loader + $" · 累计游玩 {instance.Preferences.PlayedMinutes:0.0} 分钟\n" + instance.Runtime.Reason + "\n" + instance.RuntimeDirectory, 700)
    {
        card = instance;
        var runtimes = owner is MainWindow main ? main.JavaRuntimes : new JavaRuntimeCache();
        var effective = state.RuntimeFor(card.GameDirectory, card.Id);
        Closed += (_, _) => lifetime.Cancel();
        var folders = new WrapPanel();
        foreach (var (label, relative) in new[] { ("游戏目录", ""), ("存档", "saves"), ("资源包", "resourcepacks"), ("光影", "shaderpacks") })
        {
            var button = Button(label + " ↗", (_, _) => Try(() =>
            {
                var path = relative.Length == 0 ? card.RuntimeDirectory : SafePaths.Under(card.RuntimeDirectory, relative);
                Directory.CreateDirectory(path); MainWindow.OpenPath(path);
            }));
            button.Margin = new Thickness(0, 0, 8, 8); folders.Children.Add(button);
        }
        Body.Children.Add(folders);
        Label("基本设置");
        var iconKey = card.Preferences.IconKey; var iconPng = card.Preferences.IconPng;
        var iconPreview = new Image { Name = "InstanceIconPreview", Source = card.Icon, Width=32,Height=32,Stretch=Stretch.Uniform,Margin=new Thickness(12,0,12,0) };
        RenderOptions.SetBitmapScalingMode(iconPreview, BitmapScalingMode.NearestNeighbor);
        var iconRow=new Grid { Margin=new Thickness(16,10,10,10),MinHeight=36 };
        iconRow.ColumnDefinitions.Add(new ColumnDefinition());
        for(var column=0;column<3;column++) iconRow.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        iconRow.Children.Add(new TextBlock { Text="游戏图标",VerticalAlignment=VerticalAlignment.Center }); Grid.SetColumn(iconPreview,1); iconRow.Children.Add(iconPreview);
        var editIcon=Button("\uE70F",(_,_) => Try(() =>
        {
            var chooser=new InstanceIconWindow(this,iconKey,iconPng);
            if(chooser.ShowDialog()!=true) return;
            iconKey=chooser.IconKey; iconPng=chooser.IconPng;
            iconPreview.Source=iconPng is not null ? InstanceIcons.Decode(iconPng) : InstanceIcons.Contains(iconKey) ? InstanceIcons.Builtin(iconKey) : new InstanceCard(card.Instance,new InstancePreferences()).Icon;
        }));
        editIcon.Name="EditInstanceIconButton"; editIcon.FontFamily=new FontFamily("Segoe MDL2 Assets"); editIcon.ToolTip="更换游戏图标";
        System.Windows.Automation.AutomationProperties.SetName(editIcon,"更换游戏图标"); Grid.SetColumn(editIcon,2); iconRow.Children.Add(editIcon);
        var resetIcon=Button("\uE777",(_,_) =>
        {
            iconKey=""; iconPng=null;
            iconPreview.Source=new InstanceCard(card.Instance,new InstancePreferences()).Icon;
        });
        resetIcon.Name="ResetInstanceIconButton"; resetIcon.FontFamily=new FontFamily("Segoe MDL2 Assets"); resetIcon.ToolTip="恢复默认图标";
        System.Windows.Automation.AutomationProperties.SetName(resetIcon,"恢复默认图标"); Grid.SetColumn(resetIcon,3); iconRow.Children.Add(resetIcon);
        var iconSurface=new Border { Child=iconRow,CornerRadius=new CornerRadius(9),BorderThickness=new Thickness(1),Margin=new Thickness(0,0,0,8) };
        iconSurface.SetResourceReference(Border.BackgroundProperty,"Surface"); iconSurface.SetResourceReference(Border.BorderBrushProperty,"Line"); Body.Children.Add(iconSurface);
        var fields = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        fields.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel();
        left.Children.Add(new TextBlock { Text = "显示名称（不改变目录名）", Margin = new Thickness(0, 0, 0, 8) });
        var name = new TextBox { Text = card.DisplayName }; left.Children.Add(name); fields.Children.Add(left);
        Body.Children.Add(fields);
        var independent = new CheckBox { Name = "IndependentRuntimeBox", Content = "使用独立运行设置（关闭时沿用全局设置）", IsChecked = card.Preferences.HasIndependentSettings, Margin = new Thickness(0, 16, 0, 12) }; Body.Children.Add(independent);
        var runtimePanel = new StackPanel(); Body.Children.Add(runtimePanel);
        var settingsGroup = new Border { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), ClipToBounds = true };
        settingsGroup.SetResourceReference(Border.BackgroundProperty, "Surface"); settingsGroup.SetResourceReference(Border.BorderBrushProperty, "Line");
        var rows = new StackPanel(); settingsGroup.Child = rows; runtimePanel.Children.Add(settingsGroup);
        var javaEditor = new StackPanel();
        var javaSetting = new InstanceSettingRow("游戏 Java", javaEditor) { Name = "InstanceJavaSetting" }; rows.Children.Add(javaSetting);
        var javaRow = new Grid(); javaRow.ColumnDefinitions.Add(new ColumnDefinition());
        javaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); javaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var choices = new[] { new JavaChoice(null, "沿用全局设置"), new JavaChoice("", "自动选择（按此版本要求）") };
        void SetJavaChoices(IReadOnlyList<JavaInstallation> found, string? value)
        {
            var all = choices.Concat(found.Select(runtime => new JavaChoice(runtime.Path, runtime.Display))).ToArray(); java.ItemsSource = all;
            java.SelectedItem = all.FirstOrDefault(choice => string.Equals(choice.Path, value, StringComparison.OrdinalIgnoreCase));
            if (java.SelectedItem is null) java.Text = value ?? "";
        }
        SetJavaChoices(runtimes.Snapshot().Items, card.Preferences.JavaPath);
        java.ToolTip = "全局 Java：" + (string.IsNullOrWhiteSpace(state.JavaPath) ? "自动选择" : state.JavaPath); javaRow.Children.Add(java);
        var browseJava = Button("浏览…", (_, _) =>
        {
            var file = new OpenFileDialog { Title = "为此版本选择 Java", Filter = "Java 程序|java.exe;javaw.exe" };
            if (file.ShowDialog(this) == true) { java.SelectedItem = null; java.Text = file.FileName; }
        }); Grid.SetColumn(browseJava, 1); javaRow.Children.Add(browseJava);
        var detectingJava = false;
        async Task RefreshJava(bool force, Button button)
        {
            if (detectingJava) return;
            detectingJava = true;
            button.IsEnabled = false; javaStatus.Text = "正在载入 Java 检测记录…";
            var saved = card.Preferences.JavaPath;
            var missingSelection = !string.IsNullOrWhiteSpace(saved) && !File.Exists(saved);
            var snapshot = runtimes.Snapshot();
            var missing = missingSelection ? saved : snapshot.MissingPaths.FirstOrDefault();
            if (missing is not null) javaStatus.Text = "未找到该路径的 Java：" + missing + "\n正在自动重新检测…";
            try
            {
                var value = ReadJava();
                var found = await runtimes.GetAvailableAsync(card.GameDirectory, value ?? state.JavaPath, lifetime.Token, force || missingSelection);
                lifetime.Token.ThrowIfCancellationRequested(); value = ReadJava();
                if (force) card.Preferences.AutoSelectedJavaPath = "";
                if (missingSelection && card.Preferences.JavaPath == saved)
                {
                    card.Preferences.JavaPath = "";
                    try { save(); } catch { card.Preferences.JavaPath = saved; throw; }
                    if (string.Equals(value, saved, StringComparison.OrdinalIgnoreCase)) value = "";
                }
                else if (force) save();
                SetJavaChoices(found, value);
                javaStatus.Text = (missing is null ? "" : "未找到该路径的 Java：" + missing + "\n已自动重新检测。" + (missingSelection ? "此实例已改为自动选择。" : "") + "\n")
                    + (found.Count == 0 ? "未检测到 Java。请安装 64 位 Java 后重新检测，或手动选择 java.exe。" : $"已载入 {found.Count} 个 Java 环境。启动时会校验此版本要求的 Java 版本和位数。")
                    + (runtimes.SaveWarning is null ? "" : "\n" + runtimes.SaveWarning);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { javaStatus.Text = ex.Message; }
            finally { detectingJava = false; button.IsEnabled = true; }
        }
        var detectJava = Button("检测", async (sender, _) => await RefreshJava(true, (Button)sender));
        Loaded += async (_, _) => await RefreshJava(false, detectJava);
        Grid.SetColumn(detectJava, 2); javaRow.Children.Add(detectJava);
        javaEditor.Children.Add(javaRow); javaEditor.Children.Add(javaStatus);
        var memory = new MemoryAllocationEditor { Name = "InstanceMemoryEditor" };
        memory.Load(effective.AutoMemory, effective.MemoryMb, card.RuntimeDirectory);
        var memorySetting = new InstanceSettingRow("游戏内存", memory) { Name = "InstanceMemorySetting" }; rows.Children.Add(memorySetting);
        var jvmEditor = new JvmSettingsEditor(); jvmEditor.Load(effective.Jvm);
        var advancedSetting = new InstanceSettingRow("高级选项", jvmEditor, "虚拟机与游戏参数、启动前命令、兼容性与显卡设置") { Name = "InstanceAdvancedSetting", Summary = "按需调整" }; rows.Children.Add(advancedSetting);
        void UpdateSummaries()
        {
            var inherited = independent.IsChecked != true;
            var path = inherited ? state.JavaPath : ReadJava();
            javaSetting.Summary = (inherited ? "全局 · " : "") + (path is null ? "沿用全局设置" : path.Length == 0 ? "自动选择 Java" : path);
            memorySetting.Summary = (inherited ? "全局 · " : "") + ((inherited ? state.AutoMemory : memory.Automatic) ? "自动分配内存" : $"{(inherited ? state.MemoryMb : memory.ManualMemoryMb) / 1024d:0.0} GB");
        }
        java.SelectionChanged += (_, _) => UpdateSummaries();
        java.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => UpdateSummaries()));
        memory.AutomaticOption.Checked += (_, _) => UpdateSummaries(); memory.CustomOption.Checked += (_, _) => UpdateSummaries();
        memory.MemorySlider.ValueChanged += (_, _) => UpdateSummaries();
        void UpdateRuntimeControls()
        {
            var enabled = independent.IsChecked == true;
            javaEditor.IsEnabled = memory.IsEnabled = jvmEditor.IsEnabled = enabled;
            UpdateSummaries();
        }
        independent.Checked += (_, _) => UpdateRuntimeControls(); independent.Unchecked += (_, _) => UpdateRuntimeControls();
        UpdateRuntimeControls();
        var favorite = new CheckBox { Name = "FavoriteBox", Content = "收藏这个世界", IsChecked = card.Preferences.Favorite, Margin = new Thickness(0, 13, 0, 8) }; Body.Children.Add(favorite);
        Label("模组管理");
        if (!card.Runtime.Isolated) Body.Children.Add(new TextBlock { Text = "当前模组保存在共享根目录，修改会影响使用同一目录的版本。", TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 0, 0, 8) });
        Body.Children.Add(modSummary);
        var manageMods = Button("管理模组", (_, _) => Try(() => { new ModManagerWindow(this, card).ShowDialog(); ReloadMods(); }));
        manageMods.HorizontalAlignment = HorizontalAlignment.Left; manageMods.Margin = new Thickness(0); Body.Children.Add(manageMods); Body.Children.Add(status);
        if (owner is MainWindow launcher)
        {
            var deletion = new StackPanel { Margin = new Thickness(0, 22, 0, 0) };
            deletion.Children.Add(new TextBlock { Text = "删除实例", FontSize = 16, FontWeight = FontWeights.SemiBold });
            deletion.Children.Add(new TextBlock { Text = "删除此版本目录；共享根目录与其他实例保留。", FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 10) });
            var delete = Button("删除实例…", (_, _) => Try(() => { if (launcher.DeleteInstance(card, this)) Close(); }));
            delete.Name = "DeleteInstanceButton"; delete.HorizontalAlignment = HorizontalAlignment.Left; delete.Margin = new Thickness(0);
            deletion.Children.Add(delete); Body.Children.Add(deletion);
        }
        Actions.Children.Add(Button("关闭", (_, _) => Close()));
        Actions.Children.Add(Button("保存设置", (_, _) => Try(() =>
        {
            var display = name.Text.Trim(); if (display.Length is 0 or > 100) throw new ArgumentException("显示名称需为 1–100 个字符。");
            int? value = null; string? javaPath = null; JvmSettings? jvm = null; bool? automatic = null;
            if (independent.IsChecked == true)
            {
                value = memory.ManualMemoryMb;
                javaPath = ReadJava(); automatic = memory.Automatic; jvm = jvmEditor.Read();
                if (!string.IsNullOrEmpty(javaPath) && !File.Exists(javaPath)) throw new ArgumentException("Java 路径不存在。请重新选择，或选择自动匹配 / 沿用全局设置。");
                if (automatic != true && jvm.InitialMemoryMb > value) throw new ArgumentException("初始堆内存不能大于最大游戏内存。");
            }
            card.Preferences.IconKey=iconKey; card.Preferences.IconPng=iconPng;
            card.Preferences.DisplayName = display; card.Preferences.Favorite = favorite.IsChecked == true; card.Preferences.MemoryMb = value; card.Preferences.JavaPath = javaPath;
            card.Preferences.AutoMemory = automatic; card.Preferences.Jvm = jvm;
            save(); Close();
        }), true)); Body.Children.Add(Actions);
        KeepActionsVisible();
        ReloadMods();
    }
    private void ReloadMods()
    {
        var items = ModFiles.List(card.RuntimeDirectory);
        modSummary.Text = items.Count == 0 ? "此实例暂无模组。可在模组管理中添加、搜索和批量启用 / 停用。" : $"共 {items.Count} 个模组 · {items.Count(m => m.Enabled)} 个已启用";
    }
    private void Try(Action action) { try { action(); } catch (Exception ex) { status.Text = ex.Message; } }
    private string? ReadJava() => java.SelectedItem is JavaChoice choice && (java.Text == choice.Display || java.Text == choice.Path) ? choice.Path : java.Text.Trim();
    private sealed record JavaChoice(string? Path, string Display);
}
