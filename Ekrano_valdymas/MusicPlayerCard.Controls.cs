using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Ekrano_valdymas;
internal sealed partial class MusicPlayerCard
{
    private readonly List<Action> modeAvailabilityUpdates=new();
    private readonly Rectangle volumeFill=new(){Height=3};
    private readonly Ellipse volumeThumb=new(){Width=8,Height=8};
    private OpticallyCenteredText? volumeLabel;
    internal Action<double>? DiagnosticVolumeAction {get;set;}
    internal double DisplayedVolume {get;private set;}
    private double unmutedVolume=50;
    internal void SetVolume(double value)
    {
        value=Math.Clamp(value,0,100);
        if(value>0)unmutedVolume=value;
        if(DiagnosticVolumeAction!=null)DiagnosticVolumeAction(value);
        else if(!Preview)player.ConsoleVolume=value;
        ShowVolume(value);
    }
    private void ShowVolume(double value){DisplayedVolume=value;volumeFill.Width=84*value/100;Canvas.SetLeft(volumeThumb,433+84*value/100-4);if(volumeLabel!=null)volumeLabel.Text=$"{value:0}";}
    private void RefreshVolume()=>ShowVolume(Preview?68:player.ConsoleVolume);
    internal void SyncSymbolColor(){foreach(var update in modeAvailabilityUpdates)update();foreach(var p in Surface.Children.OfType<Path>().Where(p=>p.Tag is string tag&&tag.StartsWith("Aligned auxiliary")))p.Fill=(p.Tag is "Aligned auxiliary Shuffle"&&player.ShuffleActive==true)||(p.Tag is "Aligned auxiliary Repeat"&&player.RepeatActive==true)?lamp:title.TextBrush;}
    private void BuildAuxiliaryControls()
    {
        // Header controls and five evenly spaced bottom controls.
        Glyph("Shuffle", "M0,3 L4,3 16,17 21,17 M17,13 L21,17 17,21 M0,17 L4,17 16,3 21,3 M17,0 L21,3 17,7",285,async()=>await player.ConsoleModeAsync(true));
        Glyph("Repeat", "M2,7 L2,3 21,3 M17,0 L21,3 17,7 M21,14 L21,18 2,18 M6,14 L2,18 6,21",575,async()=>await player.ConsoleModeAsync(false));
        Glyph("Mute / restore", "M0,7 L5,7 11,1 11,21 5,15 0,15 Z M15,4 Q23,11 15,18",414,()=>{double v=player.ConsoleVolume;if(v>0)unmutedVolume=v;SetVolume(v>0?0:unmutedVolume);return Task.CompletedTask;},42);
        Put(new Rectangle{Fill=new SolidColorBrush(Color.FromArgb(70,150,150,150))},433,40.5,84,3);
        volumeFill.Fill=volumeThumb.Fill=lamp;Put(volumeFill,433,40.5,0,3);Put(volumeThumb,433,38,8,8);
        volumeLabel=Text(525,28,32,28,14);volumeLabel.InkAlignment=HorizontalAlignment.Right;
        var hit=new Border{Background=Brushes.Transparent,Cursor=Cursors.Hand,Tag="NoWindowDrag",Focusable=true,ToolTip="Volume"};Put(hit,428,22,94,40);
        void Set(double x)=>SetVolume((x-5)/84*100);
        hit.MouseLeftButtonDown+=(_,e)=>{hit.CaptureMouse();Set(e.GetPosition(hit).X);e.Handled=true;};
        hit.MouseMove+=(_,e)=>{if(hit.IsMouseCaptured&&e.LeftButton==MouseButtonState.Pressed){Set(e.GetPosition(hit).X);e.Handled=true;}};
        hit.MouseLeftButtonUp+=(_,e)=>{if(hit.IsMouseCaptured){Set(e.GetPosition(hit).X);hit.ReleaseMouseCapture();e.Handled=true;}};
        hit.TouchDown+=(_,e)=>{hit.CaptureTouch(e.TouchDevice);Set(e.GetTouchPoint(hit).Position.X);e.Handled=true;};
        hit.TouchMove+=(_,e)=>{if(e.TouchDevice.Captured==hit){Set(e.GetTouchPoint(hit).Position.X);e.Handled=true;}};
        hit.TouchUp+=(_,e)=>{if(e.TouchDevice.Captured==hit){Set(e.GetTouchPoint(hit).Position.X);hit.ReleaseTouchCapture(e.TouchDevice);e.Handled=true;}};
        hit.MouseWheel+=(_,e)=>{SetVolume(player.ConsoleVolume+Math.Sign(e.Delta)*2);e.Handled=true;};
        hit.PreviewKeyDown+=(_,e)=>{if(e.Key is Key.Left or Key.Right or Key.Up or Key.Down){SetVolume(player.ConsoleVolume+(e.Key is Key.Up or Key.Right?1:-1));e.Handled=true;}};
    }
    private void Glyph(string tip,string path,double center,Func<Task>? action,double centerY=261)
    {
        // Normalize actual stroked ink, including sharp joins, rather than only its centerline.
        var geometry=Geometry.Parse(path).GetWidenedPathGeometry(new Pen(Brushes.White,1.3));double width=22*geometry.Bounds.Width/geometry.Bounds.Height;
        var symbol=new Path{Data=geometry,Fill=Brushes.White,Stretch=Stretch.Fill,IsHitTestVisible=false,Tag="Aligned auxiliary "+tip};
        symbol.SetResourceReference(Shape.FillProperty,"PrimaryTextBrush");Put(symbol,center-width/2,centerY-11,width,22);
        if(action==null){symbol.Opacity=.35;symbol.ToolTip="Selected service does not expose favorites";return;}
        var hit=new Button{Background=Brushes.Transparent,BorderThickness=new Thickness(0),Cursor=Cursors.Hand,Tag="NoWindowDrag",ToolTip=tip};
        var template=new FrameworkElementFactory(typeof(Border));template.SetValue(Border.BackgroundProperty,Brushes.Transparent);hit.Template=new ControlTemplate(typeof(Button)){VisualTree=template};
        System.Windows.Automation.AutomationProperties.SetName(hit,tip);Put(hit,center-19,centerY-19,38,38);
        void RefreshAvailability()
        {
            if(tip is not ("Shuffle" or "Repeat"))return;
            bool available=tip=="Shuffle"?player.CanShuffle:player.CanRepeat;
            hit.IsEnabled=available;symbol.Opacity=available?1:.35;
        }
        modeAvailabilityUpdates.Add(RefreshAvailability);RefreshAvailability();
        async void Invoke(){if(Preview||!hit.IsEnabled)return;hit.IsEnabled=false;try{await action();}catch(Exception e){InteractionDiagnostics.RecordFailure(tip,e);var feedback=new ToolTip{Content=e.Message,PlacementTarget=hit,IsOpen=true};var close=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(5)};close.Tick+=(_,_)=>{feedback.IsOpen=false;close.Stop();};close.Start();}finally{hit.IsEnabled=true;RefreshAvailability();}}
        hit.Click+=(_,e)=>{e.Handled=true;Invoke();};
    }
}

