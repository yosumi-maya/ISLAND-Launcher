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
        metadata["arguments"]!["jvm"] = new JsonArray("-DignoreList=bootstraplauncher,${version_name}.jar");
        metadata["arguments"]!["game"] = new JsonArray("--version", "${version_name}");
        File.WriteAllText(Path.Combine(directory, child + ".json"), metadata.ToJsonString());
        plan = GameLauncher.Prepare(root, VersionProfile.Read(root, child), java.Path, 512, identity, javaMajor: java.Major);
        try
        {
            check(plan.Arguments.Contains("-DignoreList=bootstraplauncher," + child + ".jar," + Path.GetFileName(fixture.ClientJar)) &&
                plan.Arguments.TakeLast(2).SequenceEqual(new[] { "--version", child }),
                "Forge installer metadata ignores the inherited client JAR while preserving the selected game version name");
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

    public static async Task VerifyLongClassPathAsync(string root, ResolvedVersion fixture, JavaInstallation java,
        JavaInstallation? legacyJava, Action<bool, string> check)
    {
        var profile = fixture.Profile.DeepClone().AsObject();
        var libraries = profile.Array("libraries");
        for (var i = 0; i < 230; i++)
        {
            var library = new JsonObject { ["name"] = "test." + new string('a', 70) + $":dependency{i}:1" };
            libraries.Add(library);
            var path = LaunchLibraries.PathFor(root, library);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var emptyJar = ZipFile.Open(path, ZipArchiveMode.Create);
        }
        const string option = "中文 参数 \"quoted\" C:\\mods\\";
        profile["arguments"]!["jvm"] = new JsonArray("-Disland.test.option=" + option);
        const string accessToken = "temporary-auth-fixture-secret";
        var identity = new LaunchIdentity("Fixture", new string('0', 32), accessToken, true);
        foreach (var runtime in new[] { java, legacyJava }.OfType<JavaInstallation>().DistinctBy(runtime => runtime.Path))
        {
            var plan = GameLauncher.Prepare(root, fixture with { Profile = profile }, runtime.Path, 512, identity, javaMajor: runtime.Major);
            try
            {
                check(plan.Arguments.Any(argument => argument.Length > 32_767) &&
                    plan.StartInfo.ArgumentList.All(argument => argument.Length < 1024), $"Java {runtime.Major} launch keeps the Windows command line short");
                if (runtime.Major >= 9)
                    check(Directory.GetFiles(plan.NativesDirectory, "*.args").Length == 1 &&
                        Directory.GetFiles(plan.NativesDirectory, "*.args").All(path => !File.ReadAllText(path).Contains(accessToken)),
                        "Long JVM options use an argument file without writing the game access token");
                else
                    check(Directory.GetFiles(plan.NativesDirectory, "class-path-*.jar").Length == 1, "Java 8 uses a local classpath manifest");
                using var process = Process.Start(plan.StartInfo)!;
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch { if (!process.HasExited) process.Kill(true); throw; }
                var output = await stdout + await stderr;
                var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(option));
                if (process.ExitCode != 0 || !output.Contains("ISLAND_JVM_STARTED") || !output.Contains("CUSTOM_BASE64=" + encoded) ||
                    !output.Contains("ARG=" + accessToken))
                    throw new Exception($"Long classpath fixture failed on Java {runtime.Major} (exit {process.ExitCode}): {output}");
                check(true, $"Java {runtime.Major} loads the last JAR and preserves Unicode, quoted flags and game arguments");
            }
            finally { await GameLauncher.CleanupNativesAsync(root, plan.NativesDirectory); }
        }
    }

    private static async Task VerifyClientAndLoaderRepairAsync(string root, Action<bool, string> check)
    {
        var game = Path.Combine(root, "repair-launch-jars");
        string WriteVersion(string id, JsonObject profile)
        {
            var directory = Path.Combine(game, "versions", id);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, id + ".json"), profile.ToJsonString());
            return Path.Combine(directory, id + ".jar");
        }
        var vanilla = Encoding.UTF8.GetBytes("original client");
        var altered = Encoding.UTF8.GetBytes("patched client!");
        JsonObject Artifact(byte[] bytes, string url) => new()
        {
            ["url"] = url, ["size"] = bytes.LongLength, ["sha1"] = Convert.ToHexString(SHA1.HashData(bytes))
        };
        const string minecraft = "1.20.1";
        var original = WriteVersion(minecraft, new JsonObject
        {
            ["id"] = minecraft, ["mainClass"] = "Fixture",
            ["downloads"] = new JsonObject { ["client"] = Artifact(vanilla, "https://fixture.test/client") }
        });
        var alternative = WriteVersion("alternate", new JsonObject());
        File.WriteAllBytes(original, altered);
        File.WriteAllBytes(alternative, altered);
        WriteVersion("custom", new JsonObject { ["inheritsFrom"] = minecraft, ["jar"] = "alternate" });
        WriteVersion("explicit-base", new JsonObject { ["inheritsFrom"] = minecraft, ["jar"] = minecraft });
        var handler = new FixtureHandler(vanilla);
        var settings = new AppSettings { GameDirectory = game, Source = DownloadSource.Official };
        using var network = new DownloadClient(settings, handler);
        await GameLauncher.RepairAsync(game, VersionProfile.Read(game, "custom"), network, null, default);
        check(handler.Requests == 0 && File.ReadAllBytes(alternative).SequenceEqual(altered), "Inherited download metadata cannot overwrite a selected custom client");
        await GameLauncher.RepairAsync(game, VersionProfile.Read(game, "explicit-base"), network, null, default);
        check(handler.Requests == 1 && File.ReadAllBytes(original).SequenceEqual(vanilla), "An explicit reference to the original client still repairs it with its own descriptor");

        const string id = "forge-fixture";
        const string loaderVersion = "47.4.10";
        var generated = Artifact(vanilla, "");
        var loaderLibrary = new JsonObject
        {
            ["name"] = $"net.minecraftforge:forge:{minecraft}-{loaderVersion}:client",
            ["downloads"] = new JsonObject { ["artifact"] = generated }
        };
        var child = new JsonObject { ["inheritsFrom"] = minecraft, ["libraries"] = new JsonArray(loaderLibrary) };
        var jar = LaunchLibraries.PathFor(game, loaderLibrary);
        Directory.CreateDirectory(Path.GetDirectoryName(jar)!);
        File.WriteAllBytes(jar, altered);
        var marker = Path.Combine(Path.GetDirectoryName(WriteVersion(id, child))!, "blockdock-instance.json");
        var resolved = VersionProfile.Read(game, id);
        try
        {
            await GameLauncher.RepairAsync(game, resolved, network, null, default);
            throw new Exception("Damaged imported loader JAR passed validation.");
        }
        catch (LaunchFileVerifier.InvalidLocalFile error)
        {
            check(error.FilePath == jar && handler.Requests == 1, "A corrupt local loader JAR stops preparation without fetching an unrelated file");
        }

        File.WriteAllText(marker, $$"""{"minecraft":"{{minecraft}}","loader":"Forge","loaderVersion":"{{loaderVersion}}"}""");
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Entry(string name, byte[] bytes) { using var stream = archive.CreateEntry(name).Open(); stream.Write(bytes); }
            var coordinate = loaderLibrary.Str("name");
            var hash = generated.Str("sha1");
            Entry("install_profile.json", Encoding.UTF8.GetBytes($$"""
                {"spec":1,"minecraft":"{{minecraft}}","json":"/version.json",
                 "data":{"PATCHED":{"client":"[{{coordinate}}]"},"PATCHED_SHA":{"client":"'{{hash}}'"} } }
                """));
            Entry("version.json", Encoding.UTF8.GetBytes(child.ToJsonString()));
            Entry("maven/" + SafePaths.Maven(loaderLibrary.Str("name")), vanilla);
        }
        var installer = new InstallerHandler(buffer.ToArray());
        using var installerNetwork = new DownloadClient(settings, installer);
        await GameLauncher.RepairAsync(game, resolved, installerNetwork, null, default);
        check(installer.Downloads == 1 && File.ReadAllBytes(jar).SequenceEqual(vanilla), "An installed Forge instance restores a damaged dependency from its original installer");
        await GameLauncher.RepairAsync(game, resolved, installerNetwork, null, default);
        check(installer.Downloads == 1, "A verified loader dependency does not run the installer again");

        child.Remove("libraries");
        WriteVersion(id, child);
        File.WriteAllBytes(jar, []);
        await GameLauncher.RepairAsync(game, VersionProfile.Read(game, id), installerNetwork, null, default);
        check(installer.Downloads == 1 && File.ReadAllBytes(jar).SequenceEqual(vanilla),
            "A cached installer restores a processor output omitted from the launch profile");

        var cacheDirectory = Path.Combine(game, ".blockdock", "cache", "installers");
        var installerName = $"forge-{minecraft}-{loaderVersion}-installer.jar";
        File.Move(Path.Combine(cacheDirectory, "forge", installerName), Path.Combine(cacheDirectory, installerName));
        var forge = new ForgeInstaller(settings, installerNetwork);
        var request = new InstallRequest(minecraft, id, LoaderKind.Forge, loaderVersion);
        check((await forge.ReadCachedGeneratedLibrariesAsync(request, default)).Single().Path == jar &&
            (await forge.ReadCachedGeneratedLibrariesAsync(request with { Loader = LoaderKind.NeoForge }, default)).Count == 0,
            "The old shared installer cache is used only when its Maven group matches the loader");

        var duplicate = Path.Combine(game, "duplicate.jar");
        using (var archive = ZipFile.Open(duplicate, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("fixture.txt").Open());
            writer.Write("structurally valid but incorrect JAR");
        }
        await using var verifier = new LaunchFileVerifier(game, installerNetwork);
        try
        {
            await verifier.EnsureManyAsync([new(duplicate, []), new(duplicate, [], Sha1: generated.Str("sha1"))], "", null, default);
            throw new Exception("A hashless duplicate masked the generated library checksum.");
        }
        catch (LaunchFileVerifier.InvalidLocalFile error)
        {
            check(error.FilePath == duplicate, "A duplicate local library descriptor cannot hide the generated file checksum");
        }

        var legacy = JsonNode.Parse("""
            {"mainClass":"net.minecraft.launchwrapper.Launch","libraries":[
              {"name":"net.minecraftforge:forge:1.7.10-10.13.4.1614-1.7.10","url":"https://maven.minecraftforge.net/"}
            ]}
            """)!.AsObject();
        var legacyFile = LaunchLibraries.Files(game, legacy).Single();
        check(legacyFile.Urls.Count == 2 && legacyFile.Urls[1].EndsWith("-universal.jar") &&
            Path.GetFileName(legacyFile.Path) == Path.GetFileName(new Uri(legacyFile.Urls[0]).AbsolutePath),
            "Legacy Forge can download the universal JAR into the path used by its launch configuration");
    }

    public static async Task RunAsync(string root, Action<bool, string> check)
    {
        await VerifyClientAndLoaderRepairAsync(root, check);
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

    private sealed class InstallerHandler(byte[] bytes) : HttpMessageHandler
    {
        public int Downloads { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.Host != "maven.minecraftforge.net") throw new InvalidOperationException("Unexpected repair download: " + uri);
            if (uri.AbsolutePath.EndsWith(".sha1", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Convert.ToHexString(SHA1.HashData(bytes))) });
            Downloads++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }
}
