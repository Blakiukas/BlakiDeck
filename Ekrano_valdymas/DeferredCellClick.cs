using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
namespace Ekrano_valdymas;

// Wait for Windows' double-click interval before running a single-click command.
internal sealed class DeferredCellClick
{
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
    private readonly DispatcherTimer timer = new();
    private Action? pending;
    internal DeferredCellClick(FrameworkElement owner)
    {
        timer.Tick += (_, _) => { var action = pending; Cancel(); action?.Invoke(); };
        owner.Unloaded += (_, _) => Cancel();
    }
    internal void Schedule(Action action)
    {
        Cancel(); pending = action;
        timer.Interval = TimeSpan.FromMilliseconds(GetDoubleClickTime()); timer.Start();
    }
    internal void Cancel() { timer.Stop(); pending = null; }
}
