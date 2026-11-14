using System.Windows;
using System.Windows.Media;
namespace Ekrano_valdymas;
// Vector header badges remain sharp at touchscreen scaling without raster exports.
public sealed class ModuleHeaderIcon : FrameworkElement
{
 public string Kind {get;set;}="Macro";
 protected override void OnRender(DrawingContext dc)
 {
  double size=Math.Min(ActualWidth,ActualHeight);if(size<=0)return;
  dc.PushTransform(new TranslateTransform((ActualWidth-size)/2,(ActualHeight-size)/2));dc.PushTransform(new ScaleTransform(size/32,size/32));
  bool link=Kind=="Link";
  Color accent=link?Color.FromRgb(49,178,244):Color.FromRgb(168,112,248);
  var fill=new LinearGradientBrush(link?Color.FromRgb(24,94,143):Color.FromRgb(78,42,130),Color.FromRgb(12,19,30),90);
  dc.DrawEllipse(fill,new Pen(new SolidColorBrush(accent),1.2),new Point(16,16),14,14);
  dc.DrawEllipse(null,new Pen(new SolidColorBrush(Color.FromArgb(70,255,255,255)),.7),new Point(16,16),12.3,12.3);
  var pen=new Pen(new SolidColorBrush(link?Color.FromRgb(178,231,255):Color.FromRgb(228,211,255)),2){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};
  if(link) {
   // Two closed, overlapping chain links, rotated as one centered symbol.
   dc.PushTransform(new RotateTransform(42,16,16));
   dc.DrawRoundedRectangle(null,pen,new Rect(11,3,10,16),5,5);
   dc.DrawRoundedRectangle(null,pen,new Rect(11,13,10,16),5,5);
   dc.Pop();
  } else {
   dc.DrawGeometry(null,pen,Geometry.Parse("M 10,10 L 22,10 L 22,16 L 10,16 L 10,22 L 22,22"));
   foreach(var p in new[]{new Point(10,10),new Point(22,16),new Point(10,22)})dc.DrawRoundedRectangle(new SolidColorBrush(accent),pen,new Rect(p.X-2.5,p.Y-2.5,5,5),1,1);
  }
  dc.Pop();dc.Pop();
 }
}
