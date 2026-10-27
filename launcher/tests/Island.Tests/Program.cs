using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using BlockDock.Core;
using Island.Core;

var inspectIndex = Array.IndexOf(args, "--inspect-instances");
if (inspectIndex >= 0)
{
    foreach (var instance in InstanceCatalog.ListInstalled(Path.GetFullPath(args[inspectIndex + 1]))) Console.WriteLine(instance.Name + " | " + instance.LoaderLabel);
    return;
}
if (args is ["--java-scan", var gameRoot, var expectedJava])
{
    var discovered = await JavaRuntimeCatalog.DiscoverAsync(Path.GetFullPath(gameRoot), "", default);
    foreach (var java in discovered) Console.WriteLine(java.Display);
    var expected = discovered.SingleOrDefault(java => java.Path.Equals(Path.GetFullPath(expectedJava), StringComparison.OrdinalIgnoreCase));
    if (expected is not { Is64Bit: true } || !expected.Vendor.Contains("Azul", StringComparison.OrdinalIgnoreCase))
        throw new Exception("Expected executable Azul Zulu runtime was not discovered");
    Console.WriteLine("PASS LIVE ZULU SCAN: " + expected.Display);
    return;
}

var root = Path.Combine(Path.GetTempPath(), "Island Tests " + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var count = 0;
void Check(bool value, string label) { if (!value) throw new Exception(label); count++; Console.WriteLine("PASS " + label); }
void Throws(Action action, string label) { try { action(); } catch (Exception ex) when (ex is ArgumentException or InvalidDataException or IOException) { Check(true, label); return; } throw new Exception("Expected failure: " + label); }
void Write(string id, JsonObject json)
{
    var folder = SafePaths.Under(root, "versions/" + id); Directory.CreateDirectory(folder);
    File.WriteAllText(Path.Combine(folder, id + ".json"), json.ToJsonString());
}
JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();
try
{
    FolderAndWorldTests.Run(root, Check);
    RuntimeSettingsTests.Run(root, Check);
    JvmAndMemoryTests.Run(root, Check);
    await PlayerSkinTests.RunAsync(root, Check);
    await MicrosoftLoginTests.RunAsync(Check);
    await DownloadCenterTests.RunAsync(root, Check);
    await ContentDownloadTests.RunAsync(root, Check);
    AppearanceTests.Run(root, Check);
    InstanceCatalogTests.Run(root, Check);
    MemoryCleanupTests.Run(root, Check);
    await LaunchOptionsTests.RunAsync(root, Check);
    await LaunchCompatibilityTests.RunAsync(root, Check);
    await JavaRuntimeCacheTests.RunAsync(root, Check);
    ZuluDiscoveryTests.Run(root, Check);
    var offline = PlayerAccount.Offline("Notch");
    Check(offline.Uuid == "b50ad385829d3141a2167e7d7539ba7f", "Java-compatible offline UUID");
    Throws(() => PlayerAccount.Offline("坏 名字"), "Reject invalid player names");
    var store = new LauncherStore(Path.Combine(root, "configuration")); var state = store.Load();
    state.GameDirectory = root; state.Accounts.Add(offline); state.For("A").Favorite = true; store.Save(state); store.Save(state);
    Check(store.Load().For("A").Favorite && File.Exists(Path.Combine(store.DirectoryPath, "settings.json.bak")), "Atomic settings and backup roundtrip");
    state.GameDirectory = root + "-another"; Check(!state.For("A").Favorite, "Instance preferences scoped to game directory");
    File.WriteAllText(Path.Combine(store.DirectoryPath, "settings.json"), "{corrupt");
    store.Load(); Check(store.LoadWarning is not null && File.ReadAllText(Path.Combine(store.DirectoryPath, "settings.json")) == "{corrupt", "Corrupt settings preserved");
    var protectedToken = SecretProtection.Protect("secret-token-for-test");
    Check(!protectedToken.Contains("secret") && SecretProtection.Unprotect(protectedToken) == "secret-token-for-test", "DPAPI token roundtrip");
    Write("base", Parse("""{"id":"base","mainClass":"OldMain","javaVersion":{"majorVersion":17},"libraries":[{"name":"org.example:shared:1"},{"name":"org.example:classed:1:natives-windows"}],"arguments":{"jvm":["-cp","${classpath}"],"game":["--username","${auth_player_name}"]}}"""));
    File.WriteAllBytes(Path.Combine(root, "versions/base/base.jar"), [1]);
    Write("child", Parse("""{"id":"child","inheritsFrom":"base","mainClass":"NewMain","javaVersion":{"majorVersion":8},"libraries":[{"name":"org.example:shared:2"}],"arguments":{"game":["--gameDir","${game_directory}"]}}"""));
    // Base has a client download entry so its JAR is inherited.
    var baseJson = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "versions/base/base.json")))!.AsObject();
    baseJson["downloads"] = new JsonObject { ["client"] = new JsonObject { ["url"] = "https://fixture.invalid/client.jar" } }; Write("base", baseJson);
    var resolved = VersionProfile.Read(root, "child");
    Check(resolved.Profile.Str("mainClass") == "NewMain" && resolved.Profile.Array("libraries").Count == 2, "Profile inheritance and Maven conflict replacement");
    Check(resolved.Profile["javaVersion"]?["majorVersion"]?.GetValue<int>() == 17, "Highest Java requirement inherited");
    Check(resolved.ClientJar == Path.GetFullPath(Path.Combine(root, "versions/base/base.jar")), "Inherited client JAR");
    Check(resolved.Profile["arguments"].Array("game").Count == 4, "Parent and loader game arguments merged");
    Write("renamed", Parse("""{"id":"upstream-id","mainClass":"Main","downloads":{"client":{"url":"https://fixture.invalid/game.jar"}}}"""));
    Check(VersionProfile.Read(root, "renamed").ClientJar == SafePaths.Under(root, "versions/renamed/renamed.jar"), "Imported profile uses enclosing directory for renamed client JAR");
    Throws(() => GameLauncher.CleanupNativesAsync(root, root).GetAwaiter().GetResult(), "Native cleanup refuses directories it did not create");
    Write("cycle1", Parse("""{"inheritsFrom":"cycle2"}""")); Write("cycle2", Parse("""{"inheritsFrom":"cycle1"}"""));
    Throws(() => VersionProfile.Read(root, "cycle1"), "Detect inheritance cycle");
    Write("escape", Parse("""{"inheritsFrom":"../outside"}""")); Throws(() => VersionProfile.Read(root, "escape"), "Reject inherited path traversal");
    Write("missing-parent", Parse("""{"inheritsFrom":"not-present"}""")); Throws(() => VersionProfile.Read(root, "missing-parent"), "Report missing parent version");
    Check(!VersionProfile.Allowed(Parse("""{"rules":[{"action":"allow","os":{"name":"linux"}}]}""")), "Exclude other-platform libraries");
    Check(!VersionProfile.Allowed(Parse("""{"rules":[{"action":"allow","features":{"is_demo_user":true}}]}""")), "No unwanted demo or quickplay args");
    Check(VersionProfile.Allowed(Parse("""{"rules":[{"action":"allow","features":{"is_demo_user":false}}]}""")), "Match false feature requirements");
    Check(VersionProfile.SplitArguments("--gameDir \"F:\\含 空格\\游戏\" --token \"\"").SequenceEqual(new[] { "--gameDir", "F:\\含 空格\\游戏", "--token", "" }), "Quoted legacy arguments preserve spaces and empty values");
    Throws(() => VersionProfile.SplitArguments("--test \"broken"), "Reject unclosed argument quotes");
    var instance = Path.Combine(root, "versions/child"); var sourceMod = Path.Combine(root, "fixture.jar"); File.WriteAllBytes(sourceMod, [1, 2, 3]);
    ModFiles.Add(instance, [sourceMod]); var mod = ModFiles.List(instance).Single();
    ModFiles.Toggle(instance, mod); Check(!ModFiles.List(instance).Single().Enabled, "Disable mod without deleting it");
    ModFiles.Toggle(instance, ModFiles.List(instance).Single()); Check(ModFiles.List(instance).Single().Enabled, "Re-enable mod");
    Throws(() => ModFiles.Add(instance, [sourceMod]), "Never overwrite an existing mod");
    Throws(() => ModFiles.Toggle(instance, mod with { Path = sourceMod }), "Mod operations confined to instance");
    var nativesArchive = Path.Combine(root, "natives.zip");
    using (var zip = ZipFile.Open(nativesArchive, ZipArchiveMode.Create)) { using var w = new StreamWriter(zip.CreateEntry("META-INF/test").Open()); w.Write("skip"); }
    var natives = Path.Combine(root, "native-output"); Directory.CreateDirectory(natives);
    GameLauncher.ExtractNatives(nativesArchive, natives, ["META-INF/"]); Check(!Directory.EnumerateFiles(natives, "*", SearchOption.AllDirectories).Any(), "Native archive excludes signatures");
    var malicious = Path.Combine(root, "malicious.zip"); using (var zip = ZipFile.Open(malicious, ZipArchiveMode.Create)) { using var w = new StreamWriter(zip.CreateEntry("../escape.dll").Open()); w.Write("bad"); }
    Throws(() => GameLauncher.ExtractNatives(malicious, natives, []), "Native ZIP traversal rejected");
    Check(!File.Exists(Path.Combine(root, "escape.dll")), "Traversal writes no outside file");
    var formatter = new GameLogFormatter();
    Check(formatter.Push("plain output") == "plain output", "Plain loader output preserved");
    Check(formatter.Push("<log4j:Event logger=\"Game\" level=\"INFO\" thread=\"Render thread\">") is null
        && formatter.Push("<log4j:Message><![CDATA[游戏启动成功]]></log4j:Message>") is null
        && formatter.Push("</log4j:Event>") == "INFO [Render thread] 游戏启动成功", "Mojang XML console events formatted as readable text");

    var authRequests = new List<string>();
    using (var auth = new MicrosoftAuth(new MockHandler(async request =>
    {
        var url = request.RequestUri!.AbsoluteUri; authRequests.Add(url);
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync();
        if (url.EndsWith("/token")) { Check(body.Contains("device_code") || body.Contains("refresh_token"), "OAuth grant sent in form body"); return """{"access_token":"msa","refresh_token":"refresh"}"""; }
        if (url.Contains("user.auth")) return """{"Token":"xbox","DisplayClaims":{"xui":[{"uhs":"123"}]}}""";
        if (url.Contains("xsts.auth")) return """{"Token":"xsts","DisplayClaims":{"xui":[{"uhs":"123"}]}}""";
        if (url.EndsWith("/authentication/login_with_xbox")) return """{"access_token":"minecraft"}""";
        if (url.EndsWith("/entitlements/mcstore")) return """{"items":[{"name":"game_minecraft"}]}""";
        if (url.EndsWith("/minecraft/profile")) { Check(request.Headers.Authorization?.Parameter == "minecraft", "Minecraft profile authenticated with bearer token"); return """{"id":"123456781234123412341234567890ab","name":"TestPlayer"}"""; }
        throw new Exception("Unexpected auth URL: " + url);
    })))
    {
        var result = await auth.CompleteAsync(Guid.NewGuid().ToString(), new("device", "CODE", "https://microsoft.com/link", 0, 20), CancellationToken.None);
        Check(result.Account.Microsoft && result.Identity.AccessToken == "minecraft" && SecretProtection.Unprotect(result.Account.ProtectedRefreshToken) == "refresh", "Microsoft, Xbox, XSTS and Minecraft authorization exchange");
        Check(authRequests.Count == 6, "Auth chain verifies entitlements before reading the Java profile");
        result.Account.SkinPng = new byte[] { 1, 2, 3 };
        var refresh = await auth.RefreshAsync(result.Account, CancellationToken.None);
        Check(refresh.Account.Id == result.Account.Id, "Token refresh preserves selected account identity");
        Check(refresh.Account.SkinPng == result.Account.SkinPng, "Token refresh preserves the cached skin when the profile skin is unchanged");
    }
    using (var auth = new MicrosoftAuth(new MockHandler(_ => Task.FromResult("{}"))))
    {
        try { await auth.BeginAsync("", CancellationToken.None); throw new Exception("Missing client ID accepted"); }
        catch (ArgumentException) { Check(true, "No borrowed Microsoft client ID"); }
    }

    // Execute a real JVM through the launch plan, using a tiny local fixture instead of a Minecraft account.
    var java = (await JavaDiscovery.DiscoverAsync(JavaSearchPaths.ForGameDirectory(root, ""), CancellationToken.None))
        .First(j => j.Is64Bit && File.Exists(Path.Combine(Path.GetDirectoryName(j.Path)!, "javac.exe")));
    var javac = Path.Combine(Path.GetDirectoryName(java.Path)!, "javac.exe");
    if (!File.Exists(javac)) throw new Exception("Runtime test needs a JDK with javac.exe");
    var javaSource = Path.Combine(root, "IslandLaunchFixture.java");
    await File.WriteAllTextAsync(javaSource, """
        public class IslandLaunchFixture {
            public static void main(String[] args) {
                System.out.println("ISLAND_JVM_STARTED");
                System.out.println("DIR=" + System.getProperty("user.dir"));
                System.out.println("JAVA_OPTIONS=" + System.getenv("_JAVA_OPTIONS"));
                System.out.println("IGNORE_BASE64=" + java.util.Base64.getEncoder().encodeToString(System.getProperty("ignoreList", "").getBytes(java.nio.charset.StandardCharsets.UTF_8)));
                System.out.println("CUSTOM=" + System.getProperty("island.test.option"));
                System.out.println("CUSTOM_BASE64=" + java.util.Base64.getEncoder().encodeToString(System.getProperty("island.test.option", "").getBytes(java.nio.charset.StandardCharsets.UTF_8)));
                System.out.println("MAX_MEMORY_MB=" + Runtime.getRuntime().maxMemory() / 1048576);
                for (String arg : args) System.out.println("ARG=" + arg);
            }
        }
        """);
    var compileInfo = new ProcessStartInfo(javac) { UseShellExecute = false, CreateNoWindow = true }; compileInfo.ArgumentList.Add(javaSource);
    using (var compile = Process.Start(compileInfo)!) { await compile.WaitForExitAsync(); Check(compile.ExitCode == 0, "Compile real Java fixture"); }
    var fixtureId = "本地启动 测试";
    Write(fixtureId, Parse("""{"id":"本地启动 测试","mainClass":"IslandLaunchFixture","arguments":{"game":["--username","${auth_player_name}","--gameDir","${game_directory}","--accessToken","${auth_access_token}",{"rules":[{"action":"allow","features":{"is_demo_user":true}}],"value":"--demo"}]}}"""));
    using (var zip = ZipFile.Open(Path.Combine(root, "versions", fixtureId, fixtureId + ".jar"), ZipArchiveMode.Create)) zip.CreateEntryFromFile(Path.Combine(root, "IslandLaunchFixture.class"), "IslandLaunchFixture.class");
    var fixture = VersionProfile.Read(root, fixtureId);
    await LaunchCompatibilityTests.VerifyRuntimeAsync(root, fixture, java, new("TestPlayer", offline.Uuid, "0", false), Check);
    await LaunchOptionsTests.VerifyRuntimeAsync(root, fixture, java, new("TestPlayer", offline.Uuid, "0", false), Check);
    try { await LauncherJava.ResolveAsync(new() { GameDirectory = root, JavaPath = java.Path }, new(999), new(), CancellationToken.None); throw new Exception("Incompatible Java accepted"); }
    catch (InvalidOperationException) { Check(true, "Compatibility checking rejects a manually selected Java below the metadata requirement"); }
    Check((await LauncherJava.ResolveAsync(new() { GameDirectory = root, JavaPath = java.Path }, new(999), new() { SkipCompatibilityCheck = true }, CancellationToken.None)).Major == java.Major, "Explicit compatibility bypass still probes and accepts the selected Java");
    var customSettings = new JvmSettings { OmitDefaultArguments = true, OmitOptimizations = true, InitialMemoryMb = 128, PermGenMb = 256, ExtraArguments = "-Disland.test.option=\"中文 参数\" -XX:+UseSerialGC -Xmx768M" };
    var customPlan = GameLauncher.Prepare(root, fixture, java.Path, 1024, new("TestPlayer", offline.Uuid, "0", false), options: customSettings, javaMajor: java.Major);
    Check(!customPlan.Arguments.Contains("-Dfile.encoding=UTF-8") && !customPlan.Arguments.Contains("-XX:+UseG1GC") && customPlan.Arguments.Contains("-cp") && customPlan.Arguments.Any(argument => argument.StartsWith("-Djava.library.path=")), "Default and optimization switches preserve essential metadata, classpath and native paths");
    Check(!customPlan.Arguments.Any(argument => argument.StartsWith("-XX:MaxPermSize=")) && customPlan.Arguments.Contains("-Xms128M"), "Modern Java omits removed permanent-generation flags and applies configured initial heap");
    using (var process = Process.Start(customPlan.StartInfo)!)
    {
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { await process.WaitForExitAsync(timeout.Token); } catch { process.Kill(true); throw; }
        var output = await stdout;
        var heapMatch = System.Text.RegularExpressions.Regex.Match(output, @"MAX_MEMORY_MB=(\d+)");
        Check(process.ExitCode == 0 && output.Contains("CUSTOM_BASE64=" + Convert.ToBase64String(Encoding.UTF8.GetBytes("中文 参数"))) && heapMatch.Success && long.Parse(heapMatch.Groups[1].Value) is > 600 and <= 768, "Actual JVM receives Unicode custom properties and honors the custom heap override: " + await stderr + output);
    }
    await GameLauncher.CleanupNativesAsync(root, customPlan.NativesDirectory);
    var collectorPlan = GameLauncher.Prepare(root, fixture, java.Path, 1024, new("TestPlayer", offline.Uuid, "0", false), options: new() { ExtraArguments = "-XX:+UseSerialGC" }, javaMajor: java.Major);
    Check(!collectorPlan.Arguments.Contains("-XX:+UseG1GC"), "Automatic optimization never adds a competing collector to explicit custom GC flags");
    await GameLauncher.CleanupNativesAsync(root, collectorPlan.NativesDirectory);
    var oldJavaPlan = GameLauncher.Prepare(root, fixture, java.Path, 1024, new("TestPlayer", offline.Uuid, "0", false), options: new() { PermGenMb = 256 }, javaMajor: 7);
    Check(oldJavaPlan.Arguments.Contains("-XX:MaxPermSize=256M") && !oldJavaPlan.Arguments.Contains("-XX:+UseG1GC"), "Legacy memory option is emitted only for old Java without modern optimization flags");
    await GameLauncher.CleanupNativesAsync(root, oldJavaPlan.NativesDirectory);
    var quickProfile = (JsonObject)fixture.Profile.DeepClone();
    quickProfile["arguments"]!["game"]!.AsArray().Add(Parse("""{"rules":[{"action":"allow","features":{"is_quick_play_singleplayer":true}}],"value":["--quickPlaySingleplayer","${quickPlaySingleplayer}"]}"""));
    Directory.CreateDirectory(Path.Combine(root, "versions", fixtureId, "saves", "中文 存档"));
    var quickPlan = GameLauncher.Prepare(root, fixture with { Profile = quickProfile }, java.Path, 1024, new("TestPlayer", offline.Uuid, "0", false), "中文 存档");
    Check(quickPlan.Arguments.Contains("--quickPlaySingleplayer") && quickPlan.Arguments.Contains("中文 存档"), "Continue enables metadata-gated Quick Play for an existing world");
    await GameLauncher.CleanupNativesAsync(root, quickPlan.NativesDirectory);
    FolderAndWorldTests.WriteWorld(root, "Root World", "共享地图", DateTimeOffset.Now);
    var sharedPlan = GameLauncher.Prepare(root, fixture with { Profile = quickProfile }, java.Path, 1024, new("TestPlayer", offline.Uuid, "0", false), "Root World", VersionIsolation.Disabled);
    Check(sharedPlan.StartInfo.WorkingDirectory == root && sharedPlan.Arguments.Contains(root) && sharedPlan.Arguments.Contains("Root World"), "Shared working directory, gameDir and Quick Play all use root saves");
    using (var process = Process.Start(sharedPlan.StartInfo)!)
    {
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { await process.WaitForExitAsync(timeout.Token); } catch { process.Kill(true); throw; }
        Check(process.ExitCode == 0 && (await stdout).Contains("DIR=" + root), "Actual JVM starts in the shared game root: " + await stderr);
    }
    await GameLauncher.CleanupNativesAsync(root, sharedPlan.NativesDirectory);
    Throws(() => GameLauncher.Prepare(root, fixture with { Profile = quickProfile }, java.Path, 1024, new("TestPlayer", offline.Uuid, "0", false), "../../outside"), "Quick Play target cannot escape the saves directory");
    Environment.SetEnvironmentVariable("_JAVA_OPTIONS", "-Dunwanted=true");
    var plan = GameLauncher.Prepare(root, fixture, java.Path, 1024, new("TestPlayer", offline.Uuid, "0", false));
    Check(plan.Arguments.Contains("-XX:+UseG1GC") && plan.Arguments.Contains("-Dfile.encoding=UTF-8"), "Default launch keeps basic encoding and G1 optimization arguments");
    Check(!plan.StartInfo.Environment.ContainsKey("_JAVA_OPTIONS") && !plan.Arguments.Contains("--demo"), "Sanitize inherited JVM environment and feature arguments");
    using (var process = Process.Start(plan.StartInfo)!)
    {
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { await process.WaitForExitAsync(timeout.Token); } catch { process.Kill(true); throw; }
        var output = await stdout; var error = await stderr;
        Check(process.ExitCode == 0 && output.Contains("ISLAND_JVM_STARTED"), "Actual JVM process starts and exits normally: " + error);
        Check(output.Contains("ARG=" + Path.Combine(root, "versions", fixtureId)), "Unicode and spaces survive process argument boundaries: " + output.Replace('\n', ' '));
        Check(output.Contains("JAVA_OPTIONS=null"), "JVM starts with sanitized environment");
    }
    Environment.SetEnvironmentVariable("_JAVA_OPTIONS", null);
    Directory.Delete(plan.NativesDirectory, true);

    // Legacy assets must follow the runtime directory while libraries and asset objects stay shared.
    var resourceText = "legacy-resource-fixture";
    var resourceHash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(resourceText))).ToLowerInvariant();
    var assetIndex = new JsonObject { ["map_to_resources"] = true, ["objects"] = new JsonObject
    {
        ["sound/test.txt"] = new JsonObject { ["hash"] = resourceHash, ["size"] = Encoding.UTF8.GetByteCount(resourceText) }
    } }.ToJsonString();
    var legacyProfile = (JsonObject)fixture.Profile.DeepClone();
    legacyProfile["assetIndex"] = new JsonObject { ["id"] = "legacy-fixture", ["url"] = "https://fixture.invalid/index", ["size"] = (long)Encoding.UTF8.GetByteCount(assetIndex) };
    using (var network = new DownloadClient(new() { GameDirectory = root }, new MockHandler(request => Task.FromResult(request.RequestUri!.Host == "fixture.invalid" ? assetIndex : resourceText))))
    {
        await GameLauncher.RepairAsync(root, fixture with { Profile = legacyProfile }, network, null, CancellationToken.None, VersionIsolation.Disabled);
        Check(File.ReadAllText(Path.Combine(root, "resources", "sound", "test.txt")) == resourceText && !File.Exists(Path.Combine(root, "versions", fixtureId, "resources", "sound", "test.txt")), "Legacy resources map into the shared root when isolation is disabled");
        await GameLauncher.RepairAsync(root, fixture with { Profile = legacyProfile }, network, null, CancellationToken.None, VersionIsolation.Enabled);
        Check(File.ReadAllText(Path.Combine(root, "versions", fixtureId, "resources", "sound", "test.txt")) == resourceText, "Legacy resources map into the version directory when isolation is enabled");
    }

    if (args.Contains("--network"))
    {
        using var network = new DownloadClient(new() { GameDirectory = root });
        var installer = new MinecraftInstaller(new() { GameDirectory = root }, network);
        var versions = await installer.GetVersionsAsync(CancellationToken.None);
        Check(versions.Count > 100 && versions.Any(v => v.Type == "release"), "Live Mojang manifest");
        var fabric = await installer.GetLoaderVersionsAsync(LoaderKind.Fabric, "1.21.1", CancellationToken.None);
        Check(fabric.Count > 0, "Live Fabric loader metadata");
    }
    var installIndex = Array.IndexOf(args, "--install");
    if (installIndex >= 0)
    {
        var installRoot = Path.GetFullPath(args[installIndex + 1]); var version = args[installIndex + 2];
        var loaderIndex = Array.IndexOf(args, "--loader");
        var loader = loaderIndex >= 0 ? Enum.Parse<LoaderKind>(args[loaderIndex + 1]) : LoaderKind.Vanilla;
        var settings = new AppSettings { GameDirectory = installRoot };
        using var network = new DownloadClient(settings);
        var installer = new MinecraftInstaller(settings, network);
        var loaderVersion = loader == LoaderKind.Vanilla ? null : (await installer.GetLoaderVersionsAsync(loader, version, CancellationToken.None)).First(v => v.Stable).Version;
        var id = version + (loader == LoaderKind.Vanilla ? "" : "-" + loader) + "-Island";
        var lastStage = ""; var lastCompleted = -1;
        var installed = await installer.InstallAsync(new(version, id, loader, loaderVersion), new Progress<InstallProgress>(p =>
        {
            if (p.Stage == lastStage && (p.Completed == lastCompleted || p.Completed != p.Total && p.Completed % 200 != 0)) return;
            lastStage = p.Stage; lastCompleted = p.Completed; Console.WriteLine(p.Stage + " " + p.Completed + "/" + p.Total);
        }), CancellationToken.None);
        var actual = VersionProfile.Read(installRoot, id);
        var detected = await JavaDiscovery.ResolveAsync(settings, JavaRequirement.FromMetadata(actual.Profile), CancellationToken.None);
        var actualPlan = GameLauncher.Prepare(installRoot, actual, detected.Path, 2048, new("IslandTest", offline.Uuid, "0", false));
        Check(actualPlan.Arguments.Contains(actual.Profile.Str("mainClass")) && File.Exists(actual.ClientJar), "Real Minecraft install and launch plan");
        Directory.Delete(actualPlan.NativesDirectory, true);
        Console.WriteLine("INSTALLED " + installed);
    }
    Console.WriteLine($"ALL {count} CHECKS PASSED");
}
finally
{
    Environment.SetEnvironmentVariable("_JAVA_OPTIONS", null);
    // Only remove the exact temporary directory created by this test invocation.
    if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) Directory.Delete(root, true);
}

sealed class MockHandler(Func<HttpRequestMessage, Task<string>> response) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => new(HttpStatusCode.OK) { Content = new StringContent(await response(request), Encoding.UTF8, "application/json") };
}
