using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Ekrano_valdymas;
internal static class BuiltInIconGallery
{
    internal static string Category(string name)=>name.StartsWith("OBS",StringComparison.OrdinalIgnoreCase)||name.StartsWith("Restart")||name.StartsWith("Sleep")||name.StartsWith("Shut down")||name.StartsWith("Web cam")||name is "mic.png" or "mute mic.png" or "Macros.png" or "Philips Hue.png"?"Macros":name is "Amazon_music.png" or "Apple_music.png" or "Calc.png" or "ChatGPT.png" or "Chrome.png" or "Deezer.png" or "Discord.png" or "MusicBee.png" or "NZTX.png" or "PowerShell.png" or "Qobuz.png" or "SoundCloud.png" or "Spotify.png" or "Steam.png" or "Steam2.png" or "Telegram.png" or "Tidal.png"?"Apps":"Other";
    internal static SettingsWindow Create(Window? owner,string category,Action<string> select)
    {
        var window=AppearanceEditorUi.Window("Built-in icons",owner,620);window.Width=640;
        var root=new DockPanel{Margin=new Thickness(18,4,18,12)};window.Content=root;
        var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        footer.Children.Add(MacroUi.Button("Cancel",(_,_)=>window.Close()));
        var tabs=SettingsEditorTabs.Create("BuiltInIconCategories");root.Children.Add(tabs);
        foreach(string group in new[]{"Macros","Apps","Other"})
        {
            var tiles=new WrapPanel();var tab=new TabItem{Header=group,Content=MacroUi.Scroll(tiles)};tabs.Items.Add(tab);
            if(group==category)tabs.SelectedItem=tab;
            if(!Directory.Exists(DefaultIconCatalog.PackagedDirectory))continue;
            foreach(string path in Directory.EnumerateFiles(DefaultIconCatalog.PackagedDirectory,"*.png").Where(p=>Category(Path.GetFileName(p))==group).OrderBy(Path.GetFileName))
            {
                var content=new StackPanel();content.Children.Add(new Image{Source=IconBitmap.Load(path),Width=56,Height=56,Stretch=Stretch.Uniform});
                content.Children.Add(new TextBlock{Text=Path.GetFileNameWithoutExtension(path),TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,FontSize=12,Margin=new Thickness(0,4,0,0)});
                var button=MacroUi.Button("",(_,_)=>{select(path);window.DialogResult=true;});button.Content=content;button.Width=108;button.Height=120;button.ToolTip=Path.GetFileNameWithoutExtension(path);tiles.Children.Add(button);
            }
        }
        AppearanceEditorUi.Frame(window);return window;
    }
    internal static string? Choose(Window? owner,string category="Apps")
    {
        string? selected=null;Create(owner,category,path=>selected=path).ShowDialog();return selected;
    }
}
