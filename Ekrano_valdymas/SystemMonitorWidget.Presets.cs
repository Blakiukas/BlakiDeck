using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Ekrano_valdymas;
public partial class SystemMonitorWidget
{
    internal static string MetricGroup(MetricKind kind)=>kind.ToString().StartsWith("Cpu")?"CPU":kind.ToString().StartsWith("Gpu")||kind==MetricKind.VramUsed?"GPU":IsDisk(kind)?"Disk":"";
    internal static void AddGroupedSensor(ItemsControl parent,MetricKind kind,MenuItem item)
    {
        string group=MetricGroup(kind);
        if(group.Length==0){parent.Items.Add(item);return;}
        var menu=parent.Items.OfType<MenuItem>().FirstOrDefault(m=>Equals(m.Tag,"sensor-group:"+group));
        if(menu==null){menu=new MenuItem{Header=group,Tag="sensor-group:"+group};parent.Items.Insert(parent.Items.OfType<MenuItem>().Count(m=>m.Tag is string t&&t.StartsWith("sensor-group:")),menu);}
        menu.Items.Add(item);
    }
    private sealed record PresetCell(string? Target,string? Name,string? Appearance,string? Icon,bool Active,MetricPresentation? Sensor);
    private sealed record SavedPreset(MonitorGridLayout Grid,PresetCell[] Cells);
    private sealed record PresetFile(string Selected,Dictionary<string,SavedPreset> Layouts);
    private readonly Dictionary<string,SavedPreset> savedPresets=new();
    private string selectedPreset="All";
    internal string SelectedPreset=>selectedPreset;
    private string PresetsPath=>settingsFile+".presets.json";
    private static readonly string[] PresetNames=["All","CPU","GPU","Disk"];
    private void InitializePresets()
    {
        try{if(File.Exists(PresetsPath)&&JsonSerializer.Deserialize<PresetFile>(File.ReadAllText(PresetsPath)) is {} file){foreach(var pair in file.Layouts.Where(p=>PresetNames.Contains(p.Key)&&p.Value.Grid.Columns==GridColumns&&p.Value.Grid.IsValid(Slots.Count)))savedPresets[pair.Key]=pair.Value;if(PresetNames.Contains(file.Selected))selectedPreset=file.Selected;}}
        catch(Exception ex){InteractionDiagnostics.RecordFailure("Monitor presets",ex);}
        // The existing working files are authoritative for edits made since the
        // last switch; never overwrite them with an older preset snapshot here.
        BuildPresetButtons();
    }
    private SavedPreset CapturePreset()=>new(CaptureGridLayout(),Slots.Select(s=>new PresetCell(s.TransferTarget,s.Shortcut?.CustomName,s.Shortcut?.Presentation,s.Shortcut?.IconPath,s.IsActive,SensorCellPreferences.Capture(CellLocation(s),s.TransferTarget))).ToArray());
    private static bool LegacyGpuTarget(string? target)=>target is "sensor:GpuPowerLimit" or "sensor:GpuPowerLimitActive" or "sensor:GpuThermalLimitActive" or "sensor:GpuLimitReason";
    private static SavedPreset UpgradeGpuPreset(SavedPreset preset)
    {
        var old=preset.Grid.Cells.Where(c=>c.Slot<preset.Cells.Length&&LegacyGpuTarget(preset.Cells[c.Slot].Target)).ToArray();
        if(old.Length==0)return preset;
        var keeper=old.OrderByDescending(c=>c.Width*c.Height).First();
        int x=old.Min(c=>c.X),y=old.Min(c=>c.Y),right=old.Max(c=>c.X+c.Width),bottom=old.Max(c=>c.Y+c.Height);
        var remaining=preset.Grid with{Cells=preset.Grid.Cells.Where(c=>!old.Contains(c)).ToArray()};
        var combined=new MonitorGridCell(keeper.Slot,x,y,right-x,bottom-y);
        if(!remaining.Fits(combined,keeper.Slot))combined=keeper with{LargeGraph=false};
        var cells=preset.Cells.ToArray();
        foreach(var cell in old)cells[cell.Slot]=new(null,null,null,null,true,null);
        cells[keeper.Slot]=new("sensor:GpuStatus",null,null,null,true,SensorFactoryDefaults.ForMetric(MetricKind.GpuStatus));
        return new(remaining with{Cells=[..remaining.Cells,combined]},cells);
    }
    private void MigrateGpuStatus()
    {
        var current=CapturePreset();var upgraded=UpgradeGpuPreset(current);
        bool changed=!ReferenceEquals(current,upgraded);
        foreach(string key in savedPresets.Keys.ToArray()){var old=savedPresets[key];savedPresets[key]=UpgradeGpuPreset(old);changed|=!ReferenceEquals(old,savedPresets[key]);}
        if(!changed)return;
        RestorePreset(upgraded);savedPresets[selectedPreset]=upgraded;
        AtomicJson.Write(PresetsPath,new PresetFile(selectedPreset,savedPresets));
    }
    private SavedPreset FactoryPreset(string name)
    {
        if(name=="All")
        {
            var captured=CurrentMonitorDefaults.For(ViewKey);var fullCells=captured.Cells;return new(captured.Grid,Enumerable.Range(0,fullCells.Max(c=>c.Index)+1).Select(i=>fullCells.FirstOrDefault(c=>c.Index==i) is {} c?new PresetCell(c.Target,null,null,null,true,c.Presentation):new PresetCell(null,null,null,null,true,null)).ToArray());
        }
        if(name=="GPU")
        {
            var metrics=SelectableMetrics.Where(k=>MetricGroup(k)=="GPU"&&k!=MetricKind.GpuStatus).ToArray();
            int w=GridColumns/3;int rowHeight=12/(int)Math.Ceiling(metrics.Length/2d);
            var tiles=metrics.Select((k,i)=>new MonitorGridCell(i,i%2*w,i/2*rowHeight,w,rowHeight)).Append(new(metrics.Length,w*2,0,GridColumns-w*2,12)).ToArray();
            var data=metrics.Append(MetricKind.GpuStatus).Select(k=>new PresetCell("sensor:"+k,null,null,null,true,SensorFactoryDefaults.ForView(ViewKey,k) with{Mode="Value"})).ToArray();
            return new(new(GridColumns,12,tiles),data);
        }
        var kinds=SelectableMetrics.Where(k=>MetricGroup(k)==name).ToArray();
        int across=3,rows=(int)Math.Ceiling(kinds.Length/(double)across),width=GridColumns/across,height=12/rows;
        var grid=new MonitorGridLayout(GridColumns,12,Enumerable.Range(0,across*rows).Select(i=>new MonitorGridCell(i,i%across*width,i/across*height,width,height,false)).ToArray());
        var cells=Enumerable.Range(0,across*rows).Select(i=>i<kinds.Length?new PresetCell("sensor:"+kinds[i],null,null,null,true,SensorFactoryDefaults.ForView(ViewKey,kinds[i]) with{Mode="Value"}):new PresetCell(null,null,null,null,true,null)).ToArray();
        return new(grid,cells);
    }
    private void RestorePreset(SavedPreset preset)
    {
        preset=UpgradeGpuPreset(preset);
        foreach(var slot in Slots)slot.SetContent(null);
        for(int i=0;i<Math.Min(Slots.Count,preset.Cells.Length);i++){var cell=preset.Cells[i];Slots[i].SetContent(cell.Target,cell.Name,cell.Appearance,cell.Icon);Slots[i].IsActive=cell.Active;}
        SensorCellPreferences.ApplyPlacement(Slots.Select((s,i)=>(s,i)).ToDictionary(p=>CellLocation(p.s),p=>p.i<preset.Cells.Length?preset.Cells[p.i].Sensor:null));
        ApplyGridLayout(preset.Grid);UpdateSensorActivity();RefreshValues();SaveSlots();
    }
    internal void SwitchPreset(string name)
    {
        if(!PresetNames.Contains(name)||name==selectedPreset)return;
        var previous=CapturePreset();savedPresets[selectedPreset]=previous;
        var next=savedPresets.GetValueOrDefault(name)??FactoryPreset(name);
        try{RestorePreset(next);}catch{RestorePreset(previous);throw;}
        selectedPreset=name;savedPresets[name]=next;
        AtomicJson.Write(PresetsPath,new PresetFile(selectedPreset,savedPresets));
        RefreshPresetButtons();
    }
    private void BuildPresetButtons()
    {
        presetButtons.Children.Clear();
        foreach(string name in PresetNames)
        {
            var button=new Button{Tag="NoWindowDrag",Width=40,Height=30,Margin=new Thickness(2,0,2,0),Style=HeaderSelectionStyle.Create(),ToolTip=$"Layout {Array.IndexOf(PresetNames,name)+1}"};
            var text=new OpticallyCenteredText{Text=(Array.IndexOf(PresetNames,name)+1).ToString(),TextFontSize=11,TextFontWeight=FontWeights.SemiBold,IsHitTestVisible=false};text.SetResourceReference(OpticallyCenteredText.TextBrushProperty,"PanelTextBrush");button.Content=text;
            System.Windows.Automation.AutomationProperties.SetName(button,"System Monitor layout "+(Array.IndexOf(PresetNames,name)+1));
            button.Click+=(_,e)=>{e.Handled=true;SwitchPreset(name);};presetButtons.Children.Add(button);
        }
        RefreshPresetButtons();
    }
    private void RefreshPresetButtons(){foreach(Button button in presetButtons.Children){bool active=((OpticallyCenteredText)button.Content).Text==(Array.IndexOf(PresetNames,selectedPreset)+1).ToString();Selector.SetIsSelected(button,active);System.Windows.Automation.AutomationProperties.SetItemStatus(button,active?"Selected":"Not selected");}}
}
