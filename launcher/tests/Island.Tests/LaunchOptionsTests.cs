using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;
using System.IO.Compression;
using BlockDock.Core;
using Island.Core;
using Microsoft.Win32;

internal static class LaunchOptionsTests
{
    public static async Task RunAsync(string root, Action<bool, string> check)
    {
        static IReadOnlyList<string> Gc(GarbageCollectorMode mode, int major, params string[] custom) => GarbageCollectors.Arguments(new() { GarbageCollector = mode }, major, custom);
        check(Gc(GarbageCollectorMode.PreferZ, 17).SequenceEqual(new[] { "-XX:+UseZGC" }) && Gc(GarbageCollectorMode.PreferZ, 8).Contains("-XX:+UseG1GC"), "Preferred ZGC uses supported Java and falls back for Java 8");
        check(Gc(GarbageCollectorMode.PreferGenerationalZ, 21).Contains("-XX:+ZGenerational") && !Gc(GarbageCollectorMode.PreferGenerationalZ, 24).Contains("-XX:+ZGenerational"), "Generational ZGC uses the opt-in flag only on Java 21 and 22");
        check(Gc(GarbageCollectorMode.G1, 17).Count == 1 && Gc(GarbageCollectorMode.TunedG1, 17).Contains("-XX:MaxGCPauseMillis=200"), "Standard G1 and tuned G1 generate distinct argument sets");
        check(Gc(GarbageCollectorMode.Custom, 25).Count == 0 && Gc(GarbageCollectorMode.PreferZ, 25, "-XX:+UseSerialGC").Count == 0, "Custom GC arguments take precedence and unspecified mode adds no collector");
        check(GarbageCollectors.Arguments(new() { OmitOptimizations = true }, 25, []).Count == 0 && Gc(GarbageCollectorMode.TunedG1, 7).Count == 0, "Optimization opt-out and old Java do not receive modern collector arguments");
        var options = new JvmSettings { ExtraGameArguments = "--server \"服务器 地址\"", PreLaunchCommand = "echo test", GarbageCollector = GarbageCollectorMode.PreferGenerationalZ,
            DisableJavaLaunchWrapper = true, DisableLwjglUnsafeAgent = true, HighPerformanceGpu = true, VerifyLoginCertificates = false };
        var state = new LauncherState { GameDirectory = root, Jvm = options }; state.EnsureGameFolders(); state.For("A").Jvm = options with { ExtraGameArguments = "--width 900" };
        var store = new LauncherStore(Path.Combine(root, "advanced-options")); store.Save(state); var loaded = store.Load();
        check(loaded.Jvm == options && loaded.RuntimeFor(root, "A").Jvm.ExtraGameArguments == "--width 900", "New launch options persist and independent instances retain their own game arguments");
        check(new JvmSettings().VerifyLoginCertificates && !new JvmSettings().HighPerformanceGpu && JsonSerializer.Deserialize<JvmSettings>("{}")!.VerifyLoginCertificates, "New and upgraded settings keep certificate verification enabled and GPU registry changes opt-in");
        check(LaunchPatches.UseWrapper(new(), 8, 65001) && !LaunchPatches.UseWrapper(new(), 19, 65001) && !LaunchPatches.UseWrapper(new() { DisableJavaLaunchWrapper = true }, 8, 65001), "Java wrapper is conditional on the affected Java / Windows encoding and the disable switch");
        var profile = JsonNode.Parse("""{"libraries":[{"name":"org.lwjgl:lwjgl:3.4.1"}]}""")!.AsObject();
        check(LaunchPatches.UseUnsafeAgent(new(), 25, profile) && !LaunchPatches.UseUnsafeAgent(new(), 21, profile) && !LaunchPatches.UseUnsafeAgent(new() { DisableLwjglUnsafeAgent = true }, 25, profile), "LWJGL agent applies only to supported Java / LWJGL and respects opt-out");
        profile["libraries"]!.AsArray().Add(new JsonObject { ["name"] = "net.minecraftforge:fmlloader:1" });
        check(!LaunchPatches.UseUnsafeAgent(new(), 25, profile), "Forge module launches are excluded from automatic LWJGL agent injection");
        var wrapper = LaunchPatches.Extract(root, true); var agent = LaunchPatches.Extract(root, false);
        check(new FileInfo(wrapper).Length == 16345 && new FileInfo(agent).Length == 9821, "Pinned bundled compatibility components extract locally without a launch-time download");
        File.WriteAllText(agent, "corrupt"); check(new FileInfo(LaunchPatches.Extract(root, false)).Length == 9821, "Corrupt compatibility cache is repaired from embedded resources");
        using (var handler = MicrosoftAuth.CreateHandler(true)) check(handler.ServerCertificateCustomValidationCallback is null, "Default login transport retains the system certificate verifier");
        using var rsa = RSA.Create(2048); var certRequest = new CertificateRequest("CN=local-fixture", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = certRequest.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(1));
        using var authRequest = new HttpRequestMessage(HttpMethod.Post, "https://login.microsoftonline.com/consumers/oauth2/v2.0/token");
        using var otherRequest = new HttpRequestMessage(HttpMethod.Get, "https://textures.minecraft.net/texture/fixture");
        check(MicrosoftAuth.ValidateCompatibilityCertificate(authRequest, cert, null, SslPolicyErrors.RemoteCertificateChainErrors)
            && !MicrosoftAuth.ValidateCompatibilityCertificate(otherRequest, cert, null, SslPolicyErrors.RemoteCertificateChainErrors), "Explicit SSL compatibility mode is restricted to login hosts and does not weaken skin or other downloads");
        var java = Path.Combine(root, "fixture java", "java.exe");
        var registry = new FakeGpuStore { Value = "GpuPreference=1;VRROptimizeEnable=1;" };
        using (GpuPreferenceLease.Acquire(java, registry)) check(registry.Value as string == "GpuPreference=2;VRROptimizeEnable=1;", "GPU preference request preserves other application preference fields");
        check(registry.Value as string == "GpuPreference=1;VRROptimizeEnable=1;" && registry.Disposed, "Normal exit restores the exact previous GPU preference");
        registry = new(); using (GpuPreferenceLease.Acquire(java, registry)) { }
        check(registry.Value is null, "A previously absent GPU preference is removed when the lease ends");
        registry = new() { Value = "GpuPreference=1;" }; using (GpuPreferenceLease.Acquire(java, registry)) registry.Value = "GpuPreference=0;UserChanged=1;";
        check(registry.Value as string == "GpuPreference=0;UserChanged=1;", "Restoration preserves graphics settings changed by the user during play");
        var commandDirectory = Path.Combine(root, "命令 含空格"); Directory.CreateDirectory(commandDirectory);
        var output = new List<string>();
        await PreLaunchCommands.RunAsync("echo COMMAND_OK & echo marker>\"command marker.txt\"", commandDirectory, default, line => { lock (output) output.Add(line); });
        check(output.Any(line => line.Contains("COMMAND_OK")) && File.ReadAllText(Path.Combine(commandDirectory, "command marker.txt")).Contains("marker"), "A real pre-launch cmd command runs in the correct Unicode / spaced working directory and captures output");
        try { await PreLaunchCommands.RunAsync("exit /b 7", commandDirectory, default); throw new Exception("Failure not reported"); }
        catch (InvalidOperationException ex) { check(ex.Message.Contains('7'), "A failing pre-launch command blocks startup with its exit code"); }
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { await PreLaunchCommands.RunAsync("echo bad>cancelled.txt", commandDirectory, canceled.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { check(!File.Exists(Path.Combine(commandDirectory, "cancelled.txt")), "A canceled pre-launch command never starts a process"); }
        using var runningCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250)); var watch = Stopwatch.StartNew();
        var sleeper = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        try { await PreLaunchCommands.RunAsync($"\"{sleeper}\" -NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 30\"", commandDirectory, runningCancellation.Token); throw new Exception("Running command cancellation ignored"); }
        catch (OperationCanceledException) { check(watch.Elapsed < TimeSpan.FromSeconds(8), "Canceling an active pre-launch command terminates its owned process tree promptly"); }
    }
    public static async Task VerifyRuntimeAsync(string root, ResolvedVersion fixture, JavaInstallation java, LaunchIdentity identity, Action<bool, string> check)
    {
        var remembered = await LauncherJava.ResolveAsync(new() { GameDirectory = root }, new(java.Major), new(), default, java.Path);
        check(remembered.Path == java.Path && remembered.Major >= java.Major && remembered.Is64Bit, "A remembered automatic Java is re-probed before reuse and still satisfies the instance requirement");
        var metadata = fixture.Profile.DeepClone().AsObject(); metadata["arguments"]!["jvm"] = new JsonArray("-XX:+UseParallelGC");
        var plan = GameLauncher.Prepare(root, fixture with { Profile = metadata }, java.Path, 512, identity,
            options: new() { GarbageCollector = GarbageCollectorMode.PreferGenerationalZ, ExtraGameArguments = "--island-test \"中文 参数\"" }, javaMajor: java.Major);
        try
        {
            check(plan.Arguments.Count(argument => argument.StartsWith("-XX:+Use", StringComparison.Ordinal) && argument.EndsWith("GC", StringComparison.Ordinal)) == 1,
                "Selecting a collector replaces a conflicting metadata collector rather than passing incompatible flags");
            var output = await Execute(plan.StartInfo);
            check(output.Contains("ARG=中文 参数") && output.Contains("ISLAND_JVM_STARTED"), "A real JVM starts with the selected collector and receives quoted Unicode game arguments");
        }
        finally { await GameLauncher.CleanupNativesAsync(root, plan.NativesDirectory); }
        if (java.Major >= 25)
        {
            var lwjgl = Path.Combine(root, "libraries", "org", "lwjgl", "lwjgl", "3.4.1", "lwjgl-3.4.1.jar"); Directory.CreateDirectory(Path.GetDirectoryName(lwjgl)!);
            using (ZipFile.Open(lwjgl, ZipArchiveMode.Create)) { }
            metadata = fixture.Profile.DeepClone().AsObject(); metadata["libraries"] = new JsonArray(new JsonObject { ["name"] = "org.lwjgl:lwjgl:3.4.1" });
            plan = GameLauncher.Prepare(root, fixture with { Profile = metadata }, java.Path, 512, identity, javaMajor: java.Major);
            try
            {
                var output = await Execute(plan.StartInfo);
                check(output.Contains("LWJGL Unsafe Agent version: 2.0") && output.Contains("ISLAND_JVM_STARTED"), "The pinned LWJGL agent initializes in an actual Java 25+ process before the fixture game");
            }
            finally { await GameLauncher.CleanupNativesAsync(root, plan.NativesDirectory); }
        }
    }
    private static async Task<string> Execute(ProcessStartInfo info)
    {
        using var process = new Process { StartInfo = info }; process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var cancel = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        await process.WaitForExitAsync(); var output = await stdout + await stderr;
        if (process.ExitCode != 0) throw new Exception("Runtime option fixture failed: " + output);
        return output;
    }
    private sealed class FakeGpuStore : IGpuPreferenceStore
    {
        public object? Value; public bool Disposed;
        public object? Read(string _) => Value;
        public RegistryValueKind Kind(string _) => RegistryValueKind.String;
        public void Write(string _, object value, RegistryValueKind kind) => Value = value;
        public void Delete(string _) => Value = null;
        public void Dispose() => Disposed = true;
    }
}
