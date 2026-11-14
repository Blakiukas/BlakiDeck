using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Ekrano_valdymas;
public partial class SystemMonitorWidget
{
    public sealed record MetricPresentation(string Mode="Value",string? Color=null,int Minutes=2,double VisibleSeconds=0,
        double TitleSize=14,double ValueSize=24,bool AutoFit=true,bool HideTitle=false,string? TitleColor=null,string? GuideColor=null,double GuideOpacity=.255,
        bool? NetworkDown=null,bool? NetworkUp=null,string? NetworkDownColor=null,string? NetworkUpColor=null,
        double? GraphTitleSize=null,double? GraphValueSize=null,double? GraphIntervalSize=null,string MemoryUnit="GB",double ResetTextSize=12,string? DiskId=null,string? GpuClockColor=null,string? HardwareSensorId=null,string? HardwareSensorLabel=null,string? DiskReadColor=null,string? DiskWriteColor=null,string? DiskTemperatureColor=null,string? CpuLoadColor=null,string? CpuTemperatureColor=null,string? CpuPowerColor=null,string? GpuPowerColor=null,Dictionary<string,string?>? StatusLabelColors=null,bool HideCorePercentage=false,double DetailTextSize=10,bool AdaptiveColors=false,Dictionary<string,AdaptiveColorRule>? AdaptiveRules=null,bool OneDecimal=false,bool ShowHardwareInfo=false);
    internal static IReadOnlyList<int> GraphDurationMinutes {get;}=Array.AsReadOnly(new[]{2,8,15,30,60});
    internal static double GraphIntervalFontSize(MetricPresentation value,bool topBar=false)
        =>value.GraphIntervalSize is {} size&&double.IsFinite(size)?Math.Clamp(size,8,80):topBar?8:10;
    // Keep the two appended enum identities readable for saved layouts. New
    // choices expose one NETWORK source; direction belongs to the cell.
    internal static IEnumerable<MetricKind> SelectableMetrics=>Enum.GetValues<MetricKind>().Where(k=>k is not MetricKind.NetworkDownload and not MetricKind.NetworkUpload and not MetricKind.CodexWeekly and not MetricKind.GpuPowerLimit and not MetricKind.GpuPowerLimitActive and not MetricKind.GpuThermalLimitActive and not MetricKind.GpuLimitReason);
    internal static MetricKind NormalizeMetric(MetricKind kind)=>kind is MetricKind.NetworkDownload or MetricKind.NetworkUpload?MetricKind.Network:kind==MetricKind.CodexWeekly?MetricKind.CodexFiveHour:kind;
    internal static MetricPresentation NormalizeNetworkPresentation(MetricKind kind,MetricPresentation value)
        =>value.NetworkDown==null&&value.NetworkUp==null&&kind is (MetricKind.NetworkDownload or MetricKind.NetworkUpload)
            ?value with{NetworkDown=kind==MetricKind.NetworkDownload,NetworkUp=kind==MetricKind.NetworkUpload}:value;
    internal static MetricPresentation EffectiveNetworkPresentation(MetricPresentation value,bool largeGraph)
    {
        bool down=value.NetworkDown==true,up=value.NetworkUp==true;
        if(value.NetworkDown==null&&value.NetworkUp==null){down=true;up=largeGraph;}
        if(!down&&!up)down=true;
        if(!largeGraph&&value.Mode=="Graph"&&down&&up)up=false;
        return value with{NetworkDown=down,NetworkUp=up};
    }
    internal static string? NetworkDataColor(MetricPresentation value,bool upload)=>upload?value.NetworkUpColor??value.Color:value.NetworkDownColor??value.Color;
    internal static string NetworkDisplay(IReadOnlyDictionary<MetricKind,double?> readings,MetricPresentation preference,bool multiline)
    {
        var lines=new List<string>();
        string Rate(MetricKind kind)=>MetricInfo.Format(kind,readings.GetValueOrDefault(kind),0);
        if(preference.NetworkDown==true||preference.NetworkUp!=true)lines.Add("DL "+Rate(MetricKind.NetworkDownload));
        if(preference.NetworkUp==true)lines.Add("UP "+Rate(MetricKind.NetworkUpload));
        return string.Join(multiline?"\n":" · ",lines);
    }
    private Dictionary<MetricKind,MetricPresentation> presentations=new();
    private string PresentationPath=>Path.Combine(DiagnosticState.LocalApplicationData,"Ekrano_valdymas","system-monitor-presentation.json");
    private void LoadPresentations()
    {
        try{if(File.Exists(PresentationPath))presentations=JsonSerializer.Deserialize<Dictionary<MetricKind,MetricPresentation>>(File.ReadAllText(PresentationPath))??new();}catch(Exception e){InteractionDiagnostics.RecordFailure("Metric presentation",e);}
        foreach(var slot in Slots){ApplyPresentation(slot);slot.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(MonitorSlot.Kind))ApplyPresentation(slot);};}
    }
    private void ApplyPresentation(MonitorSlot slot)=>slot.SetPresentation(GetPresentation(slot.Kind));
    internal void SetPresentation(MetricKind kind,MetricPresentation value)
    {
        if(value.Mode=="Percent"&&kind!=MetricKind.MemoryUsed)value=value with{Mode="Value"};
        presentations[kind]=value;foreach(var slot in Slots.Where(s=>s.Kind==kind))ApplyPresentation(slot);
        Directory.CreateDirectory(Path.GetDirectoryName(PresentationPath)!);File.WriteAllText(PresentationPath+".tmp",JsonSerializer.Serialize(presentations));File.Move(PresentationPath+".tmp",PresentationPath,true);RefreshValues();
    }
    private DeferredCellClick? metricClick;
    private void Metric_PreviewMouseLeftButtonUp(object sender,MouseButtonEventArgs e)
    {
        if(liveGridGesture)SwapLiveCell(e.GetPosition(this));
        var slot=pressedSlot;pressedSlot=null;
        if(liveGridChanged){liveGridChanged=false;liveGridStart=null;if(sender is UIElement dragElement)dragElement.ReleaseMouseCapture();ApplyGridLayout(CaptureGridLayout());SaveSlots();e.Handled=true;return;}
        liveGridStart=null;
        if(sender is UIElement element&&element.IsMouseCaptured)element.ReleaseMouseCapture();
        if(liveGridGesture){liveGridGesture=false;e.Handled=true;return;}
        if(slot?.IsShortcut==true){if(!slot.Shortcut!.IsEmpty){ActivateShortcut(slot.Shortcut);return;}(metricClick??=new DeferredCellClick(this)).Schedule(()=>{if(!Slots.Contains(slot))return;if(slot.Shortcut!.IsEmpty){var file=new Microsoft.Win32.OpenFileDialog{Title="Choose shortcut"};if(file.ShowDialog(Window.GetWindow(this))==true){slot.SetContent(file.FileName);SaveSlots();ReadingsChanged?.Invoke();}}else ActivateShortcut(slot.Shortcut);});}
    }
    internal ContextMenu PresentationMenu(MonitorSlot slot,Action<MetricKind>? select=null,Action<MetricPresentation>? apply=null,MetricPresentation? appearance=null,Action? clear=null)
    {
        var menu=ListAppearanceUi.Create(this);
        var preference=NormalizeNetworkPresentation(slot.Kind,appearance??slot.Presentation);
        if(NormalizeMetric(slot.Kind)==MetricKind.Network)preference=EffectiveNetworkPresentation(preference,slot.LargeGraph);
        Action? refreshNetworkColors=null;
        void Save(MetricPresentation p){preference=p;if(apply!=null)apply(p);else SetPresentation(slot.Kind,p);}
        var caption=new OpticallyCenteredText{Text=slot.Label,TextFontFamily=menu.FontFamily,TextFontWeight=FontWeights.SemiBold,TextFontSize=menu.FontSize*.88,TextBrush=Brushes.LightGray,CompactLineBox=true};
        var heading=new MenuItem{Header=new Border{BorderBrush=new SolidColorBrush(Color.FromArgb(80,255,255,255)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(4),Padding=new Thickness(8,3,8,3),Margin=new Thickness(0,0,0,4),VerticalAlignment=VerticalAlignment.Center,Child=caption},Focusable=false,IsHitTestVisible=false};menu.Items.Add(heading);
        menu.Opened+=(_,_)=>caption.TextFontSize=heading.FontSize*.88;
        if(slot.Shortcut==null)menu.Items.Add(new Separator());
        if(slot.Shortcut is {} shortcut)
        {
            var owner=Window.GetWindow(this)??Application.Current.MainWindow;
            void Action(string title,System.Action run){var item=new MenuItem{Header=title};item.Click+=(_,_)=>{run();SaveSlots();ReadingsChanged?.Invoke();};menu.Items.Add(item);}
            Action("Choose shortcut…",()=>{var file=new Microsoft.Win32.OpenFileDialog();if(file.ShowDialog(owner)==true)slot.SetContent(file.FileName);});
            if(!shortcut.IsEmpty)
            {
                Action("Appearance…",()=>ShortcutAppearanceEditor.Create(owner,shortcut.Name,shortcut.Target!,shortcut.Presentation??"IconText",(name,target,appearance,icon)=>{slot.SetContent(target,name,appearance,icon);SaveSlots();},iconPath:shortcut.IconPath).ShowDialog());
                if(MacroLibrary.IsReference(shortcut.Target))Action("Edit macro",()=>{if(MacroLibrary.Current.Find(shortcut.Target) is {} macro)MacroEditor.Show(owner,macro);});
                Action("Rename",()=>{var dialog=new ShortcutRenameDialog(shortcut.Name){Owner=owner};if(dialog.ShowDialog()==true)shortcut.Rename(dialog.ShortcutName);});
                if(ShortcutShellActions.IsFileTarget(shortcut.Target))Action("Properties",()=>ShortcutShellActions.ShowProperties(owner,shortcut.Target));

            }
            menu.Items.Add(new Separator());
        }

        var metric=new MenuItem{Header="Sensor"};menu.Items.Add(metric);
        foreach(var kind in SelectableMetrics)
        {
            var item=new MenuItem{Header=MetricInfo.Label(kind),IsCheckable=true,IsChecked=NormalizeMetric(slot.Kind)==kind};
            item.Click+=(_,_)=>{if(select!=null){select(kind);return;}slot.SetContent("sensor:"+kind);FitNewStatusCell(Slots.IndexOf(slot),kind);UpdateSensorActivity();RefreshValues();SaveSlots();};AddGroupedSensor(metric,kind,item);
        }
        if(slot.IsShortcut)
        {
            menu.Items.Add(new Separator());
            var clearShortcut=new MenuItem{Header="Clear cell"};clearShortcut.Click+=(_,_)=>ClearMonitorCell(slot);menu.Items.Add(clearShortcut);
            var deleteShortcut=new MenuItem{Header="Delete cell"};deleteShortcut.Click+=(_,_)=>DeleteMonitorCell(slot);menu.Items.Add(deleteShortcut);
            return menu;
        }
        if(MetricGroup(slot.Kind)=="GPU")
        {
            var gpu=new MenuItem{Header="GPU device"};menu.Items.Add(gpu);
            foreach(var adapter in GpuTelemetrySelection.Adapters){var item=new MenuItem{Header=adapter.Name+" · "+adapter.Id,IsCheckable=true,IsChecked=GpuTelemetrySelection.Current?.Id==adapter.Id};item.Click+=(_,_)=>GpuTelemetrySelection.Select(adapter.Id);gpu.Items.Add(item);}
        }
        if(IsDisk(slot.Kind))
        {
            var disks=new MenuItem{Header="Disk"};menu.Items.Add(disks);
            void FillDisks(){disks.Items.Clear();foreach(string key in (Shared??this).DiskChoices(slot.Kind,preference.DiskId)){var item=new MenuItem{Header=DiskLabel(key),IsCheckable=true,IsChecked=key==(preference.DiskId??"_Total")};item.Click+=(_,_)=>Save(preference with{DiskId=key=="_Total"?null:key});disks.Items.Add(item);}}
            FillDisks();disks.SubmenuOpened+=(_,_)=>FillDisks();
        }
        if(NormalizeMetric(slot.Kind)==MetricKind.Network)
        {
            var network=new MenuItem{Header="Network settings"};menu.Items.Add(network);
            var down=new MenuItem{Header="DL",IsCheckable=true,IsChecked=preference.NetworkDown==true,StaysOpenOnClick=true};
            var up=new MenuItem{Header="UP",IsCheckable=true,IsChecked=preference.NetworkUp==true,StaysOpenOnClick=true};
            void Direction(bool upload)
            {
                if(!slot.LargeGraph&&preference.Mode=="Graph"){down.IsChecked=!upload;up.IsChecked=upload;}
                else if(!down.IsChecked&&!up.IsChecked){if(upload)up.IsChecked=true;else down.IsChecked=true;}
                Save(preference with{NetworkDown=down.IsChecked,NetworkUp=up.IsChecked});refreshNetworkColors?.Invoke();
            }
            down.Click+=(_,_)=>Direction(false);up.Click+=(_,_)=>Direction(true);
            network.Items.Add(down);network.Items.Add(up);
            network.ToolTip=slot.LargeGraph?"Choose DL, UP or both. DL is solid; UP is dashed when both are shown.":preference.Mode=="Graph"?"Choose DL or UP for this small graph. Numeric value can show both.":"Choose DL, UP or both numeric readings. A small graph shows one direction.";
        }
        if(menu.Items[menu.Items.Count-1] is not Separator)menu.Items.Add(new Separator());
        void Mode(string title,string mode){var item=new MenuItem{Header=title,IsCheckable=true,IsChecked=!SupportsGraph(slot.Kind)?mode=="Value":mode=="Graph"?slot.LargeGraph||preference.Mode=="Graph":!slot.LargeGraph&&(preference.Mode==mode||mode=="Value"&&preference.Mode=="Information"),IsEnabled=!SupportsGraph(slot.Kind)||!slot.LargeGraph||mode=="Graph"};item.Click+=(_,_)=>Save(NormalizeMetric(slot.Kind)==MetricKind.Network?EffectiveNetworkPresentation(preference with{Mode=mode},slot.LargeGraph):preference with{Mode=mode});menu.Items.Add(item);}
        if(SupportsGraph(slot.Kind)&&NormalizeMetric(slot.Kind)!=MetricKind.CodexFiveHour)Mode(slot.Kind is MetricKind.CpuLoad or MetricKind.GpuLoad or MetricKind.MemoryLoad or MetricKind.CpuCoreLoad?"Percentage":"Numeric value","Value");
        if(slot.Kind==MetricKind.MemoryUsed)Mode("Percentage","Percent");
        if(SupportsGraph(slot.Kind))Mode("Line graph","Graph");
        if(slot.Kind is (MetricKind.CpuLoad or MetricKind.GpuLoad)&&(slot.LargeGraph||preference.Mode=="Graph"))
        {
            var hardware=new MenuItem{Header="Model",IsCheckable=true,IsChecked=preference.ShowHardwareInfo,ToolTip="CPU / GPU model"};
            hardware.Click+=(_,_)=>Save(preference with{ShowHardwareInfo=hardware.IsChecked});menu.Items.Add(hardware);
        }

        if(slot.Kind is MetricKind.VramUsed or MetricKind.MemoryUsed||slot.Kind==MetricKind.HardwareSensor&&HardwareTelemetry.Find(preference.HardwareSensorId)?.Unit=="MB")
        {
            var units=new MenuItem{Header="Units"};
            foreach(string unit in new[]{"MB","GB"}){var item=new MenuItem{Header=unit,IsCheckable=true,IsChecked=preference.MemoryUnit==unit};item.Click+=(_,_)=>Save(preference with{MemoryUnit=unit});units.Items.Add(item);}
            units.Items.Add(new Separator());
            var precision=new MenuItem{Header="1 decimal",IsCheckable=true,IsChecked=preference.OneDecimal};
            precision.Click+=(_,_)=>Save(preference with{OneDecimal=precision.IsChecked});
            units.Items.Add(precision);
            menu.Items.Add(units);
        }
        var duration=new MenuItem{Header="Graph duration"};if(SupportsGraph(slot.Kind)&&(slot.LargeGraph||preference.Mode=="Graph"))menu.Items.Add(duration);
        foreach(var minutes in GraphDurationMinutes){var item=new MenuItem{Header=$"{minutes} minutes",IsCheckable=true,IsChecked=preference.Minutes==minutes};item.Click+=(_,_)=>Save(preference with{Minutes=minutes,VisibleSeconds=0});duration.Items.Add(item);}
        if(menu.Items[menu.Items.Count-1] is not Separator)menu.Items.Add(new Separator());
        var colors=new MenuItem{Header="Colors"};menu.Items.Add(colors);
        var adaptive=new MenuItem{Name="AdaptiveColors",IsCheckable=true,IsChecked=preference.AdaptiveColors,StaysOpenOnClick=true};
        var adaptiveHeader=new StackPanel{Orientation=Orientation.Horizontal};adaptiveHeader.Children.Add(new TextBlock{Text="Adaptive colors",VerticalAlignment=VerticalAlignment.Center});
        var pencil=AdaptiveColorEditor.Pencil(()=>{menu.IsOpen=false;new AdaptiveColorEditor(Window.GetWindow(this)??Application.Current.MainWindow,slot.Kind,preference,Save).ShowDialog();adaptive.IsChecked=preference.AdaptiveColors;});
        pencil.SetBinding(WidthProperty,new System.Windows.Data.Binding(nameof(MenuItem.FontSize)){Source=adaptive});pencil.SetBinding(HeightProperty,new System.Windows.Data.Binding(nameof(MenuItem.FontSize)){Source=adaptive});
        adaptiveHeader.Children.Add(pencil);adaptive.Header=adaptiveHeader;
        adaptive.Click+=(_,_)=>Save(preference with{AdaptiveColors=adaptive.IsChecked});menu.Items.Add(adaptive);
        if(NormalizeMetric(slot.Kind)==MetricKind.Network)
        {
            refreshNetworkColors=()=>{colors.Items.Clear();foreach(bool upload in new[]{false,true}.Where(upload=>upload?preference.NetworkUp==true:preference.NetworkDown==true))
            {
                var direction=new MenuItem{Header=upload?"UP":"DL"};colors.Items.Add(direction);
                ColorSwatchRow.BindMenu(upload?preference.NetworkUpColor:preference.NetworkDownColor,color=>Save(upload?preference with{NetworkUpColor=color}:preference with{NetworkDownColor=color}),direction,Window.GetWindow(this)??Application.Current.MainWindow);
            }
            };refreshNetworkColors();
        }
        else ColorSwatchRow.BindMenu(preference.Color,color=>Save(preference with{Color=color}),colors,Window.GetWindow(this)??Application.Current.MainWindow);
        menu.Items.Add(new Separator());
        var restore=new MenuItem{Header="Default"};restore.Click+=(_,_)=>Save(ViewKey=="full"?SensorFactoryDefaults.Reference(CellLocation(slot),NormalizeMetric(slot.Kind),preference):SensorFactoryDefaults.ForView(ViewKey,NormalizeMetric(slot.Kind)) with{Mode=slot.LargeGraph?"Graph":"Value"});menu.Items.Add(restore);
        var clearCell=new MenuItem{Header="Clear cell"};clearCell.Click+=(_,_)=>{if(clear!=null){clear();return;}ClearMonitorCell(slot);};menu.Items.Add(clearCell);
        if(Slots.Contains(slot)){var delete=new MenuItem{Header="Delete cell"};delete.Click+=(_,_)=>DeleteMonitorCell(slot);menu.Items.Add(delete);}
        return menu;
    }
    internal void OpenMetricOptions(Border border,MonitorSlot slot)
    {
        var menu=PresentationMenu(slot);border.ContextMenu=menu;menu.PlacementTarget=border;
        menu.Placement=System.Windows.Controls.Primitives.PlacementMode.MousePoint;menu.IsOpen=true;
    }
    public sealed partial class MonitorSlot
    {
        public MetricPresentation Presentation {get;private set;}=new();
        public bool LargeGraph {get;private set;}
        internal void SetLargeGraph(bool value){LargeGraph=value;Notify(nameof(LargeGraph));Notify(nameof(IsGraph));}
        public bool IsGraph=>LargeGraph||Presentation.Mode=="Graph";
        public bool ShowCoreBars=>IsCoreLoad&&!IsGraph;
        public PointCollection GraphPoints {get;private set;}=new();
        public void SetPresentation(MetricPresentation value){Presentation=value with{Minutes=GraphDurationMinutes.Contains(value.Minutes)?value.Minutes:2};Notify(nameof(Presentation));Notify(nameof(IsGraph));Notify(nameof(ShowCoreBars));}
        private void UpdatePresentation(double? value,double totalRam)
        {
            if(Presentation.Mode=="Percent"&&Kind==MetricKind.MemoryUsed&&value.HasValue&&totalRam>0)DisplayValue=$"{value/totalRam*100:0}%";
            if(Presentation.Color is {} color&&value.HasValue)try{ValueBrush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));}catch{}
        }
    }
}
