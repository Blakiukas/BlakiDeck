using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ekrano_valdymas;

internal static class SensorSupportWindow
{
    internal static SettingsWindow Create(Window? owner, Func<string> status, Action retry)
    {
        var window=AppearanceEditorUi.Window("Sensor support",owner,360);
        window.Width=620;
        var root=new DockPanel{Margin=new Thickness(20,8,20,18)};
        var footer=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,16,0,0)};
        var text=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=16,Foreground=Brushes.WhiteSmoke};
        var refresh=MacroUi.Button("Refresh",(_,_)=>text.Text=status());
        footer.Children.Add(refresh);
        footer.Children.Add(MacroUi.Button("Retry access",(_,_)=>{retry();text.Text=status();}));
        var close=MacroUi.Button("Close",(_,_)=>window.Close());close.IsCancel=true;footer.Children.Add(close);
        DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var content=new StackPanel();content.Children.Add(text);
        content.Children.Add(new TextBlock{Text="PawnIO provides access to advanced hardware sensors. A missing reading can also mean that the selected device does not expose that sensor.",TextWrapping=TextWrapping.Wrap,FontSize=15,Foreground=Brushes.Silver,Margin=new Thickness(0,16,0,12)});
        content.Children.Add(MacroUi.Button("PawnIO website…",(_,_)=>
        {
            try { Process.Start(new ProcessStartInfo("https://pawnio.eu/"){UseShellExecute=true}); }
            catch { MessageBox.Show(window,"The website could not be opened. Visit https://pawnio.eu/ in your browser.","Sensor support",MessageBoxButton.OK,MessageBoxImage.Information); }
        }));
        root.Children.Add(MacroUi.Scroll(content));window.Content=root;
        text.Text=status();AppearanceEditorUi.Frame(window,300,420);
        return window;
    }
}
