using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using Island.Core;

namespace Island.App;

public sealed class BrowserLoginWindow : FormWindow
{
    private readonly TextBlock status = new() { Name = "BrowserLoginStatus", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private readonly ProgressBar progress = new() { Name = "BrowserLoginProgress", Height = 4, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Hidden };
    private readonly Button browserButton;
    private readonly Func<Action<string>, Action, CancellationToken, Task<AuthResult>> signIn;
    private readonly Action<string> openBrowser;
    private readonly CancellationToken external;
    private CancellationTokenSource? attempt;
    private string? address;
    private bool closed;
    public AuthResult? Result { get; private set; }
    public bool UseDeviceCode { get; private set; }

    public BrowserLoginWindow(Window owner, MicrosoftAuth auth, string clientId, CancellationToken external)
        : this(owner, async (open, authorized, token) =>
        {
            token.ThrowIfCancellationRequested();
            using var login = auth.BeginBrowserLogin(clientId);
            token.ThrowIfCancellationRequested();
            open(login.AuthorizationUri.AbsoluteUri);
            return await auth.CompleteBrowserAsync(login, token, authorized);
        }, external) { }

    internal BrowserLoginWindow(Window owner, Func<Action<string>, Action, CancellationToken, Task<AuthResult>> signIn,
        CancellationToken external, Action<string>? openBrowser = null)
        : base(owner, "登录 Microsoft", "在系统浏览器的 Microsoft 官方页面授权，完成后会自动返回。启动器不会收集你的密码。", 600)
    {
        this.signIn = signIn; this.external = external; this.openBrowser = openBrowser ?? MainWindow.OpenPath;
        Body.Children.Add(new TextBlock { Text = "选择登录方式", FontSize = 16, FontWeight = FontWeights.SemiBold });
        Body.Children.Add(status);
        Body.Children.Add(progress);
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(progress, "正在登录 Microsoft");
        status.Text = "选择登录方式后，再打开 Microsoft 官方登录页。";
        browserButton = Button("打开浏览器登录  ↗", (_, _) => OpenOrRetry(), true); browserButton.Name = "ReopenMicrosoftBrowser";
        Actions.Children.Add(Button("取消", (_, _) => Close()));
        var fallback = Button("设备码登录", (_, _) => { UseDeviceCode = true; Close(); }); fallback.Name = "UseMicrosoftDeviceCode";
        Actions.Children.Add(fallback); Actions.Children.Add(browserButton); Body.Children.Add(Actions);
        AddMinecraftPurchaseLink(this.openBrowser);
        var cancellation = external.Register(() => Dispatcher.BeginInvoke(new Action(() => { if (!closed) Close(); })));
        Closed += (_, _) =>
        {
            closed = true;
            SetLoading(false);
            cancellation.Dispose();
            attempt?.Cancel();
        };
    }

    private void SetLoading(bool loading)
    {
        progress.IsIndeterminate = loading && SystemParameters.ClientAreaAnimation;
        progress.Visibility = progress.IsIndeterminate ? Visibility.Visible : Visibility.Hidden;
    }

    private void SetStatus(string message)
    {
        status.Text = message;
        if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
            UIElementAutomationPeer.CreatePeerForElement(status)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private async void OpenOrRetry()
    {
        if (attempt is not null && address is not null)
        {
            try { openBrowser(address); }
            catch { SetStatus("无法打开系统浏览器，请检查默认浏览器或改用设备码登录。"); }
        }
        else await StartAsync();
    }
    private async Task StartAsync()
    {
        if (attempt is not null || closed) return;
        var current = CancellationTokenSource.CreateLinkedTokenSource(external); attempt = current; address = null;
        browserButton.Content = "重新打开浏览器  ↗";
        SetStatus("正在打开 Microsoft 官方登录页…");
        SetLoading(true);
        try
        {
            var pending = signIn(url =>
            {
                if (closed || current.IsCancellationRequested || attempt != current) return;
                address = url; openBrowser(url);
                SetStatus("已打开浏览器，正在等待 Microsoft 授权…");
            }, () =>
            {
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (closed || current.IsCancellationRequested || attempt != current) return;
                    browserButton.IsEnabled = false;
                    SetStatus("已收到浏览器授权，正在验证 Xbox 身份、Minecraft 游戏资格和玩家档案…");
                }));
            }, current.Token);
            // A late provider response must not keep Cancel or closing the launcher blocked.
            _ = pending.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            var result = await pending.WaitAsync(current.Token);
            current.Token.ThrowIfCancellationRequested();
            if (!closed) { Result = result; DialogResult = true; }
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested) { if (!closed) Close(); }
        catch (Exception ex)
        {
            if (!closed)
            {
                SetStatus(ex.Message + "\n可以重试，或改用设备码登录。");
                browserButton.Content = "重试浏览器登录  ↗";
                browserButton.IsEnabled = true;
            }
        }
        finally
        {
            if (attempt == current)
            {
                SetLoading(false);
                attempt = null;
                address = null;
            }
            current.Dispose();
        }
    }
}
