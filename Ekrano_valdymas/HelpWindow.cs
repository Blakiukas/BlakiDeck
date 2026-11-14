using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Ekrano_valdymas;
internal sealed class HelpWindow : SettingsWindow
{
    internal string DisplayLanguage {get;private set;}="en";
    internal HelpWindow() : this(null) { }
    internal HelpWindow(string? document)
    {
        Title="BlakiDeck Help";Width=850;Height=850;ShowInTaskbar=false;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;
        ResizeMode=ResizeMode.CanResize;Foreground=Brushes.WhiteSmoke;
        DialogWorkArea.Attach(this,540,360,deferDuringNativeMoveResize:true);
        System.Windows.Shell.WindowChrome.SetWindowChrome(this,new(){CaptionHeight=0,ResizeBorderThickness=new Thickness(6),GlassFrameThickness=new Thickness(0)});
        var panel=new DockPanel{Margin=new Thickness(24)};
        Content=new Border{Background=DocumentAppearance.Background,CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(1),BorderBrush=DocumentAppearance.Border,Child=panel};
        var header=new DockPanel{Margin=new Thickness(0,0,0,16),Background=Brushes.Transparent};
        DockPanel.SetDock(header,Dock.Top);panel.Children.Add(header);
        var close=MacroUi.Button("Close",(_,_)=>Close());close.IsCancel=true;DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
        close.Margin=new Thickness(16,0,0,0);
        var choice=new LanguageSwitch(){VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(choice,Dock.Right);header.Children.Add(choice);
        var title=new OpticallyCenteredText{Text="HELP",TextFontSize=28,TextBrush=Brushes.White,InkAlignment=HorizontalAlignment.Left};header.Children.Add(title);
        var text=new TextBox{Name="HelpText",IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,Background=Brushes.Transparent,Foreground=Brushes.WhiteSmoke,BorderThickness=new Thickness(0),FontFamily=new FontFamily("Segoe UI"),FontSize=16,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        var chrome=new ListBox();PlaylistAppearance.ApplyChrome(chrome);
        text.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)]=chrome.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)];
        panel.Children.Add(text);
        void Translate()
        {
            DisplayLanguage=choice.SelectedLanguage;
            string resource=document==null?"BlakiDeck.Help."+DisplayLanguage:"BlakiDeck.Legal."+document+(DisplayLanguage=="lt"?".lt":"");
            using var stream=typeof(HelpWindow).Assembly.GetManifestResourceStream(resource)!;
            using var reader=new StreamReader(stream);
            string content=reader.ReadToEnd();
            // The distributable originals also contain summaries in other languages.
            // Show only the selected language in the reader.
            if(DisplayLanguage=="en"&&document=="License")content=content.Split("Lietuviška santrauka",StringSplitOptions.None)[0].TrimEnd();
            if(DisplayLanguage=="en"&&document=="Privacy")content=content.Split("LIETUVIŲ",StringSplitOptions.None)[0].Replace("ENGLISH","").TrimEnd();
            text.Text=content;text.Select(0,0);text.ScrollToHome();
            title.Text=DisplayLanguage=="lt"?"PAGALBA":"HELP";
            close.Content=DisplayLanguage=="lt"?"Uždaryti":"Close";
            Title=DisplayLanguage=="lt"?"BlakiDeck pagalba":"BlakiDeck Help";
            if(document!=null) {
                string heading=document switch {"License"=>DisplayLanguage=="lt"?"Licencija":"License","Privacy"=>DisplayLanguage=="lt"?"Privatumas":"Privacy",_=>DisplayLanguage=="lt"?"Komponentų licencijos":"Third-party notices"};
                title.Text=heading;Title="BlakiDeck — "+heading;
            }
        }
        choice.SelectionChanged+=(_,_)=>Translate();Translate();
    }
}
