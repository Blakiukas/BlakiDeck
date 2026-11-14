using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace Ekrano_valdymas;

// Explicit feedback also works during touch capture, without waiting for hover.
internal sealed class VuAdjustmentFeedback
{
    private readonly Func<double> readValue;
    private readonly ToolTip tip;
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(1200)};
    private bool held;

    internal VuAdjustmentFeedback(FrameworkElement owner,Func<double> readValue)
    {
        this.readValue=readValue;
        tip=new ToolTip{PlacementTarget=owner,Placement=PlacementMode.Top,VerticalOffset=-8,
            StaysOpen=true,IsHitTestVisible=false,FontSize=18,Padding=new Thickness(12,8,12,8),
            Background=new SolidColorBrush(Color.FromRgb(32,33,33)),Foreground=Brushes.White,
            BorderBrush=new SolidColorBrush(Color.FromRgb(88,90,90))};
        owner.ToolTip=tip;
        tip.Opened+=(_,_)=>Refresh();
        timer.Tick+=(_,_)=>{timer.Stop();tip.IsOpen=false;};
        owner.Unloaded+=(_,_)=>{held=false;timer.Stop();tip.IsOpen=false;};
        Refresh();
    }
    private void Refresh()=>tip.Content="VU "+(readValue()/MusicConsoleControl.DefaultVuSensitivity*100).ToString("0",CultureInfo.CurrentCulture)+"%";
    internal void Begin(){held=true;Show();}
    internal void End(){held=false;Show();}
    internal void Show(){timer.Stop();Refresh();tip.IsOpen=true;if(!held)timer.Start();}
}
