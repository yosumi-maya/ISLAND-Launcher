using System.Diagnostics;
using System.Text.Json.Nodes;

internal static class JavaProbeFixture
{
    public static bool TryRun(string[] args)
    {
        var config = Path.Combine(AppContext.BaseDirectory, "java-fixture.json");
        if (!File.Exists(config)) return false;
        if (!args.SequenceEqual(new[] { "-XshowSettings:properties", "-version" }))
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "processor-args.json"), System.Text.Json.JsonSerializer.Serialize(args));
            return true;
        }
        var json = JsonNode.Parse(File.ReadAllText(config))!;
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "probe.pid"), Environment.ProcessId.ToString());
        if (json["mode"]?.GetValue<string>() == "slow") Thread.Sleep(30000);
        if (json["mode"]?.GetValue<string>() == "invalid") { Console.WriteLine("not a Java runtime"); return true; }
        Console.Error.WriteLine("Property settings:");
        Console.Error.WriteLine("    java.version = " + json["version"]!.GetValue<string>());
        Console.Error.WriteLine("    java.vendor = Fixture OpenJDK");
        Console.Error.WriteLine("    os.arch = " + json["arch"]!.GetValue<string>());
        Console.Error.WriteLine("    sun.arch.data.model = " + (json["arch"]!.GetValue<string>() == "x86" ? "32" : "64"));
        return true;
    }

    public static string Create(string root, string version = "21.0.8+9", string arch = "amd64", string mode = "valid")
    {
        var bin = Path.Combine(root, "bin"); Directory.CreateDirectory(bin);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "BlockDock.Tests.exe"), Path.Combine(bin, "java.exe"), true);
        foreach (var name in new[] { "BlockDock.Tests.dll", "BlockDock.Tests.deps.json", "BlockDock.Tests.runtimeconfig.json", "BlockDock.Core.dll" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, name), Path.Combine(bin, name), true);
        File.WriteAllText(Path.Combine(bin, "java-fixture.json"), new JsonObject { ["version"] = version, ["arch"] = arch, ["mode"] = mode }.ToJsonString());
        return Path.Combine(bin, "java.exe");
    }

    public static bool IsAlive(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }
}
