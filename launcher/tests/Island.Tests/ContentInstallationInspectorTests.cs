using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Island.Core;

internal static class ContentInstallationInspectorTests
{
    private static byte[] Jar(string entryName, string text)
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        { using var writer = new StreamWriter(zip.CreateEntry(entryName).Open()); writer.Write(text); }
        return bytes.ToArray();
    }
    public static async Task RunAsync(string root, Action<bool, string> check)
    {
        var index = 0;
        ContentTarget Target()
        {
            var folder = Path.Combine(root, "dependency-inspection", (++index).ToString()); Directory.CreateDirectory(Path.Combine(folder, "mods"));
            return new(folder, "fixture", "fixture", "1.20.1", "Fabric", folder, VersionIsolation.Enabled);
        }
        ContentFile File(byte[] bytes, string id = "sample-dependency") => new(id, "expected-v2", ContentKind.Mod, "Sample Dependency", "sample-dependency-v2.jar", "https://fixture.invalid/dependency", bytes.Length,
            Convert.ToHexString(SHA1.HashData(bytes)), Convert.ToHexString(SHA512.HashData(bytes))) { IsDependency = true, ProjectSlug = id, ReleaseVersion = "2.0" };
        async Task<ContentInstallationMatch?> Inspect(ContentTarget target, ContentFile file) => (await ContentInstallationInspector.InspectAsync(new(target, "Fixture", null, [file]), default)).SingleOrDefault();
        var bytes = Jar("fabric.mod.json", "{\"schemaVersion\":1,\"id\":\"sample_dependency\",\"name\":\"Sample Dependency\",\"version\":\"2.0\"}");
        var dependency = File(bytes);
        foreach (var filename in new[] { "[前置]sample-dependency-v2.jar", "[前置][API]已经改名.jar", "【前置】完全不同的名字.JAR" })
        {
            var target = Target(); var path = Path.Combine(target.RuntimeDirectory, "mods", filename); await System.IO.File.WriteAllBytesAsync(path, bytes);
            var match = await Inspect(target, dependency);
            check(match is { Confirmed: true } && match.Path == path, "Dependency hashes identify installed files independently of prefix or filename: " + filename);
        }
        foreach (var (entry, metadata) in new[]
        {
            ("fabric.mod.json", "{\"id\":\"sample_dependency\",\"name\":\"Sample Dependency\",\"version\":\"1.0\"}"),
            ("quilt.mod.json", "{\"quilt_loader\":{\"id\":\"sample_dependency\",\"version\":\"1.0\",\"metadata\":{\"name\":\"Sample Dependency\"}}}"),
            ("META-INF/mods.toml", "modLoader=\"javafml\"\n[[mods]]\nmodId = \"sample_dependency\"\nversion = \"1.0\"\ndisplayName = \"Sample Dependency\"\n[[dependencies.sample_dependency]]\nmodId=\"unrelated\"\n"),
            ("META-INF/neoforge.mods.toml", "[[mods]]\r\nmodId='sample_dependency'\r\nversion='1.0'\r\ndisplayName='Sample Dependency'\r\n"),
            ("mcmod.info", "[{\"modid\":\"sample_dependency\",\"name\":\"Sample Dependency\",\"version\":\"1.0\"}]")
        })
        {
            var target = Target(); await System.IO.File.WriteAllBytesAsync(Path.Combine(target.RuntimeDirectory, "mods", "用户改过名字.jar"), Jar(entry, metadata));
            var match = await Inspect(target, dependency);
            check(match is { Kind: ContentMatchKind.ModMetadata, Confirmed: false, Version: "1.0" }, "Renamed JAR metadata is detected without claiming an unverified version is identical: " + entry);
        }
        {
            var target = Target(); await System.IO.File.WriteAllTextAsync(Path.Combine(target.RuntimeDirectory, "mods", "[前置]sample-dependency-v2.jar"), "different bytes");
            check(await Inspect(target, dependency) is { Kind: ContentMatchKind.FileName, Confirmed: false }, "A labeled matching filename alone is only a possible match and never auto-skips installation");
        }
        {
            var target = Target(); await System.IO.File.WriteAllBytesAsync(Path.Combine(target.RuntimeDirectory, "mods", "[前置]renamed.jar.disabled"), bytes);
            check(await Inspect(target, dependency) is { Enabled: false, Confirmed: false }, "Disabled dependency JARs do not count as enabled installed prerequisites");
        }
        {
            var target = Target(); var oldBytes = Encoding.UTF8.GetBytes("old registered dependency"); var old = File(oldBytes) with { VersionId = "old-v1" };
            Directory.CreateDirectory(Path.Combine(target.RuntimeDirectory, ".island"));
            await System.IO.File.WriteAllTextAsync(Path.Combine(target.RuntimeDirectory, ".island", "content.json"), JsonSerializer.Serialize(new[] { old }));
            await System.IO.File.WriteAllBytesAsync(Path.Combine(target.RuntimeDirectory, "mods", "renamed-old.jar"), oldBytes);
            check(await Inspect(target, dependency) is { Kind: ContentMatchKind.ManagedOtherVersion, Confirmed: false }, "Registered older dependencies remain recognizable after renaming without treating them as the requested version");
        }
        {
            var target = Target(); await System.IO.File.WriteAllBytesAsync(Path.Combine(target.RuntimeDirectory, "mods", "sample-dependency-v2.jar.backup"), bytes);
            await System.IO.File.WriteAllBytesAsync(Path.Combine(target.RuntimeDirectory, "mods", "broken-metadata.jar"), Jar("fabric.mod.json", "[\"invalid shape\"]"));
            check(await Inspect(target, dependency) is null, "Backup files and malformed metadata do not produce false installed-dependency matches or abort detection");
        }
        {
            var target = Target(); var path = Path.Combine(target.RuntimeDirectory, "mods", dependency.FileName); await System.IO.File.WriteAllBytesAsync(path, bytes);
            using var guard = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            check(await Inspect(target, dependency) is { Kind: ContentMatchKind.Unreadable, Confirmed: false }, "Unreadable files stay unverified so users can decide whether to install the dependency");
        }
        {
            var target = Target(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            try { await ContentInstallationInspector.InspectAsync(new(target, "Fixture", null, [dependency]), cancellation.Token); throw new Exception("Expected canceled detection."); }
            catch (OperationCanceledException) { check(true, "Closing an installation preview can cancel dependency detection"); }
        }
    }
}
