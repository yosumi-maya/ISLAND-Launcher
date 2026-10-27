using System.IO;
using System.Windows.Threading;
using BlockDock.App;
using BlockDock.Core;

internal static class DownloadQueueTests
{
    public static (string Name, Action<string> Run)[] Cases() =>
    [
        ("多任务按加入顺序执行且不并发安装", root => WithQueue(root, (q, runs) =>
        {
            dynamic first = q.Enqueue(Request("A"), Options(root), null);
            dynamic second = q.Enqueue(Request("B"), Options(root), null);
            Check(q.Items.Count == 2 && State(first) == "Running" && State(second) == "Queued" && runs.Count == 1, "第一个任务应执行，第二个应独立等待");
            runs[0].Completion.SetResult("done-A"); Pump(() => runs.Count == 2);
            Check(State(first) == "Completed" && State(second) == "Running", "完成后应继续下一个任务");
        })),
        ("暂停等待实际执行退出，恢复后使用原任务重新排队", root => WithQueue(root, (q, runs) =>
        {
            dynamic first = q.Enqueue(Request("A"), Options(root), null);
            dynamic second = q.Enqueue(Request("B"), Options(root), null);
            q.Pause(first);
            Check(State(first) == "Pausing" && runs[0].Token.IsCancellationRequested && runs.Count == 1, "原执行退出前不能启动后续任务");
            q.Resume(first);
            Check(State(first) == "Pausing", "不能提前继续仍在退出的任务");
            runs[0].Completion.SetCanceled(runs[0].Token); Pump(() => runs.Count == 2);
            Check(State(first) == "Paused" && State(second) == "Running", "暂停应保留首任务并让后续任务运行");
            q.Resume(first);
            Check(State(first) == "Queued", "恢复应排队等待当前执行结束");
            runs[1].Completion.SetResult("done-B"); Pump(() => runs.Count == 3);
            Check(ReferenceEquals(runs[0].Task, runs[2].Task) && State(first) == "Running", "恢复必须使用原任务与目标");
        })),
        ("取消等待任务后不会执行或恢复它", root => WithQueue(root, (q, runs) =>
        {
            dynamic first = q.Enqueue(Request("A"), Options(root), null);
            dynamic second = q.Enqueue(Request("B"), Options(root), null);
            q.Cancel(second); q.Resume(second);
            Check(State(second) == "Canceled" && !(bool)second.CanToggle, "取消是终态");
            runs[0].Completion.SetResult("done"); Pump(() => State(first) == "Completed");
            Check(runs.Count == 1, "取消的等待任务不能占用执行器");
        })),
        ("等待任务可单独暂停和取消，不影响执行中的任务", root => WithQueue(root, (q, runs) =>
        {
            dynamic first = q.Enqueue(Request("A"), Options(root), null);
            dynamic second = q.Enqueue(Request("B"), Options(root), null);
            q.Pause(second);
            Check(State(second) == "Paused" && !runs[0].Token.IsCancellationRequested && State(first) == "Running", "只能暂停所选任务");
            q.Cancel(second);
            Check(State(second) == "Canceled" && (bool)first.CanToggle, "暂停任务取消不能影响其他卡片");
        })),
        ("暂停退出过程中取消应最终取消且继续后续任务", root => WithQueue(root, (q, runs) =>
        {
            dynamic first = q.Enqueue(Request("A"), Options(root), null);
            q.Enqueue(Request("B"), Options(root), null);
            q.Pause(first); q.Cancel(first);
            Check(State(first) == "Canceling", "取消应覆盖暂停意图");
            runs[0].Completion.SetCanceled(runs[0].Token); Pump(() => runs.Count == 2);
            q.Resume(first);
            Check(State(first) == "Canceled", "取消任务不能回到暂停或等待状态");
        })),
        ("旧一轮任务进度不能覆盖恢复后的进度", root => WithQueue(root, (q, runs) =>
        {
            dynamic task = q.Enqueue(Request("A"), Options(root), null);
            q.Pause(task); runs[0].Completion.SetCanceled(runs[0].Token); Pump(() => State(task) == "Paused");
            q.Resume(task); Pump(() => runs.Count == 2);
            runs[1].Progress.Report(new("新执行", "new", 3, 10)); q.FlushProgress();
            runs[0].Progress.Report(new("旧执行", "old", 9, 10)); q.FlushProgress();
            Check((string)task.Stage == "新执行" && (double)task.Percent == 30, "迟到进度必须按执行轮次丢弃");
        })),
        ("任务冻结设置并拒绝重复目标名称", root => WithQueue(root, (q, _) =>
        {
            var settings = Options(root); settings.IncludeOptionalMods = false;
            dynamic task = q.Enqueue(Request("Example"), settings, null);
            settings.GameDirectory = root + "-other"; settings.IncludeOptionalMods = true;
            var retained = Get<AppSettings>(task, "Settings");
            Check(retained.GameDirectory == root && !retained.IncludeOptionalMods, "修改设置不能改变已加入任务");
            var rejected = false;
            try { q.Enqueue(Request("EXAMPLE"), Options(root + "/."), null); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && q.Items.Count == 1, "同一目录同名实例不能重复排队");
        })),
        ("清理只移除已结束卡片，关闭会停止整个队列", root => WithQueue(root, (q, runs) =>
        {
            dynamic first = q.Enqueue(Request("A"), Options(root), null);
            dynamic second = q.Enqueue(Request("B"), Options(root), null);
            q.Cancel(second); q.ClearEnded();
            Check(q.Items.Count == 1 && State(first) == "Running", "清理不能丢弃活动任务");
            var stopping = (Task)q.ShutdownAsync();
            Check(!stopping.IsCompleted && runs[0].Token.IsCancellationRequested, "关闭应等待实际执行退出");
            runs[0].Completion.SetCanceled(runs[0].Token); Pump(() => stopping.IsCompleted);
            Check(State(first) == "Canceled" && runs.Count == 1, "关闭后不能启动后续任务");
        })),
        ("任务失败会隐藏密钥并允许后续任务继续", root => WithQueue(root, (q, runs) =>
        {
            var options = Options(root); options.CurseForgeApiKey = "fixture-secret-key";
            dynamic first = q.Enqueue(Request("A"), options, null);
            q.Enqueue(Request("B"), Options(root), null);
            runs[0].Completion.SetException(new IOException("error fixture-secret-key")); Pump(() => runs.Count == 2);
            Check(State(first) == "Failed" && !((string)first.LogText).Contains("fixture-secret-key") && (bool)first.CanToggle, "失败信息应脱敏且允许单独重试");
        }))
    ];

    private sealed record Run(object Task, IProgress<InstallProgress> Progress, CancellationToken Token, TaskCompletionSource<string> Completion);
    private static void WithQueue(string root, Action<dynamic, List<Run>> test)
    {
        var type = typeof(MainWindow).Assembly.GetType("BlockDock.App.DownloadQueue") ?? throw new InvalidOperationException("缺少支持多任务的独立下载队列");
        var runs = new List<Run>();
        Func<object, IProgress<InstallProgress>, CancellationToken, Task<string>> worker = (task, progress, token) =>
        {
            var run = new Run(task, progress, token, new(TaskCreationOptions.RunContinuationsAsynchronously));
            runs.Add(run); return run.Completion.Task;
        };
        dynamic queue = Activator.CreateInstance(type, worker)!;
        try { test(queue, runs); }
        finally
        {
            var shutdown = (Task)queue.ShutdownAsync();
            foreach (var run in runs) run.Completion.TrySetCanceled();
            Pump(() => shutdown.IsCompleted); shutdown.GetAwaiter().GetResult();
        }
    }
    private static AppSettings Options(string root) => new() { GameDirectory = root };
    private static InstallRequest Request(string name) => new("1.20.1", name);
    private static string State(object task) => Get<object>(task, "State").ToString()!;
    private static T Get<T>(object task, string property) => (T)task.GetType().GetProperty(property)!.GetValue(task)!;
    private static void Pump(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!done())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("队列未完成预期转换");
            var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame); Thread.Sleep(2);
        }
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
