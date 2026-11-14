using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace Ekrano_valdymas;
internal static class MicrophoneFeedback
{
    private static Popup? current;
    private static Action? dismiss;
    internal static FrameworkElement? DiagnosticContent=>current?.Child as FrameworkElement;
    internal static void Show(Window? owner,string device,bool muted)
        =>ShowState(owner,device,muted?"MIC MUTED":"MIC ON",muted);
    internal static void ShowState(Window? owner,string device,string state,bool off)
    {
        if(owner==null)return;
        dismiss?.Invoke();
        var text=new TextBlock{Text=$"{state}\n{device}",FontFamily=new FontFamily("Segoe UI"),FontSize=18,Foreground=Brushes.White,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,MaxWidth=420};
        var popup=new Popup{PlacementTarget=owner,Placement=PlacementMode.Center,AllowsTransparency=true,StaysOpen=true,IsHitTestVisible=false,
            Child=new Border{Background=new SolidColorBrush(Color.FromRgb(23,23,23)),BorderBrush=off?Brushes.OrangeRed:Brushes.MediumSeaGreen,BorderThickness=new Thickness(2),CornerRadius=new CornerRadius(8),Padding=new Thickness(18,12,18,12),Child=text}};
        var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(3)};
        void Close(){timer.Stop();popup.IsOpen=false;owner.Closed-=OwnerClosed;if(ReferenceEquals(current,popup)){current=null;dismiss=null;}}
        void OwnerClosed(object? sender,EventArgs e)=>Close();
        timer.Tick+=(_,_)=>Close();owner.Closed+=OwnerClosed;
        current=popup;dismiss=Close;popup.IsOpen=true;timer.Start();
    }
}

