using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Hud1920x480;

namespace Ekrano_valdymas;

internal sealed class HudVisualizerCard : UserControl
{
    private readonly Hud1920x480.AudioSpectrumService audio = new();
    private readonly WpfMusicVisualizer spectrum = new() { Width = 270, Height = 270 };
    private readonly ScaleTransform coreScale = new();
    private readonly ScaleTransform overallScale = new();
    private Rect fitEnvelopeBounds=Rect.Empty;
    private static readonly Geometry albumArtClip=CreateAlbumArtClip();
    private readonly Grid visual = new() { Width = 342, Height = 300, IsHitTestVisible = false };
    private readonly StackPanel musicLabel = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Image centerImage = new() { Name="HudVisualizerCenterImage", Width=92, Height=92, Stretch=Stretch.Uniform, HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Center, Visibility=Visibility.Collapsed };
    private ImageSource? sourceIcon, albumCover;
    private string? sourceName;
    private SettingsWindow? settingsWindow;
    private readonly DispatcherTimer poll = new(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly Func<bool> playing;
    private readonly double[] targets = new double[42];
    private readonly SolidColorBrush accent = new(), primary = new();
    private HudVisualizerProfile profile = HudVisualizerProfile.Load();
    private bool suspended, subscribed;
    private TimeSpan lastFrame;
    private TimeSpan nextFrame;
    private long refreshChecked;
    private int refreshHz;
    internal long FrameCount { get; private set; }
    internal Hud1920x480.AudioSpectrumRuntimeSnapshot AudioSnapshot => audio.CaptureRuntimeSnapshot();
    internal bool Preview { get; private set; }
    internal bool Running => subscribed;
    internal HudVisualizerProfile Profile => profile;
    internal WpfMusicVisualizer Spectrum => spectrum;
    internal double EffectiveOverallVisualSize => overallScale.ScaleX;
    internal Rect FitEnvelopeBounds => fitEnvelopeBounds;
    private static readonly (string Name,double Height,double Smoothing,double Glow,double Center)[] presets=
    [
        ("Calm",.24,.24,.18,.35),
        ("Balanced",.3875739644970404,.15,.356,.6),
        ("Bright",.48,.1,.6,.8),
        ("Soft",.16,.85,.12,.2),
        ("Pulse",.38,.02,.36,1),
        ("Dynamic",.68,.04,.48,.72)
    ];
    internal string? SelectedPreset
    {
        get
        {
            foreach(var preset in presets)
                if(Math.Abs(profile.MusicVisualizerHeight-preset.Height)<=1e-8&&Math.Abs(profile.MusicVisualizerSmoothing-preset.Smoothing)<=1e-8&&
                   Math.Abs(profile.MusicVisualizerCenterReaction-preset.Center)<=1e-8)return preset.Name;
            return null;
        }
    }
    internal Canvas Surface { get; } = new() { Width = 626, Height = 426 };
    internal HudVisualizerCard(Func<bool> playing)
    {
        this.playing = playing;
        var backing = new Border { Width = 626, Height = 426, CornerRadius = new CornerRadius(18) };
        backing.SetResourceReference(Border.BackgroundProperty, "PanelMetalBrush"); CommonMetal.SetSurface(backing, true); Surface.Children.Add(backing);
        var core = new Grid { RenderTransformOrigin = new Point(.5, .5), RenderTransform = coreScale };
        core.Children.Add(new Ellipse { Width = 150, Height = 150, Stroke = new SolidColorBrush(Color.FromArgb(54,255,255,255)), StrokeThickness = 1 });
        core.Children.Add(new Ellipse { Width = 132, Height = 132, Stroke = accent, StrokeThickness = 2, Opacity = .68 });
        // Preserve the original BlakiHUD core layout, including its font and underline.
        musicLabel.Children.Add(new TextBlock { Text = "MUSIC", FontFamily = new FontFamily(new Uri("pack://application:,,,/"), "./BlakiHud/Fonts/#Teko"), FontSize = 39, FontWeight = FontWeights.Bold, Foreground = primary, TextAlignment = TextAlignment.Center });
        musicLabel.Children.Add(new Border { Width = 78, Height = 3, Margin = new Thickness(0,-3,0,0), Background = accent });
        core.Children.Add(musicLabel);core.Children.Add(centerImage);visual.Children.Add(core); visual.Children.Add(spectrum);
        visual.RenderTransformOrigin=new Point(.5,.5);visual.RenderTransform=overallScale;
        Canvas.SetLeft(visual, (626-342)/2d); Canvas.SetTop(visual, (426-300)/2d); Surface.Children.Add(visual);
        var options = new ModuleOptionsButton { Width = 32, Height = 32 }; Canvas.SetLeft(options,566);Canvas.SetTop(options,22);Surface.Children.Add(options);
        Content = new Viewbox { Stretch = Stretch.Uniform, Child = Surface };
        Loaded += (_,_) => { SpotifyWidget.PlaybackStateChanged += PlaybackChanged; UpdateActivity(); };
        Unloaded += (_,_) => { SpotifyWidget.PlaybackStateChanged -= PlaybackChanged; Stop(); };
        IsVisibleChanged += (_,_) => UpdateActivity();
        poll.Tick += (_,_) => Poll();
        spectrum.SizeChanged+=(_,_)=>
        {
            fitEnvelopeBounds=Rect.Empty;
            if(IsLoaded&&spectrum.IsArrangeValid&&spectrum.ActualWidth>0&&spectrum.ActualHeight>0)Draw(subscribed||Preview);
        };
        RefreshColors();RefreshCenter();overallScale.ScaleX=overallScale.ScaleY=profile.OverallVisualSize;Draw(false);
    }
    internal static Color Palette(double p)
    {
        Color[] colors = [Color.FromRgb(245,245,242),Color.FromRgb(227,59,50),Color.FromRgb(229,108,0),Color.FromRgb(240,210,41),Color.FromRgb(58,181,91),Color.FromRgb(50,197,210),Color.FromRgb(52,120,229),Color.FromRgb(154,79,209),Color.FromRgb(12,12,12)];
        p=Math.Clamp(p,0,8);int lo=(int)p,hi=Math.Min(lo+1,8);double a=p-lo;
        return Color.FromRgb((byte)Math.Round(colors[lo].R+(colors[hi].R-colors[lo].R)*a),(byte)Math.Round(colors[lo].G+(colors[hi].G-colors[lo].G)*a),(byte)Math.Round(colors[lo].B+(colors[hi].B-colors[lo].B)*a));
    }
    private void RefreshColors() { accent.Color=CompactColorPicker.Parse(profile.CustomAccentColor,Palette(profile.AccentPosition));primary.Color=CompactColorPicker.Parse(profile.CustomTextColor,Palette(profile.TextColorPosition)); }
    internal void SetMediaContext(ImageSource? sourceIcon,ImageSource? albumCover,string? sourceName=null)
    {
        this.sourceIcon=sourceIcon;this.albumCover=albumCover;this.sourceName=sourceName;RefreshCenter();
    }
    private void RefreshCenter()
    {
        centerImage.Source=profile.CenterContent switch {"SourceIcon"=>sourceIcon,"AlbumCover"=>albumCover,_=>null};
        bool hasImage=centerImage.Source!=null;
        centerImage.Visibility=hasImage?Visibility.Visible:Visibility.Collapsed;
        musicLabel.Visibility=hasImage?Visibility.Collapsed:Visibility.Visible;
        bool cover=profile.CenterContent=="AlbumCover";
        centerImage.Stretch=cover?Stretch.UniformToFill:Stretch.Uniform;
        centerImage.Clip=cover?albumArtClip:null;
        centerImage.ToolTip=profile.CenterContent=="SourceIcon"?sourceName??"Music source":"Album cover";
    }
    internal void RefreshProfile()
    {
        profile.Normalize();RefreshColors();RefreshCenter();fitEnvelopeBounds=Rect.Empty;
        UpdateActivity();Draw(subscribed||Preview);
    }
    internal void SaveProfile()=>profile.Save();
    internal void RestoreProfile(HudVisualizerProfile previous){profile=previous.Copy();RefreshProfile();}
    internal void RestoreImportedProfile(){profile=HudVisualizerProfile.Imported();RefreshProfile();}
    internal void ApplyPreset(string key)
    {
        // These explicit options change motion, not the imported
        // palette, frequency band, overall geometry or selected center artwork.
        foreach(var preset in presets)
        {
            if(preset.Name!=key)continue;
            profile.MusicVisualizerHeight=preset.Height;profile.MusicVisualizerSmoothing=preset.Smoothing;
            profile.MusicVisualizerCenterReaction=preset.Center;
            RefreshProfile();return;
        }
    }
    private static Geometry CreateAlbumArtClip()
    {
        var clip=new EllipseGeometry(new Point(46,46),46,46);clip.Freeze();return clip;
    }
    private void UpdateOverallScale()
    {
        double requested=profile.OverallVisualSize;
        if(!spectrum.IsArrangeValid||spectrum.ActualWidth<=0||spectrum.ActualHeight<=0){overallScale.ScaleX=overallScale.ScaleY=requested;return;}
        // Drawing bounds include the real pen joins and halo strokes. Measuring
        // the 270-pixel layout rectangle would miss their possible protrusions.
        Rect bounds=spectrum.FrameInkBounds;
        if(profile.EffectiveComposition=="Mirrored")bounds.Union(spectrum.MirroredInkBounds);
        if(!bounds.IsEmpty)bounds.Offset((visual.Width-spectrum.Width)/2,(visual.Height-spectrum.Height)/2);
        double cx=visual.Width/2,cy=visual.Height/2;
        double coreRadius=75.5*coreScale.ScaleX;
        bounds.Union(new Rect(cx-coreRadius,cy-coreRadius,coreRadius*2,coreRadius*2));
        if(fitEnvelopeBounds.IsEmpty)fitEnvelopeBounds=bounds;else fitEnvelopeBounds.Union(bounds);
        // Retain the largest encountered envelope until settings change. New
        // peaks shrink safely before painting, without zooming back on each beat.
        double halfX=Math.Max(Math.Abs(fitEnvelopeBounds.Left-cx),Math.Abs(fitEnvelopeBounds.Right-cx));
        double halfY=Math.Max(Math.Abs(fitEnvelopeBounds.Top-cy),Math.Abs(fitEnvelopeBounds.Bottom-cy));
        double fit=Math.Min((Surface.Width/2-4)/Math.Max(1,halfX),(Surface.Height/2-4)/Math.Max(1,halfY));
        overallScale.ScaleX=overallScale.ScaleY=Math.Min(requested,fit);
    }
    internal void OpenSettings(Window owner)
    {
        if(settingsWindow is {IsVisible:true}){settingsWindow.Activate();return;}
        settingsWindow=HudVisualizerSettings.Create(this,owner);
        settingsWindow.Closed+=(_,_)=>settingsWindow=null;
        settingsWindow.ShowDialog();
    }
    private void PlaybackChanged(object? sender,EventArgs e) { if(Dispatcher.CheckAccess())UpdateActivity();else Dispatcher.BeginInvoke(UpdateActivity); }
    internal void SetSuspended(bool value) { suspended=value;UpdateActivity(); }
    private void UpdateActivity()
    {
        bool visible=profile.MusicVisualizerEnabled&&(!profile.MusicVisualizerAuto||playing()||Preview);
        visual.Visibility=visible?Visibility.Visible:Visibility.Hidden;
        if(!IsLoaded||!IsVisible||suspended||Preview||!visible||!playing()){Stop();if(!Preview)Draw(false);return;}
        audio.SetActive(!DiagnosticState.SuppressAudioCapture);poll.Start();
        if(!subscribed){subscribed=true;lastFrame=nextFrame=TimeSpan.Zero;VisualizerRenderClock.Subscribe(Frame);}
    }
    private void Stop() { poll.Stop();audio.SetActive(false);if(subscribed)VisualizerRenderClock.Unsubscribe(Frame);subscribed=false; }
    private void Poll()
    {
        var levels=audio.GetLinearFrequencyLevels(42,18,140,Math.Clamp(profile.MusicVisualizerSmoothing,0,1));levels.CopyTo(targets,0);
    }
    private void Frame(TimeSpan time)
    {
        if(time==lastFrame)return;
        if(Environment.TickCount64-refreshChecked>=1000){refreshChecked=Environment.TickCount64;var window=Window.GetWindow(this);if(window!=null)refreshHz=HudDisplayRefreshRate.Read(new System.Windows.Interop.WindowInteropHelper(window).Handle);}
        int rate=refreshHz;
        if(rate>0){if(time<nextFrame)return;var period=TimeSpan.FromSeconds(1d/rate);nextFrame=nextFrame==TimeSpan.Zero||time-nextFrame>=period?time+period:nextFrame+period;}
        lastFrame=time;FrameCount++;Draw(true);
    }
    private void Draw(bool active)
    {
        Span<double> levels=stackalloc double[42];
        for(int i=0;i<42;i++)levels[i]=active?targets[i]:.035+.015*(1+Math.Sin(i*1.37));
        var style=new MusicVisualizerStyle(79,Math.Clamp(profile.MusicVisualizerHeight,0,1),Math.Clamp(profile.MusicVisualizerScale,.6,1.1),Math.Clamp(profile.MusicVisualizerGlow,0,1),Math.Clamp(profile.MusicVisualizerSmoothing,0,1),Math.Clamp(profile.MusicVisualizerCenterReaction,0,1),Math.Clamp(profile.MusicVisualizerStartBin,0,40),Math.Clamp(profile.MusicVisualizerEndBin,1,41),profile.MusicVisualizerInvert);
        // Work in pre-Overall DIPs so the selected scale remains fixed during
        // playback. Reserve 4DIP for containment and 2DIP for layout rounding;
        // the renderer separately reserves the actual contour stroke radius.
        spectrum.ConfigureModuleEnvelope(
            (Surface.Width/2-6)/profile.OverallVisualSize,
            (Surface.Height/2-6)/profile.OverallVisualSize);
        spectrum.MirrorSpectrum=profile.EffectiveComposition=="Mirrored";
        spectrum.RadialBars=profile.EffectiveComposition=="RadialBars";
        double scale=spectrum.Update(levels,accent.Color,primary.Color,style);coreScale.ScaleX=coreScale.ScaleY=active?Math.Clamp(scale,1,1.28):1;
        UpdateOverallScale();
    }
    internal void SetPreview(double[] levels) { Preview=true;UpdateActivity();Array.Copy(levels,targets,Math.Min(42,levels.Length));Draw(true); }
    internal void EndPreview() { Preview=false;UpdateActivity(); }
    internal ContextMenu Options()
    {
        var menu=new ContextMenu();menu.SetResourceReference(StyleProperty,"MiniContextMenuStyle");
        var open=new MenuItem{Header="Visualizer settings…"};open.SetResourceReference(StyleProperty,"MiniMenuItemStyle");
        open.Click+=(_,_)=>{menu.IsOpen=false;if(Window.GetWindow(this) is Window owner)Dispatcher.BeginInvoke(new Action(()=>OpenSettings(owner)));};
        menu.Items.Add(open);return menu;
    }
}

