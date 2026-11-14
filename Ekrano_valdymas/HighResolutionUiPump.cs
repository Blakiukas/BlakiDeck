using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Ekrano_valdymas;

// A monitor-rate cap, not VSync. One pending UI operation, no missed-frame replay.
internal sealed class HighResolutionUiPump : IDisposable
{
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr CreateWaitableTimerEx(IntPtr attributes,string? name,uint flags,uint access);
    [DllImport("kernel32.dll")] private static extern bool SetWaitableTimer(IntPtr timer,ref long due,int period,IntPtr callback,IntPtr arg,bool resume);
    [DllImport("kernel32.dll")] private static extern uint WaitForMultipleObjects(uint count,IntPtr[] handles,bool all,uint milliseconds);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    private readonly object gate = new();
    private readonly AutoResetEvent wake = new(false);
    private int stopped, queued;
    private long periodTicks;
    private double lastDelivered;
    internal Task Completion { get; }
    internal double Hertz => Stopwatch.Frequency / (double)Volatile.Read(ref periodTicks);

    internal HighResolutionUiPump(Dispatcher dispatcher, Action<double> update, double hertz=60)
    {
        periodTicks=Period(hertz);
        lastDelivered=Stopwatch.GetTimestamp();
        Completion=Task.Run(()=>Run(dispatcher,update));
    }
    private static long Period(double hz) => (long)Math.Ceiling(Stopwatch.Frequency / (double.IsFinite(hz) && hz is >= 10 and <= 1000 ? hz : 60));
    internal void SetRate(double hertz)
    {
        lock(gate)
        {
            if(stopped!=0)return;
            long period=Period(hertz);
            if(Interlocked.Exchange(ref periodTicks,period)!=period)wake.Set();
        }
    }
    private void Run(Dispatcher dispatcher,Action<double> update)
    {
        IntPtr timer=CreateWaitableTimerEx(IntPtr.Zero,null,2,0x1F0003);
        try
        {
            if(timer==IntPtr.Zero)throw new InvalidOperationException("High-resolution timer unavailable");
            IntPtr[] handles=[timer,wake.SafeWaitHandle.DangerousGetHandle()];
            double deadline=Stopwatch.GetTimestamp()+Volatile.Read(ref periodTicks);
            while(Volatile.Read(ref stopped)==0)
            {
                // Also respect the last actual UI delivery, so a delayed callback
                // cannot be followed by a catch-up burst at the next old deadline.
                deadline=Math.Max(deadline,Volatile.Read(ref lastDelivered)+Volatile.Read(ref periodTicks));
                double remaining=deadline-Stopwatch.GetTimestamp();
                if(remaining>0)
                {
                    long due=-(long)Math.Max(1,Math.Ceiling(remaining*10000000/Stopwatch.Frequency));
                    if(!SetWaitableTimer(timer,ref due,0,IntPtr.Zero,IntPtr.Zero,false))throw new InvalidOperationException("Timer scheduling failed");
                    uint result=WaitForMultipleObjects(2,handles,false,uint.MaxValue);
                    if(result==1){deadline=Stopwatch.GetTimestamp()+Volatile.Read(ref periodTicks);continue;}
                    if(result!=0)throw new InvalidOperationException("Timer wait failed");
                    continue;
                }
                if(Interlocked.CompareExchange(ref queued,1,0)==0)
                    dispatcher.BeginInvoke(DispatcherPriority.Render,new Action(()=>{
                        try
                        {
                            if(Volatile.Read(ref stopped)!=0)return;
                            double now=Stopwatch.GetTimestamp();
                            double elapsed=(now-Volatile.Read(ref lastDelivered))/Stopwatch.Frequency;
                            Volatile.Write(ref lastDelivered,now);
                            update(Math.Clamp(elapsed,0,.1));
                        }
                        finally{Volatile.Write(ref queued,0);}
                    }));
                double current=Stopwatch.GetTimestamp(),period=Volatile.Read(ref periodTicks);
                deadline=Math.Max(deadline+period,current+period);
            }
        }
        catch(Exception ex){InteractionDiagnostics.RecordFailure("VU update pump",ex);}
        finally
        {
            if(timer!=IntPtr.Zero)CloseHandle(timer);
            lock(gate){stopped=1;wake.Dispose();}
        }
    }
    public void Dispose(){lock(gate){if(stopped!=0)return;Volatile.Write(ref stopped,1);wake.Set();}}
}
