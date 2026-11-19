using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace Island.App;

internal static class BundledCatalogCredential
{
    private static readonly Lazy<string> credential = new(CatalogCredentialEnvelope.Open);
    internal static string Load() => credential.Value;

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    internal static string Decode(byte[] envelope, byte[] masked, byte[] pad)
    {
        if (envelope.Length < 28 || masked.Length != 32 || pad.Length != 32) throw new CryptographicException("下载服务凭据无效。");
        var key = new byte[32];
        var plain = new byte[envelope.Length - 28];
        try
        {
            for (var i = 0; i < key.Length; i++) key[i] = (byte)(masked[(i * 7) % 32] ^ pad[i] ^ ((i * 13 + 71) & 255));
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(envelope.AsSpan(0, 12), envelope.AsSpan(28), envelope.AsSpan(12, 16), plain);
            return Encoding.UTF8.GetString(plain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(masked); CryptographicOperations.ZeroMemory(pad);
            CryptographicOperations.ZeroMemory(envelope);
        }
    }
}
