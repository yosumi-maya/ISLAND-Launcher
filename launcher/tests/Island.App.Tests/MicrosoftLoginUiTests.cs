using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Island.App;
using Island.Core;

internal static class MicrosoftLoginUiTests
{
    private static IEnumerable<T> Children<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); if (child is T item) yield return item;
            foreach (var nested in Children<T>(child)) yield return nested;
        }
    }
    private static async Task Wait(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!predicate() && DateTime.UtcNow < deadline) await Task.Delay(20);
        if (!predicate()) throw new Exception("Microsoft login UI did not reach the expected state.");
    }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static BrowserLoginWindow Window(Window owner, Func<Action<string>, CancellationToken, Task<AuthResult>> authenticate, Action<string> open, CancellationToken token = default, bool startOnLoaded = true)
    {
        var window = new BrowserLoginWindow(owner, authenticate, token, open) { Left = -16000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
        if (startOnLoaded) window.Loaded += (_, _) => Click(Children<Button>(window).Single(button => button.Name == "ReopenMicrosoftBrowser"));
        return window;
    }

    public static async Task RunAsync(Window owner, Action<bool, string> check, Action<Window, string> capture)
    {
        var opens = 0; var canceled = false; var stopped = false; var starts = 0; string? lastUrl = null;
        async Task<AuthResult> Pending(Action<string> open, CancellationToken token)
        {
            starts++; open("https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize");
            try { await Task.Delay(Timeout.Infinite, token); throw new Exception("Unexpected pending completion."); }
            catch (OperationCanceledException) { canceled = true; throw; }
            finally { stopped = true; }
        }
        var pending = Window(owner, Pending, url => { opens++; lastUrl = url; }, startOnLoaded: false);
        try
        {
            pending.Show(); await Task.Delay(100); pending.UpdateLayout();
            check(opens == 0 && starts == 0 && Children<Button>(pending).Single(b => b.Name == "ReopenMicrosoftBrowser").Content as string == "打开浏览器登录  ↗",
                "Opening Microsoft login presents the method choice without starting authorization or opening the browser");
            capture(pending, "Microsoft-Login-Choice");
            var purchase = Children<Button>(pending).Single(b => b.Name == "PurchaseMinecraftButton");
            Click(purchase);
            check(lastUrl == FormWindow.MinecraftPurchaseUrl && starts == 0 && pending.IsVisible,
                "Purchase link opens only the official Minecraft store and does not begin login: url=" + lastUrl + ", starts=" + starts + ", visible=" + pending.IsVisible);
            check(purchase.TranslatePoint(new Point(),pending).X < Children<Button>(pending).Single(b=>b.Name=="UseMicrosoftDeviceCode").TranslatePoint(new Point(),pending).X,
                "Purchase link occupies the left side of the login footer");
            opens = 0;
            Click(Children<Button>(pending).Single(b => b.Name == "ReopenMicrosoftBrowser"));
            await Wait(() => opens == 1); pending.UpdateLayout();
            var buttons = Children<Button>(pending).ToArray();
            check(Children<TextBlock>(pending).Single(t => t.Name == "BrowserLoginStatus").Text.Contains("已打开浏览器")
                && buttons.Any(b => b.Name == "UseMicrosoftDeviceCode") && pending.Result is null, "Microsoft login opens the system browser and keeps a visible device-code alternative while waiting");
            Click(buttons.Single(b => b.Name == "ReopenMicrosoftBrowser"));
            check(opens == 2, "Reopening the browser reuses the active authorization attempt");
            capture(pending, "Microsoft-Browser-Login");
            check(buttons.Where(b => b.Name is "UseMicrosoftDeviceCode" or "ReopenMicrosoftBrowser").All(b => b.ActualWidth >= 80 && b.TranslatePoint(new Point(b.ActualWidth, 0), pending).X < pending.ActualWidth), "Login buttons remain within the native dialog at normal text sizing");
            Click(buttons.Single(b => b.Name == "UseMicrosoftDeviceCode")); await Wait(() => stopped);
            check(canceled && pending.UseDeviceCode && pending.Result is null && !pending.IsVisible, "Choosing device-code login closes and cancels the browser attempt before the caller starts a new flow");
        }
        finally { pending.Close(); }

        var idleDeviceStarts = 0; var idleDeviceOpens = 0;
        var idleDevice = Window(owner, (_, _) => { idleDeviceStarts++; return Task.FromException<AuthResult>(new Exception("Must not start")); }, _ => idleDeviceOpens++, startOnLoaded: false);
        idleDevice.Show(); await Task.Delay(40);
        Click(Children<Button>(idleDevice).Single(button => button.Name == "UseMicrosoftDeviceCode"));
        check(idleDevice.UseDeviceCode && idleDeviceStarts == 0 && idleDeviceOpens == 0 && !idleDevice.IsVisible,
            "Choosing device-code from the initial dialog never starts a browser authorization attempt");

        var attempts = 0;
        var failure = Window(owner, (_, _) => { attempts++; return Task.FromException<AuthResult>(new InvalidOperationException("模拟：尚未配置本机回调")); }, _ => { });
        try
        {
            failure.Show(); await Wait(() => Children<TextBlock>(failure).Any(t => t.Text.Contains("模拟："))); failure.UpdateLayout();
            check(Children<TextBlock>(failure).Any(t => t.Text.Contains("改用设备码登录")) && failure.Result is null, "Browser failures retain the dialog and explain retry or fallback instead of saving an account");
            Click(Children<Button>(failure).Single(b => b.Name == "ReopenMicrosoftBrowser"));
            await Wait(() => attempts == 2);
            check(attempts == 2, "Retry starts a fresh authorization attempt after a failure");
            capture(failure, "Microsoft-Browser-Retry");
        }
        finally { failure.Close(); }

        using (var failedCancellation = new CancellationTokenSource())
        {
            var failed = Window(owner, (_, _) => Task.FromException<AuthResult>(new IOException("模拟登录失败")), _ => { }, failedCancellation.Token);
            try
            {
                failed.Show(); await Wait(() => Children<TextBlock>(failed).Any(text => text.Text.Contains("模拟登录失败")));
                failedCancellation.Cancel(); await Wait(() => !failed.IsVisible);
                check(failed.Result is null, "A failed browser login still responds to external cancellation after its attempt has finished");
            }
            finally { failed.Close(); }
        }
        using (var lateCancellation = new CancellationTokenSource())
        {
            var lateResponse = new TaskCompletionSource<AuthResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var late = Window(owner, (_, _) => lateResponse.Task, _ => { }, lateCancellation.Token);
            try
            {
                late.Show(); lateCancellation.Cancel(); await Wait(() => !late.IsVisible);
                lateResponse.SetException(new IOException("模拟服务在取消后才返回错误")); await Task.Delay(40);
                check(late.Result is null, "Cancellation closes the login dialog even when a provider ignores its token and later fails");
            }
            finally { late.Close(); }
        }

        var profile = new LauncherStore(Path.Combine(Path.GetTempPath(), "Island Login Closing " + Guid.NewGuid().ToString("N")));
        profile.Save(new LauncherState { GameDirectory = Path.Combine(profile.DirectoryPath, "game") });
        var host = new MainWindow(profile) { Left = -16000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
        try
        {
            host.Show();
            var failedTask = host.RunOperationAsync(async token =>
            {
                var dialog = Window(host, (_, _) => Task.FromException<AuthResult>(new IOException("失败后取消")), _ => { }, token);
                _ = host.Dispatcher.BeginInvoke(new Action(async () =>
                {
                    await Wait(() => Children<TextBlock>(dialog).Any(text => text.Text.Contains("失败后取消")));
                    Click((Button)host.FindName("CancelButton"));
                }));
                dialog.ShowDialog(); token.ThrowIfCancellationRequested();
                await Task.CompletedTask;
            }, "正在准备 Microsoft 登录…");
            await failedTask;
            check(((Border)host.FindName("OperationPanel")).Visibility == Visibility.Collapsed && host.IsEnabled,
                "Canceling failed Microsoft login clears the launcher's busy panel and restores the main window");

            var closingTask = host.RunOperationAsync(async token =>
            {
                // Emulate a login task whose cancellation finishes synchronously inside Closing.
                await Task.Delay(Timeout.Infinite, token);
            }, "正在准备 Microsoft 登录…");
            _ = host.Dispatcher.BeginInvoke(new Action(async () =>
            {
                await Wait(() => Application.Current.Windows.OfType<MessageWindow>().Any());
                var prompt = Application.Current.Windows.OfType<MessageWindow>().Single();
                Click(Children<Button>(prompt).Single(button => button.Content as string == "关闭启动器"));
            }));
            host.Close(); await closingTask; await Wait(() => !host.IsVisible);
            check(!host.IsVisible, "Closing the launcher while login is pending waits for cancellation and avoids reentrant Close calls");
        }
        finally { if (host.IsVisible) host.Close(); }

        opens = 0; stopped = false; canceled = false;
        using var cancellation = new CancellationTokenSource();
        var closing = Window(owner, Pending, _ => opens++, cancellation.Token);
        closing.Show(); await Wait(() => opens == 1); cancellation.Cancel(); await Wait(() => stopped && !closing.IsVisible);
        check(canceled && closing.Result is null && !closing.UseDeviceCode, "External cancellation closes the dialog without automatically starting another login flow");

        var accepted = PlayerAccount.Offline("AuthTester"); accepted.Microsoft = true;
        using (var deviceCancellation = new CancellationTokenSource())
        {
            var device = new DeviceLogin("fixture", "TEST-CODE", "https://microsoft.com/link", 5, 60);
            var failedDevice = new DeviceLoginWindow(owner, device, _ => Task.FromException<AuthResult>(new IOException("模拟设备码登录失败")), deviceCancellation.Token)
                { Left = -16000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
            try
            {
                failedDevice.Show(); await Wait(() => Children<TextBlock>(failedDevice).Any(text => text.Text.Contains("模拟设备码登录失败")));
                check(Children<Button>(failedDevice).Any(button => button.Content as string == "取消"), "Device-code login exposes an explicit Cancel action after failure");
                deviceCancellation.Cancel(); await Wait(() => !failedDevice.IsVisible);
                check(failedDevice.Result is null, "Failed device-code login closes when the launcher operation is canceled");
            }
            finally { failedDevice.Close(); }
        }
        using (var explicitDeviceCancellation = new CancellationTokenSource())
        {
            var deviceOpens = new List<string>();
            var device = new DeviceLogin("fixture", "TEST-CODE", "https://microsoft.com/link", 5, 60);
            var dialog = new DeviceLoginWindow(owner, device, async token => { await Task.Delay(Timeout.Infinite, token); throw new Exception(); },
                explicitDeviceCancellation.Token, deviceOpens.Add, openBrowserOnLoad: true)
                { Left = -16000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
            dialog.Show(); await Wait(()=>deviceOpens.Count==1);
            check(deviceOpens.Single()==device.VerificationUrl && dialog.Result is null,
                "Explicit device-code choice opens its official verification page once while waiting for authorization");
            capture(dialog,"Microsoft-Device-Login"); explicitDeviceCancellation.Cancel(); await Wait(()=>!dialog.IsVisible);
        }
        if (owner is MainWindow main)
        {
            ((RadioButton)main.FindName("NavAccounts")).IsChecked = true;
            Exception? promptError = null;
            _ = main.Dispatcher.BeginInvoke(new Action(()=>
            {
                var dialog=Application.Current.Windows.OfType<FormWindow>().Single(window=>window.Title=="添加本地玩家");
                try
                {
                    dialog.UpdateLayout(); var link=Children<Button>(dialog).Single(button=>button.Name=="PurchaseMinecraftButton");
                    check(link.IsVisible && link.TranslatePoint(new Point(),dialog).X < Children<Button>(dialog).Single(button=>button.Content as string=="取消").TranslatePoint(new Point(),dialog).X,
                        "Local-player dialog also exposes the official purchase link on the left"); capture(dialog,"Local-Player-Purchase");
                }
                catch(Exception ex){promptError=ex;}
                finally{dialog.Close();}
            }));
            Click(Children<Button>(main).Single(button=>button.Content as string=="添加本地账号"));
            if(promptError is not null)throw promptError;
            check(main.FindName("RootBox") is null,"Settings no longer expose a second game-root editor");
            ((RadioButton)main.FindName("NavSettings")).IsChecked = true;
            ((ScrollViewer)main.FindName("SettingsPage")).ScrollToBottom(); main.UpdateLayout(); await Task.Delay(60);
            capture(main,"Settings-Github-Downloads");
            ((RadioButton)main.FindName("NavAccounts")).IsChecked = true;

        }
        var expected = new AuthResult(accepted, new(accepted.Name, accepted.Uuid, "test-session", true));
        var success = Window(owner, (_, _) => Task.FromResult(expected), _ => { });
        check(success.ShowDialog() == true && success.Result == expected && !success.UseDeviceCode, "A verified result completes the native modal dialog and returns the authenticated account");
    }
}
