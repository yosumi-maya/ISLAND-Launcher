using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Island.Core;

internal static class FolderAndWorldTests
{
    public static void Run(string root, Action<bool, string> check)
    {
        var store = new LauncherStore(Path.Combine(root, "folder-settings"));
        Directory.CreateDirectory(store.DirectoryPath);
        var firstRoot = Path.Combine(root, "A 游戏", ".minecraft");
        File.WriteAllText(Path.Combine(store.DirectoryPath, "settings.json"), JsonSerializer.Serialize(new { GameDirectory = firstRoot, SelectedInstanceId = "Shared", MemoryMb = 4096 }));
        var state = store.Load();
        check(state.GameFolders.Count == 1 && state.ActiveGameFolder.Directory == firstRoot && state.SelectedInstanceId == "Shared", "Legacy single-directory settings migrate without losing current selection");
        var first = state.ActiveGameFolder;
        var second = state.AddGameFolder(Path.Combine(root, "B 游戏", ".minecraft"), "模组世界");
        state.For("Shared").DisplayName = "原版世界"; state.RememberInstance("Shared");
        state.SelectGameFolder(second.Id);
        state.For("Shared").DisplayName = "模组世界"; state.RememberInstance("Other");
        state.SelectGameFolder(first.Id);
        check(state.SelectedInstanceId == "Shared" && state.For("Shared").DisplayName == "原版世界", "Folder switching restores selection and isolates same-named instance preferences");
        state.SelectGameFolder(second.Id);
        check(state.SelectedInstanceId == "Other" && state.For("Shared").DisplayName == "模组世界", "Each folder remembers its own selected instance");
        check(state.AddGameFolder(second.Directory.ToUpperInvariant() + Path.DirectorySeparatorChar, "重复名称").Id == second.Id && state.GameFolders.Count == 2, "Reimport deduplicates case and trailing separators");
        second.Name = GameFolder.ValidateName("  我的整合包  "); state.ShowFavorites = true; store.Save(state);
        var restored = store.Load();
        check(restored.ActiveGameFolder.Name == "我的整合包" && restored.ShowFavorites && restored.GameFolders.Count == 2, "Folder names, active folder and favorites view persist");
        check(restored.For(first.Directory, "Shared").DisplayName == "原版世界" && restored.For(second.Directory, "Shared").DisplayName == "模组世界", "Cross-folder preferences resolve without changing active directory");
        try { GameFolder.ValidateName("\n"); throw new Exception("Empty name accepted"); } catch (ArgumentException) { check(true, "Invalid directory display name rejected"); }

        var instance = Path.Combine(root, "world fixture");
        var date = new DateTimeOffset(2026, 10, 23, 9, 30, 0, TimeSpan.FromHours(8));
        var a = WriteWorld(instance, "First World", "§a林间小屋 🌲", date);
        var world = WorldHistory.Read(a, "First World");
        check(world.IconPath is null, "Worlds without a cover do not fabricate a thumbnail path");
        var icon = Path.Combine(Path.GetDirectoryName(a)!, "icon.png"); File.WriteAllBytes(icon, [1, 2, 3]);
        check(WorldHistory.Read(a, "First World").IconPath == icon, "Recent world metadata resolves icon.png from that exact save directory");
        check(world.Name == "林间小屋 🌲" && world.LastPlayed == date && world.FolderName == "First World", "Read real gzip NBT world name, modified UTF-8 emoji and LastPlayed timestamp");
        WriteWorld(instance, "Second World", "第二个世界", date.AddHours(1));
        File.SetLastWriteTimeUtc(a, DateTime.UtcNow.AddDays(2));
        var corrupt = Path.Combine(instance, "saves", "Corrupt"); Directory.CreateDirectory(corrupt); File.WriteAllText(Path.Combine(corrupt, "level.dat"), "broken");
        check(WorldHistory.FindLatest(instance)?.Name == "第二个世界", "Recent world uses saved play time and skips corrupted worlds");
        check(WorldHistory.FindLatest(Path.Combine(root, "no-saves")) is null, "No fabricated map record for instances without saves");
    }
    public static string WriteWorld(string instance, string folder, string name, DateTimeOffset played)
    {
        var directory = Path.Combine(instance, "saves", folder); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "level.dat");
        using var file = File.Create(path); using var gzip = new GZipStream(file, CompressionMode.Compress); using var writer = new BinaryWriter(gzip);
        void Text(string value)
        {
            using var encoded = new MemoryStream();
            foreach (var c in value)
            {
                if (c is >= (char)1 and <= (char)0x7f) encoded.WriteByte((byte)c);
                else if (c <= 0x7ff) { encoded.WriteByte((byte)(0xc0 | c >> 6)); encoded.WriteByte((byte)(0x80 | c & 63)); }
                else { encoded.WriteByte((byte)(0xe0 | c >> 12)); encoded.WriteByte((byte)(0x80 | c >> 6 & 63)); encoded.WriteByte((byte)(0x80 | c & 63)); }
            }
            Span<byte> length = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)encoded.Length); writer.Write(length); writer.Write(encoded.ToArray());
        }
        writer.Write((byte)10); Text(""); writer.Write((byte)10); Text("Data");
        writer.Write((byte)8); Text("LevelName"); Text(name);
        writer.Write((byte)4); Text("LastPlayed"); Span<byte> time = stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(time, played.ToUnixTimeMilliseconds()); writer.Write(time);
        // Nested values must not overwrite the world's own last-played value.
        writer.Write((byte)10); Text("Player"); writer.Write((byte)4); Text("LastPlayed"); writer.Write(new byte[] { 0x7f, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff }); writer.Write((byte)0);
        writer.Write((byte)0); writer.Write((byte)0); return path;
    }
}
