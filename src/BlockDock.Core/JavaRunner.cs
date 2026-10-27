using System.Diagnostics;
using System.Text;

namespace BlockDock.Core;

public static class JavaRunner
{
    public static async Task RunAsync(string java, IEnumerable<string> arguments, string workingDirectory, Action<string>? log, CancellationToken token)
    {
        var info = new ProcessStartInfo(java) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = workingDirectory };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException("无法启动 Java。");
        using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        var tail = new Queue<string>();
        var gate = new object();
        async Task ReadAsync(StreamReader reader)
        {
            while (await reader.ReadLineAsync(token) is { } line)
            {
                lock (gate) { tail.Enqueue(line); while (tail.Count > 14) tail.Dequeue(); }
                log?.Invoke(line);
            }
        }
        await Task.WhenAll(ReadAsync(process.StandardOutput), ReadAsync(process.StandardError), process.WaitForExitAsync(token));
        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new IOException($"Java 安装处理器退出，代码 {process.ExitCode}。\n" + string.Join("\n", tail));
    }
}
