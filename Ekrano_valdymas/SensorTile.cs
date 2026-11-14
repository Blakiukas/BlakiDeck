using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using static Ekrano_valdymas.SystemMonitorWidget;
namespace Ekrano_valdymas;

// Typography is arranged independently of the plot; it is never stretched with it.
public sealed class SensorTile : Grid
{
    public static readonly DependencyProperty SlotProperty=DependencyProperty.Register(nameof(Slot),typeof(MonitorSlot),typeof(SensorTile),new PropertyMetadata(null,(d,e)=>((SensorTile)d).BindSlot((MonitorSlot?)e.OldValue)));
    public MonitorSlot? Slot {get=>(MonitorSlot?)GetValue(SlotProperty);set=>SetValue(SlotProperty,value);}
    public static readonly DependencyProperty TargetProperty=DependencyProperty.Register(nameof(Target),typeof(string),typeof(SensorTile),new PropertyMetadata(null,(d,e)=>((SensorTile)d).BindTarget()));
    public string? Target {get=>(string?)GetValue(TargetProperty);set=>SetValue(TargetProperty,value);}
    private SystemMonitorWidget? service;
    private Window? activityWindow;
    private readonly List<UIElement> opacitySources=new();
    private static readonly DependencyPropertyDescriptor opacityProperty=DependencyPropertyDescriptor.FromProperty(UIElement.OpacityProperty,typeof(UIElement));
    internal ContextMenu? OptionsMenu {get;private set;}
    private MonitorSlot? external;
    private MonitorSlot? observedSlot;
    private MonitorSlot? Model=>Slot??external;
    private readonly OpticallyCenteredText label=Text(12,FontWeights.Bold),value=Text(20,FontWeights.Bold),duration=Text(10,FontWeights.Normal);
    private readonly OpticallyCenteredText[] guides=Enumerable.Range(0,5).Select(_=>Text(10,FontWeights.Normal)).ToArray();
    private readonly Grid plotArea=new();
    private readonly SensorHeaderGrid heading=new();
    private readonly OpticallyCenteredText hardwareModel=Text(12,FontWeights.Normal);
    private readonly NetworkGraphHeader networkHeading=new();
    private readonly NetworkValueRow networkNumeric=new(){Visibility=Visibility.Collapsed};
    private readonly OpticallyCenteredText networkUpValue=Text(20,FontWeights.Bold);
    private readonly StackPanel info=new();
    private readonly GpuStatusView gpuStatus=new(){Visibility=Visibility.Collapsed};
    private readonly OpticallyCenteredText details=Text(10,FontWeights.Normal);
    private readonly OpticallyCenteredText reset=Text(10,FontWeights.Normal);
    private readonly ProgressBar remaining=new(){Minimum=0,Maximum=100,Height=4,Margin=new Thickness(2,3,2,3)};
    private SystemMonitorWidget? MonitorOwner {get {for(DependencyObject? node=this;node!=null;node=VisualTreeHelper.GetParent(node))if(node is SystemMonitorWidget monitor)return monitor;return null;}}
    private string Location {get { if(Slot!=null)return MonitorOwner?.CellLocation(Slot)??"monitor:-1"; for(DependencyObject? n=this;n!=null;n=VisualTreeHelper.GetParent(n)){if(n is ShortcutsWidget w&&DataContext is ShortcutsWidget.Slot slot)return w.SensorLocation(slot);if(n is QuickLaunchBar&&DataContext is QuickLaunchBar.QuickLaunchSlot top)return "top:"+top.Index;}return "detached"; }}
    private MetricPresentation RawAppearance=>service==null||Model==null?new():NormalizeNetworkPresentation(Model.Kind,SensorCellPreferences.Get(Location,service.GetPresentation(Model.Kind)));
    private readonly AdaptiveSensorColors adaptiveColors=new();
    private MetricPresentation Appearance
    {
        get
        {
            var p=RawAppearance;if(Model is not {} model)return p;
            if(NormalizeMetric(model.Kind)==MetricKind.Network)p=EffectiveNetworkPresentation(p,model.LargeGraph);
            if(service!=null&&!IsStatus(model.Kind))p=adaptiveColors.Apply(service,model.Kind,p);
            return SupportsGraph(model.Kind)&&(model.LargeGraph||p.Mode=="Graph")?p with{TitleSize=p.GraphTitleSize??p.TitleSize,ValueSize=p.GraphValueSize??p.ValueSize}:p;
        }
    }
    private void ApplyCell(MetricPresentation p){SensorCellPreferences.Set(Location,p);Refresh();}
    private bool InTopBar {get {for(DependencyObject? node=this;node!=null;node=VisualTreeHelper.GetParent(node))if(node is QuickLaunchBar)return true;return false;}}
    private MetricPresentation DefaultReference(MetricKind kind)=>InTopBar?SensorFactoryDefaults.ForMetric(kind) with{TitleSize=14,ValueSize=24,GraphIntervalSize=8}:SensorFactoryDefaults.Reference(Location,kind,service?.GetPresentation(kind)??new(GraphTitleSize:20,GraphValueSize:20));
    internal SensorCellEditor CreateEditor()=>new(Window.GetWindow(this),Model!.Kind,RawAppearance,k=>{if(external!=null)SelectExternal(k);else {Model!.SetContent("sensor:"+k);var owner=MonitorOwner??service;owner!.FitNewStatusCell(owner.Slots.IndexOf(Model),k);owner.SaveCurrentLayout();}Refresh();},ApplyCell,Model!.LargeGraph,defaults:DefaultReference(Model!.Kind),baseSettings:DefaultReference,topBar:InTopBar){EditLayout=MonitorOwner is {} monitor?()=>monitor.CreateLayoutEditor().ShowDialog():null};
    private readonly ContentControl embeddedContent=new(){IsHitTestVisible=false};
    private string? embeddedTarget;
    private readonly HistoryPlot plot=new();
    private readonly CoreLoadPlot cores=new(){Margin=new Thickness(0,3,0,0)};
    private readonly Image shortcutIcon=new(){Stretch=Stretch.Uniform,Margin=new Thickness(8)};
    private double seconds=120;
    private int preset=2;
    private double savedZoom;
    private sealed record LayoutState(MonitorSlot Model,MetricKind Kind,bool Active,bool Shortcut,bool LargeGraph,MetricPresentation Appearance,bool List,bool TopBar,double Width,double Height,double Dpi,Brush TitleBrush);
    private LayoutState? appliedLayout;
    private double networkNumericSize=double.NaN;
    internal long RefreshCountForProbe{get;private set;}
    internal long LayoutApplyCountForProbe{get;private set;}
    internal long ReparentCountForProbe{get;private set;}
    private static OpticallyCenteredText Text(double size,FontWeight weight){var text=new OpticallyCenteredText{TextFontFamily=new FontFamily("Segoe UI"),TextFontSize=size,TextFontWeight=weight,Ellipsis=true};text.SetResourceReference(OpticallyCenteredText.TextBrushProperty,"ModuleTitleBrush");return text;}
    public SensorTile()
    {
        heading.Title=label;heading.Value=value;heading.Duration=duration;
        heading.Hardware=hardwareModel;hardwareModel.Tag="HardwareModel";hardwareModel.Visibility=Visibility.Collapsed;heading.Children.Add(hardwareModel);
        duration.Tag="GraphInterval";duration.InkAlignment=HorizontalAlignment.Right;
        networkUpValue.Tag="NetworkUpValue";networkHeading.Visibility=Visibility.Collapsed;Children.Add(networkHeading);
        Children.Add(networkNumeric);SetRow(networkNumeric,1);
        Margin=new Thickness(5,2,5,3);ClipToBounds=true;Background=Brushes.Transparent;value.Tag="SensorValue";
        RowDefinitions.Add(new(){Height=new GridLength(20)});RowDefinitions.Add(new(){Height=new GridLength(26)});RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});RowDefinitions.Add(new(){Height=new GridLength(14)});
        heading.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});heading.ColumnDefinitions.Add(new(){Width=new GridLength(6)});heading.ColumnDefinitions.Add(new(){Width=GridLength.Auto});heading.ColumnDefinitions.Add(new(){Width=GridLength.Auto});heading.Children.Add(label);heading.Children.Add(duration);SetColumn(duration,3);duration.MinWidth=44;Children.Add(heading);Children.Add(value);SetRow(value,1);
        plotArea.ColumnDefinitions.Add(new(){Width=new GridLength(40)});plotArea.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        heading.HardwareInset=plotArea.ColumnDefinitions[0].Width.Value;
        plotArea.Children.Add(plot);SetColumn(plot,1);plotArea.Children.Add(cores);SetColumnSpan(cores,2);cores.Visibility=Visibility.Collapsed;
        var axis=new Grid();for(int i=0;i<5;i++)axis.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        for(int i=0;i<5;i++){axis.Children.Add(guides[i]);SetRow(guides[i],i);}
        plotArea.Children.Add(axis);Children.Add(plotArea);SetRow(plotArea,2);
        
        Children.Add(shortcutIcon);SetRow(shortcutIcon,1);SetRowSpan(shortcutIcon,3);shortcutIcon.Visibility=Visibility.Collapsed;
        Children.Add(details);SetRow(details,3);details.Visibility=Visibility.Collapsed;details.FitToBox=true;
        info.VerticalAlignment=VerticalAlignment.Bottom;info.Margin=new Thickness(2,0,2,5);remaining.Margin=new Thickness(2,3,2,5);reset.Height=18;info.Children.Add(remaining);info.Children.Add(reset);Children.Add(info);SetRow(info,2);SetRowSpan(info,1);info.Visibility=Visibility.Collapsed;
        Children.Add(gpuStatus);SetRowSpan(gpuStatus,4);Children.Add(embeddedContent);SetRowSpan(embeddedContent,4);embeddedContent.Visibility=Visibility.Collapsed;
        Loaded+=(_,_)=>{ObserveSlot();SensorCellPreferences.Changed+=Refresh;Connect();};Unloaded+=(_,_)=>{ClearSlotSubscription();SensorCellPreferences.Changed-=Refresh;ClearActivitySources();Disconnect();};SizeChanged+=(_,_)=>Refresh();IsVisibleChanged+=(_,_)=>{if(IsVisible)Connect();else Disconnect();};
        PreviewMouseWheel+=(_,e)=>{if(!(Model?.LargeGraph==true||Appearance.Mode=="Graph")||plotArea.Visibility!=Visibility.Visible)return;seconds=WheelInterval(seconds,e.Delta);SaveZoom();Refresh();e.Handled=true;};
        PreviewMouseLeftButtonDown+=(_,e)=>{if(e.ClickCount==2&&(Model?.LargeGraph==true||Appearance.Mode=="Graph")){ResetZoom();e.Handled=true;}};
        MouseRightButtonUp+=(_,e)=>{e.Handled=OpenOptions();};
    }
    internal static SensorTile? FindIn(DependencyObject node){if(node is SensorTile tile)return tile;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)if(FindIn(VisualTreeHelper.GetChild(node,i)) is {} child)return child;return null;}
    internal bool OpenOptions(){Refresh();if(OpenEmbeddedOptions())return true;if(Model is not {} model||service==null)return false;OptionsMenu?.SetCurrentValue(ContextMenu.IsOpenProperty,false);var menu=(MonitorOwner??service).PresentationMenu(model,external==null?null:SelectExternal,ApplyCell,RawAppearance,external==null?null:()=>ChangeExternal(null,null));
        if(!model.IsShortcut){var edit=new MenuItem{Header="Edit"};edit.Click+=(_,_)=>CreateEditor().ShowDialog();menu.Items.Insert(1,edit);}
        if(!model.IsShortcut&&MonitorOwner is {} layoutOwner){var layout=new MenuItem{Header="Layout…",Name="OpenMonitorLayout"};layout.Click+=(_,_)=>{menu.IsOpen=false;Dispatcher.BeginInvoke(new Action(()=>layoutOwner.CreateLayoutEditor().ShowDialog()));};menu.Items.Insert(2,layout);}
        OptionsMenu=menu;menu.PlacementTarget=this;menu.IsOpen=true;return true;}
    private bool OpenEmbeddedOptions()
    {
        if(Model is not {} model||MonitorOwner is not {} monitor||model.Shortcut is not {} shortcut||!(UsageMini.IsTarget(shortcut.Target)||shortcut.Target==MacroLibraryCard.ClockTarget))return false;
        var menu=ListAppearanceUi.Create(this);var edit=new MenuItem{Header="Edit"};menu.Items.Add(edit);
        edit.Click+=(_,_)=>
        {
            if(UsageMini.IsTarget(shortcut.Target))UsageMiniEditor.Create(Window.GetWindow(this),UsageMini.Decode(shortcut.Target),p=>{model.SetContent(UsageMini.Encode(p));monitor.SaveCurrentLayout();Refresh();}).ShowDialog();
            else ClockEditor.Create(Window.GetWindow(this),MonitorClockContent.Decode(shortcut.Presentation),_=>{},p=>{model.SetContent(MacroLibraryCard.ClockTarget,"Clock",System.Text.Json.JsonSerializer.Serialize(p));monitor.SaveCurrentLayout();Refresh();}).ShowDialog();
        };
        var delete=new MenuItem{Header="Delete cell"};delete.Click+=(_,_)=>monitor.DeleteMonitorCell(model);menu.Items.Add(delete);
        OptionsMenu=menu;menu.PlacementTarget=this;menu.IsOpen=true;return true;
    }
    internal void EditCell()
    {
        Refresh();
        if(Model is not {} model)return;
        var owner=Window.GetWindow(this);var monitor=MonitorOwner;
        if(model.Shortcut is {} shortcut)
        {
            if(UsageMini.IsTarget(shortcut.Target)&&monitor!=null)
                UsageMiniEditor.Create(owner,UsageMini.Decode(shortcut.Target),p=>{model.SetContent(UsageMini.Encode(p));monitor.SaveCurrentLayout();Refresh();}).ShowDialog();
            else if(shortcut.Target==MacroLibraryCard.ClockTarget&&monitor!=null)
                ClockEditor.Create(owner,MonitorClockContent.Decode(shortcut.Presentation),_=>{},p=>{model.SetContent(MacroLibraryCard.ClockTarget,"Clock",System.Text.Json.JsonSerializer.Serialize(p));monitor.SaveCurrentLayout();Refresh();}).ShowDialog();
            else if(MacroLibrary.Current.Find(shortcut.Target??"") is {} macro)MacroEditor.Show(owner,macro);
            else if(shortcut.IsEmpty)CreateEditor().ShowDialog();
            else ShortcutAppearanceEditor.Create(owner,shortcut.Name,shortcut.Target!,shortcut.Presentation??"IconText",(name,target,appearance,icon)=>{model.SetContent(target,name,appearance,icon);monitor?.SaveCurrentLayout();Refresh();},iconPath:shortcut.IconPath).ShowDialog();
        }
        else if(service!=null)CreateEditor().ShowDialog();
    }
    internal void ResetZoom(){seconds=preset*60;SaveZoom();Refresh();}
    private void SaveZoom(){if(service!=null&&Model is {} m)ApplyCell(RawAppearance with{VisibleSeconds=seconds});}
    private void SelectExternal(MetricKind kind)=>ChangeExternal("sensor:"+kind,GetMetricLabel(kind));
    private void ChangeExternal(string? target,string? name)
    {
        for(DependencyObject? node=this;node!=null;node=VisualTreeHelper.GetParent(node))
            if(node is FrameworkElement {DataContext:ShortcutsWidget.Slot slot}){slot.SetTarget(target,name);break;}
            else if(node is FrameworkElement {DataContext:QuickLaunchBar.QuickLaunchSlot top}){top.SetTarget(target,name);break;}
        for(DependencyObject? node=this;node!=null;node=VisualTreeHelper.GetParent(node))
            if(node is ShortcutsWidget owner){owner.SaveTouchShortcuts();break;}
            else if(node is QuickLaunchBar bar){bar.SaveTouchShortcuts();break;}
    }
    private void ClearSlotSubscription(){if(observedSlot!=null)observedSlot.PropertyChanged-=Changed;observedSlot=null;}
    private void ObserveSlot()
    {
        var next=IsLoaded?Slot:null;
        if(ReferenceEquals(next,observedSlot))return;
        ClearSlotSubscription();observedSlot=next;
        if(observedSlot!=null)observedSlot.PropertyChanged+=Changed;
    }
    private void BindSlot(MonitorSlot? old){ObserveSlot();Connect();}
    private void BindTarget(){external=Target?.StartsWith("sensor:")==true&&Enum.TryParse<MetricKind>(Target[7..],out var kind)?new(kind):null;Connect();}
    private void Changed(object? sender,PropertyChangedEventArgs e){if(e.PropertyName is nameof(MonitorSlot.Kind) or nameof(MonitorSlot.Presentation) or nameof(MonitorSlot.IsShortcut) or nameof(MonitorSlot.IsActive) or nameof(MonitorSlot.LargeGraph))Connect();}
    private void RefreshCores()
    {
        if(!NeedsCoreUpdates||service==null||Model is not {} model)return;
        // The 100 ms lane changes only the displayed signal, never rebuilds the
        // text/graph layout or reads every other history series.
        if(external!=null||MonitorOwner!=service)model.Update(service.Latest.GetValueOrDefault(MetricKind.CpuCoreLoad),service.TotalRam,MonitorOwner?.ColorIntensity??45,service.LatestCoreLoads);
        value.Text=model.DisplayValue;value.TextBrush=BrushFor(Appearance.Color,model.ValueBrush);
        if(cores.IsVisible)cores.Set(model.CoreBars.Select(b=>b.Height).ToArray(),value.TextBrush);
    }
    protected override Size ArrangeOverride(Size size)
    {
        var result=base.ArrangeOverride(size);
        if(cores.Visibility==Visibility.Visible&&value.Parent==heading){
            bool expand=Appearance.HideTitle&&Appearance.HideCorePercentage;
            var references=new[]{label,value}.Where(t=>t.Visibility==Visibility.Visible||(expand&&ReferenceEquals(t,value)))
                .Select(t=>t.TransformToAncestor(this).TransformBounds(t.GetCenteringReferenceBounds())).Where(r=>!r.IsEmpty).ToArray();
            if(references.Length>0){
                double gap=Math.Max(0,Margin.Top+references.Min(r=>r.Top));
                double headerBottom=heading.TranslatePoint(new Point(0,heading.ActualHeight),this).Y;
                double top=expand?references.Min(r=>r.Top)-plotArea.TranslatePoint(new Point(),this).Y:references.Max(r=>r.Bottom)+gap-headerBottom;
                var spacing=new Thickness(0,Math.Max(0,top),0,Math.Max(0,gap-Margin.Bottom));
                if(cores.Margin!=spacing)cores.Margin=spacing;
            }
        }
        return result;
    }
    internal bool NeedsCoreUpdates=>Model is {IsActive:true,IsShortcut:false,Kind:MetricKind.CpuCoreLoad}&&Presented();
    private void ClearActivitySources()
    {
        foreach(var source in opacitySources)opacityProperty.RemoveValueChanged(source,ActivityChanged);
        opacitySources.Clear();
        if(activityWindow!=null)activityWindow.StateChanged-=ActivityChanged;
        activityWindow=null;
    }
    private void ObserveActivitySources()
    {
        if(!IsLoaded||Model==null){ClearActivitySources();return;}
        // A retained dashboard can be loaded behind the player at Opacity=0.
        // WPF IsVisible does not change when that host becomes opaque again.
        if(opacitySources.Count==0)
            for(DependencyObject? node=this;node!=null;node=VisualTreeHelper.GetParent(node))
                if(node is UIElement element){opacitySources.Add(element);opacityProperty.AddValueChanged(element,ActivityChanged);}
        var window=Window.GetWindow(this);
        if(window!=activityWindow)
        {
            if(activityWindow!=null)activityWindow.StateChanged-=ActivityChanged;
            activityWindow=window;
            if(activityWindow!=null)activityWindow.StateChanged+=ActivityChanged;
        }
    }
    private void ActivityChanged(object? sender,EventArgs e)
    {
        if(!Presented())Disconnect();
        else if(service==null)Connect();
    }
    private void Disconnect(){if(service!=null){service.ReadingsChanged-=Refresh;service.CoreReadingsChanged-=RefreshCores;service.TrackPresentation(this,false);}service=null;}
    private bool Presented()
    {
        if(!IsLoaded||!IsVisible)return false;
        for(DependencyObject? node=this;node!=null;node=VisualTreeHelper.GetParent(node))if(node is UIElement ui&&ui.Opacity==0)return false;
        return Window.GetWindow(this)?.WindowState!=WindowState.Minimized;
    }
    private void Connect()
    {
        ObserveActivitySources();
        if(!Presented()||Model==null){Disconnect();return;}
        var next=Shared??(Window.GetWindow(this) as MainWindow)?.EnsureSystemMonitor();
        if(next!=service){Disconnect();service=next;if(service!=null){service.ReadingsChanged+=Refresh;service.CoreReadingsChanged+=RefreshCores;}}
        service?.TrackPresentation(this,true);
        if(Model.IsShortcut){Refresh();return;}
        service?.ActivateMetric(Model.Kind);Refresh();
    }
    private object? tooltipBeforeAccessProblem;
    private bool showingAccessProblem;
    private void Refresh()
    {
        if(!Presented()||Model is not {} model||service==null)return;
        if(showingAccessProblem){ToolTip=tooltipBeforeAccessProblem;showingAccessProblem=false;}
        string? target=model.Shortcut?.Target;
        bool isEmbedded=UsageMini.IsTarget(target)||target==MacroLibraryCard.ClockTarget;
        if(isEmbedded)
        {
            if(embeddedTarget!=target+model.Shortcut?.Presentation){TooltipPolicy.InvalidateOwner(this);ToolTip=UsageMini.IsTarget(target)?UsageMini.Description(target):"Clock";embeddedTarget=target+model.Shortcut?.Presentation;embeddedContent.Content=UsageMini.IsTarget(target)?new UsageMini{Target=target}:new MonitorClockContent(model.Shortcut?.Presentation);}
            foreach(UIElement child in Children)child.Visibility=ReferenceEquals(child,embeddedContent)?Visibility.Visible:Visibility.Collapsed;
            return;
        }
        if(embeddedTarget!=null){embeddedTarget=null;embeddedContent.Content=null;appliedLayout=null;heading.Visibility=value.Visibility=Visibility.Visible;}
        embeddedContent.Visibility=Visibility.Collapsed;
        RefreshCountForProbe++;
        var appearance=Appearance;
        bool list=false,topBar=false;for(DependencyObject? node=this;node!=null;node=VisualTreeHelper.GetParent(node)){if(node is ShortcutsWidget owner){list=owner.ListMode;break;}if(node is QuickLaunchBar){topBar=true;break;}}
        var titleBrush=(Brush)Application.Current.FindResource("ModuleTitleBrush");
        var nextLayout=new LayoutState(model,model.Kind,model.IsActive,model.IsShortcut,model.LargeGraph,appearance,list,topBar,ActualWidth,ActualHeight,VisualTreeHelper.GetDpi(this).PixelsPerDip,titleBrush);
        bool layoutChanged=appliedLayout!=nextLayout;
        if(layoutChanged)
        {
            TooltipPolicy.InvalidateOwner(this);
            ToolTip=null;details.ToolTip=null;plot.ToolTip=null;reset.ToolTip=null;
            appliedLayout=nextLayout;LayoutApplyCountForProbe++;
            RestoreStandardHeader();networkNumericSize=double.NaN;SetRow(plotArea,2);SetRowSpan(plotArea,1);
            info.Visibility=Visibility.Collapsed;details.Visibility=Visibility.Collapsed;
            hardwareModel.Visibility=Visibility.Collapsed;
            RowDefinitions[0].Height=new GridLength(20);RowDefinitions[1].Height=new GridLength(26);RowDefinitions[2].Height=new GridLength(1,GridUnitType.Star);RowDefinitions[3].Height=new GridLength(0);
        }
        if(model.IsShortcut||!model.IsActive){PlaceValue(false);heading.Graph=false;SetColumnSpan(label,4);label.Visibility=Visibility.Visible;label.InkVerticalAlignment=VerticalAlignment.Center;value.FitToBox=true;}
        gpuStatus.Visibility=Visibility.Collapsed;heading.Visibility=Visibility.Visible;
        if(model.IsActive&&!model.IsShortcut&&IsStatus(model.Kind)){
            heading.Visibility=value.Visibility=plotArea.Visibility=duration.Visibility=shortcutIcon.Visibility=details.Visibility=info.Visibility=Visibility.Collapsed;
            gpuStatus.Visibility=Visibility.Visible;gpuStatus.RefreshStatus(service,appearance,model.Kind);ToolTip=model.Kind==MetricKind.GpuStatus?service.GpuDetails:model.Kind==MetricKind.DiskStatus?service.SelectedDisk(appearance.DiskId)?.Description:null;return;
        }
        if(!model.IsActive||model.Shortcut?.IsEmpty==true){label.Text="";value.Text="+";value.TextBrush=(Brush)Application.Current.FindResource("ModuleTitleBrush");value.TextFontSize=24;value.Visibility=Visibility.Visible;SetRow(value,0);SetRowSpan(value,4);plotArea.Visibility=duration.Visibility=shortcutIcon.Visibility=Visibility.Collapsed;return;}
        if(model.IsShortcut)
        {
            var shortcut=model.Shortcut!;
            bool titleVisible=shortcut.ShowIcon&&shortcut.ShowName;
            RowDefinitions[0].Height=new GridLength(titleVisible?26:0);RowDefinitions[1].Height=new GridLength(0);RowDefinitions[2].Height=new GridLength(1,GridUnitType.Star);RowDefinitions[3].Height=new GridLength(0);
            heading.Visibility=titleVisible?Visibility.Visible:Visibility.Collapsed;
            SetRow(shortcutIcon,titleVisible?1:0);SetRowSpan(shortcutIcon,titleVisible?3:4);
            SetRow(value,0);SetRowSpan(value,4);
            label.Text=!shortcut.IsEmpty&&shortcut.ShowIcon&&shortcut.ShowName?model.Label:"";plotArea.Visibility=duration.Visibility=Visibility.Collapsed;
            shortcutIcon.Visibility=shortcut.ShowIcon?Visibility.Visible:Visibility.Collapsed;shortcutIcon.Source=shortcut.Icon;
            value.Visibility=!shortcut.ShowIcon||shortcut.IsEmpty?Visibility.Visible:Visibility.Collapsed;
            value.Text=shortcut.IsEmpty?"+":shortcut.Name;value.TextFontSize=shortcut.MacroTypography?.SafeSize??24;value.TextBrush=Brushes.White;return;
        }
        if(layoutChanged){shortcutIcon.Visibility=Visibility.Collapsed;value.Visibility=Visibility.Visible;label.Visibility=Visibility.Visible;}
        if(preset!=appearance.Minutes||savedZoom!=appearance.VisibleSeconds){preset=appearance.Minutes;savedZoom=appearance.VisibleSeconds;seconds=Math.Clamp(appearance.VisibleSeconds>0?appearance.VisibleSeconds:preset*60d,15,MonitorHistory.MaximumSeconds);}
        if(layoutChanged)label.TextFontSize=ActualWidth>=160?14:12;
        label.Text=model.Kind==MetricKind.DiskRead?DiskReadLabel(appearance.DiskId):GetMetricLabel(model.Kind);
        // External tiles use the same latest reading, presentation and history as monitor tiles.
        if(external!=null||MonitorOwner!=service){if(layoutChanged)model.SetPresentation(appearance);model.Update(service.Latest.GetValueOrDefault(model.Kind),service.TotalRam,MonitorOwner?.ColorIntensity??45,service.LatestCoreLoads);}
        bool network=NormalizeMetric(model.Kind)==MetricKind.Network;
        bool graph=SupportsGraph(model.Kind)&&(model.LargeGraph||appearance.Mode=="Graph"),dualNumeric=network&&!graph&&appearance.NetworkDown==true&&appearance.NetworkUp==true;
        hardwareModel.Visibility=graph&&appearance.ShowHardwareInfo&&model.Kind is (MetricKind.CpuLoad or MetricKind.GpuLoad)?Visibility.Visible:Visibility.Collapsed;
        if(hardwareModel.Visibility==Visibility.Visible){hardwareModel.Text=HardwareModelName.Short(model.Kind==MetricKind.GpuLoad);hardwareModel.ToolTip=HardwareModelName.Full(model.Kind==MetricKind.GpuLoad);hardwareModel.TextBrush=titleBrush;}
        bool splitNetwork=network&&appearance.NetworkDown==true&&appearance.NetworkUp==true&&(list||!graph||model.LargeGraph);
        bool codexMetric=model.Kind is MetricKind.CodexFiveHour or MetricKind.CodexWeekly;
        string nextValue=appearance.Mode=="Percent"&&model.Kind==MetricKind.MemoryUsed&&service.TotalRam>0&&service.Latest.GetValueOrDefault(model.Kind) is {} ram?$"{ram/service.TotalRam*100:0}%":model.DisplayValue;
        if(model.Kind is (MetricKind.VramUsed or MetricKind.MemoryUsed)&&appearance.Mode!="Percent"&&service.Latest.GetValueOrDefault(model.Kind) is {} memory)
        {
            double multiplier=1073741824d/(appearance.MemoryUnit=="GB"?1073741824d:1048576d);string format=appearance.MemoryUnit=="GB"?(appearance.OneDecimal?"0.0":"0.##"):"0";
            nextValue=(memory*multiplier).ToString(format)+(model.Kind==MetricKind.MemoryUsed?"/"+(service.TotalRam*multiplier).ToString(format):"")+" "+(appearance.MemoryUnit=="GB"?"GB":"MB");
        }
        value.TextBrush=model.DisplayValue is "N/A" or "--"?titleBrush:model.ValueBrush;
        if(network){nextValue=NetworkDisplay(service.Latest,splitNetwork?appearance with{NetworkUp=false}:appearance,false);value.TextBrush=BrushFor(NetworkDataColor(appearance,appearance.NetworkUp==true&&appearance.NetworkDown!=true),value.TextBrush);}
        if(codexMetric){var usage=service.UsageSnapshot;var windows=usage?.Windows??[];nextValue=windows.Length==0?"N/A":string.Join(graph||list?" · ":"\n",windows.Select(w=>$"{w.Label} {(w.Remaining.HasValue?$"{w.Remaining:0}%":"N/A")}"));if((graph||list)&&usage!=null&&DateTime.Now-usage.RetrievedAt>=TimeSpan.FromMinutes(2))nextValue+=" · STALE";}

        if(model.Kind==MetricKind.HardwareSensor){var raw=HardwareTelemetry.Find(appearance.HardwareSensorId);if(raw!=null&&appearance.HardwareSensorLabel!=null&&raw.Label!=appearance.HardwareSensorLabel)raw=null;nextValue=raw?.DisplayIn(appearance.MemoryUnit,appearance.OneDecimal)??"N/A";label.Text=raw?.Name??appearance.HardwareSensorLabel??"CHOOSE SENSOR";ToolTip=raw==null?"Selected device/sensor unavailable. No substitute used.":raw.Description;}
        if(IsDisk(model.Kind)){nextValue=service.DiskDisplay(model.Kind,appearance.DiskId);label.Text=GetMetricLabel(model.Kind);ToolTip=(IsDiskRate(model.Kind)&&appearance.DiskId==null?null:service.SelectedDisk(appearance.DiskId))?.Description??"Physical disk activity · MB/s (1024-based)";}
        if(model.Kind is MetricKind.CpuTemperature or MetricKind.CpuPower or MetricKind.CpuVoltage or MetricKind.CpuFan or MetricKind.CpuPump or MetricKind.GpuFan or MetricKind.GpuPump or MetricKind.GpuVoltage)ToolTip=HardwareTelemetry.Describe(model.Kind);
        if(model.Kind is MetricKind.MemoryUsed or MetricKind.MemoryLoad)ToolTip="Physical RAM: used / total available to Windows (hardware-reserved memory excluded). Windows-style units: 1 MB = 1,048,576 bytes; 1 GB = 1,073,741,824 bytes. The total can differ from the nominal capacity printed on RAM modules. This is not virtual/committed memory.";
        if(model.Kind==MetricKind.CpuEffectiveClock)ToolTip="Average effective CPU core frequency over the sampling interval, including idle time. Not maximum boost or advertised frequency. N/A if no unique supported whole-CPU source is available.";
        if(model.Kind==MetricKind.GpuHotspot)ToolTip="Hotspot requires a dedicated sensor from the selected GPU. N/A means it is not exposed; core and memory temperatures are not substitutes.";
        if(model.Kind is >=MetricKind.GpuPowerLimit and <=MetricKind.GpuLimitReason){nextValue=service.GpuDisplay(model.Kind);ToolTip=service.GpuDetails;}
        if(nextValue=="N/A"&&service.SensorAccessProblem is {} accessProblem&&
            model.Kind is (MetricKind.HardwareSensor or MetricKind.CpuTemperature or MetricKind.CpuPower or MetricKind.CpuVoltage or MetricKind.CpuFan or MetricKind.CpuPump or MetricKind.GpuTemperature or MetricKind.GpuHotspot or MetricKind.GpuPower or MetricKind.GpuVoltage or MetricKind.GpuFan or MetricKind.GpuPump))
        { tooltipBeforeAccessProblem=ToolTip;ToolTip=accessProblem;showingAccessProblem=true; }
        value.Text=list&&!splitNetwork?$"{GetMetricLabel(model.Kind)}  {nextValue}":nextValue;
        if(layoutChanged){if(topBar)label.TextFontSize=10;duration.Height=double.NaN;duration.VerticalAlignment=VerticalAlignment.Stretch;duration.InkVerticalAlignment=VerticalAlignment.Top;duration.TextFontSize=GraphIntervalFontSize(appearance,topBar);duration.MinWidth=0;plotArea.ColumnDefinitions[0].Width=new GridLength(topBar?26:40);foreach(var guide in guides)guide.TextFontSize=topBar?10:12;}
        if(list){if(layoutChanged){PlaceValue(false);RowDefinitions[0].Height=new GridLength(1,GridUnitType.Star);for(int i=1;i<4;i++)RowDefinitions[i].Height=new GridLength(0);label.Visibility=Visibility.Collapsed;SetRow(value,0);SetRowSpan(value,1);value.TextFontSize=20;plotArea.Visibility=duration.Visibility=Visibility.Collapsed;}if(splitNetwork)LayoutNetworkNumeric(appearance,true,layoutChanged,20);return;}
        if(layoutChanged)
        {
        RowDefinitions[0].Height=new GridLength(topBar?14:20);RowDefinitions[1].Height=new GridLength(topBar?20:26);RowDefinitions[2].Height=new GridLength(1,GridUnitType.Star);RowDefinitions[3].Height=new GridLength(0);SetRow(value,1);
        SetColumnSpan(label,graph?1:4);heading.Graph=graph;
        label.Visibility=appearance.HideTitle?Visibility.Collapsed:Visibility.Visible;
        label.TextFontSize=appearance.TitleSize;label.FitToBox=appearance.AutoFit;
        label.TextBrush=BrushFor(appearance.TitleColor,titleBrush);
        }
        value.TextBrush=BrushFor(appearance.Color,value.TextBrush);
        if(network)value.TextBrush=BrushFor(NetworkDataColor(appearance,appearance.NetworkUp==true&&appearance.NetworkDown!=true),value.TextBrush);
        if(layoutChanged)value.TextFontSize=appearance.ValueSize;
        double numericSize=appearance.ValueSize;
        if(layoutChanged){
        heading.Height=double.NaN;heading.VerticalAlignment=VerticalAlignment.Stretch;
        if(graph)RowDefinitions[0].Height=new GridLength(GraphHeaderHeight(appearance,topBar));
        value.FitToBox=appearance.AutoFit;
        PlaceValue(graph);
        if(graph){SetRow(value,0);SetColumn(value,2);RowDefinitions[1].Height=new GridLength(0);heading.ColumnDefinitions[0].Width=new GridLength(1,GridUnitType.Star);heading.ColumnDefinitions[2].Width=new GridLength(1,GridUnitType.Star);heading.ColumnDefinitions[3].Width=new GridLength(topBar?38:70);}
        else {SetRow(value,1);SetColumn(value,0);}
        heading.AutoFit=appearance.AutoFit;heading.TitleSize=label.TextFontSize;heading.ValueSize=value.TextFontSize;heading.DurationSize=duration.TextFontSize;heading.InvalidateMeasure();
        plotArea.Visibility=duration.Visibility=graph?Visibility.Visible:Visibility.Collapsed;
        cores.Visibility=Visibility.Collapsed;plot.Visibility=Visibility.Visible;
        }
        if(!graph&&model.IsCoreLoad){
            if(layoutChanged){
                PlaceValue(true);heading.Graph=false;SetColumnSpan(label,1);SetColumn(value,2);
                heading.ColumnDefinitions[0].Width=new GridLength(1,GridUnitType.Star);
                heading.ColumnDefinitions[2].Width=GridLength.Auto;
                heading.ColumnDefinitions[3].Width=new GridLength(0);
                value.Visibility=appearance.HideCorePercentage?Visibility.Hidden:Visibility.Visible;
                double headerSize=Math.Max(appearance.HideTitle?0:appearance.TitleSize,appearance.HideCorePercentage&&!appearance.HideTitle?0:appearance.ValueSize);
                RowDefinitions[0].Height=new GridLength(headerSize==0?0:Math.Min(Math.Max(20,headerSize*1.25),Math.Max(20,ActualHeight*.45)));
                bool expandBars=appearance.HideTitle&&appearance.HideCorePercentage;SetRow(plotArea,expandBars?0:2);SetRowSpan(plotArea,expandBars?3:1);
                RowDefinitions[1].Height=new GridLength(0);
                label.InkAlignment=HorizontalAlignment.Left;value.InkAlignment=HorizontalAlignment.Right;
                label.InkVerticalAlignment=value.InkVerticalAlignment=VerticalAlignment.Bottom;
                plotArea.Visibility=Visibility.Visible;duration.Visibility=Visibility.Collapsed;
                foreach(var guide in guides)guide.Visibility=Visibility.Collapsed;
                plot.Visibility=Visibility.Collapsed;cores.Visibility=Visibility.Visible;
            }
            cores.Set(model.CoreBars.Select(b=>b.Height).ToArray(),value.TextBrush);return;
        }
        bool rawInfo=model.Kind==MetricKind.HardwareSensor;bool diskInfo=IsDisk(model.Kind),gpuInfo=model.Kind is >=MetricKind.GpuPowerLimit and <=MetricKind.GpuLimitReason;
        details.Visibility=rawInfo||diskInfo||gpuInfo?Visibility.Visible:Visibility.Collapsed;
        details.Text=rawInfo?(HardwareTelemetry.Find(appearance.HardwareSensorId)?.DisplayDevice??"Device unavailable"):diskInfo?((IsDiskRate(model.Kind)&&appearance.DiskId==null?null:service.SelectedDisk(appearance.DiskId))?.Description??(IsDiskRate(model.Kind)&&appearance.DiskId==null?"All disks":"Disk information unavailable")):gpuInfo?(model.Kind==MetricKind.GpuPowerLimit?$"Default {service.GpuInfo.DefaultLimit?.ToString("0")??"N/A"} W · Max {service.GpuInfo.MaxLimit?.ToString("0")??"N/A"} W":PowerPlanName()):"";
        details.TextBrush=titleBrush;details.ToolTip=ToolTip;details.TextFontSize=rawInfo?Math.Clamp(appearance.DetailTextSize,8,80):10;details.FitToBox=appearance.AutoFit;details.Height=rawInfo?Math.Min(details.TextFontSize*1.8,Math.Max(0,ActualHeight*.4)):diskInfo||gpuInfo?18:0;RowDefinitions[3].Height=new GridLength(details.Height);
        if(!graph&&!codexMetric)numericSize=LayoutValueRows(appearance,model.IsCoreLoad,details.Height,dualNumeric?NetworkDisplay(service.Latest,appearance,false):null,dualNumeric);
        if(!graph&&codexMetric){
            var snapshot=service.UsageSnapshot;var windows=snapshot?.Windows??[];bool fresh=snapshot!=null&&DateTime.Now-snapshot.RetrievedAt<TimeSpan.FromMinutes(2)&&snapshot.RetrievedAt<=DateTime.Now;
            RowDefinitions[1].Height=new GridLength(1,GridUnitType.Star);RowDefinitions[2].Height=GridLength.Auto;SetRowSpan(value,1);
            remaining.Visibility=windows.Length==1&&windows[0].Remaining.HasValue?Visibility.Visible:Visibility.Collapsed;remaining.Value=windows.FirstOrDefault().Remaining??0;remaining.Foreground=value.TextBrush;
            string Countdown(DateTime? target){if(target==null)return "N/A";var left=target.Value-DateTime.Now;if(left<=TimeSpan.Zero)return "0m";return left.TotalDays>=1?$"{(int)left.TotalDays}d {left.Hours}h {left.Minutes}m":left.TotalHours>=1?$"{(int)left.TotalHours}h {left.Minutes}m":$"{Math.Ceiling(left.TotalMinutes):0}m";}
            reset.Text=windows.Length==0?"UNAVAILABLE":string.Join(" · ",windows.Select(w=>$"RESET {(windows.Length>1?w.Label+" ":"")}{Countdown(w.Reset)}"))+(fresh?"":" · STALE");reset.TextFontSize=Math.Clamp(appearance.ResetTextSize,8,80);reset.Height=reset.TextFontSize*1.5;reset.FitToBox=appearance.AutoFit;reset.ToolTip=snapshot==null?"No account limit data":$"Account limit reset times · Retrieved {snapshot.RetrievedAt:G}";info.Visibility=Visibility.Visible;
            info.Measure(new Size(Math.Max(1,ActualWidth),double.PositiveInfinity));LayoutValueRows(appearance,false,info.DesiredSize.Height,stretchValueRow:true);return;
        }

        if(!graph){SetRowSpan(value,1);if(dualNumeric)LayoutNetworkNumeric(appearance,false,layoutChanged,numericSize);if(model.IsCoreLoad){plotArea.Visibility=Visibility.Visible;foreach(var guide in guides)guide.Visibility=Visibility.Collapsed;plot.Visibility=Visibility.Collapsed;cores.Set(model.CoreBars.Select(b=>b.Height).ToArray(),value.TextBrush);cores.Visibility=Visibility.Visible;}return;}SetRowSpan(value,1);
        double earliest=MonitorHistory.Now-seconds;
        var networkSeries=network&&(appearance.NetworkDown==true||appearance.NetworkUp==true)
            ?new[]{(Kind:MetricKind.NetworkDownload,Enabled:appearance.NetworkDown==true),(Kind:MetricKind.NetworkUpload,Enabled:appearance.NetworkUp==true)}.Where(s=>s.Enabled).Select(s=>(Samples:service.History.Read(s.Kind,earliest),Dashed:s.Kind==MetricKind.NetworkUpload&&appearance.NetworkDown==true,Upload:s.Kind==MetricKind.NetworkUpload)).ToArray():[];
        var samples=IsDisk(model.Kind)?service.DiskMetricSamples(appearance.DiskId,earliest,model.Kind):networkSeries.Length>0?[]:service.History.Read(network?MetricKind.Network:model.Kind,earliest);
        bool percent=model.Kind is MetricKind.CpuLoad or MetricKind.CpuCoreLoad or MetricKind.GpuLoad or MetricKind.MemoryLoad or MetricKind.CodexFiveHour or MetricKind.CodexWeekly;
        double factor=model.Kind is MetricKind.Network or MetricKind.NetworkDownload or MetricKind.NetworkUpload or MetricKind.DiskRead or MetricKind.DiskWrite?1d/1048576:model.Kind is MetricKind.MemoryUsed or MetricKind.VramUsed?1024d:1;
        if(model.Kind is (MetricKind.VramUsed or MetricKind.MemoryUsed)&&appearance.MemoryUnit=="GB")factor/=1000;
        var scaleSamples=networkSeries.Length>0?networkSeries.SelectMany(s=>s.Samples):samples.AsEnumerable();
        double maximum=percent?100:Math.Max(1,scaleSamples.Where(s=>s.Value.HasValue&&s.Time>=MonitorHistory.Now-seconds).Select(s=>s.Value!.Value*factor).DefaultIfEmpty(1).Max()*1.1);
        string unit=model.Kind switch {MetricKind.CpuTemperature or MetricKind.GpuTemperature or MetricKind.GpuHotspot=>"°C",MetricKind.CpuPower or MetricKind.GpuPower or MetricKind.GpuPowerLimit=>"W",MetricKind.CpuVoltage or MetricKind.GpuVoltage=>"V",MetricKind.CpuFan or MetricKind.GpuFan=>"RPM",MetricKind.MemoryUsed or MetricKind.VramUsed=>"MB",MetricKind.Network or MetricKind.NetworkDownload or MetricKind.NetworkUpload or MetricKind.DiskRead or MetricKind.DiskWrite=>"MB/s",_=>"%"};
        if(model.Kind is (MetricKind.VramUsed or MetricKind.MemoryUsed)&&appearance.MemoryUnit=="GB")unit="GB";
        for(int i=0;i<5;i++){guides[i].Visibility=(topBar||plotArea.ActualWidth<300||plotArea.ActualHeight<70)&&i is 1 or 3?Visibility.Collapsed:Visibility.Visible;guides[i].Text=$"{maximum*(4-i)/4:0.#}{(percent?"%":"")}";}
        duration.Text=percent?$"{seconds/60:0.#} min":$"{seconds/60:0.#} min · {unit}";
        if(network&&model.LargeGraph&&appearance.NetworkDown==true&&appearance.NetworkUp==true)
        {
            value.Text=NetworkDisplay(service.Latest,appearance with{NetworkUp=false},false);
            networkUpValue.Text=NetworkDisplay(service.Latest,appearance with{NetworkDown=false},false);
            networkUpValue.TextBrush=BrushFor(NetworkDataColor(appearance,true),value.TextBrush);
            LayoutNetworkDualGraph(appearance,layoutChanged);
        }
        Brush graphBrush=Application.Current.TryFindResource("SystemMonitorBrush") as Brush??Brushes.LimeGreen;
        if(appearance.Color is {} custom)try{graphBrush=(Brush)new BrushConverter().ConvertFromString(custom)!;}catch{} 
        plot.SparseGuides=topBar||plotArea.ActualWidth<300||plotArea.ActualHeight<70;plot.GuideColor=BrushFor(appearance.GuideColor,(Brush)Application.Current.FindResource("ModuleTitleBrush"));plot.GuideOpacity=appearance.GuideOpacity;
        plot.ToolTip=networkSeries.Length==2?"DL — solid line · UP — dashed line":null;
        if(codexMetric){var windows=service.UsageSnapshot?.Windows??[];plot.ToolTip="Account limits: 5H solid · WEEK dashed";plot.SetColoredSeries(windows.Select(w=>(service.History.Read(w.Label=="5H"?MetricKind.CodexFiveHour:MetricKind.CodexWeekly,earliest),w.Label=="WEEK",graphBrush)).ToArray(),seconds,100,1);return;}
        if(networkSeries.Length>0)plot.SetColoredSeries(networkSeries.Select(s=>(s.Samples,s.Dashed,BrushFor(NetworkDataColor(appearance,s.Upload),graphBrush))).ToArray(),seconds,maximum,factor);else plot.Set(samples,seconds,maximum,factor,graphBrush);
    }
    internal string LocationForProbe=>Location;
    internal double GraphReferenceCenter(double headerHeight)
    {
        // The module edge is Margin.Top above this tile. The first guide is
        // ten percent into the remaining plot. Center the capital reference
        // between those physical boundaries without moving the plot itself.
        return (headerHeight+Math.Max(0,ActualHeight-headerHeight)*.1-Margin.Top)/2;
    }
    private double GraphHeaderHeight(MetricPresentation appearance,bool topBar=false)
    {
        double requested=Math.Max(topBar?14:20,Math.Max(appearance.TitleSize,appearance.ValueSize)*1.05);
        return Math.Min(requested,Math.Max(1,ActualHeight-(topBar?12:24)));
    }
    private void RestoreStandardHeader()
    {
        // Special inline rows own bottom alignment only while they are active.
        // Ordinary numeric/title boxes return to the common cap-centered rule.
        label.InkVerticalAlignment=value.InkVerticalAlignment=VerticalAlignment.Center;
        label.InkAlignment=value.InkAlignment=HorizontalAlignment.Center;
        label.InvalidateVisual();value.InvalidateVisual();
        if(networkNumeric.Visibility==Visibility.Visible)
        {
            foreach(var text in new[]{label,value,networkUpValue})if(text.Parent==networkNumeric){networkNumeric.Children.Remove(text);ReparentCountForProbe++;}
            if(label.Parent==null)MoveText(label,heading);if(value.Parent==null)MoveText(value,this);
            networkNumeric.Visibility=Visibility.Collapsed;heading.Visibility=Visibility.Visible;
        }
        if(networkHeading.Visibility!=Visibility.Visible)return;
        foreach(var text in new[]{label,value,duration})
        {
            MoveText(text,heading);text.InkVerticalAlignment=VerticalAlignment.Center;
        }
        SetColumn(label,0);SetColumn(value,2);SetColumn(duration,3);networkHeading.Visibility=Visibility.Collapsed;heading.Visibility=Visibility.Visible;
    }
    private void LayoutNetworkNumeric(MetricPresentation appearance,bool list,bool layoutChanged,double size)
    {
        value.Text=NetworkDisplay(service!.Latest,appearance with{NetworkUp=false},false);networkUpValue.Text=NetworkDisplay(service.Latest,appearance with{NetworkDown=false},false);
        value.TextBrush=BrushFor(NetworkDataColor(appearance,false),value.TextBrush);networkUpValue.TextBrush=BrushFor(NetworkDataColor(appearance,true),value.TextBrush);
        var texts=list?new[]{label,value,networkUpValue}:new[]{value,networkUpValue};
        foreach(var text in texts){MoveText(text,networkNumeric);if(layoutChanged){text.Visibility=Visibility.Visible;SetRow(text,0);SetColumn(text,0);}}
        if(list){label.Text="NETWORK";heading.Visibility=Visibility.Collapsed;}
        networkNumeric.Visibility=Visibility.Visible;SetRow(networkNumeric,list?0:1);
        if(layoutChanged||networkNumericSize!=size){networkNumericSize=size;networkNumeric.Configure(texts,texts.Select(_=>size).ToArray(),appearance.AutoFit);}
    }
    private void LayoutNetworkDualGraph(MetricPresentation appearance,bool layoutChanged)
    {
        if(!layoutChanged)return;
        foreach(var text in new[]{label,value,duration})
        {
            MoveText(text,networkHeading);
        }
        MoveText(networkUpValue,networkHeading);
        heading.Visibility=Visibility.Collapsed;networkHeading.Visibility=Visibility.Visible;duration.MinWidth=0;
        networkHeading.Configure(label,value,networkUpValue,duration,appearance.TitleSize,appearance.ValueSize,duration.TextFontSize,appearance.AutoFit);
        RowDefinitions[0].Height=new GridLength(GraphHeaderHeight(appearance));RowDefinitions[1].Height=new GridLength(0);RowDefinitions[2].Height=new GridLength(1,GridUnitType.Star);
    }
    private double LayoutValueRows(MetricPresentation appearance,bool core,double footer=0,string? valueText=null,bool preserveValueFont=false,bool stretchValueRow=false)
    {
        // Allocate from actual glyphs after width fitting. The capital-to-baseline
        // reference stays stable; reserve descent space separately for drawing.
        (double Height,double FontSize) Required(OpticallyCenteredText text,double requested,string? content=null)
        {
            var metrics=text.GetValueRowMetrics(requested,content);
            double fit=appearance.AutoFit?Math.Min(1,Math.Max(1,ActualWidth-8)/Math.Max(1,metrics.Width)):1;
            double advance=text.LineAdvance>0?text.LineAdvance:requested*.76;
            return ((metrics.Reference+Math.Max(0,metrics.LineCount-1)*advance+metrics.BottomOverflow*2)*fit+8,requested*fit);
        }
        var titleMetrics=Required(label,appearance.TitleSize);var valueMetrics=Required(value,appearance.ValueSize,valueText);
        double title=appearance.HideTitle?0:titleMetrics.Height,number=valueMetrics.Height;
        double budget=Math.Max(1,ActualHeight-footer-(core?Math.Max(14,ActualHeight*.32):0));
        double ratio=appearance.AutoFit?Math.Min(1,budget/Math.Max(1,title+number)):1;
        label.TextFontSize=titleMetrics.FontSize*ratio;if(!preserveValueFont)value.TextFontSize=valueMetrics.FontSize*ratio;title*=ratio;number*=ratio;
        double spare=Math.Max(0,budget-title-number);
        heading.Height=title;heading.VerticalAlignment=VerticalAlignment.Bottom;
        RowDefinitions[0].Height=new GridLength(title+spare/2);
        RowDefinitions[1].Height=stretchValueRow?new GridLength(1,GridUnitType.Star):new GridLength(number);
        return valueMetrics.FontSize*ratio;
    }
    internal MetricPresentation CellAppearance=>Appearance;
    internal void ApplyForProbe(MetricPresentation p)=>ApplyCell(p);
    private void MoveText(OpticallyCenteredText text,Panel parent){if(ReferenceEquals(text.Parent,parent))return;if(text.Parent is Panel previous)previous.Children.Remove(text);parent.Children.Add(text);ReparentCountForProbe++;}
    private void PlaceValue(bool inHeader){MoveText(value,inHeader?heading:this);SetRow(value,inHeader?0:1);SetColumn(value,inHeader?2:0);SetRowSpan(value,1);}
    private static Brush BrushFor(string? color,Brush fallback){try{return color==null?fallback:(Brush)new BrushConverter().ConvertFromString(color)!;}catch{return fallback;}}
    internal double VisibleSeconds=>seconds;
    internal static double WheelInterval(double current,int delta)=>Math.Clamp((delta>0?Math.Ceiling(current/60)-1:Math.Floor(current/60)+1)*60,60,MonitorHistory.MaximumSeconds);
    internal void ZoomForProbe(int delta){seconds=WheelInterval(seconds,delta);SaveZoom();Refresh();}
    internal void ResetForProbe()=>ResetZoom();
}

// One line, independent colors and one shared cap-to-baseline reference bottom.
// This header is used only by a large Network graph with both directions enabled.
internal sealed class NetworkGraphHeader:Grid
{
    private OpticallyCenteredText[] texts=[];
    private double[] sizes=[];
    private bool fit=true;
    private readonly GraphHeaderLayout layout=new();
    internal void Configure(OpticallyCenteredText title,OpticallyCenteredText down,OpticallyCenteredText up,OpticallyCenteredText duration,double titleSize,double valueSize,double durationSize,bool autoFit)
    {texts=[title,down,up,duration];sizes=[titleSize,valueSize,valueSize,durationSize];fit=autoFit;InvalidateMeasure();}
    protected override Size MeasureOverride(Size available)
    {
        return texts.Length==4?layout.Measure(available,texts.Take(3).ToArray(),sizes.Take(3).ToArray(),texts[3],sizes[3],fit):base.MeasureOverride(available);
    }
    protected override Size ArrangeOverride(Size size)
    {
        return texts.Length==4?layout.Arrange(size):base.ArrangeOverride(size);
    }
}

internal sealed class HistoryPlot : FrameworkElement
{
    private readonly DrawingVisual guideVisual=new(),dataVisual=new();
    internal HistoryPlot(){AddVisualChild(guideVisual);AddVisualChild(dataVisual);RenderOptions.SetEdgeMode(guideVisual,EdgeMode.Aliased);}
    protected override int VisualChildrenCount=>2;
    protected override Visual GetVisualChild(int index)=>index==0?guideVisual:index==1?dataVisual:throw new ArgumentOutOfRangeException(nameof(index));
    private (MonitorHistory.Sample[] Samples,bool Dashed,Brush Color)[] series=[];
    private double seconds=120,maximum=100,factor=1;
    internal Brush GuideColor=Brushes.White;
    internal double GuideOpacity=.255;
    internal bool SparseGuides;
    internal double[] GuidePixelRows=[];
    private Brush brush=Brushes.LimeGreen;
    internal int SeriesCountForProbe=>series.Length;
    internal bool[] DashedSeriesForProbe=>series.Select(s=>s.Dashed).ToArray();
    internal string[] SeriesColorsForProbe=>series.Select(s=>s.Color.ToString()).ToArray();
    internal void Set(MonitorHistory.Sample[] values,double duration,double max,double multiplier,Brush color)=>SetSeries([(values,false)],duration,max,multiplier,color);
    internal void SetSeries((MonitorHistory.Sample[] Samples,bool Dashed)[] values,double duration,double max,double multiplier,Brush color)=>SetColoredSeries(values.Select(s=>(s.Samples,s.Dashed,color)).ToArray(),duration,max,multiplier);
    internal void SetColoredSeries((MonitorHistory.Sample[] Samples,bool Dashed,Brush Color)[] values,double duration,double max,double multiplier){series=values;seconds=duration;maximum=max;factor=multiplier;InvalidateVisual();}
    internal static bool HistoryHasGap(MonitorHistory.Sample previous,MonitorHistory.Sample sample)
    {
        // Network rates cover the measured counter interval even when the UI
        // timer is late. Join only when that interval reaches the prior sample.
        // Null readings still split above; uncovered intervals remain gaps.
        if(sample.IntervalStart is double start&&double.IsFinite(start)&&start<=sample.Time)
            return start>previous.Time+.05;
        return sample.Time-previous.Time>2.5;
    }
    internal static void AppendBucketPoints(IEnumerable<MonitorHistory.Sample> bucket,List<Point> points,double now,double seconds,double width,double top,double height,double factor,double maximum)
    {
        // A bucket needs only its endpoints and first minimum/maximum. Keep the
        // former Distinct/OrderBy result without arrays, a hash set or a sorter
        // for every plot column; equal-time candidates retain their input order.
        bool any=false;
        MonitorHistory.Sample first=default,minimum=default,maximumSample=default,last=default;
        foreach(var sample in bucket)
        {
            if(!sample.Value.HasValue)continue;
            if(!any){first=minimum=maximumSample=sample;any=true;}
            else
            {
                if(Comparer<double?>.Default.Compare(sample.Value,minimum.Value)<0)minimum=sample;
                if(Comparer<double?>.Default.Compare(sample.Value,maximumSample.Value)>0)maximumSample=sample;
            }
            last=sample;
        }
        if(!any)return;
        var a=first;var b=minimum;var c=maximumSample;var d=last;
        static void ByTime(ref MonitorHistory.Sample left,ref MonitorHistory.Sample right)
        {
            if(left.Time.CompareTo(right.Time)>0)(left,right)=(right,left);
        }
        ByTime(ref a,ref b);ByTime(ref b,ref c);ByTime(ref c,ref d);
        ByTime(ref a,ref b);ByTime(ref b,ref c);ByTime(ref a,ref b);
        for(int index=0;index<4;index++)
        {
            var sample=index switch{0=>a,1=>b,2=>c,_=>d};
            if((index>0&&sample.Equals(a))||(index>1&&sample.Equals(b))||(index>2&&sample.Equals(c)))continue;
            var point=new Point(Math.Clamp((sample.Time-now+seconds)/seconds*width,0,width),top+height*(1-Math.Clamp(sample.Value!.Value*factor/maximum,0,1)));
            points.Add(point);
        }
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);using var guideContext=guideVisual.RenderOpen();using var dataContext=dataVisual.RenderOpen();dc=dataContext;
        double width=ActualWidth,top=ActualHeight/10,height=Math.Max(0,ActualHeight*.8);if(width<=0||height<=0)return;
        // Snap after DPI and every ancestor render scale to equal one-device-pixel coverage.
        var source=PresentationSource.FromVisual(this);if(source?.RootVisual is not Visual root||source.CompositionTarget==null)return;
        var toRoot=TransformToAncestor(root);var device=source.CompositionTarget.TransformToDevice;
        Point offset=device.Transform(toRoot.Transform(new Point())),unit=device.Transform(toRoot.Transform(new Point(1,1)));
        // PointToScreen quantizes coordinates through Win32; retain the exact
        // fractional ancestor scale and use it only for the root screen origin.
        Point screen=root.PointToScreen(new Point());Point origin=new(screen.X+offset.X,screen.Y+offset.Y);
        double sx=Math.Abs(unit.X-offset.X),sy=Math.Abs(unit.Y-offset.Y);
        if(sx<=0||sy<=0)return;var guide=GuideColor.Clone();guide.Opacity=Math.Clamp(GuideOpacity,0,1);guide.Freeze();GuidePixelRows=new double[5];
        for(int i=0;i<5;i++){if(SparseGuides&&i is 1 or 3){GuidePixelRows[i]=0;continue;}double pixel=Math.Round(origin.Y+(top+i*height/4)*sy);GuidePixelRows[i]=pixel;double y=(pixel-origin.Y)/sy;for(double x=0;x<width*sx;x+=9)guideContext.DrawRectangle(guide,null,new Rect(x/sx,y,Math.Min(3,width*sx-x)/sx,1/sy));}
        if(SparseGuides)GuidePixelRows=GuidePixelRows.Where((_,i)=>i%2==0).ToArray();double now=MonitorHistory.Now;
        foreach(var signal in series)
        {
        // A plot frame is immutable. Mutable brushes/pens acquire an inheritance
        // context for every retained drawing command (hundreds per graph).
        // Snapshot the brush, preserving the caller's editable palette object.
        brush=(Brush)signal.Color.GetCurrentValueAsFrozen();
        var visible=signal.Samples.Where(s=>s.Time>=now-seconds).ToArray();
        var pen=new Pen(brush,1.3){DashStyle=signal.Dashed?DashStyles.Dash:DashStyles.Solid};pen.Freeze();
        // Group into plot columns and retain both extrema in time order.
        var segments=new List<List<MonitorHistory.Sample>>{new()};
        foreach(var sample in visible){if(!sample.Value.HasValue){if(segments[^1].Count>0)segments.Add(new());continue;}if(segments[^1].Count>0&&HistoryHasGap(segments[^1][^1],sample))segments.Add(new());segments[^1].Add(sample);}
        foreach(var segment in segments)
        {
        var points=new List<Point>();
        foreach(var bucket in segment.GroupBy(s=>(int)((s.Time-now+seconds)/seconds*width)))
        {
            AppendBucketPoints(bucket,points,now,seconds,width,top,height,factor,maximum);
        }
        if(points.Count>0)dc.DrawEllipse(brush,null,points[0],1,1);
        if(!signal.Dashed)
        {
            for(int i=1;i<points.Count;i++)dc.DrawLine(pen,points[i-1],points[i]);
        }
        else if(points.Count>1)
        {
            // One continuous path preserves dash phase across sample columns.
            // Individual DrawLine calls would restart each dash and appear solid.
            var path=new StreamGeometry();using(var context=path.Open()){context.BeginFigure(points[0],false,false);context.PolyLineTo(points.Skip(1).ToArray(),true,false);}path.Freeze();dc.DrawGeometry(null,pen,path);
        }
        }
        }
    }
}

internal sealed class CoreLoadPlot : FrameworkElement
{
    private double[] heights=[];private Brush brush=Brushes.LimeGreen;
    private static readonly Pen outline=CreateOutline();
    private static Pen CreateOutline(){var pen=new Pen(new SolidColorBrush(Color.FromArgb(90,220,220,220)),.5);pen.Freeze();return pen;}
    internal void Set(double[] values,Brush color){heights=values;brush=color;InvalidateVisual();}
    protected override void OnRender(DrawingContext dc){base.OnRender(dc);if(heights.Length==0)return;double step=ActualWidth/heights.Length;for(int i=0;i<heights.Length;i++){double height=Math.Clamp(heights[i]/36,0,1)*ActualHeight;dc.DrawRectangle(null,outline,new Rect(i*step+1,.25,Math.Max(0,step-2),Math.Max(0,ActualHeight-.5)));dc.DrawRectangle(brush,null,new Rect(i*step+1,ActualHeight-height,Math.Max(0,step-2),height));}}
}


