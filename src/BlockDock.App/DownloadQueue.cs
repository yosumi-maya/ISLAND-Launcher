using System.Collections.ObjectModel;
using System.IO;
using BlockDock.Core;

namespace BlockDock.App;

public sealed class DownloadQueue
{
    private readonly ObservableCollection<DownloadTask> tasks = [];
    private readonly Func<DownloadTask, IProgress<InstallProgress>, CancellationToken, Task<string>> worker;
    private CancellationTokenSource? activeCancellation;
    private Task pump = Task.CompletedTask;
    private bool pumping, closed;
    public ReadOnlyObservableCollection<DownloadTask> Items { get; }
    public DownloadTask? Current { get; private set; }
    public bool IsBusy => Current is not null;
    public bool HasUnfinishedTasks => tasks.Any(t => t.State is DownloadTaskState.Queued or DownloadTaskState.Running or DownloadTaskState.Pausing or DownloadTaskState.Canceling or DownloadTaskState.Paused);
    public event EventHandler? Changed;
    public event Action<DownloadTask>? TaskSettled;

    public DownloadQueue(Func<DownloadTask, IProgress<InstallProgress>, CancellationToken, Task<string>> worker)
    {
        this.worker = worker;
        Items = new(tasks);
    }
    public DownloadTask Enqueue(InstallRequest request, AppSettings settings, PackInfo? pack = null)
    {
        if (closed) throw new InvalidOperationException("下载队列已关闭。");
        request = request with { InstanceName = SafePaths.InstanceName(request.InstanceName) };
        if (request.Loader != LoaderKind.Vanilla && request.InstanceName.Equals(request.MinecraftVersion, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("加载器版本名称需要与原版版本号不同。");
        var task = new DownloadTask(request, settings, pack);
        if (tasks.Any(t => !t.IsEnded && t.TargetDirectory.Equals(task.TargetDirectory, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("相同名称的任务已在队列中，请先继续或取消该任务。");
        if (Directory.Exists(task.TargetDirectory) || File.Exists(task.TargetDirectory)) throw new IOException("已有同名版本，请换一个名称。");
        tasks.Add(task); Notify(); StartPump();
        return task;
    }
    public void Pause(DownloadTask task)
    {
        if (closed || !tasks.Contains(task)) return;
        if (task.State == DownloadTaskState.Running && task == Current)
        {
            task.SetState(DownloadTaskState.Pausing, "正在暂停下载…"); Notify(); activeCancellation?.Cancel();
        }
        else if (task.State == DownloadTaskState.Queued)
        {
            task.SetState(DownloadTaskState.Paused, "下载已暂停", "点击 ▶ 继续，或点击 × 取消任务。");
            task.AppendLog("暂停等待中的任务。"); Notify();
        }
    }
    public DownloadTask EnqueueWork(AppSettings settings, DownloadWork work)
    {
        if (closed) throw new InvalidOperationException("下载队列已关闭。");
        if (work.TargetFiles.Count == 0) throw new ArgumentException("下载任务没有目标文件。");
        var targets = work.TargetFiles.Select(Path.GetFullPath).ToArray();
        if (tasks.Any(task => !task.IsEnded && task.Work is { } other && other.TargetFiles.Any(file => targets.Contains(Path.GetFullPath(file), StringComparer.OrdinalIgnoreCase))))
            throw new InvalidOperationException("相同资源或前置文件已在队列中，请先继续或取消该任务。");
        var task = new DownloadTask(new("", work.Name), settings, null, work with { TargetFiles = Array.AsReadOnly(targets) });
        tasks.Add(task); Notify(); StartPump(); return task;
    }
    public void Resume(DownloadTask task)
    {
        if (closed || !tasks.Contains(task) || task.State is not (DownloadTaskState.Paused or DownloadTaskState.Failed)) return;
        task.SetState(DownloadTaskState.Queued, "等待继续", "将在当前下载结束后继续。"); Notify(); StartPump();
    }
    public void Cancel(DownloadTask task)
    {
        if (!tasks.Contains(task) || !task.CanCancel) return;
        if (task == Current && task.State is DownloadTaskState.Running or DownloadTaskState.Pausing)
        {
            task.SetState(DownloadTaskState.Canceling, "正在取消任务…"); Notify(); activeCancellation?.Cancel();
        }
        else
        {
            Canceled(task); Notify();
        }
    }
    public void ClearEnded()
    {
        foreach (var task in tasks.Where(t => t.IsEnded).ToList()) tasks.Remove(task);
        Notify();
    }
    public void FlushProgress() => Current?.Flush();
    public Task ShutdownAsync()
    {
        closed = true;
        foreach (var task in tasks.Where(t => t.CanCancel).ToList()) Cancel(task);
        return pump;
    }
    private void StartPump() { if (!pumping && !closed) pump = PumpAsync(); }
    private async Task PumpAsync()
    {
        pumping = true;
        try
        {
            while (!closed && tasks.FirstOrDefault(t => t.State == DownloadTaskState.Queued) is { } task)
            {
                Current = task;
                using var cancellation = new CancellationTokenSource();
                activeCancellation = cancellation;
                var generation = task.Start(); Notify();
                try
                {
                    var result = await worker(task, new QueueProgress(p => task.Capture(generation, p)), cancellation.Token);
                    task.SetState(DownloadTaskState.Completed, "安装完成", result);
                    task.AppendLog("安装完成：" + result);
                }
                catch (Exception ex) when (ex is OperationCanceledException || cancellation.IsCancellationRequested)
                {
                    if (!closed && task.State == DownloadTaskState.Pausing)
                    {
                        task.SetState(DownloadTaskState.Paused, "下载已暂停", "点击 ▶ 继续，或点击 × 取消任务。");
                        task.AppendLog("用户暂停下载；保留当前任务与续传缓存。");
                    }
                    else Canceled(task);
                }
                catch (Exception ex)
                {
                    task.SetState(DownloadTaskState.Failed, "安装未完成", ex.Message.Split('\n')[0]); task.AppendLog(ex.ToString());
                }
                finally
                {
                    Current = null; activeCancellation = null; Notify(); TaskSettled?.Invoke(task);
                }
            }
        }
        finally { pumping = false; Notify(); }
    }
    private static void Canceled(DownloadTask task)
    {
        task.SetState(DownloadTaskState.Canceled, "任务已取消", "该任务已结束，不能继续。");
        task.AppendLog("用户取消任务。");
    }
    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);
    private sealed class QueueProgress(Action<InstallProgress> callback) : IProgress<InstallProgress> { public void Report(InstallProgress value) => callback(value); }
}
