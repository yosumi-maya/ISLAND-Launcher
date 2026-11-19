using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Island.Core;
using Microsoft.Win32;
namespace Island.App;
public sealed class InstanceIconWindow : FormWindow
{
    public string IconKey { get; private set; }
    public byte[]? IconPng { get; private set; }
    private readonly ListBox list = new() { Name = "InstanceIconChoices", BorderThickness = new Thickness(0), Background=Brushes.Transparent, HorizontalContentAlignment=HorizontalAlignment.Stretch };
    private readonly TextBlock status = new() { TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,10,0,0) };
    public InstanceIconWindow(Window owner, string key, byte[]? png) : base(owner,"游戏图标","选择内置游戏图标，或导入自己的图片。",560)
    {
        IconKey=key; IconPng=png;
        list.ItemContainerStyle=(Style)System.Windows.Markup.XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ListBoxItem">
         <Setter Property="FocusVisualStyle" Value="{x:Null}"/><Setter Property="HorizontalContentAlignment" Value="Center"/><Setter Property="VerticalContentAlignment" Value="Center"/>
         <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ListBoxItem">
          <Border x:Name="B" Background="{DynamicResource Surface}" BorderBrush="{DynamicResource Line}" BorderThickness="1" CornerRadius="8" Padding="5"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border>
          <ControlTemplate.Triggers>
           <Trigger Property="IsSelected" Value="True"><Setter TargetName="B" Property="Background" Value="{DynamicResource AccentSoft}"/></Trigger>
           <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="B" Property="BorderBrush" Value="{DynamicResource Focus}"/></Trigger>

          </ControlTemplate.Triggers>
         </ControlTemplate></Setter.Value></Setter>
        </Style>
        """);
        var panelFactory=new FrameworkElementFactory(typeof(WrapPanel)); list.ItemsPanel=new ItemsPanelTemplate(panelFactory);
        var import=new ListBoxItem { Content="＋", FontSize=27, Width=48,Height=48,ToolTip="导入自定义图标", Tag="import" }; list.Items.Add(import);
        if(png is not null) Add("custom","当前自定义图标",InstanceIcons.Decode(png)).Tag=png;
        foreach(var choice in InstanceIcons.Choices) Add(choice.Key,choice.Name,InstanceIcons.Builtin(choice.Key));
        list.SelectionChanged += (_,_) =>
        {
            if(list.SelectedItem is not ListBoxItem item) return;
            if(item.Tag as string == "import")
            {
                var file=new OpenFileDialog { Title="选择游戏图标",Filter="图标图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif" };
                try { if(file.ShowDialog(this)==true) { var data=InstanceIcons.Import(file.FileName); IconPng=data; IconKey=""; var custom=Add("custom","自定义图标",InstanceIcons.Decode(data)); custom.Tag=data; list.SelectedItem=custom; status.Text="已选择自定义图标。"; } else RestoreSelection(); }
                catch(Exception ex) { status.Text=ex.Message; RestoreSelection(); }
            }
            else if(item.Tag is byte[] custom) { IconKey=""; IconPng=custom; status.Text="已选择：自定义图标"; }
            else { IconKey=item.Tag as string ?? ""; IconPng=null; status.Text="已选择："+item.ToolTip; }
        };
        RestoreSelection(); Body.Children.Add(list); Body.Children.Add(status);
        Actions.Children.Add(Button("取消",(_,_)=>DialogResult=false)); Actions.Children.Add(Button("确定",(_,_)=>DialogResult=true,true)); Body.Children.Add(Actions); KeepActionsVisible();
    }
    private ListBoxItem Add(string key,string title,ImageSource source)
    {
        var image=new Image { Source=source,Width=32,Height=32,Stretch=Stretch.Uniform }; RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.NearestNeighbor);
        var item=new ListBoxItem { Content=image,Width=48,Height=48,Padding=new Thickness(6),Margin=new Thickness(2),ToolTip=title,Tag=key };
        System.Windows.Automation.AutomationProperties.SetName(item,title); list.Items.Add(item); return item;
    }
    private void RestoreSelection() => list.SelectedItem=list.Items.OfType<ListBoxItem>().LastOrDefault(i=>IconPng is not null ? ReferenceEquals(i.Tag,IconPng) : i.Tag as string == IconKey);
}
