using System.Windows;
using System.Windows.Controls;
using Island.App;

internal sealed class InputLayoutPreview : FormWindow
{
    private readonly List<TextBox> samples = [];
    private InputLayoutPreview(Window owner) : base(owner, "输入框尺寸核对", "显示名称、内存、路径、Java、只读 / 禁用状态、多行输入及较大字号。", 820)
    {
        Pair("显示名称（13 号）", Box("DisplayName", "CTIIIIIIII · 中文名称"), "内存上限 / MB", Box("Memory", "4096"));
        var java = new ComboBox { IsEditable = true, IsTextSearchEnabled = false, ItemsSource = new[] { "自动选择（推荐）", "F:\\Java 环境\\bin\\java.exe" }, Text = "F:\\Java 环境\\bin\\java.exe" };
        Pair("游戏目录", Box("GameRoot", "F:\\含 空格的游戏目录\\.minecraft"), "Java（可手动输入）", java);
        Pair("20 号文字", Box("Font20", "中文 Test gjpq", 20), "29 号文字 / 设备码", Box("Font29", "ABCD-1234", 29));
        var readOnly = Box("ReadOnly", "只读文字 #7856A4"); readOnly.IsReadOnly = true;
        var disabled = Box("Disabled", "6144"); disabled.IsEnabled = false;
        Pair("只读输入", readOnly, "禁用输入", disabled);
        Label("多行 JVM 参数"); var multiline = Box("Multiline", "-Xmx4096M\n-Dexample=中文 参数\n-XX:+UseG1GC"); multiline.AcceptsReturn = true; multiline.TextWrapping = TextWrapping.Wrap; multiline.Height = 100; multiline.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; Body.Children.Add(multiline);
    }
    private TextBox Box(string name, string text, double font = 13)
    {
        var box = new TextBox { Name = name, Text = text, FontSize = font }; samples.Add(box); return box;
    }
    private void Pair(string firstLabel, Control first, string secondLabel, Control second)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 12) }; row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition());
        foreach (var (label, control, column) in new[] { (firstLabel, first, 0), (secondLabel, second, 1) })
        {
            var field = new StackPanel { Margin = new Thickness(column == 0 ? 0 : 8, 0, column == 0 ? 8 : 0, 0) };
            field.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 8) }); field.Children.Add(control); Grid.SetColumn(field, column); row.Children.Add(field);
        }
        Body.Children.Add(row);
    }
    public static void Save(Window owner, string directory)
    {
        var preview = new InputLayoutPreview(owner) { Left = -15000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
        try
        {
            preview.Show(); preview.UpdateLayout();
            App.SaveCapture((FrameworkElement)preview.Content, System.IO.Path.Combine(directory, "InputSizing.png"));
            foreach (var box in preview.samples)
            {
                var text = box.GetRectFromCharacterIndex(0, true);
                Console.WriteLine($"INPUT {box.Name}: font={box.FontSize}; height={box.ActualHeight:0.##}; textTop={text.Y:0.##}; textHeight={text.Height:0.##}; textLeft={text.X:0.##}");
            }
        }
        finally { preview.Close(); }
    }
}
