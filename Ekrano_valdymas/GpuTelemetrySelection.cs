using LibreHardwareMonitor.Hardware;
using System.IO;
using System.Text.Json;

namespace Ekrano_valdymas;

internal sealed record GpuAdapterChoice(string Id,string Name,string Vendor);
internal static class GpuTelemetrySelection
{
    private static GpuAdapterChoice[] adapters=[];
    private static string FilePath=>Path.Combine(DiagnosticState.LocalApplicationData,"Ekrano_valdymas","gpu-device.json");
    private static DateTime readAt;
    private static string? selectedId;
    internal static event Action? Changed;
    internal static GpuAdapterChoice[] Adapters=>Volatile.Read(ref adapters);
    internal static void PublishAdapters(GpuAdapterChoice[] values)=>Volatile.Write(ref adapters,values);
    internal static bool IsGpu(HardwareType type)=>type is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;
    private static string? SelectedId
    {
        get
        {
            if(DateTime.UtcNow>=readAt){readAt=DateTime.UtcNow.AddSeconds(1);try{selectedId=File.Exists(FilePath)?JsonSerializer.Deserialize<string>(File.ReadAllText(FilePath)):null;}catch{}}
            return selectedId;
        }
    }
    internal static GpuAdapterChoice? Current
    {
        get {var list=Adapters;var id=SelectedId;return id==null?list.FirstOrDefault():list.FirstOrDefault(a=>a.Id==id);}
    }
    internal static IHardware? Pick(IEnumerable<IHardware> hardware)
    {
        var gpus=hardware.Where(h=>IsGpu(h.HardwareType)).OrderBy(h=>h.HardwareType==HardwareType.GpuNvidia?0:h.HardwareType==HardwareType.GpuAmd?1:2).ThenBy(h=>h.Identifier.ToString(),StringComparer.Ordinal).ToArray();
        Volatile.Write(ref adapters,gpus.Select(h=>new GpuAdapterChoice(h.Identifier.ToString(),h.Name,h.HardwareType.ToString())).ToArray());
        var current=Current;return current==null?null:gpus.FirstOrDefault(h=>h.Identifier.ToString()==current.Id);
    }
    internal static void Select(string id)
    {
        if(!Adapters.Any(a=>a.Id==id))return;
        AtomicJson.Write(FilePath,id);selectedId=id;readAt=DateTime.UtcNow.AddSeconds(1);Changed?.Invoke();
    }
    internal static double? Find(IEnumerable<(HardwareType Hardware,ISensor Sensor)> all,Func<HardwareType,bool> filter,SensorType type,params string[] names)
    {
        var matches=all.Where(x=>filter(x.Hardware)&&x.Sensor.SensorType==type).Select(x=>x.Sensor).ToArray();
        foreach(string name in names)
        {
            var exact=matches.Where(s=>s.Name.Equals(name,StringComparison.OrdinalIgnoreCase)).ToArray();
            if(exact.Length>1)return null;
            var sensor=exact.SingleOrDefault();
            if(sensor?.Value is float v&&float.IsFinite(v)&&(type==SensorType.Temperature?v>=-273.15:v>=0)&&(type!=SensorType.Load||v<=100))return v;
        }
        // A different sensor of the same type is not a substitute for this metric.
        return null;
    }
    internal static (double? Value,string Scope) Power(IEnumerable<(HardwareType Hardware,ISensor Sensor)> all)
    {
        var sensors=all.Where(s=>IsGpu(s.Hardware)&&s.Sensor.SensorType==SensorType.Power).ToArray();
        foreach(string name in new[]{"GPU Total Board Power","Total Board Power","GPU Package","GPU Power","GPU Core"})
        {
            var exact=sensors.Where(s=>s.Sensor.Name.Equals(name,StringComparison.OrdinalIgnoreCase)).ToArray();if(exact.Length>1)return(null,"GPU");
            var sensor=exact.SingleOrDefault();
            if(sensor.Sensor?.Value is float watts&&float.IsFinite(watts)&&watts>=0)
                return(watts,name.Contains("Board")||sensor.Hardware==HardwareType.GpuNvidia&&name=="GPU Package"?"BOARD":name=="GPU Core"?"CHIP":"PACKAGE");
        }
        return(null,"GPU");
    }
    internal static double? Pump(IEnumerable<(HardwareType Hardware,ISensor Sensor)> all,bool gpu)
    {
        // Generic controller "Pump" cannot tell us which cooling loop it serves.
        var named=all.Where(s=>s.Sensor.SensorType==SensorType.Fan&&
            (gpu?IsGpu(s.Hardware)&&s.Sensor.Name.Contains("pump",StringComparison.OrdinalIgnoreCase)
                :!IsGpu(s.Hardware)&&(s.Sensor.Name.Equals("CPU Pump",StringComparison.OrdinalIgnoreCase)||s.Sensor.Name.Equals("CPU_PUMP",StringComparison.OrdinalIgnoreCase)))).ToArray();
        return named.Length==1&&named[0].Sensor.Value is float v&&float.IsFinite(v)&&v>=0?v:null;
    }
}
