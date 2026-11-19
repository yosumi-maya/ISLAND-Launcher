param([Parameter(Mandatory=$true)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
$secret = [Environment]::GetEnvironmentVariable('CF_API_KEY')
if ([string]::IsNullOrWhiteSpace($secret)) {
    $storedPath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'IslandLauncher/curseforge-key.dat'
    if (Test-Path -LiteralPath $storedPath) {
        try {
            $protected = [Convert]::FromBase64String([IO.File]::ReadAllText($storedPath))
            $plain = [Security.Cryptography.ProtectedData]::Unprotect($protected, $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
            try { $secret = [Text.Encoding]::UTF8.GetString($plain) }
            finally { [Security.Cryptography.CryptographicOperations]::ZeroMemory($plain) }
        } catch { throw '无法读取维护者的构建凭据。请通过 CF_API_KEY 提供构建输入。' }
    }
}
$secret = if ($null -eq $secret) { '' } else { $secret.Trim() }
if ($secret.Length -gt 1024 -or $secret.ToCharArray().Where({ [char]::IsControl($_) }).Count -gt 0) { throw '构建凭据格式无效。' }
if ($secret.Length -eq 0) {
    if ($env:ISLAND_REQUIRE_BUNDLED_KEY -eq '1') { throw '正式发布需要内置下载凭据。请通过 CF_API_KEY 提供构建输入。' }
    $source = 'namespace Island.App; internal static class CatalogCredentialEnvelope { internal static string Open() => ""; }'
} else {
    $plain = [Text.Encoding]::UTF8.GetBytes($secret)
    $key = [Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
    $pad = [Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
    $nonce = [Security.Cryptography.RandomNumberGenerator]::GetBytes(12)
    $cipher = [byte[]]::new($plain.Length)
    $tag = [byte[]]::new(16)
    $masked = [byte[]]::new(32)
    $aes = [Security.Cryptography.AesGcm]::new($key, 16)
    try {
        $aes.Encrypt($nonce, $plain, $cipher, $tag)
        for ($i = 0; $i -lt 32; $i++) { $masked[($i * 7) % 32] = $key[$i] -bxor $pad[$i] -bxor (($i * 13 + 71) -band 255) }
        $envelope = $nonce + $tag + $cipher
        $source = @"
// Generated build input. Encrypted, not a confidential secret once distributed.
namespace Island.App;
internal static class CatalogCredentialEnvelope
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining | System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]
    internal static string Open() => BundledCatalogCredential.Decode([$($envelope -join ',')], [$($masked -join ',')], [$($pad -join ',')]);
}
"@
    } finally {
        $aes.Dispose()
        [Security.Cryptography.CryptographicOperations]::ZeroMemory($plain)
        [Security.Cryptography.CryptographicOperations]::ZeroMemory($key)
        [Security.Cryptography.CryptographicOperations]::ZeroMemory($pad)
        $secret = $null
    }
}
$destination = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
[IO.File]::WriteAllText($destination, $source, [Text.UTF8Encoding]::new($false))
