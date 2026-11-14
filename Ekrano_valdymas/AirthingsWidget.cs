using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
namespace Ekrano_valdymas;

internal sealed class AirthingsWidget:UserControl
{
    private readonly AirthingsService service;
    private readonly Button devices=new(){Height=34,HorizontalContentAlignment=HorizontalAlignment.Stretch};
    private readonly OpticallyCenteredText deviceName=Text(15);
    private readonly OpticallyCenteredText moduleTitle=Text(27);
    private static FontFamily Face(string name)=>name=="Teko"?(FontFamily)Application.Current.FindResource("TekoFont"):new FontFamily(name);
    private static Brush ColorBrush(string? color,string fallback)=>color==null?(Brush)Application.Current.FindResource(fallback):new SolidColorBrush(CompactColorPicker.Parse(color,Colors.White));
    private void ApplyTypography()
    {
        moduleTitle.TextFontSize=Math.Clamp(display.TitleSize,8,80);moduleTitle.TextFontFamily=Face(display.TitleFont);moduleTitle.TextBrush=ColorBrush(display.UsesQualityColors?null:display.TitleColor,"ModuleTitleBrush");
        sample.FontSize=Math.Clamp(display.FooterSize,8,32);status.FontSize=Math.Clamp(display.FooterSize,8,32);
        deviceName.TextFontFamily=Face(display.LabelFont);deviceName.TextFontSize=Math.Clamp(display.LabelSize+4,8,80);deviceName.TextBrush=ColorBrush(display.UsesQualityColors?null:display.LabelColor,"ModuleTitleBrush");
    }
    private readonly UniformGrid readings=new(){Columns=2,Margin=new Thickness(0,8,0,5)};
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new Thickness(3,4,3,0)};
    private readonly TextBlock sample=new(){FontSize=10,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(3,0,3,0)};
    private readonly System.Windows.Threading.DispatcherTimer freshness=new(){Interval=TimeSpan.FromMinutes(1)};
    private string? selected;
    private AirthingsDisplay display=AirthingsDisplay.Load();
    private AirthingsGaugePresentation presentation=AirthingsGaugePresentation.Load();
    private readonly Button overviewBack;
    private readonly Button gaugeToggle;
    private readonly Button gaugeZoomToggle;
    private static void SetToolSelected(Button button,bool active)
    {
        Selector.SetIsSelected(button,active);
        button.Opacity=active?1:.65;
        ((OpticallyCenteredText)button.Content).SetResourceReference(OpticallyCenteredText.TextBrushProperty,"PanelTextBrush");
        System.Windows.Automation.AutomationProperties.SetItemStatus(button,active?"Selected":"Not selected");
    }
    private readonly AirthingsGaugeZoom gaugeZoom=new();
    private string? gaugeZoomRoom;
    private void SaveDisplay(){if(presentation.VerticalGauges)display.SaveGauges();else display.Save();}
    private void LoadDisplay()=>display=presentation.VerticalGauges?AirthingsDisplay.LoadGauges():AirthingsDisplay.Load();
    internal void DisplayForProbe(AirthingsDisplay value){display=value;Render();}
    internal bool UsesGaugePresentation=>presentation.VerticalGauges;
    internal void SetGaugePresentationForProbe(bool enabled){presentation=presentation with{VerticalGauges=enabled};LoadDisplay();Render();}
    private string SelectionPath=>Path.Combine(DiagnosticState.LocalApplicationData,"Ekrano_valdymas","airthings-selection.json");
    private static OpticallyCenteredText Text(double size)=>new(){TextFontFamily=new FontFamily("Segoe UI"),TextFontSize=size,TextFontWeight=FontWeights.SemiBold,TextBrush=Brushes.White,FitToBox=true};
    internal AirthingsWidget(AirthingsService? source=null)
    {
        LoadDisplay();service=source??AirthingsService.Shared;Foreground=Brushes.White;sample.SetResourceReference(TextBlock.ForegroundProperty,"PanelTextBrush");
        try{selected=System.Text.Json.JsonSerializer.Deserialize<string>(File.ReadAllText(SelectionPath));}catch{}
        var shell=new Border{Padding=new Thickness(22),CornerRadius=new CornerRadius(18),BorderBrush=new SolidColorBrush(Color.FromArgb(64,255,255,255)),BorderThickness=new Thickness(1.5)};shell.SetResourceReference(Border.BackgroundProperty,"PanelMetalBrush");CommonMetal.SetSurface(shell,true);Content=shell;
        var root=new Grid();foreach(var height in new[]{new GridLength(45),new GridLength(1),GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})root.RowDefinitions.Add(new(){Height=height});shell.Child=root;
        var header=new DockPanel();root.Children.Add(header);
        Button Tool(string glyph,string hint,RoutedEventHandler action){var b=new Button{Width=38,Height=36,Margin=new Thickness(4,0,0,0),VerticalAlignment=VerticalAlignment.Center,Tag="NoWindowDrag",ToolTip=hint};b.SetResourceReference(StyleProperty,"ModuleOptionsButtonStyle");var icon=Text(20);icon.Text=glyph;icon.TextFontFamily=new FontFamily("Segoe MDL2 Assets");icon.TextFontWeight=FontWeights.Normal;icon.Width=20;icon.Height=20;icon.FitToBox=false;icon.SetResourceReference(OpticallyCenteredText.TextBrushProperty,"PanelTextBrush");b.Content=icon;b.Click+=action;System.Windows.Automation.AutomationProperties.SetName(b,hint);DockPanel.SetDock(b,Dock.Right);header.Children.Add(b);return b;}
        Tool("\uE713","Airthings setup",(_,_)=>CreateSetup().ShowDialog()).Name="AirthingsSetupButton";
        gaugeToggle=Tool("\uE9D9","Show vertical sensor gauges",(_,_)=>{presentation=presentation with{VerticalGauges=!presentation.VerticalGauges};presentation.Save();LoadDisplay();Render();});gaugeToggle.Name="AirthingsGaugeToggle";
        gaugeZoomToggle=Tool("\uE71E","Zoom sensor scales",(_,_)=>{presentation=presentation with{GaugeZoom=!presentation.GaugeZoom};presentation.Save();RenderReadings();});gaugeZoomToggle.Name="AirthingsGaugeZoomButton";
        var selectedToolStyle=HeaderSelectionStyle.Create();gaugeToggle.Style=selectedToolStyle;gaugeZoomToggle.Style=selectedToolStyle;
        Tool("\uE72C","Refresh Airthings",async(_,_)=>await service.Refresh(retry:true)).Name="AirthingsRefreshButton";
        overviewBack=Tool("\uE72B","Back to rooms",(_,_)=>{display=display with{View="Overview"};SaveDisplay();Render();});overviewBack.Name="AirthingsBackToRooms";
        var title=moduleTitle;title.Tag="ModuleTitle";title.Text="AIRTHINGS";title.TextFontFamily=(FontFamily)Application.Current.FindResource("TekoFont");title.TextFontWeight=FontWeights.Bold;title.FitToBox=true;title.InkAlignment=HorizontalAlignment.Left;title.SetResourceReference(OpticallyCenteredText.TextBrushProperty,"ModuleTitleBrush");header.Children.Add(title);
        var divider=new Border();divider.SetResourceReference(Border.BackgroundProperty,"PanelDividerBrush");root.Children.Add(divider);Grid.SetRow(divider,1);
        devices.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button"><Border x:Name="frame" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#18FFFFFF" BorderBrush="#45FFFFFF" BorderThickness="1" CornerRadius="7" Padding="10,0"><ContentPresenter VerticalAlignment="Stretch" HorizontalAlignment="Stretch"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="frame" Property="BorderBrush" Value="#D98D39"/></Trigger></ControlTemplate.Triggers></ControlTemplate>
        """);
        var selector=new DockPanel();var arrow=new System.Windows.Shapes.Path{Data=Geometry.Parse("M1,1 L8,8 L15,1"),Width=16,Height=9,StrokeThickness=2,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,8,0)};arrow.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,"ModuleTitleBrush");DockPanel.SetDock(arrow,Dock.Right);selector.Children.Add(arrow);deviceName.InkAlignment=HorizontalAlignment.Left;deviceName.SetResourceReference(OpticallyCenteredText.TextBrushProperty,"ModuleTitleBrush");selector.Children.Add(deviceName);devices.Content=selector;devices.Margin=new Thickness(3,10,3,0);root.Children.Add(devices);Grid.SetRow(devices,2);devices.Click+=(_,_)=>OpenDevices();
        root.Children.Add(status);Grid.SetRow(status,3);root.Children.Add(readings);Grid.SetRow(readings,4);root.Children.Add(sample);Grid.SetRow(sample,5);
        Loaded+=(_,_)=>{service.Changed+=Changed;service.Start();freshness.Start();Render();};Unloaded+=(_,_)=>{service.Changed-=Changed;freshness.Stop();};freshness.Tick+=(_,_)=>RenderReadings();Render();
    }
    private void Changed(){if(Dispatcher.CheckAccess())Render();else Dispatcher.BeginInvoke(Render);}
    private bool AccountConfigured=>service.HasCredentials||service.RetrievedAt.HasValue;
    private void Render()
    {
        var known=service.Devices.FirstOrDefault(d=>Key(d)==selected);
        if(selected==null||known!=null&&!AirthingsStatusRules.HasSensors(known))selected=service.Devices.FirstOrDefault(AirthingsStatusRules.HasSensors) is {} first?Key(first):null;
        var device=service.Devices.FirstOrDefault(d=>Key(d)==selected&&AirthingsStatusRules.HasSensors(d));
        deviceName.Text=device==null?(selected!=null?"Saved room unavailable · choose room":AccountConfigured?"No devices available":"Choose device / room"):RoomLabel(device);
        devices.ToolTip=device==null?(AccountConfigured?"Check account status in Airthings setup":"Connect in Airthings setup"):FriendlyType(device.Type);status.Text=presentation.VerticalGauges&&presentation.HasConflicts?"Conflicting saved sensor limits — review Gauge limits in Setup.":service.Status;status.Visibility=status.Text=="Connected"?Visibility.Collapsed:Visibility.Visible;status.Foreground=Brushes.LightGray;RenderReadings();
    }
    private static string Key(AirthingsDevice device)=>device.Account+":"+device.Serial;
    private string RoomLabel(AirthingsDevice device)=>presentation.RoomFor(device)?.DisplayLabel is {Length:>0} label?label:$"{device.Home} · {device.Name}";
    internal void SelectDevice(string? account,string? serial)
    {
        var device=service.Devices.FirstOrDefault(d=>d.Account==account&&d.Serial==serial&&AirthingsStatusRules.HasSensors(d));
        if(device!=null){selected=Key(device);AtomicJson.Write(SelectionPath,selected);}
        if(display.View=="Overview"){display=display with{View="Both"};SaveDisplay();}
        Render();
    }
    internal ContextMenu? DeviceMenu {get;private set;}
    internal void OpenDevices(){var menu=ListAppearanceUi.Create(this);DeviceMenu=menu;foreach(var d in service.Devices.Where(AirthingsStatusRules.HasSensors).OrderBy(d=>d.Name,StringComparer.CurrentCultureIgnoreCase)){var item=new MenuItem{Header=$"{RoomLabel(d)} · {FriendlyType(d.Type)}",IsCheckable=true,IsChecked=Key(d)==selected,ToolTip=d.Home};item.Click+=(_,_)=>{selected=Key(d);AtomicJson.Write(SelectionPath,selected);if(display.View=="Overview"){display=display with{View="Both"};SaveDisplay();}Render();};menu.Items.Add(item);}if(menu.Items.Count==0)menu.Items.Add(new MenuItem{Header="No air-quality monitors — open Setup",IsEnabled=false});menu.PlacementTarget=devices;menu.Placement=PlacementMode.Bottom;menu.MinWidth=devices.ActualWidth;menu.IsOpen=true;}
    private void RenderReadings()
    {
        ApplyTypography();
        gaugeToggle.ToolTip=presentation.VerticalGauges?"Vertical sensor gauges · switch to readings and history":"Readings and history · switch to vertical sensor gauges";
        System.Windows.Automation.AutomationProperties.SetName(gaugeToggle,(string)gaugeToggle.ToolTip);
        overviewBack.Visibility=!presentation.VerticalGauges&&display.View!="Overview"?Visibility.Visible:Visibility.Collapsed;
        SetToolSelected(gaugeToggle,presentation.VerticalGauges);
        gaugeZoomToggle.Visibility=presentation.VerticalGauges?Visibility.Visible:Visibility.Collapsed;
        SetToolSelected(gaugeZoomToggle,presentation.GaugeZoom);
        gaugeZoomToggle.ToolTip=presentation.GaugeZoom?"Zoom on · show full sensor scales":"Zoom off · zoom around current readings";
        System.Windows.Automation.AutomationProperties.SetName(gaugeZoomToggle,(string)gaugeZoomToggle.ToolTip);
        if(gaugeZoomRoom!=selected){gaugeZoom.Clear();gaugeZoomRoom=selected;}
        readings.Children.Clear();readings.Rows=0;readings.Columns=2;if(!presentation.VerticalGauges&&display.View=="Overview"){RenderOverview();return;}var device=service.Devices.FirstOrDefault(d=>Key(d)==selected&&AirthingsStatusRules.HasSensors(d));
        if(device==null){sample.Text=AccountConfigured?"No sample · "+service.Status:"No sample · Open the gear to connect your account";return;}
        bool old=AirthingsQuality.IsStale(device.Recorded);
        if(presentation.VerticalGauges&&AirthingsStatusRules.HasSensors(device))
        {
            readings.Columns=1;readings.Rows=1;readings.Children.Add(new AirthingsGaugeView(device,display,presentation.RoomFor(device),presentation.GaugeZoom?gaugeZoom:null).Viewport());SetSampleFooter(device,old);return;
        }
        foreach(var r in AirthingsStatusRules.Sensors(device).Select(type=>device.Readings.FirstOrDefault(r=>r.SensorType==type)??new AirthingsReading(type,null,"")))
        {
            var (title,value)=ReadingText(r,old);
            FrameworkElement panel;
            if(display.View=="Both")panel=new AirthingsReadingRow(title,value,Math.Clamp(display.LabelSize,8,80),Math.Clamp(display.ValueSize,8,80)){Margin=new Thickness(5)};
            else panel=new AirthingsReadingStack(title,value,Math.Clamp(display.LabelSize,8,80),Math.Clamp(display.ValueSize,8,80)){Margin=new Thickness(5)};
            var frame=new Border{Child=panel,CornerRadius=new CornerRadius(8),BorderBrush=new SolidColorBrush(Color.FromArgb(40,255,255,255)),BorderThickness=new Thickness(1),Margin=new Thickness(3)};
            frame.SetResourceReference(Border.BackgroundProperty,"ModuleCardSurfaceBrush");
            if(display.UsesQualityColors)frame.ToolTip=QualityHint(r,old);
            frame.Tag="NoWindowDrag";frame.Cursor=System.Windows.Input.Cursors.Hand;
            frame.MouseLeftButtonUp+=(_,e)=>{display=display with{Sensor=r.SensorType,View="Both"};SaveDisplay();RenderReadings();e.Handled=true;};
            readings.Children.Add(frame);
        }
        SetSampleFooter(device,old);
        if(readings.Children.Count==0){var empty=Text(18);empty.Text="Hub connects your monitors\nIt has no air-quality sensors";readings.Columns=1;readings.Children.Add(empty);sample.Text=$"Hub · no measurements\nCloud checked {service.RetrievedAt?.ToLocalTime().ToString("HH:mm")??"unavailable"}";sample.SetResourceReference(TextBlock.ForegroundProperty,"PanelTextBrush");}else {readings.Columns=2;if(display.View!="Numbers"){var cards=new UniformGrid{Columns=2};foreach(UIElement child in readings.Children.Cast<UIElement>().ToArray()){readings.Children.Remove(child);cards.Children.Add(child);}readings.Columns=1;readings.Children.Add(GraphCard(device,display.Sensor));if(display.View=="Both")readings.Children.Add(cards);}}
    }
    private void SetSampleFooter(AirthingsDevice device,bool old)
    {
        string age=device.Recorded is {} time?$" · {Math.Max(0,(int)(DateTimeOffset.UtcNow-time).TotalMinutes)} min ago":"";
        sample.Text=$"{(old?"OLDER SAMPLE":"SAMPLE")} {device.Recorded?.ToLocalTime().ToString("MM-dd HH:mm")??"unavailable"}{age}\nCloud checked {service.RetrievedAt?.ToLocalTime().ToString("HH:mm")??"unavailable"}";sample.Foreground=old?Brushes.SandyBrown:(Brush)Application.Current.FindResource("PanelTextBrush");
        sample.ToolTip=old?"The cloud connection can be healthy while the device sample is older. Check the device / Hub sync in the Airthings app.":"Device measurement time and latest cloud request are shown separately.";
    }
    private void RenderOverview()
    {
        readings.Columns=1;
        foreach(var d in service.Devices.Where(AirthingsStatusRules.HasSensors)){var card=GraphCard(d,display.Sensor,true);card.ToolTip=$"Open room · sample {d.Recorded?.ToLocalTime().ToString("g")??"unavailable"}";card.MouseLeftButtonUp+=(_,e)=>{selected=Key(d);AtomicJson.Write(SelectionPath,selected);display=display with{View="Both"};SaveDisplay();Render();e.Handled=true;};readings.Children.Add(card);}
        string period=display.Days<3?$"{display.Days*24:0} h":$"{display.Days:0} days";sample.Text=$"Local history · {period} · click a room to open\nCloud checked {service.RetrievedAt?.ToLocalTime().ToString("HH:mm")??"unavailable"}";
        if(readings.Children.Count==0){var empty=Text(18);empty.Text=service.Devices.Length>0?"No air-quality monitors in this account":AccountConfigured?"No devices available · check account status in Setup":"Connect your account in Setup";readings.Children.Add(empty);}
    }
    private (OpticallyCenteredText Label,OpticallyCenteredText Value) ReadingText(AirthingsReading reading,bool old)
    {
        var label=Text(Math.Clamp(display.LabelSize,8,80));label.TextFontFamily=Face(display.LabelFont);label.Text=SensorName(reading.SensorType);label.InkAlignment=HorizontalAlignment.Left;
        label.TextBrush=ColorBrush(display.UsesQualityColors?null:display.LabelColor,"ModuleTitleBrush");
        var value=Text(Math.Clamp(display.ValueSize,8,80));value.TextFontFamily=Face(display.ValueFont);value.Tag="SensorValue";value.InkAlignment=HorizontalAlignment.Right;
        var converted=display.Convert(reading);value.Text=reading.Value.HasValue?$"{converted.Value:0.##} {converted.Unit}":"N/A";
        value.TextBrush=!reading.Value.HasValue||old?Brushes.Gray:display.UsesQualityColors?AirthingsQuality.BrushFor(AirthingsQuality.Evaluate(reading)):ColorBrush(display.ValueColor,"PanelTextBrush");
        return(label,value);
    }
    private static string QualityHint(AirthingsReading reading,bool old)
    {
        if(old)return "Older than 60 min / unavailable sample — air quality is not classified.";
        string hint=AirthingsQuality.IsSupported(reading.SensorType)?"Airthings: "+AirthingsQuality.Name(AirthingsQuality.Evaluate(reading)):"Not included in the Airthings air-quality indicator.";
        if(reading.SensorType.StartsWith("radon",StringComparison.OrdinalIgnoreCase))hint+=" Radon keeps the reported averaging period; use long-term averages to assess exposure.";
        return hint;
    }
    private Border GraphCard(AirthingsDevice device,string sensor,bool overview=false)
    {
        var reading=device.Readings.FirstOrDefault(r=>r.SensorType.Equals(sensor,StringComparison.OrdinalIgnoreCase))??device.Readings.FirstOrDefault(r=>r.SensorType.StartsWith("radon",StringComparison.OrdinalIgnoreCase))??device.Readings.FirstOrDefault(r=>r.SensorType.Equals("voc",StringComparison.OrdinalIgnoreCase))??device.Readings.FirstOrDefault();
        var grid=new Grid{Margin=new Thickness(8,4,8,2)};grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        if(reading!=null)
        {
            bool old=AirthingsQuality.IsStale(device.Recorded);var converted=display.Convert(reading);
            var (caption,value)=ReadingText(reading,old);if(overview)caption.Text=RoomLabel(device)+" · "+caption.Text;
            var row=new AirthingsReadingRow(caption,value,Math.Clamp(display.LabelSize,8,80),Math.Clamp(display.ValueSize,8,80));
            var period=Text(Math.Clamp(display.IntervalSize,8,32));period.Name="AirthingsHistoryInterval";period.Text=display.Days<3?$"{display.Days*24:0} h":$"{display.Days:0} days";period.TextFontFamily=Face(display.AxisFont);period.TextFontWeight=FontWeights.Normal;period.FitToBox=false;period.CenterCapToBaseline=false;period.InkAlignment=HorizontalAlignment.Right;period.InkVerticalAlignment=VerticalAlignment.Top;period.ToolTip="Displayed history period";
            period.TextBrush=ColorBrush(display.UsesQualityColors?null:display.AxisColor,"PanelTextBrush");period.Opacity=.8;
            grid.Children.Add(new AirthingsGraphHeader(row,period){Margin=new Thickness(0,0,0,3),ToolTip=QualityHint(reading,old)});
            var plot=new AirthingsPlot{Line=display.UsesQualityColors?Brushes.Gray:ColorBrush(display.Color,"SystemMonitorBrush"),AxisFont=Face(display.AxisFont),AxisSize=Math.Clamp(display.AxisSize,8,24),AxisBrush=ColorBrush(display.UsesQualityColors?null:display.AxisColor,"PanelTextBrush"),GuideBrush=ColorBrush(display.UsesQualityColors?null:display.GuideColor,"PanelTextBrush")};
            if(display.UsesQualityColors)plot.DataBrush=x=>AirthingsQuality.BrushFor(AirthingsQuality.Evaluate(reading with{Value=x}));
            plot.Set(service.Archive.Read(device,reading.SensorType),display.Days,x=>display.Convert(reading.SensorType,x,reading.Unit).Value,converted.Unit);grid.Children.Add(plot);Grid.SetRow(plot,1);
        }
        else{var caption=Text(Math.Clamp(display.LabelSize,8,80));caption.Text=RoomLabel(device)+" · no readings";grid.Children.Add(caption);}
        var frame=new Border{Tag="NoWindowDrag",Child=grid,Margin=new Thickness(3),CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(1),BorderBrush=new SolidColorBrush(Color.FromArgb(40,255,255,255))};frame.SetResourceReference(Border.BackgroundProperty,"ModuleCardSurfaceBrush");return frame;
    }
    internal static string DisplayUnit(string unit)=>unit.ToLowerInvariant() switch {"pct"=>"%","c" or "degc"=>"°C","bq"=>"Bq/m³","mbar"=>"hPa",_=>unit};
    internal static string FriendlyType(string type)=>type.ToUpperInvariant() switch {"WAVE_PLUS"=>"Wave Plus","WAVE_GEN2"=>"Wave Gen 2","WAVE_MINI"=>"Wave Mini","HUB"=>"Hub",_=>type.Replace('_',' ')};
    private static string SensorName(string type)=>type.ToLowerInvariant() switch {"radonshorttermavg"=>"RADON · 24H", "radonlongtermavg"=>"RADON · LONG TERM", "temp" or "temperature"=>"TEMPERATURE", "co2"=>"CO₂", "voc"=>"VOC", "mold" or "moldrisk"=>"MOLD RISK", _=>type.ToUpperInvariant()};
    private static Expander SetupSection(string title,bool expanded=false)=>new()
    {
        Header=title,Foreground=Brushes.White,IsExpanded=expanded,Margin=new Thickness(0,10,0,0),
        Background=new SolidColorBrush(Color.FromRgb(42,39,35)),BorderBrush=new SolidColorBrush(Color.FromRgb(66,62,56)),
        BorderThickness=new Thickness(1),Padding=new Thickness(8,6,8,6),HorizontalContentAlignment=HorizontalAlignment.Stretch
    };
    internal SettingsWindow CreateRoomNames(Window? owner=null)
    {
        var original=presentation;bool saved=false;
        var dialog=MacroUi.Window("Airthings room names",owner??Window.GetWindow(this),490,350);
        var root=new DockPanel();dialog.Content=root;var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(20,8,20,16)};DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
        var body=new StackPanel{Margin=new Thickness(20,0,20,8)};var scroll=MacroUi.Scroll(body);root.Children.Add(scroll);dialog.AutoFitScroll=scroll;
        body.Children.Add(new TextBlock{Text="Names apply only in BlakiDeck. Leave a name empty to use the original room name.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
        foreach(var device in service.Devices.Where(AirthingsStatusRules.HasSensors))
        {
            body.Children.Add(new TextBlock{Text=$"{device.Home} · {device.Name}",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,4)});
            var input=new TextBox{Text=RoomLabel(device),Name="AirthingsRoomName",MinHeight=32,Tag=Key(device)};body.Children.Add(input);
            input.TextChanged+=(_,_)=>
            {
                var room=presentation.RoomFor(device)??new AirthingsStatusRoom(device.Account,device.Serial);
                var renamed=room with{DisplayLabel=string.IsNullOrWhiteSpace(input.Text)?null:input.Text.Trim()};
                presentation=presentation with{Rooms=(presentation.Rooms??[]).Where(r=>r.Account!=device.Account||r.Serial!=device.Serial).Append(renamed).ToArray()};
                Render();
            };
        }
        
        buttons.Children.Add(MacroUi.Button("Save",(_,_)=>{presentation.Save();saved=true;dialog.Close();}));
        buttons.Children.Add(MacroUi.Button("Cancel",(_,_)=>dialog.Close()));
        dialog.Closed+=(_,_)=>{if(!saved){presentation=original;Render();}};
        return dialog;
    }
    internal SettingsWindow CreateSetup()
    {
        bool gauges=presentation.VerticalGauges;
        var window=MacroUi.Window(gauges?"Airthings gauges setup":"Airthings readings setup",Window.GetWindow(this),490,500);window.ShowInTaskbar=false;var body=new StackPanel{Margin=new Thickness(20)};var scroll=MacroUi.Scroll(body);window.Content=scroll;window.AutoFitScroll=scroll;
        void Select(string title,string[] choices,string selectedValue,Action<string> change)
        {
            var row=new Grid{Margin=new Thickness(0,4,0,4)};
            row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            var label=new OpticallyCenteredText{Text=title,TextFontFamily=window.FontFamily,TextFontSize=window.FontSize,TextFontWeight=FontWeights.Normal,InkAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,12,0),IsHitTestVisible=false};
            label.SetBinding(OpticallyCenteredText.TextBrushProperty,new System.Windows.Data.Binding(nameof(Window.Foreground)){Source=window});row.Children.Add(label);
            var box=new ComboBox{MinHeight=32};Grid.SetColumn(box,1);if(title=="Color scheme")box.Name="AirthingsColorMode";
            foreach(string choice in choices){var item=new ComboBoxItem{Content=title=="Graph sensor"?SensorName(choice):choice,Tag=choice};box.Items.Add(item);if(choice==selectedValue)box.SelectedItem=item;}
            row.Children.Add(box);body.Children.Add(row);box.SelectionChanged+=(_,_)=>{if(box.SelectedItem is ComboBoxItem{Tag:string value}){change(value);SaveDisplay();Render();}};
        }
        if(!gauges)Select("Display",new[]{"Overview","Numbers","Graph","Both"},display.View,v=>display=display with{View=v});
        if(presentation.VerticalGauges)
        {
            var gaugeLimits=MacroUi.Button("Gauge limits…",(_,_)=>CreateGaugeLimits(window).ShowDialog());gaugeLimits.Name="AirthingsGaugeLimitsButton";gaugeLimits.Margin=new Thickness(0,8,0,4);body.Children.Add(gaugeLimits);
            body.Children.Add(new TextBlock{Text=presentation.HasConflicts?"Conflicting limits from placed room indicators were not imported. Choose this module's limits in Gauge limits.":"Gauge limits are kept per room. Temperature and pressure stay neutral until you enable your own limits.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,6)});
        }
        var roomNames=MacroUi.Button("Room names…",(_,_)=>CreateRoomNames(window).ShowDialog());roomNames.Name="AirthingsRoomNamesButton";roomNames.Margin=new Thickness(0,4,0,6);body.Children.Add(roomNames);
        if(!gauges){
        var sensors=service.Devices.SelectMany(AirthingsStatusRules.Sensors).Distinct().Order().ToArray();if(sensors.Length>0)Select("Graph sensor",sensors,display.Sensor,v=>display=display with{Sensor=v});
        Select("History period",new[]{"12 h","24 h","48 h","7 days","30 days","365 days"},display.Days switch{.5=>"12 h",1=>"24 h",2=>"48 h",7=>"7 days",30=>"30 days",_=>"365 days"},v=>display=display with{Days=v switch{"12 h"=>.5,"24 h"=>1,"48 h"=>2,"7 days"=>7,"30 days"=>30,_=>365}});
        }
        Select("Pressure",new[]{"hPa","mbar","mmHg","inHg"},display.Pressure,v=>display=display with{Pressure=v});Select("Temperature",new[]{"°C","°F"},display.Temperature,v=>display=display with{Temperature=v});Select("Radon",new[]{"Bq/m³","pCi/L"},display.Radon,v=>display=display with{Radon=v});
        var fonts=SetupSection("Fonts and sizes");body.Children.Add(fonts);var outerBody=body;body=new StackPanel{Margin=new Thickness(10,6,10,10)};fonts.Content=body;
        void SizeChoice(string title,double size,double defaultSize,double maximum,Action<double> setSize){var caption=new TextBlock{Text=title+" size · "+size.ToString("0"),Margin=new Thickness(0,6,0,0)};body.Children.Add(caption);var slider=new Slider{Name="AirthingsSize"+title.Replace(" ",""),Minimum=8,Maximum=maximum,Value=size,TickFrequency=1,IsSnapToTickEnabled=true};if(title=="History interval")slider.Name="AirthingsIntervalSize";SliderDefaults.Mark(slider,defaultSize);body.Children.Add(slider);slider.ValueChanged+=(_,_)=>{caption.Text=title+" size · "+slider.Value.ToString("0");setSize(slider.Value);SaveDisplay();Render();};}
        void FontChoice(string title,string font,double size,double defaultSize,Action<string> setFont,Action<double> setSize){Select(title+" font",new[]{"Teko","Segoe UI","Segoe UI Semibold"},font,setFont);SizeChoice(title,size,defaultSize,title=="Axes"?24:80,setSize);}
        var fontDefaults=new AirthingsDisplay();
        FontChoice("Title",display.TitleFont,display.TitleSize,fontDefaults.TitleSize,v=>display=display with{TitleFont=v},v=>display=display with{TitleSize=v});
        FontChoice("Current values",display.ValueFont,display.ValueSize,fontDefaults.ValueSize,v=>display=display with{ValueFont=v},v=>display=display with{ValueSize=v});
        FontChoice("Labels",display.LabelFont,display.LabelSize,fontDefaults.LabelSize,v=>display=display with{LabelFont=v},v=>display=display with{LabelSize=v});
        FontChoice("Axes",display.AxisFont,display.AxisSize,fontDefaults.AxisSize,v=>display=display with{AxisFont=v},v=>display=display with{AxisSize=v});
        if(gauges)FontChoice("Units",display.UnitFont,display.UnitSize,fontDefaults.UnitSize,v=>display=display with{UnitFont=v},v=>display=display with{UnitSize=v});
        SizeChoice(gauges?"Radon period":"History interval",display.IntervalSize,fontDefaults.IntervalSize,32,v=>display=display with{IntervalSize=v});
        SizeChoice("Sample information",display.FooterSize,fontDefaults.FooterSize,32,v=>display=display with{FooterSize=v});
        body=outerBody;var colors=SetupSection("Colors");var colorContent=new StackPanel{Margin=new Thickness(10,6,10,10)};var colorBody=new StackPanel{Name="AirthingsManualColors",IsEnabled=!display.UsesQualityColors,Opacity=display.UsesQualityColors?.45:1};colors.Content=colorContent;body.Children.Add(colors);
        body=colorContent;
        Select("Color scheme",new[]{"Manual","Airthings air quality"},display.UsesQualityColors?"Airthings air quality":"Manual",v=>{display=display with{ColorMode=v};colorBody.IsEnabled=!display.UsesQualityColors;colorBody.Opacity=display.UsesQualityColors?.45:1;});
        colorContent.Children.Add(new TextBlock{Text="Airthings: green / yellow / red readings and history. Temperature, pressure and older/missing samples stay neutral. Manual colors are retained while inactive.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,2)});colorContent.Children.Add(colorBody);body=outerBody;
        void ColorChoice(string title,string? current,Action<string?> set){colorBody.Children.Add(new TextBlock{Text=title,Margin=new Thickness(0,8,0,3)});colorBody.Children.Add(SensorCellEditor.ColorPicker(current,c=>{set(c);SaveDisplay();Render();}));}
        ColorChoice("Title",display.TitleColor,c=>display=display with{TitleColor=c});ColorChoice("Current values",display.ValueColor,c=>display=display with{ValueColor=c});ColorChoice("Labels",display.LabelColor,c=>display=display with{LabelColor=c});ColorChoice("Axes",display.AxisColor,c=>display=display with{AxisColor=c});if(!gauges)ColorChoice("Graph guides",display.GuideColor,c=>display=display with{GuideColor=c});
        if(!gauges)ColorChoice("Graph data",display.Color,c=>display=display with{Color=c??"#53B5FF"});
        if(!gauges){
        var importState=new TextBlock{Text="History stays on this computer, up to 1 year. CSV imports into the currently selected room; timestamps must be UTC. Gaps stay visible.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,4)};body.Children.Add(importState);body.Children.Add(MacroUi.Button("Import history CSV into selected room",async(_,_)=>{var device=service.Devices.FirstOrDefault(d=>Key(d)==selected);if(device==null){importState.Text="Choose a room first.";return;}var file=new Microsoft.Win32.OpenFileDialog{Filter="CSV files|*.csv",Title="Import Airthings history — "+device.Name};if(file.ShowDialog(window)==true)try{importState.Text="Importing history…";int imported=await Task.Run(()=>service.Archive.Import(file.FileName,device));importState.Text=$"Imported {imported} values into {device.Name}.";RenderReadings();}catch(Exception e){importState.Text=e.Message;}}));
        }
        body.Children.Add(CreateConnectionSection(window));var close=MacroUi.Button("Close",(_,_)=>{});close.Height=30;close.MinWidth=90;close.HorizontalAlignment=HorizontalAlignment.Center;close.Margin=new Thickness(0,10,0,0);body.Children.Add(close);close.Click+=(_,_)=>window.Close();return window;
    }
    internal SettingsWindow CreateGaugeLimits(Window? owner=null)
    {
        var original=presentation;bool committed=false;
        bool HasSensors(AirthingsDevice device)=>AirthingsStatusRules.Sensors(device).Length>0;
        bool SavedHasSensors(AirthingsStatusRoom room){var source=service.Devices.FirstOrDefault(device=>device.Account==room.Account&&device.Serial==room.Serial);return source==null||HasSensors(source);}
        var roomList=service.Devices.Where(HasSensors).Select(presentation.RoomForEditor)
            .Concat((presentation.Rooms??[]).Where(SavedHasSensors))
            .GroupBy(room=>(room.Account,room.Serial)).Select(group=>group.First()).ToArray();
        var options=new AirthingsStatusOptions(Rooms:roomList);
        void Apply(AirthingsStatusOptions value){presentation=original.WithRoomLimits(value.Rooms??[]);Render();}
        var dialog=AirthingsStatusMiniEditor.CreateForGaugeLimits(owner??Window.GetWindow(this),options,value=>{Apply(value);presentation.Save();committed=true;},Apply);
        string originalTitle=dialog.Title;dialog.Title="Airthings gauge limits";
        // AppearanceEditorUi.Frame has already copied the original title into
        // its local chrome. Rename only this reused dialog's visible heading.
        foreach(var heading in FindChildren(dialog).OfType<OpticallyCenteredText>().Where(text=>text.Text==originalTitle))heading.Text=dialog.Title;
        var tabs=FindChildren(dialog).OfType<TabControl>().FirstOrDefault(tab=>tab.Name=="AirthingsStatusEditorTabs");
        if(tabs!=null)
        {
            while(tabs.Items.Count>1)tabs.Items.RemoveAt(tabs.Items.Count-1);
            if(tabs.Items[0] is TabItem first&&first.Content is ScrollViewer scroll&&scroll.Content is StackPanel panel)
            {
                foreach(var text in panel.Children.OfType<TextBlock>().Take(3).ToArray())panel.Children.Remove(text);
                panel.Children.Insert(0,new TextBlock{Text="Select rooms to change their gauge limits; other rooms keep their choices. Only measurements supported by the device are shown. Use the pencil to rename a room in BlakiDeck only. Sensor checkboxes enable classification; temperature and pressure require your own limits. Smooth shading uses these existing boundaries without changing their categories.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4,4,4,10)});
                if(original.HasConflicts)panel.Children.Insert(0,new TextBlock{Text="Conflicting saved mini limits were not imported. Review the ranges here; Save makes these gauge choices authoritative.",Foreground=Brushes.Gold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4,4,4,8)});
            }
        }
        dialog.Closed+=(_,_)=>{if(!committed){presentation=original;Render();}};
        return dialog;
    }
    private static IEnumerable<DependencyObject> FindChildren(DependencyObject root)
    {
        foreach(var child in LogicalTreeHelper.GetChildren(root))if(child is DependencyObject element){yield return element;foreach(var descendant in FindChildren(element))yield return descendant;}
    }
    private Expander CreateConnectionSection(SettingsWindow window)
    {
        var section=SetupSection("Account connection",true);
        var connectionBody=new StackPanel();section.Content=connectionBody;
        var connectionStatus=new TextBlock{Name="AirthingsConnectionStatus",FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,4)};
        var connectionDetail=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)};connectionBody.Children.Add(connectionStatus);connectionBody.Children.Add(connectionDetail);
        bool editing=false,closed=false;
        var actions=new WrapPanel();connectionBody.Children.Add(actions);
        var change=MacroUi.Button("Change connection",(_,_)=>{});change.Name="AirthingsChangeConnection";change.Margin=new Thickness(0,0,8,6);actions.Children.Add(change);
        var retry=MacroUi.Button("Retry connection",(_,_)=>{});retry.Name="AirthingsRetryConnection";retry.Margin=new Thickness(0,0,0,6);actions.Children.Add(retry);
        var credentials=new StackPanel{Name="AirthingsConnectionCredentials",Margin=new Thickness(0,4,0,0)};connectionBody.Children.Add(credentials);
        credentials.Children.Add(new TextBlock{Text="Create an application at consumer-api-doc.airthings.com → My Applications. Choose Client Credentials and read:device:current_values. Enter its credentials below. They are protected for your Windows account.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
        credentials.Children.Add(new TextBlock{Text="Client ID"});var id=new TextBox{Margin=new Thickness(0,4,0,10)};id.ContextMenu=TextEditMenu.Create(id);credentials.Children.Add(id);
        credentials.Children.Add(new TextBlock{Text="Client secret"});var secret=new PasswordBox{Margin=new Thickness(0,4,0,12),Background=new SolidColorBrush(Color.FromRgb(38,35,31)),Foreground=Brushes.White,BorderBrush=Brushes.DimGray,Padding=new Thickness(8,4,8,4)};secret.ContextMenu=TextEditMenu.Create(secret);credentials.Children.Add(secret);
        var feedback=new TextBlock{TextWrapping=TextWrapping.Wrap};credentials.Children.Add(feedback);
        var credentialActions=new WrapPanel();credentials.Children.Add(credentialActions);
        var connect=MacroUi.Button("Save and connect",(_,_)=>{});connect.Height=32;connect.Margin=new Thickness(0,10,8,0);credentialActions.Children.Add(connect);
        var cancel=MacroUi.Button("Cancel change",(_,_)=>{});cancel.Height=32;cancel.Margin=new Thickness(0,10,0,0);credentialActions.Children.Add(cancel);
        bool Connected()=>service.Status.StartsWith("Connected",StringComparison.Ordinal)||(service.Status=="Refreshing…"&&service.RetrievedAt.HasValue);
        void UpdateConnection()
        {
            if(closed)return;
            bool configured=service.HasCredentials||service.RetrievedAt.HasValue;
            bool connected=Connected();
            connectionStatus.Text=connected?(service.Status=="Refreshing…"?"Connected — refreshing…":service.Status):service.HasCredentials&&service.Status=="Not connected — open Setup"?"Credentials saved — checking connection…":service.Status;
            connectionStatus.Foreground=connected?Brushes.LightGreen:Brushes.White;
            string lastCheck=service.RetrievedAt is {} time?$"Last successful cloud check {time.ToLocalTime():g}.":"";
            connectionDetail.Text=connected?$"{service.Devices.Length} {(service.Devices.Length==1?"device":"devices")} available. {lastCheck}":lastCheck;
            if(service.HasCredentials)connectionDetail.Text+=(connectionDetail.Text.Length>0?"\n":"")+(connected?"Credentials are saved securely for this Windows account.":"Saved credentials are retained. Retry the connection or choose Change connection to replace them.");
            connectionDetail.Visibility=connectionDetail.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
            credentials.Visibility=editing||!configured?Visibility.Visible:Visibility.Collapsed;
            change.Visibility=configured&&!editing?Visibility.Visible:Visibility.Collapsed;
            retry.Visibility=service.HasCredentials&&!connected&&!editing?Visibility.Visible:Visibility.Collapsed;
            cancel.Visibility=configured?Visibility.Visible:Visibility.Collapsed;window.RequestContentFit();
        }
        void ConnectionChanged(){if(window.Dispatcher.CheckAccess())UpdateConnection();else window.Dispatcher.BeginInvoke(UpdateConnection);}
        change.Click+=(_,_)=>{editing=true;feedback.Text="Enter replacement credentials. The current connection stays unchanged until Save and connect.";UpdateConnection();id.Focus();};
        cancel.Click+=(_,_)=>{editing=false;id.Clear();secret.Clear();feedback.Text="";UpdateConnection();};
        retry.Click+=async(_,_)=>{retry.IsEnabled=false;try{await service.Refresh(retry:true);}finally{retry.IsEnabled=true;UpdateConnection();}};
        connect.Click+=async(_,_)=>
        {
            connect.IsEnabled=false;editing=true;
            try{await service.Configure(id.Text,secret.Password);feedback.Text=service.Status;if(Connected()){editing=false;id.Clear();secret.Clear();}}
            finally{connect.IsEnabled=true;UpdateConnection();}
        };
        window.Loaded+=(_,_)=>{service.Changed+=ConnectionChanged;UpdateConnection();};
        window.Closed+=(_,_)=>{closed=true;service.Changed-=ConnectionChanged;secret.Clear();id.Clear();};
        UpdateConnection();return section;
    }
}



