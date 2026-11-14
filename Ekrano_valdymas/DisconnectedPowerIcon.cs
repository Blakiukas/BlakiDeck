using System.Windows;
using System.Windows.Media;
namespace Ekrano_valdymas;

// Two separated connector halves. Geometry is symmetric around (16,16).
public sealed class DisconnectedPowerIcon : FrameworkElement
{
    protected override void OnRender(DrawingContext dc)
    {
        var brush = TryFindResource("PanelTextBrush") as Brush ?? Brushes.White;
        var pen = new Pen(brush,2) { StartLineCap=PenLineCap.Round, EndLineCap=PenLineCap.Round, LineJoin=PenLineJoin.Round };
        double scale=Math.Min(ActualWidth,ActualHeight)/32;
        dc.PushTransform(new TranslateTransform((ActualWidth-32*scale)/2,(ActualHeight-32*scale)/2));
        dc.PushTransform(new ScaleTransform(scale,scale));
        dc.PushTransform(new RotateTransform(-35,16,16));
        dc.DrawGeometry(null,pen,Geometry.Parse("M 2,16 L 6,16 M 6,10 L 6,22 L 11,22 L 11,10 Z M 11,13 L 14,13 M 11,19 L 14,19 M 21,10 L 21,22 L 26,22 L 26,10 Z M 26,16 L 30,16 M 16,5 L 18,8 M 16,27 L 18,24"));
        dc.Pop();dc.Pop();dc.Pop();
    }
}
