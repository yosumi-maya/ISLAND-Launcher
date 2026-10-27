using System.Runtime.InteropServices;
using System.Text;
using BlockDock.Core;

namespace Island.Core;

public sealed record RuntimeSettings(string JavaPath, int MemoryMb, bool AutoMemory, JvmSettings Jvm);

public enum GarbageCollectorMode { TunedG1, G1, PreferZ, PreferGenerationalZ, Custom }

public sealed record JvmSettings
{
    public bool OmitDefaultArguments { get; set; }
    public bool OmitOptimizations { get; set; }
    public bool SkipCompatibilityCheck { get; set; }
    public string ExtraArguments { get; set; } = "";
    public string ExtraGameArguments { get; set; } = "";
    public string PreLaunchCommand { get; set; } = "";
    public GarbageCollectorMode GarbageCollector { get; set; } = GarbageCollectorMode.TunedG1;
    public bool DisableJavaLaunchWrapper { get; set; }
    public bool DisableLwjglUnsafeAgent { get; set; }
    public bool HighPerformanceGpu { get; set; }
    public bool VerifyLoginCertificates { get; set; } = true;
    public int? InitialMemoryMb { get; set; }
    public int? PermGenMb { get; set; }
    public void Validate()
    {
        if (InitialMemoryMb is < 16 or > 65536) throw new ArgumentException("初始堆内存需为 16–65536 MB，或留空。");
        if (PermGenMb is < 16 or > 65536) throw new ArgumentException("永久代内存需为 16–65536 MB，或留空。");
        JvmArguments.Parse(ExtraArguments);
        JvmArguments.Parse(ExtraGameArguments, "游戏");
        if (!Enum.IsDefined(GarbageCollector)) throw new ArgumentException("请选择有效的内存管理方式。");
        if (PreLaunchCommand.Length > 8192 || PreLaunchCommand.IndexOfAny(['\0', '\r', '\n']) >= 0) throw new ArgumentException("启动前命令需为不超过 8192 字符的一行，多条命令可用 && 连接。");
    }
}

public static class JvmArguments
{
    public static IReadOnlyList<string> Parse(string? text, string category = "JVM")
    {
        text ??= "";
        if (text.Length > 16384 || text.Contains('\0')) throw new ArgumentException(category + " 参数过长或包含无效字符。");
        var result = new List<string>(); var token = new StringBuilder(); var quote = '\0'; var started = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\' && quote != '\'')
            {
                var start = i; while (i + 1 < text.Length && text[i + 1] == '\\') i++;
                var count = i - start + 1;
                if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    token.Append('\\', count / 2);
                    if (count % 2 == 1) { token.Append('"'); i++; }
                }
                else token.Append('\\', count);
                started = true;
            }
            else if (c is '"' or '\'' && (quote == '\0' || quote == c)) { quote = quote == '\0' ? c : '\0'; started = true; }
            else if (char.IsWhiteSpace(c) && quote == '\0') { if (started) { result.Add(token.ToString()); token.Clear(); started = false; } }
            else { token.Append(c); started = true; }
        }
        if (quote != '\0') throw new ArgumentException(category + " 参数的引号未闭合。");
        if (started) result.Add(token.ToString());
        return result;
    }
}

public static class LauncherJava
{
    public static Task<JavaInstallation> ResolveAsync(AppSettings settings, JavaRequirement requirement, JvmSettings options, CancellationToken token,
        string? previousAutomaticPath = null, JavaRuntimeCache? runtimes = null, Action<string>? unavailable = null)
        => (runtimes ?? JavaRuntimeCache.Transient).ResolveAsync(settings, requirement, options, token, previousAutomaticPath, unavailable);
}

public sealed record MemorySnapshot(long TotalMb, long AvailableMb);

public static class MemoryAllocator
{
    public static MemorySnapshot ReadSystem()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref status)) throw new InvalidOperationException("无法读取系统可用内存，请改用手动分配内存。");
        return new((long)(status.TotalPhysical / 1048576), (long)(status.AvailablePhysical / 1048576));
    }
    public static int Recommend(MemorySnapshot system, int modCount, bool is64Bit = true)
    {
        var desired = modCount switch { 0 => 2048, <= 100 => 4096, <= 250 => 6144, _ => 8192 };
        var reserve = Math.Clamp(Math.Min(system.TotalMb / 8, system.AvailableMb / 3), 512, 4096);
        var budget = Math.Min(system.TotalMb * 3 / 5, system.AvailableMb - reserve);
        if (!is64Bit) budget = Math.Min(budget, 1024);
        if (budget < 512) throw new InvalidOperationException("当前可用内存不足以自动分配，请关闭部分程序后重试，或手动设置内存。");
        return (int)(Math.Min(desired, budget) / 256 * 256);
    }
    public static int CountMods(string directory) => ModFiles.List(directory).Count(mod => mod.Enabled);
    public static string Preview(string? directory = null)
    {
        try
        {
            var system = ReadSystem(); var mods = directory is null ? 0 : CountMods(directory);
            var memory = Recommend(system, mods);
            return (directory is null ? "无模组基础建议 " : "当前建议 ") + $"{memory} MB · 可用 {system.AvailableMb / 1024d:0.0} / 总计 {system.TotalMb / 1024d:0.0} GB" + (directory is null ? "" : $" · {mods} 个启用模组") + "；启动时重新计算。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { return ex.Message; }
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
}
