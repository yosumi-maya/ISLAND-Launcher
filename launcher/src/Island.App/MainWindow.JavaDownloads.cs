using System.Windows;
using System.Windows.Controls;
using BlockDock.Core;
using Island.Core;

namespace Island.App;

public partial class MainWindow
{
    private readonly JavaDownloads javaDownloads;
    private sealed record JavaDownloadChoice<T>(string Label, T Value);

    private void InitializeJavaDownloads()
    {
        AutoDownloadJavaBox.IsChecked = state.AutoDownloadJava;
        JavaVersionBox.ItemsSource = new[] { 8, 11, 16, 17, 21, 25, 26 }
            .Select(major => new JavaDownloadChoice<int>($"Java {major}", major)).ToArray();
        JavaVersionBox.SelectedValue = 21;
        JavaSourceBox.ItemsSource = new JavaDownloadChoice<JavaDownloadSource>[]
        {
            new("国内镜像优先", JavaDownloadSource.Auto),
            new("清华大学优先", JavaDownloadSource.Tsinghua),
            new("南京大学优先", JavaDownloadSource.Nanjing),
            new("中国科技云优先", JavaDownloadSource.CstCloud),
            new("仅官方源", JavaDownloadSource.Official)
        };
        JavaSourceBox.SelectedValue = Enum.IsDefined(state.JavaSource) ? state.JavaSource : JavaDownloadSource.Auto;
        JavaVendorBox.ItemsSource = new JavaDownloadChoice<JavaVendor>[]
        {
            new("Eclipse Temurin", JavaVendor.Temurin),
            new("Azul Zulu", JavaVendor.Zulu)
        };
        JavaVendorBox.SelectedValue = Enum.IsDefined(state.JavaDownloadVendor) ? state.JavaDownloadVendor : JavaVendor.Temurin;
        UpdateJavaDownloadSource();
    }

    private JavaVendor ReadJavaVendor() => JavaVendorBox.SelectedValue is JavaVendor vendor ? vendor : JavaVendor.Temurin;
    private JavaDownloadSource ReadJavaSource() => JavaSourceBox.SelectedValue is JavaDownloadSource source ? source : JavaDownloadSource.Auto;
    private void ChangeJavaDownloadOptions(object sender, SelectionChangedEventArgs e) => UpdateJavaDownloadSource();

    private void UpdateJavaDownloadSource()
    {
        if (JavaSourceBox is null || JavaDownloadHint is null) return;
        var temurin = ReadJavaVendor() == JavaVendor.Temurin;
        JavaSourceBox.IsEnabled = temurin;
        JavaDownloadHint.Text = (temurin ? "Temurin 按所选镜像优先下载，失败后尝试其他镜像和官方源。"
            : "Zulu 使用 Azul 官方下载源。") + " 启动时的下载偏好在保存设置后生效。";
        if (temurin && ReadJavaSource() == JavaDownloadSource.Official)
            JavaDownloadHint.Text = "Temurin 使用官方下载源。启动时的下载偏好在保存设置后生效。";
    }

    private async void DownloadJava(object sender, RoutedEventArgs e)
    {
        if (JavaVersionBox.SelectedValue is not int major) return;
        var vendor = ReadJavaVendor();
        var source = ReadJavaSource();
        await RunOperationAsync(async token =>
        {
            await DownloadJavaAsync(major, vendor, source, token);
        }, $"正在准备 Java {major}…");
    }

    private async Task<JavaInstallation> DownloadJavaAsync(int major, JavaVendor vendor, JavaDownloadSource source, CancellationToken token)
    {
        if (!Enum.IsDefined(vendor)) vendor = JavaVendor.Temurin;
        if (!Enum.IsDefined(source)) source = JavaDownloadSource.Auto;
        var java = await javaDownloads.InstallAsync(major, vendor, source, Progress(), token);
        await JavaRuntimes.RegisterAsync(java, token);
        ApplyJavaChoices(JavaRuntimes.Snapshot().Items, ReadJavaValue());
        JavaStatus.Text = $"Java {java.Version} · {java.Vendor} 已可用。\n{java.Path}";
        StatusLabel.Text = $"●  Java {java.Major} 已可用";
        Log(JavaStatus.Text);
        return java;
    }
}
