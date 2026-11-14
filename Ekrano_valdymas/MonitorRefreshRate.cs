using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Ekrano_valdymas;

internal sealed class MonitorRefreshRate : IDisposable
{
    internal sealed record Display(string Device,string Name,uint Numerator,uint Denominator,string Source)
    { public double Hertz => Denominator==0 ? 60 : Numerator/(double)Denominator; }
    private readonly Window owner;
    private readonly DispatcherTimer debounce;
    private HwndSource? source;
    private IntPtr monitor, pendingMonitor;
    internal Display Current {get;private set;} = new("unknown","unknown",60,1,"fallback");
    internal int QueryCount {get;private set;}
    internal event Action? Changed;
    internal MonitorRefreshRate(Window window)
    {
        owner=window;
        debounce=new DispatcherTimer(DispatcherPriority.Background,owner.Dispatcher){Interval=TimeSpan.FromMilliseconds(120)};
        debounce.Tick+=OnDebounce;
        owner.LocationChanged+=OnLocation;owner.SizeChanged+=OnSize;
        owner.SourceInitialized+=OnSource;owner.StateChanged+=OnState;
        Attach(); Refresh();
    }
    private void OnSource(object? sender,EventArgs e){Attach();Refresh();}
    private void Attach()
    {
        var handle=new WindowInteropHelper(owner).Handle;
        if(handle==IntPtr.Zero)return; // SourceInitialized attaches after the native window exists.
        var next=HwndSource.FromHwnd(handle);
        if(next==source)return;
        source?.RemoveHook(Hook);source=next;source?.AddHook(Hook);
    }
    private void OnLocation(object? sender,EventArgs e)=>CheckMonitor();
    private void OnSize(object sender,SizeChangedEventArgs e)=>CheckMonitor();
    private void OnState(object? sender,EventArgs e){if(owner.WindowState!=WindowState.Minimized)Invalidate();}
    private void CheckMonitor()
    {
        IntPtr next=MonitorFromWindow(new WindowInteropHelper(owner).Handle,2);
        if(next!=pendingMonitor){pendingMonitor=next;Invalidate();}
    }
    internal void Invalidate(){debounce.Stop();debounce.Start();}
    private void OnDebounce(object? sender,EventArgs e){debounce.Stop();Refresh();}
    private IntPtr Hook(IntPtr hwnd,int msg,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(msg==0x47)CheckMonitor(); // WM_WINDOWPOSCHANGED, including native moves
        if(msg is 0x7e or 0x219 || msg==0x218 && wParam.ToInt64() is 7 or 18)Invalidate();
        return IntPtr.Zero; // display change, device change, resume; never consume owner's message
    }
    private void Refresh()
    {
        QueryCount++;
        monitor=MonitorFromWindow(new WindowInteropHelper(owner).Handle,2);
        pendingMonitor=monitor;
        Current=Read(monitor);
        Changed?.Invoke();
    }
    internal static Display Read(IntPtr monitor)
    {
        using var callbackTiming = new UiCallbackTiming("MonitorRefreshRate.Read");
        var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>(),Device=""};
        if(!GetMonitorInfo(monitor,ref info))return new("unknown","unknown",60,1,"fallback: GetMonitorInfo failed");
        // Windows 11 understands virtual refresh rates; Windows 10 falls back.
        foreach(uint flags in new uint[]{0x52,0x12,2})
        for(int attempt=0;attempt<3;attempt++)
        {
            int error=GetDisplayConfigBufferSizes(flags,out uint pc,out uint mc);
            if(error!=0)break;
            var paths=new DisplayPath[pc];var modes=new Mode[mc];
            error=QueryDisplayConfig(flags,ref pc,paths,ref mc,modes,IntPtr.Zero);
            if(error==122)continue;
            if(error!=0)break;
            var candidates=new List<Display>();
            for(int i=0;i<pc;i++)
            {
                var path=paths[i];
                var sourceName=new SourceName{Header=new Header{Type=1,Size=(uint)Marshal.SizeOf<SourceName>(),Adapter=path.Source.Adapter,Id=path.Source.Id},Name=""};
                if(GetSourceName(ref sourceName)!=0||sourceName.Name!=info.Device)continue;
                var targetName=new TargetName{Header=new Header{Type=2,Size=(uint)Marshal.SizeOf<TargetName>(),Adapter=path.Target.Adapter,Id=path.Target.Id},Friendly="",Path=""};
                GetTargetName(ref targetName);
                Rational rate=path.Target.Rate;string origin="active path refresh";
                for(int j=0;j<mc;j++)
                    if(modes[j].Type==2&&modes[j].Id==path.Target.Id&&modes[j].Adapter==path.Target.Adapter)
                    {rate=modes[j].Signal.Vertical;origin="active physical target vSyncFreq";break;}
                double hz=rate.Denominator==0?0:rate.Numerator/(double)rate.Denominator;
                if(hz is >=10 and <=1000)candidates.Add(new(info.Device,targetName.Friendly,rate.Numerator,rate.Denominator,origin));
            }
            // Cloned outputs share a source: use the slowest physical target.
            if(candidates.Count>0)return candidates.OrderBy(x=>x.Hertz).First();
            break;
        }
        return new(info.Device,info.Device,60,1,"fallback: active refresh unavailable");
    }
    internal sealed record ActiveDisplay(string Device,string Name,string Path,uint Width,uint Height,uint Numerator,uint Denominator,uint Rotation);
    // Read every physical target independently, including cloned outputs. This
    // one-shot inventory does not alter the visualizer's slowest-target policy.
    internal static ActiveDisplay[] ReadActiveDisplays()
    {
        foreach(uint flags in new uint[]{0x52,0x12,2})
        for(int attempt=0;attempt<3;attempt++)
        {
            if(GetDisplayConfigBufferSizes(flags,out uint pc,out uint mc)!=0)break;
            var paths=new DisplayPath[pc];var modes=new Mode[mc];
            int error=QueryDisplayConfig(flags,ref pc,paths,ref mc,modes,IntPtr.Zero);
            if(error==122)continue;if(error!=0)break;
            var result=new List<ActiveDisplay>();
            for(int i=0;i<pc;i++)
            {
                var p=paths[i];
                var sourceName=new SourceName{Header=new Header{Type=1,Size=(uint)Marshal.SizeOf<SourceName>(),Adapter=p.Source.Adapter,Id=p.Source.Id},Name=""};
                var targetName=new TargetName{Header=new Header{Type=2,Size=(uint)Marshal.SizeOf<TargetName>(),Adapter=p.Target.Adapter,Id=p.Target.Id},Friendly="",Path=""};
                GetSourceName(ref sourceName);GetTargetName(ref targetName);
                var rate=p.Target.Rate;uint width=0,height=0;
                for(int j=0;j<mc;j++)if(modes[j].Type==2&&modes[j].Id==p.Target.Id&&modes[j].Adapter==p.Target.Adapter)
                {var signal=modes[j].Signal;rate=signal.Vertical;width=signal.ActiveX;height=signal.ActiveY;break;}
                result.Add(new(sourceName.Name,string.IsNullOrWhiteSpace(targetName.Friendly)?sourceName.Name:targetName.Friendly,targetName.Path,width,height,rate.Numerator,rate.Denominator,p.Target.Rotation));
            }
            return result.ToArray();
        }
        return [];
    }
    public void Dispose()
    {
        debounce.Stop();debounce.Tick-=OnDebounce;source?.RemoveHook(Hook);
        owner.LocationChanged-=OnLocation;owner.SizeChanged-=OnSize;
        owner.SourceInitialized-=OnSource;owner.StateChanged-=OnState;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct MonitorInfo {public int Size;public Rect Bounds,Work;public uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Device;}
    [StructLayout(LayoutKind.Sequential)] private struct Rational {public uint Numerator,Denominator;}
    [StructLayout(LayoutKind.Sequential,Pack=4)] private struct Source {public long Adapter;public uint Id,ModeIndex,Status;}
    // Pack=4 matches LUID's native 4-byte alignment, not a native 64-bit integer.
    [StructLayout(LayoutKind.Sequential,Pack=4)] private struct Target {public long Adapter;public uint Id,ModeIndex,Technology,Rotation,Scaling;public Rational Rate;public uint Scan;public int Available;public uint Status;}
    [StructLayout(LayoutKind.Sequential,Pack=4)] private struct DisplayPath {public Source Source;public Target Target;public uint Flags;}
    [StructLayout(LayoutKind.Sequential)] private struct Signal {public ulong PixelRate;public Rational Horizontal,Vertical;public uint ActiveX,ActiveY,TotalX,TotalY,Standard,Scan;}
    [StructLayout(LayoutKind.Explicit,Size=64)] private struct Mode {[FieldOffset(0)]public uint Type;[FieldOffset(4)]public uint Id;[FieldOffset(8)]public long Adapter;[FieldOffset(16)]public Signal Signal;}
    [StructLayout(LayoutKind.Sequential,Pack=4)] private struct Header {public uint Type,Size;public long Adapter;public uint Id;}
    [StructLayout(LayoutKind.Sequential,Pack=4,CharSet=CharSet.Unicode)] private struct SourceName {public Header Header;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Name;}
    [StructLayout(LayoutKind.Sequential,Pack=4,CharSet=CharSet.Unicode)] private struct TargetName {public Header Header;public uint Flags,Technology;public ushort Manufacturer,Product;public uint Connector;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)]public string Friendly;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Path;}
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="GetMonitorInfoW")] private static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags,out uint paths,out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags,ref uint paths,[Out]DisplayPath[] path,ref uint modes,[Out]Mode[] mode,IntPtr topology);
    [DllImport("user32.dll",EntryPoint="DisplayConfigGetDeviceInfo")] private static extern int GetSourceName(ref SourceName name);
    [DllImport("user32.dll",EntryPoint="DisplayConfigGetDeviceInfo")] private static extern int GetTargetName(ref TargetName name);
}
