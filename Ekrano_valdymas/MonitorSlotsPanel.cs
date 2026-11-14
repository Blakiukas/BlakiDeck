using System.Windows;
using System.Windows.Controls;
namespace Ekrano_valdymas;
public sealed class MonitorSlotsPanel : Panel
{
    public static readonly DependencyProperty WideProperty=DependencyProperty.Register(nameof(Wide),typeof(bool),typeof(MonitorSlotsPanel),new FrameworkPropertyMetadata(false,FrameworkPropertyMetadataOptions.AffectsMeasure|FrameworkPropertyMetadataOptions.AffectsArrange));
    public bool Wide {get=>(bool)GetValue(WideProperty);set=>SetValue(WideProperty,value);}
    public static readonly DependencyProperty SectionsProperty=DependencyProperty.Register(nameof(Sections),typeof(string),typeof(MonitorSlotsPanel),new FrameworkPropertyMetadata("",FrameworkPropertyMetadataOptions.AffectsMeasure|FrameworkPropertyMetadataOptions.AffectsArrange));
    public string Sections {get=>(string)GetValue(SectionsProperty);set=>SetValue(SectionsProperty,value);}
    public static readonly DependencyProperty CustomProperty=DependencyProperty.Register(nameof(Custom),typeof(MonitorGridLayout),typeof(MonitorSlotsPanel),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsMeasure|FrameworkPropertyMetadataOptions.AffectsArrange));
    public MonitorGridLayout? Custom {get=>(MonitorGridLayout?)GetValue(CustomProperty);set=>SetValue(CustomProperty,value);}
    private Rect Cell(int i,Size size)
    {
        if(Custom is {} custom&&i<custom.Cells.Length){var cell=custom.Cells[i];return Snap(new Rect(cell.X*size.Width/custom.Columns,cell.Y*size.Height/custom.Rows,cell.Width*size.Width/custom.Columns,cell.Height*size.Height/custom.Rows));}
        var legacy=LegacyCell(i,size,Wide,Sections,InternalChildren.Count);return Sections.Length>0?Snap(legacy):legacy;
    }
    internal static Rect LegacyCell(int i,Size size,bool Wide,string Sections,int childCount)
    {
        if(Sections.Length>0){var parts=Sections.Split(',');int offset=0;for(int section=0;section<parts.Length;section++){int count=SystemMonitorWidget.SectionCount(parts[section]);if(i<offset+count){int n=i-offset,sectionColumns=parts[section]=="quad"?2:1,sectionRows=count/sectionColumns;double width=size.Width/parts.Length;return new Rect(section*width+n%sectionColumns*width/sectionColumns,n/sectionColumns*size.Height/sectionRows,width/sectionColumns,size.Height/sectionRows);}offset+=count;}return Rect.Empty;}
        if(Wide){if(i<2)return new(0,i*size.Height/2,size.Width/2,size.Height/2);int n=i-2;return new(size.Width/2+n%3*size.Width/6,n/3*size.Height/3,size.Width/6,size.Height/3);}
        int columns=childCount switch{<=1=>1,<=2=>2,4=>2,_=>3};int rows=(int)Math.Ceiling(childCount/(double)columns);
        return new(i%columns*size.Width/columns,i/columns*size.Height/rows,size.Width/columns,size.Height/rows);
    }
    private Rect Snap(Rect cell)
    {
        var dpi=System.Windows.Media.VisualTreeHelper.GetDpi(this);
        double left=Math.Floor(cell.Left*dpi.DpiScaleX+.5000001)/dpi.DpiScaleX,right=Math.Floor(cell.Right*dpi.DpiScaleX+.5000001)/dpi.DpiScaleX;
        double top=Math.Floor(cell.Top*dpi.DpiScaleY+.5000001)/dpi.DpiScaleY,bottom=Math.Floor(cell.Bottom*dpi.DpiScaleY+.5000001)/dpi.DpiScaleY;
        return new(left,top,right-left,bottom-top);
    }
    protected override Size MeasureOverride(Size size){size=new(double.IsFinite(size.Width)?size.Width:600,double.IsFinite(size.Height)?size.Height:330);for(int i=0;i<InternalChildren.Count;i++)InternalChildren[i].Measure(Cell(i,size).Size);return size;}
    protected override Size ArrangeOverride(Size size){for(int i=0;i<InternalChildren.Count;i++)InternalChildren[i].Arrange(Cell(i,size));return size;}
}
