using System.Windows;
using System.Windows.Media;
namespace Ekrano_valdymas;

internal static class RailGeometry
{
 // Used only by the isolated layer capture; normal rendering keeps all layers.
 internal static string ProbeLayer="all";
 internal static void CircularTop(StreamGeometryContext g,Point end,double radius=15)
  =>g.ArcTo(end,new Size(radius,radius),0,false,SweepDirection.Clockwise,true,false);
 internal const double OriginalBaseThickness=9.5;
 internal const double EdgeWidth=3;
 internal const double TabTopRadius=11;
 internal const double BaseThickness=14.25;
 internal static double BottomExtension=>BaseThickness-OriginalBaseThickness+CornerDrop;
 internal static double CornerDrop {get;set;}
 // The lower junction is an actual radius, matching the requested contour.
 // Keep concave feet distinct from the convex outside base corners.
 internal static void BlendCorner(StreamGeometryContext g,Point start,Point corner,Point end)
 {
  var incoming=corner-start;var outgoing=end-corner;
  double radius=incoming.Length;
  g.ArcTo(end,new Size(radius,radius),0,false,
   Vector.CrossProduct(incoming,outgoing)>0?SweepDirection.Clockwise:SweepDirection.Counterclockwise,true,false);
 }
 internal static Geometry OuterClip(double w,double h,bool roundTop=false)
 {
  double l=5,r=w-5,b=h+BottomExtension,c=15;
  var shape=new StreamGeometry();using(var g=shape.Open()){
   g.BeginFigure(new Point(l,0),true,true);g.LineTo(new Point(r,0),true,false);g.LineTo(new Point(r,b-c),true,false);
   BlendCorner(g,new Point(r,b-c),new Point(r,b),new Point(r-c,b));g.LineTo(new Point(l+c,b),true,false);
   BlendCorner(g,new Point(l+c,b),new Point(l,b),new Point(l,b-c));
  }if(roundTop)shape.Transform=new MatrixTransform(1,0,0,-1,0,h);shape.Freeze();return shape;
 }
}
internal sealed class MetalRailBase : System.Windows.Controls.Border
{
 internal MetalRailBase()=>CommonMetal.SetSurface(this,true);
 protected override void OnRender(DrawingContext dc){} // Painted once with the connected rail.
}

internal sealed class MetalMinimizeFace : FrameworkElement
{
 internal bool SquareOuterTop {get;set;}
 internal MetalMinimizeFace()=>CommonMetal.SetSurface(this,true);
 internal FrameworkElement? MaterialHost {get;set;}
 internal Geometry FaceShape(){
  double b=ActualHeight+RailGeometry.BottomExtension,join=ActualHeight-RailGeometry.OriginalBaseThickness;
  var rect=new Rect(-15,1,ActualWidth+15,b-1);
  var shape=new StreamGeometry();using(var g=shape.Open()){
   g.BeginFigure(new Point(-15,join),true,true);RailGeometry.BlendCorner(g,new Point(-15,join),new Point(0,join),new Point(0,join-15));g.LineTo(new Point(0,16),true,false);
   RailGeometry.CircularTop(g,new Point(15,1));
   g.LineTo(new Point(ActualWidth-15,1),true,false);
   if(SquareOuterTop){g.LineTo(new Point(ActualWidth,0),true,false);}else RailGeometry.CircularTop(g,new Point(ActualWidth,16));
   g.LineTo(new Point(ActualWidth,rect.Bottom),true,false);
   g.LineTo(new Point(-15,rect.Bottom),true,false);
  }shape.Freeze();return shape;
 }
 protected override void OnRender(DrawingContext dc){} // The connected rail owns its outline and fill.

}

// Shared rail geometry keeps the active card and its base one continuous face.
internal sealed class CatalogRailFace : FrameworkElement
{
 internal MetalRailEdge? ConnectedEdge {get;set;}
 internal bool SquareOuterTop {get;set;}
 internal FrameworkElement? MaterialHost {get;set;}
 internal CatalogRailFace()=>CommonMetal.SetSurface(this,true);
 private Brush Material(Geometry shape)=>CommonMetal.ForElement(this);
 internal int Selected {get;set;}
 internal int TabCount {get;set;}=5;
 internal Color Surface {get;set;}
 internal Brush?[] Materials {get;set;}=new Brush?[11];
 internal static Color Lighten(Color c,double amount)=>Color.FromRgb((byte)Math.Round(c.R+(255-c.R)*amount),(byte)Math.Round(c.G+(255-c.G)*amount),(byte)Math.Round(c.B+(255-c.B)*amount));
 protected override void OnRender(DrawingContext dc)
 {
  base.OnRender(dc);double w=ActualWidth,h=ActualHeight;if(w<50||h<20)return;
  int count=Math.Clamp(TabCount,1,11);
  double step=w/count,baseY=h-RailGeometry.OriginalBaseThickness;var body=Lighten(Surface,.17);
  // The full top bar owns the shared base; do not draw a second local strip.
  for(int i=0;i<count;i++){
   if(i==Selected||RailGeometry.ProbeLayer=="no-inactive")continue;
   var inactive=InactiveShape(i);
   // Render the complete plate behind MetalRailBase (Z=20), including its
   // 9.5-unit overlap. Clipping at the rail top leaves two antialiased edges
   // merely touching, which can expose a fractional-pixel slit.
   if(CommonMetal.NoSkin)dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(22,23,24)),null,inactive);
   else if(RailGeometry.ProbeLayer=="no-inactive-bevel")dc.DrawGeometry(Material(inactive),null,inactive);
   else LightMetalBevel.Draw(dc,inactive,this,RailGeometry.EdgeWidth,completeFace:true,luminance:130/255d);
   if(CommonMetal.NoSkin)
   {
    // A plate's sides continue behind the rail; no independent rounded bottom corners.
    dc.PushClip(inactive);
    dc.DrawGeometry(null,new Pen(new SolidColorBrush(Color.FromArgb(64,255,255,255)),3),inactive);
    dc.Pop();
   }
  }
  if(ConnectedEdge is { } edge&&edge.Minimize.ActualWidth>10&&edge.Base.ActualWidth>10){
   var shape=edge.Outline().Clone();var origin=edge.TranslatePoint(new Point(),this);shape.Transform=new TranslateTransform(origin.X,origin.Y);
   if(CommonMetal.NoSkin)dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(53,55,57)),null,shape);
   else if(RailGeometry.ProbeLayer=="no-unified-bevel")dc.DrawGeometry(Material(shape),null,shape);
   else LightMetalBevel.Draw(dc,shape,this,RailGeometry.EdgeWidth,completeFace:RailGeometry.ProbeLayer!="no-active-fill");
   if(CommonMetal.NoSkin)
   {
    // Outline the same union used by the metal face: no cross-line closes
    // the active tab at its junction with the lower rail.
    dc.PushClip(shape);
    dc.DrawGeometry(null,new Pen(new SolidColorBrush(Color.FromArgb(64,255,255,255)),3),shape);
    dc.Pop();
   }
  }
 }
 internal Geometry InactiveShape(int i){double step=ActualWidth/Math.Clamp(TabCount,1,11),baseY=ActualHeight,r=RailGeometry.TabTopRadius;
   double x=i==0?5:i*step+3,end=(i+1)*step-3,y=SquareOuterTop&&i==0?0:4;
   var inactive=new StreamGeometry();using(var g=inactive.Open()){
    g.BeginFigure(new Point(x,baseY),true,true);
    if(SquareOuterTop&&i==0){g.LineTo(new Point(x,y),true,false);g.LineTo(new Point(x+r,y),true,false);}else{g.LineTo(new Point(x,y+r),true,false);RailGeometry.CircularTop(g,new Point(x+r,y),r);}
    g.LineTo(new Point(end-r,y),true,false);RailGeometry.CircularTop(g,new Point(end,y+r),r);g.LineTo(new Point(end,baseY),true,false);
   }inactive.Freeze();return inactive;
 }
 internal Geometry ActiveShape(){double w=ActualWidth,h=ActualHeight,baseY=h-RailGeometry.OriginalBaseThickness,step=w/Math.Clamp(TabCount,1,11);
  double left=Selected==0?5:Selected*step+3,right=(Selected+1)*step-3,top=1,r=RailGeometry.TabTopRadius,bottom=h+RailGeometry.BottomExtension;
  var shape=new StreamGeometry();using(var g=shape.Open()){
   g.BeginFigure(new Point(Selected==0?left:left-15,bottom),true,true);
   if(Selected>0){g.LineTo(new Point(left-15,baseY),true,false);RailGeometry.BlendCorner(g,new Point(left-15,baseY),new Point(left,baseY),new Point(left,baseY-15));}
   if(SquareOuterTop&&Selected==0){g.LineTo(new Point(left,0),true,false);g.LineTo(new Point(left+r,top),true,false);}else{g.LineTo(new Point(left,top+r),true,false);RailGeometry.CircularTop(g,new Point(left+r,top),r);}
   g.LineTo(new Point(right-r,top),true,false);RailGeometry.CircularTop(g,new Point(right,top+r),r);
   g.LineTo(new Point(right,baseY-15),true,false);RailGeometry.BlendCorner(g,new Point(right,baseY-15),new Point(right,baseY),new Point(right+15,baseY));
   g.LineTo(new Point(right+15,bottom),true,false);g.LineTo(new Point(Selected==0?left:left-15,bottom),true,false);
  }shape.Freeze();return shape;
 }
}





// Mill the union once: no cut through the active tab/base join and no clipped corner strips.
internal sealed class MetalRailEdge : FrameworkElement
{
 internal CatalogRailFace Catalog=null!;
 internal MetalMinimizeFace Minimize=null!;
 internal MetalRailBase Base=null!;
 internal FrameworkElement Host=null!;
 internal Geometry Outline(){
  Geometry At(FrameworkElement e,Geometry g){var copy=g.Clone();var origin=e.TranslatePoint(new Point(),this);copy.Transform=new TranslateTransform(origin.X,origin.Y);return copy;}
  Geometry shape=At(Base,new RectangleGeometry(new Rect(0,0,Base.ActualWidth,Base.ActualHeight+RailGeometry.BottomExtension)));
  shape=Geometry.Combine(shape,At(Catalog,Catalog.ActiveShape()),GeometryCombineMode.Union,null,.05,ToleranceType.Absolute);
  shape=Geometry.Combine(shape,At(Minimize,Minimize.FaceShape()),GeometryCombineMode.Union,null,.05,ToleranceType.Absolute);
  if(Host.Clip!=null)shape=Geometry.Combine(shape,Host.Clip,GeometryCombineMode.Intersect,null,.05,ToleranceType.Absolute);
  shape.Freeze();return shape;
 }
 protected override void OnRender(DrawingContext dc){Catalog.InvalidateVisual();}
}



