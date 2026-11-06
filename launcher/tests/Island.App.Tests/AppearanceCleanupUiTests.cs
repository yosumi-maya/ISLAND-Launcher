using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Island.App;
using Island.Core;
internal static class AppearanceCleanupUiTests
{
    private static IEnumerable<T> Children<T>(DependencyObject root) where T:DependencyObject
    {for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T item)yield return item;foreach(var nested in Children<T>(child))yield return nested;}}
    public static async Task RunAsync(Window owner,string root,Action<bool,string> check,Action<Window,string> capture)
    {
        var store=new LauncherStore(Path.Combine(root,"appearance-cleanup"));var state=new LauncherState {GameDirectory=Path.Combine(root,"appearance-cleanup-game")};store.Save(state);
        var window=new MainWindow(store,javaRuntimes:new JavaRuntimeCache(store.DirectoryPath,(_,_,_)=>Task.FromResult<IReadOnlyList<BlockDock.Core.JavaInstallation>>([]))) {Owner=owner,Left=-16000,Top=0,WindowStartupLocation=WindowStartupLocation.Manual,ShowInTaskbar=false};
        try
        {
            window.Show();T Find<T>(string name)=>(T)window.FindName(name);
            Find<RadioButton>("NavSettings").IsChecked=true;window.UpdateLayout();
            var themes=Find<WrapPanel>("ThemePresetButtons").Children.OfType<Button>().ToArray();
            themes.Single(b=>b.Tag as string=="#AD526E").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));await Task.Delay(80);
            check((Find<Button>("OpenAppearanceButton").Content as string)!.StartsWith("自定义外观设置"),"Appearance entry uses the requested custom appearance label");
            var choices=Find<WrapPanel>("CardAnimationChoices").Children.OfType<RadioButton>().ToArray();
            check(choices.Select(b=>b.Content as string).SequenceEqual(new[]{"关","悬浮跟随","轻盈上浮"}),"Only the three retained card animation choices are offered");
            check(window.FindName("CardAnimationDescription") is null && window.FindName("QuickAppearanceStatus") is null,"Animation description and automatic-save explanation rows are absent");
            check(themes.All(b=>b.BorderThickness.Left==1),"Selected theme presets keep the same base border thickness");
            Find<StackPanel>("WindowControls").Visibility=Visibility.Visible;Find<StackPanel>("WindowTrafficLights").Visibility=Visibility.Collapsed;window.UpdateLayout();
            var minimize=Find<Button>("WindowMinimizeButton");minimize.Focus();Keyboard.Focus(minimize);window.UpdateLayout();
            check(minimize.Template.FindName("KeyboardRing",minimize) is null && ((Border)minimize.Template.FindName("B",minimize)).BorderThickness.Left==0,"Focused title-bar buttons have no added outer frame");
            check(Find<RadioButton>("NavSettings").Template.FindName("NavRing",Find<RadioButton>("NavSettings")) is null,"Navigation selection has no extra focus frame");
            capture(window,"Appearance-Cleanup-Settings");
            var chooser=new InstanceIconWindow(window,"quilt",null){Left=-16000,Top=0,WindowStartupLocation=WindowStartupLocation.Manual};chooser.Show();chooser.UpdateLayout();
            var list=Children<ListBox>(chooser).Single();var item=list.Items.OfType<ListBoxItem>().Single(i=>i.Tag as string=="quilt");item.Focus();chooser.UpdateLayout();
            var border=(Border)item.Template.FindName("B",item);
            check(border.BorderThickness.Left==1 && ((SolidColorBrush)border.Background).Color==((SolidColorBrush)chooser.FindResource("AccentSoft")).Color,"Icon selection uses a filled background without a thick selection frame");capture(chooser,"Appearance-Cleanup-Icon-Selection");chooser.Close();
            Find<Button>("OpenAppearanceButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));window.UpdateLayout();
            var preview=(AppearancePreview)Find<ContentControl>("AppearancePreviewHost").Content;preview.Highlight(SurfaceArea.Hero);
            check(Children<Button>(preview).Where(b=>b.Name.StartsWith("Preview")).All(b=>b.BorderThickness.Left==0),"Custom appearance preview does not add a selected-region outline");capture(window,"Appearance-Cleanup-Preview");
        }
        finally{await ((MultiplayerView)window.FindName("MultiplayerPage")).ShutdownAsync();window.Close();ThemeManager.Apply(AppearanceSettings.DefaultColor);AppearanceRenderer.Apply(new LauncherAppearance(),AppearanceSettings.DefaultColor);}
    }
}
