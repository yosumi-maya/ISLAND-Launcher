using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using BlockDock.Core;

namespace Island.Core;

public sealed record SavedWorld(string Name, string FolderName, DateTimeOffset LastPlayed, string? IconPath = null);

public static class WorldHistory
{
    public static SavedWorld? FindLatest(string instanceDirectory)
    {
        try
        {
            var saves = SafePaths.Under(instanceDirectory, "saves");
            if (!Directory.Exists(saves)) return null;
            SavedWorld? latest = null;
            foreach (var directory in Directory.EnumerateDirectories(saves))
            {
                try
                {
                    var folder = Path.GetFileName(directory);
                    var metadata = SafePaths.Under(saves, folder + "/level.dat");
                    if (!File.Exists(metadata)) continue;
                    var world = Read(metadata, folder);
                    if (latest is null || world.LastPlayed > latest.LastPlayed) latest = world;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { }
            }
            return latest;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { return null; }
    }

    public static SavedWorld Read(string metadata, string folderName)
    {
        const int limit = 8 * 1024 * 1024;
        using var input = new FileStream(metadata, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var expanded = new MemoryStream();
        var buffer = new byte[8192];
        int length;
        while ((length = gzip.Read(buffer)) > 0)
        {
            if (expanded.Length + length > limit) throw new InvalidDataException("存档元数据超出读取限制。");
            expanded.Write(buffer, 0, length);
        }
        var nbt = new NbtReader(expanded.ToArray()); nbt.ReadRoot();
        var name = Regex.Replace(nbt.LevelName ?? folderName, "§[0-9a-fk-or]", "", RegexOptions.IgnoreCase).Trim();
        if (name.Length == 0) name = folderName;
        var date = new DateTimeOffset(File.GetLastWriteTimeUtc(metadata));
        if (nbt.PlayedAt is > 0 and <= 253402300799999) date = DateTimeOffset.FromUnixTimeMilliseconds(nbt.PlayedAt.Value);
        var icon = SafePaths.Under(Path.GetDirectoryName(metadata)!, "icon.png");
        return new(name, folderName, date, File.Exists(icon) ? icon : null);
    }

    private sealed class NbtReader(byte[] data)
    {
        private int offset;
        private int tags;
        public string? LevelName { get; private set; }
        public long? PlayedAt { get; private set; }
        private void Require(int count)
        {
            if (count < 0 || offset > data.Length - count) throw new InvalidDataException("存档 NBT 数据不完整。");
        }
        private byte Byte() { Require(1); return data[offset++]; }
        private int Int() { Require(4); var value = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, 4)); offset += 4; return value; }
        private long Long() { Require(8); var value = BinaryPrimitives.ReadInt64BigEndian(data.AsSpan(offset, 8)); offset += 8; return value; }
        private void Skip(long count) { if (count < 0 || count > int.MaxValue) throw new InvalidDataException("NBT 长度无效。"); Require((int)count); offset += (int)count; }
        private string String()
        {
            Require(2); var size = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2)); offset += 2; Require(size);
            // Java DataInput uses modified UTF-8; keep surrogate pairs so emoji world names survive.
            var end = offset + size; var text = new StringBuilder();
            while (offset < end)
            {
                var first = data[offset++];
                if (first < 0x80) text.Append((char)first);
                else if ((first & 0xe0) == 0xc0 && offset < end)
                {
                    var second = data[offset++]; if ((second & 0xc0) != 0x80) throw new InvalidDataException("NBT 字符串编码无效。");
                    text.Append((char)(((first & 0x1f) << 6) | (second & 0x3f)));
                }
                else if ((first & 0xf0) == 0xe0 && offset + 1 < end)
                {
                    var second = data[offset++]; var third = data[offset++];
                    if ((second & 0xc0) != 0x80 || (third & 0xc0) != 0x80) throw new InvalidDataException("NBT 字符串编码无效。");
                    text.Append((char)(((first & 15) << 12) | ((second & 63) << 6) | (third & 63)));
                }
                else throw new InvalidDataException("NBT 字符串编码无效。");
            }
            return text.ToString();
        }
        public void ReadRoot()
        {
            if (Byte() != 10) throw new InvalidDataException("存档元数据不是 NBT compound。");
            var name = String(); ReadPayload(10, 0, name == "Data", "");
        }
        private void ReadPayload(byte type, int depth, bool worldData, string name)
        {
            if (++tags > 200000 || depth > 64) throw new InvalidDataException("NBT 结构超出读取限制。");
            switch (type)
            {
                case 1: Skip(1); break;
                case 2: Skip(2); break;
                case 3: case 5: Skip(4); break;
                case 4: var value = Long(); if (worldData && name == "LastPlayed") PlayedAt = value; break;
                case 6: Skip(8); break;
                case 7: Skip(Int()); break;
                case 8: var text = String(); if (worldData && name == "LevelName") LevelName = text; break;
                case 9:
                    var element = Byte(); var count = Int();
                    if (count < 0 || count > 1000000 || element == 0 && count > 0) throw new InvalidDataException("NBT 列表长度无效。");
                    for (var i = 0; i < count; i++) ReadPayload(element, depth + 1, false, "");
                    break;
                case 10:
                    byte child;
                    while ((child = Byte()) != 0)
                    {
                        var key = String(); ReadPayload(child, depth + 1, depth == 0 && key == "Data" || worldData && child != 10, key);
                    }
                    break;
                case 11: Skip((long)Int() * 4); break;
                case 12: Skip((long)Int() * 8); break;
                default: throw new InvalidDataException("未知的存档 NBT 类型。");
            }
        }
    }
}
