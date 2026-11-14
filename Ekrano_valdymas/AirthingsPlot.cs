using System.Globalization;
using System.Windows;
using System.Windows.Media;
namespace Ekrano_valdymas;

internal sealed class AirthingsPlot:FrameworkElement
{
    private AirthingsPoint[] points=[];private double days=1;private Func<double,double> convert=x=>x;
    internal string Unit="";internal Brush Line=Brushes.DeepSkyBlue;
    internal Func<double,Brush>? DataBrush;
    internal FontFamily AxisFont=new("Segoe UI");internal double AxisSize=11;internal Brush? AxisBrush,GuideBrush;
    internal void Set(AirthingsPoint[] values,double range,Func<double,double> conversion,string unit){points=values;days=range;convert=conversion;Unit=unit;InvalidateVisual();}
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);double w=ActualWidth,h=ActualHeight;if(w<100||h<20)return;bool compact=h<70;
        var text=AxisBrush??Application.Current.TryFindResource("PanelTextBrush") as Brush??Brushes.White;
        var face=new Typeface(AxisFont,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal);double dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip;
        void Label(string label,double x,double y,bool right=false,bool center=false){var f=new FormattedText(label,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,face,AxisSize,text,dpi);var cap=new FormattedText("H",CultureInfo.CurrentCulture,FlowDirection.LeftToRight,face,AxisSize,text,dpi);double top=cap.BuildGeometry(new Point()).Bounds.Top;dc.DrawText(f,new Point(right?x-f.Width:x,center?y-(cap.Baseline+top)/2:y));}
        var end=DateTimeOffset.UtcNow;var start=end.AddDays(-days);var data=points.Where(p=>p.Time>=start&&p.Time<=end).ToArray();
        if(data.Length<2){Label(data.Length==0?"No stored history yet":"Collecting history · 1 sample",8,Math.Max(0,h/2-8));return;}
        double min=data.Min(p=>convert(p.Value)),max=data.Max(p=>convert(p.Value));double pad=Math.Max((max-min)*.1,Math.Max(1,Math.Abs(max)*.01));min-=pad;max+=pad;
        string Format(double v)=>Math.Abs(v)>=100?v.ToString("0"):v.ToString("0.##");
        double left=compact?5:Math.Max(44,new[]{Format(min),Format(max)}.Max(s=>new FormattedText(s,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,face,AxisSize,text,dpi).Width)+8),top=compact?4:Math.Max(8,AxisSize*.7),bottom=h-(compact?4:AxisSize*1.5+6),right=w-8;if(right<=left||bottom<=top)return;
        var guide=(GuideBrush??text).Clone();guide.Opacity=.25;var pen=new Pen(guide,1){DashStyle=DashStyles.Dot};for(int i=0;i<3;i++){double y=top+(bottom-top)*i/2;dc.DrawLine(pen,new(left,y),new(right,y));if(!compact)Label(Format(max-(max-min)*i/2),left-6,y,true,true);}
        if(!compact){Label(days>=7?start.ToLocalTime().ToString("MM-dd"):days>=1?start.ToLocalTime().ToString("MM-dd HH:mm"):start.ToLocalTime().ToString("HH:mm"),left,h-AxisSize*1.5-2);Label("Now",right,h-AxisSize*1.5-2,true);}
        double span=(end-start).TotalSeconds;Point Map(AirthingsPoint p)=>new(left+(p.Time-start).TotalSeconds/span*(right-left),top+(1-(convert(p.Value)-min)/(max-min))*(bottom-top));
        var steps=data.Zip(data.Skip(1),(a,b)=>(b.Time-a.Time).TotalSeconds).Where(x=>x>0).Order().ToArray();double gap=Math.Max(1200,steps.Length==0?1200:steps[steps.Length/2]*2.5);Point? previous=null;DateTimeOffset? last=null;
        foreach(var bucket in data.GroupBy(p=>(int)Map(p).X))foreach(var p in new[]{bucket.First(),bucket.MinBy(p=>p.Value)!,bucket.MaxBy(p=>p.Value)!,bucket.Last()}.Distinct().OrderBy(p=>p.Time)){var point=Map(p);var brush=DataBrush?.Invoke(p.Value)??Line;if(previous is {} before&&last is {} time&&(p.Time-time).TotalSeconds<=gap)dc.DrawLine(new Pen(brush,1.5),before,point);else dc.DrawEllipse(brush,null,point,1.5,1.5);previous=point;last=p.Time;}
    }
}
