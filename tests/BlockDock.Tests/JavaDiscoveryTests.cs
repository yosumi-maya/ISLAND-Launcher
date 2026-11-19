using System.Diagnostics;
using System.Text.Json.Nodes;
using BlockDock.Core;

internal static class JavaDiscoveryTests
{
    public static void AddTo(List<(string Name, Func<Task> Test)> tests, string root)
    {
        tests.Add(("Java 实际探测识别旧版、现代版和 EA 输出", async () =>
        {
            foreach (var (version, major) in new[] { ("1.8.0_441", 8), ("17.0.15", 17), ("21.0.8+9-LTS", 21), ("25-ea", 25) })
            {
                var java = JavaProbeFixture.Create(Path.Combine(root, "java-versions", major.ToString()), version);
                var found = await JavaDiscovery.ProbeAsync(java, default);
                Check(found is not null && found.Major == major && found.Architecture == "x64" && found.Version == version && found.Vendor == "Fixture OpenJDK", "应识别真实子进程输出 " + version);
            }
        }));
        tests.Add(("零退出但没有 Java 信息的程序不算 Java", async () =>
        {
            var java = JavaProbeFixture.Create(Path.Combine(root, "java-invalid"), mode: "invalid");
            Check(await JavaDiscovery.ProbeAsync(java, default) is null, "仅存在可执行文件和退出码 0 不足以证明 Java 可用");
        }));
        tests.Add(("Java 探测超时会结束子进程", async () =>
        {
            var java = JavaProbeFixture.Create(Path.Combine(root, "java-timeout"), mode: "slow");
            var clock = Stopwatch.StartNew();
            Check(await JavaDiscovery.ProbeAsync(java, default, TimeSpan.FromMilliseconds(800)) is null, "超时程序应被排除");
            Check(clock.Elapsed < TimeSpan.FromSeconds(4), "不能等待悬挂的探测程序");
            var pidFile = Path.Combine(Path.GetDirectoryName(java)!, "probe.pid");
            if (File.Exists(pidFile)) Check(!JavaProbeFixture.IsAlive(int.Parse(File.ReadAllText(pidFile))), "超时后不能遗留探测进程");
        }));
        tests.Add(("取消 Java 检测会结束自己的子进程", async () =>
        {
            var java = JavaProbeFixture.Create(Path.Combine(root, "java-cancel"), mode: "slow");
            using var cancellation = new CancellationTokenSource();
            var probe = JavaDiscovery.ProbeAsync(java, cancellation.Token);
            var pidFile = Path.Combine(Path.GetDirectoryName(java)!, "probe.pid");
            for (var i = 0; i < 100 && !File.Exists(pidFile); i++) await Task.Delay(20);
            Check(File.Exists(pidFile), "探测子进程应已启动");
            cancellation.Cancel();
            try { await probe; throw new Exception("取消应返回取消状态"); } catch (OperationCanceledException) { }
            Check(!JavaProbeFixture.IsAlive(int.Parse(File.ReadAllText(pidFile))), "取消后不能遗留探测进程");
        }));
        tests.Add(("Java 搜索去重并跳过模组和过深目录", () =>
        {
            var home = Path.Combine(root, "java-paths");
            var java = JavaProbeFixture.Create(Path.Combine(home, "runtime", "component", "windows-x64", "jdk"));
            var ignored = JavaProbeFixture.Create(Path.Combine(home, "mods", "embedded"));
            var deep = JavaProbeFixture.Create(Path.Combine(home, "a", "b", "c", "d", "e", "f", "g"));
            var javaw = Path.Combine(Path.GetDirectoryName(java)!, "javaw.exe"); File.Copy(java, javaw);
            var paths = JavaDiscovery.CollectCandidates(new([java, java.ToUpperInvariant(), javaw], [home]), default);
            Check(paths.Count(p => p.Equals(java, StringComparison.OrdinalIgnoreCase)) == 1, "重复地址和 javaw 别名应归为同一 java.exe");
            Check(!paths.Contains(ignored) && !paths.Contains(deep), "不能递归扫描模组或超出深度限制");
            var aliases = JavaDiscovery.CollectCandidates(new([java, Path.GetDirectoryName(Path.GetDirectoryName(java))!], []), default);
            Check(aliases.Count == 1, "Java Home 与 bin/java.exe 的路径分隔符应统一后去重");
            return Task.CompletedTask;
        }));
        tests.Add(("选择 Java 优先同主版本补丁而不是最新主版本", () =>
        {
            var choices = new[] { Runtime("newest", "25", 25), Runtime("old-patch", "17.0.2", 17), Runtime("preferred", "17.0.15", 17), Runtime("ea", "17-ea", 17) };
            Check(JavaDiscovery.Select(choices, new(17)).Path == "preferred", "应选择稳定的 Java 17 最新补丁");
            Check(JavaDiscovery.Select([Runtime("21", "21.0.8", 21), Runtime("25", "25", 25)], new(17)).Path == "21", "缺少同主版本时选择满足最低要求的较低主版本");
            return Task.CompletedTask;
        }));
        tests.Add(("Java 选择排除 32 位及低于元数据要求的运行时", () =>
        {
            Check(JavaDiscovery.Select([Runtime("32-bit", "21.0.8", 21, "x86"), Runtime("old", "8", 8), Runtime("valid", "21.0.1", 21)], new(21)).Path == "valid", "不能优先选 32 位或旧 Java");
            try { JavaDiscovery.Select([Runtime("old", "8", 8)], new(17)); throw new Exception("应该拒绝不满足要求的 Java"); } catch (InvalidOperationException ex) { Check(ex.Message.Contains("17"), "应说明要求的 Java 主版本"); }
            return Task.CompletedTask;
        }));
        tests.Add(("游戏和加载器的 Java 元数据取较高要求", () =>
        {
            var game = JsonNode.Parse("""{"javaVersion":{"majorVersion":21}}""")!.AsObject();
            var loader = JsonNode.Parse("""{"javaVersion":{"majorVersion":25}}""")!.AsObject();
            Check(JavaRequirement.FromMetadata(game, loader).MinimumMajor == 25, "加载器可提高最低要求");
            Check(JavaRequirement.FromMetadata(loader, game).MinimumMajor == 25, "加载器不能降低游戏声明的要求");
            Check(JavaRequirement.FromMetadata(new()).MinimumMajor == 8, "无声明的旧版元数据使用 Java 8");
            return Task.CompletedTask;
        }));
        tests.Add(("手动指定的 Java 也必须满足安装要求", async () =>
        {
            var java = JavaProbeFixture.Create(Path.Combine(root, "java-manual"), "1.8.0_441");
            try { await JavaDiscovery.ResolveAsync(new() { GameDirectory = root, JavaPath = java }, new(17), default); throw new Exception("应该拒绝手动指定的不兼容 Java"); }
            catch (InvalidOperationException ex) { Check(ex.Message.Contains("17") && ex.Message.Contains("8"), "应说明当前版本和所需版本"); }
        }));
        tests.Add(("无处理器的旧版 Forge 安装不依赖 Java", async () =>
        {
            var game = Path.Combine(root, "legacy-forge-no-java");
            var jar = Path.Combine(root, "legacy-forge-installer.jar");
            using (var archive = System.IO.Compression.ZipFile.Open(jar, System.IO.Compression.ZipArchiveMode.Create))
            {
                await using (var writer = new StreamWriter(archive.CreateEntry("install_profile.json").Open()))
                    await writer.WriteAsync("""{"install":{"minecraft":"1.7.10","path":"net.minecraftforge:forge:1.7.10-test","filePath":"forge.jar"},"versionInfo":{"id":"legacy","libraries":[]}}""");
                await using var payload = new StreamWriter(archive.CreateEntry("forge.jar").Open()); await payload.WriteAsync("legacy fixture");
            }
            var settings = new AppSettings { GameDirectory = game, JavaPath = Path.Combine(root, "nonexistent", "java.exe") };
            var bytes = await File.ReadAllBytesAsync(jar);
            using var network = new DownloadClient(settings, new FakeHandler(request => request.RequestUri!.AbsolutePath.EndsWith(".sha1")
                ? new(System.Net.HttpStatusCode.OK) { Content = new StringContent(Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(bytes))) }
                : new(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
            var profile = await new ForgeInstaller(settings, network).InstallAsync(new("1.7.10", "Legacy", LoaderKind.Forge, "test"), new(), Path.Combine(game, "work"), null, default);
            Check(profile["id"]!.GetValue<string>() == "legacy" && File.Exists(Path.Combine(game, "libraries", "net", "minecraftforge", "forge", "1.7.10-test", "forge-1.7.10-test.jar")), "仅解压库文件时不能预先阻止安装");
        }));
        tests.Add(("现代 Forge 与 NeoForge 处理器使用实际验证的 Java", async () =>
        {
            foreach (var kind in new[] { LoaderKind.Forge, LoaderKind.NeoForge })
            {
                var caseRoot = Path.Combine(root, "modern-java-" + kind);
                var java = JavaProbeFixture.Create(Path.Combine(caseRoot, "runtime"));
                var game = Path.Combine(caseRoot, "game"); var work = Path.Combine(caseRoot, "work"); Directory.CreateDirectory(work);
                var jar = Path.Combine(caseRoot, "installer.jar");
                var profile = new JsonObject { ["minecraft"] = "1.21.1", ["spec"] = 1, ["json"] = "/version.json", ["processors"] = new JsonArray(new JsonObject { ["jar"] = "test:processor:1", ["args"] = new JsonArray("fixture argument with spaces") }) };
                using (var processor = new MemoryStream())
                {
                    using (var embedded = new System.IO.Compression.ZipArchive(processor, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
                    { await using var manifest = new StreamWriter(embedded.CreateEntry("META-INF/MANIFEST.MF").Open()); await manifest.WriteAsync("Main-Class: fixture.Main\r\n"); }
                    using var installer = System.IO.Compression.ZipFile.Open(jar, System.IO.Compression.ZipArchiveMode.Create);
                    await using (var writer = new StreamWriter(installer.CreateEntry("install_profile.json").Open())) await writer.WriteAsync(profile.ToJsonString());
                    await using (var writer = new StreamWriter(installer.CreateEntry("version.json").Open())) await writer.WriteAsync("""{"id":"modern","javaVersion":{"majorVersion":21},"libraries":[]}""");
                    await using var entry = installer.CreateEntry("maven/test/processor/1/processor-1.jar").Open(); await entry.WriteAsync(processor.ToArray());
                }
                var bytes = await File.ReadAllBytesAsync(jar);
                var settings = new AppSettings { GameDirectory = game, JavaPath = java };
                using var network = new DownloadClient(settings, new FakeHandler(request => request.RequestUri!.AbsolutePath.EndsWith(".sha1")
                    ? new(System.Net.HttpStatusCode.OK) { Content = new StringContent(Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(bytes))) }
                    : new(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
                var result = await new ForgeInstaller(settings, network).InstallAsync(new("1.21.1", "Modern", kind, "test"), new(), work, null, default);
                var args = System.Text.Json.JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(java)!, "processor-args.json")))!;
                Check(result["id"]!.GetValue<string>() == "modern" && args[^2] == "fixture.Main" && args[^1] == "fixture argument with spaces", "应把经过版本检查的 Java 路径用于实际处理器，并正确传递带空格的参数");
            }
        }));
    }
    private static JavaInstallation Runtime(string path, string version, int major, string arch = "x64") => new(path, version, major, arch, "Fixture");
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
