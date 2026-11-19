using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json.Nodes;
using BlockDock.Core;

namespace Island.Core;

public static class LaunchLibraries
{
    public static IEnumerable<TransferFile> Files(string root, JsonObject profile)
    {
        foreach (var library in profile.Array("libraries").OfType<JsonObject>().Where(VersionProfile.Allowed))
        {
            var downloads = library["downloads"] as JsonObject;
            if (downloads?["artifact"] is JsonObject artifact && artifact.Str("url").Length > 0)
                yield return MinecraftInstaller.FromArtifact(PathFor(root, library), artifact);
            else if (downloads is null)
            {
                var path = PathFor(root, library);
                if (library.Str("hint") == "local" || library.Str("name").StartsWith("org.jackhuang.hmcl:", StringComparison.Ordinal))
                {
                    if (!File.Exists(path)) throw new FileNotFoundException("缺少整合包附带的本地兼容库，请重新导入完整游戏目录：" + library.Str("name"), path);
                }
                else yield return new(path, [library.Str("url", "https://libraries.minecraft.net/").TrimEnd('/') + "/" + SafePaths.Maven(library.Str("name"))]);
            }
            var classifier = library["natives"].Str("windows").Replace("${arch}", "64");
            if (classifier.Length > 0 && downloads?["classifiers"]?[classifier] is JsonObject native)
            {
                // Old Minecraft / MultiMC classifiers can omit downloads.path.
                var relative = native.Str("path", SafePaths.Maven(library.Str("name") + ":" + classifier));
                yield return MinecraftInstaller.FromArtifact(SafePaths.Under(root, "libraries/" + relative), native);
            }
        }
    }

    public static string PathFor(string root, JsonObject library)
        => SafePaths.Under(root, "libraries/" + (library["downloads"]?["artifact"]?.Str("path", SafePaths.Maven(library.Str("name"))) ?? SafePaths.Maven(library.Str("name"))));

    public static bool HasArtifact(string root, JsonObject library)
        => library["downloads"] is null || library["downloads"]?["artifact"] is JsonObject || File.Exists(PathFor(root, library));

    public static int MinimumJava(string root, JsonObject profile, string? instanceDirectory = null)
    {
        var minimum = JavaRequirement.FromMetadata(profile).MinimumMajor;
        var main = profile.Str("mainClass");
        // Older Cleanroom profiles retain Minecraft 1.12's Java 8 declaration.
        // Foundation / Cleanroom are Java 21+, independently of the base game.
        if (main.StartsWith("top.outlands.foundation.", StringComparison.Ordinal)
            || profile.Array("libraries").Any(library => library.Str("name").StartsWith("com.cleanroommc:cleanroom:", StringComparison.Ordinal)))
            minimum = Math.Max(minimum, 21);
        var entryName = main.Replace('.', '/') + ".class";
        var candidates = profile.Array("libraries").OfType<JsonObject>().Where(VersionProfile.Allowed).Where(library =>
            main.StartsWith("top.outlands.foundation.", StringComparison.Ordinal) ? library.Str("name").StartsWith("top.outlands:foundation:", StringComparison.Ordinal)
            : main.StartsWith("com.gtnewhorizons.retrofuturabootstrap.", StringComparison.Ordinal) && library.Str("name").Contains("lwjgl3ify", StringComparison.OrdinalIgnoreCase));
        Span<byte> header = stackalloc byte[8];
        foreach (var library in candidates)
        {
            var path = PathFor(root, library);
            if (!File.Exists(path)) continue;
            try
            {
                using var archive = ZipFile.OpenRead(path);
                if (archive.GetEntry(entryName) is not { } entry) continue;
                using var input = entry.Open(); input.ReadExactly(header);
                if (BinaryPrimitives.ReadUInt32BigEndian(header) == 0xcafebabe)
                    minimum = Math.Max(minimum, BinaryPrimitives.ReadUInt16BigEndian(header[6..]) - 44);
                break;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { }
        }
        // GTOCore can require Java 21 while its Minecraft 1.20.1 metadata still declares 17.
        // Inspect its actual entry class, not a fixed requirement inferred from a pack's display name.
        var mods = instanceDirectory is null ? null : Path.Combine(instanceDirectory, "mods");
        if (mods is not null && Directory.Exists(mods))
        foreach (var path in Directory.EnumerateFiles(mods, "gtocore*.jar", SearchOption.TopDirectoryOnly))
        {
            try
            {
                using var archive = ZipFile.OpenRead(path);
                if (archive.GetEntry("com/gtocore/Core.class") is not { } entry) continue;
                using var input = entry.Open(); input.ReadExactly(header);
                if (BinaryPrimitives.ReadUInt32BigEndian(header) == 0xcafebabe)
                    minimum = Math.Max(minimum, BinaryPrimitives.ReadUInt16BigEndian(header[6..]) - 44);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { }
        }
        return minimum;
    }
}
