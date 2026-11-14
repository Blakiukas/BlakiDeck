using System.Windows;
using System.Windows.Controls;
namespace Ekrano_valdymas;

// Product messages share settings chrome, ownership and work-area fitting.
// Native file/OS permission dialogs remain owned by their system providers.
internal static class AppMessage
{
    internal static SettingsWindow Create(Window? owner,string text,string title,MessageBoxButton buttons=MessageBoxButton.OK)
    {
        owner=SettingsWindow.Sessions.LastOrDefault()??owner??Application.Current.Windows.OfType<Window>().FirstOrDefault(w=>w.IsActive)??Application.Current.MainWindow;
        var window=MacroUi.Window(title,owner,520,260);window.Tag=MessageBoxResult.Cancel;window.MinHeight=180;
        var root=new DockPanel{Margin=new Thickness(20)};window.Content=root;
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,14,0,0)};DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var results=buttons switch{MessageBoxButton.YesNo=>new[]{MessageBoxResult.Yes,MessageBoxResult.No},MessageBoxButton.YesNoCancel=>new[]{MessageBoxResult.Yes,MessageBoxResult.No,MessageBoxResult.Cancel},MessageBoxButton.OKCancel=>new[]{MessageBoxResult.OK,MessageBoxResult.Cancel},_=>new[]{MessageBoxResult.OK}};
        foreach(var result in results){var button=MacroUi.Button(result.ToString(),(_,_)=>{window.Tag=result;window.DialogResult=true;});button.IsCancel=result==MessageBoxResult.Cancel;button.IsDefault=result==MessageBoxResult.OK;actions.Children.Add(button);}
        root.Children.Add(MacroUi.Scroll(new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap}));return window;
    }
    internal static MessageBoxResult Show(string text,string title="BlakiDeck",MessageBoxButton buttons=MessageBoxButton.OK,MessageBoxImage image=MessageBoxImage.None)=>Show(null,text,title,buttons,image);
    internal static MessageBoxResult Show(Window? owner,string text,string title="BlakiDeck",MessageBoxButton buttons=MessageBoxButton.OK,MessageBoxImage image=MessageBoxImage.None)
    {
        if(!Application.Current.Dispatcher.CheckAccess())return Application.Current.Dispatcher.Invoke(()=>Show(owner,text,title,buttons,image));
        var window=Create(owner,text,title,buttons);window.ShowDialog();return window.Tag is MessageBoxResult result?result:MessageBoxResult.Cancel;
    }
}
