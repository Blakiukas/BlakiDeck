using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static Ekrano_valdymas.SystemMonitorWidget;
namespace Ekrano_valdymas;

internal sealed class SensorCellEditor:SettingsWindow
{
    private DockPanel? editorRoot; internal Action? EditLayout {get;set;}
    private Action commit=()=>{};private string? draftGpu;
    internal SensorCellEditor(Window owner,MetricKind kind,MetricPresentation appearance,Action<MetricKind> choose,Action<MetricPresentation> apply,bool largeGraph=false,MetricPresentation? defaults=null,Func<MetricKind,MetricPresentation>? baseSettings=null,bool topBar=false)
    {
        var draft=appearance;draftGpu=GpuTelemetrySelection.Current?.Id;var originalGpu=draftGpu;
        bool saved=false,previewed=false;
        commit=()=>{apply(draft);saved=true;};
        Closed+=(_,_)=>{if(saved)return;if(previewed)apply(appearance);if(originalGpu!=null&&draftGpu!=originalGpu)GpuTelemetrySelection.Select(originalGpu);};
        Build(owner,kind,appearance,choose,p=>{draft=p;previewed=true;apply(p);},largeGraph,defaults,baseSettings,topBar);
    }
    private void Build(Window owner,MetricKind kind,MetricPresentation appearance,Action<MetricKind> choose,Action<MetricPresentation> apply,bool largeGraph,MetricPresentation? defaults,Func<MetricKind,MetricPresentation>? baseSettings,bool topBar)
    {
        Owner=owner;ShowInTaskbar=false;Title=GetMetricLabel(kind)+" — settings";
        Width=520;if(!IsLoaded)Height=800;MinWidth=400;MinHeight=440;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        MacroUi.ApplyTheme(this);FontSize=14;
        var root=editorRoot??=new DockPanel{Margin=new Thickness(20),LastChildFill=true};if(root.Parent==null)Content=root;root.Children.Clear();
        var current=NormalizeNetworkPresentation(kind,appearance);
        var close=MacroUi.Button("Cancel",(_,_)=>Close());close.Name="SensorCellClose";close.Width=110;close.HorizontalAlignment=HorizontalAlignment.Right;
        var footer=new DockPanel{Margin=new Thickness(0,18,0,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var restore=MacroUi.Button("Default",(_,_)=>{var baseline=(baseSettings?.Invoke(kind)??defaults??SensorFactoryDefaults.ForMetric(kind)) with{Mode=largeGraph?"Graph":current.Mode,HardwareSensorId=current.HardwareSensorId,HardwareSensorLabel=current.HardwareSensorLabel,DiskId=current.DiskId};apply(baseline);Build(owner,kind,baseline,choose,apply,largeGraph,baseline,baseSettings,topBar);});restore.Name="SensorCellDefault";DockPanel.SetDock(restore,Dock.Left);footer.Children.Add(restore);var save=MacroUi.Button("Save",(_,_)=>{commit();Close();});save.Name="SensorCellSave";save.Width=110;DockPanel.SetDock(save,Dock.Right);footer.Children.Add(save);footer.Children.Add(close);
        var mainTabs=SettingsEditorTabs.Create("SensorCellMainTabs");root.Children.Add(mainTabs);
        var cellBody=new StackPanel{Name="SensorCellFields"};
        var colorsBody=new StackPanel{Name="SensorCellColorsFields"};
        var cellScroll=MacroUi.Scroll(cellBody);AutoFitScroll=cellScroll;
        mainTabs.Items.Add(new TabItem{Name="CellTab",Header="Cell",Content=cellScroll});
        mainTabs.Items.Add(new TabItem{Name="ColorsTab",Header="Colors",Content=MacroUi.Scroll(colorsBody)});        var layoutTab=new TabItem{Name="SensorLayoutAction",Header="Layout",Visibility=EditLayout!=null?Visibility.Visible:Visibility.Collapsed};
        mainTabs.SelectionChanged+=(_,e)=>{if(ReferenceEquals(e.Source,mainTabs)&&mainTabs.SelectedItem is TabItem {Content:ScrollViewer selectedScroll}){AutoFitScroll=selectedScroll;RequestContentFit();}if(ReferenceEquals(e.Source,mainTabs)&&ReferenceEquals(mainTabs.SelectedItem,layoutTab)&&EditLayout is {} action){Close();Dispatcher.BeginInvoke(action);}};
        mainTabs.Items.Add(layoutTab);Loaded+=(_,_)=>layoutTab.Visibility=EditLayout!=null?Visibility.Visible:Visibility.Collapsed;


        if(NormalizeMetric(kind)==MetricKind.Network)current=EffectiveNetworkPresentation(current,largeGraph);
        Action? refreshTextSizes=null;Action? refreshVisibility=null;
        void Change(Func<MetricPresentation,MetricPresentation> edit){current=edit(current);apply(current);}
        var adaptiveRow=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,8,0,12)};
        var adaptive=new CheckBox{Name="AdaptiveColors",Content="Adaptive colors",Foreground=Brushes.White,IsChecked=current.AdaptiveColors,VerticalAlignment=VerticalAlignment.Center};adaptiveRow.Children.Add(adaptive);
        adaptive.Click+=(_,_)=>Change(p=>p with{AdaptiveColors=adaptive.IsChecked==true});
        adaptiveRow.Children.Add(AdaptiveColorEditor.Pencil(()=>{new AdaptiveColorEditor(this,kind,current,p=>{current=p;apply(p);adaptive.IsChecked=p.AdaptiveColors;}).ShowDialog();}));colorsBody.Children.Add(adaptiveRow);
        TextBlock Label(Panel panel,string text)
        {
            var label=new TextBlock{Text=text,Margin=new Thickness(0,10,0,4)};
            panel.Children.Add(label);return label;
        }
        if(kind==MetricKind.HardwareSensor)
        {
            Label(cellBody,"Device and sensor");var sources=new ComboBox{Name="HardwareSource",MinWidth=200};cellBody.Children.Add(sources);
            string Caption(HardwareReading reading)=>reading.Label+" · "+(reading.Display!="N/A"?reading.Display:reading.Unit+" — "+(reading.Fresh?"No data":"Stale data"));
            void FillSources(){sources.Items.Clear();foreach(var reading in HardwareTelemetry.Readings){var item=new ComboBoxItem{Content=Caption(reading),Tag=reading.Id};sources.Items.Add(item);if(reading.Id==current.HardwareSensorId)sources.SelectedItem=item;}}
            bool filling=true;FillSources();filling=false;sources.DropDownOpened+=(_,_)=>{filling=true;FillSources();filling=false;};
            // Update existing rows in place: preserve selection, keyboard focus and scroll position.
            var refresh=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
            refresh.Tick+=(_,_)=>{var readings=HardwareTelemetry.Readings.ToDictionary(r=>r.Id);foreach(var item in sources.Items.OfType<ComboBoxItem>())if(item.Tag is string id){string caption=readings.TryGetValue(id,out var reading)?Caption(reading):(item.Content?.ToString()?.Split(" · ")[0]??id)+" · Unavailable";if(!Equals(item.Content,caption))item.Content=caption;}};
            sources.Loaded+=(_,_)=>refresh.Start();sources.Unloaded+=(_,_)=>refresh.Stop();
            sources.SelectionChanged+=(_,_)=>{if(!filling&&sources.SelectedItem is ComboBoxItem{Tag:string id}&&HardwareTelemetry.Find(id) is {} selected){Change(p=>p with{HardwareSensorId=id,HardwareSensorLabel=selected.Label});Build(owner,kind,current,choose,apply,largeGraph,defaults,baseSettings,topBar);}};
            cellBody.Children.Add(new TextBlock{Text="Exact device sensor. Missing, invalid or expired readings: N/A. Hardware expires after 5 seconds; page-file readings after 30 seconds. No automatic replacement.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)});
        }
        if(MetricGroup(kind)=="GPU")
        {
            Label(cellBody,"GPU device — applies to all GPU readings");
            var devices=new ComboBox{MinWidth=180};cellBody.Children.Add(devices);
            foreach(var adapter in GpuTelemetrySelection.Adapters){var item=new ComboBoxItem{Content=adapter.Name+" · "+adapter.Id,Tag=adapter.Id};devices.Items.Add(item);if(adapter.Id==draftGpu)devices.SelectedItem=item;}
            devices.SelectionChanged+=(_,_)=>{if(devices.SelectedItem is ComboBoxItem{Tag:string id}){draftGpu=id;GpuTelemetrySelection.Select(id);}};
        }
        if(IsDisk(kind))
        {
            Label(cellBody,"Disk");
            var disks=new ComboBox{Name="SensorDisk",MinWidth=180};cellBody.Children.Add(disks);
            bool filling=false;
            void FillDisks(){filling=true;disks.Items.Clear();foreach(string key in SystemMonitorWidget.Shared?.DiskChoices(kind,current.DiskId)??Enumerable.Empty<string>()){var item=new ComboBoxItem{Content=DiskLabel(key),Tag=key};disks.Items.Add(item);if(key==(current.DiskId??(IsDiskRate(kind)?"_Total":SystemMonitorWidget.Shared?.SelectedDisk(null)?.SelectionId)))disks.SelectedItem=item;}filling=false;}
            FillDisks();disks.DropDownOpened+=(_,_)=>FillDisks();
            disks.SelectionChanged+=(_,_)=>{if(!filling&&disks.SelectedItem is ComboBoxItem{Tag:string key})Change(p=>p with{DiskId=key=="_Total"?null:key});};
        }
        if(kind is >=MetricKind.GpuPowerLimit and <=MetricKind.GpuStatus)
            cellBody.Children.Add(new TextBlock{Name="GpuDiagnosticDetails",Text=SystemMonitorWidget.Shared?.GpuDetails??"GPU data unavailable",TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,8,0,8)});
        if(kind is MetricKind.DiskWear or MetricKind.DiskHealth)
            cellBody.Children.Add(new TextBlock{Text=kind==MetricKind.DiskWear?"Manufacturer-reported endurance used. This is not remaining health. N/A means not reported.":"Estimated remaining endurance: 100% minus reported wear. This does not guarantee fault-free operation. N/A means wear is not reported.",TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,8,0,8)});
        var unitsPanel=new StackPanel{Visibility=(kind is MetricKind.MemoryUsed or MetricKind.VramUsed||kind==MetricKind.HardwareSensor&&HardwareTelemetry.Find(current.HardwareSensorId)?.Unit=="MB")?Visibility.Visible:Visibility.Collapsed};Label(unitsPanel,"Units");
        var units=new ComboBox{Name="SensorMemoryUnit",ItemsSource=new[]{"GB","MB"},SelectedItem=current.MemoryUnit};unitsPanel.Children.Add(units);cellBody.Children.Add(unitsPanel);
        units.SelectionChanged+=(_,_)=>{if(units.SelectedItem is string unit)Change(p=>p with{MemoryUnit=unit});};
        var network=new StackPanel{Name="NetworkDirections",Margin=new Thickness(0,10,0,0),Visibility=NormalizeMetric(kind)==MetricKind.Network?Visibility.Visible:Visibility.Collapsed};
        network.Children.Add(new TextBlock{Text="Network directions",Margin=new Thickness(0,0,0,6)});
        var directions=new StackPanel{Orientation=Orientation.Horizontal};network.Children.Add(directions);
        var down=new CheckBox{Content="DL",IsChecked=current.NetworkDown==true,Foreground=Brushes.White,Margin=new Thickness(0,0,24,0)};
        var up=new CheckBox{Content="UP",IsChecked=current.NetworkUp==true,Foreground=Brushes.White};directions.Children.Add(down);directions.Children.Add(up);
        var directionHint=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,6,0,0)};network.Children.Add(directionHint);
        void ReflectDirections()
        {
            down.IsChecked=current.NetworkDown;up.IsChecked=current.NetworkUp;
            directionHint.Text=largeGraph?"Choose DL, UP or both in this large graph.":current.Mode=="Graph"?
                "Choose DL or UP for this small graph. Numeric value can show both.":
                "Choose DL, UP or both numeric readings. A small graph shows one direction.";
        }
        ReflectDirections();network.ToolTip="DL and UP have separate colors. With both enabled, DL is solid and UP is dashed.";cellBody.Children.Add(network);
        void DirectionsChanged(bool upload)
        {
            if(!largeGraph&&current.Mode=="Graph"){down.IsChecked=!upload;up.IsChecked=upload;}
            else if(down.IsChecked!=true&&up.IsChecked!=true){if(upload)up.IsChecked=true;else down.IsChecked=true;}
            Change(p=>p with{NetworkDown=down.IsChecked==true,NetworkUp=up.IsChecked==true});refreshVisibility?.Invoke();SettingsEditorTabs.RequestFit(mainTabs);
        }
        down.Click+=(_,_)=>DirectionsChanged(false);up.Click+=(_,_)=>DirectionsChanged(true);

        var presentationFields=new StackPanel{Visibility=!SupportsGraph(kind)||NormalizeMetric(kind)==MetricKind.CodexFiveHour?Visibility.Collapsed:Visibility.Visible};cellBody.Children.Add(presentationFields);
        Label(presentationFields,"Presentation");
        var mode=new ComboBox{Name="SensorCellPresentation",ItemsSource=Modes(kind),SelectedItem=largeGraph?"Graph":current.Mode=="Information"?"Value":current.Mode};presentationFields.Children.Add(mode);
        string[] Modes(MetricKind selected)=>largeGraph?new[]{"Graph"}:selected is MetricKind.CodexFiveHour or MetricKind.CodexWeekly?
            new[]{"Value","Graph"}:selected==MetricKind.MemoryUsed?new[]{"Value","Percent","Graph"}:new[]{"Value","Graph"};
        mode.SelectionChanged+=(_,_)=>
        {
            if(mode.SelectedItem is string selectedMode)
            {
                Change(p=>network.Visibility==Visibility.Visible?EffectiveNetworkPresentation(p with{Mode=selectedMode},largeGraph):p with{Mode=selectedMode});
                ReflectDirections();refreshTextSizes?.Invoke();refreshVisibility?.Invoke();SettingsEditorTabs.RequestFit(mainTabs);
            }
        };
        var textOptions=new WrapPanel{Name="SensorTextOptions",Margin=new Thickness(0,12,0,0)};cellBody.Children.Add(textOptions);
        var fit=new CheckBox{Foreground=Brushes.White,Content="Auto-fit text",IsChecked=current.AutoFit,Margin=new Thickness(0,0,24,0)};
        textOptions.Children.Add(fit);fit.Click+=(_,_)=>Change(p=>p with{AutoFit=fit.IsChecked==true});
        var hide=new CheckBox{Foreground=Brushes.White,Content="Hide title",IsChecked=current.HideTitle,Margin=new Thickness(0)};
        textOptions.Children.Add(hide);hide.Click+=(_,_)=>Change(p=>p with{HideTitle=hide.IsChecked==true});
        if(kind==MetricKind.CpuCoreLoad){var hidePercent=new CheckBox{Name="HideCorePercentage",Foreground=Brushes.White,Content="Hide percentage",IsChecked=current.HideCorePercentage,Margin=new Thickness(0,10,0,0)};cellBody.Children.Add(hidePercent);hidePercent.Click+=(_,_)=>Change(p=>p with{HideCorePercentage=hidePercent.IsChecked==true});}
        bool updatingTextSizes=false;
        Slider Size(Panel panel,string title,double initial,double min,double max,Action<double> set,double? defaultValue=null)
        {
            var caption=Label(panel,title+" — "+initial.ToString("0.#"));
            var slider=new Slider{Minimum=min,Maximum=max,Value=initial,TickFrequency=1,IsSnapToTickEnabled=true};
            if(defaultValue is {} reference)SliderDefaults.Mark(slider,reference,"Base setting");
            panel.Children.Add(slider);
            slider.ValueChanged+=(_,e)=>{caption.Text=title+" — "+e.NewValue.ToString("0.#");if(!updatingTextSizes)set(e.NewValue);};
            return slider;
        }
        defaults??=new(GraphTitleSize:20,GraphValueSize:20);
        bool GraphSizes()=>SupportsGraph(kind)&&(largeGraph||current.Mode=="Graph");
        double TitleSize()=>GraphSizes()?current.GraphTitleSize??current.TitleSize:current.TitleSize;
        double ValueSize()=>GraphSizes()?current.GraphValueSize??current.ValueSize:current.ValueSize;
        double DefaultTitleSize()=>GraphSizes()?defaults.GraphTitleSize??20:defaults.TitleSize;
        double DefaultValueSize()=>GraphSizes()?defaults.GraphValueSize??20:defaults.ValueSize;
        var titleSize=Size(cellBody,"Title size",TitleSize(),8,80,n=>Change(p=>GraphSizes()?p with{GraphTitleSize=n}:p with{TitleSize=n}),DefaultTitleSize());
        var valueSize=Size(cellBody,"Value size",ValueSize(),8,80,n=>Change(p=>GraphSizes()?p with{GraphValueSize=n}:p with{ValueSize=n}),DefaultValueSize());
        if(kind==MetricKind.HardwareSensor){var detailSize=Size(cellBody,"Device text size",current.DetailTextSize,8,80,n=>Change(p=>p with{DetailTextSize=n}),defaults.DetailTextSize);detailSize.Name="SensorDetailTextSize";}
        Slider? intervalSize=null;
        refreshTextSizes=()=>
        {
            updatingTextSizes=true;
            try
            {
                titleSize.Value=TitleSize();valueSize.Value=ValueSize();
                SliderDefaults.Mark(titleSize,DefaultTitleSize(),"Base setting");SliderDefaults.Mark(valueSize,DefaultValueSize(),"Base setting");
                if(intervalSize!=null){intervalSize.Value=GraphIntervalFontSize(current,topBar);SliderDefaults.Mark(intervalSize,GraphIntervalFontSize(defaults,topBar),"Base setting");}
            }
            finally{updatingTextSizes=false;}
        };
        var graphFields=new StackPanel();cellBody.Children.Add(graphFields);
        Label(graphFields,"Graph duration");var minutes=new ComboBox{Name="SensorCellGraphDuration",ItemsSource=GraphDurationMinutes,SelectedItem=current.Minutes};graphFields.Children.Add(minutes);
        minutes.SelectionChanged+=(_,_)=>Change(p=>p with{Minutes=(int)minutes.SelectedItem,VisibleSeconds=0});
        intervalSize=Size(graphFields,"Interval text size",GraphIntervalFontSize(current,topBar),8,80,n=>Change(p=>p with{GraphIntervalSize=n}),GraphIntervalFontSize(defaults,topBar));
        intervalSize.Tag="GraphIntervalSize";
        var resetFields=new StackPanel();cellBody.Children.Add(resetFields);
        var resetSize=Size(resetFields,"Reset text size",current.ResetTextSize,8,80,n=>Change(p=>p with{ResetTextSize=n}),defaults.ResetTextSize);resetSize.Tag="ResetTextSize";

        var networkColors=new StackPanel{Name="NetworkColors",Visibility=NormalizeMetric(kind)==MetricKind.Network?Visibility.Visible:Visibility.Collapsed};
        var directionColors=SettingsEditorTabs.Create("NetworkColorTabs");networkColors.Children.Add(directionColors);colorsBody.Children.Add(networkColors);
        foreach(bool upload in new[]{false,true})
        {
            var tabBody=new StackPanel();Label(tabBody,"Value / data line color");
            tabBody.Children.Add(ColorPicker(upload?current.NetworkUpColor:current.NetworkDownColor,c=>Change(p=>upload?p with{NetworkUpColor=c}:p with{NetworkDownColor=c})));
            tabBody.Children.Add(new TextBlock{Text="Default uses the shared sensor color.",FontSize=12,Margin=new Thickness(0,6,0,0)});
            directionColors.Items.Add(new TabItem{Name=upload?"UPColorTab":"DLColorTab",Header=upload?"UP":"DL",Content=tabBody});
        }
        void ColorChoice(Panel panel,string title,string? initial,Action<string?> set){Label(panel,title);panel.Children.Add(ColorPicker(initial,set));}
        var standardColor=new StackPanel{Name="SensorCellValueColor",Visibility=NormalizeMetric(kind)==MetricKind.Network?Visibility.Collapsed:Visibility.Visible};
        ColorChoice(standardColor,"Value / data line color",current.Color,c=>Change(p=>p with{Color=c}));colorsBody.Children.Add(standardColor);
        if(kind==MetricKind.GpuStatus)ColorChoice(colorsBody,"Clock color",current.GpuClockColor,c=>Change(p=>p with{GpuClockColor=c}));
        if(kind==MetricKind.DiskStatus){
            ColorChoice(colorsBody,"Read color",current.DiskReadColor,c=>Change(p=>p with{DiskReadColor=c}));
            ColorChoice(colorsBody,"Write color",current.DiskWriteColor,c=>Change(p=>p with{DiskWriteColor=c}));
            ColorChoice(colorsBody,"Temperature color",current.DiskTemperatureColor,c=>Change(p=>p with{DiskTemperatureColor=c}));
        }
        if(kind==MetricKind.CpuStatus){
            ColorChoice(colorsBody,"Load value color",current.CpuLoadColor,c=>Change(p=>p with{CpuLoadColor=c}));
            ColorChoice(colorsBody,"Temperature value color",current.CpuTemperatureColor,c=>Change(p=>p with{CpuTemperatureColor=c}));
            ColorChoice(colorsBody,"Current usage value color",current.CpuPowerColor,c=>Change(p=>p with{CpuPowerColor=c}));
        }
        if(kind==MetricKind.GpuStatus)ColorChoice(colorsBody,"Current usage value color",current.GpuPowerColor,c=>Change(p=>p with{GpuPowerColor=c}));
        ColorChoice(colorsBody,"Title color",current.TitleColor,c=>Change(p=>p with{TitleColor=c}));
        var guideFields=new StackPanel();colorsBody.Children.Add(guideFields);
        ColorChoice(guideFields,"Guide color",current.GuideColor,c=>Change(p=>p with{GuideColor=c}));
        Size(guideFields,"Guide opacity (%)",current.GuideOpacity*100,0,100,n=>Change(p=>p with{GuideOpacity=n/100}));

        refreshVisibility=()=>{foreach(TabItem tab in directionColors.Items)tab.Visibility=(tab.Name=="UPColorTab"?current.NetworkUp==true:current.NetworkDown==true)?Visibility.Visible:Visibility.Collapsed;
            if(directionColors.SelectedItem is not TabItem selectedColor||selectedColor.Visibility!=Visibility.Visible)directionColors.SelectedItem=directionColors.Items.Cast<TabItem>().FirstOrDefault(t=>t.Visibility==Visibility.Visible);
            bool graph=GraphSizes();graphFields.Visibility=guideFields.Visibility=graph?Visibility.Visible:Visibility.Collapsed;resetFields.Visibility=!graph&&NormalizeMetric(kind)==MetricKind.CodexFiveHour?Visibility.Visible:Visibility.Collapsed;};refreshVisibility();
        if(IsLoaded){SettingsEditorTabs.RequestFit(mainTabs);RequestContentFit();}
    }
    internal static FrameworkElement ColorPicker(string? initial,Action<string?> set)=>ColorSwatchRow.Create(initial,set,bright:true);
}
