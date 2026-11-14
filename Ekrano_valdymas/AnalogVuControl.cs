using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Ekrano_valdymas;
// The finished console VU, reused by the independent card and analog modes.
public sealed class AnalogVuControl:UserControl
{
    internal Canvas Surface {get;}=new(){Width=835,Height=335};
    private readonly Viewbox viewbox;
    private readonly RotateTransform[] needles={new(-50,4,208),new(-50,4,208)};
    private readonly VuBallistics leftBallistics=new(),rightBallistics=new();
    private double left,right;
    public AnalogVuControl():this(true) { }
    internal AnalogVuControl(bool drawHousing)
    {
        viewbox=new Viewbox{Stretch=Stretch.Uniform,Child=Surface};Content=viewbox;
        if(drawHousing)Put(new Image{Source=MusicModuleParts.Crop(new Rect(854,52,835,335)),Stretch=Stretch.Fill},0,0,835,335);
        for(int i=0;i<2;i++)
        {
            double x=47+i*399;
            var layer=new Canvas{Clip=new RectangleGeometry(new Rect(0,0,342,207),4,4)};Put(layer,x,63,342,207);
            var needle=new Image{Source=MusicModuleParts.Asset($"vu-{i+1}-needle.png"),Width=8,Height=218,Stretch=Stretch.Fill,RenderTransform=needles[i],Effect=new DropShadowEffect{Color=Colors.Black,Opacity=.24,BlurRadius=3.5,ShadowDepth=3.5,Direction=330}};
            Canvas.SetLeft(needle,167);Canvas.SetTop(needle,40);layer.Children.Add(needle);
            Put(new Image{Source=MusicModuleParts.Asset($"vu-{i+1}-cover.png"),Stretch=Stretch.Fill},x,63,342,207);
            Put(MusicModuleParts.VuGlass(),x,63,342,207);
        }
        RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.HighQuality);
    }
    public Stretch ContentStretch{get=>viewbox.Stretch;set=>viewbox.Stretch=value;}
    public void SetLevels(double l,double r){left=VuResponse.CalibratedInput(l);right=VuResponse.CalibratedInput(r);}
    public void RenderFrame(double seconds){double dt=Math.Clamp(seconds,1d/240,.05);needles[0].Angle=LevelToAngle(leftBallistics.Step(left,dt));needles[1].Angle=LevelToAngle(rightBallistics.Step(right,dt));}
    public void Reset(){left=right=0;leftBallistics.Reset();rightBallistics.Reset();foreach(var needle in needles)needle.Angle=-50;}
    internal (Point Pivot,Point Tip,double Angle) GetNeedleGeometry(bool isLeft)
    {
        int i=isLeft?0:1;double angle=needles[i].Angle,radians=angle*Math.PI/180,x=47+i*399+171,y=63+248;
        var transform=Surface.TransformToAncestor(this);return(transform.Transform(new Point(x,y)),transform.Transform(new Point(x+Math.Sin(radians)*208,y-Math.Cos(radians)*208)),angle);
    }
    private static double LevelToAngle(double input)
    {
        // Keep the loopback calibration (+18 dB after InputGain) while using
        // the finished dial's amplitude-to-angle geometry, not the old prototype scale.
        return VuResponse.ConsoleAngle(input*Math.Pow(10,18d/20)/VuResponse.ConsoleInputGain);
    }
    private void Put(FrameworkElement e,double x,double y,double width,double height){e.Width=width;e.Height=height;e.IsHitTestVisible=false;Canvas.SetLeft(e,x);Canvas.SetTop(e,y);Surface.Children.Add(e);}
}
