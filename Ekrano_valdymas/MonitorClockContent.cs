using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace Ekrano_valdymas;
internal sealed class MonitorClockContent:Grid
{
    private readonly OpticallyCenteredText text=new(){Margin=new Thickness(4)};
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(1)};
    internal static ClockAppearance Decode(string? json){try{return JsonSerializer.Deserialize<ClockAppearance>(json??"")??new();}catch{return new();}}
    internal MonitorClockContent(string? json)
    {
        Children.Add(text);Decode(json).Apply(text);void Refresh()=>text.Text=DateTime.Now.ToString("HH:mm");
        timer.Tick+=(_,_)=>Refresh();Loaded+=(_,_)=>{Refresh();timer.Start();};Unloaded+=(_,_)=>timer.Stop();Refresh();
    }
}
public partial class SystemMonitorWidget
{
    internal static bool DropClock(Window owner,Point point,ClockAppearance appearance)
    {
        DependencyObject? hit=owner.InputHitTest(point) as DependencyObject;
        MonitorSlot? slot=null;
        for(var node=hit;node!=null;node=node is Visual?VisualTreeHelper.GetParent(node):null)
        {
            if(node is FrameworkElement {DataContext:MonitorSlot found})slot=found;
            if(node is SystemMonitorWidget monitor)
            {
                var transfer=new ShortcutTransfer(MacroLibraryCard.ClockTarget,"Clock",JsonSerializer.Serialize(appearance),(_,_,_)=>{});
                if(slot!=null)return (slot.Shortcut?.IsEmpty==true||!slot.IsActive)&&monitor.AcceptShortcutTransfer(slot,transfer);
                return monitor.AcceptFreeShortcutFromWindow(owner,point,transfer);
            }
        }
        return false;
    }
}
