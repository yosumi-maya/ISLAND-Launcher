using System.ComponentModel;
using System.IO;
using BlockDock.Core;

namespace BlockDock.App;

public enum DownloadTaskState { Queued, Running, Pausing, Canceling, Paused, Failed, Completed, Canceled }

public sealed record DownloadWork(string Name, string Subtitle, IReadOnlyList<string> TargetFiles,
    Func<AppSettings, IProgress<InstallProgress>, CancellationToken, Task<string>> Run, string? IconUrl = null);

public sealed class DownloadTask : INotifyPropertyChanged
{
    private sealed record PendingProgress(int Attempt, InstallProgress Progress);
    private PendingProgress? pending;
    private int attempt;
    public event PropertyChangedEventHandler? PropertyChanged;
    public Guid Id { get; } = Guid.NewGuid();
    public InstallRequest Request { get; }
    public AppSettings Settings { get; }
    public PackInfo? Pack { get; }
    public DownloadWork? Work { get; }
    public string TargetDirectory { get; }
    public string Name => Work?.Name ?? Request.InstanceName;
    public string? IconUrl => Work?.IconUrl ?? Pack?.IconUrl;
    public LoaderKind Loader => Request.Loader;
    public string Subtitle => Work?.Subtitle ?? $"Minecraft {Request.MinecraftVersion}  ·  {(Loader == LoaderKind.Vanilla ? "原版" : Loader + " " + Request.LoaderVersion)}";
    public DownloadTaskState State { get; private set; } = DownloadTaskState.Queued;
    public string Stage { get; private set; } = "等待下载";
    public string Detail { get; private set; } = "已加入下载队列";
    public double Percent { get; private set; }
    public bool IsIndeterminate { get; private set; }
    public string Numbers { get; private set; } = "";
    public string PercentLabel => IsIndeterminate ? "—" : $"{Percent:0.#}%";
    public string DetailText => Stage + (Detail.Length > 0 ? " · " + Detail : "");
    public string LogText { get; private set; } = "";
    public string? LogPath { get; internal set; }
    public bool IsEnded => State is DownloadTaskState.Completed or DownloadTaskState.Canceled;
    public bool ShowPause => State is DownloadTaskState.Running or DownloadTaskState.Queued or DownloadTaskState.Pausing;
    public bool CanToggle => State is DownloadTaskState.Running or DownloadTaskState.Queued or DownloadTaskState.Paused or DownloadTaskState.Failed;
    public bool CanCancel => !IsEnded && State != DownloadTaskState.Canceling;
    public string ToggleCaption => State == DownloadTaskState.Failed ? "重试下载" : ShowPause ? "暂停下载" : "继续下载";
    public string StateText => State switch
    {
        DownloadTaskState.Queued => "等待中", DownloadTaskState.Running => "下载中", DownloadTaskState.Pausing => "正在暂停",
        DownloadTaskState.Canceling => "正在取消", DownloadTaskState.Paused => "已暂停", DownloadTaskState.Failed => "未完成",
        DownloadTaskState.Completed => "已完成", _ => "已取消"
    };
    public string StateColor => State switch
    {
        DownloadTaskState.Completed => "#219E78", DownloadTaskState.Failed => "#C35663", DownloadTaskState.Paused => "#B18C3D",
        DownloadTaskState.Running => "#2563EB", _ => "#8995A9"
    };

    internal DownloadTask(InstallRequest request, AppSettings settings, PackInfo? pack, DownloadWork? work = null)
    {
        Request = request;
        Settings = new AppSettings { GameDirectory = Path.GetFullPath(settings.GameDirectory), Source = settings.Source,
            JavaPath = settings.JavaPath, ParallelDownloads = settings.ParallelDownloads, CurseForgeApiKey = settings.CurseForgeApiKey, IncludeOptionalMods = settings.IncludeOptionalMods };
        Pack = pack;
        Work = work;
        TargetDirectory = work is null ? SafePaths.Under(Settings.GameDirectory, "versions/" + request.InstanceName) : Path.GetDirectoryName(work.TargetFiles[0])!;
    }
    internal int Start()
    {
        var generation = Interlocked.Increment(ref attempt);
        Interlocked.Exchange(ref pending, null);
        SetState(DownloadTaskState.Running, generation > 1 ? "继续下载" : "准备下载", "正在准备安装文件");
        IsIndeterminate = true;
        AppendLog(generation > 1 ? "继续当前任务，复用已完成文件与续传缓存。" : $"开始安装 {Subtitle}\n目标目录：{(Work is null ? Settings.GameDirectory : TargetDirectory)}\n{(Work is null ? "实例名称" : "资源名称")}：{Name}");
        Notify();
        return generation;
    }
    internal void Capture(int generation, InstallProgress progress)
    {
        if (generation == Volatile.Read(ref attempt)) Volatile.Write(ref pending, new(generation, progress));
    }
    internal void Flush()
    {
        var report = Interlocked.Exchange(ref pending, null);
        if (report is null || report.Attempt != attempt || State != DownloadTaskState.Running) return;
        var progress = report.Progress;
        if (Stage != progress.Stage) AppendLog(progress.Stage);
        Stage = progress.Stage; Detail = Hide(progress.Detail); Percent = progress.Percent; IsIndeterminate = progress.Total == 0;
        Numbers = $"{FormatBytes(progress.BytesPerSecond)}/s  ·  {FormatBytes(progress.Bytes)}";
        Notify();
    }
    internal void SetState(DownloadTaskState state, string stage, string detail = "")
    {
        State = state; Stage = stage; Detail = Hide(detail); IsIndeterminate = false; Numbers = "";
        if (state == DownloadTaskState.Completed) Percent = 100;
        Notify();
    }
    internal string Hide(string text) => Settings.CurseForgeApiKey.Length == 0 ? text : text.Replace(Settings.CurseForgeApiKey, "[已隐藏]", StringComparison.Ordinal);
    internal void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {Hide(message)}{Environment.NewLine}";
        LogText += line;
        if (LogPath is not null)
        {
            try { File.AppendAllText(LogPath, line); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* The in-memory log remains available. */ }
        }
        Notify();
    }
    private static string FormatBytes(double bytes) => bytes >= 1024 * 1024 * 1024 ? $"{bytes / (1024 * 1024 * 1024):F2} GB" : bytes >= 1024 * 1024 ? $"{bytes / (1024 * 1024):F1} MB" : $"{bytes / 1024:F0} KB";
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
