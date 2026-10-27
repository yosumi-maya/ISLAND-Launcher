using System.Windows;
using System.Windows.Controls;
using Island.Core;

namespace Island.App;

public sealed class BrowserLoginWindow : FormWindow
{
    private readonly TextBlock status = new() { Name = "BrowserLoginStatus", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private readonly Button browserButton;
    private readonly Func<Action<string>, CancellationToken, Task<AuthResult>> signIn;
    private readonly Action<string> openBrowser;
    private readonly CancellationToken external;
    private CancellationTokenSource? attempt;
    private string? address;
    private bool closed;
    public AuthResult? Result { get; private set; }
    public bool UseDeviceCode { get; private set; }

    public BrowserLoginWindow(Window owner, MicrosoftAuth auth, string clientId, CancellationToken external)
        : this(owner, async (open, token) =>
        {
            token.ThrowIfCancellationRequested();
            using var login = auth.BeginBrowserLogin(clientId);
            token.ThrowIfCancellationRequested();
            open(login.AuthorizationUri.AbsoluteUri);
            return await auth.CompleteBrowserAsync(login, token);
        }, external) { }

    internal BrowserLoginWindow(Window owner, Func<Action<string>, CancellationToken, Task<AuthResult>> signIn,
        CancellationToken external, Action<string>? openBrowser = null)
        : base(owner, "登录 Microsoft", "在系统浏览器的 Microsoft 官方页面授权，完成后会自动返回。启动器不会收集你的密码。", 600)
    {
        this.signIn = signIn; this.external = external; this.openBrowser = openBrowser ?? MainWindow.OpenPath;
        Body.Children.Add(new TextBlock { Text = "浏览器登录", FontSize = 16, FontWeight = FontWeights.SemiBold });
        Body.Children.Add(status);
        browserButton = Button("重新打开浏览器  ↗", (_, _) => OpenOrRetry(), true); browserButton.Name = "ReopenMicrosoftBrowser";
        Actions.Children.Add(Button("取消", (_, _) => Close()));
        var fallback = Button("改用设备码登录", (_, _) => { UseDeviceCode = true; Close(); }); fallback.Name = "UseMicrosoftDeviceCode";
        Actions.Children.Add(fallback); Actions.Children.Add(browserButton); Body.Children.Add(Actions);
        Loaded += async (_, _) => await StartAsync();
        Closed += (_, _) => { closed = true; attempt?.Cancel(); };
    }

    private async void OpenOrRetry()
    {
        if (attempt is not null && address is not null)
        {
            try { openBrowser(address); }
            catch { status.Text = "无法打开系统浏览器，请检查默认浏览器或改用设备码登录。"; }
        }
        else await StartAsync();
    }
    private async Task StartAsync()
    {
        if (attempt is not null || closed) return;
        var current = CancellationTokenSource.CreateLinkedTokenSource(external); attempt = current; address = null;
        browserButton.Content = "重新打开浏览器  ↗"; status.Text = "正在打开 Microsoft 官方登录页…";
        try
        {
            var result = await signIn(url =>
            {
                address = url; openBrowser(url);
                status.Text = "已打开浏览器。授权后将自动验证 Xbox 身份、Minecraft 游戏资格和玩家档案，请稍候。";
            }, current.Token);
            current.Token.ThrowIfCancellationRequested();
            if (!closed) { Result = result; DialogResult = true; }
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested) { if (!closed) Close(); }
        catch (Exception ex)
        {
            if (!closed) { status.Text = ex.Message + "\n可以重试，或改用设备码登录。"; browserButton.Content = "重试浏览器登录  ↗"; }
        }
        finally { attempt = null; address = null; current.Dispose(); }
    }
}
