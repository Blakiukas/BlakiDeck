using System.Runtime.InteropServices;
using System.Text;

namespace Ekrano_valdymas;
public partial class SystemMonitorWidget
{
    private NvidiaSnapshot gpuSnapshot=new(null,null,null,null,null);
    internal NvidiaSnapshot GpuInfo=>readingSource?.GpuInfo??gpuSnapshot;
    internal static bool IsStatus(MetricKind kind)=>kind is MetricKind.GpuStatus or MetricKind.CpuStatus or MetricKind.DiskStatus;
    internal static bool IsTextMetric(MetricKind kind)=>kind is MetricKind.DiskHealth or MetricKind.GpuPowerLimitActive or MetricKind.GpuThermalLimitActive or MetricKind.GpuLimitReason or MetricKind.GpuStatus or MetricKind.CpuStatus or MetricKind.DiskStatus;
    internal (string Label,string Value)[] GpuStatusRows()
    {
        var source=readingSource??this;var gpu=source.GpuInfo;
        string W(double? value)=>value is {} n?$"{n:0} W":"Unavailable";
        return [("CURRENT USAGE",W(source.Latest.GetValueOrDefault(MetricKind.GpuPower))),
            ("ACTIVE LIMIT",W(gpu.EnforcedLimit)),("MAX LIMIT",W(gpu.MaxLimit)),
            ("CLOCK",source.Latest.GetValueOrDefault(MetricKind.GpuClock) is {} clock?$"{clock:0} MHz":"Unavailable"),
            ("PERFORMANCE STATUS",gpu.Reasons.HasValue?LimitReason(gpu):"Unavailable"),("WIN POWER PLAN",PowerPlanName())];
    }
    internal static bool SupportsGraph(MetricKind kind)=>NormalizeMetric(kind)!=MetricKind.CodexFiveHour&&!IsTextMetric(kind)&&kind is not MetricKind.HardwareSensor and not MetricKind.DiskTemperature and not MetricKind.DiskWear;
    internal static string LimitReason(NvidiaSnapshot gpu)
    {
        if(gpu.Reasons is not {} bits)return "N/A";
        var reasons=new List<string>();
        foreach(var (mask,label) in new (ulong,string)[]{(4,"Power limit reached"),(0x20,"Temperature limit"),(0x40,"Thermal protection"),(0x80,"External power brake"),(8,"Hardware slowdown"),(2,"Clock setting"),(0x10,"Sync boost"),(0x100,"Display clocks"),(0x200,"Board policy"),(0x400,"GPU self-protection")})
            if((bits&mask)!=0 && !(mask==8&&(bits&0xc0)!=0))reasons.Add(label);
        if((bits&~0x7ffUL)!=0)reasons.Add("Other limit");
        return reasons.Count>0?string.Join(" · ",reasons):(bits&1)!=0?"No workload":"No active limit";
    }
    internal string GpuDisplay(MetricKind kind)=>kind switch
    {
        MetricKind.GpuLimitReason=>LimitReason(GpuInfo).Replace(" · ","\n"),
        MetricKind.GpuPowerLimitActive=>GpuInfo.Flag(4) is {} p?(p>0?"Active":"Not active"):"N/A",
        MetricKind.GpuThermalLimitActive=>GpuInfo.Flag(0x60) is {} t?(t>0?"Active":"Not active"):"N/A",
        _=>GpuInfo.EnforcedLimit is {} w?$"{w:0} W":"N/A"
    };
    internal string GpuDetails
    {
        get
        {
            var g=GpuInfo;string W(double? v)=>v is {} n?$"{n:0} W":"N/A";
            return $"Applied limit: {W(g.EnforcedLimit)}\nRequested: {W(g.RequestedLimit)} · Default: {W(g.DefaultLimit)} · Maximum allowed: {W(g.MaxLimit)}\nReason: {LimitReason(g)}\nClock: {g.Clock?.ToString("0")??"N/A"} MHz · State: {g.State??"N/A"}\nWin Power Plan: {PowerPlanName()}\nCurrent usage scope: {gpuPowerScope}. GPU self-protection limits clock speed under the GPU reliability policy; it does not by itself indicate a fault. A power limit does not identify Windows as its cause.";
        }
    }
    private static string planName="N/A";
    private static DateTime nextPlanRead;
    internal static string PowerPlanName()
    {
        if(DateTime.UtcNow<nextPlanRead)return planName;
        nextPlanRead=DateTime.UtcNow.AddSeconds(10);
        try
        {
            if(PowerGetActiveScheme(IntPtr.Zero,out var pointer)!=0)return planName="N/A";
            try
            {
                var id=Marshal.PtrToStructure<Guid>(pointer);uint size=0;
                PowerReadFriendlyName(IntPtr.Zero,ref id,IntPtr.Zero,IntPtr.Zero,null,ref size);
                if(size is >0 and <4096){var bytes=new byte[size];if(PowerReadFriendlyName(IntPtr.Zero,ref id,IntPtr.Zero,IntPtr.Zero,bytes,ref size)==0)planName=Encoding.Unicode.GetString(bytes).TrimEnd('\0');}
            }
            finally { LocalFree(pointer); }
        }
        catch { planName="N/A"; }
        return planName;
    }
    [DllImport("powrprof.dll")]private static extern uint PowerGetActiveScheme(IntPtr root,out IntPtr scheme);
    [DllImport("powrprof.dll")]private static extern uint PowerReadFriendlyName(IntPtr root,ref Guid scheme,IntPtr subgroup,IntPtr setting,byte[]? buffer,ref uint size);
    [DllImport("kernel32.dll")]private static extern IntPtr LocalFree(IntPtr memory);
}
