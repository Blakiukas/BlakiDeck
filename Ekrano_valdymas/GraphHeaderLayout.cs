using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Ekrano_valdymas;

// Every header text shares the main reading's cap-to-baseline bottom. The
// right-aligned interval grows upward, without changing that line or the plot.
internal sealed class GraphHeaderLayout
{
    private readonly Dictionary<OpticallyCenteredText,Rect> boxes=new();
    private OpticallyCenteredText? interval;
    private Size measured;
    private double anchorReference;
    private OpticallyCenteredText? referenceText;
    private bool leftAnchored;
    private double ReferenceCenter(double headerHeight)
    {
        for(DependencyObject? node=referenceText;node!=null;node=VisualTreeHelper.GetParent(node))
            if(node is SensorTile tile)return tile.GraphReferenceCenter(headerHeight);
        return headerHeight/2;
    }

    private static double DefaultIntervalSize(DependencyObject text)
    {
        for(DependencyObject? node=text;node!=null;node=VisualTreeHelper.GetParent(node))if(node is QuickLaunchBar)return 8;
        return 10;
    }

    private static (double Width,double Reference,double Extent,double TopOverflow,double BottomOverflow) Metrics(OpticallyCenteredText text,double size)
    {
        var metrics=text.GetNaturalMetrics(size);
        if(text.Text.EndsWith('%')&&double.TryParse(text.Text.TrimEnd('%'),NumberStyles.Number,CultureInfo.CurrentCulture,out var percent)&&percent>=0&&percent<=100)
            metrics.Width=Math.Max(metrics.Width,text.GetValueRowMetrics(size,"100%").Width);
        return metrics;
    }

    internal Size Measure(Size available,IReadOnlyList<OpticallyCenteredText> main,IReadOnlyList<double> sizes,OpticallyCenteredText duration,double durationSize,bool autoFit,OpticallyCenteredText? hardware=null,double hardwareInset=4)
    {
        leftAnchored=main.Count>1&&main[0].Visibility!=Visibility.Visible&&main.Skip(1).Any(t=>t.Text.StartsWith("DL ")||t.Text.StartsWith("UP "));
        (double Width,double Reference,double Extent,double TopOverflow,double BottomOverflow) MainMetrics(OpticallyCenteredText text,double size)
        {
            var result=Metrics(text,size);
            if(leftAnchored&&(text.Text.StartsWith("DL ")||text.Text.StartsWith("UP ")))result.Width=Math.Max(result.Width,text.GetValueRowMetrics(size,text.Text[..3]+"8888 MB/s").Width);
            return result;
        }
        double width=double.IsFinite(available.Width)?available.Width:0;
        double height=double.IsFinite(available.Height)?available.Height:24;
        width=Math.Max(0,width);height=Math.Max(0,height);boxes.Clear();referenceText=main.FirstOrDefault();interval=duration;measured=new Size(width,height);
        const double gap=6,padding=4;

        var active=Enumerable.Range(0,main.Count).Where(i=>main[i].Visibility==Visibility.Visible).ToArray();
        var natural=active.Select(i=>MainMetrics(main[i],sizes[i])).ToArray();
        double verticalFit=autoFit&&natural.Length>0?Math.Min(1,Math.Max(1,height-4)/Math.Max(1,natural.Max(m=>m.Reference))):1;
        // Derive the line from configured main sizes before reserving interval
        // width. Width fitting shrinks upward from this line, so changing only
        // the interval size cannot shift the title/value baseline.
        anchorReference=natural.Select(m=>m.Reference*verticalFit).DefaultIfEmpty(0).Max();
        double layoutHeight=height;double center=ReferenceCenter(height);double bottom=center+anchorReference/2;
        height=Math.Max(height,center*2);

        duration.FitToBox=false;duration.MinWidth=0;duration.InkAlignment=HorizontalAlignment.Right;duration.UseLayoutRounding=false;
        duration.CenterCapToBaseline=true;
        var naturalInterval=Metrics(duration,durationSize);
        var defaultInterval=Metrics(duration,DefaultIntervalSize(duration));
        double defaultAboveFit=Math.Max(0,bottom-1)/Math.Max(.001,defaultInterval.Reference+defaultInterval.TopOverflow);
        double defaultBelowFit=defaultInterval.BottomOverflow>0?Math.Max(0,height-bottom-1)/defaultInterval.BottomOverflow:1;
        double defaultFit=Math.Max(.001,Math.Min(1,Math.Min(Math.Max(0,width-2)/Math.Max(1,defaultInterval.Width),Math.Min(defaultAboveFit,defaultBelowFit))));
        double defaultWidth=Math.Min(width,defaultInterval.Width*defaultFit+2);
        // A large interval must not consume the readings. Keep the allocation
        // that the main text has with the legacy interval size (8 in the bar,
        // 10 elsewhere). If the main text already fits its requested sizes,
        // its unused space remains available for upward interval growth.
        double naturalMainWidth=natural.Sum(m=>m.Width)*verticalFit+padding*active.Length+gap*Math.Max(0,active.Length-1);
        double protectedMainWidth=active.Length==0?0:Math.Min(naturalMainWidth,Math.Max(0,width-defaultWidth-gap));
        double intervalBudget=Math.Max(0,width-protectedMainWidth-(active.Length>0?gap:0));
        // Actual digit/capital overshoot and descenders need drawing space,
        // but never become the alignment reference. The interval alone fits
        // the width and the space above/below the locked main baseline.
        double aboveFit=Math.Max(0,bottom-1)/Math.Max(.001,naturalInterval.Reference+naturalInterval.TopOverflow);
        double belowFit=naturalInterval.BottomOverflow>0?Math.Max(0,height-bottom-1)/naturalInterval.BottomOverflow:1;
        double intervalFit=Math.Max(.001,Math.Min(1,Math.Min(Math.Max(0,intervalBudget-2)/Math.Max(1,naturalInterval.Width),Math.Min(aboveFit,belowFit))));
        duration.TextFontSize=Math.Max(.1,durationSize*intervalFit);
        var intervalMetrics=Metrics(duration,duration.TextFontSize);
        double intervalWidth=Math.Min(intervalBudget,intervalMetrics.Width+2);
        boxes[duration]=new Rect(Math.Max(0,width-intervalWidth),0,intervalWidth,Math.Max(0,bottom));

        double inset=leftAnchored?Math.Min(16,width*.04):0;
        double mainWidth=Math.Max(0,width-intervalWidth-(active.Length>0?gap:0)-inset);
        if(active.Length>0)
        {
            double factor=autoFit?Math.Max(.001,Math.Min(verticalFit,Math.Max(0,mainWidth-gap*(active.Length-1)-padding*active.Length)/Math.Max(1,natural.Sum(m=>m.Width)))):1;
            foreach(int i in active){main[i].FitToBox=false;main[i].UseLayoutRounding=false;main[i].TextFontSize=Math.Max(.1,sizes[i]*factor);}
            var metrics=active.Select(i=>MainMetrics(main[i],main[i].TextFontSize)).ToArray();
            double naturalBoxes=metrics.Sum(m=>m.Width+padding),textBudget=Math.Max(0,mainWidth-gap*(active.Length-1));
            double boxFit=Math.Min(1,textBudget/Math.Max(.001,naturalBoxes));
            double group=naturalBoxes*boxFit+gap*(active.Length-1),left=leftAnchored?inset:Math.Max(0,(mainWidth-group)/2);
            for(int n=0;n<active.Length;n++)
            {
                // With auto-fit off, retain the requested fonts and trim each
                // allocated text box instead of giving the first title every
                // pixel and dropping the value altogether.
                var text=main[active[n]];text.InkAlignment=leftAnchored?HorizontalAlignment.Left:HorizontalAlignment.Center;double extent=Math.Max(0,(metrics[n].Width+padding)*boxFit);
                boxes[text]=new Rect(left,0,extent,Math.Max(0,bottom));left+=extent+gap;
            }
        }
        foreach(var text in main.Where(t=>t.Visibility!=Visibility.Visible))boxes[text]=new Rect(0,0,0,0);
        if(hardware?.Visibility==Visibility.Visible)
        {
            // Use only the empty space before the centered title/value group.
            // The model cannot shrink the reading or move its baseline.
            double edge=main.Where(t=>t.Visibility==Visibility.Visible&&boxes.ContainsKey(t)).Select(t=>boxes[t].X).DefaultIfEmpty(width-intervalWidth).Min();
            hardware.TextFontSize=Math.Min(14,Math.Max(9,sizes[0]*.65));hardware.FitToBox=false;hardware.InkAlignment=HorizontalAlignment.Left;hardware.CenterCapToBaseline=true;
            boxes[hardware]=new Rect(hardwareInset,0,Math.Max(0,edge-hardwareInset-6),Math.Max(0,bottom));
            // On narrow graphs share the title space with the model, while
            // protecting the reading's complete 100% width at its chosen size.
            var hm=Metrics(hardware,hardware.TextFontSize);
            if(boxes[hardware].Width<hm.Width+padding&&main.Count==2&&active.Contains(1))
            {
                double availableLine=Math.Max(0,width-intervalWidth-gap-hardwareInset);
                double valueSize=sizes[1]*verticalFit;
                var vm=MainMetrics(main[1],valueSize);
                double valueWidth=Math.Min(availableLine,vm.Width+padding);
                main[1].TextFontSize=valueSize;main[1].InkAlignment=HorizontalAlignment.Center;
                double textRoom=Math.Max(0,availableLine-valueWidth-gap*(active.Contains(0)?2:1));
                double titleSize=sizes[0]*verticalFit;
                var tm=Metrics(main[0],titleSize);
                double titleNatural=active.Contains(0)?tm.Width+padding:0;
                double fit=Math.Min(1,textRoom/Math.Max(1,hm.Width+padding+titleNatural));
                double modelWidth=(hm.Width+padding)*fit;
                hardware.TextFontSize=Math.Max(9,hardware.TextFontSize*fit);
                boxes[hardware]=new Rect(hardwareInset,0,modelWidth,Math.Max(0,bottom));
                double x=hardwareInset+modelWidth+gap;
                if(active.Contains(0)){main[0].TextFontSize=Math.Max(Math.Min(12,titleSize),titleSize*fit);boxes[main[0]]=new Rect(x,0,titleNatural*fit,Math.Max(0,bottom));x+=titleNatural*fit+gap;}
                boxes[main[1]]=new Rect(x,0,valueWidth,Math.Max(0,bottom));
            }
        }
        foreach(var pair in boxes)pair.Key.Measure(pair.Value.Size);
        return new Size(width,layoutHeight);
    }

    internal Size Arrange(Size size)
    {
        foreach(var pair in boxes)
        {
            bool corner=pair.Key==interval;
            bool alignmentChanged=pair.Key.InkVerticalAlignment!=VerticalAlignment.Bottom;
            pair.Key.InkVerticalAlignment=VerticalAlignment.Bottom;
            // Grid's final arranged width can differ from its measure width
            // after layout rounding. The corner belongs to the final viewport.
            double bottom=Math.Max(0,ReferenceCenter(size.Height)+anchorReference/2);
            var box=corner?new Rect(Math.Max(0,size.Width-pair.Value.Width),0,pair.Value.Width,bottom):new Rect(Math.Max(0,pair.Value.X+(leftAnchored?0:(size.Width-measured.Width)/2)),0,pair.Value.Width,bottom);
            pair.Key.Arrange(box);
            if(alignmentChanged||pair.Key.TextRenderInputsChanged())pair.Key.InvalidateVisual();
        }
        return size;
    }
}
