using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
namespace Ekrano_valdymas;
internal static class PlayerLayoutDiagram
{
    internal static FrameworkElement Create(string finish="dark")
    {
        var panel=new StackPanel{Margin=new Thickness(0,8,0,0)};
        var canvas=new Canvas{Width=1900,Height=426,IsHitTestVisible=false};
        void Label(string text,double x,double y,double width,double height,double size=28){var label=new OpticallyCenteredText{Text=text,TextFontFamily=new FontFamily("Segoe UI"),TextFontSize=size,TextBrush=Brushes.WhiteSmoke,FitToBox=true,Width=width,Height=height};Canvas.SetLeft(label,x);Canvas.SetTop(label,y);canvas.Children.Add(label);}
        void Box(double x,double y,double width,double height,bool round=false){System.Windows.Shapes.Shape shape=round?new System.Windows.Shapes.Ellipse():new System.Windows.Shapes.Rectangle{RadiusX=8,RadiusY=8};shape.Stroke=Brushes.Gray;shape.StrokeThickness=2;shape.Fill=new SolidColorBrush(Color.FromRgb(32,32,32));shape.Width=width;shape.Height=height;Canvas.SetLeft(shape,x);Canvas.SetTop(shape,y);canvas.Children.Add(shape);}
        // Frame the complete player, including the volume and power controls.
        var chassis=new System.Windows.Shapes.Rectangle{Width=1896,Height=422,RadiusX=20,RadiusY=20,Stroke=Brushes.Gray,StrokeThickness=2,Fill=new SolidColorBrush(Color.FromRgb(25,25,25))};
        Canvas.SetLeft(chassis,2);Canvas.SetTop(chassis,2);canvas.Children.Add(chassis);
        void AdjustmentScrew(double centerX)
        {
            Box(centerX-16,335,32,32,true);
            var slot=new System.Windows.Shapes.Line{X1=centerX-9,Y1=351,X2=centerX+9,Y2=351,Stroke=Brushes.WhiteSmoke,StrokeThickness=3,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round};
            canvas.Children.Add(slot);
            Label("↕",centerX+22,335,24,32,23);
        }
        Box(40,44.5,335,335);Label("Album cover",55,160,305,70);
        Box(398.5,84.5,438,172);Label("Track title",414,106,404,45);Label("Artist",414,152,404,35,24);Label("0:00  ─────────  3:45",414,205,404,35,23);
        for(int i=0;i<3;i++){Box(399+i*51,44.5,48,29);Label(new[]{"≡♪","↻","⤨"}[i],399+i*51,44.5,48,29,22);}
        Label("SOURCES",710,44.5,85,29,17);Box(800.5,46.5,29,29,true);var sourcesChevron=new System.Windows.Shapes.Path{Data=Geometry.Parse("M1,1 L8,8 L15,1"),Width=16,Height=9,Stroke=Brushes.WhiteSmoke,StrokeThickness=2,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round};Canvas.SetLeft(sourcesChevron,807);Canvas.SetTop(sourcesChevron,56.5);canvas.Children.Add(sourcesChevron);
        Box(418,281,100,100,true);Label("|◀",418,281,100,100,26);
        Box(544,273,120,120,true);Label("▶Ⅱ",544,273,120,120,32);
        Box(686,281,100,100,true);Label("▶|",686,281,100,100,26);
        Box(856,49,831,331);Box(894,92,345,220);Box(1304,92,345,220);
        Label("Left VU",910,180,310,50);Label("Right VU",1320,180,310,50);
        AdjustmentScrew(1065);AdjustmentScrew(1475);
        Label("Playlist / VU",1060,53,440,32,24);
        Label("VOLUME",1720,80,150,28,20);Box(1725,125,140,140,true);Label("↕",1725,125,140,140,35);
        Label("POWER",1754,288,80,26,18);Box(1765,325,60,60,true);Label("⏻",1765,325,60,60,32);
        panel.Children.Add(new Viewbox{Stretch=Stretch.Uniform,Child=canvas,Margin=new Thickness(0,0,0,24)});
        void Guide(string name,string description){var row=new TextBlock{Foreground=Brushes.WhiteSmoke,FontSize=15,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,3,0,3)};row.Inlines.Add(new Run(name+": "){Foreground=Brushes.DarkOrange,FontWeight=FontWeights.SemiBold});row.Inlines.Add(new Run(description));panel.Children.Add(row);}
        Guide("Sources", "choose Local player, Spotify or another available music source.");
        Guide("Playlist / Repeat / Shuffle", "show the local playlist instead of VU meters; repeat or shuffle when supported by the selected source.");
        Guide("Playlist", "double-click a track to play; single-click to select. Drag to reorder. Folder opens the music browser; bin clears the list; gear opens its appearance settings.");
        Guide("Play / pause · Previous / Next", "tap to control playback; hold Previous / Next to rewind or fast-forward when supported.");
        Guide("Seek / time", "tap the progress bar to jump within the track. Format / quality is shown only when reported by the source.");
        Guide("Volume", "drag vertically or turn the mouse wheel. Click without dragging to show or hide the top bar.");
        Guide("Power", "switch the player off and pause music; press again to switch on.");
        Guide("VU sensitivity", "drag either adjustment screw vertically or use its mouse wheel; double-click to reset. Local and streaming sensitivity are saved separately.");
        return panel;
    }
}
