using System.Net;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using BlockDock.Core;
using Island.Core;

internal static class LaunchCompatibilityTests
{
    public static async Task VerifyRuntimeAsync(string root, ResolvedVersion fixture, JavaInstallation java, LaunchIdentity identity, Action<bool, string> check)
    {
        // A renamed child must refer to its actual inherited client JAR, not <selected version>.jar.
        var child = "Forge 子版本";
        var directory = Path.Combine(root, "versions", child); Directory.CreateDirectory(directory);
        var metadata = new JsonObject
        {
            ["id"] = child, ["inheritsFrom"] = fixture.Id,
            ["arguments"] = new JsonObject
            {
                ["jvm"] = new JsonArray("-DignoreList=bootstraplauncher,${version_name}.jar,${primary_jar_name}"),
                ["game"] = new JsonArray("--primary-jar", "${primary_jar_name}")
            }
        };
        File.WriteAllText(Path.Combine(directory, child + ".json"), metadata.ToJsonString());
        var inherited = VersionProfile.Read(root, child);
        check(inherited.ClientJar == fixture.ClientJar, "A Forge child keeps the actual inherited client JAR despite a different selected version name");
        var plan = GameLauncher.Prepare(root, inherited, java.Path, 512, identity, javaMajor: java.Major);
        try
        {
            var jar = Path.GetFileName(fixture.ClientJar);
            check(plan.Arguments.Contains("-DignoreList=bootstraplauncher," + child + ".jar," + jar) && !plan.Arguments.Any(arg => arg.Contains("${primary_jar_name}")),
                "HMCL primary_jar_name expands to the resolved JAR basename in Forge ignoreList");
            using var process = new Process { StartInfo = plan.StartInfo }; process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { if (!process.HasExited) process.Kill(true); throw; }
            var output = await stdout + await stderr;
            var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("bootstraplauncher," + child + ".jar," + jar));
            check(process.ExitCode == 0 && output.Contains("IGNORE_BASE64=" + expected) && output.Contains("ARG=" + jar),
                "A real JVM receives the complete Unicode / spaced primary JAR in its system property and game arguments");
        }
        finally { await GameLauncher.CleanupNativesAsync(root, plan.NativesDirectory); }
        var standaloneProfile = fixture.Profile.DeepClone().AsObject();
        standaloneProfile["arguments"]!["jvm"] = new JsonArray("-DignoreList=${primary_jar_name}");
        plan = GameLauncher.Prepare(root, fixture with { Profile = standaloneProfile }, java.Path, 512, identity, javaMajor: java.Major);
        try { check(plan.Arguments.Contains("-DignoreList=" + Path.GetFileName(fixture.ClientJar)), "Standalone imported profiles resolve the primary client JAR placeholder"); }
        finally { await GameLauncher.CleanupNativesAsync(root, plan.NativesDirectory); }
        standaloneProfile["arguments"]!["jvm"] = new JsonArray("-Dtest=${unsupported_fixture_parameter}");
        try { GameLauncher.Prepare(root, fixture with { Profile = standaloneProfile }, java.Path, 512, identity, javaMajor: java.Major); throw new Exception("Unknown placeholder accepted"); }
        catch (InvalidDataException) { check(true, "Unknown placeholders still fail clearly instead of silently dropping loader configuration"); }
    }

    public static async Task RunAsync(string root, Action<bool, string> check)
    {
        var modRoot = Path.Combine(root, "mod-java-requirement");
        var modInstance = Path.Combine(modRoot, "versions", "Odyssey"); var mods = Path.Combine(modInstance, "mods"); Directory.CreateDirectory(mods);
        File.WriteAllText(Path.Combine(modInstance, "Odyssey.json"), """{"mainClass":"Main","javaVersion":{"majorVersion":17}}""");
        void WriteMod(string path, byte major, string entryName = "com/gtocore/Core.class")
        {
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create); using var entry = zip.CreateEntry(entryName).Open();
            entry.Write(new byte[] { 0xca, 0xfe, 0xba, 0xbe, 0, 0, 0, major });
        }
        var modPath = Path.Combine(mods, "gtocore-test.jar"); WriteMod(modPath, 65);
        check(JavaRequirement.FromMetadata(VersionProfile.Read(modRoot, "Odyssey").Profile).MinimumMajor == 21, "An enabled GTOCore entry class raises stale Minecraft Java 17 metadata to its actual Java 21 requirement");
        check(JavaRequirement.FromMetadata(VersionProfile.Read(modRoot, "Odyssey", VersionIsolation.Disabled).Profile).MinimumMajor == 17, "Explicit shared game directories ignore GTOCore installed only in the isolated version folder");
        File.Move(modPath, modPath + ".disabled");
        check(JavaRequirement.FromMetadata(VersionProfile.Read(modRoot, "Odyssey").Profile).MinimumMajor == 17, "Disabled GTOCore does not raise the Java requirement");
        WriteMod(modPath, 69, "META-INF/versions/25/com/gtocore/Core.class");
        check(JavaRequirement.FromMetadata(VersionProfile.Read(modRoot, "Odyssey").Profile).MinimumMajor == 17, "An optional multi-release class is not mistaken for the GTOCore entry class");
        File.Delete(modPath); WriteMod(modPath, 61);
        check(JavaRequirement.FromMetadata(VersionProfile.Read(modRoot, "Odyssey").Profile).MinimumMajor == 17, "A Java 17 GTOCore build does not receive a hard-coded Java 21 requirement");
        var state = new LauncherState { GameDirectory = root }; state.EnsureGameFolders(); state.For("AutoJava").AutoSelectedJavaPath = "C:/fixture/java.exe";
        var store = new LauncherStore(Path.Combine(root, "automatic-java-cache")); store.Save(state);
        check(store.Load().For("AutoJava") is { AutoSelectedJavaPath: "C:/fixture/java.exe", HasIndependentSettings: false }, "Automatic Java selection persists across app restarts without becoming a manual or independent override");
        var profile = JsonNode.Parse("""{"mainClass":"top.outlands.foundation.boot.Foundation","javaVersion":{"majorVersion":8},"libraries":[{"name":"com.cleanroommc:cleanroom:0.3.31-alpha","downloads":{}}]}""")!.AsObject();
        check(LaunchLibraries.MinimumJava(root, profile) >= 21, "A legacy Java 8 declaration cannot select Java 8 for a Foundation / Cleanroom instance");
        var library = profile["libraries"]![0]!.AsObject(); var local = LaunchLibraries.PathFor(root, library); Directory.CreateDirectory(Path.GetDirectoryName(local)!); File.WriteAllText(local, "installed-loader");
        check(LaunchLibraries.HasArtifact(root, library), "An installed Cleanroom artifact with an empty downloads object remains in the launch classpath");
        profile = JsonNode.Parse("""{"libraries":[{"name":"tv.twitch:twitch-platform:5.16","natives":{"windows":"natives-windows-${arch}"},"downloads":{"classifiers":{"natives-windows-64":{"url":"https://libraries.minecraft.net/fixture.jar","sha1":"1234","size":10}}}}]}""")!.AsObject();
        var native = LaunchLibraries.Files(root, profile).Single();
        check(native.Path.EndsWith(Path.Combine("tv", "twitch", "twitch-platform", "5.16", "twitch-platform-5.16-natives-windows-64.jar")) && native.Sha1 == "1234", "GTNH / legacy native metadata without path resolves a file coordinate instead of the libraries directory");
        profile = JsonNode.Parse("""{"libraries":[{"name":"org.jackhuang.hmcl:mmc-bootstrap:1.0"}]}""")!.AsObject();
        try { _ = LaunchLibraries.Files(root, profile).ToArray(); throw new Exception("Missing local bootstrap accepted"); }
        catch (FileNotFoundException) { check(true, "A missing imported launcher bootstrap reports the local dependency instead of retrying a nonexistent Mojang download"); }
        var directory = Path.Combine(root, "verification-fixture"); Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "asset.bin"); var bytes = Encoding.UTF8.GetBytes("known-good-data"); File.WriteAllBytes(file, bytes);
        var hash = Convert.ToHexString(SHA1.HashData(bytes)); var handler = new FixtureHandler(bytes);
        using var network = new DownloadClient(new() { Source = DownloadSource.Official }, handler);
        var transfer = new TransferFile(file, ["https://fixture.test/asset"], bytes.Length, hash);
        await using (var verifier = new LaunchFileVerifier(directory, network)) await verifier.EnsureAsync(transfer, default);
        await using (var verifier = new LaunchFileVerifier(directory, network))
        {
            await verifier.EnsureAsync(transfer, default);
            check(verifier.CacheHits == 1 && handler.Requests == 0, "A verified unchanged file reuses its persistent validation without reading or downloading it again");
        }
        File.WriteAllBytes(file, Encoding.UTF8.GetBytes("corrupted-data!")); File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(3));
        await using (var verifier = new LaunchFileVerifier(directory, network))
        {
            await verifier.EnsureAsync(transfer, default);
            check(verifier.CacheHits == 0 && handler.Requests == 1 && File.ReadAllBytes(file).SequenceEqual(bytes), "Changing a cached file triggers full verification and repairs corrupt contents");
        }
        handler.Bytes = Encoding.UTF8.GetBytes("new-expected-data");
        var updated = transfer with { Sha1 = Convert.ToHexString(SHA1.HashData(handler.Bytes)), Size = handler.Bytes.Length };
        await using (var verifier = new LaunchFileVerifier(directory, network))
        {
            await verifier.EnsureAsync(updated, default);
            check(verifier.CacheHits == 0 && handler.Requests == 2 && File.ReadAllBytes(file).SequenceEqual(handler.Bytes), "Updated expected metadata invalidates a cached verification");
        }
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await using (var verifier = new LaunchFileVerifier(directory, network))
        {
            try { await verifier.EnsureAsync(updated, canceled.Token); throw new Exception("Canceled cache access accepted"); }
            catch (OperationCanceledException) { check(verifier.CacheHits == 0, "Cancellation is honored even when a verification cache hit is available"); }
        }
    }
    private sealed class FixtureHandler(byte[] bytes) : HttpMessageHandler
    {
        public byte[] Bytes = bytes; public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Requests++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Bytes) }); }
    }
}
