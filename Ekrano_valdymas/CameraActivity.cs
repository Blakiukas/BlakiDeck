using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;

namespace Ekrano_valdymas;

// Passive Media Foundation sensor reports: does not open or capture the camera.
internal sealed class CameraActivity:IDisposable
{
    [ComImport,Guid("97ec2ea4-0e42-4937-97ac-9d6d328824e1"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface Shutdown {void Close();void GetStatus(out uint status);}
    [ComImport,Guid("D0CEF145-B3F4-4340-A2E5-7A5080CA05CB"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface Monitor{void Start();void Stop();}
    [ComImport,Guid("683F7A5E-4A19-43CD-B1A9-DBF4AB3F7777"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] public interface Reports{void GetCount(out uint count);void GetActivityReport(uint index,out Report report);void GetActivityReportByDeviceName([MarshalAs(UnmanagedType.LPWStr)]string name,out Report report);}
    [ComImport,Guid("3E8C4BE1-A8C2-4528-90DE-2851BDE5FEAD"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] public interface Report{void GetFriendlyName([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder name,uint size,out uint written);void GetSymbolicLink([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder name,uint size,out uint written);void GetProcessCount(out uint count);void GetProcessActivity(uint index,out Activity activity);}
    [ComImport,Guid("39DC7F4A-B141-4719-813C-A7F46162A2B8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] public interface Activity{void GetProcessId(out uint pid);void GetStreamingState([MarshalAs(UnmanagedType.Bool)]out bool streaming);void GetStreamingMode(out uint mode);void GetReportTime(out long time);}
    [ComVisible(true),Guid("DE5072EE-DBE3-46DC-8A87-B6F631194751"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] public interface Callback{[PreserveSig]int OnActivitiesReport(Reports reports);}
    [ComVisible(true),ClassInterface(ClassInterfaceType.None)] public sealed class Sink:Callback
    {
        internal Action<Dictionary<string,bool>>? Update;
        internal int Calls;internal string? Error;
        public int OnActivitiesReport(Reports reports)
        {
            Calls++;
            try{var snapshot=new Dictionary<string,bool>(StringComparer.OrdinalIgnoreCase);reports.GetCount(out var count);for(uint i=0;i<count;i++){reports.GetActivityReport(i,out var report);try{var link=new StringBuilder(2048);report.GetSymbolicLink(link,(uint)link.Capacity,out _);report.GetProcessCount(out var processes);bool streaming=false;for(uint j=0;j<processes;j++){report.GetProcessActivity(j,out var activity);try{activity.GetStreamingState(out bool active);streaming|=active;}finally{Marshal.ReleaseComObject(activity);}}snapshot[link.ToString()]=streaming;}finally{Marshal.ReleaseComObject(report);}}Update?.Invoke(snapshot);}catch(Exception ex){Error=ex.ToString();}
            return 0;
        }
    }
    [DllImport("mfplat.dll")] private static extern int MFStartup(uint version,uint flags);
    [DllImport("mfplat.dll")] private static extern int MFShutdown();
    [DllImport("mfsensorgroup.dll")] private static extern int MFCreateSensorActivityMonitor(Callback callback,out Monitor monitor);
    internal string? Error {get;private set;}
    internal bool Running=>monitor!=null;
    internal object Diagnostic {get{lock(states)return new{sink.Calls,sink.Error,states=states.ToArray()};}}
    private readonly Sink sink=new();private Monitor? monitor;private bool started;
    private readonly Dictionary<string,bool> states=new(StringComparer.OrdinalIgnoreCase);
    private readonly long startedAt=Stopwatch.GetTimestamp();
    internal CameraActivity()
    {
        try{Marshal.ThrowExceptionForHR(MFStartup(0x20070,0));started=true;sink.Update=snapshot=>{lock(states){states.Clear();foreach(var pair in snapshot)states[pair.Key]=pair.Value;}};Marshal.ThrowExceptionForHR(MFCreateSensorActivityMonitor(sink,out var value));monitor=value;monitor.Start();}catch(Exception ex){Error=ex.ToString();Dispose();}
    }
    internal bool? Streaming(string id)
    {
        if(monitor==null||sink.Error!=null)return null;
        string match=id.Replace('\\','#');lock(states){foreach(var pair in states)if(pair.Key.Contains(match,StringComparison.OrdinalIgnoreCase))return pair.Value;}
        // Microsoft IMFSensorActivityMonitor sample treats no activity after a
        // 500 ms initial wait as not in use. Do not apply this on API failure.
        return Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds>=500?false:null;
    }
    public void Dispose(){if(monitor!=null){try{monitor.Stop();if(monitor is Shutdown shutdown)shutdown.Close();}catch{}Marshal.ReleaseComObject(monitor);monitor=null;}if(started){MFShutdown();started=false;}lock(states)states.Clear();}
}


