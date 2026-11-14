using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ekrano_valdymas;

// The current wide player's original rendered parts and audio-domain VU response.
// AnalogVuControl remains untouched for the other visualizer modes.
internal sealed class OriginalVuCardMeter : UserControl
{
    internal Canvas Surface {get;}=new(){Width=835,Height=335,Clip=new RectangleGeometry(new Rect(0,0,835,335),24,24)};
    private readonly RotateTransform[] needles=[new(-50,4,208),new(-50,4,208)];
    private readonly Image housing=new(){Stretch=Stretch.Fill};
    private readonly List<(Image Image,string File)> parts=new();
    private string finish="";
    private readonly System.Windows.Shapes.Ellipse[] peakLights=new System.Windows.Shapes.Ellipse[2];
    private readonly long[] peakUntil=new long[2];
    private double sensitivity=MusicConsoleControl.DefaultVuSensitivity;
    private readonly List<RotateTransform> screwRotations=new();
    internal event EventHandler? SensitivityChanged;
    internal double Sensitivity {get=>sensitivity;set=>SetSensitivity(value);}
    internal void SetSensitivity(double value,bool save=false)
    {
        sensitivity=Math.Clamp(double.IsFinite(value)?value:MusicConsoleControl.DefaultVuSensitivity,MusicConsoleControl.MinimumVuSensitivity,MusicConsoleControl.MaximumVuSensitivity);
        foreach(var r in screwRotations)r.Angle=Math.Log2(sensitivity/MusicConsoleControl.DefaultVuSensitivity)*90;
        if(save)SensitivityChanged?.Invoke(this,EventArgs.Empty);
    }
    internal double[] Angles=>needles.Select(n=>n.Angle).ToArray();
    internal OriginalVuCardMeter()
    {
        Content=new Viewbox{Child=Surface,Stretch=Stretch.Uniform};Put(housing,0,0,835,335);
        for(int i=0;i<2;i++)
        {
            double x=47+i*399;
            var layer=new Canvas{Clip=new RectangleGeometry(new Rect(0,0,342,207),4,4)};Put(layer,x,63,342,207);
            var needle=new Image{Width=8,Height=218,Stretch=Stretch.Fill,RenderTransform=needles[i]};
            parts.Add((needle,$"vu-{i+1}-needle.png"));Canvas.SetLeft(needle,167);Canvas.SetTop(needle,40);layer.Children.Add(needle);
            var cover=new Image{Stretch=Stretch.Fill};parts.Add((cover,$"vu-{i+1}-cover.png"));Put(cover,x,63,342,207);
            var lens=new System.Windows.Shapes.Ellipse{Fill=new RadialGradientBrush{GradientOrigin=new Point(.32,.25),GradientStops=new GradientStopCollection{new(Color.FromRgb(47,3,2),0),new(Color.FromRgb(29,1,0),.65),new(Color.FromRgb(17,0,0),1)}}};
            Put(lens,x+276.5,191.5,11,11);
            var lit=new System.Windows.Shapes.Ellipse{Opacity=0,Fill=new RadialGradientBrush{GradientOrigin=new Point(.35,.3),GradientStops=new GradientStopCollection{new(Color.FromRgb(255,75,35),0),new(Color.FromRgb(220,12,3),.45),new(Color.FromRgb(104,2,0),1)}}};
            Put(lit,x+276.5,191.5,11,11);peakLights[i]=lit;
            Put(MusicModuleParts.VuGlass(),x,63,342,207);
        }
        for(int i=0;i<2;i++)AddSensitivityScrew(218+i*399,309);
        RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.HighQuality);RefreshFinish();
    }
    private void AddSensitivityScrew(double cx,double cy)
    {
        var knob=new Grid{Background=Brushes.Transparent,Cursor=RotaryCursor.Value,Tag="NoWindowDrag",Focusable=true,ToolTip="VU sensitivity · drag / wheel · double-click to reset"};
        var head=new Image{Stretch=Stretch.Fill};parts.Add((head,"screw-head.png"));head.Width=head.Height=38;head.RenderTransformOrigin=new Point(.5,.5);
        var rotation=new RotateTransform(Math.Log2(sensitivity/MusicConsoleControl.DefaultVuSensitivity)*90);screwRotations.Add(rotation);head.RenderTransform=rotation;
        knob.Children.Add(head);Put(knob,cx-24,cy-24,48,48);knob.IsHitTestVisible=true;
        System.Windows.Automation.AutomationProperties.SetName(knob,"VU sensitivity");
        var feedback=new VuAdjustmentFeedback(knob,()=>sensitivity);
        double start=0,value=MusicConsoleControl.DefaultVuSensitivity;bool mouse=false;TouchDevice? touch=null;
        void Begin(double y){start=y;value=sensitivity;knob.Focus();feedback.Begin();}
        // Equal travel means equal dB change; neutral is the former maximum.
        void Move(double y){SetSensitivity(value*Math.Pow(2,(start-y)/60));feedback.Show();}
        void Save(){SetSensitivity(sensitivity,true);feedback.End();}
        knob.MouseLeftButtonDown+=(_,e)=>{if(e.StylusDevice!=null)return;if(e.ClickCount==2){SetSensitivity(MusicConsoleControl.DefaultVuSensitivity,true);feedback.Show();e.Handled=true;return;}mouse=true;Begin(e.GetPosition(Surface).Y);knob.CaptureMouse();e.Handled=true;};
        knob.MouseMove+=(_,e)=>{if(mouse)Move(e.GetPosition(Surface).Y);};
        knob.MouseLeftButtonUp+=(_,e)=>{if(!mouse)return;mouse=false;knob.ReleaseMouseCapture();Save();e.Handled=true;};
        knob.LostMouseCapture+=(_,_)=>{if(mouse){mouse=false;Save();}};
        knob.TouchDown+=(_,e)=>{if(touch==null&&!mouse){touch=e.TouchDevice;Begin(e.GetTouchPoint(Surface).Position.Y);knob.CaptureTouch(touch);}e.Handled=true;};
        knob.TouchMove+=(_,e)=>{if(touch==e.TouchDevice)Move(e.GetTouchPoint(Surface).Position.Y);e.Handled=true;};
        knob.TouchUp+=(_,e)=>{if(touch==e.TouchDevice){touch=null;knob.ReleaseTouchCapture(e.TouchDevice);Save();}e.Handled=true;};
        knob.LostTouchCapture+=(_,e)=>{if(touch==e.TouchDevice){touch=null;Save();}};
        knob.MouseWheel+=(_,e)=>{SetSensitivity(sensitivity*Math.Pow(2,Math.Sign(e.Delta)/20d),true);feedback.Show();e.Handled=true;};
        knob.KeyDown+=(_,e)=>{if(e.Key is Key.Left or Key.Down or Key.Right or Key.Up){SetSensitivity(sensitivity*Math.Pow(2,(e.Key is Key.Left or Key.Down?-1:1)/20d),true);feedback.Show();e.Handled=true;}else if(e.Key==Key.Home){SetSensitivity(MusicConsoleControl.DefaultVuSensitivity,true);feedback.Show();e.Handled=true;}};
    }
    internal void RefreshFinish()
    {
        if(finish==MusicFinish.Name)return;finish=MusicFinish.Name;
        var panel=MusicConsoleControl.Bitmap("panel-static.png",finish:finish);double scale=panel.PixelWidth/1900d;
        var crop=new CroppedBitmap(panel,new Int32Rect((int)Math.Round(854*scale),(int)Math.Round(47*scale),(int)Math.Round(835*scale),(int)Math.Round(335*scale)));crop.Freeze();housing.Source=finish=="militarity"?MusicConsoleControl.Bitmap("vu-card-housing.png",finish:finish):crop;
        foreach(var part in parts)part.Image.Source=MusicConsoleControl.Bitmap(part.File,finish:finish);
    }
    internal void SetLevels(double left,double right)
    {
        double gain=Sensitivity/MusicConsoleControl.VuSensitivityCalibration;
        needles[0].Angle=ClassicVuMeter.Angle(left*gain);needles[1].Angle=ClassicVuMeter.Angle(right*gain);
        long now=Environment.TickCount64;for(int i=0;i<2;i++){if(needles[i].Angle>ClassicVuMeter.Angle(Math.Pow(10,1d/20)))peakUntil[i]=now+220;peakLights[i].Opacity=now<peakUntil[i]?1:0;}
    }
    internal (Point Pivot,Point Tip,double Angle) GetNeedleGeometry(bool isLeft)
    {
        int i=isLeft?0:1;double a=needles[i].Angle,r=a*Math.PI/180,x=47+i*399+171,y=63+248;
        var t=Surface.TransformToAncestor(this);return(t.Transform(new Point(x,y)),t.Transform(new Point(x+Math.Sin(r)*208,y-Math.Cos(r)*208)),a);
    }
    private void Put(FrameworkElement e,double x,double y,double w,double h){e.Width=w;e.Height=h;e.IsHitTestVisible=false;Canvas.SetLeft(e,x);Canvas.SetTop(e,y);Surface.Children.Add(e);}
}

