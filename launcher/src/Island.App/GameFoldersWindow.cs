using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Island.Core;

namespace Island.App;

public sealed record GameFolderDraft(string Directory, string Name, int InstanceCount, bool Existing = false, VersionIsolation Isolation = VersionIsolation.Auto);

public sealed class GameFoldersWindow : FormWindow
{
    private readonly List<(GameFolderDraft Draft, TextBox Name, ComboBox Isolation)> rows = [];
    private readonly TextBlock error = new() { Foreground = new SolidColorBrush(Color.FromRgb(160, 91, 62)), Margin = new Thickness(0, 10, 0, 0) };
    public IReadOnlyList<GameFolderDraft> Result { get; private set; } = [];
    public GameFoldersWindow(Window owner, IEnumerable<GameFolderDraft> folders, bool importing) : base(owner,
        importing ? "为游戏目录命名" : "管理游戏目录",
        "为每个文件夹设置名称和版本隔离方式。自动检测会逐个版本判断使用根目录还是版本目录；切换设置不会移动游戏文件。", 660)
    {
        var list = new StackPanel();
        foreach (var draft in folders)
        {
            var body = new StackPanel();
            var name = new TextBox { Name = "FolderName", Text = draft.Name, MaxLength = 40 };
            body.Children.Add(name);
            body.Children.Add(new TextBlock { Text = draft.Directory, ToolTip = draft.Directory, FontSize = 11, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 9, 0, 5) });
            body.Children.Add(new TextBlock
            {
                Text = (draft.InstanceCount < 0 ? "目录暂不可访问" : draft.InstanceCount == 0 ? "暂无实例，可在这个目录创建游戏" : $"{draft.InstanceCount} 个实例") + (importing && draft.Existing ? "  ·  已添加，将更新名称" : ""),
                FontSize = 11, Foreground = (Brush)FindResource("Muted")
            });
            var isolationRow = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            isolationRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) }); isolationRow.ColumnDefinitions.Add(new ColumnDefinition());
            isolationRow.Children.Add(new TextBlock { Text = "版本隔离", VerticalAlignment = VerticalAlignment.Center });
            var isolation = new ComboBox
            {
                ItemsSource = new[] { new IsolationChoice(VersionIsolation.Auto, "自动检测（推荐）"), new IsolationChoice(VersionIsolation.Enabled, "开启 · 各版本独立存档、模组与配置"), new IsolationChoice(VersionIsolation.Disabled, "关闭 · 共享 .minecraft 根目录") },
                DisplayMemberPath = "Label", SelectedValuePath = "Value", SelectedValue = draft.Isolation, Tag = draft.Directory
            };
            Grid.SetColumn(isolation, 1); isolationRow.Children.Add(isolation); body.Children.Add(isolationRow);
            list.Children.Add(new MaterialBorder { Area = SurfaceArea.Panels, Style = (Style)FindResource("Card"), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 10), Child = body });
            rows.Add((draft, name, isolation));
        }
        Body.Children.Add(new ScrollViewer { Content = list, MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Body.Children.Add(error);
        var cancel = Button("取消", (_, _) => DialogResult = false); cancel.IsCancel = true; Actions.Children.Add(cancel);
        var save = Button(importing ? "保存并导入" : "保存设置", (_, _) =>
        {
            try { Result = rows.Select(row => row.Draft with { Name = GameFolder.ValidateName(row.Name.Text), Isolation = row.Isolation.SelectedValue is VersionIsolation mode ? mode : VersionIsolation.Auto }).ToArray(); DialogResult = true; }
            catch (ArgumentException ex) { error.Text = ex.Message; }
        }, true);
        save.IsDefault = true; Actions.Children.Add(save); Body.Children.Add(Actions);
    }
    private sealed record IsolationChoice(VersionIsolation Value, string Label);
}
