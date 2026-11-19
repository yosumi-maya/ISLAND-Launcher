using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Island.App;
using Island.Core;
internal static class MultiplayerUiTests
{
    private sealed class Engine:ITerracottaSession
    {
        public bool IsStarted{get;private set;}
        public bool HoldStart,FailPoll;public int Starts,Stops,Joins,Polls;public bool StartCanceled;
        public TerracottaState Current=new("waiting",0,"","","",null,[]);
        public async Task StartAsync(IProgress<string>? progress,CancellationToken token)
        {
            Starts++;progress?.Report("正在准备测试组件…");
            if(HoldStart)try{await Task.Delay(Timeout.Infinite,token);}catch(OperationCanceledException){StartCanceled=true;throw;}
            IsStarted=true;
        }
        public Task ImportAsync(string archive,CancellationToken token)=>Task.CompletedTask;
        public Task<TerracottaState> StateAsync(CancellationToken token)
        {Polls++;if(FailPoll)throw new TaskCanceledException("mock HTTP timeout");return Task.FromResult(Current);}
        public Task HostAsync(string player,CancellationToken token){Set("host-scanning");return Task.CompletedTask;}
        public Task JoinAsync(string room,string player,CancellationToken token){Joins++;Set("guest-connecting",room);return Task.CompletedTask;}
        public Task LeaveAsync(CancellationToken token){Set("waiting");return Task.CompletedTask;}
        public Task ShutdownAsync(){Stops++;IsStarted=false;return Task.CompletedTask;}
        public void Set(string state,string room="",string address="")=>Current=new(state,Current.Index+1,room,address,"MEDIUM",null,state is "host-ok" or "guest-ok" ? [new("好友","Terracotta 0.4.2","HOST"),new("UiTester","ISLAND","LOCAL")] : []);
    }
    private static IEnumerable<T> Children<T>(DependencyObject root) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T t)yield return t;foreach(var nested in Children<T>(child))yield return nested;}
    }
    private static T Find<T>(MainWindow window,string name) where T:FrameworkElement=>(T)window.FindName(name);
    private static T Field<T>(MultiplayerView view,string name) where T:FrameworkElement=>(T)view.FindName(name);
    private static void Click(Button b)=>b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static async Task Wait(Func<bool> ready)
    {var until=DateTime.UtcNow.AddSeconds(12);while(!ready() && DateTime.UtcNow<until)await Task.Delay(40);if(!ready())throw new Exception("Multiplayer UI did not reach the expected state");}
    public static async Task RunAsync(Window owner,string root,Action<bool,string> check,Action<Window,string> capture)
    {
        var store=new LauncherStore(Path.Combine(root,"multiplayer-ui"));var state=new LauncherState {GameDirectory=Path.Combine(root,"multiplayer-game")};store.Save(state);
        var engine=new Engine();var window=new MainWindow(store,multiplayerSession:engine){Owner=owner,Left=-16000,Top=0,WindowStartupLocation=WindowStartupLocation.Manual,ShowInTaskbar=false};
        try
        {
            window.Show();Find<RadioButton>(window,"NavMultiplayer").IsChecked=true;window.UpdateLayout();
            var page=Find<MultiplayerView>(window,"MultiplayerPage");
            Button Button(string name)=>Field<Button>(page,name);TextBlock Text(string name)=>Field<TextBlock>(page,name);
            check(engine.Starts==0 && page.IsVisible,"Opening multiplayer does not start or download the component");
            var doll=Find<SkinPreview3D>(window,"SidebarDoll");
            check(System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(doll).Height>=doll.ActualHeight+doll.Margin.Top+doll.Margin.Bottom,$"Added navigation leaves the complete paper doll visible at normal window size (slot={System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(doll).Height}, doll={doll.ActualHeight})");
            capture(window,"Multiplayer-Ready-1240");window.Width=1020;window.Height=700;window.UpdateLayout();
            check(System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(doll).Height>=doll.ActualHeight+doll.Margin.Top+doll.Margin.Bottom,$"Compact navigation leaves the complete paper doll visible at minimum window size (slot={System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(doll).Height}, doll={doll.ActualHeight})");
            capture(window,"Multiplayer-Ready-1020");
            Click(Button("HostButton"));await Wait(()=>Text("StateText").Text.Contains("等待游戏"));
            check(engine.Starts==1 && !Button("JoinButton").IsEnabled && Button("LeaveButton").IsEnabled,"Hosting waits for a LAN world and prevents duplicate room commands");
            engine.Set("host-ok","U/ABCD-1234-EFGH-5678");await Wait(()=>Field<TextBox>(page,"RoomValue").Text.Contains("ABCD"));
            check(Field<ItemsControl>(page,"Members").Items.Count==2,"Host room code and members update from polling");capture(window,"Multiplayer-Host-1020");
            var previous=engine.Polls;Find<RadioButton>(window,"NavLibrary").IsChecked=true;
            check(engine.IsStarted,"Navigating away preserves the room");Find<RadioButton>(window,"NavMultiplayer").IsChecked=true;
            Click(Button("LeaveButton"));await Wait(()=>Button("HostButton").IsEnabled);
            check(!page.HasRoom && Field<Grid>(page,"RoomPanel").Visibility==Visibility.Collapsed,"Leaving a room clears its code and re-enables room creation");
            Field<TextBox>(page,"RoomInput").Text="bad room";Click(Button("JoinButton"));await Wait(()=>Text("Feedback").Text.Contains("完整"));check(engine.Joins==0,"Malformed room codes are rejected before issuing a join command");
            Field<TextBox>(page,"RoomInput").Text="U/ABCD-1234-EFGH-5678";Click(Button("JoinButton"));await Wait(()=>engine.Joins==1 && page.HasRoom && Button("LeaveButton").IsEnabled);
            engine.Set("guest-ok",address:"127.0.0.1:25566");await Wait(()=>Field<TextBox>(page,"RoomValue").Text=="127.0.0.1:25566");
            check(Text("DetailText").Text.Contains("多人游戏") && Text("RoomCaption").Text.Contains("地址"),"Successful joining shows the game entry instructions and local address");capture(window,"Multiplayer-Guest-1020");
            var themes=Find<WrapPanel>(window,"ThemePresetButtons").Children.OfType<Button>().ToArray();
            Click(themes.Single(b=>b.Tag as string=="#5DAF98"));await Task.Delay(120);Find<RadioButton>(window,"NavMultiplayer").IsChecked=true;window.UpdateLayout();capture(window,"Multiplayer-Guest-Dark-1020");
            check(AppearanceRenderer.Current.DarkTheme,"Multiplayer inherits the saved dark appearance preset");
            Click(themes.Single(b=>b.Tag as string=="#32634D"));await Task.Delay(120);Find<RadioButton>(window,"NavMultiplayer").IsChecked=true;
            _=window.Dispatcher.BeginInvoke(new Action(()=>Click(Children<Button>(Application.Current.Windows.OfType<MessageWindow>().Single()).Single(b=>b.Content as string=="取消"))));
            window.Close();check(window.IsVisible && engine.IsStarted,"Declining room shutdown preserves the window and active session");
            engine.FailPoll=true;await Wait(()=>Text("StateText").Text.Contains("失去响应"));
            check(!engine.IsStarted && engine.Stops>0 && Button("HostButton").IsEnabled,"Repeated HTTP timeouts stop the failed session and offer a retry");
            engine.FailPoll=false;engine.HoldStart=true;var beforeStart=engine.Starts;Click(Button("HostButton"));await Wait(()=>engine.Starts>beforeStart && Button("CancelButton").IsVisible);Click(Button("CancelButton"));
            await Wait(()=>engine.StartCanceled && Button("HostButton").IsEnabled);check(Text("StateText").Text.Contains("取消"),"Canceling component startup cancels its request and restores controls");
            engine.StartCanceled=false;beforeStart=engine.Starts;Click(Button("HostButton"));await Wait(()=>engine.Starts>beforeStart && Button("CancelButton").IsVisible);window.Close();await Wait(()=>!window.IsVisible);
            check(engine.StartCanceled && !engine.IsStarted,"Closing during component startup cancels and cleans up without reentrant window closure");
        }
        finally{await Find<MultiplayerView>(window,"MultiplayerPage").ShutdownAsync();if(window.IsVisible)window.Close();}
    }
}
