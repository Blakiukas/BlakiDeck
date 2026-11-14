using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
namespace Ekrano_valdymas;

internal static class TextEditMenu
{
    internal static void Opening(object sender,ContextMenuEventArgs e)
    {
        if(sender is not TextBox input)return;
        var menu=Create(input);menu.Placement=e.CursorLeft<0?PlacementMode.Bottom:PlacementMode.MousePoint;
        input.ContextMenu=menu;e.Handled=true;menu.IsOpen=true;
    }
    internal static ContextMenu Create(Control input)
    {
        var menu=new ContextMenu{PlacementTarget=input,Placement=PlacementMode.Bottom};
        menu.SetResourceReference(FrameworkElement.StyleProperty,"MiniContextMenuStyle");
        menu.Background=new SolidColorBrush(Color.FromRgb(26,23,20));
        foreach(var (label,keys,command) in new[]{("Cut","Ctrl+X",ApplicationCommands.Cut),("Copy","Ctrl+C",ApplicationCommands.Copy),("Paste","Ctrl+V",ApplicationCommands.Paste)})
        {
            var header=new Grid{MinWidth=150};header.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            header.Children.Add(new TextBlock{Text=label});var shortcut=new TextBlock{Text=keys,Margin=new Thickness(28,0,0,0),Foreground=Brushes.Silver};Grid.SetColumn(shortcut,1);header.Children.Add(shortcut);
            var item=new MenuItem{Header=header,Command=new ClipboardCommand(command,input)};item.SetResourceReference(FrameworkElement.StyleProperty,"MiniMenuItemStyle");menu.Items.Add(item);
        }
        return menu;
    }
    private sealed class ClipboardCommand(RoutedCommand command,Control target):ICommand
    {
        public event EventHandler? CanExecuteChanged{add=>CommandManager.RequerySuggested+=value;remove=>CommandManager.RequerySuggested-=value;}
        private static bool ClipboardUnavailable(Exception ex)=>ex is System.Runtime.InteropServices.ExternalException or UnauthorizedAccessException;
        public bool CanExecute(object? parameter)
        {
            try{return command.CanExecute(parameter,target);}
            catch(Exception ex) when(ClipboardUnavailable(ex)){return false;}
        }
        public void Execute(object? parameter)
        {
            try{command.Execute(parameter,target);}
            catch(Exception ex) when(ClipboardUnavailable(ex)){AppMessage.Show(Window.GetWindow(target),"The Windows clipboard is currently unavailable. Please try again.");}
        }
    }
}
