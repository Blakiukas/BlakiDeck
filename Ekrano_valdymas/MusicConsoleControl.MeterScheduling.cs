using System.Windows;
using System.Windows.Threading;

namespace Ekrano_valdymas;
internal sealed partial class MusicConsoleControl
{
    private Window? meterWindow;
    private MonitorRefreshRate? meterDisplay;
    private readonly DispatcherTimer lightingTimer=new(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(1000d/30)};
    private readonly DispatcherTimer metadataTimer=new(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(200)};
    private double lastLighting;
    internal event Action<long>? MeterPresented;
    internal MonitorRefreshRate.Display? MeterDisplay => meterDisplay?.Current;
    internal int MeterDisplayQueries => meterDisplay?.QueryCount??0;
    internal bool MeterPumpRunning => meterPump!=null;
    internal bool DiagnosticSchedules {get;set;}
    internal bool LightingPumpRunning=>lightingTimer.IsEnabled;

    private void AttachMeterWindow()
    {
        if(meterWindow!=null)return;
        meterWindow=Window.GetWindow(this);
        if(meterWindow==null)return;
        meterWindow.StateChanged+=MeterWindowChanged;
        meterWindow.IsVisibleChanged+=MeterWindowVisible;
        SpotifyWidget.PlaybackStateChanged+=MeterPlaybackChanged;
        player.PlaybackModesChanged+=RefreshPlaylistControls;
        ResourcePolicy.Changed+=UpdateMeterActivity;
        meterDisplay=new MonitorRefreshRate(meterWindow);
        meterDisplay.Changed+=MeterDisplayChanged;
        lightingTimer.Tick+=LightingTick;
        metadataTimer.Tick+=MetadataTick;
        Refresh();UpdateMeterActivity();
    }
    private void DetachMeterWindow()
    {
        StopMeterRendering();lightingTimer.Stop();metadataTimer.Stop();StopGamePulse();
        lightingTimer.Tick-=LightingTick;metadataTimer.Tick-=MetadataTick;
        SpotifyWidget.PlaybackStateChanged-=MeterPlaybackChanged;
        player.PlaybackModesChanged-=RefreshPlaylistControls;
        ResourcePolicy.Changed-=UpdateMeterActivity;
        if(meterWindow!=null){meterWindow.StateChanged-=MeterWindowChanged;meterWindow.IsVisibleChanged-=MeterWindowVisible;}
        if(meterDisplay!=null){meterDisplay.Changed-=MeterDisplayChanged;meterDisplay.Dispose();}
        meterWindow=null;meterDisplay=null;
        player.ConsoleActive=false;audio.SetActive(this,false);
    }
    private void MeterWindowChanged(object? sender,EventArgs e)=>UpdateMeterActivity();
    private void MeterWindowVisible(object sender,DependencyPropertyChangedEventArgs e)=>UpdateMeterActivity();
    private void MeterDisplayChanged()=>meterPump?.SetRate(meterDisplay!.Current.Hertz);
    private void MeterPlaybackChanged(object? sender,EventArgs e)
    {
        if(Dispatcher.CheckAccess())UpdateAnimationActivity();
        else Dispatcher.BeginInvoke(new Action(UpdateAnimationActivity));
    }
    internal void UpdateAnimationActivity()
    {
        UpdateAudioActivity();
        bool visible=IsLoaded&&IsVisible&&meterWindow is {IsVisible:true,WindowState:not WindowState.Minimized};
        UpdateGamePulse(visible&&powerOn&&ResourcePolicy.IsGame);
        if(Diagnostic&&!DiagnosticSchedules||!visible){StopMeterRendering();lightingTimer.Stop();return;}
        if(ResourcePolicy.IsGame){StopMeterRendering();AdvanceGameNeedles();UpdateLights(PlaybackPlaying);if(GameNeedlesSettling||GameShadeTransition||ScreenPowerTransition){if(!lightingTimer.IsEnabled){lastLighting=clock.Elapsed.TotalSeconds;lightingTimer.Start();}}else lightingTimer.Stop();return;}
        double now=clock.Elapsed.TotalSeconds;
        bool playing=PlaybackPlaying&&powerOn;
        if(playing)lastPlayingTime=now;
        var level=audio.ReadClassicVu();
        // A damped floating-point state can retain a subnormal tail forever.
        // Use the exact rendered angle, without adding a signal dead zone/filter.
        double gain=vuSensitivity/VuSensitivityCalibration;
        bool moving=powerOn?(playing||ClassicVuMeter.Angle(level.Left*gain)!=-50||ClassicVuMeter.Angle(level.Right*gain)!=-50):
            (System.Diagnostics.Stopwatch.GetTimestamp()-powerOffTimestamp)/(double)System.Diagnostics.Stopwatch.Frequency<2;
        if(moving)StartMeterRendering();
        else if(meterPump!=null){Tick(0);StopMeterRendering();UpdateAudioActivity();}
        double target=dimEnabled&&now-lastPlayingTime>=dimDelaySeconds?.75:0;
        if(Math.Abs(target-dim)>1e-6||ScreenPowerTransition||PeakHolding||GameShadeTransition)
        {
            if(!lightingTimer.IsEnabled){lastLighting=now;lightingTimer.Start();}
        }
        else lightingTimer.Stop();
    }
    private void UpdateAudioActivity()
    {
        bool visible=IsLoaded&&IsVisible&&meterWindow is {IsVisible:true,WindowState:not WindowState.Minimized};
        // Keep capture only through the needle's natural release after pause.
        // Ownership is independent of the small EQ and BlakiHUD visualizer.
        audio.SetActive(this,visible&&powerOn&&(PlaybackPlaying||meterPump!=null)&&!Diagnostic&&!ResourcePolicy.IsGame);
    }
    private void UpdateMeterActivity()
    {
        UpdateGameVisualState();
        bool visible=IsLoaded&&IsVisible&&meterWindow is {IsVisible:true,WindowState:not WindowState.Minimized};
        UpdateAudioActivity();
        player.ConsoleActive=visible;
        metadataTimer.Interval=ResourcePolicy.IsSaving?TimeSpan.FromSeconds(1):TimeSpan.FromMilliseconds(200);
        if(visible&&(!Diagnostic||DiagnosticSchedules))
        {
            UpdateAnimationActivity();
            if(!metadataTimer.IsEnabled){Refresh();metadataTimer.Start();}
        }
        else{StopMeterRendering();lightingTimer.Stop();metadataTimer.Stop();StopGamePulse();}
    }
    private void LightingTick(object? sender,EventArgs e)
    {
        double now=clock.Elapsed.TotalSeconds;
        AdvanceVuLighting(PlaybackPlaying&&powerOn,now,Math.Clamp(now-lastLighting,0,.1));lastLighting=now;
        UpdateAnimationActivity();
    }
    private void MetadataTick(object? sender,EventArgs e){Refresh();UpdateAnimationActivity();}
}
