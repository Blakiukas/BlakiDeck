using System.IO;
using System.Windows;
using System.Windows.Controls;
namespace Ekrano_valdymas;

public partial class SystemMonitorWidget
{
    // Cell editors persist through the monitor that owns their placement.
    internal void SaveCurrentLayout()=>SaveSlots();
    private readonly SystemMonitorWidget? readingSource;
    internal string? ViewKey {get;}
    internal string CellLocation(MonitorSlot slot)=>$"monitor{(ViewKey==null?"":"-"+ViewKey)}:{Slots.IndexOf(slot)}";
    private void SeedIndependentLayout()
    {
        if(ViewKey==null||File.Exists(settingsFile))return;
        if(ViewKey is "full" or "wide"){
            var captured=CurrentMonitorDefaults.For(ViewKey);var seededCells=captured.Cells.OrderBy(c=>c.Index).ToArray();
            AtomicJson.Write(settingsFile,seededCells.Select(c=>c.Target[7..]).ToArray());
            AtomicJson.Write(settingsFile+".mixed.json",seededCells.Select(c=>new{Target=c.Target,Name=(string?)null,Appearance=(string?)null,Icon=(string?)null}).ToArray());
            AtomicJson.Write(GridFile,captured.Grid);
            SensorCellPreferences.ApplyPlacement(seededCells.ToDictionary(c=>$"monitor-{ViewKey}:{c.Index}",c=>(MetricPresentation?)c.Presentation));return;
        }
        string original=Path.Combine(DiagnosticState.LocalApplicationData,"Ekrano_valdymas","system-monitor.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsFile)!);
        foreach(var pair in new[]{(original,settingsFile),(original+".mixed.json",settingsFile+".mixed.json"),(Path.ChangeExtension(original,".active.json"),Path.ChangeExtension(settingsFile,".active.json"))})
            if(File.Exists(pair.Item1))File.Copy(pair.Item1,pair.Item2,false);
        var cells=Enumerable.Range(0,12).ToDictionary(i=>$"monitor-{ViewKey}:{i}",i=>(MetricPresentation?)SensorCellPreferences.Get("monitor:"+i,new(GraphTitleSize:20,GraphValueSize:20)));
        SensorCellPreferences.ApplyPlacement(cells);
    }
    public static readonly DependencyProperty SectionLayoutProperty=DependencyProperty.Register(nameof(SectionLayout),typeof(string),typeof(SystemMonitorWidget),new PropertyMetadata(""));
    public string SectionLayout {get=>(string)GetValue(SectionLayoutProperty);private set=>SetValue(SectionLayoutProperty,value);}
    internal int SlotCapacity=>GridLayout is {} grid?grid.Cells.Select(c=>c.Slot+1).DefaultIfEmpty(0).Max():SectionLayout.Length>0?SectionLayout.Split(',').Sum(SectionCount):WideLayout?11:9;
    internal static int SectionCount(string kind)=>kind switch{"quad"=>4,"stack"=>3,"graphs"=>2,_=>9};
    private bool IsLargeGraph(int index)
    {
        if(index<0)return false;
        if(GridLayout is {} grid)return index<grid.Cells.Length&&grid.Cells[index].LargeGraph;
        if(SectionLayout.Length==0)return WideLayout&&index<2;
        foreach(var part in SectionLayout.Split(',')){int count=SectionCount(part);if(index<count)return part=="graphs";index-=count;}
        return false;
    }
    private string SectionFile=>settingsFile+".sections";
    private void LoadSectionLayout()
    {
        string layout=ViewKey=="full"?"quad,quad,stack":"";
        if(File.Exists(SectionFile)){var saved=File.ReadAllText(SectionFile);if(ValidSections(saved))layout=saved;}
        SectionLayout=layout;RebuildActiveSlots();
    }
    private bool ValidSections(string value)=>value.Length==0||value.Split(',') is var parts&&parts.Length<=(ViewKey=="full"?3:WideLayout||ViewKey=="wide"?2:1)&&parts.All(p=>p is "quad" or "stack" or "graphs");
    internal void SetSections(string value)
    {
        if(!ValidSections(value))throw new ArgumentException("Invalid monitor sections",nameof(value));
        if(File.Exists(GridFile))File.Delete(GridFile);GridLayout=null;
        SectionLayout=value;RebuildActiveSlots();Directory.CreateDirectory(Path.GetDirectoryName(SectionFile)!);File.WriteAllText(SectionFile,value);
    }
    internal MenuItem LayoutOptions()
    {
        var menu=new MenuItem{Header="Layout",Icon=MainWindow.MonitorMenuIcon("\uE8A9")};
        var header=new StackPanel{Orientation=Orientation.Horizontal};header.Children.Add(new TextBlock{Text="Layout",VerticalAlignment=VerticalAlignment.Center});
        var edit=new Button{Name="EditMonitorLayout",Width=28,Height=26,Margin=new Thickness(12,0,0,0),ToolTip="Edit layout",Tag="NoWindowDrag"};edit.SetResourceReference(StyleProperty,"ModuleOptionsButtonStyle");edit.Content=new OpticallyCenteredText{Text="\uE70F",TextFontFamily=new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),TextFontSize=14,TextBrush=AdaptiveSensorColors.PencilBrush};
        edit.Click+=(_,e)=>{e.Handled=true;menu.IsSubmenuOpen=false;ItemsControl? ancestor=ItemsControl.ItemsControlFromItemContainer(menu);while(ancestor is MenuItem parent){parent.IsSubmenuOpen=false;ancestor=ItemsControl.ItemsControlFromItemContainer(parent);}if(ancestor is ContextMenu context)context.IsOpen=false;Dispatcher.BeginInvoke(new Action(()=>CreateLayoutEditor().ShowDialog()));};header.Children.Add(edit);menu.Header=header;
        void Add(string label,string pattern){var item=new MenuItem{Header=label,IsCheckable=true,IsChecked=GridLayout==null&&SectionLayout==pattern};item.Click+=(_,_)=>SetSections(pattern);menu.Items.Add(item);}
        Add("Original layout","");
        if(ViewKey=="full")
        {
            Add("2×2 · 2×2 · 3 vertical","quad,quad,stack");
            Add("2 graphs · 2×2 · 3 vertical","graphs,quad,stack");
            Add("2×2 · 2×2 · 2×2","quad,quad,quad");
        }
        else if(WideLayout){Add("2×2 · 3 vertical","quad,stack");Add("2 graphs · 2×2","graphs,quad");}
        else {Add("2×2 cards","quad");Add("3 vertical cards","stack");Add("2 graphs","graphs");}
        menu.Items.Add(new Separator());
        var clear=new MenuItem{Header="Clear layout",Icon=MainWindow.MonitorMenuIcon("\uE74D")};clear.Click+=(_,_)=>ApplyGridLayout(new(GridColumns,12,[]));menu.Items.Add(clear);
        return menu;
    }
}
