using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Ekrano_valdymas;
internal static class BuiltInIconGallery
{
    internal static readonly string[] Sets=["Original","Flat"];
    internal static string SetOf(string file)=>file.StartsWith("3D--",StringComparison.Ordinal)?"Flat":file.StartsWith("Flat--",StringComparison.Ordinal)?"Retired":"Original";
    internal static string DisplayName(string file)=>SetOf(file)=="Original"?file:file[(file.IndexOf("--",StringComparison.Ordinal)+2)..];
    internal static string Category(string name)=>name.StartsWith("OBS",StringComparison.OrdinalIgnoreCase)||name.StartsWith("Restart")||name.StartsWith("Sleep")||name.StartsWith("Shut down")||name.StartsWith("Web cam")||name is "mic.png" or "mute mic.png" or "Macros.png" or "Philips Hue.png"?"Macros":name is "Amazon_music.png" or "Apple_music.png" or "Calc.png" or "ChatGPT.png" or "Chrome.png" or "Deezer.png" or "Discord.png" or "MusicBee.png" or "NZTX.png" or "PowerShell.png" or "Qobuz.png" or "SoundCloud.png" or "Spotify.png" or "Steam.png" or "Steam2.png" or "Telegram.png" or "Tidal.png"?"Apps":"Other";
    internal static SettingsWindow Create(Window? owner,string category,Action<string> select)
    {
        var window=AppearanceEditorUi.Window("Built-in icons",owner,620);window.Width=640;
        var root=new DockPanel{Margin=new Thickness(18,4,18,12)};window.Content=root;
        var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        footer.Children.Add(MacroUi.Button("Cancel",(_,_)=>window.Close()));
        var tabs=SettingsEditorTabs.Create("BuiltInIconCategories");root.Children.Add(tabs);
        string selectedSet="Original";
        void Populate()
        {
        string selectedCategory=(tabs.SelectedItem as TabItem)?.Header as string??category;
        tabs.Items.Clear();
        foreach(string group in new[]{"Macros","Apps","Other"})
        {
            var tiles=new WrapPanel();var tab=new TabItem{Header=group,Content=MacroUi.Scroll(tiles)};tabs.Items.Add(tab);
            if(group==selectedCategory)tabs.SelectedItem=tab;
            if(!Directory.Exists(DefaultIconCatalog.PackagedDirectory))continue;
            foreach(string path in Directory.EnumerateFiles(DefaultIconCatalog.PackagedDirectory,"*.png").Where(p=>SetOf(Path.GetFileName(p))==selectedSet&&Category(DisplayName(Path.GetFileName(p)))==group).OrderBy(Path.GetFileName))
            {
                var content=new StackPanel();content.Children.Add(new Image{Source=IconBitmap.Load(path),Width=56,Height=56,Stretch=Stretch.Uniform});
                string label=Path.GetFileNameWithoutExtension(DisplayName(Path.GetFileName(path)));
                content.Children.Add(new TextBlock{Text=label,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,FontSize=12,Margin=new Thickness(0,4,0,0)});
                var button=MacroUi.Button("",(_,_)=>{select(path);window.DialogResult=true;});button.Content=content;button.Tag=path;button.Width=108;button.Height=120;button.ToolTip=label;tiles.Children.Add(button);
            }
        }
        }
        Populate();
        AppearanceEditorUi.Frame(window);
        // This header belongs only to the gallery; other editor headers keep their layout.
        var surface=(Grid)window.Content;
        var frame=(DockPanel)((Border)surface.Children[0]).Child;
        var header=(DockPanel)frame.Children[0];
        var title=(OpticallyCenteredText)header.Children[header.Children.Count-1];
        title.InkAlignment=HorizontalAlignment.Left;
        var selector=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(8,0,8,0)};
        DockPanel.SetDock(selector,Dock.Right);header.Children.Insert(header.Children.Count-1,selector);
        foreach(string set in Sets)
        {
            var button=MacroUi.Button(set,(_,_)=>{selectedSet=set;Populate();UpdateSelection();});
            button.Name="IconSet"+set.Replace("3D","Sculpted");button.MinWidth=58;button.Height=40;
            button.Margin=new Thickness(3,0,0,0);button.Tag=set;selector.Children.Add(button);
        }
        void UpdateSelection(){foreach(Button b in selector.Children)b.Background=Equals(b.Tag,selectedSet)?new SolidColorBrush(Color.FromRgb(72,74,76)):new SolidColorBrush(Color.FromRgb(37,35,31));}
        UpdateSelection();window.MinWidth=620;return window;
    }
    internal static string? Choose(Window? owner,string category="Apps")
    {
        string? selected=null;Create(owner,category,path=>selected=path).ShowDialog();return selected;
    }
}

