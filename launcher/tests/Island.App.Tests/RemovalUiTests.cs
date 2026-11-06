using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using BlockDock.Core;
using Island.App;
using Island.Core;

internal static class RemovalUiTests
{
    private static IEnumerable<T> Children<T>(DependencyObject root) where T:DependencyObject
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {var child=VisualTreeHelper.GetChild(root,i);if(child is T item)yield return item;foreach(var nested in Children<T>(child))yield return nested;}
    }
    private static Button Button(Window window,string text)=>Children<Button>(window).Single(button=>button.Content as string==text);
    private static void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    internal static async Task RunAsync(Window owner,string root,Action<bool,string> check,Action<Window,string> capture)
    {
        var fixture=Path.Combine(root,"removal-ui");var first=Path.Combine(fixture,"first");var second=Path.Combine(fixture,"second");
        foreach(var directory in new[]{first,second})
        {
            var version=Path.Combine(directory,"versions","Beta");Directory.CreateDirectory(version);
            File.WriteAllText(Path.Combine(version,"Beta.json"),"""{"id":"Beta","mainClass":"Fixture","arguments":{"game":[]}}""");
            Directory.CreateDirectory(Path.Combine(directory,"mods"));File.WriteAllText(Path.Combine(directory,"mods","keep.jar"),"keep");
            Directory.CreateDirectory(Path.Combine(version,"saves"));File.WriteAllText(Path.Combine(version,"saves","own-world.txt"),"world");
        }
        var state=new LauncherState{GameDirectory=first};state.EnsureGameFolders();state.AddGameFolder(second,"Second").Isolation=VersionIsolation.Disabled;
        state.For(first,"Beta").DisplayName="First Beta";state.For(second,"Beta").DisplayName="Second Beta";state.RememberInstance("Beta");
        var store=new LauncherStore(Path.Combine(fixture,"settings"));store.Save(state);
        var recycled=Path.Combine(fixture,"owned-recycle");Directory.CreateDirectory(recycled);var calls=0;var mode="success";
        var cache=new JavaRuntimeCache(store.DirectoryPath,(_,_,_)=>Task.FromResult<IReadOnlyList<JavaInstallation>>([]));
        var window=new MainWindow(store,javaRuntimes:cache,downloadService:new DownloadCenterUiTests.Service(),instanceRecycler:path=>
        {
            calls++;if(!GameFolder.SamePath(path,Path.Combine(second,"versions","Beta")))throw new Exception("Unexpected deletion path");
            if(mode=="fail")throw new IOException("fixture deletion failed");if(mode=="cancel")throw new OperationCanceledException();if(mode=="noop")return;
            Directory.Move(path,Path.Combine(recycled,"Beta"));
        }) {Owner=owner,Left=-16000,Top=0,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual};
        void Modal<T>(Action trigger,Action<T> inspect) where T:Window
        {
            Exception? failure=null;
            window.Dispatcher.BeginInvoke(new Action(()=>
            {
                var dialog=Application.Current.Windows.OfType<T>().LastOrDefault(w=>w.IsVisible);
                if(dialog is null){failure=new Exception("Expected modal "+typeof(T).Name);return;}
                try{inspect(dialog);}catch(Exception ex){failure=ex;}finally{if(dialog.IsVisible)dialog.Close();}
            }));
            trigger();if(failure is not null)throw failure;
        }
        void Manage()
        {
            var list=(ListBox)window.FindName("InstanceList");window.UpdateLayout();
            var container=(ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem);
            var menu=container.ContextMenu;menu.PlacementTarget=container;menu.IsOpen=true;
            menu.Items.OfType<MenuItem>().Single(item=>item.Header as string=="实例管理").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }
        try
        {
            window.Show();window.UpdateLayout();
            Modal<GameFoldersWindow>(()=>Click((Button)window.FindName("ManageFoldersButton")),dialog=>
            {
                var remove=Children<Button>(dialog).Single(button=>button.Name=="RemoveGameFolderButton" && button.Tag as string==first);
                capture(dialog,"Game-Folders-Remove");Click(remove);
                check(Children<Button>(dialog).Single(button=>button.Name=="RemoveGameFolderButton").IsEnabled==false,"Directory manager keeps at least one row and disables its removal");
                Click(Button(dialog,"取消"));
            });
            check(store.Load().GameFolders.Count==2 && File.Exists(Path.Combine(first,"mods","keep.jar")),"Canceling directory edits preserves records and files");
            Modal<GameFoldersWindow>(()=>Click((Button)window.FindName("ManageFoldersButton")),dialog=>
            {Click(Children<Button>(dialog).Single(button=>button.Name=="RemoveGameFolderButton" && button.Tag as string==first));Click(Button(dialog,"保存设置"));});
            check(store.Load().GameFolders.Count==1 && GameFolder.SamePath(store.Load().GameDirectory,second) && window.FindName("RootBox") is null,"Saving directory removal switches the active root and updates settings");
            check(File.Exists(Path.Combine(first,"versions","Beta","Beta.json")) && File.Exists(Path.Combine(first,"mods","keep.jar")),"Removing a game directory never deletes its instances or shared files");
            check(((ListBox)window.FindName("InstanceList")).Items.Cast<InstanceCard>().Single().GameDirectory==second,"Library and directory tabs refresh after removal");
            Modal<InstanceWindow>(Manage,dialog=>
            {
                var deletion=Children<Button>(dialog).Single(b=>b.Name=="DeleteInstanceButton");deletion.BringIntoView();dialog.UpdateLayout();capture(dialog,"Instance-Delete-Entry");
                Modal<MessageWindow>(()=>Click(deletion),confirmation=>
                {
                    var text=string.Join("\n",Children<TextBlock>(confirmation).Select(t=>t.Text));
                    check(text.Contains("Second Beta") && text.Contains(Path.Combine(second,"versions","Beta")) && text.Contains("共享根目录"),"Delete confirmation identifies the exact instance path and retained shared data");
                    check(Button(confirmation,"取消").IsDefault && !Button(confirmation,"删除实例").IsDefault,"Destructive confirmation defaults to cancel");capture(confirmation,"Instance-Delete-Confirmation");Click(Button(confirmation,"取消"));
                });
                check(calls==0 && dialog.IsVisible && Directory.Exists(Path.Combine(second,"versions","Beta")),"Canceling confirmation does not touch files or close management");
            });
            foreach(var outcome in new[]{"fail","cancel","noop"})
            {
                mode=outcome;
                Modal<InstanceWindow>(Manage,dialog=>
                {
                    Modal<MessageWindow>(()=>Click(Children<Button>(dialog).Single(b=>b.Name=="DeleteInstanceButton")),confirmation=>Click(Button(confirmation,"删除实例")));
                    check(dialog.IsVisible && Directory.Exists(Path.Combine(second,"versions","Beta")) && store.Load().Instances.Values.Any(p=>p.DisplayName=="Second Beta"),"Failed, canceled or incomplete recycling keeps the instance and preferences: "+outcome);
                });
            }
            var card=((ListBox)window.FindName("InstanceList")).Items.Cast<InstanceCard>().Single();
            var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var running=window.RunOperationAsync(token=>gate.Task.WaitAsync(token),"fixture operation");
            try{window.DeleteInstance(card,window);throw new Exception("Busy deletion accepted");}catch(InvalidOperationException){check(true,"Active launcher operations reject deletion before confirmation");}
            gate.SetResult();await running;
            mode="success";
            Modal<InstanceWindow>(Manage,dialog=>
            {
                Modal<MessageWindow>(()=>Click(Children<Button>(dialog).Single(b=>b.Name=="DeleteInstanceButton")),confirmation=>Click(Button(confirmation,"删除实例")));
                check(!dialog.IsVisible,"Successful deletion closes instance management");
            });
            check(!Directory.Exists(Path.Combine(second,"versions","Beta")) && File.Exists(Path.Combine(recycled,"Beta","saves","own-world.txt")),"Only the selected version and its isolated content reach the injected recycle provider");
            check(File.Exists(Path.Combine(second,"mods","keep.jar")) && File.Exists(Path.Combine(first,"versions","Beta","Beta.json")),"Deleting a shared instance preserves shared mods and same-named versions in other roots");
            check(((ListBox)window.FindName("InstanceList")).Items.Count==0 && store.Load().Instances.Values.All(p=>p.DisplayName!="Second Beta") && store.Load().Instances.Values.Any(p=>p.DisplayName=="First Beta"),"Deleted instance disappears from the library and only its preferences are cleared");
        }
        finally{await ((DownloadCenter)window.FindName("DownloadCenterView")).ShutdownAsync();window.Close();}
    }
}
