using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BlockDock.Core;

namespace Island.Core;

public enum ContentMatchKind { ExactHash, ManagedOtherVersion, ModMetadata, FileName, Unreadable }
public sealed record ContentInstallationMatch(string ProjectId, string Path, ContentMatchKind Kind, bool Enabled, string Version = "")
{
    public bool Confirmed => Enabled && Kind == ContentMatchKind.ExactHash;
    public string Description => !Enabled ? "检测到已停用文件，未按已安装处理"
        : Kind switch
        {
            ContentMatchKind.ExactHash => "已安装相同版本（内容校验一致）",
            ContentMatchKind.ManagedOtherVersion => "已安装其他版本，请先移除旧版本或取消勾选",
            ContentMatchKind.ModMetadata => "疑似已安装（JAR 内模组信息匹配）",
            ContentMatchKind.FileName => "疑似已安装（文件名匹配，内容不同）",
            _ => "检测未完成，请手动确认"
        };
}

public static class ContentInstallationInspector
{
    private sealed record Identity(string Id, string Name, string Version);
    private sealed record LocalFile(string Path, bool Enabled, long Size, string Hash, IReadOnlyList<Identity> Identities, bool Unreadable);

    public static Task<IReadOnlyList<ContentInstallationMatch>> InspectAsync(ContentPlan plan, CancellationToken token, IProgress<string>? progress = null)
        => Task.Run(async () =>
        {
            var records = ReadRecords(plan.Target);
            var locals = new Dictionary<ContentKind, List<LocalFile>>();
            foreach (var kind in plan.Files.Select(file => file.Kind).Distinct())
            {
                var folder = SafePaths.Under(plan.Target.RuntimeDirectory, ContentDownloads.Folder(kind));
                var found = new List<LocalFile>(); locals[kind] = found;
                if (!Directory.Exists(folder)) continue;
                var extension = kind == ContentKind.Mod ? ".jar" : ".zip";
                var paths = Directory.EnumerateFiles(folder).Where(path => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(extension + ".disabled", StringComparison.OrdinalIgnoreCase)).ToArray();
                var sizes = plan.Files.Where(file => file.Kind == kind).Select(file => file.Size)
                    .Concat(records.Where(record => plan.Files.Any(file => file.ProjectId == record.ProjectId)).Select(record => record.Size)).ToHashSet();
                for (var index = 0; index < paths.Length; index++)
                {
                    token.ThrowIfCancellationRequested(); progress?.Report($"正在检测已安装资源… {index + 1} / {paths.Length}");
                    var path = paths[index];
                    try
                    {
                        path = SafePaths.Under(folder, Path.GetFileName(path));
                        var size = new FileInfo(path).Length; var hash = "";
                        if (sizes.Contains(size))
                        {
                            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                            hash = Convert.ToHexString(await SHA512.HashDataAsync(stream, token));
                        }
                        found.Add(new(path, !path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase), size, hash,
                            kind == ContentKind.Mod ? ReadIdentities(path) : [], false));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                    { found.Add(new(path, !path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase), 0, "", [], true)); }
                }
            }
            var matches = new List<ContentInstallationMatch>();
            foreach (var file in plan.Files)
            {
                var candidates = new List<ContentInstallationMatch>();
                foreach (var local in locals.GetValueOrDefault(file.Kind) ?? [])
                {
                    if (local.Hash.Length > 0 && local.Size == file.Size && local.Hash.Equals(file.Sha512, StringComparison.OrdinalIgnoreCase))
                        candidates.Add(new(file.ProjectId, local.Path, ContentMatchKind.ExactHash, local.Enabled, file.ReleaseVersion));
                    else if (records.Any(record => record.ProjectId == file.ProjectId && record.VersionId != file.VersionId && local.Hash.Length > 0 && local.Hash.Equals(record.Sha512, StringComparison.OrdinalIgnoreCase)))
                        candidates.Add(new(file.ProjectId, local.Path, ContentMatchKind.ManagedOtherVersion, local.Enabled));
                    else if (local.Identities.FirstOrDefault(identity => IdentityMatches(file, identity)) is { } identity)
                        candidates.Add(new(file.ProjectId, local.Path, ContentMatchKind.ModMetadata, local.Enabled, identity.Version));
                    else if (StripLabels(Path.GetFileName(local.Path)).Equals(StripLabels(file.FileName), StringComparison.OrdinalIgnoreCase))
                        candidates.Add(new(file.ProjectId, local.Path, local.Unreadable ? ContentMatchKind.Unreadable : ContentMatchKind.FileName, local.Enabled));
                }
                var match = candidates.OrderByDescending(candidate => candidate.Confirmed).ThenByDescending(candidate => candidate.Enabled).ThenBy(candidate => candidate.Kind).FirstOrDefault();
                if (match is not null) matches.Add(match);
            }
            return (IReadOnlyList<ContentInstallationMatch>)matches;
        }, token);

    private static bool IdentityMatches(ContentFile file, Identity identity)
    {
        var slug = Normalize(file.ProjectSlug.Length > 0 ? file.ProjectSlug : file.ProjectId);
        return (slug.Length >= 3 && Normalize(identity.Id) == slug)
            || (Normalize(file.Name).Length >= 3 && Normalize(identity.Name) == Normalize(file.Name));
    }
    private static string Normalize(string value) => string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();
    private static string StripLabels(string name) => Regex.Replace(name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? name[..^9] : name,
        @"^(?:\s*(?:\[[^\]]*\]|【[^】]*】|\([^)]*\)))+\s*", "");
    private static IReadOnlyList<ContentFile> ReadRecords(ContentTarget target)
    {
        var path = SafePaths.Under(target.RuntimeDirectory, ".island/content.json");
        if (!File.Exists(path) || new FileInfo(path).Length > 1024 * 1024) return [];
        try { return JsonSerializer.Deserialize<List<ContentFile>>(File.ReadAllText(path)) ?? []; }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return []; }
    }
    private static string Value(JsonNode? node, string name) => node?[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
    private static IReadOnlyList<Identity> ReadIdentities(string path)
    {
        var found = new List<Identity>();
        try
        {
            using var archive = ZipFile.OpenRead(path);
            string Read(string name)
            {
                var entry = archive.GetEntry(name);
                if (entry is null || entry.Length > 1024 * 1024) return "";
                using var reader = new StreamReader(entry.Open()); return reader.ReadToEnd();
            }
            foreach (var name in new[] { "fabric.mod.json", "quilt.mod.json", "mcmod.info" })
            {
                var text = Read(name); if (text.Length == 0) continue;
                try
                {
                    var data = JsonNode.Parse(text);
                    if (name == "quilt.mod.json") { var mod = data?["quilt_loader"]; found.Add(new(Value(mod, "id"), Value(mod?["metadata"], "name"), Value(mod, "version"))); }
                    else if (name == "fabric.mod.json") found.Add(new(Value(data, "id"), Value(data, "name"), Value(data, "version")));
                    else foreach (var mod in (data as JsonArray ?? data?["modList"] as JsonArray ?? []).OfType<JsonObject>()) found.Add(new(Value(mod, "modid"), Value(mod, "name"), Value(mod, "version")));
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { }
            }
            foreach (var name in new[] { "META-INF/mods.toml", "META-INF/neoforge.mods.toml" })
            {
                var text = Read(name);
                foreach (Match section in Regex.Matches(text, @"(?ms)^\s*\[\[mods\]\]\s*$(.*?)(?=^\s*\[|\z)"))
                {
                    string Field(string key) => Regex.Match(section.Groups[1].Value, @"(?m)^\s*" + key + "\\s*=\\s*[\"']([^\"'\\r\\n]*)[\"']").Groups[1].Value;
                    found.Add(new(Field("modId"), Field("displayName"), Field("version")));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { }
        return found;
    }
}
