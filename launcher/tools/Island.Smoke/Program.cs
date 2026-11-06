using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using BlockDock.Core;
using Island.Core;

if (args.Length < 2) throw new ArgumentException("Usage: <game-root> <version-id> [output.log]");
var root = Path.GetFullPath(args[0]); var resolved = VersionProfile.Read(root, args[1]);
using (var network = new DownloadClient(new() { GameDirectory = root })) await GameLauncher.RepairAsync(root, resolved, network, null, CancellationToken.None);
var java = await JavaDiscovery.ResolveAsync(new() { GameDirectory = root }, JavaRequirement.FromMetadata(resolved.Profile), CancellationToken.None);
var player = PlayerAccount.Offline("IslandTest");
var plan = GameLauncher.Prepare(root, resolved, java.Path, 2048, new(player.Name, player.Uuid, "0", false));
plan.StartInfo.ArgumentList.Add("--width"); plan.StartInfo.ArgumentList.Add("854"); plan.StartInfo.ArgumentList.Add("--height"); plan.StartInfo.ArgumentList.Add("480");
var lines = new ConcurrentQueue<string>();
using var process = new Process { StartInfo = plan.StartInfo };
try
{
    process.Start(); Console.WriteLine($"Started {resolved.Id} with Java {java.Version}, PID {process.Id}");
    async Task Read(StreamReader reader)
    {
        var formatter = new GameLogFormatter();
        while (await reader.ReadLineAsync() is { } raw)
            if (formatter.Push(raw) is { } line) { lines.Enqueue(line); Console.WriteLine(line); }
    }
    var stdout = Read(process.StandardOutput); var stderr = Read(process.StandardError);
    var deadline = DateTime.UtcNow.AddSeconds(100); var ready = false;
    while (DateTime.UtcNow < deadline && !process.HasExited)
    {
        process.Refresh();
        // Keep this automated rendering check out of the user's desktop.
        if (process.MainWindowHandle != IntPtr.Zero) NativeMethods.ShowWindow(process.MainWindowHandle, 0);
        ready = lines.Any(line => line.Contains("Created:") && line.Contains("atlas"));
        if (ready) { await Task.Delay(1500); break; }
        await Task.Delay(100);
    }
    if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
    await Task.WhenAll(stdout, stderr);
    if (args.Length >= 3) { var output = Path.GetFullPath(args[2]); Directory.CreateDirectory(Path.GetDirectoryName(output)!); await File.WriteAllLinesAsync(output, lines); }
    if (!ready) throw new Exception("Minecraft did not reach texture-atlas initialization. Inspect smoke output.");
    Console.WriteLine("PASS REAL MINECRAFT RENDERER INITIALIZED");
}
finally
{
    if (process.Id != 0 && !process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
    if (!await GameLauncher.CleanupNativesAsync(root, plan.NativesDirectory)) Console.WriteLine("Native files retained while held by Windows: " + plan.NativesDirectory);
}

internal static class NativeMethods
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(IntPtr window, int command);
}
