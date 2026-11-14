using System.Windows;
namespace Ekrano_valdymas;

public sealed record MonitorGridCell(int Slot,int X,int Y,int Width,int Height,bool LargeGraph=false,bool AutoShortcut=false)
{
    internal Rect Bounds=>new(X,Y,Width,Height);
}
public sealed record MonitorGridLayout(int Columns,int Rows,MonitorGridCell[] Cells)
{
    internal bool IsValid(int slots)=>Columns is >=6 and <=18&&Rows==12&&Cells is not null&&Cells.Length<=slots&&
        Cells.Select(c=>c.Slot).Distinct().Count()==Cells.Length&&Cells.All(c=>c.Slot>=0&&c.Slot<slots&&Fits(c,c.Slot));
    internal bool Fits(MonitorGridCell cell,int except)=>cell.Width>=1&&cell.Width<=Columns&&cell.Height>=2&&cell.Height<=Rows&&cell.X>=0&&cell.Y>=0&&cell.X<=Columns-cell.Width&&cell.Y<=Rows-cell.Height&&
        Cells.Where(c=>c.Slot!=except).All(c=>{var overlap=Rect.Intersect(c.Bounds,cell.Bounds);return overlap.IsEmpty||overlap.Width==0||overlap.Height==0;});
    internal MonitorGridLayout Move(MonitorGridCell cell)=>this with{Cells=Cells.Select(c=>c.Slot==cell.Slot?cell:c).ToArray()};
}
