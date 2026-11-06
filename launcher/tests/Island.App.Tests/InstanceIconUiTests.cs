using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BlockDock.Core;
using Island.App;
using Island.Core;
internal static class InstanceIconUiTests
{
    private static IEnumerable<T> Children<T>(DependencyObject root) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) { var child=VisualTreeHelper.GetChild(root,i); if(child is T t) yield return t; foreach(var nested in Children<T>(child))yield return nested; }
    }
    private static void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    public static void Run(Window owner,string root,Action<bool,string> check,Action<Window,string> capture)
    {
        check(InstanceIcons.Choices.Count==14 && InstanceIcons.Choices.All(c=>InstanceIcons.Builtin(c.Key) is BitmapSource),"All fourteen original icon resources load");
        var directory=Path.Combine(root,"icon-game","versions","IconFixture"); Directory.CreateDirectory(directory);
        var instance=new InstalledInstance("IconFixture","1.20.1","Forge",directory,DateTime.Now);
        var prefs=new InstancePreferences();
        check(ReferenceEquals(new InstanceCard(instance,prefs).Icon,InstanceIcons.Builtin("forge")),"Forge defaults to original anvil artwork");
        var source=SkinUiTests.MakeSkin(root); File.Copy(source,Path.Combine(directory,"icon.png"));
        check(!ReferenceEquals(new InstanceCard(instance,prefs).Icon,InstanceIcons.Builtin("forge")),"Pack artwork takes priority over loader defaults");
        prefs.IconKey="quilt";
        check(ReferenceEquals(new InstanceCard(instance,prefs).Icon,InstanceIcons.Builtin("quilt")),"Selected built-in artwork overrides pack artwork");
        prefs.IconPng=InstanceIcons.Import(source);
        check(new InstanceCard(instance,prefs).Icon is BitmapSource,"Imported custom artwork is decoded from saved bytes");
        var state=new LauncherState {GameDirectory=Path.Combine(root,"icon-game")}; var saved=state.For("IconFixture"); saved.IconKey="quilt"; saved.IconPng=prefs.IconPng;
        var store=new LauncherStore(Path.Combine(root,"icon-store"));store.Save(state);
        check(store.Load().For("IconFixture").IconPng!.SequenceEqual(prefs.IconPng),"Custom icon persists through a settings reload");
        bool rejected=false;try {InstanceIcons.Decode(new byte[] {1,2,3});}catch(Exception){rejected=true;}
        check(rejected,"Malformed imported images are rejected");
        var chooser=new InstanceIconWindow(owner,"forge",null) {Left=-16000,Top=0,WindowStartupLocation=WindowStartupLocation.Manual};
        chooser.Loaded+=(_,_)=> { var list=Children<ListBox>(chooser).Single(); list.SelectedItem=list.Items.OfType<ListBoxItem>().Single(i=>i.Tag as string=="quilt"); capture(chooser,"Instance-Icon-Chooser"); Click(Children<Button>(chooser).Single(b=>b.Content as string=="确定")); };
        check(chooser.ShowDialog()==true && chooser.IconKey=="quilt" && chooser.IconPng is null,"Icon chooser confirms the selected built-in icon");
        var manager=new InstanceWindow(owner,new InstanceCard(instance,saved),state,()=>store.Save(state)) {Left=-16000,Top=0,WindowStartupLocation=WindowStartupLocation.Manual};
        manager.Show();manager.UpdateLayout();capture(manager,"Instance-Icon-Settings");
        Click(Children<Button>(manager).Single(b=>b.Name=="ResetInstanceIconButton")); manager.Close();
        check(saved.IconKey=="quilt" && saved.IconPng is not null,"Closing without saving preserves the previous icon");
        manager=new InstanceWindow(owner,new InstanceCard(instance,saved),state,()=>store.Save(state)) {Left=-16000,Top=0,WindowStartupLocation=WindowStartupLocation.Manual};
        manager.Show();manager.UpdateLayout();Click(Children<Button>(manager).Single(b=>b.Name=="ResetInstanceIconButton"));Click(Children<Button>(manager).Single(b=>b.Content as string=="保存设置"));
        check(saved.IconKey.Length==0 && saved.IconPng is null && store.Load().For("IconFixture").IconPng is null,"Saving reset restores automatic pack or loader icon selection");
    }
}
