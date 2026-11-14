using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ekrano_valdymas;

// Numeric DL/UP share a single row and optical bottom, with independent colors.
internal sealed class NetworkValueRow:Grid
{
    private OpticallyCenteredText[] texts=[];
    private double[] sizes=[];
    private bool fit=true;
    private readonly Dictionary<OpticallyCenteredText,Rect> boxes=new();
    internal void Configure(OpticallyCenteredText[] values,double[] fontSizes,bool autoFit)
    {texts=values;sizes=fontSizes;fit=autoFit;InvalidateMeasure();}
    private (double Width,double Reference,double Extent) Metrics(OpticallyCenteredText text,double size)
    {
        var face=new Typeface(text.TextFontFamily,text.TextFontStyle,text.TextFontWeight,FontStretches.Normal);
        FormattedText Format(string content)=>new(content,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,face,size,Brushes.White,VisualTreeHelper.GetDpi(text).PixelsPerDip);
        var formatted=Format(OpticallyCenteredText.IsNetworkRate(text.Text)?text.Text[..3]+new string('8',Math.Max(4,text.Text.Split(' ',StringSplitOptions.RemoveEmptyEntries)[1].Count(char.IsDigit)))+". MB/s":text.Text);var cap=Format("H");var ink=formatted.BuildGeometry(new Point()).Bounds;double top=cap.BuildGeometry(new Point()).Bounds.Top;
        double overflow=ink.IsEmpty?0:Math.Max(0,Math.Max(top-ink.Top,ink.Bottom-cap.Baseline));
        return(Math.Max(formatted.WidthIncludingTrailingWhitespace,ink.IsEmpty?0:ink.Width),cap.Baseline-top,cap.Baseline-top+overflow*2);
    }
    protected override Size MeasureOverride(Size available)
    {
        double width=Math.Max(0,double.IsFinite(available.Width)?available.Width:0),height=Math.Max(0,double.IsFinite(available.Height)?available.Height:24);boxes.Clear();
        if(texts.Length==0)return new(width,height);
        const double gap=8,padding=4;
        var natural=texts.Select((t,i)=>Metrics(t,sizes[i])).ToArray();
        double factor=fit?Math.Max(.001,Math.Min(1,Math.Min(Math.Max(0,width-gap*(texts.Length-1)-padding*texts.Length)/Math.Max(1,natural.Sum(m=>m.Width)),Math.Max(1,height-4)/Math.Max(1,natural.Max(m=>m.Extent))))):1;
        for(int i=0;i<texts.Length;i++){texts[i].FitToBox=false;texts[i].InkAlignment=HorizontalAlignment.Left;texts[i].TextFontSize=Math.Max(.1,sizes[i]*factor);}
        var metrics=texts.Select(t=>Metrics(t,t.TextFontSize)).ToArray();
        double reference=metrics.Max(m=>m.Reference),bottom=(height+reference)/2;
        double group=metrics.Sum(m=>m.Width+padding)+gap*(texts.Length-1),left=Math.Max(0,(width-group)/2);
        for(int i=0;i<texts.Length;i++){double extent=Math.Max(0,Math.Min(metrics[i].Width+padding,width-left));boxes[texts[i]]=new Rect(left,0,extent,Math.Max(0,bottom));texts[i].Measure(boxes[texts[i]].Size);left+=extent+gap;}
        return new(width,height);
    }
    protected override Size ArrangeOverride(Size size)
    {
        foreach(var pair in boxes){pair.Key.InkVerticalAlignment=VerticalAlignment.Bottom;pair.Key.Arrange(pair.Value);pair.Key.InvalidateVisual();}
        return size;
    }
}
