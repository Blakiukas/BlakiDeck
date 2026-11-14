using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ekrano_valdymas;

internal sealed class ShortcutRenameDialog:SettingsWindow
{
    private readonly TextBox nameBox;
    public string ShortcutName=>nameBox.Text.Trim();
    public ShortcutRenameDialog(string currentName)
    {
        Title="Rename shortcut";Width=470;Height=230;MinWidth=340;MinHeight=200;
        ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=new SolidColorBrush(Color.FromRgb(26,23,20));Foreground=Brushes.White;
        FontFamily=new FontFamily("Segoe UI");FontSize=16;MacroUi.ApplyTheme(this);
        var root=new DockPanel{Margin=new Thickness(18)};Content=root;
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,14,0,0)};
        DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var save=MacroUi.Button("Save",(_,_)=>{if(ShortcutName.Length>0)DialogResult=true;});save.IsDefault=true;
        var cancel=MacroUi.Button("Cancel",(_,_)=>Close());cancel.IsCancel=true;actions.Children.Add(save);actions.Children.Add(cancel);
        nameBox=new TextBox{Text=currentName,MinHeight=34,VerticalContentAlignment=VerticalAlignment.Center};
        root.Children.Add(nameBox);save.IsEnabled=ShortcutName.Length>0;
        nameBox.TextChanged+=(_,_)=>save.IsEnabled=ShortcutName.Length>0;
        Loaded+=(_,_)=>{if(ShowActivated){nameBox.Focus();nameBox.SelectAll();}};
    }
}
