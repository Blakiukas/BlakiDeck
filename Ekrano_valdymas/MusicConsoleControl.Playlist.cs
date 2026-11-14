using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
namespace Ekrano_valdymas;

internal sealed partial class MusicConsoleControl
{
    private readonly Canvas playlistLayer=new(){Width=1900,Height=426,Visibility=Visibility.Collapsed};
    private PlaylistCard widePlaylist=null!;
    private readonly List<(ToggleButton button,Image image,Rectangle light,string name)> playlistButtons=new();
    private MusicService? playlistSource;
    private bool playlistVisible;
    private Rectangle? sourcesLight;
    internal bool IntegratedPlaylistVisible=>playlistLayer.Visibility==Visibility.Visible;
    internal bool PlaylistButtonLit=>playlistButtons[0].button.IsChecked==true;
    internal string PlaylistSummary=>widePlaylist.Summary;

    private void AddPlaylistControls()
    {
        var frame=new Image{Source=Bitmap("playlist-wide-frame.png",legacy:true),IsHitTestVisible=false};frame.Width=1900;frame.Height=426;
        playlistLayer.Children.Add(frame);
        widePlaylist=new PlaylistCard(embedded:true){Width=831,Height=331};
        Canvas.SetLeft(widePlaylist,856);Canvas.SetTop(widePlaylist,49);playlistLayer.Children.Add(widePlaylist);
        if(instanceFinish=="militarity")playlistLayer.Children.Add(new Image{Source=WideBitmap("audio-dept-overlay.png",instanceFinish),Width=1900,Height=426,IsHitTestVisible=false});
        Surface.Children.Add(playlistLayer);
        string[] names={"playlist","repeat","shuffle"};
        for(int i=0;i<names.Length;i++)
        {
            string name=names[i];var image=new Image{Source=WideBitmap($"playlist-{name}-off.png",instanceFinish),Stretch=Stretch.Fill,IsHitTestVisible=false};
            RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.HighQuality);
            var light=new Rectangle{Fill=lampBrush,OpacityMask=new ImageBrush(WideBitmap($"playlist-{name}-light-mask.png",instanceFinish)),Opacity=0,IsHitTestVisible=false};
            RenderOptions.SetBitmapScalingMode(light,BitmapScalingMode.HighQuality);
            var layers=new Grid();layers.Children.Add(image);layers.Children.Add(light);
            var button=new ToggleButton{Width=52,Height=33,Content=layers,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Tag="NoWindowDrag",ToolTip=char.ToUpper(name[0])+name[1..],Cursor=System.Windows.Input.Cursors.Hand};
            var presenter=new FrameworkElementFactory(typeof(ContentPresenter));
            var hitSurface=new FrameworkElementFactory(typeof(Border));hitSurface.SetValue(Border.BackgroundProperty,Brushes.Transparent);hitSurface.AppendChild(presenter);
            button.Template=new ControlTemplate(typeof(ToggleButton)){VisualTree=hitSurface};
            System.Windows.Automation.AutomationProperties.SetName(button,(string)button.ToolTip);
            // Same Blender geometry/crop translation: match the visible frame rim,
            // rather than its mostly dark nominal machining bounds.
            Canvas.SetLeft(button,399+i*51);Canvas.SetTop(button,44.5);Surface.Children.Add(button);
            button.Click+=async(_,_)=>{
                if(!powerOn){RefreshPlaylistControls();return;}
                if(name=="playlist")playlistVisible=!playlistVisible;
                else await Safe(()=>player.ConsoleModeAsync(name=="shuffle"));
                RefreshPlaylistControls();
            };
            playlistButtons.Add((button,image,light,name));
        }
        var sourceLayers=new Grid();
        var sourceBody=new Image{Source=WideBitmap("sources-off.png",instanceFinish),Stretch=Stretch.Fill,IsHitTestVisible=false};RenderOptions.SetBitmapScalingMode(sourceBody,BitmapScalingMode.HighQuality);sourceLayers.Children.Add(sourceBody);
        sourcesLight=new Rectangle{Fill=lampBrush,OpacityMask=new ImageBrush(WideBitmap("sources-light-mask.png",instanceFinish)),IsHitTestVisible=false};RenderOptions.SetBitmapScalingMode(sourcesLight,BitmapScalingMode.HighQuality);sourceLayers.Children.Add(sourcesLight);
        sourceLayers.Width=122;sourceLayers.Height=33;Canvas.SetLeft(sourceLayers,710);Canvas.SetTop(sourceLayers,44.5);Surface.Children.Add(sourceLayers);
        var sources=new Button{Background=Brushes.Transparent,BorderThickness=new Thickness(0),Tag="NoWindowDrag",ToolTip="Sources",Cursor=System.Windows.Input.Cursors.Hand};
        var sourceHit=new FrameworkElementFactory(typeof(Border));sourceHit.SetValue(Border.BackgroundProperty,Brushes.Transparent);sources.Template=new ControlTemplate(typeof(Button)){VisualTree=sourceHit};
        sources.Width=37;sources.Height=37;Canvas.SetLeft(sources,796.5);Canvas.SetTop(sources,42.5);Surface.Children.Add(sources);System.Windows.Automation.AutomationProperties.SetName(sources,"Sources");
        sources.Click+=(_,e)=>{e.Handled=true;if(powerOn&&Window.GetWindow(this) is MainWindow main)main.OpenMusicSources(sources);};
        Loaded+=(_,_)=>RefreshPlaylistControls();
    }
    private void RefreshPlaylistControls()
    {
        bool local=player.SelectedService==MusicService.Local;
        if(playlistSource!=player.SelectedService){playlistSource=player.SelectedService;playlistVisible=local;}
        playlistLayer.Visibility=local&&playlistVisible?Visibility.Visible:Visibility.Collapsed;
        widePlaylist.SetActive(local&&powerOn);widePlaylist.IsEnabled=powerOn;
        widePlaylist.Opacity=SongPowerLevel;
        if(sourcesLight!=null)SetLampOpacity(sourcesLight,powerOn?lightIntensity*(1-Math.Max(dim,externalDim))*.18:0);
        foreach(var (button,image,light,name) in playlistButtons)
        {
            bool enabled=powerOn&&(name=="playlist"?local:name=="shuffle"?player.CanShuffle:player.CanRepeat);
            bool lit=powerOn&&(name=="playlist"?local&&playlistVisible:name=="shuffle"?player.ShuffleActive==true:player.RepeatActive==true);
            button.IsEnabled=enabled;button.IsChecked=lit;
            SetLampOpacity(light,powerOn?lightIntensity*(1-Math.Max(dim,externalDim))*(lit?1:.18):0);
        }
    }
    internal void ActivateSelectedPlaylist(){playlistSource=null;RefreshPlaylistControls();}
    internal void DiagnosticTogglePlaylist(){if(DiagnosticState.Root==null)throw new InvalidOperationException();playlistVisible=!playlistVisible;RefreshPlaylistControls();}
    internal object PlaylistLightingEvidence()=>new{color=lampBrush.Color.ToString(),intensity=lightIntensity,dim,externalDim,powerOn,buttons=playlistButtons.Select(b=>new{b.name,state=b.button.IsChecked,opacity=b.light.Opacity,passive=b.image.Source.ToString()}).ToArray()};
    internal double PlaylistEmissionLevel=>playlistButtons[0].light.Opacity;
    internal double SourcesEmissionLevel=>sourcesLight?.Opacity??0;
    internal PlaylistCard EmbeddedPlaylist=>widePlaylist;
    internal object ApprovedLayoutEvidence()
    {
        if(DiagnosticState.Root==null)throw new InvalidOperationException();
        var buttons=playlistButtons.Select(b=>new{b.name,box=new Rect(Canvas.GetLeft(b.button),Canvas.GetTop(b.button),b.button.Width,b.button.Height),physicalBezel=new Rect(Canvas.GetLeft(b.button)+2,Canvas.GetTop(b.button)+2,48,29)}).ToArray();
        if(buttons.Any(b=>Math.Abs(b.physicalBezel.Y-46.5)>.001))throw new Exception("Button model/crop placement differs");
        if(Math.Abs(Canvas.GetTop(title)-101)>.001||Math.Abs(Canvas.GetTop(progress)-197)>.001)throw new Exception("Information content must move with its frame by20");
        var source=Surface.Children.OfType<Button>().Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Sources");
        var sourceBox=new Rect(Canvas.GetLeft(source),Canvas.GetTop(source),source.Width,source.Height);
        if(Math.Abs(sourceBox.X+sourceBox.Width/2-815)>.001||Math.Abs(sourceBox.Y+sourceBox.Height/2-61)>.001)throw new Exception("Sources must align with the Blender chassis aperture");
        return new{buttons,sources=new{hitBox=sourceBox,physicalBezel=new Rect(800.5,46.5,29,29),requestedRelation="29 design-unit diameter; same top/bottom as rectangular row; center matches Blender aperture"},nominalAlbumFrameTop=44.5,visibleEdgeMeasurement="See raster edge evidence; nominal bounds are not visible edges",informationFrame=new Rect(398.5,84.5,438,172),informationShift=20,transportShift=30,titleBox=new Rect(Canvas.GetLeft(title),Canvas.GetTop(title),title.Width,title.Height),titleInk=title.GetRenderedInkBounds(),title.TextFontSize,font=title.TextFontFamily.Source,reference="output/playlist-example/measurements.json"};
    }
    internal void SetApprovedLayoutPreview()
    {
        if(DiagnosticState.Root==null)throw new InvalidOperationException();
        SetPreview();title.Text="Easterville - Radio Edit";artist.Text="Ludwig Armstrong";
        time.Text="0:02";total.Text="3:22";SetProgress(2d/202);widePlaylist.SetPreview();RefreshPlaylistControls();
    }
}


