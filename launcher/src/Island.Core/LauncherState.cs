using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BlockDock.Core;

namespace Island.Core;

public sealed class LauncherState
{
    public string GameDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "IslandData", ".minecraft");
    public string JavaPath { get; set; } = "";
    public string ThemeColor { get; set; } = AppearanceSettings.DefaultColor;
    public LauncherAppearance Appearance { get; set; } = new();
    public int MemoryMb { get; set; } = 4096;
    public bool AutoMemory { get; set; } = true;
    public bool CleanMemoryBeforeLaunch { get; set; }
    public JvmSettings Jvm { get; set; } = new();
    public DownloadSource Source { get; set; } = DownloadSource.Auto;
    public int DownloadParallelDownloads { get; set; } = 8;
    public string MicrosoftClientId { get; set; } = "";
    public string? SelectedAccountId { get; set; }
    public string? SelectedInstanceId { get; set; }
    public string? SelectedGameFolderId { get; set; }
    public List<GameFolder> GameFolders { get; set; } = [];
    public bool ShowFavorites { get; set; }
    public List<PlayerAccount> Accounts { get; set; } = [];
    public Dictionary<string, InstancePreferences> Instances { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    [JsonIgnore] public GameFolder ActiveGameFolder => GameFolders.First(folder => folder.Id == SelectedGameFolderId);
    public void EnsureGameFolders()
    {
        var current = GameFolder.NormalizePath(GameDirectory);
        var unique = new List<GameFolder>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var folder in GameFolders ?? [])
        {
            if (folder is null || string.IsNullOrWhiteSpace(folder.Directory)) continue;
            try { folder.Directory = GameFolder.NormalizePath(folder.Directory); }
            catch (ArgumentException) { continue; }
            if (unique.Any(existing => GameFolder.SamePath(existing.Directory, folder.Directory))) continue;
            if (string.IsNullOrEmpty(folder.Id) || !ids.Add(folder.Id)) { folder.Id = Guid.NewGuid().ToString("N"); ids.Add(folder.Id); }
            try { folder.Name = GameFolder.ValidateName(folder.Name); }
            catch (ArgumentException) { folder.Name = GameFolder.SuggestName(folder.Directory); }
            if (!Enum.IsDefined(folder.Isolation)) folder.Isolation = VersionIsolation.Auto;
            unique.Add(folder);
        }
        GameFolders = unique;
        var active = GameFolders.FirstOrDefault(folder => GameFolder.SamePath(folder.Directory, current));
        if (active is null)
        {
            active = new() { Directory = current, Name = GameFolders.Count == 0 ? "默认目录" : GameFolder.SuggestName(current) };
            GameFolders.Add(active);
        }
        GameDirectory = active.Directory; SelectedGameFolderId = active.Id;
        if (SelectedInstanceId is null) SelectedInstanceId = active.LastSelectedInstanceId;
        active.LastSelectedInstanceId = SelectedInstanceId;
    }
    public GameFolder AddGameFolder(string directory, string name)
    {
        directory = GameFolder.NormalizePath(directory); name = GameFolder.ValidateName(name);
        var existing = GameFolders.FirstOrDefault(folder => GameFolder.SamePath(folder.Directory, directory));
        if (existing is not null) return existing;
        var result = new GameFolder { Directory = directory, Name = name }; GameFolders.Add(result); return result;
    }
    public void SelectGameFolder(string id)
    {
        var target = GameFolders.FirstOrDefault(folder => folder.Id == id) ?? throw new ArgumentException("游戏目录不在列表中。");
        var current = GameFolders.FirstOrDefault(folder => folder.Id == SelectedGameFolderId);
        if (current?.Id == target.Id) return;
        if (current is not null) current.LastSelectedInstanceId = SelectedInstanceId;
        SelectedGameFolderId = target.Id; GameDirectory = target.Directory; SelectedInstanceId = target.LastSelectedInstanceId;
    }
    public void RememberInstance(string id, string? directory = null)
    {
        directory ??= GameDirectory;
        if (GameFolder.SamePath(directory, GameDirectory)) SelectedInstanceId = id;
        if (GameFolders.FirstOrDefault(folder => GameFolder.SamePath(folder.Directory, directory)) is { } current) current.LastSelectedInstanceId = id;
    }
    public AppSettings DownloadSettings(string? directory = null, string? instanceId = null) => new() { GameDirectory = directory ?? GameDirectory,
        JavaPath = instanceId is null ? JavaPath : For(directory ?? GameDirectory, instanceId).JavaPath ?? JavaPath, Source = Source,
        ParallelDownloads = DownloadParallelDownloads is 4 or 8 or 16 or 32 ? DownloadParallelDownloads : 8, CurseForgeApiKey = "" };
    public InstancePreferences For(string id) => For(GameDirectory, id);
    public RuntimeSettings RuntimeFor(string directory, string id)
    {
        var preferences = For(directory, id);
        return new(preferences.JavaPath ?? JavaPath, preferences.MemoryMb ?? MemoryMb,
            preferences.AutoMemory ?? (preferences.MemoryMb is null && AutoMemory), preferences.Jvm ?? Jvm);
    }
    public InstancePreferences For(string directory, string id)
    {
        var key = Path.GetFullPath(directory).TrimEnd('\\', '/').ToUpperInvariant() + "|" + id;
        if (!Instances.TryGetValue(key, out var value)) Instances[key] = value = new();
        return value;
    }
}

public sealed class GameFolder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Directory { get; set; } = "";
    public string Name { get; set; } = "";
    public VersionIsolation Isolation { get; set; } = VersionIsolation.Auto;
    public string? LastSelectedInstanceId { get; set; }
    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new ArgumentException("请选择完整的游戏目录路径。");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
    public static bool SamePath(string first, string second) => string.Equals(NormalizePath(first), NormalizePath(second), StringComparison.OrdinalIgnoreCase);
    public static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("请填写目录显示名称。");
        name = name.Trim();
        if (name.Length is 0 or > 40 || name.Any(char.IsControl)) throw new ArgumentException("目录显示名称需为 1–40 个字符，不能包含换行或控制字符。");
        return name;
    }
    public static string SuggestName(string directory)
    {
        directory = NormalizePath(directory);
        var leaf = Path.GetFileName(directory);
        var name = leaf.Equals(".minecraft", StringComparison.OrdinalIgnoreCase) ? Path.GetFileName(Path.GetDirectoryName(directory)) : leaf;
        if (string.IsNullOrWhiteSpace(name)) name = directory;
        name = string.Concat(name.Where(c => !char.IsControl(c))).Trim();
        return name.Length > 40 ? name[..40] : name;
    }
}

public sealed class InstancePreferences
{
    public string DisplayName { get; set; } = "";
    public bool Favorite { get; set; }
    public DateTimeOffset? LastPlayed { get; set; }
    public double PlayedMinutes { get; set; }
    public int? MemoryMb { get; set; }
    // null inherits the launcher setting; an empty string explicitly requests automatic selection.
    public string? JavaPath { get; set; }
    // Technical cache only; it does not turn on independent runtime settings.
    public string AutoSelectedJavaPath { get; set; } = "";
    public bool? AutoMemory { get; set; }
    public JvmSettings? Jvm { get; set; }
    [JsonIgnore] public bool HasIndependentSettings => JavaPath is not null || MemoryMb is not null || AutoMemory is not null || Jvm is not null;
}

public sealed class PlayerAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Uuid { get; set; } = "";
    public bool Microsoft { get; set; }
    public string ProtectedRefreshToken { get; set; } = "";
    public string ClientId { get; set; } = "";
    public byte[]? SkinPng { get; set; }
    public string SkinUrl { get; set; } = "";
    public string SkinVariant { get; set; } = "classic";
    public string Label => Name + (Microsoft ? "  ·  Microsoft" : "  ·  本地离线");
    public static PlayerAccount Offline(string name)
    {
        if (!Regex.IsMatch(name, "^[A-Za-z0-9_]{3,16}$")) throw new ArgumentException("玩家名需为 3–16 位英文字母、数字或下划线。");
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x30); bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new() { Name = name, Uuid = Convert.ToHexString(bytes).ToLowerInvariant() };
    }
}

public sealed class LauncherStore
{
    public string DirectoryPath { get; }
    public string? LoadWarning { get; private set; }
    public LauncherStore(string? root = null) => DirectoryPath = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IslandLauncher");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public LauncherState Load()
    {
        var path = Path.Combine(DirectoryPath, "settings.json");
        if (!File.Exists(path)) { var initial = new LauncherState(); initial.EnsureGameFolders(); return initial; }
        try
        {
            var saved = File.ReadAllText(path);
            var state = JsonSerializer.Deserialize<LauncherState>(saved) ?? throw new JsonException("配置为空。");
            if (state.Accounts is null || state.Instances is null || string.IsNullOrWhiteSpace(state.GameDirectory)) throw new JsonException("配置字段无效。");
            state.GameDirectory = Path.GetFullPath(state.GameDirectory);
            state.MemoryMb = Math.Clamp(state.MemoryMb, 512, 65536);
            try { state.ThemeColor = AppearanceSettings.NormalizeColor(state.ThemeColor); }
            catch (ArgumentException) { state.ThemeColor = AppearanceSettings.DefaultColor; }
            state.Appearance = (state.Appearance ?? new()).Normalize();
            using var document = JsonDocument.Parse(saved);
            // Existing users keep their manual memory limit when upgrading.
            if (!document.RootElement.TryGetProperty(nameof(LauncherState.AutoMemory), out _)) state.AutoMemory = false;
            state.Jvm ??= new();
            foreach (var preferences in state.Instances.Values.Where(preferences => preferences.HasIndependentSettings && preferences.Jvm is null))
            {
                // Upgrade individual Java/memory overrides to a complete independent runtime snapshot.
                preferences.AutoMemory ??= preferences.MemoryMb is null && state.AutoMemory;
                preferences.MemoryMb ??= state.MemoryMb; preferences.Jvm = state.Jvm with { };
            }
            state.EnsureGameFolders();
            return state;
        }
        catch (Exception ex) when (ex is JsonException or IOException or ArgumentException)
        {
            // Keep the original file for recovery; saving later preserves another copy.
            LoadWarning = "配置未能读取，已使用默认值。原配置保留在：" + path;
            var initial = new LauncherState(); initial.EnsureGameFolders(); return initial;
        }
    }
    public void Save(LauncherState state)
    {
        state.EnsureGameFolders();
        Directory.CreateDirectory(DirectoryPath);
        var path = Path.Combine(DirectoryPath, "settings.json");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, Options), new UTF8Encoding(false));
            if (File.Exists(path)) File.Copy(path, path + ".bak", true);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
