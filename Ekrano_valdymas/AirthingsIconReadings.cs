using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Ekrano_valdymas;

// All four readings share one fit. Units use 70% of the numeric size and the same baseline.
internal sealed class AirthingsIconReadings : Panel
{
    private readonly double numberSize,iconSize;
    private readonly List<(FrameworkElement Icon,OpticallyCenteredText Number,OpticallyCenteredText Unit)> cells=[];
    internal AirthingsIconReadings(double numberSize,double iconSize){this.numberSize=numberSize;this.iconSize=iconSize;}
    internal void Add(string sensor,OpticallyCenteredText number)
    {
        string content=number.Text;int split=content.IndexOf(' ');
        number.Text=split<0?content:content[..split];
        var unit=new OpticallyCenteredText{Text=split<0?"":content[(split+1)..],TextFontFamily=number.TextFontFamily,TextFontWeight=number.TextFontWeight,TextBrush=number.TextBrush,Tag="AirthingsMiniUnit"};
        foreach(var text in new[]{number,unit}){text.FitToBox=false;text.CompactLineBox=true;text.CenterCapToBaseline=true;text.Ellipsis=false;text.InkAlignment=HorizontalAlignment.Left;}
        var color=AirthingsStatusRules.Canonical(sensor) switch{"temp"=>"#FFAA55","humidity"=>"#58B9FF","co2"=>"#53DCC1","voc"=>"#E8CA62","pressure"=>"#9FB8FF","mold"=>"#A7D878",_=>"#C39BFF"};
        var icon=new Viewbox{Child=AirthingsEditorVisuals.SensorIcon(sensor,24,new SolidColorBrush((Color)ColorConverter.ConvertFromString(color))),Stretch=Stretch.Uniform,Tag="AirthingsIconZone"};
        cells.Add((icon,number,unit));Children.Add(icon);Children.Add(number);Children.Add(unit);
    }
    private readonly record struct Metrics(double Width,double Cap,double Overflow);
    private Metrics MeasureText(OpticallyCenteredText text,double size)
    {
        var face=new Typeface(text.TextFontFamily,text.TextFontStyle,text.TextFontWeight,FontStretches.Normal);
        FormattedText Format(string value)=>new(value,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,face,size,text.TextBrush,VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var actual=Format(text.Text);var cap=Format("H");var ink=actual.BuildGeometry(new Point()).Bounds;double top=cap.BuildGeometry(new Point()).Bounds.Top;
        return new(ink.IsEmpty?0:ink.Width,cap.Baseline-top,1+(ink.IsEmpty?0:Math.Max(0,Math.Max(top-ink.Top,ink.Bottom-cap.Baseline))));
    }
    private readonly List<(Metrics Number,Metrics Unit,Size NumberBox,Size UnitBox)> fitted=[];
    private double fittedIcon;
    protected override Size MeasureOverride(Size available)
    {
        var size=new Size(double.IsFinite(available.Width)?available.Width:300,double.IsFinite(available.Height)?available.Height:100);
        double width=Math.Max(0,size.Width/2-7),height=Math.Max(0,size.Height/2-5);
        fittedIcon=Math.Max(0,Math.Min(iconSize,Math.Min(height,width*.25)));
        double textWidth=Math.Max(0,width-fittedIcon-6),scale=1;
        foreach(var cell in cells){var n=MeasureText(cell.Number,numberSize);var u=MeasureText(cell.Unit,numberSize*.7);scale=Math.Min(scale,Math.Min(Math.Max(0,textWidth-6)/Math.Max(1,n.Width+u.Width+4),height/Math.Max(1,n.Cap+2*Math.Max(n.Overflow,u.Overflow))));}
        scale=Math.Clamp(scale,.001,1);fitted.Clear();
        foreach(var cell in cells)
        {
            cell.Number.TextFontSize=numberSize*scale;cell.Unit.TextFontSize=numberSize*.7*scale;
            var n=MeasureText(cell.Number,cell.Number.TextFontSize);var u=MeasureText(cell.Unit,cell.Unit.TextFontSize);
            var ns=new Size(n.Width+2,n.Cap+2*n.Overflow);var us=new Size(u.Width+2,u.Cap+2*u.Overflow);
            cell.Icon.Measure(new Size(fittedIcon,fittedIcon));cell.Number.Measure(ns);cell.Unit.Measure(us);
            fitted.Add((n,u,ns,us));
        }
        return size;
    }
    protected override Size ArrangeOverride(Size size)
    {
        // Arrange must never change font properties or remeasure children:
        // fractional DPI rounding otherwise keeps invalidating the parent layout.
        double width=Math.Max(0,size.Width/2-6),height=Math.Max(0,size.Height/2-4),textWidth=Math.Max(0,width-fittedIcon-6);
        for(int i=0;i<Math.Min(cells.Count,fitted.Count);i++)
        {
            var cell=cells[i];var fit=fitted[i];var n=fit.Number;var u=fit.Unit;
            double x=i%2*size.Width/2+3,y=i/2*size.Height/2+2,baseline=y+(height-n.Cap)/2+n.Cap;
            cell.Icon.Arrange(new Rect(x,y+(height-fittedIcon)/2,fittedIcon,fittedIcon));
            double start=x+fittedIcon+6+Math.Max(0,(textWidth-(n.Width+u.Width+6))/2);
            cell.Number.Arrange(new Rect(start,baseline-n.Cap-n.Overflow,fit.NumberBox.Width,fit.NumberBox.Height));
            cell.Unit.Arrange(new Rect(start+n.Width+6,baseline-u.Cap-u.Overflow,fit.UnitBox.Width,fit.UnitBox.Height));
        }
        return size;
    }
}