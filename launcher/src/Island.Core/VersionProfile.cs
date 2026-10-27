using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BlockDock.Core;

namespace Island.Core;

public sealed record ResolvedVersion(string Id, JsonObject Profile, string ClientJar, IReadOnlyList<JsonObject> Chain);

public static class VersionProfile
{
    public static ResolvedVersion Read(string root, string id, VersionIsolation isolation = VersionIsolation.Auto)
    {
        var chain = new List<JsonObject>();
        var enclosingIds = new Dictionary<JsonObject, string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string name)
        {
            SafePaths.InstanceName(name);
            if (visited.Count >= 20 || !visited.Add(name)) throw new InvalidDataException("版本继承出现循环或层数过多。");
            var path = SafePaths.Under(root, $"versions/{name}/{name}.json");
            if (!File.Exists(path)) throw new FileNotFoundException($"缺少父版本 {name}。请先安装对应原版或重新导入完整游戏目录。", path);
            var json = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new InvalidDataException("版本 JSON 无效。");
            if (json.Str("inheritsFrom") is { Length: > 0 } parent) Visit(parent);
            enclosingIds[json] = name;
            chain.Add(json);
        }
        Visit(id);
        var merged = new JsonObject();
        var libraries = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var game = new JsonArray(); var jvm = new JsonArray();
        string? client = null;
        var minimumJava = 8;
        foreach (var json in chain)
        {
            foreach (var (key, value) in json)
                if (key is not ("libraries" or "arguments" or "inheritsFrom")) merged[key] = value?.DeepClone();
            foreach (var library in json.Array("libraries").OfType<JsonObject>())
            {
                var parts = library.Str("name").Split(':');
                var key = parts.Length >= 3 ? parts[0] + ":" + parts[1] + ":" + (parts.Length > 3 ? parts[3] : "") : library.ToJsonString();
                libraries[key] = (JsonObject)library.DeepClone();
            }
            foreach (var argument in json["arguments"].Array("game")) game.Add(argument?.DeepClone());
            foreach (var argument in json["arguments"].Array("jvm")) jvm.Add(argument?.DeepClone());
            minimumJava = Math.Max(minimumJava, JavaRequirement.FromMetadata(json).MinimumMajor);
            var jarId = json.Str("jar", enclosingIds[json]);
            if (json["downloads"]?["client"] is JsonObject || json.Str("jar").Length > 0 || chain.Count == 1)
                client = SafePaths.Under(root, $"versions/{SafePaths.InstanceName(jarId)}/{jarId}.jar");
            else if (client is null)
            {
                var candidate = SafePaths.Under(root, $"versions/{SafePaths.InstanceName(jarId)}/{jarId}.jar");
                if (File.Exists(candidate)) client = candidate;
            }
        }
        // Imported profiles can use an ID different from their enclosing directory.
        var ownJar = SafePaths.Under(root, $"versions/{id}/{id}.jar");
        if (File.Exists(ownJar) && chain[^1]["downloads"]?["client"] is JsonObject) client = ownJar;
        merged["libraries"] = new JsonArray(libraries.Values.Select(x => (JsonNode)x).ToArray());
        merged["arguments"] = new JsonObject { ["game"] = game, ["jvm"] = jvm };
        merged["javaVersion"] = new JsonObject { ["majorVersion"] = minimumJava };
        merged["javaVersion"]!["majorVersion"] = LaunchLibraries.MinimumJava(root, merged, GameDirectories.Resolve(root, id, isolation).Directory);
        if (string.IsNullOrWhiteSpace(merged.Str("mainClass"))) throw new InvalidDataException("版本缺少 mainClass，无法启动。");
        return new(id, merged, client ?? ownJar, chain);
    }

    public static bool Allowed(JsonObject item) => Allowed(item, null);
    public static bool Allowed(JsonObject item, IReadOnlyDictionary<string, bool>? enabledFeatures)
    {
        if (item["rules"] is not JsonArray rules) return true;
        var allowed = false;
        foreach (var rule in rules.OfType<JsonObject>())
        {
            var os = rule["os"];
            if (os is not null)
            {
                if (os.Str("name", "windows") != "windows") continue;
                if (os.Str("arch") is { Length: > 0 } arch && arch != "x86_64" && arch != "amd64") continue;
                if (os.Str("version") is { Length: > 0 } pattern && !Regex.IsMatch(Environment.OSVersion.Version.ToString(), pattern, RegexOptions.None, TimeSpan.FromSeconds(1))) continue;
            }
            if (rule["features"] is JsonObject features && features.Any(pair =>
                (enabledFeatures?.TryGetValue(pair.Key, out var active) == true && active) != (pair.Value?.GetValue<bool>() == true))) continue;
            allowed = rule.Str("action") == "allow";
        }
        return allowed;
    }

    public static IReadOnlyList<string> SplitArguments(string text)
    {
        var result = new List<string>(); var current = new System.Text.StringBuilder();
        var quoted = false; var started = false;
        foreach (var c in text)
        {
            if (c == '"') { quoted = !quoted; started = true; }
            else if (char.IsWhiteSpace(c) && !quoted) { if (started) { result.Add(current.ToString()); current.Clear(); started = false; } }
            else { current.Append(c); started = true; }
        }
        if (quoted) throw new InvalidDataException("启动参数引号未闭合。");
        if (started) result.Add(current.ToString());
        return result;
    }
}
