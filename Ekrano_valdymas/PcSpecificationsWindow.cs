using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Ekrano_valdymas;

internal static class PcSpecificationsWindow
{
    private static SettingsWindow? active;
    internal static void Open(Window? owner)
    {
        if(active is {IsVisible:true}){active.Activate();return;}
        var window=Create(owner);active=window;
        window.Closed+=(_,_)=>{if(ReferenceEquals(active,window))active=null;};
        window.Show();
    }
    internal static SettingsWindow Create(Window? owner)=>CreateWithClipboard(owner,null);
    internal static SettingsWindow CreateWithClipboard(Window? owner,Action<string>? clipboard)
    {
        var window=AppearanceEditorUi.Window("PC specifications",owner,780);window.Width=1080;
        var root=new DockPanel{Margin=new Thickness(20,8,20,16)};
        var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        var close=MacroUi.Button("Close",(_,_)=>window.Close());close.IsCancel=true;
        var copy=MacroUi.Button("Copy all",(_,_)=>{});copy.IsEnabled=false;
        footer.Children.Add(copy);footer.Children.Add(close);DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var columns=new Grid();columns.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});columns.ColumnDefinitions.Add(new(){Width=new GridLength(20)});columns.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        var overview=new StackPanel{Name="PcOverview"};
        var loading=new TextBlock{Text="Reading PC specifications…",Foreground=Brushes.WhiteSmoke,FontSize=16,TextWrapping=TextWrapping.Wrap};overview.Children.Add(loading);
        var listScroll=MacroUi.Scroll(overview);listScroll.Name="PcOverviewScroll";columns.Children.Add(listScroll);
        var detail=new DockPanel{Margin=new Thickness(16)};
        var title=new TextBlock{Name="PcDeviceTitle",Text="Device details",TextWrapping=TextWrapping.Wrap,FontSize=20,FontWeight=FontWeights.SemiBold,Foreground=Brushes.WhiteSmoke,Margin=new Thickness(0,0,0,14)};
        DockPanel.SetDock(title,Dock.Top);detail.Children.Add(title);
        var details=new StackPanel();
        TextBox Text(string name)=>new(){Name=name,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,BorderThickness=new Thickness(0),Padding=new Thickness(0),Background=Brushes.Transparent,Foreground=Brushes.WhiteSmoke,FontSize=15,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        var text=Text("PcDeviceDetails");text.ContextMenuOpening+=TextEditMenu.Opening;details.Children.Add(text);
        var liveHeading=new TextBlock{Text="Current readings",FontSize=17,FontWeight=FontWeights.SemiBold,Foreground=Brushes.WhiteSmoke,Margin=new Thickness(0,18,0,8),Visibility=Visibility.Collapsed};details.Children.Add(liveHeading);
        var live=Text("PcLiveReadings");live.ContextMenuOpening+=TextEditMenu.Opening;details.Children.Add(live);
        var extra=Text("PcAdditionalReadings");extra.ContextMenuOpening+=TextEditMenu.Opening;
        var more=new Expander{Header="Additional sensor readings",Content=extra,Style=AirthingsEditorVisuals.DisclosureStyle(),Foreground=Brushes.WhiteSmoke,FontSize=15,Margin=new Thickness(0,12,0,0),Visibility=Visibility.Collapsed};details.Children.Add(more);
        var detailScroll=MacroUi.Scroll(details);detailScroll.Name="PcDetailsScroll";detail.Children.Add(detailScroll);
        var frame=new Border{BorderBrush=new SolidColorBrush(Color.FromRgb(90,86,80)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8),Child=detail};Grid.SetColumn(frame,2);columns.Children.Add(frame);root.Children.Add(columns);
        var copyDetail=CopyFeedback.Create(()=>string.Join(Environment.NewLine+Environment.NewLine,new[]{text.Text,live.Text,more.IsExpanded?extra.Text:""}.Where(t=>t.Length>0)),copied=>frame.BorderBrush=new SolidColorBrush(copied?Color.FromRgb(59,107,245):Color.FromRgb(90,86,80)),clipboard);
        copyDetail.Name="CopyDeviceDetails";copyDetail.HorizontalAlignment=HorizontalAlignment.Right;DockPanel.SetDock(copyDetail,Dock.Bottom);detail.Children.Insert(1,copyDetail);
        window.Content=root;AppearanceEditorUi.Frame(window,470,740,compact:false);
        // Keep both panes and the footer stable while selecting different devices.
        window.Height=780;
        bool closed=false;PcSpecificationDevice? selected=null;
        var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromSeconds(1)};
        void UpdateLive()
        {
            if(selected==null)return;
            bool supported=selected.HardwareKind.Length>0;liveHeading.Visibility=supported?Visibility.Visible:Visibility.Collapsed;
            var readings=supported?PcSpecifications.LiveReadings(selected):[];
            static bool MainReading(HardwareReading r)=>r.Name is "GPU Core" or "GPU Memory" or "GPU Package" or "GPU Power" or "GPU Memory Junction" or "GPU Hot Spot" or "GPU Memory Used" or "GPU Memory Total" or "CPU Package" or "CPU PPT" or "CPU Total" or "Core (Tctl/Tdie)" or "CPU (Tctl/Tdie)" or "Core Average" or "Cores (Average)" or "Cores (Average Effective)" || r.Unit=="RPM";
            static string Format(HardwareReading r)=>r.Name+": "+(r.Unit=="MB"?r.DisplayIn("GB"):r.Display);
            string value=string.Join(Environment.NewLine,readings.Where(MainReading).Select(Format));
            if(supported&&value.Length==0)value=readings.Length==0?"Current readings are not available for this device.":"Available readings are listed below.";
            if(live.Text!=value)live.Text=value;
            string additional=string.Join(Environment.NewLine,readings.Where(r=>!MainReading(r)).Select(Format));
            if(extra.Text!=additional)extra.Text=additional;
            more.Visibility=additional.Length>0?Visibility.Visible:Visibility.Collapsed;
        }
        timer.Tick+=(_,_)=>UpdateLive();
        window.Closed+=(_,_)=>{closed=true;timer.Stop();};
        window.Loaded+=async(_,_)=>
        {
            var sections=await PcSpecifications.ReadAsync();if(closed)return;
            overview.Children.Clear();var choices=new List<Button>();var rowCopies=new Dictionary<Button,Button>();
            void Select(Button button,PcSpecificationDevice device)
            {
                CopyFeedback.Reset(copyDetail);
                selected=device;title.Text=device.Name;text.Text=device.Text;more.IsExpanded=false;
                foreach(var choice in choices){choice.Background=choice==button?new SolidColorBrush(Color.FromRgb(65,63,59)):Brushes.Transparent;choice.BorderBrush=choice==button?new SolidColorBrush(Color.FromRgb(255,138,0)):Brushes.Transparent;rowCopies[choice].Visibility=choice==button?Visibility.Visible:Visibility.Hidden;}
                UpdateLive();detailScroll.ScrollToTop();
            }
            foreach(var section in sections.OrderBy(s=>s.Title switch {"CPU"=>0,"Graphics"=>1,"Board & BIOS"=>2,"Memory"=>3,"Storage"=>4,"Audio"=>5,"Network"=>6,"Displays"=>7,_=>8}))
            {
                string caption=section.Title switch {"Graphics"=>"GPU","Memory"=>"RAM",_=>section.Title};
                overview.Children.Add(new TextBlock{Text=caption,FontSize=16,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(Color.FromRgb(59,107,245)),Margin=new Thickness(5,choices.Count==0?0:10,0,3)});
                foreach(var device in section.Devices)
                {
                    var label=new TextBlock{Text=device.Name,FontSize=14,TextWrapping=TextWrapping.Wrap,Foreground=Brushes.WhiteSmoke,Margin=new Thickness(8,3,8,3)};
                    var button=new Button{Name="PcDeviceChoice",Content=label,Tag=device,HorizontalContentAlignment=HorizontalAlignment.Stretch,MinHeight=24,Padding=new Thickness(0),Margin=new Thickness(0),BorderThickness=new Thickness(1)};
                    button.SetResourceReference(FrameworkElement.StyleProperty,"ModuleOptionsButtonStyle");
                    button.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
                    <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
                      <Border Name="surface" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="6">
                        <ContentPresenter HorizontalAlignment="Stretch" VerticalAlignment="Center"/>
                      </Border>
                      <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="surface" Property="Background" Value="#28FFFFFF"/></Trigger></ControlTemplate.Triggers>
                    </ControlTemplate>
                    """);
                    System.Windows.Automation.AutomationProperties.SetName(button,caption+": "+device.Name);
                    var row=new Grid();row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.Children.Add(button);
                    var rowFrame=new Border{CornerRadius=new CornerRadius(6),BorderThickness=new Thickness(1),BorderBrush=Brushes.Transparent,Child=row};
                    var rowCopy=CopyFeedback.Create(()=>device.Name,copied=>{rowFrame.BorderBrush=copied?new SolidColorBrush(Color.FromRgb(59,107,245)):Brushes.Transparent;rowFrame.Background=copied?new SolidColorBrush(Color.FromArgb(45,59,107,245)):Brushes.Transparent;},clipboard);rowCopy.Name="CopyDeviceRow";rowCopy.Tag=device;rowCopy.Width=28;rowCopy.Height=24;rowCopy.Visibility=Visibility.Hidden;Grid.SetColumn(rowCopy,1);row.Children.Add(rowCopy);rowCopies[button]=rowCopy;
                    button.Click+=(_,_)=>Select(button,device);overview.Children.Add(rowFrame);choices.Add(button);
                }
            }
            copy.IsEnabled=true;copy.Click+=(_,_)=>{try{Clipboard.SetText(string.Join(Environment.NewLine+Environment.NewLine,sections.Select(s=>s.Title+Environment.NewLine+s.Text)));}catch{copy.ToolTip="Clipboard is busy. Try again.";}};
            if(choices.Count>0)Select(choices[0],(PcSpecificationDevice)choices[0].Tag);
            timer.Start();window.RequestContentFit();
        };
        return window;
    }
}
