using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BlockDock.Core;

namespace Island.Core;

// Launcher discovery supplements the shared installer's metadata without changing
// downloader installation choices or writing to imported game directories.
public static class InstanceCatalog
{
    public static IReadOnlyList<InstalledInstance> ListInstalled(string root) => MinecraftInstaller.ListInstalled(root)
        .Select(instance => instance with { Loader = IdentifyLoader(root, instance.Name, instance.Loader), Minecraft = IdentifyMinecraft(root, instance.Name, instance.Minecraft) }).ToArray();

    private static bool IsMinecraftVersion(string value) => Regex.IsMatch(value,
        @"^(?:1\.\d+(?:\.\d+)?|(?:2[5-9]|[3-9]\d)\.\d+(?:\.\d+)?)(?:-(?:pre|rc)\d+)?$|^\d{2}w\d{2}[a-z]$|^[ab]1\.\d+(?:\.\d+)?[a-z]?$|^(?:rd|inf)-\d+$|^c0\.\d+(?:_\d+)?[a-z]?$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));

    private static string IdentifyMinecraft(string root, string id, string fallback)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (visited.Count < 20 && visited.Add(id))
        {
            try
            {
                var path = SafePaths.Under(root, $"versions/{SafePaths.InstanceName(id)}/{id}.json");
                if (!File.Exists(path)) break;
                if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject profile) break;
                foreach (var field in new[] { "clientVersion", "minecraftVersion" })
                    if (Text(profile, field) is { } version && IsMinecraftVersion(version)) return version;
                var arguments = (profile["arguments"]?["game"] as JsonArray ?? []).OfType<JsonValue>()
                    .Where(value => value.TryGetValue<string>(out _)).Select(value => value.GetValue<string>()).ToArray();
                for (var i = 0; i + 1 < arguments.Length; i++)
                    if (arguments[i] == "--fml.mcVersion" && IsMinecraftVersion(arguments[i + 1])) return arguments[i + 1];
                foreach (var patch in (profile["patches"] as JsonArray ?? []).OfType<JsonObject>())
                    if (Text(patch, "id") is "game" or "net.minecraft" && IsMinecraftVersion(Text(patch, "version"))) return Text(patch, "version");
                foreach (var library in (profile["libraries"] as JsonArray ?? []).OfType<JsonObject>())
                {
                    var name = Text(library, "name");
                    if (!name.StartsWith("net.minecraftforge:forge:", StringComparison.Ordinal) && !name.StartsWith("net.minecraftforge:fmlloader:", StringComparison.Ordinal)) continue;
                    var version = name.Split(':')[2].Split('-')[0];
                    if (IsMinecraftVersion(version)) return version;
                }
                if (IsMinecraftVersion(Text(profile, "id"))) return Text(profile, "id");
                if (IsMinecraftVersion(Text(profile, "jar"))) return Text(profile, "jar");
                id = Text(profile, "inheritsFrom");
                if (id.Length == 0) break;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            { break; }
        }
        return IsMinecraftVersion(id) ? id : IsMinecraftVersion(fallback) ? fallback : "未知版本";
    }

    private static string IdentifyLoader(string root, string id, string fallback)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unknownModLoader = false;
        while (visited.Count < 20 && visited.Add(id))
        {
            try
            {
                SafePaths.InstanceName(id);
                var path = SafePaths.Under(root, $"versions/{id}/{id}.json");
                if (!File.Exists(path)) break;
                if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject profile) break;
                if (Detect(profile) is { } loader) return loader;
                var main = Text(profile, "mainClass");
                unknownModLoader |= main is "cpw.mods.bootstraplauncher.BootstrapLauncher" or "cpw.mods.modlauncher.Launcher";
                id = Text(profile, "inheritsFrom");
                if (id.Length == 0) break;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            { break; }
        }
        if (!string.IsNullOrWhiteSpace(fallback) && !fallback.Equals("Vanilla", StringComparison.OrdinalIgnoreCase)) return fallback;
        return unknownModLoader ? "未知加载器" : "Vanilla";
    }
    private static string? Detect(JsonObject profile)
    {
        var libraries = (profile["libraries"] as JsonArray ?? []).OfType<JsonObject>().Select(library => Text(library, "name")).ToArray();
        bool Has(params string[] prefixes) => libraries.Any(name => prefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
        var main = Text(profile, "mainClass");
        var patches = (profile["patches"] as JsonArray ?? []).OfType<JsonObject>().Select(patch => Text(patch, "id")).ToArray();
        if (Has("com.cleanroommc:cleanroom:") || patches.Contains("cleanroom", StringComparer.OrdinalIgnoreCase)) return "Cleanroom";
        if (Has("net.neoforged:neoforge:", "net.neoforged:forge:", "net.neoforged.fancymodloader:loader:")) return "NeoForge";
        if (Has("org.quiltmc:quilt-loader:") || main.StartsWith("org.quiltmc.loader.", StringComparison.OrdinalIgnoreCase)) return "Quilt";
        if (Has("net.fabricmc:fabric-loader:") || main.StartsWith("net.fabricmc.loader.", StringComparison.OrdinalIgnoreCase)) return "Fabric";
        if (Has("net.minecraftforge:forge:", "net.minecraftforge:fmlloader:", "net.minecraftforge:minecraftforge:")
            || main.StartsWith("net.minecraftforge.", StringComparison.OrdinalIgnoreCase)) return "Forge";
        if (Has("com.mumfrey:liteloader:")) return "LiteLoader";
        var args = Text(profile, "minecraftArguments") + " " + (profile["arguments"] as JsonObject)?["game"]?.ToJsonString();
        if (args.Contains("--fml.neoForgeVersion", StringComparison.Ordinal) || args.Contains("neoforgeclient", StringComparison.OrdinalIgnoreCase)) return "NeoForge";
        if (args.Contains("net.minecraftforge.fml.common.launcher.FMLTweaker", StringComparison.Ordinal)
            || args.Contains("cpw.mods.fml.common.launcher.FMLTweaker", StringComparison.Ordinal)
            || args.Contains("forgeclient", StringComparison.OrdinalIgnoreCase)) return "Forge";
        return null;
    }
    private static string Text(JsonObject profile, string property) => profile[property] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
}
