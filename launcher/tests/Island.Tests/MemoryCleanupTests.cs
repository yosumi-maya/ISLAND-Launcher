using System.Text.Json;
using Island.Core;

internal static class MemoryCleanupTests
{
    public static void Run(string root, Action<bool, string> check)
    {
        var store = new LauncherStore(Path.Combine(root, "memory-cleanup"));
        check(!store.Load().CleanMemoryBeforeLaunch, "Memory cleanup is opt-in for a fresh configuration");
        var state = store.Load(); state.CleanMemoryBeforeLaunch = true; store.Save(state);
        check(store.Load().CleanMemoryBeforeLaunch, "Memory cleanup enabled flag survives atomic settings persistence");
        state.CleanMemoryBeforeLaunch = false; store.Save(state);
        check(!store.Load().CleanMemoryBeforeLaunch, "Memory cleanup can be disabled again");
        File.WriteAllText(Path.Combine(store.DirectoryPath, "settings.json"), JsonSerializer.Serialize(new { GameDirectory = root, MemoryMb = 4096 }));
        check(!store.Load().CleanMemoryBeforeLaunch, "Upgrading an older configuration does not silently enable system cleanup");
        var native = new FakeNative(); var result = MemoryCleanup.Run(native, default);
        check(result.Outcome == MemoryCleanupOutcome.Completed && native.Commands.SequenceEqual(new[] { 2, 4 }), "Cleanup trims working sets before purging standby pages, exactly once each");
        native = new() { Allowed = false }; result = MemoryCleanup.Run(native, default);
        check(result.Outcome == MemoryCleanupOutcome.PermissionDenied && native.Commands.Count == 0, "Missing privilege prevents all system-memory commands");
        native = new() { WorkingSetStatus = unchecked((int)0xC0000022) }; result = MemoryCleanup.Run(native, default);
        check(result.Outcome == MemoryCleanupOutcome.StandbyOnly && native.Commands.Count == 2 && result.Message.Contains("C0000022"), "One failed operation still attempts the other and reports partial success");
        native = new() { StandbyStatus = -1 }; result = MemoryCleanup.Run(native, default);
        check(result.Outcome == MemoryCleanupOutcome.WorkingSetsOnly, "Failed standby purge is not reported as complete success");
        native = new() { WorkingSetStatus = -1, StandbyStatus = -1 }; result = MemoryCleanup.Run(native, default);
        check(result.Outcome == MemoryCleanupOutcome.Failed && result.Message.Contains("继续启动"), "Cleanup failure is recoverable with an honest status");
        check(Enum.GetValues<MemoryCleanupOutcome>().All(outcome => !new MemoryCleanupResult(outcome).Summary.Contains("继续启动"))
            && new MemoryCleanupResult(MemoryCleanupOutcome.Failed, "test detail").Summary.Contains("test detail"), "Manual cleanup feedback retains details without suggesting that a game will launch");
        check(new MemoryCleanupResult(MemoryCleanupOutcome.Completed).Report(1024, 2560).Contains("1,536 MB"), "Cleanup feedback reports the actual positive available-memory delta");
        check(new MemoryCleanupResult(MemoryCleanupOutcome.Completed).Report(2560, 2048).Contains("0 MB"), "Concurrent memory pressure never produces a negative or fabricated release amount");
        check(!new MemoryCleanupResult(MemoryCleanupOutcome.PermissionDenied).Report(1024, 2048).Contains("本次释放"), "Permission failure cannot claim successful memory release even if available RAM rises");
        check(new MemoryCleanupResult(MemoryCleanupOutcome.Completed).Report(null, null).Contains("无法读取"), "Unavailable system counters are not presented as a zero-byte measurement");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); native = new();
        try { MemoryCleanup.Run(native, cancellation.Token); throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { check(native.Commands.Count == 0 && !native.PrivilegeAttempted, "Pre-canceled cleanup never changes token privileges or memory"); }
        using var between = new CancellationTokenSource(); native = new() { AfterCommand = () => between.Cancel() };
        try { MemoryCleanup.Run(native, between.Token); throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { check(native.Commands.SequenceEqual(new[] { 2 }), "Canceling after working-set trim prevents standby purge"); }
    }

    private sealed class FakeNative : IMemoryCleanupNative
    {
        public bool Allowed = true, PrivilegeAttempted;
        public int WorkingSetStatus, StandbyStatus;
        public Action? AfterCommand;
        public List<int> Commands { get; } = [];
        public bool EnablePrivilege() { PrivilegeAttempted = true; return Allowed; }
        public int Execute(int command) { Commands.Add(command); AfterCommand?.Invoke(); return command == 2 ? WorkingSetStatus : StandbyStatus; }
    }
}
