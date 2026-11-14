using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Ekrano_valdymas;

internal sealed class AnalogVuCard : UserControl
{
    internal OriginalVuCardMeter Meter {get;}=new();
    internal Canvas Surface {get;}=new(){Width=626,Height=426};
    internal Rect MeterFrame {get;}=new((626-228*835/335d)/2,85,228*835/335d,228);
    private readonly AudioSpectrumService audio;
    private readonly DispatcherTimer timer=new(DispatcherPriority.Render){Interval=TimeSpan.FromMilliseconds(16)};
    internal bool Preview {get;set;}
    private bool suspended;
    internal AnalogVuCard(AudioSpectrumService audio)
    {
        this.audio=audio;Background=Brushes.Transparent;Surface.Background=Brushes.Transparent;
        RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.HighQuality);
        var backing=new Border{CornerRadius=new CornerRadius(18)};backing.SetResourceReference(Border.BackgroundProperty,"PanelMetalBrush");CommonMetal.SetSurface(backing,true);Put(backing,0,0,626,426);
        Put(Meter,MeterFrame.X,MeterFrame.Y,MeterFrame.Width,MeterFrame.Height);
        foreach(double y in new[]{68d,330d})Put(new Rectangle{Fill=new SolidColorBrush(Color.FromArgb(65,180,180,180))},28,y,570,1);
        Label("BlakiDeck · Stereo VU Monitor",25,32,19,"ModuleTitle");
        Label("L / R · Stereo Level",352,48,21,null);
        Surface.Clip=new RectangleGeometry(new Rect(0,0,626,426),18,18);Content=new Viewbox{Stretch=Stretch.Uniform,Child=Surface};
        Loaded+=(_,_)=>{ResourcePolicy.Changed+=UpdateActivity;UpdateActivity();};Unloaded+=(_,_)=>{ResourcePolicy.Changed-=UpdateActivity;UpdateActivity();};IsVisibleChanged+=(_,_)=>UpdateActivity();
        timer.Tick+=(_,_)=>{Meter.RefreshFinish();var level=audio.ReadClassicVu();Meter.SetLevels(level.Left,level.Right);};
    }
    internal void RefreshSettings(double sensitivity){Meter.Sensitivity=sensitivity;Meter.RefreshFinish();}
    private void Label(string value,double y,double height,double size,string? tag)
    {
        var text=new OpticallyCenteredText{Text=value,TextFontFamily=new FontFamily(new Uri("pack://application:,,,/"),"./Assets/Fonts/#Michroma"),TextFontSize=size,InkAlignment=HorizontalAlignment.Center,Tag=tag,Ellipsis=true};
        text.SetResourceReference(OpticallyCenteredText.TextBrushProperty,"PrimaryTextBrush");Put(text,28,y,570,height);
    }
    private void Put(FrameworkElement e,double x,double y,double w,double h){e.Width=w;e.Height=h;Canvas.SetLeft(e,x);Canvas.SetTop(e,y);Surface.Children.Add(e);}
    internal void SetSuspended(bool value){suspended=value;UpdateActivity();}
    private void UpdateActivity(){bool active=IsLoaded&&IsVisible&&!Preview&&!suspended&&!ResourcePolicy.IsGame;audio.SetActive(this,active);if(active)timer.Start();else timer.Stop();}
    internal void SetPreview(double left,double right){Preview=true;UpdateActivity();Meter.SetLevels(left,right);}
}
