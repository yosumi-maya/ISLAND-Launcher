using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Island.Core;

namespace Island.App;

public partial class ContentInstallConfirmation : UserControl
{
    private readonly ContentPlan plan;
    private readonly CancellationTokenSource lifetime = new();
    private bool detectionFailed;
    internal IReadOnlyList<ContentInstallChoice> Choices { get; }
    public bool Ready { get; private set; }
    public event EventHandler? Changed;
    public ContentInstallConfirmation(ContentPlan plan)
    {
        this.plan = plan; InitializeComponent();
        var main = plan.Files.FirstOrDefault(file => !file.IsDependency) ?? plan.Files.First();
        var label = main.Kind == ContentKind.Mod ? "模组" : ContentDownloads.Label(main.Kind);
        InstallHeading.Text = "确认" + label + "安装";
        InstallIntroduction.Text = "即将安装以下" + label + "到此文件夹";
        InstallDestination.Text = Path.GetDirectoryName(ContentDownloads.Destination(plan.Target, main));
        InstallDestination.ToolTip = InstallDestination.Text;
        InstallInstance.Text = plan.Target.Display; InstallInstance.ToolTip = plan.Target.RuntimeDirectory;
        Choices = plan.Files.OrderBy(file => file.IsDependency).Select(file => new ContentInstallChoice(file, plan)).ToArray();
        foreach (var choice in Choices) choice.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ContentInstallChoice.Install)) UpdateSummary(); };
        InstallChoices.ItemsSource = Choices;
        Loaded += async (_, _) => await DetectAsync();
        SelectionSummary.Text = "正在检测已安装的资源…"; InstallChoices.IsEnabled = DetectAgain.IsEnabled = false;
    }
    public void Close() => lifetime.Cancel();
    public ContentPlan SelectedPlan() => plan with { SkippedDependencies = Choices.Where(choice => choice.CanToggle && !choice.Install).Select(choice => choice.File.ProjectId).ToArray() };
    private async void DetectAgainClick(object sender, RoutedEventArgs e) => await DetectAsync();
    private async Task DetectAsync()
    {
        if (lifetime.IsCancellationRequested) return;
        Ready = false; Changed?.Invoke(this, EventArgs.Empty);
        detectionFailed = false;
        InstallChoices.IsEnabled = DetectAgain.IsEnabled = false; DetectionProgress.Visibility = Visibility.Visible;
        try
        {
            var progress = new Progress<string>(message => { if (!lifetime.IsCancellationRequested && !Ready) SelectionSummary.Text = message; });
            var matches = await ContentInstallationInspector.InspectAsync(plan, lifetime.Token, progress);
            lifetime.Token.ThrowIfCancellationRequested();
            foreach (var choice in Choices) choice.Apply(matches.FirstOrDefault(match => match.ProjectId == choice.File.ProjectId));
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            detectionFailed = true;
            SelectionHint.Text = "检测未完成，可手动取消已安装的前置。"; SelectionHint.ToolTip = ex.Message;
            foreach (var choice in Choices) choice.SetUnverified();
        }
        finally
        {
            if (!lifetime.IsCancellationRequested)
            {
                Ready = true; InstallChoices.IsEnabled = DetectAgain.IsEnabled = true; DetectionProgress.Visibility = Visibility.Collapsed; UpdateSummary();
            }
        }
    }
    private void UpdateSummary()
    {
        if (!Ready) return;
        var skipped = Choices.Count(choice => choice.CanToggle && !choice.Install);
        SelectionSummary.Text = $"将安装本体 {Choices.Count(choice => !choice.CanToggle)} 项、前置 {Choices.Count(choice => choice.CanToggle && choice.Install)} 项，跳过 {skipped} 项。";
        SelectionHint.Text = detectionFailed ? "检测未完成，前置可手动取消勾选；未选项目将跳过。" : skipped > 0 ? "未勾选的前置将跳过安装，请确认实例中已有可用版本。" : "本体固定安装，前置可自行勾选；原有文件不会被覆盖。";
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed class ContentInstallChoice(ContentFile file, ContentPlan plan) : INotifyPropertyChanged
{
    public ContentFile File { get; } = file;
    public string Title => File.Name;
    public string Role => File.IsDependency ? "前置" : "本体";
    public bool CanToggle => File.IsDependency;
    public string CheckLabel => CanToggle ? "安装前置：" + Title : "本体固定安装：" + Title;
    public string Destination => ContentDownloads.Destination(plan.Target, File);
    public string FileLabel => File.FileName + (File.Kind != plan.Files.FirstOrDefault(item => !item.IsDependency)?.Kind ? "  → " + ContentDownloads.Folder(File.Kind) : "");
    private bool install = !file.IsDependency || !plan.SkippedDependencies.Contains(file.ProjectId);
    private bool manual = plan.SkippedDependencies.Contains(file.ProjectId), applying;
    public bool Install { get => install; set { if (!applying && CanToggle) manual = true; var next = !CanToggle || value; if (install == next) return; install = next; Notify(); Notify(nameof(Detection)); } }
    private string detection = "等待检测已安装资源…";
    private bool confirmed;
    public string Detection => detection + (CanToggle ? Install ? confirmed ? " · 将复用现有文件" : " · 本次安装" : " · 本次跳过" : "");
    public string ExistingPath { get; private set; } = "";
    public string ExistingLabel => ExistingPath.Length > 0 ? "已找到：" + Path.GetFileName(ExistingPath) : "";
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public void Apply(ContentInstallationMatch? match)
    {
        detection = match?.Description ?? "未检测到已安装版本"; confirmed = match?.Confirmed == true;
        if (match?.Version is { Length: > 0 } version) detection += " · " + version;
        ExistingPath = match?.Path ?? "";
        if (!manual) { applying = true; try { Install = !CanToggle || match?.Confirmed != true; } finally { applying = false; } }
        Notify(nameof(Detection)); Notify(nameof(ExistingPath)); Notify(nameof(ExistingLabel));
    }
    public void SetUnverified() { detection = "检测未完成，请手动确认是否需要安装"; Notify(nameof(Detection)); }
}
