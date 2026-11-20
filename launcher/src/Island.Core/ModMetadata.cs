using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Island.Core;

public sealed record ModMetadata(string Id, string Name, string Version, string Description, byte[]? Icon, string Homepage = "");

public static class ModMetadataReader
{
    private static string Value(JsonNode? node, string name) => node is JsonObject data && data[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
    public static IReadOnlyList<ModMetadata> Read(string path, bool includeIcons = false)
    {
        var found = new List<ModMetadata>();
        try
        {
            using var archive = ZipFile.OpenRead(path);
            string Read(string name)
            {
                var entry = archive.GetEntry(name);
                if (entry is null || entry.Length > 1024 * 1024) return "";
                using var reader = new StreamReader(entry.Open()); return reader.ReadToEnd();
            }
            byte[]? Icon(string name)
            {
                if (!includeIcons || name.Length == 0 || archive.GetEntry(name.TrimStart('/')) is not { Length: > 0 and <= 1024 * 1024 } entry) return null;
                try
                {
                    var bytes = new byte[(int)entry.Length];
                    using var stream = entry.Open();
                    stream.ReadExactly(bytes);
                    return bytes;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException) { return null; }
            }
            string JsonIcon(JsonNode? icon) => icon is JsonObject images
                ? images.OrderByDescending(entry => int.TryParse(entry.Key, out var size) ? size : 0)
                    .Select(entry => entry.Value).OfType<JsonValue>().FirstOrDefault()?.ToString() ?? ""
                : icon is JsonValue value && value.TryGetValue<string>(out var path) ? path : "";
            foreach (var name in new[] { "fabric.mod.json", "quilt.mod.json", "mcmod.info" })
            {
                var text = Read(name);
                if (text.Length == 0) continue;
                try
                {
                    var data = JsonNode.Parse(text);
                    switch (name)
                    {
                        case "fabric.mod.json" when data is JsonObject fabric:
                            found.Add(new(Value(fabric, "id"), Value(fabric, "name"), Value(fabric, "version"),
                                Value(fabric, "description"), Icon(JsonIcon(fabric["icon"])), Value(fabric["contact"], "homepage")));
                            break;
                        case "quilt.mod.json" when data is JsonObject quilt && quilt["quilt_loader"] is JsonObject mod:
                            var metadata = mod["metadata"] as JsonObject;
                            found.Add(new(Value(mod, "id"), Value(metadata, "name"), Value(mod, "version"),
                                Value(metadata, "description"), Icon(JsonIcon(metadata?["icon"])), Value(metadata?["contact"], "homepage")));
                            break;
                        case "mcmod.info":
                            var entries = data as JsonArray ?? (data as JsonObject)?["modList"] as JsonArray ?? [];
                            foreach (var entry in entries.OfType<JsonObject>())
                                found.Add(new(Value(entry, "modid"), Value(entry, "name"), Value(entry, "version"),
                                    Value(entry, "description"), Icon(Value(entry, "logoFile")), Value(entry, "url")));
                            break;
                    }
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { }
            }
            foreach (var name in new[] { "META-INF/mods.toml", "META-INF/neoforge.mods.toml" })
            {
                var text = Read(name);
                foreach (Match section in Regex.Matches(text, @"(?ms)^[ \t]*\[\[mods\]\][ \t]*\r?$(.*?)(?=^\s*\[|\z)"))
                {
                    string Field(string key) => Regex.Match(section.Groups[1].Value, @"(?m)^\s*" + key + "\\s*=\\s*[\"']([^\"'\\r\\n]*)[\"']").Groups[1].Value;
                    var version = Field("version");
                    if (version == "${file.jarVersion}")
                        version = Regex.Match(Read("META-INF/MANIFEST.MF"), @"(?im)^Implementation-Version:[ \t]*(.+)\r?$").Groups[1].Value.Trim();
                    var description = Regex.Match(section.Groups[1].Value, "(?ms)^\\s*description[ \t]*=[ \t]*(?:'''(.*?)'''|\"\"\"(.*?)\"\"\")");
                    var summary = description.Success ? description.Groups[1].Value + description.Groups[2].Value : Field("description");
                    found.Add(new(Field("modId"), Field("displayName"), version, summary, Icon(Field("logoFile")), Field("displayURL")));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { }
        return found;
    }
}
