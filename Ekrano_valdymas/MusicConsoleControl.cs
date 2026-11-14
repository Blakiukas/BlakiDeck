using System.Diagnostics;
using System.Windows.Media.Animation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Ekrano_valdymas;
internal sealed partial class MusicConsoleControl : UserControl
{
    internal readonly Canvas Surface = new() { Width = 1900, Height = 426, ClipToBounds = true, Background = Brushes.Transparent };
    private readonly RotateTransform[] needles = [new(-50, 4, 208), new(-50, 4, 208)];
    private readonly Image album, albumPrevious;
    private int albumTransitionVersion;
    internal const double ContentOffsetX=-22,RotaryOffsetX=6.125;
    private readonly RotateTransform volumeRotation=new(-135,60,60);
    private Line? volumeIndicator;
    private Line? volumePassiveIndicator;
    private FrameworkElement? volumeUnderlight;
    private double volumeStartY, volumeStartValue, volumeDisplayValue, volumeDisplayUntil;
    private bool volumeMoved;
    private TouchDevice? volumeTouch;
    private bool volumeMouse;
    private double volumeDragThreshold=6;
    private bool dimEnabled;private int dimDelaySeconds=10;
    private ImageSource? lastAlbum;
    internal event EventHandler? TopBarToggleRequested;
    private readonly OpticallyCenteredText title, artist, time, total;
    private readonly OpticallyCenteredText audioDetails;
    private readonly Rectangle progress, positionMark;
    private readonly List<(UIElement element, double strength, bool symbol)> lamps = new();
    private readonly List<Border> vuDimmers = new();
    private readonly List<Border> vuWarmth = new();
    private readonly List<Border> screenDimmers = new();
    private readonly List<Border> screenWarmth = new();
    private double screenPowerFrom=1,songPowerFrom=1,screenPowerTarget=1,screenPowerStart=-10;
    private const double SongPowerFadeSeconds=2.4;
    private double ScreenPowerDuration=>Math.Max(ScreenPowerChannelDuration(screenPowerFrom,false),ScreenPowerChannelDuration(songPowerFrom,true));
    private double ScreenPowerChannelDuration(double from,bool song)=>from==screenPowerTarget?0:
        screenPowerTarget>from?(song?.25+SongPowerFadeSeconds:1.4):(song?SongPowerFadeSeconds:.45);
    private bool ScreenPowerTransition=>clock.Elapsed.TotalSeconds-screenPowerStart<ScreenPowerDuration;
    internal double ScreenPowerLevel=>ReadScreenPower(false);
    internal double SongPowerLevel=>ReadScreenPower(true);
    private double ReadScreenPower(bool song)
    {
            double from=song?songPowerFrom:screenPowerFrom;
            double t=clock.Elapsed.TotalSeconds-screenPowerStart;
            // The information display fades for longer, not merely after a longer delay.
            double progress=screenPowerTarget>from?Math.Clamp((t-(song?.25:.2))/(song?SongPowerFadeSeconds:1.2),0,1):Math.Clamp(t/(song?SongPowerFadeSeconds:.45),0,1);
            double smooth=progress*progress*(3-2*progress);
            return from+(screenPowerTarget-from)*smooth;
    }
    private void BeginScreenPower(bool enabled)
    {
        songPowerFrom=SongPowerLevel;screenPowerFrom=ScreenPowerLevel;screenPowerTarget=enabled?1:0;screenPowerStart=clock.Elapsed.TotalSeconds;
    }
    internal bool ElectronicScreensAreOff => screenDimmers.Count==2&&screenDimmers.All(s=>s.Opacity==1);
    private readonly List<Rectangle> transportShades = new();
    private readonly HashSet<UIElement> pressedLamps = new();
    private readonly HashSet<UIElement> directionalLamps = new();
    private readonly HashSet<UIElement> pendingLamps = new();
    private bool pendingPlay;
    private long playCommandVersion;
    private double pendingPlayUntil;
    private readonly SolidColorBrush lampBrush = new(Color.FromRgb(238, 183, 100));
    private bool renderingSubscribed;
    private HighResolutionUiPump? meterPump;
    private readonly string instanceFinish=MusicFinish.Name;
    private readonly SpotifyWidget player;
    private readonly AudioSpectrumService audio;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private double lastPlayingTime, stoppedSeconds, dim;
    private bool dragging;
    private int ticks;
    private double lightIntensity = 1;
    private double volumeLightIntensity = .5;
    internal double VolumeLightIntensity=>volumeLightIntensity*100;
    internal double VolumeLightLevel=>volumeUnderlight?.Opacity??0;
    internal Color LightingColor=>lampBrush.Color;
    private readonly SolidColorBrush volumeLampBrush=new(Color.FromRgb(238,183,100));
    private Color? volumeLightColor;
    internal Color VolumeLightingColor=>volumeLampBrush.Color;
    internal void SetVolumeLightColor(Color? color)
    {
        volumeLightColor=color;volumeLampBrush.Color=color??lampBrush.Color;
        if(volumeIndicator?.Effect is DropShadowEffect glow)glow.Color=volumeLampBrush.Color;
    }
    internal double PlayLightLevel=>lamps.Where(item=>!directionalLamps.Contains(item.element)).Select(item=>item.element.Opacity).DefaultIfEmpty().Max();
    internal void SetVolumeLighting(double percent)
    {
        volumeLightIntensity=double.IsFinite(percent)?Math.Clamp(percent/100,0,1):.5;
        UpdateLights(PlaybackPlaying);
    }
    private double externalDim;
    internal void SetExternalDimming(double amount){externalDim=Math.Clamp(amount,0,1);UpdateLights(PlaybackPlaying);}
    internal bool Diagnostic { get; set; }
    internal double VolumeAngle=>volumeRotation.Angle;
    internal int MeterUpdates=>ticks;
    internal double[] MeterAngles=>needles.Select(n=>n.Angle).ToArray();
    internal bool IndicatorLampsAreOff => lamps.All(item=>item.element.Opacity==0)&&volumeIndicator?.Opacity==0&&(volumeUnderlight==null||volumeUnderlight.Opacity==0);
    internal bool TransportBodiesAreDimmed => transportShades.Count == 3 && transportShades.All(s => s.Opacity >= .65);
    internal bool TransportBodiesAreUnshaded => transportShades.Count == 3 && transportShades.All(s => s.Opacity == 0);
    internal bool DirectionalLampsAreStandby=>lamps.Where(item=>directionalLamps.Contains(item.element)).All(item=>item.element.Opacity==0);
    internal double[] DirectionalLighting=>lamps.Where(item=>directionalLamps.Contains(item.element)).Select(item=>item.element.Opacity).Concat(transportShades.Where((_,i)=>i!=1).Select(s=>s.Opacity)).ToArray();
    internal Func<string,Task<bool>>? DiagnosticCommand {get;set;}
    internal bool DiagnosticPlaying {get;set;}
    private bool PlaybackPlaying=>Diagnostic?DiagnosticPlaying:player.IsPlaying;
    internal bool PlayLampsAreLit=>lamps.Where(item=>!directionalLamps.Contains(item.element)).All(item=>item.element.Opacity>0);
    internal double PlayBodyShade=>transportShades[1].Opacity;
    internal void RefreshDiagnosticLights(){if(Diagnostic)UpdateLights(DiagnosticPlaying);}
    internal event Action<string>? DiagnosticTransportAction;
    internal MusicConsoleControl(SpotifyWidget player, AudioSpectrumService audio)
    {
        this.player = player; this.audio = audio;
        UseLayoutRounding=false;Surface.UseLayoutRounding=false;ClipToBounds=true;
        Stylus.SetIsPressAndHoldEnabled(this,false);
        Background = Brushes.Transparent;
        Surface.Clip=new RectangleGeometry(new Rect(0,0,1900,426),24,24);
        // Fit the complete console into the shared dashboard height without
        // cropping the chassis or changing the artwork's proportions.
        Content = new Viewbox { Stretch = Stretch.Uniform, Child = Surface };
        AddChassis();
        album = new Image { Stretch = Stretch.UniformToFill, Clip = new RectangleGeometry(new Rect(0, 0, 319, 319), 5, 5), IsHitTestVisible = false };
        albumPrevious=new Image{Stretch=Stretch.UniformToFill,Clip=album.Clip,IsHitTestVisible=false};Put(albumPrevious,76,60,319,319);
        Put(album, 76, 60, 319, 319);
        Put(new Border { Background=new SolidColorBrush(Color.FromRgb(3,4,4)), CornerRadius=new CornerRadius(5), IsHitTestVisible=false },431,100,417,151);
        title = Text(441, 106, 397, 39, 27, HorizontalAlignment.Left);
        title.TextFontFamily = new FontFamily(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Michroma");
        artist = Text(441, 147, 397, 29, 19, HorizontalAlignment.Left);
        audioDetails=Text(441,177,397,19,12,HorizontalAlignment.Right);audioDetails.TextBrush=new SolidColorBrush(Color.FromRgb(145,133,116));
        Put(new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(38, 35, 29)), IsHitTestVisible = false },441,202,397,1);
        progress = new Rectangle { Fill = lampBrush, IsHitTestVisible = false }; Put(progress,441,202,0,1);
        positionMark = new Rectangle { Fill = lampBrush, RadiusX = 1, RadiusY = 1, IsHitTestVisible = false };Put(positionMark,441,199,3,7);
        time = Text(441, 218, 115, 25, 16, HorizontalAlignment.Left);
        // Reserve the longest volume label at the same fixed face/size as elapsed time.
        // Per-string fitting changes the visible height between 98% and 100%.
        total = Text(688, 218, 150, 25, 16, HorizontalAlignment.Right);
        total.Ellipsis=false;
        gameModeFrame=new Border{BorderBrush=new SolidColorBrush(Color.FromRgb(185,166,139)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(3),IsHitTestVisible=false,Visibility=Visibility.Collapsed};
        Put(gameModeFrame,561,218,122,25);
        gameModeText=Text(567,220,110,21,12,HorizontalAlignment.Center);
        gameModeText.Text="GAME MODE";gameModeText.TextFontFamily=title.TextFontFamily;
        gameModeText.TextBrush=new SolidColorBrush(Color.FromRgb(185,166,139));gameModeText.Visibility=Visibility.Collapsed;
        artist.TextFontFamily=time.TextFontFamily=total.TextFontFamily=title.TextFontFamily;
        artist.TextBrush=new SolidColorBrush(Color.FromRgb(166,151,129));
        time.TextBrush=total.TextBrush=new SolidColorBrush(Color.FromRgb(185,166,139));
        Border seek = new() { Background = Brushes.Transparent, Cursor = Cursors.Hand, Tag = "NoWindowDrag", ToolTip = HardwareTip("Seek") };Put(seek,435,186,409,33);
        seek.MouseLeftButtonUp += async (_,e) => { e.Handled=true; await SeekAt(e.GetPosition(Surface).X); };
        seek.MouseLeftButtonDown += (_,e)=>{seek.CaptureMouse();e.Handled=true;};
        seek.TouchDown += (_,e)=>{seek.CaptureTouch(e.TouchDevice);e.Handled=true;};
        seek.TouchUp += async (_,e)=>{seek.ReleaseTouchCapture(e.TouchDevice);e.Handled=true;await SeekAt(e.GetTouchPoint(Surface).Position.X);};
        async Task SeekAt(double x) { seek.ReleaseMouseCapture(); if(!powerOn)return; if(!Diagnostic) await Safe(()=>player.ConsoleSeekAsync(Math.Clamp((x-ContentOffsetX-441)/397,0,1)*player.ConsoleDuration)); }
        // Common glass layer lies above text, without taking input.
        Transport("previous", 440, 281, 131, "Previous / hold to rewind", -1);
        Transport("play", 566, 273, 147, "Play / pause", 0);
        Transport("next", 708, 281, 131, "Next / hold to fast-forward", 1);
        for (int i = 0; i < 2; i++)
        {
            double x = 923 + i * 399;
            Canvas layer = new() { Clip = new RectangleGeometry(new Rect(0, 0, 342, 207), 4, 4), IsHitTestVisible = false };Put(layer, x, 115, 342, 207);
            Image needle = Image($"vu-{i + 1}-needle.png"); needle.Width = 8; needle.Height = 218;
            Canvas.SetLeft(needle, 167); Canvas.SetTop(needle, 40); needle.RenderTransform = needles[i];
            // One moving silhouette. A displaced effect reads as a second needle
            // and adds a separately rasterized moving surface.
            layer.Children.Add(needle);
            AddImage($"vu-{i + 1}-cover.png", x, 115, 342, 207);
            AddPeakIndicator(i,x+282,249);
            // Cooling filament tint is below the luminance layer, so it cannot
            // create an orange glow after the lamp has nearly faded out.
            Border warmth = new() { Background=new SolidColorBrush(Color.FromRgb(166,79,18)),Opacity=0,CornerRadius=new CornerRadius(4),IsHitTestVisible=false };Put(warmth,x,115,342,207);vuWarmth.Add(warmth);
            Border shade = new() { Background=Brushes.Black,Opacity=0,CornerRadius=new CornerRadius(4),IsHitTestVisible=false };Put(shade,x,115,342,207);vuDimmers.Add(shade);
            // The actual capture failure is discoverable at the affected meter;
            // TooltipPolicy applies the same bounded wrapping as other controls.
            var meterTip=HardwareTip("VU");
            Border meterStatus=new(){Background=Brushes.Transparent,ToolTip=meterTip};
            meterStatus.ToolTipOpening+=(_,_)=>meterTip.Content=!powerOn?"VU: power off":ResourcePolicy.IsGame?"VU: paused in Game mode":audio.CaptureStatus;
            Put(meterStatus,x,115,342,207);
        }
        // Match the radial 14 x 3 model insert, 43..57 units from the axis.
        double markerTop=3,markerBottom=17;
        var markerCap=PenLineCap.Flat;
        // The physical, unlit insert stays visible independently of playback.
        {
            var recess=new Line{X1=60,Y1=markerTop-.3,X2=60,Y2=markerBottom+.3,Stroke=new SolidColorBrush(Color.FromRgb(29,32,33)),StrokeThickness=3.6,StrokeStartLineCap=markerCap,StrokeEndLineCap=markerCap,RenderTransform=volumeRotation,IsHitTestVisible=false};Put(recess,1729,140,120,120);
            var plastic=new Line{X1=60,Y1=markerTop,X2=60,Y2=markerBottom,Stroke=new SolidColorBrush(Color.FromRgb(133,142,142)),StrokeThickness=3,StrokeStartLineCap=markerCap,StrokeEndLineCap=markerCap,RenderTransform=volumeRotation,IsHitTestVisible=false};Put(plastic,1729,140,120,120);
            volumePassiveIndicator=plastic;
        }
        var indicator=new Line{X1=60,Y1=markerTop,X2=60,Y2=markerBottom,Stroke=volumeLampBrush,StrokeThickness=3,Effect=new DropShadowEffect{Color=volumeLampBrush.Color,BlurRadius=2,ShadowDepth=0,Opacity=.18},StrokeStartLineCap=markerCap,StrokeEndLineCap=markerCap,IsHitTestVisible=false,RenderTransform=volumeRotation};
        // The rotary marker remains an illuminated indicator in both finishes.
        Put(indicator,1729,140,120,120);volumeIndicator=indicator;indicator.Opacity=.95;
        Button volumeHit=new(){Background=Brushes.Transparent,Cursor=RotaryCursor.Value,Tag="NoWindowDrag",ToolTip=new ToolTip { Content="Drag to turn · wheel to adjust volume\nClick to show / hide top bar", Background=new SolidColorBrush(Color.FromRgb(23,23,23)), Foreground=new SolidColorBrush(Color.FromRgb(221,211,193)), BorderBrush=new SolidColorBrush(Color.FromRgb(65,65,65)), Padding=new Thickness(10,7,10,7), FontFamily=new FontFamily("Segoe UI"), FontSize=13 }};
        var volumeTemplate=new FrameworkElementFactory(typeof(Ellipse));volumeTemplate.SetValue(Shape.FillProperty,Brushes.Transparent);volumeHit.Template=new ControlTemplate(typeof(Button)){VisualTree=volumeTemplate};
        Put(volumeHit,1714,125,150,150);
        Panel.SetZIndex(volumeHit,1); // Keep the tab gesture reachable above the OFF input shield.
        System.Windows.Automation.AutomationProperties.SetName(volumeHit,"Volume / show or hide top bar");
        volumeHit.PreviewMouseLeftButtonDown+=(_,e)=>{if(e.StylusDevice!=null||volumeTouch!=null){e.Handled=true;return;}volumeMouse=true;BeginVolumeGesture(e.GetPosition(Surface).Y);volumeHit.CaptureMouse();e.Handled=true;};
        volumeHit.PreviewMouseMove+=(_,e)=>{if(volumeMouse&&dragging&&e.StylusDevice==null)MoveVolumeGesture(e.GetPosition(Surface).Y);};
        volumeHit.PreviewMouseLeftButtonUp+=(_,e)=>{if(e.StylusDevice!=null||!volumeMouse){e.Handled=true;return;}volumeMouse=false;EndVolumeGesture(!new Rect(0,0,150,150).Contains(e.GetPosition(volumeHit)));volumeHit.ReleaseMouseCapture();e.Handled=true;};
        volumeHit.LostMouseCapture+=(_,_)=>{if(volumeMouse){volumeMouse=false;EndVolumeGesture(true);}};
        volumeHit.PreviewTouchDown+=(_,e)=>{if(volumeTouch==null&&!volumeMouse){volumeTouch=e.TouchDevice;BeginVolumeGesture(e.GetTouchPoint(Surface).Position.Y,true);volumeHit.CaptureTouch(e.TouchDevice);}e.Handled=true;};
        volumeHit.PreviewTouchMove+=(_,e)=>{if(ReferenceEquals(volumeTouch,e.TouchDevice))MoveVolumeGesture(e.GetTouchPoint(Surface).Position.Y);e.Handled=true;};
        volumeHit.PreviewTouchUp+=(_,e)=>{if(ReferenceEquals(volumeTouch,e.TouchDevice)){volumeTouch=null;EndVolumeGesture(!new Rect(0,0,150,150).Contains(e.GetTouchPoint(volumeHit).Position));volumeHit.ReleaseTouchCapture(e.TouchDevice);}e.Handled=true;};
        volumeHit.LostTouchCapture+=(_,e)=>{if(ReferenceEquals(volumeTouch,e.TouchDevice)){volumeTouch=null;EndVolumeGesture(true);}};
        volumeHit.Click+=(_,_)=>TopBarToggleRequested?.Invoke(this,EventArgs.Empty);
        volumeHit.PreviewKeyDown+=(_,e)=>{if(e.Key is Key.Up or Key.Right or Key.Down or Key.Left){SetVolumeValue(player.ConsoleVolume+(e.Key is Key.Up or Key.Right?1:-1));e.Handled=true;}};
        volumeHit.PreviewMouseWheel+=(_,e)=>{SetVolumeValue(player.ConsoleVolume+Math.Sign(e.Delta)*2);e.Handled=true;};
        AddAlbumEdgeShade();
        foreach(var rect in new[]{new Rect(76,60,319,319),new Rect(431,100,417,151),new Rect(923,115,342,207),new Rect(1322,115,342,207)})AddGlass(rect);
        foreach(var rect in new[]{new Rect(76,60,319,319),new Rect(431,100,417,151)})
        {
            var warmth=new Border{Background=new SolidColorBrush(Color.FromRgb(166,99,35)),Opacity=0,CornerRadius=new CornerRadius(4),IsHitTestVisible=false};
            Put(warmth,rect.X,rect.Y,rect.Width,rect.Height);screenWarmth.Add(warmth);
            var shade=new Border{Background=Brushes.Black,Opacity=0,CornerRadius=new CornerRadius(4),IsHitTestVisible=false};
            Put(shade,rect.X,rect.Y,rect.Width,rect.Height);screenDimmers.Add(shade);
        }
        AddPlayerControls();
        AddInstalledSkinLayers();
        AddPlaylistControls();
        Loaded += (_, _) => AttachMeterWindow();
        Unloaded += (_, _) => { DetachMeterWindow();ClearPreviousAlbum(); };
        IsVisibleChanged += (_, _) => UpdateMeterActivity();

    }
    internal void SetDimming(bool enabled,int seconds){dimEnabled=enabled;dimDelaySeconds=seconds;lastPlayingTime=clock.Elapsed.TotalSeconds;if(!enabled)dim=0;UpdateLights(PlaybackPlaying);}
    internal void SetTopBarHidden(bool hidden) { }

    private void UpdateAlbum(ImageSource? source)
    {
        if(ReferenceEquals(source,lastAlbum))return;
        int version=++albumTransitionVersion;
        albumPrevious.Source=album.Source;album.Source=source;lastAlbum=source;
        album.BeginAnimation(OpacityProperty,null);albumPrevious.BeginAnimation(OpacityProperty,null);
        album.Opacity=1;albumPrevious.Opacity=0;
        if(IsLoaded){
            var fade=new DoubleAnimation(1,0,TimeSpan.FromSeconds(.4));
            fade.Completed+=(_,_)=>{if(version==albumTransitionVersion)ClearPreviousAlbum();};
            album.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromSeconds(.4)));albumPrevious.BeginAnimation(OpacityProperty,fade);
        }else ClearPreviousAlbum();
    }
    private void ClearPreviousAlbum(){albumTransitionVersion++;albumPrevious.BeginAnimation(OpacityProperty,null);albumPrevious.Source=null;albumPrevious.Opacity=0;}
    internal void SetLighting(Color color,double intensity) { lampBrush.Color=color;SetVolumeLightColor(volumeLightColor);lightIntensity=Math.Clamp(intensity/100,0,1);UpdateLights(PlaybackPlaying); }
    private void UpdateLights(bool playing)
    {
        playing &= powerOn;
        if(!powerOn){pendingPlay=false;pendingLamps.Clear();pressedLamps.Clear();}
        if(playing||clock.Elapsed.TotalSeconds>=pendingPlayUntil)pendingPlay=false;
        double shadeAmount=powerOn?Math.Max(dim,externalDim):VuUnlitShade;
        double illumination=powerOn?lightIntensity*(1-shadeAmount):0;
        foreach(var shade in vuDimmers)shade.Opacity=powerOn?Math.Max(shadeAmount,GameVuShade):VuUnlitShade;
        foreach(var warmth in vuWarmth)warmth.Opacity=powerOn?GameVuWarmth:0;
        for(int i=0;i<screenDimmers.Count;i++)
        {
            double screenLevel=i==0?ScreenPowerLevel:SongPowerLevel;
            screenDimmers[i].Opacity=1-screenLevel*(1-Math.Max(dim,externalDim));
            screenWarmth[i].Opacity=powerOn?.24*screenLevel*(1-screenLevel):0;
        }
        RefreshPowerLight();
        if(widePlaylist!=null)RefreshPlaylistControls();
        RefreshPeakIndicators();
        for(int i=0;i<transportShades.Count;i++)
            // Passive aluminium keeps reflecting the room when playback/power
            // changes, just like the rotary. Only the light inserts switch off.
            transportShades[i].Opacity=0;
        if(volumeIndicator!=null)SetLampOpacity(volumeIndicator,powerOn?volumeLightIntensity*(1-externalDim):0);
        if(volumePassiveIndicator!=null&&instanceFinish=="militarity")volumePassiveIndicator.Opacity=powerOn?1:.18;
        if(volumeUnderlight!=null)SetLampOpacity(volumeUnderlight,powerOn?volumeLightIntensity*(1-externalDim):0);
        foreach(var (element,strength,symbol) in lamps)
        {
            bool pending=pendingLamps.Contains(element)||pendingPlay&&!directionalLamps.Contains(element);
            double level=pressedLamps.Contains(element)?illumination:pending?strength*illumination:
                !directionalLamps.Contains(element)?strength*illumination*(playing?1:.18):
                // Display-space fit to the approved Blender .035-emission render.
                // Blender emission is linear radiance, not an overlay opacity.
                instanceFinish=="light"&&symbol?strength*illumination*.176580765:0;
            SetLampOpacity(element,level);
        }
    }
    private void SetLampOpacity(UIElement element,double target)
    {
        if(powerOn&&Math.Abs((double)element.GetAnimationBaseValue(OpacityProperty)-target)<.0001)return;
        double previous=element.Opacity;
        element.BeginAnimation(OpacityProperty,null);element.Opacity=target;
        if(IsLoaded&&powerOn)
            element.BeginAnimation(OpacityProperty,new DoubleAnimation(previous,target,TimeSpan.FromMilliseconds(160)){FillBehavior=FillBehavior.Stop});
    }
    internal void BeginVolumeGesture(double y,bool touch=false){volumeDragThreshold=touch?12:6;dragging=true;volumeMoved=false;volumeStartY=y;volumeStartValue=player.ConsoleVolume;}
    internal void MoveVolumeGesture(double y){if(!dragging)return;if(Math.Abs(y-volumeStartY)>=volumeDragThreshold)volumeMoved=true;if(volumeMoved)SetVolumeValue(volumeStartValue+(volumeStartY-y)*100/140);}
    internal void EndVolumeGesture(bool cancelled){if(!dragging)return;dragging=false;if(!cancelled&&!volumeMoved)TopBarToggleRequested?.Invoke(this,EventArgs.Empty);}
    private void SetVolumeValue(double value){if(!powerOn)return;value=Math.Clamp(value,0,100);if(!Diagnostic)player.ConsoleVolume=value;volumeDisplayValue=value;volumeDisplayUntil=clock.Elapsed.TotalSeconds+1.8;RefreshVolumeReadout(clock.Elapsed.TotalSeconds);volumeRotation.Angle=-135+value*2.7;}
    internal void RefreshVolumeReadout(double now)
    {
        total.Text=now<volumeDisplayUntil?$"VOL {volumeDisplayValue:0}%":TimeSpan.FromSeconds(player.ConsoleDuration).ToString(@"m\:ss");
    }
    private void AddAlbumEdgeShade()
    {
        Put(MusicModuleParts.AlbumShade(319),76,60,319,319);
    }

    private void StartMeterRendering()
    {
        if(renderingSubscribed&&meterPump is {Completion.IsCompleted:false})return;
        // A completed worker must not leave a cached tab permanently frozen.
        // The metadata heartbeat retries this path while the signal is active.
        StopMeterRendering();
        renderingSubscribed=true;
        meterPump=new HighResolutionUiPump(Dispatcher,Tick,meterDisplay?.Current.Hertz??60);
    }
    private void StopMeterRendering()
    {
        meterPump?.Dispose();meterPump=null;renderingSubscribed=false;
    }
    internal double MeterFrameSeconds { get; private set; }
    private void Tick(double dt)
    {
        MeterFrameSeconds=dt;
        var level=powerOn?audio.ReadClassicVu():powerOffState.AfterSilence(
            (Stopwatch.GetTimestamp()-powerOffTimestamp)/(double)Stopwatch.Frequency);
        SetNeedles(level.Left,level.Right);
        ticks++;
        MeterPresented?.Invoke(Stopwatch.GetTimestamp());
    }
    internal double AdvanceVuLighting(bool playing,double now,double dt)
    {
        if(playing)lastPlayingTime=now;stoppedSeconds=now-lastPlayingTime;
        double target=dimEnabled&&stoppedSeconds>=dimDelaySeconds?.75:0;dim+=(target-dim)*(1-Math.Exp(-dt/(playing?.10:.33)));
        UpdateLights(playing);return dim;
    }
    internal void SetNeedles(double l, double r)
    {
        needles[0].Angle=ClassicVuMeter.Angle(l*vuSensitivity/VuSensitivityCalibration);needles[1].Angle=ClassicVuMeter.Angle(r*vuSensitivity/VuSensitivityCalibration);
        RefreshPowerLight();
        RefreshPeakIndicators();
    }
    internal void SetPreview()
    {
        Diagnostic = true; UpdateMeterActivity(); audio.SetActive(this, false);
        dim=0;lastPlayingTime=0;foreach(var shade in vuDimmers)shade.Opacity=0;
        title.Text = "Midnight Reverie"; artist.Text = "Luna Meridian"; time.Text = "2:48";total.Text="5:31";
        volumeDisplayUntil=0;volumeRotation.Angle=-135+74*2.7;
        SetProgress(168d/331);UpdateLights(true);
        SetNeedles(Math.Pow(10, -4.4/20),Math.Pow(10,-.5/20));
    }
    internal void PreviewStopped(double seconds) { dim=dimEnabled&&seconds>=dimDelaySeconds?.75:0;UpdateLights(false);SetNeedles(0,0); }
    private void SetProgress(double fraction) { progress.Width=397*Math.Clamp(fraction,0,1);Canvas.SetLeft(positionMark,441+ContentOffsetX+progress.Width-1.5); }
    private void Refresh()
    {
        RefreshPlaylistControls();
        UpdateAlbum(SpotifyWidget.CurrentAlbumArt);title.Text=player.ConsoleTitle;artist.Text=player.ConsoleArtist;audioDetails.Text=player.AudioDetails;
        time.Text=TimeSpan.FromSeconds(player.ConsolePosition).ToString(@"m\:ss");RefreshVolumeReadout(clock.Elapsed.TotalSeconds);SetProgress(player.ConsoleDuration>0?player.ConsolePosition/player.ConsoleDuration:0);
        double volume=player.ConsoleVolume;volumeRotation.Angle=-135+volume*2.7;UpdateLights(PlaybackPlaying);
    }
    private static async Task Safe(Func<Task> action) {try{await action();}catch(Exception e){InteractionDiagnostics.Log("Music console: "+e.Message);}}
    private void Transport(string name,double x,double y,double size,string label,int direction)
    {
        FrameworkElement LightMask(string part)
        {
            var layer=new Rectangle{Fill=lampBrush,OpacityMask=new ImageBrush(Bitmap(name+"-"+part+"-light-mask.png",finish:instanceFinish)),IsHitTestVisible=false};
            RenderOptions.SetBitmapScalingMode(layer,BitmapScalingMode.HighQuality);
            return layer;
        }
        FrameworkElement halo=LightMask("spill");
        Put(halo,x,y,size,size);lamps.Add((halo,1,false));
        var body=new Image{Source=WideBitmap(name+"-off.png",instanceFinish),Stretch=Stretch.Fill,IsHitTestVisible=false};Put(body,x,y,size,size);
        // Baked unlit inserts still reflect brightly. Shade only the button silhouette,
        // including its inset/rim, rather than darkening the surrounding metal panel.
        var bodyShade=new Rectangle{Fill=Brushes.Black,OpacityMask=new ImageBrush(body.Source),Opacity=0,IsHitTestVisible=false};
        Put(bodyShade,x,y,size,size);transportShades.Add(bodyShade);

        FrameworkElement rim=LightMask("rim");
        Put(rim,x,y,size,size);lamps.Add((rim,1,false));
        FrameworkElement symbol=LightMask("symbol");Put(symbol,x,y,size,size);lamps.Add((symbol,1,true));
        if(direction!=0){directionalLamps.Add(symbol);directionalLamps.Add(halo);directionalLamps.Add(rim);}
        Button hit=new(){Background=Brushes.Transparent,Cursor=Cursors.Hand,ToolTip=HardwareTip(label),Tag="NoWindowDrag"};var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.BackgroundProperty,Brushes.Transparent);hit.Template=new ControlTemplate(typeof(Button)){VisualTree=border};
        // Adjacent, non-overlapping invisible regions; the physical caps stay fixed.
        double hitLeft=direction<0?435:direction==0?572.5:706.5;
        double hitRight=direction<0?572.5:direction==0?706.5:844;
        Put(hit,hitLeft,248,hitRight-hitLeft,178);System.Windows.Automation.AutomationProperties.SetName(hit,label);
        int commandCount=0;bool pressed=false,held=false,busy=false,moved=false;double start=0;var hold=new DispatcherTimer(DispatcherPriority.Input){Interval=TimeSpan.FromMilliseconds(100)};double lastSeek=0;
        var seekGesture=new HeldSeek();long suppressClickUntil=0,suppressMouseUntil=0;TouchDevice? transportTouch=null;
        void Begin(){if(!powerOn||pressed||busy)return;suppressClickUntil=Environment.TickCount64+750;pressed=true;held=false;moved=false;start=clock.Elapsed.TotalSeconds;lastSeek=start;seekGesture.Begin(start,player.ConsolePosition,player.ConsoleDuration,direction);pressedLamps.Add(symbol);pressedLamps.Add(halo);pressedLamps.Add(rim);UpdateLights(PlaybackPlaying);if(direction!=0)hold.Start();}
        async Task Command()
        {
            if(!powerOn)return;
            commandCount++;
            long version=name=="play"?++playCommandVersion:playCommandVersion;
            pendingLamps.UnionWith(new UIElement[]{symbol,halo,rim});
            if(name=="play"){pendingPlay=!PlaybackPlaying;pendingPlayUntil=clock.Elapsed.TotalSeconds+5;}
            UpdateLights(PlaybackPlaying);
            try
            {
                bool accepted=await (Diagnostic?DiagnosticCommand!(name):player.ConsoleCommandAsync(name)).WaitAsync(TimeSpan.FromSeconds(5));
                if(!accepted&&name=="play"&&version==playCommandVersion)pendingPlay=false;
            }
            catch(Exception ex){if(name=="play"&&version==playCommandVersion)pendingPlay=false;InteractionDiagnostics.RecordFailure("Transport feedback",ex);}
            finally{if(--commandCount==0){pendingLamps.Remove(symbol);pendingLamps.Remove(halo);pendingLamps.Remove(rim);}UpdateLights(PlaybackPlaying);}
        }
        async Task End(bool cancel){cancel|=!powerOn;if(!pressed)return;suppressClickUntil=Environment.TickCount64+750;pressed=false;pressedLamps.Remove(symbol);pressedLamps.Remove(halo);pressedLamps.Remove(rim);hold.Stop();held|=direction!=0&&clock.Elapsed.TotalSeconds-start>=.4;if(!cancel&&held&&!moved&&!busy){if(Diagnostic)DiagnosticTransportAction?.Invoke("seek:"+name);else await Safe(()=>player.ConsoleSeekAsync(seekGesture.Position(clock.Elapsed.TotalSeconds)));}if(!cancel&&!held){if(Diagnostic&&DiagnosticCommand==null)DiagnosticTransportAction?.Invoke("skip:"+name);else await Command();}UpdateLights(PlaybackPlaying);}
        hold.Tick+=async(_,_)=>{double now=clock.Elapsed.TotalSeconds;if(!powerOn){await End(true);return;}if(!pressed||now-start<.4)return;held=true;if(busy||now-lastSeek<.25)return;lastSeek=now;moved=true;busy=true;try{if(Diagnostic)DiagnosticTransportAction?.Invoke("seek:"+name);else await Safe(()=>player.ConsoleSeekAsync(seekGesture.Position(now)));}finally{busy=false;}};
        hit.PreviewMouseLeftButtonDown+=(_,e)=>{if(e.StylusDevice!=null||transportTouch!=null||Environment.TickCount64<suppressMouseUntil){e.Handled=true;return;}Begin();hit.CaptureMouse();e.Handled=true;};
        hit.PreviewMouseLeftButtonUp+=async(_,e)=>{if(e.StylusDevice!=null||transportTouch!=null||Environment.TickCount64<suppressMouseUntil){e.Handled=true;return;}e.Handled=true;var task=End(!new Rect(0,0,hit.ActualWidth,hit.ActualHeight).Contains(e.GetPosition(hit)));hit.ReleaseMouseCapture();await task;};
        hit.LostMouseCapture+=async(_,_)=>await End(true);
        hit.PreviewTouchDown+=(_,e)=>{e.Handled=true;if(transportTouch!=null||pressed)return;transportTouch=e.TouchDevice;suppressMouseUntil=Environment.TickCount64+750;Begin();hit.CaptureTouch(e.TouchDevice);};
        hit.PreviewTouchMove+=(_,e)=>{if(pressed)e.Handled=true;};
        hit.PreviewTouchUp+=async(_,e)=>{e.Handled=true;if(!ReferenceEquals(transportTouch,e.TouchDevice))return;transportTouch=null;suppressMouseUntil=Environment.TickCount64+750;var task=End(!new Rect(0,0,hit.ActualWidth,hit.ActualHeight).Contains(e.GetTouchPoint(hit).Position));hit.ReleaseTouchCapture(e.TouchDevice);await task;};
        hit.LostTouchCapture+=async(_,e)=>{if(!ReferenceEquals(transportTouch,e.TouchDevice))return;transportTouch=null;suppressMouseUntil=Environment.TickCount64+750;await End(true);};
        hit.PreviewKeyDown+=(_,e)=>{if(e.Key is Key.Space or Key.Enter){if(!e.IsRepeat)Begin();e.Handled=true;}};
        hit.PreviewKeyUp+=async(_,e)=>{if(e.Key is Key.Space or Key.Enter){e.Handled=true;await End(false);}};
        hit.LostKeyboardFocus+=async(_,_)=>await End(true);
        hit.Click+=async(_,e)=>{e.Handled=true;if(pressed||Environment.TickCount64<suppressClickUntil)return;if(!Diagnostic||DiagnosticCommand!=null)await Command();};
        Unloaded+=async(_,_)=>{pendingPlay=false;pendingLamps.Clear();await End(true);};
    }
    private OpticallyCenteredText Text(double x,double y,double w,double h,double size,HorizontalAlignment alignment=HorizontalAlignment.Center)
    {
        var text=new OpticallyCenteredText{TextFontFamily=new FontFamily("Segoe UI"),TextFontWeight=FontWeights.Normal,TextFontSize=size,TextBrush=new SolidColorBrush(Color.FromRgb(224,193,150)),ClipToBounds=true,InkAlignment=alignment,Ellipsis=true,IsHitTestVisible=false};Put(text,x,y,w,h);return text;
    }
    private void AddGlass(Rect rect)
    {
        if(rect.X==76) return; // Album has a continuous dark edge; no bright rectangular glass outline.
        if(rect.X==431)
        {
            Put(MusicModuleParts.SongGlass(),rect.X,rect.Y,rect.Width,rect.Height);
            return;
        }
        Put(MusicModuleParts.VuGlass(),rect.X,rect.Y,rect.Width,rect.Height);
    }
    private void Put(FrameworkElement e,double x,double y,double w,double h){e.Width=w;e.Height=h;Canvas.SetLeft(e,x+(x<1700?ContentOffsetX:RotaryOffsetX));Canvas.SetTop(e,y-5);Surface.Children.Add(e);}
    private void AddChassis()
    {
        // A real stationary Blender backing closes the Power socket. It must
        // sit below the chassis: transparent cap/rim pixels must see the inside
        // of the enclosure, never the desktop behind the player.
        var powerBacking=Image("power-backing.png",finish:"dark");
        // Every installed finish is rendered at its final 1900x426 geometry.
        // A single Blender surface preserves grain across the entire chassis.
#if ADAPTIVE_PANEL
        var assembled = PanelBitmap(3072);
#else
        var assembled = WideBitmap("panel-static.png",instanceFinish);
#endif
        var chassis=new Image{Source=assembled,Width=1900,Height=426,Stretch=Stretch.Fill,IsHitTestVisible=false};
        RenderOptions.SetBitmapScalingMode(chassis,BitmapScalingMode.HighQuality);Surface.Children.Add(chassis);
#if ADAPTIVE_PANEL
        TrackPanelResolution(chassis);
#endif
        {
            // Shared DARK Blender light footprint for every finish.
            // Blender's saved compositor exports the physical light layer.
            // Runtime applies only the selected color and independent intensity.
            volumeUnderlight=new Rectangle{Fill=volumeLampBrush,OpacityMask=new ImageBrush(Bitmap("volume-light-layer.png",finish:"dark")),IsHitTestVisible=false};
            volumeUnderlight.Width=volumeUnderlight.Height=180;
            Canvas.SetLeft(volumeUnderlight,1705);Canvas.SetTop(volumeUnderlight,105);
            Surface.Children.Add(volumeUnderlight);
        }
        // Preserve the Blender surface without a runtime white wash.
        Put(powerBacking,1739,308,100,90);
        Panel.SetZIndex(powerBacking,-1);
        var rearBacking=Image("rear-backing.png",finish:"dark");
        rearBacking.Width=1900;rearBacking.Height=426;
        Surface.Children.Add(rearBacking);Panel.SetZIndex(rearBacking,-2);

    }
    private static readonly Dictionary<string,BitmapSource> images=new();
    private static readonly Dictionary<string,string?> imageFinishes=new();
    private const int SharedImageLimit=128;
    private static readonly Dictionary<string,WeakReference<BitmapSource>> sharedImages=new();
    private static readonly LinkedList<string> sharedImageOrder=new();
    private static long ImagePixelBytes(BitmapSource image)=>((long)image.PixelWidth*Math.Max(1,image.Format.BitsPerPixel)+7)/8*image.PixelHeight;
    // Preserve the previous skin-retention policy when a panel has a smaller decoded payload.
    internal static long ImageCacheRetentionCost=>images.Sum(item=>item.Key.Contains("/panel-static.png@",StringComparison.Ordinal)?5700L*1278*4:ImagePixelBytes(item.Value));
    internal static long ImageCachePixelBytes=>images.Values.Distinct().Sum(ImagePixelBytes);
    internal static long ImageCachePixelBytesIncluding(IEnumerable<BitmapSource> extra)=>images.Values.Concat(extra).Distinct().Sum(ImagePixelBytes);
    internal static object ImageCacheForAudit()=>images.Select(item=>new{key=item.Key,finish=imageFinishes.GetValueOrDefault(item.Key),width=item.Value.PixelWidth,height=item.Value.PixelHeight,pixelBytes=ImagePixelBytes(item.Value)}).ToArray();
    internal static object SharedImageCacheForAudit()=>new
    {
        entryLimit=SharedImageLimit,count=sharedImages.Count,
        entries=sharedImages.Select(item=>
        {
            bool live=item.Value.TryGetTarget(out var image);
            return new{key=item.Key,live,width=image?.PixelWidth,height=image?.PixelHeight,pixelBytes=image==null?0:ImagePixelBytes(image)};
        }).ToArray(),
        ownership="Weak references only; active visuals own their sources. Live payload estimates are not strong-cache or unique physical bytes."
    };
    internal static void TrimImageCache(ISet<string> retainedFinishes)
    {
        foreach(var key in images.Keys.Where(key=>imageFinishes.TryGetValue(key,out var owner)&&owner!=null&&!retainedFinishes.Contains(owner)).ToArray())
        {images.Remove(key);imageFinishes.Remove(key);}
    }
    private static void CacheImage(string key,BitmapSource image,string? ownerFinish)
    {
        if(ownerFinish==null)
        {
            // Shared hardware used to stay strongly rooted after its last view
            // was released. Reuse a live source without pinning its decoder or
            // pixels; the original loader handles an ordinary weak-cache miss.
            images.Remove(key);imageFinishes.Remove(key);
            sharedImageOrder.Remove(key);sharedImageOrder.AddLast(key);
            sharedImages[key]=new WeakReference<BitmapSource>(image);
            while(sharedImageOrder.Count>SharedImageLimit)
            {
                string oldest=sharedImageOrder.First!.Value;sharedImageOrder.RemoveFirst();sharedImages.Remove(oldest);
            }
            return;
        }
        sharedImages.Remove(key);sharedImageOrder.Remove(key);
        images[key]=image;imageFinishes[key]=ownerFinish;
    }
    private static bool TryCachedImage(string key,out BitmapSource image)
    {
        // Preserve explicit diagnostic image overrides as well as finish-owned
        // sources. Reading a dead weak entry must not decode it for an audit.
        if(images.TryGetValue(key,out image!))return true;
        if(sharedImages.TryGetValue(key,out var weak)&&weak.TryGetTarget(out image!))return true;
        image=null!;return false;
    }
    internal static void ReleaseUnusedModuleImages(bool player,bool vu,bool wide)
    {
        foreach(var key in images.Keys.Where(key=>
            (!wide&&key.StartsWith("Wide/",StringComparison.Ordinal))||
            (!player&&key.EndsWith("player-card-static.png",StringComparison.Ordinal))||
            (!vu&&!wide&&(key=="panel-static.png"||key.EndsWith("/panel-static.png",StringComparison.Ordinal)))).ToArray())
            CacheImage(key,images[key],null);
    }
    private static BitmapSource WideBitmap(string file,string finish)=>Bitmap($"Wide/{MusicFinish.Normalize(finish)}/{file}",legacy:true,finish:finish);
    internal static BitmapSource Bitmap(string file,bool legacy=false,string? finish=null)=>BitmapDecoded(file,legacy,finish,0);
    private static BitmapSource BitmapDecoded(string file,bool legacy,string? finish,int decodeWidth)
    {
        using var callbackTiming = new UiCallbackTiming("MusicConsole.Bitmap");
        finish??=MusicFinish.Name;
        finish=MusicFinish.Normalize(finish);
        if(MusicFinish.IsNewSkin(finish) && HasSkinAsset(finish,file))return SkinBitmap(finish,file);
        // Saved lighting is exported per finish; keep its reflected hardware
        // with the matching chassis. Interactive emission masks stay shared.
        bool ownHardware=file is "player-card-static.png" or "previous-off.png" or "play-off.png" or "next-off.png"
            or "power-push-on.png" or "power-push-off.png" or "power-brand.png"
            or "power-label.png" or "screw-head.png";
        string? ownerFinish=file.StartsWith("Wide/",StringComparison.Ordinal)?file.Split('/')[1]
            :legacy?null:(file is "shared-material.png" or "panel-static.png"||ownHardware)?finish:null;
        if(file is not ("shared-material.png" or "panel-static.png") && !ownHardware)finish=MusicFinish.HardwareFinish(finish);
        string key=(legacy?"":"Themes/"+(finish??MusicFinish.Name)+"/")+file;
        string resourceKey=key;
        if(decodeWidth>0)key+="@"+decodeWidth;
        if(TryCachedImage(key,out var image))return image;
        var original=new BitmapImage();original.BeginInit();if(decodeWidth>0)original.CacheOption=BitmapCacheOption.OnLoad;original.UriSource=new Uri("pack://application:,,,/Assets/MusicConsole/"+resourceKey);if(decodeWidth>0)original.DecodePixelWidth=decodeWidth;original.EndInit();original.Freeze();image=original;
        if(file.EndsWith("panel-static.png",StringComparison.Ordinal))
        {
            var b=new FormatConvertedBitmap(original,PixelFormats.Bgra32,null,0);int stride=b.PixelWidth*4;byte[] pixels=new byte[stride*b.PixelHeight];b.CopyPixels(pixels,stride,0);
            double scale=b.PixelWidth/1900d;
            for(int y=(int)(115*scale);y<(int)(322*scale);y++)for(int x=(int)((923+ContentOffsetX)*scale);x<(int)((1664+ContentOffsetX)*scale);x++){int p=y*stride+x*4;if(pixels[p+2]>pixels[p+1]*1.7&&pixels[p+1]>pixels[p]*1.3&&pixels[p+2]>65){pixels[p+2]=(byte)Math.Min(245,pixels[p+2]*1.55);pixels[p+1]=(byte)Math.Min(110,pixels[p+1]*1.55);pixels[p]=(byte)Math.Min(55,pixels[p]*1.25);}}
            image=BitmapSource.Create(b.PixelWidth,b.PixelHeight,96,96,PixelFormats.Bgra32,null,pixels,stride);image.Freeze();
        }
        CacheImage(key,image,ownerFinish);return image;
    }
    internal static (BitmapSource,BitmapSource) SymbolLayers(BitmapSource image,bool neutral=false)
    {
        using var callbackTiming = new UiCallbackTiming("MusicConsole.SymbolLayers");
        var b=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);int stride=b.PixelWidth*4;byte[] body=new byte[stride*b.PixelHeight],mask=new byte[body.Length];b.CopyPixels(body,stride,0);
        for(int y=0;y<b.PixelHeight;y++)for(int x=0;x<b.PixelWidth;x++){int p=y*stride+x*4;if(Math.Abs(x-b.PixelWidth*.5)>b.PixelWidth*.3||Math.Abs(y-b.PixelHeight*.5)>b.PixelHeight*.2)continue;double warm=Math.Clamp(Math.Min(body[p+2]-body[p]*1.12,body[p+1]-body[p]*1.05)/25,0,1);if(neutral)warm=Math.Abs(y-b.PixelHeight*.5)<b.PixelHeight*.06 && Math.Abs(x-b.PixelWidth*.5)<b.PixelWidth*.23 ? Math.Clamp((body[p+2]-65)/65d,0,1) : 0;
        if(body[p+2]<55)warm=0;mask[p]=mask[p+1]=mask[p+2]=255;mask[p+3]=(byte)(body[p+3]*warm);for(int c=0;c<3;c++)body[p+c]=(byte)(body[p+c]*(1-.9*warm));}
        BitmapSource Make(byte[] pixels){var result=BitmapSource.Create(b.PixelWidth,b.PixelHeight,96,96,PixelFormats.Bgra32,null,pixels,stride);result.Freeze();return result;}return(Make(body),Make(mask));
    }
    private Image Image(string file,string? finish=null){var image=new Image{Source=Bitmap(file,finish:finish??instanceFinish),Stretch=Stretch.Fill,IsHitTestVisible=false};RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.HighQuality);return image;}
    private Image AddImage(string file,double x,double y,double w,double h){Image e=Image(file);Put(e,x,y,w,h);return e;}
}













