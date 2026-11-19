using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace BlockDock.Core;

// MultiMC components are ordered profile patches, not additional mod loaders.
public static class MultiMcProfile
{
    public static async Task<IReadOnlyList<JsonObject>> ReadPatchesAsync(ZipArchive archive, string prefix, JsonArray components, CancellationToken token)
    {
        if (components.Count > 128) throw new InvalidDataException("整合包组件数量过多。");
        var patches = new List<JsonObject>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var component in components)
        {
            token.ThrowIfCancellationRequested();
            var uid = SafePaths.InstanceName(component.Str("uid"));
            if (!ids.Add(uid)) throw new InvalidDataException("整合包重复声明组件：" + uid);
            if (archive.GetEntry(prefix + "patches/" + uid + ".json") is not { } entry) continue;
            if (entry.Length > 16 * 1024 * 1024) throw new InvalidDataException("组件补丁体积异常。");
            using var reader = new StreamReader(entry.Open());
            var patch = JsonNode.Parse(await reader.ReadToEndAsync(token)) as JsonObject ?? throw new InvalidDataException("组件补丁不是 JSON 对象。");
            if (patch.Str("uid") != uid || patch["formatVersion"]?.GetValue<int>() is not (null or 1)) throw new InvalidDataException("组件补丁标识或格式不匹配：" + uid);
            if (component.Str("version") is { Length: > 0 } version && patch.Str("version") != version) throw new InvalidDataException("组件补丁版本不匹配：" + uid);
            if (patch.Array("jarMods").Count > 0 || patch.Array("+jarMods").Count > 0 || patch.Array("-libraries").Count > 0)
                throw new NotSupportedException("暂不支持此组件的 JAR 替换或库移除指令：" + uid);
            foreach (var library in patch.Array("libraries").Concat(patch.Array("+libraries")).OfType<JsonObject>())
            {
                NormalizeNative(library);
                var path = SafePaths.Maven(library.Str("name"));
                if (library.Str("MMC-hint") == "local")
                {
                    var local = LocalEntry(archive, prefix, path);
                    await using var stream = local.Open();
                    library["downloads"] = new JsonObject { ["artifact"] = new JsonObject
                    {
                        ["path"] = path, ["size"] = local.Length,
                        ["sha1"] = Convert.ToHexString(await SHA1.HashDataAsync(stream, token)).ToLowerInvariant()
                    }};
                }
            }
            patches.Add(patch);
        }
        return patches.OrderBy(patch => patch["order"]?.GetValue<int>() ?? 0).ToArray();
    }

    private static ZipArchiveEntry LocalEntry(ZipArchive archive, string prefix, string path)
        => archive.GetEntry(prefix + "libraries/" + path) ?? archive.GetEntry(prefix + "libraries/" + Path.GetFileName(path))
            ?? throw new InvalidDataException("整合包缺少内置依赖：" + Path.GetFileName(path));

    private static void NormalizeNative(JsonObject library)
    {
        var parts = library.Str("name").Split(':');
        if (parts.Length < 3) throw new InvalidDataException("组件库坐标无效。");
        // Prism exports native jars as synthetic artifact names. Restore the Maven classifier.
        var nativeIndex = parts[1].IndexOf("-natives-", StringComparison.Ordinal);
        if (parts[0] == "org.lwjgl" && nativeIndex > 0 && parts.Length == 3)
        {
            var classifier = parts[1][(nativeIndex + 1)..];
            library["name"] = parts[0] + ":" + parts[1][..nativeIndex] + ":" + parts[2] + ":" + classifier;
            var architecture = classifier switch { "natives-windows" => "x86_64", "natives-windows-x86" => "x86", "natives-windows-arm64" => "aarch64", _ => "" };
            if (architecture.Length > 0)
                library["rules"] = new JsonArray(new JsonObject { ["action"] = "allow", ["os"] = new JsonObject { ["name"] = "windows", ["arch"] = architecture } });
        }
        var path = SafePaths.Maven(library.Str("name"));
        _ = SafePaths.ResolveUnder(Path.GetTempPath(), "libraries/" + path);
        if (library["downloads"]?["artifact"] is JsonObject artifact && artifact.Str("path").Length == 0) artifact["path"] = path;
    }

    public static JsonObject Compose(PackInfo pack, JsonObject vanilla, string id)
    {
        var profile = (JsonObject)vanilla.DeepClone();
        var libraries = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        void AddLibrary(JsonObject library)
        {
            var parts = library.Str("name").Split(':');
            var key = parts.Length >= 3 ? parts[0] + ":" + parts[1] + ":" + (parts.Length > 3 ? parts[3] : "") : library.ToJsonString();
            libraries[key] = (JsonObject)library.DeepClone();
        }
        if (!pack.ComponentPatches.Any(p => p.Str("uid") == "net.minecraft"))
            foreach (var library in vanilla.Array("libraries").OfType<JsonObject>()) AddLibrary(library);
        var jvm = new JsonArray(); var extraGame = new JsonArray();
        foreach (var argument in vanilla["arguments"].Array("jvm")) jvm.Add(argument?.DeepClone());
        var minimumJava = JavaRequirement.FromMetadata(vanilla).MinimumMajor;
        foreach (var patch in pack.ComponentPatches)
        {
            foreach (var field in new[] { "mainClass", "minecraftArguments", "assetIndex", "assets", "type", "releaseTime", "logging" })
                if (patch[field] is { } value) profile[field] = value.DeepClone();
            if (patch["mainJar"]?["downloads"]?["artifact"] is JsonObject client)
                profile["downloads"] = new JsonObject { ["client"] = client.DeepClone() };
            foreach (var library in patch.Array("libraries").Concat(patch.Array("+libraries")).OfType<JsonObject>()) AddLibrary(library);
            foreach (var argument in patch.Array("+jvmArgs")) jvm.Add(argument?.DeepClone());
            foreach (var argument in patch.Array("+gameArgs")) extraGame.Add(argument?.DeepClone());
            foreach (var tweaker in patch.Array("+tweakers")) { extraGame.Add("--tweakClass"); extraGame.Add(tweaker?.DeepClone()); }
            var majors = patch.Array("compatibleJavaMajors").Select(value => value!.GetValue<int>()).Where(value => value > 0).ToArray();
            if (majors.Length > 0) minimumJava = Math.Max(minimumJava, majors.Min());
            minimumJava = Math.Max(minimumJava, JavaRequirement.FromMetadata(patch).MinimumMajor);
        }
        var game = profile["arguments"].Array("game");
        game = game.Count > 0 ? (JsonArray)game.DeepClone() : new JsonArray(SplitArguments(profile.Str("minecraftArguments")).Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
        foreach (var argument in extraGame) game.Add(argument?.DeepClone());
        profile["arguments"] = new JsonObject { ["jvm"] = jvm, ["game"] = game };
        profile["libraries"] = new JsonArray(libraries.Values.Select(value => (JsonNode)value).ToArray());
        profile["javaVersion"] = new JsonObject { ["majorVersion"] = minimumJava };
        profile["id"] = SafePaths.InstanceName(id); profile["clientVersion"] = pack.MinecraftVersion;
        profile.Remove("inheritsFrom"); profile.Remove("jar");
        return profile;
    }

    private static IReadOnlyList<string> SplitArguments(string text)
    {
        var result = new List<string>(); var value = new StringBuilder(); var quoted = false; var started = false;
        foreach (var c in text)
        {
            if (c == '"') { quoted = !quoted; started = true; }
            else if (char.IsWhiteSpace(c) && !quoted) { if (started) { result.Add(value.ToString()); value.Clear(); started = false; } }
            else { value.Append(c); started = true; }
        }
        if (quoted) throw new InvalidDataException("组件启动参数引号未闭合。");
        if (started) result.Add(value.ToString()); return result;
    }

    public static async Task CopyLocalLibrariesAsync(PackInfo pack, string root, CancellationToken token)
    {
        using var archive = ZipFile.OpenRead(pack.ArchivePath);
        foreach (var library in pack.ComponentPatches.SelectMany(p => p.Array("libraries").Concat(p.Array("+libraries"))).OfType<JsonObject>().Where(lib => lib.Str("MMC-hint") == "local"))
        {
            var artifact = library["downloads"]!.Object("artifact");
            var path = artifact.Str("path"); var target = SafePaths.Under(root, "libraries/" + path);
            var transfer = MinecraftInstaller.FromArtifact(target, artifact);
            if (await DownloadClient.ValidAsync(target, transfer, token)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var input = LocalEntry(archive, pack.Prefix, path).Open())
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                    await input.CopyToAsync(output, token);
                if (!await DownloadClient.ValidAsync(temporary, transfer, token)) throw new InvalidDataException("整合包内置依赖校验失败。");
                File.Move(temporary, target, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
