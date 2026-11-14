using System.IO;
using System.Text.Json;
using System.Windows;
namespace Ekrano_valdymas;
public partial class SystemMonitorWidget
{
    internal ShortcutTransfer CreateCellTransfer(MonitorSlot source)
    {
        string target=source.TransferTarget;
        return new(target,source.Label,source.Shortcut?.Presentation,source.Shortcut?.IconPath,(t,n,p,i)=>
        {
            if(string.IsNullOrEmpty(t))ClearMonitorCell(source);else {source.SetContent(t,n,p,i);UpdateSensorActivity();RefreshValues();SaveSlots();}
        }){SourceOwner=this,SourceSlot=source,SourceLocation=CellLocation(source),
           SourceIsCurrent=()=>Slots.Contains(source)&&source.TransferTarget==target,
           AcceptReplacement=t=>t==null||(!AirthingsMini.IsTarget(t)&&!LegacyAirthingsStatusTarget.IsTarget(t)),
           SourceGridOffset=DragGridOffset(source)};
    }
    private System.Windows.Input.TouchDevice? cellTouch;
    private Point cellGrab;
    private void Metric_TouchDown(object sender,System.Windows.Input.TouchEventArgs e)
    {
        if(cellTouch!=null||sender is not System.Windows.Controls.Border {DataContext:MonitorSlot slot} border)return;
        cellTouch=e.TouchDevice;pressedSlot=slot;dragStart=e.GetTouchPoint(this).Position;cellGrab=e.GetTouchPoint(border).Position;
        liveGridStart=CaptureGridLayout().Cells.FirstOrDefault(c=>c.Slot==Slots.IndexOf(slot));liveGridChanged=false;liveGridGesture=false;
        e.TouchDevice.Capture(border);e.Handled=true;
    }
    private void Metric_TouchMove(object sender,System.Windows.Input.TouchEventArgs e)
    {
        if(e.TouchDevice!=cellTouch||pressedSlot is not {} slot||sender is not FrameworkElement element)return;
        var point=e.GetTouchPoint(this).Position;
        if((point-dragStart).Length<8)return;
        liveGridGesture=true;
        if(new Rect(monitorCells.RenderSize).Contains(e.GetTouchPoint(monitorCells).Position))MoveLiveCell(point);
        else if(!string.IsNullOrEmpty(slot.TransferTarget))
        {
            var transfer=CreateCellTransfer(slot);transfer.SourceGrab=cellGrab;SetTransferSize(element,slot,transfer);
            pressedSlot=null;liveGridStart=null;cellTouch=null;
            if(liveGridChanged){liveGridChanged=false;AtomicJson.Write(GridFile,CaptureGridLayout());SaveSlots();}
            if(Window.GetWindow(this) is {} owner)ShortcutDragSupport.BeginTouchDrag(element,e.TouchDevice,e.GetTouchPoint(owner).Position,transfer);
        }
        e.Handled=true;
    }
    private void Metric_LostTouchCapture(object sender,System.Windows.Input.TouchEventArgs e)
    {
        if(cellTouch!=e.TouchDevice)return;
        cellTouch=null;pressedSlot=null;liveGridStart=null;liveGridGesture=false;
        if(liveGridChanged){liveGridChanged=false;ApplyGridLayout(CaptureGridLayout());SaveSlots();}
    }
    private void Metric_TouchUp(object sender,System.Windows.Input.TouchEventArgs e)
    {
        if(e.TouchDevice!=cellTouch)return;
        if(liveGridGesture)SwapLiveCell(e.GetTouchPoint(this).Position);
        var slot=pressedSlot;pressedSlot=null;cellTouch=null;liveGridStart=null;
        e.TouchDevice.Capture(null);
        if(liveGridChanged){liveGridChanged=false;ApplyGridLayout(CaptureGridLayout());SaveSlots();}
        else if(!liveGridGesture&&slot?.Shortcut is {IsEmpty:false} shortcut)ActivateShortcut(shortcut);
        liveGridGesture=false;e.Handled=true;
    }
    private static void SetTransferSize(FrameworkElement element,MonitorSlot source,ShortcutTransfer transfer)
    {
        if(source.Kind is MetricKind.CpuStatus or MetricKind.GpuStatus or MetricKind.DiskStatus && !source.IsShortcut && Window.GetWindow(element) is {} window)
            transfer.MinimumTargetSize=element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize)).Size;
    }
    private MonitorGridCell? liveGridStart;
    private bool liveGridChanged;
    private bool liveGridGesture;
    private void Metric_LostMouseCapture(object sender,System.Windows.Input.MouseEventArgs e)
    {
        if(!liveGridChanged)return;
        liveGridChanged=false;liveGridStart=null;pressedSlot=null;
        ApplyGridLayout(CaptureGridLayout());SaveSlots();
    }
    private bool SwapLiveCell(Point point)
    {
        if(liveGridStart is not {} source)return false;
        var local=TranslatePoint(point,monitorCells);var grid=CaptureGridLayout();
        if(monitorCells.ActualWidth<=0||monitorCells.ActualHeight<=0)return false;
        var at=new Point(local.X/monitorCells.ActualWidth*grid.Columns,local.Y/monitorCells.ActualHeight*grid.Rows);
        var destination=grid.Cells.FirstOrDefault(c=>c.Slot!=source.Slot&&c.Bounds.Contains(at));
        if(destination==null)return false;
        var origin=grid.Cells.First(c=>c.Slot==source.Slot);
        var first=origin with{X=destination.X,Y=destination.Y};var second=destination with{X=origin.X,Y=origin.Y};
        var next=grid with{Cells=grid.Cells.Select(c=>c.Slot==origin.Slot?first:c.Slot==destination.Slot?second:c).ToArray()};
        if(!next.IsValid(Slots.Count))return false;
        GridLayout=next;liveGridChanged=true;return true;
    }
    private bool MoveLiveCell(Point point)
    {
        if(liveGridStart is not {} start||monitorCells.ActualWidth<=0||monitorCells.ActualHeight<=0)return false;
        var grid=CaptureGridLayout();var delta=point-dragStart;
        int dx=(int)Math.Round(delta.X/monitorCells.ActualWidth*grid.Columns),dy=(int)Math.Round(delta.Y/monitorCells.ActualHeight*grid.Rows);
        var cell=start with{X=Math.Clamp(start.X+dx,0,grid.Columns-start.Width),Y=Math.Clamp(start.Y+dy,0,grid.Rows-start.Height)};
        if(!grid.Fits(cell,cell.Slot))return true;
        if(grid.Cells.FirstOrDefault(c=>c.Slot==cell.Slot)!=cell){GridLayout=grid.Move(cell);liveGridChanged=true;}
        return true;
    }
    public static readonly DependencyProperty GridLayoutProperty=DependencyProperty.Register(nameof(GridLayout),typeof(MonitorGridLayout),typeof(SystemMonitorWidget),new PropertyMetadata(null));
    public MonitorGridLayout? GridLayout {get=>(MonitorGridLayout?)GetValue(GridLayoutProperty);private set=>SetValue(GridLayoutProperty,value);}
    private string GridFile=>settingsFile+".grid.json";
    private int GridColumns=>ViewKey=="full"?18:ViewKey=="wide"||WideLayout?12:6;
    private void LoadGridLayout()
    {
        try{if(File.Exists(GridFile)&&JsonSerializer.Deserialize<MonitorGridLayout>(File.ReadAllText(GridFile)) is {} grid&&grid.Columns==GridColumns&&grid.IsValid(Slots.Count))GridLayout=grid;}
        catch(Exception ex){InteractionDiagnostics.RecordFailure("Monitor grid",ex);}
        RebuildActiveSlots();
    }
    internal MonitorGridLayout CaptureGridLayout()
    {
        if(GridLayout is {} custom)return custom with{Cells=custom.Cells.ToArray()};
        int columns=GridColumns;
        var cells=ActiveSlots.Select((slot,i)=>{
            var bounds=MonitorSlotsPanel.LegacyCell(i,new Size(columns,12),WideLayout,SectionLayout,ActiveSlots.Count);
            int x=(int)Math.Round(bounds.X),y=(int)Math.Round(bounds.Y),right=(int)Math.Round(bounds.Right),bottom=(int)Math.Round(bounds.Bottom);
            return new MonitorGridCell(Slots.IndexOf(slot),x,y,Math.Max(1,right-x),Math.Max(2,bottom-y),slot.LargeGraph);
        }).ToArray();
        return new(columns,12,cells);
    }
    internal void ApplyGridLayout(MonitorGridLayout layout)
    {
        if(layout.Columns!=GridColumns||!layout.IsValid(Slots.Count))throw new ArgumentException("Invalid monitor grid");
        var copy=layout with{Cells=layout.Cells.ToArray()};
        AtomicJson.Write(GridFile,copy);GridLayout=copy;RebuildActiveSlots();
    }
    internal void ApplyEditorLayout(MonitorGridLayout layout,IReadOnlyDictionary<int,MetricKind?> sources)
    {
        if(layout.Columns!=GridColumns||!layout.IsValid(Slots.Count)||sources.Keys.Any(i=>i<0||i>=Slots.Count))throw new ArgumentException("Invalid monitor draft");
        ApplyGridLayout(layout);
        foreach(var (index,kind) in sources)Slots[index].SetContent(kind.HasValue?"sensor:"+kind.Value:null);
        SensorCellPreferences.ApplyPlacement(sources.ToDictionary(p=>CellLocation(Slots[p.Key]),p=>p.Value.HasValue?(MetricPresentation?)SensorFactoryDefaults.ForView(ViewKey,p.Value.Value):null));
        UpdateSensorActivity();RefreshValues();SaveSlots();
    }
    internal MonitorGridLayout DefaultGridLayout()
    {
        return FactoryPreset(selectedPreset).Grid;
    }
    internal MonitorGridEditor CreateLayoutEditor()=>new(this);
    internal static (int W,int H)[] NewSensorSizes(MetricKind? kind)=>kind is MetricKind.CpuStatus or MetricKind.GpuStatus or MetricKind.DiskStatus
        ?[(6,12),(6,8),(4,12),(4,8),(4,6),(2,4),(1,4),(2,2),(1,2)]
        :[(2,4),(1,4),(2,2),(1,2)];
    internal MonitorGridCell? FindFreeSensorCell()
    {
        var grid=CaptureGridLayout();
        int slot=Enumerable.Range(0,Slots.Count).Except(grid.Cells.Select(c=>c.Slot)).DefaultIfEmpty(-1).First();
        if(slot<0)return null;
        foreach(var size in new[]{(W:2,H:4),(W:1,H:4),(W:2,H:2),(W:1,H:2)})
            for(int y=0;y<=grid.Rows-size.H;y++)for(int x=0;x<=grid.Columns-size.W;x++)
            {var cell=new MonitorGridCell(slot,x,y,size.W,size.H);if(grid.Fits(cell,slot))return cell;}
        return null;
    }
    internal bool AddSensorInFreeSpace(MetricKind kind)
    {
        if(FindFreeSensorCell() is not {} cell)return false;
        var grid=CaptureGridLayout();
        foreach(var size in NewSensorSizes(kind))
        {
            var fit=(from y in Enumerable.Range(0,Math.Max(0,grid.Rows-size.H+1)) from x in Enumerable.Range(0,Math.Max(0,grid.Columns-size.W+1))
                     let c=cell with{X=x,Y=y,Width=size.W,Height=size.H} where grid.Fits(c,c.Slot) select c).FirstOrDefault();
            if(fit!=null){cell=fit;break;}
        }
        Slots[cell.Slot].IsActive=true;
        ApplyEditorLayout(grid with{Cells=[..grid.Cells,cell]},new Dictionary<int,MetricKind?>{{cell.Slot,kind}});
        return true;
    }
    internal void FitNewStatusCell(int slot,MetricKind kind)
    {
        if(kind is not (MetricKind.CpuStatus or MetricKind.GpuStatus or MetricKind.DiskStatus))return;
        var grid=CaptureGridLayout();var old=grid.Cells.FirstOrDefault(c=>c.Slot==slot);if(old==null)return;
        foreach(var size in NewSensorSizes(kind).Where(s=>s.W*s.H>old.Width*old.Height))
        {
            var fit=(from y in Enumerable.Range(0,Math.Max(0,grid.Rows-size.H+1)) from x in Enumerable.Range(0,Math.Max(0,grid.Columns-size.W+1))
                     let c=old with{X=x,Y=y,Width=size.W,Height=size.H} where grid.Fits(c,slot)
                     orderby Math.Abs(x-old.X)+Math.Abs(y-old.Y) select c).FirstOrDefault();
            if(fit!=null){ApplyGridLayout(grid.Move(fit));SaveSlots();return;}
        }
    }
    internal MonitorGridCell? FreeCellAt(Point point,Size area)
    {
        if(area.Width<=0||area.Height<=0||point.X<0||point.Y<0||point.X>=area.Width||point.Y>=area.Height)return null;
        var grid=CaptureGridLayout();int slot=Enumerable.Range(0,Slots.Count).Except(grid.Cells.Select(c=>c.Slot)).DefaultIfEmpty(-1).First();
        if(slot<0)return null;
        double gx=point.X/area.Width*grid.Columns,gy=point.Y/area.Height*grid.Rows;
        if(grid.Cells.Any(c=>c.Bounds.Contains(new Point(gx,gy))))return null;
        foreach(var size in new[]{(W:2,H:4),(W:1,H:4),(W:2,H:2),(W:1,H:2)})
        {
            var choices=from y in Enumerable.Range(Math.Max(0,(int)gy-size.H+1),size.H)
                        from x in Enumerable.Range(Math.Max(0,(int)gx-size.W+1),size.W)
                        let c=new MonitorGridCell(slot,x,y,size.W,size.H)
                        where c.Bounds.Contains(new Point(gx,gy))&&grid.Fits(c,slot)
                        orderby Math.Pow(c.X+c.Width/2d-gx,2)+Math.Pow(c.Y+c.Height/2d-gy,2)
                        select c;
            if(choices.FirstOrDefault() is {} cell)return cell;
        }
        return null;
    }
    internal bool AcceptFreeShortcutAt(Point point,Size area,ShortcutTransfer transfer)
    {
        if(ReferenceEquals(transfer.SourceOwner,this))return MoveCellAt(point,area,transfer);
        if(!SupportedMonitorTransfer(transfer)||!transfer.CanMoveTo(null)||FreeCellAt(point,area) is not {} cell)return false;
        var grid=CaptureGridLayout();
        if(transfer.Target==MacroLibraryCard.ClockTarget&&MonitorClockContent.Decode(transfer.Presentation).Span==2)
        {
            var larger=cell with{Width=4,Height=4};
            var fitted=(from y in Enumerable.Range(0,grid.Rows-3) from x in Enumerable.Range(0,Math.Max(0,grid.Columns-3)) let c=larger with{X=x,Y=y} where grid.Fits(c,c.Slot) orderby Math.Abs(x-cell.X)+Math.Abs(y-cell.Y) select c).FirstOrDefault();
            if(fitted==null)return false;cell=fitted;
        }
        cell=cell with{AutoShortcut=true};
        var slot=Slots[cell.Slot];
        slot.SetContent(null);slot.IsActive=true;
        ApplyGridLayout(grid with{Cells=[..grid.Cells,cell]});
        if(AcceptShortcutTransfer(slot,transfer))return true;
        ApplyGridLayout(grid);return false;
    }
    internal bool AcceptFreeShortcutFromWindow(Window owner,Point point,ShortcutTransfer transfer)=>AcceptFreeShortcutAt(owner.TranslatePoint(point,monitorCells),monitorCells.RenderSize,transfer);
    private MonitorGridCell? MovedCellAt(Point point,Size area,ShortcutTransfer transfer)
    {
        if(!ReferenceEquals(transfer.SourceOwner,this)||transfer.SourceSlot is not MonitorSlot source||!transfer.CanMoveTo(null)||area.Width<=0||area.Height<=0||point.X<0||point.Y<0||point.X>=area.Width||point.Y>=area.Height)return null;
        var grid=CaptureGridLayout();var original=grid.Cells.FirstOrDefault(c=>c.Slot==Slots.IndexOf(source));if(original==null)return null;
        var offset=transfer.SourceGridOffset??new Point(original.Width/2d,original.Height/2d);
        int x=(int)Math.Round(point.X/area.Width*grid.Columns-offset.X),y=(int)Math.Round(point.Y/area.Height*grid.Rows-offset.Y);
        return (from dy in Enumerable.Range(-1,3) from dx in Enumerable.Range(-1,3)
                let candidate=original with{X=x+dx,Y=y+dy}
                where grid.Fits(candidate,original.Slot)
                orderby dx*dx+dy*dy select candidate).FirstOrDefault();
    }
    private bool MoveCellAt(Point point,Size area,ShortcutTransfer transfer)
    {
        if(MovedCellAt(point,area,transfer) is not {} cell)return false;
        ApplyGridLayout(CaptureGridLayout().Move(cell));transfer.AcceptWithoutReplacement();SaveSlots();return true;
    }
    internal void DeleteMonitorCell(MonitorSlot slot)
    {
        int index=Slots.IndexOf(slot);if(index<0)return;slot.SetContent(null);var grid=CaptureGridLayout();ApplyGridLayout(grid with{Cells=grid.Cells.Where(c=>c.Slot!=index).ToArray()});UpdateSensorActivity();RefreshValues();SaveSlots();
    }
    internal void ClearMonitorCell(MonitorSlot slot)
    {
        int index=Slots.IndexOf(slot);if(index<0)return;
        var grid=CaptureGridLayout();bool remove=grid.Cells.Any(c=>c.Slot==index&&c.AutoShortcut);
        slot.SetContent(null);
        if(remove)ApplyGridLayout(grid with{Cells=grid.Cells.Where(c=>c.Slot!=index).ToArray()});
        UpdateSensorActivity();RefreshValues();SaveSlots();
    }
    private Point? DragGridOffset(MonitorSlot source)
    {
          var cell=liveGridStart is {} start&&start.Slot==Slots.IndexOf(source)?start:CaptureGridLayout().Cells.FirstOrDefault(c=>c.Slot==Slots.IndexOf(source));
        if(cell==null||monitorCells.ActualWidth<=0||monitorCells.ActualHeight<=0)return null;
        var point=TranslatePoint(dragStart,monitorCells);
        return new Point(point.X/monitorCells.ActualWidth*GridColumns-cell.X,point.Y/monitorCells.ActualHeight*12-cell.Y);
    }
    private void FreeSpace_DragOver(object sender,DragEventArgs e)
    {
        if(e.Handled)return;
        var transfer=ShortcutDragSupport.ResolveTransfer(e.Data);
        if(transfer!=null&&ReferenceEquals(transfer.SourceOwner,this)){e.Effects=MovedCellAt(e.GetPosition(monitorCells),monitorCells.RenderSize,transfer)!=null?DragDropEffects.Move:DragDropEffects.None;e.Handled=true;return;}
        bool supported=transfer!=null?SupportedMonitorTransfer(transfer)&&transfer.CanMoveTo(null):ExternalShortcut(e.Data)!=null;
        e.Effects=supported&&FreeCellAt(e.GetPosition(monitorCells),monitorCells.RenderSize)!=null?(transfer!=null?DragDropEffects.Move:DragDropEffects.Link):DragDropEffects.None;e.Handled=true;
    }
    private void FreeSpace_Drop(object sender,DragEventArgs e)
    {
        if(e.Handled)return;
        var transfer=ShortcutDragSupport.ResolveTransfer(e.Data);bool external=transfer==null;
        if(transfer==null&&ExternalShortcut(e.Data) is {} target)transfer=new(target,System.IO.Path.GetFileName(target),(_,_)=>{});
        e.Effects=transfer!=null&&AcceptFreeShortcutAt(e.GetPosition(monitorCells),monitorCells.RenderSize,transfer)?external?DragDropEffects.Link:DragDropEffects.Move:DragDropEffects.None;e.Handled=true;
    }
}

