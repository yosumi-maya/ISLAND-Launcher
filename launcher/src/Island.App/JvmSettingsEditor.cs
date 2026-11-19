using System.Windows;
using System.Windows.Controls;
using Island.Core;

namespace Island.App;

public sealed class JvmSettingsEditor : StackPanel
{
    private readonly CheckBox defaults = new() { Name = "OmitDefaultJvmBox", Content = "不添加默认的 Java 虚拟机参数", Margin = new Thickness(0, 0, 0, 12), ToolTip = "跳过启动器默认的编码参数；保留内存、类路径、原生库及游戏版本要求的参数。" };
    private readonly CheckBox optimizations = new() { Name = "OmitJvmOptimizationsBox", Content = "不自动添加 Java 虚拟机优化参数", Margin = new Thickness(0, 0, 0, 12), ToolTip = "关闭启动器的基础 G1 优化参数；游戏版本和自定义参数仍会保留。" };
    private readonly CheckBox compatibility = new() { Name = "SkipJavaCompatibilityBox", Content = "不检查 Java 虚拟机与游戏的兼容性", Margin = new Thickness(0, 0, 0, 14), ToolTip = "手动指定 Java 时跳过版本和位数校验；仍检查 Java 能否运行。自动选择仍按版本要求匹配。" };
    private readonly TextBox arguments = new() { Name = "ExtraJvmArgumentsBox", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 72, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBox initial = new() { Name = "InitialMemoryBox" };
    private readonly TextBox permGen = new() { Name = "PermGenMemoryBox" };
    private readonly TextBox gameArguments = new() { Name = "ExtraGameArgumentsBox" };
    private readonly TextBox preCommand = new() { Name = "PreLaunchCommandBox", MaxLength = 8192 };
    private readonly ComboBox collector = new() { Name = "GarbageCollectorBox", DisplayMemberPath = "Label", SelectedValuePath = "Mode" };
    private readonly CheckBox wrapper = new() { Name = "DisableJavaLaunchWrapperBox", Content = "禁用 Java Launch Wrapper", Margin = new Thickness(0, 8, 0, 0), ToolTip = "仅在 Windows UTF-8 系统配合 Java 8–18 时自动使用编码修复包装器；不影响 Forge 本身的启动类。" };
    private readonly CheckBox unsafeAgent = new() { Name = "DisableLwjglUnsafeAgentBox", Content = "禁用 LWJGL Unsafe Agent", Margin = new Thickness(0, 8, 0, 0), ToolTip = "默认仅对 Java 25+、LWJGL 3.4.0–3.4.1 且非 Forge 的启动配置添加此兼容组件。" };
    private readonly CheckBox highPerformance = new() { Name = "HighPerformanceGpuBox", Content = "使用高性能显卡", Margin = new Thickness(0, 8, 0, 0), ToolTip = "游戏运行期间请求所选 Java 使用 Windows 高性能图形偏好，正常退出后恢复。最终选择由系统和驱动决定；同一 Java 的其他进程也可能受到影响。" };
    private readonly CheckBox verifySsl = new() { Name = "VerifyLoginCertificatesBox", Content = "在正版登录时验证 SSL 证书", Margin = new Thickness(0, 8, 0, 0), ToolTip = "默认开启。关闭会降低登录连接的安全性，仅用于排查 Microsoft / Xbox / Minecraft 登录证书问题；不影响其他下载连接。" };
    public JvmSettingsEditor()
    {
        Children.Add(defaults); Children.Add(optimizations); Children.Add(compatibility);
        Children.Add(new TextBlock { Text = "Java 虚拟机参数", Margin = new Thickness(0, 0, 0, 8) }); Children.Add(arguments);
        Children.Add(Hint("参数用空格或换行分隔，含空格的路径请加引号。自定义参数优先，-Xmx / -Xms 会覆盖内存设置。"));
        Children.Add(new TextBlock { Text = "游戏参数", Margin = new Thickness(0, 14, 0, 8) }); Children.Add(gameArguments);
        Children.Add(new TextBlock { Text = "启动前执行命令", Margin = new Thickness(0, 12, 0, 8) }); Children.Add(preCommand);
        Children.Add(Hint("留空不执行。在游戏运行目录执行一行 cmd 命令，等待结束后启动游戏；非零退出码会停止本次启动。"));
        Children.Add(new TextBlock { Text = "内存管理", Margin = new Thickness(0, 12, 0, 8) });
        collector.ItemsSource = new[] { new { Label = "尽量使用 ZGC", Mode = GarbageCollectorMode.PreferZ }, new { Label = "尽量使用分代 ZGC", Mode = GarbageCollectorMode.PreferGenerationalZ },
            new { Label = "标准 G1GC", Mode = GarbageCollectorMode.G1 }, new { Label = "调优 G1GC", Mode = GarbageCollectorMode.TunedG1 }, new { Label = "不指定（可自定义）", Mode = GarbageCollectorMode.Custom } };
        collector.SelectedValue = GarbageCollectorMode.TunedG1; Children.Add(collector);
        Children.Add(Hint("按实际 Java 版本选择可用收集器；不支持 ZGC 时回退 G1。勾选不自动添加优化参数时不使用此预设，自定义 JVM 的 GC 参数优先。"));
        optimizations.Checked += (_, _) => collector.IsEnabled = false; optimizations.Unchecked += (_, _) => collector.IsEnabled = true;
        Children.Add(wrapper); Children.Add(unsafeAgent); Children.Add(highPerformance); Children.Add(verifySsl);
        var legacy = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
        var expand = new Button { Content = "兼容旧版本的内存选项  ▸", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(10, 6, 10, 6) };
        expand.Click += (_, _) => { legacy.Visibility = legacy.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; expand.Content = legacy.Visibility == Visibility.Visible ? "兼容旧版本的内存选项  ▾" : "兼容旧版本的内存选项  ▸"; };
        Children.Add(expand);
        var fields = new Grid(); fields.ColumnDefinitions.Add(new ColumnDefinition()); fields.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel { Margin = new Thickness(0, 0, 12, 0) }; left.Children.Add(new TextBlock { Text = "初始堆内存 / MB（Xms）", Margin = new Thickness(0, 0, 0, 8) }); left.Children.Add(initial); fields.Children.Add(left);
        var right = new StackPanel(); right.Children.Add(new TextBlock { Text = "永久代上限 / MB（MaxPermSize）", Margin = new Thickness(0, 0, 0, 8) }); right.Children.Add(permGen); Grid.SetColumn(right, 1); fields.Children.Add(right);
        legacy.Children.Add(fields); legacy.Children.Add(Hint("留空使用默认值。永久代选项仅用于 Java 7 及更早版本，Java 8 起不会添加。")); Children.Add(legacy);
    }
    private static TextBlock Hint(string text)
    {
        var hint = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 8, 0, 0) }; hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); return hint;
    }
    public void Load(JvmSettings settings)
    {
        defaults.IsChecked = settings.OmitDefaultArguments; optimizations.IsChecked = settings.OmitOptimizations; compatibility.IsChecked = settings.SkipCompatibilityCheck;
        arguments.Text = settings.ExtraArguments; initial.Text = settings.InitialMemoryMb?.ToString() ?? ""; permGen.Text = settings.PermGenMb?.ToString() ?? "";
        gameArguments.Text = settings.ExtraGameArguments; preCommand.Text = settings.PreLaunchCommand;
        collector.SelectedValue = Enum.IsDefined(settings.GarbageCollector) ? settings.GarbageCollector : GarbageCollectorMode.TunedG1; collector.IsEnabled = !settings.OmitOptimizations;
        wrapper.IsChecked = settings.DisableJavaLaunchWrapper; unsafeAgent.IsChecked = settings.DisableLwjglUnsafeAgent;
        highPerformance.IsChecked = settings.HighPerformanceGpu; verifySsl.IsChecked = settings.VerifyLoginCertificates;
    }
    public JvmSettings Read()
    {
        static int? Number(TextBox box, string label) => string.IsNullOrWhiteSpace(box.Text) ? null : int.TryParse(box.Text.Trim(), out var value) ? value : throw new ArgumentException(label + "需要填写 MB 整数或留空。");
        var settings = new JvmSettings { OmitDefaultArguments = defaults.IsChecked == true, OmitOptimizations = optimizations.IsChecked == true, SkipCompatibilityCheck = compatibility.IsChecked == true,
            ExtraArguments = arguments.Text.Trim(), InitialMemoryMb = Number(initial, "初始堆内存"), PermGenMb = Number(permGen, "永久代上限"),
            ExtraGameArguments = gameArguments.Text.Trim(), PreLaunchCommand = preCommand.Text.Trim(), GarbageCollector = collector.SelectedValue is GarbageCollectorMode mode ? mode : GarbageCollectorMode.TunedG1,
            DisableJavaLaunchWrapper = wrapper.IsChecked == true, DisableLwjglUnsafeAgent = unsafeAgent.IsChecked == true, HighPerformanceGpu = highPerformance.IsChecked == true, VerifyLoginCertificates = verifySsl.IsChecked == true };
        settings.Validate(); return settings;
    }
}
