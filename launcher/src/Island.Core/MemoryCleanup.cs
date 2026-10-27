using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Island.Core;

// Stable exit codes shared with the short-lived elevated helper.
public enum MemoryCleanupOutcome
{
    Completed = 0, WorkingSetsOnly = 10, StandbyOnly = 11,
    PermissionDenied = 12, Failed = 13, AuthorizationDeclined = 14
}

public sealed record MemoryCleanupResult(MemoryCleanupOutcome Outcome, string? Detail = null)
{
    public string Message => Describe(true);
    public string Summary => Describe(false);

    public string Report(long? beforeMb, long? afterMb)
    {
        if (beforeMb is null || afterMb is null) return Summary + "\n无法读取本次释放量。";
        var readings = $"可用内存：{beforeMb / 1024d:0.00} → {afterMb / 1024d:0.00} GB";
        if (Outcome is not (MemoryCleanupOutcome.Completed or MemoryCleanupOutcome.WorkingSetsOnly or MemoryCleanupOutcome.StandbyOnly))
            return Summary + "\n" + readings;
        var released = Math.Max(0, afterMb.Value - beforeMb.Value);
        return Summary + $"\n本次释放：{released:N0} MB（系统可用内存净增）\n" + readings;
    }

    private string Describe(bool beforeLaunch)
    {
        var ending = beforeLaunch ? "，继续启动。" : "。";
        return (Outcome switch
        {
            MemoryCleanupOutcome.Completed => "已回收系统工作集并清理备用内存。",
            MemoryCleanupOutcome.WorkingSetsOnly => "已回收系统工作集；备用内存清理失败" + ending,
            MemoryCleanupOutcome.StandbyOnly => "已清理备用内存；系统工作集回收失败" + ending,
            MemoryCleanupOutcome.PermissionDenied => "未获得所需系统权限，已跳过清理" + ending,
            MemoryCleanupOutcome.AuthorizationDeclined => "已取消管理员授权，跳过清理" + ending,
            _ => "清理未完成，已跳过" + ending
        }) + (string.IsNullOrEmpty(Detail) ? "" : " " + Detail);
    }
}

public static class MemoryCleanup
{
    // Only the dedicated helper calls this. Its token changes end with that process.
    public static MemoryCleanupResult RunNative(CancellationToken token = default) => Run(new WindowsMemoryCleanupNative(), token);

    internal static MemoryCleanupResult Run(IMemoryCleanupNative native, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!native.EnablePrivilege()) return new(MemoryCleanupOutcome.PermissionDenied);
        // SYSTEM_MEMORY_LIST_COMMAND is a 32-bit enum even in the x64 process.
        var workingSets = native.Execute(2); // MemoryEmptyWorkingSets
        token.ThrowIfCancellationRequested();
        var standby = native.Execute(4); // MemoryPurgeStandbyList
        token.ThrowIfCancellationRequested();
        var outcome = (workingSets >= 0, standby >= 0) switch
        {
            (true, true) => MemoryCleanupOutcome.Completed,
            (true, false) => MemoryCleanupOutcome.WorkingSetsOnly,
            (false, true) => MemoryCleanupOutcome.StandbyOnly,
            _ => MemoryCleanupOutcome.Failed
        };
        return new(outcome, outcome == MemoryCleanupOutcome.Completed ? null : $"NTSTATUS 0x{workingSets:X8} / 0x{standby:X8}");
    }
}

internal interface IMemoryCleanupNative
{
    bool EnablePrivilege();
    int Execute(int command);
}

internal sealed class WindowsMemoryCleanupNative : IMemoryCleanupNative
{
    public bool EnablePrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x20 | 0x8, out var handle)) return false;
        using (handle)
        {
            if (!LookupPrivilegeValue(null, "SeProfileSingleProcessPrivilege", out var luid)) return false;
            var privileges = new TokenPrivileges { Count = 1, Luid = luid, Attributes = 2 };
            // TRUE can still mean ERROR_NOT_ALL_ASSIGNED: always inspect the last error.
            return AdjustTokenPrivileges(handle, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero)
                && Marshal.GetLastWin32Error() == 0;
        }
    }

    public int Execute(int command) => NtSetSystemInformation(80, ref command, sizeof(int));

    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] private struct TokenPrivileges { public uint Count; public Luid Luid; public uint Attributes; }
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out Luid luid);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(SafeAccessTokenHandle token, [MarshalAs(UnmanagedType.Bool)] bool disableAll,
        ref TokenPrivileges state, uint length, IntPtr previous, IntPtr returnedLength);
    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int informationClass, ref int information, int length);
}
