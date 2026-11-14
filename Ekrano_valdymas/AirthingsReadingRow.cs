using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ekrano_valdymas;

/// <summary>One nominal size per text, one shared fit, and a common cap-to-baseline bottom.</summary>
internal sealed class AirthingsReadingRow:Panel
{
    internal OpticallyCenteredText Label {get;}
    internal OpticallyCenteredText Value {get;}
    internal double LabelSize {get;}
    internal double ValueSize {get;}
    internal double RenderedScale {get;private set;}=1;
    private readonly AirthingsReadingLayoutSlot layoutSlot=new();
    private Metrics measuredLabel,measuredValue;
    private const double Gap=8;

    internal AirthingsReadingRow(OpticallyCenteredText label,OpticallyCenteredText value,double labelSize,double valueSize)
    {
        Label=label;Value=value;LabelSize=labelSize;ValueSize=valueSize;
        UseLayoutRounding=false;Label.UseLayoutRounding=Value.UseLayoutRounding=false;
        Label.FitToBox=Value.FitToBox=false;
        Label.CompactLineBox=Value.CompactLineBox=true;
        Label.CenterCapToBaseline=Value.CenterCapToBaseline=true;
        Children.Add(Label);Children.Add(Value);
    }
    private readonly record struct Metrics(double Width,double CapHeight,double Overflow);
    private Metrics Get(OpticallyCenteredText text,double size)
    {
        var face=new Typeface(text.TextFontFamily,text.TextFontStyle,text.TextFontWeight,FontStretches.Normal);
        double dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var actual=new FormattedText(text.Text??"",CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,face,size,text.TextBrush,dpi);
        var cap=new FormattedText("H",CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,face,size,text.TextBrush,dpi);
        Rect ink=actual.BuildGeometry(new Point()).Bounds,reference=cap.BuildGeometry(new Point()).Bounds;
        double top=reference.IsEmpty?cap.Baseline-size:reference.Top;
        // Descenders/units get drawing room; they never define the alignment reference.
        double overflow=ink.IsEmpty?0:Math.Max(0,Math.Max(top-ink.Top,ink.Bottom-cap.Baseline));
        return new(ink.IsEmpty?0:ink.Width,cap.Baseline-top,overflow+1);
    }
    protected override Size MeasureOverride(Size available)
    {
        var label=Get(Label,LabelSize);var value=Get(Value,ValueSize);
        double width=label.Width+value.Width+Gap+4,height=Math.Max(label.CapHeight,value.CapHeight)+2*Math.Max(label.Overflow,value.Overflow);
        var desired=new Size(Math.Max(0,double.IsFinite(available.Width)?Math.Min(width,available.Width):width),Math.Max(0,double.IsFinite(available.Height)?Math.Min(height,available.Height):height));
        var size=layoutSlot.MeasureSize(available,desired);
        RenderedScale=Math.Clamp(Math.Min(1,Math.Min(Math.Max(0,size.Width-Gap-4)/Math.Max(1,label.Width+value.Width),size.Height/Math.Max(1,height))),.0001,1);
        Label.TextFontSize=LabelSize*RenderedScale;Value.TextFontSize=ValueSize*RenderedScale;
        measuredLabel=Get(Label,Label.TextFontSize);measuredValue=Get(Value,Value.TextFontSize);
        Label.Measure(TextSize(measuredLabel));Value.Measure(TextSize(measuredValue));
        return desired;
    }
    private static Size TextSize(Metrics metrics)=>new(metrics.Width+2,metrics.CapHeight+2*metrics.Overflow);
    protected override Size ArrangeOverride(Size size)
    {
        var label=measuredLabel;var value=measuredValue;
        double capHeight=Math.Max(label.CapHeight,value.CapHeight),baseline=(size.Height-capHeight)/2+capHeight;
        void Place(OpticallyCenteredText text,Metrics metrics,double x)
        {
            text.Arrange(new Rect(new Point(x,baseline-metrics.CapHeight-metrics.Overflow),TextSize(metrics)));
        }
        Place(Label,label,0);Place(Value,value,Math.Max(label.Width+Gap, size.Width-value.Width-2));
        if(layoutSlot.ObserveArrange(size))InvalidateMeasure();
        return size;
    }
}

/// <summary>Stacked cards preserve their 22-DIP label row when it fits, then fit both nominal sizes together.</summary>
internal sealed class AirthingsReadingStack:Panel
{
    internal OpticallyCenteredText Label {get;}
    internal OpticallyCenteredText Value {get;}
    internal double LabelSize {get;}
    internal double ValueSize {get;}
    internal double RenderedScale {get;private set;}=1;
    private readonly AirthingsReadingLayoutSlot layoutSlot=new();
    private double measuredTitleHeight;
    private readonly double labelRowReference;
    internal AirthingsReadingStack(OpticallyCenteredText label,OpticallyCenteredText value,double labelSize,double valueSize,double labelRowReference=22)
    {
        Label=label;Value=value;LabelSize=labelSize;ValueSize=valueSize;this.labelRowReference=labelRowReference;
        UseLayoutRounding=false;Label.UseLayoutRounding=Value.UseLayoutRounding=false;
        foreach(var text in new[]{Label,Value}){text.FitToBox=false;text.CompactLineBox=true;text.CenterCapToBaseline=true;text.InkAlignment=HorizontalAlignment.Center;Children.Add(text);}
    }
    private Size Metrics(OpticallyCenteredText text,double size)
    {
        var face=new Typeface(text.TextFontFamily,text.TextFontStyle,text.TextFontWeight,FontStretches.Normal);
        double dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var actual=new FormattedText(text.Text,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,face,size,text.TextBrush,dpi);
        var cap=new FormattedText("H",CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,face,size,text.TextBrush,dpi);
        var ink=actual.BuildGeometry(new Point()).Bounds;double top=cap.BuildGeometry(new Point()).Bounds.Top;
        double overflow=ink.IsEmpty?0:Math.Max(0,Math.Max(top-ink.Top,ink.Bottom-cap.Baseline));
        return new Size((ink.IsEmpty?0:ink.Width)+2,Math.Max(size*1.18,cap.Baseline-top+2*overflow+2));
    }
    protected override Size MeasureOverride(Size available)
    {
        var label=Metrics(Label,LabelSize);var value=Metrics(Value,ValueSize);
        double title=Math.Max(labelRowReference,Math.Max(LabelSize*1.2,label.Height));
        var desired=new Size(Math.Min(available.Width,Math.Max(label.Width,value.Width)+4),Math.Min(available.Height,title+value.Height));
        var size=layoutSlot.MeasureSize(available,desired);
        RenderedScale=Math.Clamp(Math.Min(1,Math.Min(Math.Max(0,size.Width-4)/Math.Max(1,Math.Max(label.Width,value.Width)),size.Height/Math.Max(1,title+value.Height))),.0001,1);
        Label.TextFontSize=LabelSize*RenderedScale;Value.TextFontSize=ValueSize*RenderedScale;
        measuredTitleHeight=Math.Min(size.Height,title*RenderedScale);
        Label.Measure(new Size(size.Width,measuredTitleHeight));Value.Measure(new Size(size.Width,Math.Max(0,size.Height-measuredTitleHeight)));
        return desired;
    }
    protected override Size ArrangeOverride(Size size)
    {
        double labelHeight=Math.Min(size.Height,measuredTitleHeight);
        var labelBox=new Rect(0,0,size.Width,labelHeight);var valueBox=new Rect(0,labelHeight,size.Width,Math.Max(0,size.Height-labelHeight));
        Label.Arrange(labelBox);Value.Arrange(valueBox);
        if(layoutSlot.ObserveArrange(size))InvalidateMeasure();
        return size;
    }
}

/// <summary>The historical period owns a fixed right corner independent of changing live values.</summary>
internal sealed class AirthingsGraphHeader:Panel
{
    internal AirthingsReadingRow Reading {get;}
    internal OpticallyCenteredText Period {get;}
    private readonly double periodSize;
    private readonly AirthingsReadingLayoutSlot layoutSlot=new();
    private double measuredPeriodWidth;
    private const double Gap=10;
    internal AirthingsGraphHeader(AirthingsReadingRow reading,OpticallyCenteredText period)
    {UseLayoutRounding=false;Reading=reading;Period=period;Period.UseLayoutRounding=false;periodSize=period.TextFontSize;Children.Add(Reading);Children.Add(Period);}
    private Size PeriodMetrics(double size)
    {
        var formatted=new FormattedText(Period.Text,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface(Period.TextFontFamily,Period.TextFontStyle,Period.TextFontWeight,FontStretches.Normal),size,Period.TextBrush,VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var ink=formatted.BuildGeometry(new Point()).Bounds;
        return new Size((ink.IsEmpty?0:ink.Width)+2,Math.Max(size*1.18,(ink.IsEmpty?0:ink.Height)+2));
    }
    protected override Size MeasureOverride(Size available)
    {
        var natural=PeriodMetrics(periodSize);
        double width=Math.Max(0,available.Width-natural.Width-Gap);
        Reading.Measure(new Size(width,available.Height));
        var desired=new Size(Math.Min(available.Width,Reading.DesiredSize.Width+natural.Width+Gap),Math.Min(available.Height,Math.Max(Reading.DesiredSize.Height,natural.Height)));
        var size=layoutSlot.MeasureSize(available,desired);
        // This fit depends solely on the available corner, never the changing live reading.
        double fit=Math.Clamp(Math.Min(1,Math.Min(size.Width/Math.Max(1,natural.Width),size.Height/Math.Max(1,natural.Height))),.0001,1);
        Period.TextFontSize=periodSize*fit;
        measuredPeriodWidth=Math.Min(size.Width,PeriodMetrics(Period.TextFontSize).Width);
        Period.Measure(new Size(measuredPeriodWidth,size.Height));
        return desired;
    }
    protected override Size ArrangeOverride(Size size)
    {
        double width=Math.Min(size.Width,measuredPeriodWidth);
        Period.Arrange(new Rect(Math.Max(0,size.Width-width),0,width,size.Height));
        Reading.Arrange(new Rect(0,0,Math.Max(0,size.Width-width-Gap),size.Height));
        if(layoutSlot.ObserveArrange(size))InvalidateMeasure();
        return size;
    }
}

/// <summary>Fit against the actual slot without making fitted text change its parent's desired size.</summary>
internal sealed class AirthingsReadingLayoutSlot
{
    private Size? arrangedSize;
    private Size measuredSize;
    internal Size MeasureSize(Size available,Size desired)
    {
        // Grid/ScrollViewer may measure a provisional or infinite extent before
        // assigning a finite cell. Retain that final slot across provisional
        // measure passes; only the natural content determines DesiredSize.
        measuredSize=arrangedSize??new Size(double.IsFinite(available.Width)?available.Width:desired.Width,double.IsFinite(available.Height)?available.Height:desired.Height);
        return measuredSize;
    }
    internal bool ObserveArrange(Size size)
    {
        arrangedSize=size;
        // A new slot needs one measure pass for its font fit. Arrangement never
        // changes font properties or measures children, so an unchanged slot
        // cannot restart the former child-desired-size feedback loop.
        return Math.Abs(size.Width-measuredSize.Width)>0.0000001||Math.Abs(size.Height-measuredSize.Height)>0.0000001;
    }
}
