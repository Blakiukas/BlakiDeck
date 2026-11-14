using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ekrano_valdymas;

// Tab-only layout: visible text grows upward from a fixed ink bottom. Icons
// retain their independent upper row and use only the space above that text.
internal sealed class TabInkLayout:Panel
{
    internal required OpticallyCenteredText Label;
    internal FrameworkElement? Icon;
    internal double RequestedSize;
    internal required Func<double> IconLimit;
    internal required Func<double> Bottom;
    internal required Func<double> IconTop;
    internal long MeasurePasses {get;private set;}
    internal Action<Size>? DiagnosticIconArranged;
    private Rect labelBox,iconBox;
    protected override Geometry? GetLayoutClip(Size layoutSlotSize)=>null;
    protected override Size MeasureOverride(Size available)
    {
        MeasurePasses++;
        var size=new Size(double.IsFinite(available.Width)?available.Width:0,double.IsFinite(available.Height)?available.Height:0);
        double width=Math.Max(1,size.Width-10),bottom=Math.Clamp(Bottom(),0,size.Height),limit=Math.Max(1,IconLimit());
        double top=Math.Clamp(IconTop(),0,bottom),contentHeight=bottom-top;
        double labelHeight=0;
        if(Children.Contains(Label))
        {
            double font=RequestedSize;Rect ink=TextInk(Label,font,width);
            double maxHeight=Icon==null?Math.Max(1,contentHeight):Math.Min(limit,Math.Max(1,contentHeight-4));
            for(int pass=0;pass<5&&!ink.IsEmpty&&(ink.Height>maxHeight+.001||ink.Width>width+.001);pass++)
            {font*=Math.Min(maxHeight/ink.Height,width/ink.Width);ink=TextInk(Label,font,width);}
            Label.TextFontSize=font;Label.LineAdvance=font*1.15;Label.InkVerticalAlignment=Icon==null?VerticalAlignment.Center:VerticalAlignment.Bottom;
            labelHeight=ink.IsEmpty?0:ink.Height;
            labelBox=new Rect(5,top,width,contentHeight);
            Label.Measure(labelBox.Size);
        }
        if(Icon!=null)
        {
            // Startup can measure an icon/text tab before its rail has any height.
            double iconBottom=Math.Max(0,Children.Contains(Label)?bottom-labelHeight-4:bottom);
            double iconTop=Math.Clamp(IconTop(),0,iconBottom);
            double height=Math.Max(0,iconBottom-iconTop);
            if(Icon is OpticallyCenteredText glyph)
            {
                // A temporary zero-height slot must not pass an invalid font
                // size to FormattedText. The arranged box still clips to zero.
                var ink=TextInk(glyph,80,width);glyph.TextFontSize=ink.IsEmpty?10:Math.Max(.1,80*Math.Min(height/ink.Height,width/ink.Width));
                glyph.InkVerticalAlignment=VerticalAlignment.Center;
            }
            iconBox=new Rect(5,iconTop,width,height);
            Icon.Measure(iconBox.Size);
        }
        return size;
    }
    protected override Size ArrangeOverride(Size size)
    {
        // Never remeasure children or change their font during Arrange: doing so
        // repeatedly invalidates the parent and starves other windows' layout.
        if(Children.Contains(Label))Label.Arrange(labelBox);
        Icon?.Arrange(iconBox);
        if(Icon!=null)DiagnosticIconArranged?.Invoke(iconBox.Size);
        return size;
    }
    private static Rect TextInk(OpticallyCenteredText text,double font,double width)
    {
        Rect result=Rect.Empty;int i=0;
        foreach(string line in text.Text.Replace("\r","").Split('\n'))
        {
            var ft=new FormattedText(line,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface(text.TextFontFamily,text.TextFontStyle,text.TextFontWeight,FontStretches.Normal),font,Brushes.White,VisualTreeHelper.GetDpi(text).PixelsPerDip);
            result.Union(ft.BuildGeometry(new Point(0,i++*font*1.15)).Bounds);
        }
        return result;
    }
}

