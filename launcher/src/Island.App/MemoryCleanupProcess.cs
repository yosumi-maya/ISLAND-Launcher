using System.ComponentModel;
using System.Diagnostics;
using Island.Core;

namespace Island.App;

internal static class MemoryCleanupProcess
{
    internal const string HelperArgument = "--clean-memory-helper";

    internal static ProcessStartInfo CreateStartInfo(string executable) => new(executable)
    {
        Arguments = HelperArgument, UseShellExecute = true, Verb = "runas",
        WorkingDirectory = AppContext.BaseDirectory, WindowStyle = ProcessWindowStyle.Hidden
    };

    public static async Task<MemoryCleanupResult> RunAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            // UAC must not block WPF's dispatcher. Only this fixed helper is elevated;
            // it never loads settings, accepts paths, launches Java, or writes files.
            using var process = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var executable = Environment.ProcessPath;
                if (executable is null || string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
                    executable = Path.Combine(AppContext.BaseDirectory, "岛屿启动器.exe");
                if (!File.Exists(executable)) throw new FileNotFoundException("找不到内存清理辅助程序。", executable);
                return Process.Start(CreateStartInfo(executable)) ?? throw new InvalidOperationException("内存清理辅助程序未能启动。");
            }, token);
            // A system call already in progress cannot be interrupted safely. Reap the
            // owned helper before honoring cancellation, then never start the game.
            await process.WaitForExitAsync(CancellationToken.None);
            token.ThrowIfCancellationRequested();
            return DecodeExitCode(process.ExitCode);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            token.ThrowIfCancellationRequested();
            return new(MemoryCleanupOutcome.AuthorizationDeclined);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            token.ThrowIfCancellationRequested();
            return new(MemoryCleanupOutcome.Failed, ex.Message);
        }
    }

    internal static MemoryCleanupResult DecodeExitCode(int code) => code switch
    {
        0 or 10 or 11 or 12 or 13 => new((MemoryCleanupOutcome)code),
        _ => new(MemoryCleanupOutcome.Failed, $"辅助程序退出码：{code}。")
    };
}
