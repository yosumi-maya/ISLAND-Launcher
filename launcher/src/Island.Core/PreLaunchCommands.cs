using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Island.Core;

public static class PreLaunchCommands
{
    public static async Task RunAsync(string command, string directory, CancellationToken token, Action<string>? output = null)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        new JvmSettings { PreLaunchCommand = command }.Validate();
        token.ThrowIfCancellationRequested();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding((int)GetOEMCP());
        using var process = new Process { StartInfo = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"))
        {
            Arguments = "/d /s /c \"" + command + "\"", WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = encoding, StandardErrorEncoding = encoding
        } };
        if (!process.Start()) throw new InvalidOperationException("启动前命令未能执行。");
        using var cancellation = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { } });
        async Task Drain(StreamReader reader)
        {
            var logged = 0;
            while (await reader.ReadLineAsync() is { } line) if (logged++ < 500) output?.Invoke(line.Length > 4096 ? line[..4096] : line);
        }
        var stdout = Drain(process.StandardOutput); var stderr = Drain(process.StandardError);
        await process.WaitForExitAsync(CancellationToken.None); await Task.WhenAll(stdout, stderr);
        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new InvalidOperationException($"启动前命令退出码为 {process.ExitCode}，已停止本次启动。");
    }
    [DllImport("kernel32.dll")] private static extern uint GetOEMCP();
}
