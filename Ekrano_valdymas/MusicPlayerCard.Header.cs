using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Ekrano_valdymas;
internal sealed partial class MusicPlayerCard
{
    private OpticallyCenteredText? serviceHeading;
    internal Button? SettingsButton {get;private set;}
    private void BuildHeader()
    {
        foreach(double y in new[]{68d,330d})Put(new Rectangle{Fill=new SolidColorBrush(Color.FromArgb(65,180,180,180)),IsHitTestVisible=false},28,y,570,1);
        var mark=Text(28,25,26,32,25);mark.Text="♫";mark.InkAlignment=HorizontalAlignment.Center;
        serviceHeading=Text(64,25,255,32,25);serviceHeading.Tag="ModuleTitle";serviceHeading.TextFontFamily=(FontFamily)FindResource("TekoFont");serviceHeading.TextFontWeight=FontWeights.Bold;
        serviceHeading.SetResourceReference(OpticallyCenteredText.TextBrushProperty,"ModuleTitleBrush");
        RefreshServiceHeading();
        var gear=new OpticallyCenteredText{Text="\uE713",TextFontFamily=new FontFamily("Segoe MDL2 Assets"),TextFontSize=23,TextBrush=Brushes.White,InkAlignment=HorizontalAlignment.Center};
        gear.SetResourceReference(OpticallyCenteredText.TextBrushProperty,"PrimaryTextBrush");
        var button=new Button{Content=gear,Background=Brushes.Transparent,BorderBrush=new SolidColorBrush(Color.FromArgb(90,180,180,180)),BorderThickness=new Thickness(1),Cursor=Cursors.Hand,Tag="NoWindowDrag",ToolTip="Player settings"};
        var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(5));border.SetValue(Border.BorderThicknessProperty,new Thickness(1));border.SetValue(Border.BackgroundProperty,Brushes.Transparent);border.SetValue(Border.BorderBrushProperty,button.BorderBrush);
        var content=new FrameworkElementFactory(typeof(ContentPresenter));border.AppendChild(content);button.Template=new ControlTemplate(typeof(Button)){VisualTree=border};
        button.Click+=(_,e)=>{e.Handled=true;if(Window.GetWindow(this) is MainWindow main)main.OpenCardSettings(this,button);};
        Put(button,574,24,36,36);SettingsButton=button;
    }
    private void RefreshServiceHeading(){if(serviceHeading!=null)serviceHeading.Text=player.SelectedService switch{MusicService.Local=>"LOCAL PLAYER",MusicService.AppleMusic=>"APPLE MUSIC",MusicService.AmazonMusic=>"AMAZON MUSIC",_=>player.SelectedService.ToString().ToUpperInvariant()};}
}
public partial class MainWindow
{
    internal void OpenMusicSources(FrameworkElement anchor)
    {
        var menu=NewModuleMenu();
        foreach(var service in Enum.GetValues<MusicService>().Where(s=>s!=MusicService.MusicBee).OrderBy(s=>s==MusicService.Local?0:1))
        {
            var item=new MenuItem{Header=service switch{MusicService.Local=>"Local player",MusicService.AppleMusic=>"Apple Music",MusicService.AmazonMusic=>"Amazon Music",_=>service.ToString()},IsCheckable=true,IsChecked=selectedMusicService==service};
            item.Click+=(_,_)=>{SetMusicService(service);SaveWindowSettings();};menu.Items.Add(item);
        }
        AddDefaultSourceIconOption(menu);
        menu.PlacementTarget=anchor;menu.Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom;menu.IsOpen=true;
    }
    internal void OpenCardSettings(MusicPlayerCard card,FrameworkElement anchor)
    {
        var menu=NewModuleMenu();
        var source=new MenuItem{Header="Music source"};menu.Items.Add(source);
        foreach(var service in Enum.GetValues<MusicService>().Where(s=>s!=MusicService.MusicBee).OrderBy(s=>s==MusicService.Local?0:1))
        {
            var item=new MenuItem{Header=service switch{MusicService.Local=>"Local player — requires Playlist",MusicService.AppleMusic=>"Apple Music",MusicService.AmazonMusic=>"Amazon Music",_=>service.ToString()},IsCheckable=true,IsChecked=selectedMusicService==service};
            item.Click+=(_,_)=>{SetMusicService(service);SaveWindowSettings();};source.Items.Add(item);
        }
        AddDefaultSourceIconOption(source);
        var font=new MenuItem{Header="Font…"};font.Click+=(_,_)=>CreatePlayerTextEditor().ShowDialog();menu.Items.Add(font);
        menu.PlacementTarget=anchor;menu.Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom;menu.IsOpen=true;
    }
}
