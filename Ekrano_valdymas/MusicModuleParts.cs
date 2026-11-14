using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Ekrano_valdymas;
// Finished console parts, in its original 1900 x 437 source coordinates.
internal static class MusicModuleParts
{
    internal static BitmapSource Panel => MusicConsoleControl.Bitmap("panel-static.png",legacy:true);
    internal static BitmapSource Asset(string name)=>MusicConsoleControl.Bitmap(name,legacy:true);
    internal static BitmapSource Crop(Rect rect)
    {
        var source=Panel;double s=source.PixelWidth/1900d;
        var crop=new CroppedBitmap(source,new Int32Rect((int)Math.Round(rect.X*s),(int)Math.Round(rect.Y*s),(int)Math.Round(rect.Width*s),(int)Math.Round(rect.Height*s)));crop.Freeze();return crop;
    }
    internal static Ellipse ButtonBevel(double scale=1)
    {
        var bevel=new LinearGradientBrush{StartPoint=new Point(0,0),EndPoint=new Point(.65,1)};
        bevel.GradientStops.Add(new GradientStop(Color.FromArgb(135,204,211,215),0));
        bevel.GradientStops.Add(new GradientStop(Color.FromArgb(40,116,123,129),.35));
        bevel.GradientStops.Add(new GradientStop(Color.FromArgb(210,0,0,0),.70));
        bevel.GradientStops.Add(new GradientStop(Color.FromArgb(75,147,153,160),1));
        return new Ellipse{Stroke=bevel,StrokeThickness=2.8*scale,IsHitTestVisible=false};
    }
    internal static Border SongGlass()
    {
        var glass=new LinearGradientBrush{StartPoint=new Point(0,0),EndPoint=new Point(.8,1)};
        glass.GradientStops.Add(new GradientStop(Color.FromArgb(2,255,255,255),0));glass.GradientStops.Add(new GradientStop(Color.FromArgb(15,245,242,235),.32));glass.GradientStops.Add(new GradientStop(Colors.Transparent,1));
        return new Border{Background=glass,CornerRadius=new CornerRadius(5),BorderThickness=new Thickness(3),BorderBrush=new SolidColorBrush(Color.FromArgb(180,0,0,0)),IsHitTestVisible=false};
    }
    internal static Border VuGlass()
    {
        var reflection=new LinearGradientBrush{StartPoint=new Point(0,0),EndPoint=new Point(.8,1)};
        foreach(var stop in new[]{(0d,Color.FromArgb(3,255,255,255)),(.2,Color.FromArgb(24,240,244,248)),(.36,Color.FromArgb(4,240,244,248)),(.65,Colors.Transparent),(1d,Color.FromArgb(12,255,255,255))})reflection.GradientStops.Add(new GradientStop(stop.Item2,stop.Item1));
        return new Border{Background=reflection,CornerRadius=new CornerRadius(5),BorderThickness=new Thickness(1),BorderBrush=new SolidColorBrush(Color.FromArgb(38,220,225,230)),IsHitTestVisible=false};
    }
    internal static Canvas AlbumShade(double size,double strength=1,double widthFactor=1)
    {
        double edge=size*32/319*widthFactor;var canvas=new Canvas{Width=size,Height=size,Clip=new RectangleGeometry(new Rect(0,0,size,size),5,5),IsHitTestVisible=false};
        foreach(bool vertical in new[]{false,true})foreach(bool reverse in new[]{false,true})
        {
            var brush=new LinearGradientBrush{StartPoint=vertical?new Point(.5,0):new Point(0,.5),EndPoint=vertical?new Point(.5,1):new Point(1,.5)};
            foreach(var stop in new[]{(0d,(byte)205),(.25,(byte)158),(.5,(byte)85),(.75,(byte)24),(1d,(byte)0)})brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(stop.Item2*Math.Clamp(strength,0,1)),0,0,0),stop.Item1));
            if(reverse){var p=brush.StartPoint;brush.StartPoint=brush.EndPoint;brush.EndPoint=p;}
            var strip=new Rectangle{Fill=brush,Width=vertical?size:edge,Height=vertical?edge:size};Canvas.SetLeft(strip,!vertical&&reverse?size-edge:0);Canvas.SetTop(strip,vertical&&reverse?size-edge:0);canvas.Children.Add(strip);
        }
        return canvas;
    }
}
