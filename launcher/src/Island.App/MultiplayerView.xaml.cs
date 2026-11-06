using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Island.Core;
using Microsoft.Win32;
namespace Island.App;
public partial class MultiplayerView : UserControl
{
    private ITerracottaSession? session;
    private Func<string> player=()=>"ISLAND 玩家";
    private Action? openLogs;
    private readonly CancellationTokenSource lifetime=new();
    private readonly DispatcherTimer timer=new() {Interval=TimeSpan.FromMilliseconds(500)};
    private CancellationTokenSource? request;
    private Task? running;
    private bool busy,polling,closed;
    private long lastIndex=-1;
    private int failures;
    private TerracottaState? current;
    public bool NeedsShutdown=>busy || session?.IsStarted==true;
    public bool HasRoom=>current?.InRoom==true;
    public MultiplayerView()
    {
        InitializeComponent();timer.Tick+=async (_,_)=>await RefreshStateAsync();
        IsVisibleChanged+=(_,_)=>timer.Interval=IsVisible ? TimeSpan.FromMilliseconds(500) : TimeSpan.FromSeconds(15);
        Unloaded+=(_,_)=>timer.Stop();Loaded+=(_,_)=>{if(session?.IsStarted==true)timer.Start();};
    }
    public void Initialize(ITerracottaSession engine,Func<string> selectedPlayer,Action logs)
    {session=engine;player=selectedPlayer;openLogs=logs;UpdateButtons();}
    private async Task Work(Func<CancellationToken,Task> action)
    {
        if(busy || closed || session is null)return;
        using var cancel=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);request=cancel;busy=true;Feedback.Text="";UpdateButtons();
        async Task Execute()
        {
            await Task.Yield();cancel.Token.ThrowIfCancellationRequested();await action(cancel.Token);
        }
        try{running=Execute();await running;}
        catch(OperationCanceledException) when(cancel.IsCancellationRequested)
        {
            await session.ShutdownAsync();timer.Stop();current=null;lastIndex=-1;
            if(!closed){SetupPanel.Visibility=Visibility.Visible;StateText.Text="已取消联机操作";DetailText.Text="可以重新创建或加入房间。";RoomPanel.Visibility=Visibility.Collapsed;Members.ItemsSource=null;MembersCaption.Visibility=Visibility.Collapsed;}
        }
        catch(Exception ex)
        {
            if(!closed)
            {
                Feedback.Text=ex is OperationCanceledException ? "联机请求超时，请检查网络后重试。" : ex.Message;
                if(!session.IsStarted){StateText.Text="联机准备未完成";DetailText.Text="可以重新尝试，或导入官方组件包。";}
            }
        }
        finally{busy=false;request=null;running=null;if(!closed)UpdateButtons();}
    }
    private async Task EnsureStarted(CancellationToken token)
    {
        if(session is null)throw new InvalidOperationException("联机组件尚未初始化。");
        if(!session.IsStarted)
        {
            lastIndex=-1;current=null;failures=0;
            StateText.Text="正在准备联机…";
            var progress=new Progress<string>(message=>{if(!closed && busy)DetailText.Text=message;});
            await session.StartAsync(progress,token);
        }
        timer.Start();
    }
    private async void Host(object sender,RoutedEventArgs e)=>await Work(async token=>
    {
        await EnsureStarted(token);await session!.HostAsync(player(),token);lastIndex=-1;await RefreshStateAsync();
    });
    private async void Join(object sender,RoutedEventArgs e)=>await Work(async token=>
    {
        var code=TerracottaApi.NormalizeRoom(RoomInput.Text);await EnsureStarted(token);await session!.JoinAsync(code,player(),token);lastIndex=-1;await RefreshStateAsync();
    });
    private async void Leave(object sender,RoutedEventArgs e)=>await Work(async token=>
    {
        await session!.LeaveAsync(token);lastIndex=-1;await RefreshStateAsync();Feedback.Text="已退出房间。";
    });
    private void Cancel(object sender,RoutedEventArgs e){CancelButton.IsEnabled=false;request?.Cancel();}
    private void UpdateButtons()
    {
        var active=current?.InRoom==true || current?.State=="exception";
        HostButton.IsEnabled=JoinButton.IsEnabled=RoomInput.IsEnabled=!busy && !active && !closed && session is not null;
        ImportButton.IsEnabled=!busy && session?.IsStarted!=true && !closed;
        Progress.Visibility=CancelButton.Visibility=busy ? Visibility.Visible : Visibility.Collapsed;CancelButton.IsEnabled=true;
        LeaveButton.Visibility=active ? Visibility.Visible : Visibility.Collapsed;LeaveButton.IsEnabled=!busy && !closed;
        LeaveButton.Content=current?.State is "guest-connecting" or "guest-starting" or "host-scanning" or "host-starting" ? "取消连接" : "退出房间";
    }
    private async Task RefreshStateAsync()
    {
        if(polling || session?.IsStarted!=true || closed)return;polling=true;
        try
        {
            var snapshot=await session.StateAsync(lifetime.Token);failures=0;
            if(closed || snapshot.Index<=lastIndex)return;
            lastIndex=snapshot.Index;current=snapshot;ShowState(snapshot);
        }
        catch(OperationCanceledException) when(lifetime.IsCancellationRequested){}
        catch(Exception)
        {
            if(!closed && ++failures>=5)
            {
                timer.Stop();await session.ShutdownAsync();lastIndex=-1;current=null;
                SetupPanel.Visibility=Visibility.Visible;StateText.Text="联机组件失去响应";DetailText.Text="本次连接已停止，可以重新创建或加入房间。";
                RoomPanel.Visibility=Visibility.Collapsed;Members.ItemsSource=null;MembersCaption.Visibility=Visibility.Collapsed;UpdateButtons();
            }
        }
        finally{polling=false;}
    }
    private void ShowState(TerracottaState value)
    {
        SetupPanel.Visibility=value.InRoom ? Visibility.Collapsed : Visibility.Visible;
        StateText.Text=value.Description;
        DetailText.Text=value.State switch {
            "host-scanning"=>"请进入游戏存档，按 Esc，选择“对局域网开放”。",
            "host-ok"=>"将下方房间码发给好友，并保持游戏存档与启动器运行。",
            "guest-ok"=>"打开游戏的多人游戏列表，双击“陶瓦联机大厅”；也可以用下方地址直接连接。",
            "guest-starting"=>"正在尝试 P2P 连接，较复杂的网络环境可能需要更长时间。",
            "guest-connecting"=>"请保持启动器运行。长时间无法连接时，可以取消后重试。",
            "waiting"=>"可以创建房间，或输入好友的房间码。",
            "exception"=>"请退出当前房间后重新尝试；双方也可以检查防火墙和网络。",_=>"正在等待联机组件更新状态…" };
        RoomValue.Text=value.State=="guest-ok" ? value.Address : value.Room;
        RoomCaption.Text=value.State=="guest-ok" ? "本地游戏地址" : "分享房间码";
        RoomPanel.Visibility=RoomValue.Text.Length>0 ? Visibility.Visible : Visibility.Collapsed;
        Members.ItemsSource=value.Members.Select(m=>new {m.Name,m.Vendor,Role=m.Kind switch {"HOST"=>"房主","LOCAL"=>"你",_=>"成员"}}).ToArray();
        MembersCaption.Visibility=value.Members.Count>0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateButtons();
    }
    private async void Import(object sender,RoutedEventArgs e)
    {
        var picker=new OpenFileDialog {Title="选择陶瓦 0.4.2 Windows x86_64 官方组件包",Filter="陶瓦官方安装包|*.tar.gz"};
        if(picker.ShowDialog(Window.GetWindow(this))!=true)return;
        await Work(async token=>{DetailText.Text="正在校验并安装陶瓦组件…";await session!.ImportAsync(picker.FileName,token);StateText.Text="陶瓦组件已准备好";DetailText.Text="可以创建或加入房间。";});
    }
    private void CopyRoom(object sender,RoutedEventArgs e)
    {try{Clipboard.SetText(RoomValue.Text);Feedback.Text="已复制。";}catch(Exception){Feedback.Text="暂时无法访问剪贴板，可以选中文字后手动复制。";}}
    private void OpenLogs(object sender,RoutedEventArgs e){try{openLogs?.Invoke();}catch(Exception ex){Feedback.Text=ex.Message;}}
    private void Open(string url){try{Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}catch(Exception ex){Feedback.Text=ex.Message;}}
    private void OpenDocs(object sender,RoutedEventArgs e)=>Open("https://burningtnt.github.io/Terracotta/");
    private void OpenLicense(object sender,RoutedEventArgs e)=>Open("https://github.com/burningtnt/Terracotta#许可");
    public async Task ShutdownAsync()
    {
        if(closed)return;closed=true;timer.Stop();lifetime.Cancel();request?.Cancel();UpdateButtons();
        if(running is not null)try{await running;}catch(Exception){}
        if(session is not null)await session.ShutdownAsync();
    }
}
