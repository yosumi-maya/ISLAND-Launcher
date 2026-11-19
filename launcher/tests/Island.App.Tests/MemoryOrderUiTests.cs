using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using BlockDock.Core;
using Island.App;
using Island.Core;

internal static class MemoryOrderUiTests
{
    private static void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static async Task Wait(Func<bool> ready)
    {
        var end=DateTime.UtcNow.AddSeconds(8);
        while(!ready() && DateTime.UtcNow<end)await Task.Delay(20);
        if(!ready())throw new Exception("Launch ordering did not reach the expected state.");
    }
    public static async Task RunAsync(Window owner,string root,Action<bool,string> check)
    {
        var directory=Path.Combine(root,"memory-order","game");var version=Path.Combine(directory,"versions","Fixture");Directory.CreateDirectory(version);
        var metadata=Path.Combine(version,"Fixture.json");
        File.WriteAllText(metadata,"""{"id":"Fixture","mainClass":"UnusedFixture","arguments":{"game":[]}}""");
        var account=PlayerAccount.Offline("OrderTest");var state=new LauncherState{GameDirectory=directory,CleanMemoryBeforeLaunch=true};state.Accounts.Add(account);state.SelectedAccountId=account.Id;state.EnsureGameFolders();state.For("Fixture").LastPlayed=DateTimeOffset.Now;
        var store=new LauncherStore(Path.Combine(root,"memory-order","settings"));store.Save(state);
        TaskCompletionSource? gate=new(TaskCreationOptions.RunContinuationsAsynchronously);var cleanups=0;var dialogs=0;
        var outcome=MemoryCleanupOutcome.Completed;var fails=false;
        var cache=new JavaRuntimeCache(store.DirectoryPath,(_,_,_)=>Task.FromResult<IReadOnlyList<JavaInstallation>>([]));
        var window=new MainWindow(store,memoryCleaner:async token=>
        {
            cleanups++;if(gate is not null)await gate.Task.WaitAsync(token);
            if(fails)throw new IOException("fixture cleanup failure");
            return new MemoryCleanupResult(outcome);
        },javaRuntimes:cache){Owner=owner,Left=-16000,Top=0,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual};
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};
        timer.Tick+=(_,_)=>
        {
            foreach(var dialog in Application.Current.Windows.OfType<MessageWindow>().Where(w=>w.Owner==window && w.IsVisible).ToArray()){dialogs++;dialog.Close();}
        };
        try
        {
            window.Show();window.UpdateLayout();timer.Start();await Task.Delay(80);
            var log=(TextBox)window.FindName("LogBox");var launch=(Button)window.FindName("LaunchButton");var panel=(Border)window.FindName("OperationPanel");
            // Corrupt metadata only after the library has loaded. Reading it before cleanup would fail immediately.
            File.WriteAllText(metadata,"not valid JSON");log.Clear();Click(launch);
            await Wait(()=>cleanups==1);
            check(((TextBlock)window.FindName("OperationLabel")).Text.Contains("清理内存") && panel.IsVisible && !launch.IsEnabled,"Launch immediately enters cleanup and prevents another launch");
            await Task.Delay(100);
            check(dialogs==0 && !log.Text.Contains("启动未完成") && !log.Text.Contains("版本配置已读取") && !log.Text.Contains("Java 已匹配") && !log.Text.Contains("依赖与资源已检查"),"Blocked cleanup precedes even invalid version parsing, Java matching and dependency preparation");
            Click((Button)window.FindName("CancelButton"));await Wait(()=>!panel.IsVisible);
            check(cleanups==1 && dialogs==0 && log.Text.Contains("任务已取消") && !log.Text.Contains("游戏进程已启动"),"Cancel during the first cleanup stops the entire launch before metadata errors or Java execution");
            gate=null;outcome=MemoryCleanupOutcome.AuthorizationDeclined;log.Clear();Click(launch);await Wait(()=>dialogs==1 && !panel.IsVisible);
            check(cleanups==2 && log.Text.Contains("已取消管理员授权") && log.Text.IndexOf("内存清理已结束",StringComparison.Ordinal)<log.Text.IndexOf("错误：",StringComparison.Ordinal),"Declined elevation is resolved first and then the existing launch flow continues");
            fails=true;log.Clear();Click(launch);await Wait(()=>dialogs==2 && !panel.IsVisible);
            check(cleanups==3 && log.Text.Contains("fixture cleanup failure") && log.Text.IndexOf("内存清理已结束",StringComparison.Ordinal)<log.Text.IndexOf("错误：",StringComparison.Ordinal),"Cleanup failure is reported before later preparation and keeps its existing fallback policy");
            var option=(CheckBox)window.FindName("CleanMemoryBeforeLaunchBox");option.IsChecked=false;option.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            log.Clear();Click(launch);await Wait(()=>dialogs==3 && !panel.IsVisible);
            check(cleanups==3 && log.Text.Contains("启动前内存清理未启用"),"Disabling cleanup skips the cleaner and proceeds directly to normal preparation");
            option.IsChecked=true;option.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));fails=false;outcome=MemoryCleanupOutcome.Completed;
            gate=new(TaskCreationOptions.RunContinuationsAsynchronously);
            log.Clear();Click((Button)window.FindName("ContinueButton"));
            await Wait(()=>cleanups==4);
            check(dialogs==3 && !log.Text.Contains("错误："), "Continue also waits for cleanup before refreshing a directory with invalid metadata");
            gate.SetResult(); gate=null;
            await Wait(()=>dialogs==4 && !panel.IsVisible);
            check(cleanups==4 && log.Text.IndexOf("内存清理已结束",StringComparison.Ordinal)>=0 && log.Text.IndexOf("内存清理已结束",StringComparison.Ordinal)<log.Text.IndexOf("错误：",StringComparison.Ordinal),"Continue completes exactly one cleanup before reporting invalid metadata");
        }
        finally{timer.Stop();await ((DownloadCenter)window.FindName("DownloadCenterView")).ShutdownAsync();window.Close();}
    }
}
