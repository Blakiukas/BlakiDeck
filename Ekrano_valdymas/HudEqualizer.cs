using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Ekrano_valdymas;

// BlakiHUD MainWindow.xaml.cs: DrawSpotifyEqualizer / EqualizerComposition_Rendering.
// Same 40 log-frequency bands, gain, exponent, time-based attack/release and green palette.
internal sealed class HudEqualizer : FrameworkElement
{
    private readonly AudioSpectrumService audio;
    private readonly Func<bool> playing;
    private readonly double[] samples=new double[40],targets=new double[40],levels=new double[40];
    private readonly SolidColorBrush[] brushes=Enumerable.Range(0,129).Select(i=>{
        double v=i/128d;var b=new SolidColorBrush(Color.FromArgb((byte)Math.Round((.58+v*.4)*255),(byte)Math.Round(38+v*45),(byte)Math.Round(98+v*125),(byte)Math.Round(38+v*50)));b.Freeze();return b;
    }).ToArray();
    private readonly DispatcherTimer timer=new(DispatcherPriority.Render){Interval=TimeSpan.FromMilliseconds(40)};
    private double gain=1;
    private TimeSpan last;
    private bool subscribed,preview,suspended;
    internal HudEqualizer(AudioSpectrumService audio,Func<bool> playing)
    {
        this.audio=audio;this.playing=playing;IsHitTestVisible=false;
        Loaded+=(_,_)=>{ResourcePolicy.Changed+=Activity;SpotifyWidget.PlaybackStateChanged+=PlaybackChanged;Activity();};
        Unloaded+=(_,_)=>{ResourcePolicy.Changed-=Activity;SpotifyWidget.PlaybackStateChanged-=PlaybackChanged;Activity();};
        IsVisibleChanged+=(_,_)=>Activity();
        timer.Tick+=(_,_)=>{
            bool active=playing();if(!active){Activity();return;}audio.CopyLevels(samples);
            double peak=samples.Max(),targetGain=active&&peak>.012?Math.Clamp(.9/peak,.75,6):1;
            gain+=(targetGain-gain)*(targetGain<gain?.22:.09);
            for(int i=0;i<40;i++)targets[i]=active?Math.Clamp(Math.Pow(samples[i]*gain,.72),0,1):0;
        };
    }
    private void PlaybackChanged(object? sender,EventArgs e)
    {
        if(Dispatcher.CheckAccess())Activity();
        else Dispatcher.BeginInvoke(new Action(Activity));
    }
    private void Activity()
    {
        bool visible=IsLoaded&&IsVisible&&!preview&&!suspended&&!ResourcePolicy.IsGame;
        bool active=visible&&playing();
        audio.SetActive(this,active);
        if(active)
        {timer.Start();if(!subscribed){CompositionTarget.Rendering+=Frame;subscribed=true;last=TimeSpan.Zero;}}
        else
        {
            timer.Stop();Array.Clear(targets);Array.Clear(samples);
            // Let already drawn bars fall normally, then detach the frame loop.
            if(!visible||levels.All(v=>v==0))StopFrames();
        }
    }
    private void StopFrames(){if(subscribed){CompositionTarget.Rendering-=Frame;subscribed=false;}last=TimeSpan.Zero;}
    internal void SetSuspended(bool value){suspended=value;Activity();}
    private void Frame(object? sender,EventArgs args)
    {
        if(args is not RenderingEventArgs e||e.RenderingTime==last)return;
        double elapsed=last==TimeSpan.Zero?1/60d:Math.Clamp((e.RenderingTime-last).TotalSeconds,1/240d,.1);last=e.RenderingTime;
        double attack=1-Math.Pow(1-.68,elapsed/.04),release=1-Math.Pow(1-.16,elapsed/.04);
        for(int i=0;i<40;i++)levels[i]+=(targets[i]-levels[i])*(targets[i]>levels[i]?attack:release);
        if(!timer.IsEnabled&&levels.All(v=>v<.0005)){Array.Clear(levels);StopFrames();}
        InvalidateVisual();
    }
    internal void SetPreview(){preview=true;Activity();for(int i=0;i<40;i++)levels[i]=.08+.75*Math.Pow(Math.Sin((i+3)*.38),2)*(1-i/55d);InvalidateVisual();}
    protected override void OnRender(DrawingContext dc)
    {
        double gap=3*ActualWidth/520,width=(ActualWidth-gap*39)/40;
        for(int i=0;i<40;i++){double h=Math.Max(2,ActualHeight*levels[i]);dc.DrawRectangle(brushes[Math.Clamp((int)Math.Round(levels[i]*128),0,128)],null,new Rect(i*(width+gap),ActualHeight-h,Math.Max(0,width),h));}
    }
}
