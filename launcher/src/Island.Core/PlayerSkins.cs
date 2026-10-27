using System.Buffers.Binary;
using System.Text.Json.Nodes;
using BlockDock.Core;

namespace Island.Core;

public static class PlayerSkins
{
    public const int MaximumBytes = 1024 * 1024;
    public static void Validate(byte[] png, string variant = "classic")
    {
        if (variant is not ("classic" or "slim")) throw new ArgumentException("请选择标准或纤细模型。");
        if (png.Length < 33 || png.Length > MaximumBytes || !png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            || !png.AsSpan(12, 4).SequenceEqual("IHDR"u8)
            || BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16, 4)) != 64
            || BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20, 4)) is not (32 or 64))
            throw new ArgumentException("请选择 64 × 64 或 64 × 32 的 PNG 皮肤文件（不超过 1 MB）。");
        if (variant == "slim" && BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20, 4)) == 32)
            throw new ArgumentException("旧版 64 × 32 皮肤请使用标准模型。");
    }
    public static void ReadProfile(PlayerAccount account, JsonObject profile)
    {
        var skin = profile.Array("skins").OfType<JsonObject>().FirstOrDefault(s => s.Str("state").Equals("ACTIVE", StringComparison.OrdinalIgnoreCase));
        account.SkinUrl = skin?.Str("url") ?? "";
        account.SkinVariant = skin?.Str("variant").Equals("SLIM", StringComparison.OrdinalIgnoreCase) == true ? "slim" : "classic";
    }
    public static Uri TextureUri(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Host != "textures.minecraft.net" || uri.UserInfo.Length != 0
            || !uri.IsDefaultPort || uri.Scheme is not ("http" or "https") || !uri.AbsolutePath.StartsWith("/texture/", StringComparison.Ordinal))
            throw new ArgumentException("无法识别官方皮肤地址。");
        return new UriBuilder(uri) { Scheme = "https", Port = -1 }.Uri;
    }
}
