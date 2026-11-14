using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Ekrano_valdymas;

internal sealed partial class MusicPlayerCard : UserControl
{
    internal Canvas Surface { get; } = new() { Width=626,Height=426 };
    private readonly SpotifyWidget player;
    private readonly Image album=new() { Stretch=Stretch.UniformToFill,Clip=new RectangleGeometry(new Rect(0,0,213.52,213.52),5,5) };
    private readonly Image hardware=new(){Stretch=Stretch.Fill,IsHitTestVisible=false};
    internal Rect AlbumFrame {get;}=new(24,85,228,228);
    internal Rect SongFrame {get;}=new(264,84,334,228);
    internal Rect AlbumContent {get;}=new(31.24,92.24,213.52,213.52);
    internal Rect SongContent {get;}=new(264,84,334,228);
    internal List<object> ButtonMeasurements {get;}=new();
    private readonly OpticallyCenteredText title,artist,time,total;
    private readonly OpticallyCenteredText audioDetails;
    private readonly Rectangle progress=new() { Fill=new SolidColorBrush(Color.FromRgb(238,183,100)) };
    private readonly DispatcherTimer timer=new() { Interval=TimeSpan.FromMilliseconds(200) };
    internal bool Preview { get; private set; }
    private bool suspended;
    internal void SetPreviewArtwork(ImageSource image){if(Preview)album.Source=image;}
    internal event Action<string>? DiagnosticTransportAction;
    internal bool DiagnosticPlaying {get;set;}
    private readonly List<Action> lightingUpdates=new();
    private readonly List<Action> detachButtonObservers=new();
    private readonly SolidColorBrush lamp=new(Color.FromRgb(238,183,100));
    private double lightIntensity=1;
    private readonly HudEqualizer spectrum;
    private string finish="";
    private bool noSkin;
    private readonly List<Action<string>> finishUpdates=new();
    internal void RefreshFinish(){if(finish!=MusicFinish.Name||noSkin!=CommonMetal.NoSkin){finish=MusicFinish.Name;noSkin=CommonMetal.NoSkin;hardware.Source=MusicConsoleControl.Bitmap("player-card-static.png",finish:finish);foreach(var update in finishUpdates)update(finish);}}
    internal void SetLighting(Color color,double intensity){lamp.Color=color;lightIntensity=Math.Clamp(intensity/100,0,1);UpdateLights();}
    internal void UpdateLights(){foreach(var update in lightingUpdates)update();}

    internal MusicPlayerCard(SpotifyWidget player,AudioSpectrumService audio)
    {
        this.player=player;
        Background=Brushes.Transparent;Surface.Background=Brushes.Transparent;
        RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.HighQuality);
        Surface.Clip=new RectangleGeometry(new Rect(0,0,626,426),18,18);
        Content=new Viewbox { Stretch=Stretch.Uniform,Child=Surface };
        var backing=new Border{CornerRadius=new CornerRadius(18),BorderThickness=new Thickness(1.5),BorderBrush=new SolidColorBrush(Color.FromArgb(53,255,255,255))};backing.SetResourceReference(Border.BackgroundProperty,"PanelMetalBrush");CommonMetal.SetSurface(backing,true);Put(backing,0,0,626,426);
        Put(hardware,0,0,626,426);RefreshFinish();
        Put(album,31.24,92.24,213.52,213.52);Put(MusicModuleParts.AlbumShade(213.52,.55,.7),31.24,92.24,213.52,213.52);
        title=Text(28,345,570,36,27);artist=Text(28,383,570,28,19);title.InkAlignment=artist.InkAlignment=HorizontalAlignment.Center;
        audioDetails=Text(264,82,334,23,12);audioDetails.InkAlignment=HorizontalAlignment.Right;audioDetails.TextBrush=new SolidColorBrush(Color.FromRgb(170,165,155));
        spectrum=new HudEqualizer(audio,()=>player.IsPlaying);Put(spectrum,264,110,334,47);
        Put(new Rectangle { Fill=new SolidColorBrush(Color.FromArgb(55,160,160,160)) },264,175,334,3);
        progress.Fill=lamp;Put(progress,264,175,0,3);
        time=Text(264,181,130,25,16);total=Text(468,181,130,25,16);total.InkAlignment=HorizontalAlignment.Right;
        var seek=new Border { Background=Brushes.Transparent,Cursor=Cursors.Hand,Tag="NoWindowDrag",ToolTip="Seek" };
        Put(seek,259,163,344,17);
        seek.MouseLeftButtonUp+=async(_,e)=>{e.Handled=true;await Seek((e.GetPosition(seek).X-5)/334);};
        seek.TouchUp+=async(_,e)=>{e.Handled=true;await Seek((e.GetTouchPoint(seek).Position.X-5)/334);};
        // One common scale/translation of the finished console's three-button group.
        Button("previous",505.5,131,"Previous");Button("play",639.5,147,"Play / pause");Button("next",773.5,131,"Next");
        BuildAuxiliaryControls();BuildHeader();
        Loaded+=(_,_)=>{ResourcePolicy.Changed+=UpdateActivity;UpdateActivity();};Unloaded+=(_,_)=>{ResourcePolicy.Changed-=UpdateActivity;UpdateActivity();};IsVisibleChanged+=(_,_)=>UpdateActivity();
        timer.Tick+=(_,_)=>Refresh();
    }
    private async Task Seek(double ratio)
    {
        if(Preview)return;
        try { await player.ConsoleSeekAsync(Math.Clamp(ratio,0,1)*player.ConsoleDuration); }
        catch(Exception error) { InteractionDiagnostics.RecordFailure("Music card seek",error); }
    }
    private void Button(string command,double sourceCenter,double sourceSize,string label)
    {
        const double scale=.77;double size=sourceSize*scale,center=430+(sourceCenter-639.5)*78/134,x=center-size/2,y=261-size/2;
        var nativeLayers=new List<FrameworkElement>();
        Rectangle Mask(string part)
        {
            var layer=new Rectangle{Fill=lamp,IsHitTestVisible=false,Tag=command+" "+part};
            void Update(string f)=>layer.OpacityMask=new ImageBrush(MusicConsoleControl.Bitmap(command+"-"+part+"-light-mask.png",finish:f));
            Update(finish);finishUpdates.Add(Update);Put(layer,x,y,size,size);return layer;
        }
        var spill=Mask("spill");nativeLayers.Add(spill);
        var body=new Image{Source=MusicConsoleControl.Bitmap(command+"-off.png",finish:finish),Stretch=Stretch.Fill,IsHitTestVisible=false};Put(body,x,y,size,size);nativeLayers.Add(body);
        finishUpdates.Add(f=>body.Source=MusicConsoleControl.Bitmap(command+"-off.png",finish:f));
        double diameter=(sourceSize-48)*scale;
        var plainBody=new Ellipse{Fill=Brushes.Black,IsHitTestVisible=false};Put(plainBody,center-diameter/2,261-diameter/2,diameter,diameter);
        var rim=Mask("rim");nativeLayers.Add(rim);
        var symbol=Mask("symbol");nativeLayers.Add(symbol);
        var plainGeometry=Geometry.Parse(command=="play"
            ? "M0,0 L12,8 L0,16 Z M17,0 L18.8,0 L18.8,16 L17,16 Z M23,0 L24.8,0 L24.8,16 L23,16 Z"
            : "M0,0 L1.2,0 L1.2,14 L0,14 Z M3.6,7 L12.6,0 L12.6,14 Z M13.6,7 L22.6,0 L22.6,14 Z").Clone();
        if(command=="next")plainGeometry.Transform=new MatrixTransform(-1,0,0,1,22.6,0);
        double symbolHeight=command=="play"?11.806666666666667:10.523333333333333;
        double symbolWidth=symbolHeight*plainGeometry.Bounds.Width/plainGeometry.Bounds.Height;
        var plainSymbol=new System.Windows.Shapes.Path{Data=plainGeometry,Stretch=Stretch.Fill,Fill=lamp,IsHitTestVisible=false,Tag="No skin "+command};
        Put(plainSymbol,center-symbolWidth/2,261-symbolHeight/2,symbolWidth,symbolHeight);
        var halo=new Ellipse{Stroke=lamp,StrokeThickness=(command=="play"?2.6:1.8)*scale,Effect=new System.Windows.Media.Effects.BlurEffect{Radius=3.2*scale},IsHitTestVisible=false};
        Put(halo,center-diameter/2,261-diameter/2,diameter,diameter);
        var ring=new Ellipse{Stroke=lamp,StrokeThickness=2*scale,Effect=new System.Windows.Media.Effects.BlurEffect{Radius=.45*scale},IsHitTestVisible=false};
        Put(ring,center-diameter/2,261-diameter/2,diameter,diameter);
        void Appearance(string f)
        {
            foreach(var layer in nativeLayers)layer.Visibility=CommonMetal.NoSkin?Visibility.Hidden:Visibility.Visible;
            plainSymbol.Visibility=plainBody.Visibility=halo.Visibility=ring.Visibility=CommonMetal.NoSkin?Visibility.Visible:Visibility.Hidden;
        }
        finishUpdates.Add(Appearance);Appearance(finish);
        int pending=0;bool pressed=false;DateTime pendingPlayUntil=DateTime.MinValue;
        void Light()
        {
            bool playing=Preview?DiagnosticPlaying:player.IsPlaying;
            if(playing)pendingPlayUntil=DateTime.MinValue;
            // The one-module player is always powered: keep all three inlays readable
            // at the wide player's 18% standby level, including before the first play.
            double strength=pressed?1:pending>0||DateTime.UtcNow<pendingPlayUntil?1:playing?(command=="play"?1:.25):.18;
            symbol.Opacity=plainSymbol.Opacity=ring.Opacity=rim.Opacity=spill.Opacity=strength*lightIntensity;
            halo.Opacity=strength*lightIntensity*(command=="play"?.75:.4);
        }
        lightingUpdates.Add(Light);Light();
        ButtonMeasurements.Add(new{command,center,centerY=261,size,sourceSize,scale,physicalInlayMask=true,maskSource="original wide-player symbol-light-mask",nativeBodySource="original per-skin off render",proceduralRim="No skin only"});
        var button=new Button { Background=Brushes.Transparent,BorderThickness=new Thickness(0),Cursor=Cursors.Hand,Tag="NoWindowDrag",ToolTip=label };
        var hit=new FrameworkElementFactory(typeof(Border));hit.SetValue(Border.BackgroundProperty,Brushes.Transparent);button.Template=new ControlTemplate(typeof(Button)){VisualTree=hit};
        System.Windows.Automation.AutomationProperties.SetName(button,label);
        var pressedProperty=System.ComponentModel.DependencyPropertyDescriptor.FromProperty(System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty,typeof(Button));
        EventHandler pressedChanged=(_,_)=>{pressed=button.IsPressed;Light();};
        pressedProperty.AddValueChanged(button,pressedChanged);
        detachButtonObservers.Add(()=>pressedProperty.RemoveValueChanged(button,pressedChanged));
        button.Click+=async(_,_)=>
        {
            if(DiagnosticTransportAction!=null){DiagnosticTransportAction(command);return;}
            if(Preview)return;
            pending++;bool starting=command=="play"&&!player.IsPlaying;Light();
            try { bool accepted=await player.ConsoleCommandAsync(command);if(accepted&&starting)pendingPlayUntil=DateTime.UtcNow.AddSeconds(5);Refresh(); }
            catch(Exception error) { InteractionDiagnostics.RecordFailure("Music card transport",error); }
            finally { pending--;Light(); }
        };
        Put(button,x+24*scale,y+24*scale,(sourceSize-48)*scale,(sourceSize-48)*scale);
    }
    private void UpdateActivity()
    {
        bool active=IsLoaded&&IsVisible&&!Preview&&!suspended;
        player.SetCardActive(this,active);
        timer.Interval=ResourcePolicy.IsSaving?TimeSpan.FromSeconds(1):TimeSpan.FromMilliseconds(200);
        if(active){Refresh();timer.Start();}else timer.Stop();
    }
    internal void SetSuspended(bool value){suspended=value;spectrum.SetSuspended(value);UpdateActivity();}
    internal void Release()
    {
        SetSuspended(true);
        foreach(var detach in detachButtonObservers)detach();
        detachButtonObservers.Clear();
    }
    private void Refresh()
    {
        if(Preview)return;
        UpdateLights();RefreshFinish();RefreshVolume();SyncSymbolColor();RefreshServiceHeading();
        audioDetails.Text=player.AudioDetails;
        title.Text=player.ConsoleTitle;artist.Text=player.ConsoleArtist;album.Source=SpotifyWidget.CurrentAlbumArt;
        time.Text=TimeSpan.FromSeconds(player.ConsolePosition).ToString(@"m\:ss");total.Text=TimeSpan.FromSeconds(player.ConsoleDuration).ToString(@"m\:ss");
        progress.Width=334*Math.Clamp(player.ConsoleDuration>0?player.ConsolePosition/player.ConsoleDuration:0,0,1);
    }
    internal void SetPreview()
    {
        Preview=true;DiagnosticPlaying=true;timer.Stop();player.SetCardActive(this,false);title.Text="Midnight Reverie";artist.Text="Luna Meridian";time.Text="2:48";total.Text="5:31";progress.Width=191;album.Source=SpotifyWidget.CurrentAlbumArt??Bitmap("SpotifyStandby.jpg");UpdateLights();spectrum.SetPreview();RefreshVolume();
    }
    private OpticallyCenteredText Text(double x,double y,double width,double height,double size)
    {
        var text=new OpticallyCenteredText { TextFontFamily=new FontFamily("Segoe UI"),TextFontSize=size,TextFontWeight=FontWeights.Normal,TextBrush=Brushes.White,InkAlignment=HorizontalAlignment.Left,Ellipsis=true,ClipToBounds=true };
        Put(text,x,y,width,height);return text;
    }
    private void Put(FrameworkElement element,double x,double y,double width,double height)
    { element.Width=width;element.Height=height;Canvas.SetLeft(element,x);Canvas.SetTop(element,y);Surface.Children.Add(element); }
    private static BitmapSource Bitmap(string name)
    { var image=new BitmapImage(new Uri("pack://application:,,,/Assets/"+name));image.Freeze();return image; }
}



