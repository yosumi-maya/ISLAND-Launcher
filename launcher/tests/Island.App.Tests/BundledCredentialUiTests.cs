using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Island.App;
using Island.Core;
using BlockDock.Core;

internal static class BundledCredentialUiTests
{
    public static void Run(MainWindow window, string root, Action<bool, string> check, Action<Window, string> capture)
    {
        var key = BundledCatalogCredential.Load();
        check(key.Length >= 20 && key.Length <= 1024 && key == key.Trim(), "The build-supplied credential decrypts without user configuration");
        check(window.FindName("CurseForgeKeyBox") is null && window.FindName("CurseForgeKeyStatus") is null
            && typeof(MainWindow).GetMethod("SaveCurseForgeKey", BindingFlags.Instance | BindingFlags.NonPublic) is null,
            "Settings expose no user API key editor, status field or save handler");
        var state = (LauncherState)typeof(MainWindow).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        check(state.DownloadSettings().CurseForgeApiKey == key && !JsonSerializer.Serialize(state).Contains(key), "The bundled key reaches download snapshots without entering launcher settings JSON");
        check(typeof(LauncherState).Assembly.GetType("Island.Core.CurseForgeCredentials") is null, "The runtime no longer contains user key storage and override APIs");
        var game = state.GameDirectory;
        var legacy = new LauncherStore(Path.Combine(root, "legacy-key-profile"));
        Directory.CreateDirectory(legacy.DirectoryPath);
        File.WriteAllText(Path.Combine(legacy.DirectoryPath, "curseforge-key.dat"), "invalid legacy user key");
        legacy.Save(new LauncherState { GameDirectory = game });
        var cache = new JavaRuntimeCache(legacy.DirectoryPath, (_, _, _) => Task.FromResult<IReadOnlyList<JavaInstallation>>([]));
        var fresh = new MainWindow(legacy, javaRuntimes: cache) { Owner = window, ShowInTaskbar = false, Left = -16000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual };
        try
        {
            fresh.Show();
            var loaded = (LauncherState)typeof(MainWindow).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fresh)!;
            check(loaded.DownloadSettings().CurseForgeApiKey == key, "Legacy per-user credential files cannot override the bundled key");
        }
        finally { fresh.Close(); }
        var plain = Encoding.UTF8.GetBytes("fictional-encryption-fixture");
        var aesKey = RandomNumberGenerator.GetBytes(32); var pad = RandomNumberGenerator.GetBytes(32); var masked = new byte[32];
        for (var i = 0; i < 32; i++) masked[(i * 7) % 32] = (byte)(aesKey[i] ^ pad[i] ^ ((i * 13 + 71) & 255));
        var envelope = new byte[28 + plain.Length]; RandomNumberGenerator.Fill(envelope.AsSpan(0,12));
        using (var aes = new AesGcm(aesKey, 16)) aes.Encrypt(envelope.AsSpan(0,12), plain, envelope.AsSpan(28), envelope.AsSpan(12,16));
        var copy = (byte[])envelope.Clone(); var a = (byte[])masked.Clone(); var b = (byte[])pad.Clone();
        check(BundledCatalogCredential.Decode(copy,a,b) == "fictional-encryption-fixture" && copy.All(v => v == 0) && a.All(v => v == 0) && b.All(v => v == 0),
            "Encrypted credential decoding restores the expected value and clears temporary byte buffers");
        envelope[12] ^= 1; var rejected = false;
        try { BundledCatalogCredential.Decode(envelope, masked, pad); } catch (CryptographicException) { rejected = true; }
        check(rejected, "AES-GCM rejects a modified credential authentication tag");
        CryptographicOperations.ZeroMemory(aesKey); CryptographicOperations.ZeroMemory(plain);
        ((RadioButton)window.FindName("NavSettings")).IsChecked = true; window.UpdateLayout(); capture(window,"Formal-1.0-Settings");
    }
    public static async Task LiveAsync(Action<bool,string> check)
    {
        using var client = new DownloadClient(new() { CurseForgeApiKey = BundledCatalogCredential.Load(), Source = DownloadSource.Official });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        var response = await client.GetJsonAsync("https://api.curseforge.com/v1/mods/search?gameId=432&classId=6&searchFilter=jei&pageSize=1&index=0&sortField=2&sortOrder=desc", timeout.Token);
        check(response.Array("data").Any(), "The bundled credential successfully authenticates against the official CurseForge search API");
    }
}
