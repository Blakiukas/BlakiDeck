using System.Windows.Media;
using static Ekrano_valdymas.SystemMonitorWidget;
namespace Ekrano_valdymas;

public sealed record AdaptiveColorStop(double Threshold,string Color);
public sealed record AdaptiveColorRule(AdaptiveColorStop[]? Stops=null,bool Below=false,double Hysteresis=.5,string UnavailableColor="#FF999999",string? NormalColor=null,double? RangeMaximum=null);

// State belongs to a rendered cell, never to the persisted presentation or another cell.
internal sealed class AdaptiveSensorColors
{
    private readonly Dictionary<string,(AdaptiveColorRule Rule,int Stage)> states=new();
    internal string? Resolve(string key,double? value,string? baseline,AdaptiveColorRule? rule,bool enabled)
    {
        if(!enabled){states.Remove(key);return baseline;}
        rule??=new();
        if(value is not {} n||!double.IsFinite(n)){states.Remove(key);return rule.UnavailableColor;}
        var stops=(rule.Stops??[]).Where(s=>double.IsFinite(s.Threshold)).OrderBy(s=>rule.Below?-s.Threshold:s.Threshold).ToArray();
        int stage=0;double signed=rule.Below?-n:n;
        while(stage<stops.Length&&signed>=(rule.Below?-stops[stage].Threshold:stops[stage].Threshold))stage++;
        if(states.TryGetValue(key,out var prior)&&Equals(prior.Rule,rule)&&stage<prior.Stage&&prior.Stage<=stops.Length)
        {
            double boundary=rule.Below?-stops[prior.Stage-1].Threshold:stops[prior.Stage-1].Threshold;
            if(signed>=boundary-Math.Max(0,double.IsFinite(rule.Hysteresis)?rule.Hysteresis:0))stage=prior.Stage;
        }
        states[key]=(rule,stage);return stage==0?rule.NormalColor??baseline:stops[stage-1].Color;
    }
    internal static MetricKind[] Channels(MetricKind kind)=>kind switch{
        MetricKind.Network or MetricKind.NetworkDownload or MetricKind.NetworkUpload=>[MetricKind.NetworkDownload,MetricKind.NetworkUpload],
        MetricKind.CpuStatus=>[MetricKind.CpuLoad,MetricKind.CpuTemperature,MetricKind.CpuPower,MetricKind.CpuVoltage,MetricKind.CpuFan,MetricKind.CpuPump],
        MetricKind.GpuStatus=>[MetricKind.GpuPower,MetricKind.GpuClock],
        MetricKind.DiskStatus=>[MetricKind.DiskRead,MetricKind.DiskWrite,MetricKind.DiskTemperature,MetricKind.DiskHealth,MetricKind.DiskWear],
        _=>[kind]};
    internal static double? Reading(SystemMonitorWidget source,MetricKind kind,MetricPresentation p)
    {
        double? value;
        if(kind==MetricKind.HardwareSensor){var r=HardwareTelemetry.Find(p.HardwareSensorId);if(r==null||!r.Fresh||!r.ValidValue||p.HardwareSensorLabel!=null&&p.HardwareSensorLabel!=r.Label)return null;return r.Unit=="MB"?r.Value*1e6/(p.MemoryUnit=="GB"?1073741824d:1048576d):r.Value;}
        value=IsDisk(kind)?source.DiskValue(p.DiskId,kind):source.Latest.GetValueOrDefault(kind);
        if(kind is MetricKind.NetworkDownload or MetricKind.NetworkUpload or MetricKind.DiskRead or MetricKind.DiskWrite)return value/1048576d;
        if(kind is MetricKind.MemoryUsed or MetricKind.VramUsed)return kind==MetricKind.MemoryUsed&&p.Mode=="Percent"?source.TotalRam>0?value/source.TotalRam*100:null:p.MemoryUnit=="MB"?value*1024:value;
        return value;
    }
    internal MetricPresentation Apply(SystemMonitorWidget source,MetricKind kind,MetricPresentation p)
    {
        string? Color(MetricKind channel,string? baseline)=>Resolve(channel+":"+p.HardwareSensorId+":"+p.DiskId,Reading(source,channel,p),baseline,p.AdaptiveRules?.GetValueOrDefault(channel.ToString()),p.AdaptiveColors);
        if(!p.AdaptiveColors){states.Clear();return p;}
        if(NormalizeMetric(kind)==MetricKind.Network)return p with{NetworkDownColor=Color(MetricKind.NetworkDownload,NetworkDataColor(p,false)),NetworkUpColor=Color(MetricKind.NetworkUpload,NetworkDataColor(p,true))};
        if(kind==MetricKind.CpuStatus)return p with{CpuLoadColor=Color(MetricKind.CpuLoad,p.CpuLoadColor??p.Color),CpuTemperatureColor=Color(MetricKind.CpuTemperature,p.CpuTemperatureColor??p.Color),CpuPowerColor=Color(MetricKind.CpuPower,p.CpuPowerColor??p.Color)};
        if(kind==MetricKind.GpuStatus)return p with{GpuPowerColor=Color(MetricKind.GpuPower,p.GpuPowerColor??p.Color),GpuClockColor=Color(MetricKind.GpuClock,p.GpuClockColor??p.Color)};
        if(kind==MetricKind.DiskStatus)return p with{DiskReadColor=Color(MetricKind.DiskRead,p.DiskReadColor??p.Color),DiskWriteColor=Color(MetricKind.DiskWrite,p.DiskWriteColor??p.Color),DiskTemperatureColor=Color(MetricKind.DiskTemperature,p.DiskTemperatureColor??p.Color)};
        return p with{Color=Color(kind,p.Color)};
    }
    internal static string Unit(MetricKind kind,MetricPresentation p)=>kind==MetricKind.HardwareSensor?HardwareTelemetry.Find(p.HardwareSensorId)?.Unit is "MB"?p.MemoryUnit:HardwareTelemetry.Find(p.HardwareSensorId)?.Unit??"source unit":kind is MetricKind.MemoryUsed or MetricKind.VramUsed?p.Mode=="Percent"?"%":p.MemoryUnit:kind is MetricKind.NetworkDownload or MetricKind.NetworkUpload or MetricKind.DiskRead or MetricKind.DiskWrite?"MB/s":kind.ToString().Contains("Temperature")||kind==MetricKind.GpuHotspot?"°C":kind.ToString().Contains("Power")?"W":kind.ToString().Contains("Clock")?"MHz":kind.ToString().Contains("Voltage")?"V":kind.ToString().Contains("Fan")||kind.ToString().Contains("Pump")?"RPM":"%";
    internal static Brush PencilBrush {get;}=new SolidColorBrush(Color.FromRgb(0,102,255));
    internal static string? FixedColor(MetricKind kind,MetricPresentation p)=>kind switch{
        MetricKind.NetworkDownload=>NetworkDataColor(p,false),MetricKind.NetworkUpload=>NetworkDataColor(p,true),
        MetricKind.CpuLoad=>p.CpuLoadColor??p.Color,MetricKind.CpuTemperature=>p.CpuTemperatureColor??p.Color,MetricKind.CpuPower=>p.CpuPowerColor??p.Color,
        MetricKind.GpuPower=>p.GpuPowerColor??p.Color,MetricKind.GpuClock=>p.GpuClockColor??p.Color,
        MetricKind.DiskRead=>p.DiskReadColor??p.Color,MetricKind.DiskWrite=>p.DiskWriteColor??p.Color,MetricKind.DiskTemperature=>p.DiskTemperatureColor??p.Color,_=>p.Color};
    internal static (double? Minimum,double? Maximum,string Source) TechnicalRange(MetricKind kind,MetricPresentation p)
    {
        string unit=Unit(kind,p);
        if(unit=="%")return(0,100,"Percentage domain: 0–100%");
        var source=Shared;
        if(kind is MetricKind.CpuPower or MetricKind.CpuTemperature)
        {
            string name=kind==MetricKind.CpuPower?"CPU PPT Limit":"CPU Temperature Limit";
            var limits=HardwareTelemetry.Readings.Where(r=>r.HardwareKind=="Cpu"&&r.Name==name&&r.Unit==unit&&r.Fresh&&r.ValidValue&&r.Value>0).ToArray();
            return(0,limits.Length==1?limits[0].Value:null,limits.Length==1?limits[0].Label+" · reported CPU limit":"CPU limit unavailable from telemetry; enter your range maximum manually");
        }
        if(kind==MetricKind.GpuPower)return(0,source?.GpuInfo.MaxLimit is {} max&&double.IsFinite(max)&&max>0?max:null,"GPU firmware maximum power limit; unavailable if not reported");
        if(kind==MetricKind.MemoryUsed)return(0,source?.TotalRam is >0?source.TotalRam*(p.MemoryUnit=="MB"?1024:1):null,"Physical memory available to Windows");
        // Observed peaks and arbitrary device/model constants are not technical limits.
        return(unit=="°C"?null:0,null,"Technical maximum not reported by this sensor (—)");
    }
}
