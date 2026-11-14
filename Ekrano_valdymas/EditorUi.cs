using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ekrano_valdymas;

internal static class EditorUi
{
    internal static SettingsWindow Window(string title, Window? owner, double width=540, double height=610) => new()
    {
        Title=title, Owner=owner, Width=width, Height=height, MinWidth=400, MinHeight=380,
        WindowStartupLocation=WindowStartupLocation.CenterOwner, Background=new SolidColorBrush(Color.FromRgb(35,38,40)),
        Foreground=Brushes.White, FontFamily=new FontFamily("Segoe UI"), FontSize=16
    };
    internal static Button Button(string text, RoutedEventHandler action)
    {
        var b=new Button { Content=text, MinHeight=40, Padding=new Thickness(12,5,12,5), Margin=new Thickness(4) };
        b.Click+=action;return b;
    }
    internal static void Field(Panel panel,string label, FrameworkElement control)
    {
        panel.Children.Add(new TextBlock { Text=label, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(4,5,4,2) });
        if(control is TextBox input)input.VerticalContentAlignment=VerticalAlignment.Center;
        control.Margin=new Thickness(4,2,4,2);control.MinHeight=36;panel.Children.Add(control);
    }
}
