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
    private static BrowserLoginWindow Window(Window owner, Func<Action<string>, CancellationToken, Task<AuthResult>> authenticate, Action<string> open, CancellationToken token = default)
        => new(owner, authenticate, token, open) { Left = -16000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };

    public static async Task RunAsync(Window owner, Action<bool, string> check, Action<Window, string> capture)
    {
        var opens = 0; var canceled = false; var stopped = false;
        async Task<AuthResult> Pending(Action<string> open, CancellationToken token)
        {
            open("https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize");
            try { await Task.Delay(Timeout.Infinite, token); throw new Exception("Unexpected pending completion."); }
            catch (OperationCanceledException) { canceled = true; throw; }
            finally { stopped = true; }
        }
        var pending = Window(owner, Pending, _ => opens++);
        try
        {
            pending.Show(); await Wait(() => opens == 1); pending.UpdateLayout();
            var buttons = Children<Button>(pending).ToArray();
            check(Children<TextBlock>(pending).Single(t => t.Name == "BrowserLoginStatus").Text.Contains("已打开浏览器")
                && buttons.Any(b => b.Name == "UseMicrosoftDeviceCode") && pending.Result is null, "Microsoft login opens the system browser and keeps a visible device-code alternative while waiting");
            Click(buttons.Single(b => b.Name == "ReopenMicrosoftBrowser"));
            check(opens == 2, "Reopening the browser reuses the active authorization attempt");
            capture(pending, "Microsoft-Browser-Login");
            check(buttons.Where(b => b.Name is "UseMicrosoftDeviceCode" or "ReopenMicrosoftBrowser").All(b => b.ActualWidth > 100 && b.TranslatePoint(new Point(b.ActualWidth, 0), pending).X < pending.ActualWidth), "Login buttons remain within the native dialog at normal text sizing");
            Click(buttons.Single(b => b.Name == "UseMicrosoftDeviceCode")); await Wait(() => stopped);
            check(canceled && pending.UseDeviceCode && pending.Result is null && !pending.IsVisible, "Choosing device-code login closes and cancels the browser attempt before the caller starts a new flow");
        }
        finally { pending.Close(); }

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

        opens = 0; stopped = false; canceled = false;
        using var cancellation = new CancellationTokenSource();
        var closing = Window(owner, Pending, _ => opens++, cancellation.Token);
        closing.Show(); await Wait(() => opens == 1); cancellation.Cancel(); await Wait(() => stopped && !closing.IsVisible);
        check(canceled && closing.Result is null && !closing.UseDeviceCode, "External cancellation closes the dialog without automatically starting another login flow");

        var accepted = PlayerAccount.Offline("AuthTester"); accepted.Microsoft = true;
        var expected = new AuthResult(accepted, new(accepted.Name, accepted.Uuid, "test-session", true));
        var success = Window(owner, (_, _) => Task.FromResult(expected), _ => { });
        check(success.ShowDialog() == true && success.Result == expected && !success.UseDeviceCode, "A verified result completes the native modal dialog and returns the authenticated account");
    }
}
