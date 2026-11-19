using System.Text.Json;
using Island.Core;

internal static class JvmAndMemoryTests
{
    public static void Run(string root, Action<bool, string> check)
    {
        check(MemoryAllocator.Recommend(new(32768, 20000), 0) == 2048 && MemoryAllocator.Recommend(new(32768, 20000), 300) == 8192, "Automatic memory scales from vanilla to large modpacks");
        check(MemoryAllocator.Recommend(new(8192, 3000), 300) == 1792, "Automatic memory respects current available RAM and system reserve");
        check(MemoryAllocator.Recommend(new(4096, 4000), 300) <= 4096 * 3 / 5 && MemoryAllocator.Recommend(new(32768, 20000), 300, false) == 1024, "Automatic memory caps total RAM share and 32-bit Java");
        check(MemoryAllocator.Recommend(new(14000, 1741), 0) == 1024, "Memory reserve adapts to current free RAM instead of reserving already-used system memory twice");
        try { MemoryAllocator.Recommend(new(8192, 700), 0); throw new Exception("Low memory accepted"); }
        catch (InvalidOperationException) { check(true, "Insufficient free memory asks for manual allocation instead of overcommitting"); }
        var system = MemoryAllocator.ReadSystem();
        check(system.TotalMb > 0 && system.AvailableMb > 0 && system.AvailableMb <= system.TotalMb, "Read real Windows physical memory snapshot");
        check(JvmArguments.Parse("-Dpath=\"F:\\中文 空格\\游戏\"\n-XX:+UseZGC '-Dtitle=hello world'").SequenceEqual(new[] { "-Dpath=F:\\中文 空格\\游戏", "-XX:+UseZGC", "-Dtitle=hello world" }), "Custom JVM parser preserves quoted Unicode paths, spaces and newlines");
        check(JvmArguments.Parse("\"-Dpath=F:\\game\\\\\" -Dquote=\\\"hello\\\"").SequenceEqual(new[] { "-Dpath=F:\\game\\", "-Dquote=\"hello\"" }), "Custom JVM parser handles trailing Windows path separators and escaped quotes");
        try { JvmArguments.Parse("-Dpath=\"broken"); throw new Exception("Unclosed quote accepted"); }
        catch (ArgumentException) { check(true, "Unclosed JVM argument quotes are rejected before launch"); }
        var state = new LauncherState { GameDirectory = root, AutoMemory = true, Jvm = new() { ExtraArguments = "-Dscope=global", OmitOptimizations = true } }; state.EnsureGameFolders();
        check(state.RuntimeFor(root, "A").AutoMemory && state.RuntimeFor(root, "A").Jvm.ExtraArguments == "-Dscope=global", "Unconfigured instances inherit global automatic memory and JVM options");
        var preferences = state.For("A"); preferences.AutoMemory = false; preferences.MemoryMb = 3072; preferences.Jvm = new() { ExtraArguments = "-Dscope=local", SkipCompatibilityCheck = true };
        check(!state.RuntimeFor(root, "A").AutoMemory && state.RuntimeFor(root, "A").MemoryMb == 3072 && state.RuntimeFor(root, "A").Jvm.SkipCompatibilityCheck, "Independent instance options override global memory and JVM settings");
        check(state.RuntimeFor(Path.Combine(root, "other"), "A").Jvm.ExtraArguments == "-Dscope=global", "Same-named versions in other folders retain global options");
        var store = new LauncherStore(Path.Combine(root, "jvm-settings")); store.Save(state);
        var restored = store.Load();
        check(restored.AutoMemory && restored.For("A").Jvm?.ExtraArguments == "-Dscope=local" && restored.For("A").AutoMemory == false, "Global and independent JVM and automatic-memory options survive saving");
        var legacyStore = new LauncherStore(Path.Combine(root, "legacy-memory")); Directory.CreateDirectory(legacyStore.DirectoryPath);
        File.WriteAllText(Path.Combine(legacyStore.DirectoryPath, "settings.json"), JsonSerializer.Serialize(new { GameDirectory = root, MemoryMb = 6144 }));
        check(!legacyStore.Load().AutoMemory && legacyStore.Load().MemoryMb == 6144 && new LauncherState().AutoMemory, "Upgrade preserves old manual memory while new configurations default to automatic");
        var legacyIndependent = new LauncherState { GameDirectory = root, MemoryMb = 5120, AutoMemory = false }; legacyIndependent.EnsureGameFolders(); legacyIndependent.For("Legacy").JavaPath = "old-java.exe";
        var oldJson = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(legacyIndependent))!.AsObject(); oldJson.Remove("AutoMemory"); oldJson.Remove("Jvm");
        File.WriteAllText(Path.Combine(legacyStore.DirectoryPath, "settings.json"), oldJson.ToJsonString());
        var migrated = legacyStore.Load();
        migrated.MemoryMb = 8192; migrated.Jvm.ExtraArguments = "-Dchanged=true";
        check(migrated.For("Legacy") is { JavaPath: "old-java.exe", MemoryMb: 5120, AutoMemory: false, Jvm: not null } && migrated.RuntimeFor(root, "Legacy").Jvm.ExtraArguments == "", "Legacy Java overrides migrate to independent snapshots without following later global changes");
        preferences.AutoMemory = null; preferences.MemoryMb = null; preferences.Jvm = null;
        check(!preferences.HasIndependentSettings && state.RuntimeFor(root, "A").Jvm.ExtraArguments == "-Dscope=global", "Clearing independent settings resumes global inheritance");
    }
}
