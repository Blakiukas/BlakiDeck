using LibreHardwareMonitor.Hardware;
namespace Ekrano_valdymas;

internal sealed record HardwareReading(string Id,string Device,string Name,string Unit,double? Value,DateTime SampledAt,string HardwareKind="")
{
    // Only classify known memory devices; commit is not page-file occupancy.
    internal string DisplayDevice => Device.Equals("Virtual Memory",StringComparison.OrdinalIgnoreCase)
        ? "Committed memory" : Device.Equals("Total Memory",StringComparison.OrdinalIgnoreCase)
        ? "Physical RAM" : Device;
    public string Label=>Device+" → "+Name;
    internal bool Fresh=>DateTime.UtcNow-SampledAt is var age&&age>=TimeSpan.Zero&&age<TimeSpan.FromSeconds(Id==PageFileTelemetry.SensorId?30:5);
    internal bool ValidValue=>Value is {} v&&double.IsFinite(v)&&(Unit=="°C"?v>=-273.15:v>=0)&&(Unit!="%"||v<=100);
    internal string State=>!Fresh?"Stale — unavailable":!ValidValue?"No data — unavailable":"Current";
    internal string Description=>Label+"\nSensor ID: "+Id+"\nSource unit: "+Unit+"\nSample: "+SampledAt.ToLocalTime().ToString("HH:mm:ss")+" · "+State+(Id==PageFileTelemetry.SensorId?"\nActual page-file usage reported by Windows, summed across all page files. Not committed memory or file capacity. Updated every 10 seconds; rounded to whole MB by Windows.":"")+(Device.Contains("Virtual Memory",StringComparison.OrdinalIgnoreCase)?"\nVirtual memory includes committed memory backed by RAM and the page file; this is not physical RAM usage.":Device.Contains("Total Memory",StringComparison.OrdinalIgnoreCase)?"\nPhysical system RAM.":"");
    internal string DisplayIn(string unit,bool oneDecimal=false)=>Unit=="MB"&&unit=="GB"&&Fresh&&ValidValue&&Value is {} n&&double.IsFinite(n)?(n*1_000_000d/1073741824d).ToString(oneDecimal?"0.0":"0.##")+" GB":Display;
    internal string Display=>Fresh&&ValidValue&&Value is {} value&&double.IsFinite(value)?$"{(Unit=="MB"?value*1_000_000d/1048576d:value):0.##} {Unit}":"N/A";
}
internal static class HardwareTelemetry
{
    private static HardwareReading[] local=[],advanced=[];
    internal static void Publish(HardwareReading[] readings,bool helper=false){if(helper)Volatile.Write(ref advanced,readings);else Volatile.Write(ref local,readings);}
    internal static HardwareReading[] Readings=>Volatile.Read(ref advanced).Concat(Volatile.Read(ref local)).Append(PageFileTelemetry.Current).GroupBy(r=>r.Id).Select(g=>g.OrderByDescending(r=>r.SampledAt).First()).OrderBy(r=>r.Device).ThenBy(r=>r.Name).ToArray();
    internal static HardwareReading? Find(string? id)=>id==null?null:Readings.SingleOrDefault(r=>r.Id==id);
    internal static double? CpuEffectiveClock(IEnumerable<HardwareReading> readings)
    {
        // Use the provider's whole-CPU effective average, never boost/nominal
        // clocks or a partial average of whichever cores happen to be available.
        var matches=readings.Where(r=>r.HardwareKind=="Cpu"&&r.Unit=="MHz"&&r.Name=="Cores (Average Effective)").ToArray();
        return matches.Length==1&&matches[0].Fresh&&matches[0].ValidValue?matches[0].Value:null;
    }
    internal static string Describe(SystemMonitorWidget.MetricKind kind)
    {
        string[] names=kind.ToString() switch{
            "CpuTemperature"=>["CPU Package","Core (Tctl/Tdie)","Tctl","Package"],"CpuPower"=>["CPU Package","Package","Total"],"CpuVoltage"=>["CPU Core","Vcore","Core"],"CpuFan"=>["CPU Fan","CPU_FAN"],"CpuPump"=>["CPU Pump","CPU_PUMP"],
            "GpuTemperature"=>["GPU Core","Core"],"GpuHotspot"=>["GPU Hot Spot","Hot Spot","Hotspot"],"GpuFan"=>["GPU Fan","Fan"],"GpuPump"=>["Pump","GPU Pump"],"GpuVoltage"=>["GPU Core Voltage","GPU Core","Core"],_=>[]};
                string unit=kind.ToString().Contains("Temperature")||kind.ToString().Contains("Hotspot")?"°C":kind.ToString().Contains("Voltage")?"V":kind.ToString().Contains("Power")?"W":"RPM";
        var candidates=Readings.Where(r=>r.Unit==unit&&names.Contains(r.Name,StringComparer.OrdinalIgnoreCase)).ToArray();
        if(kind.ToString().StartsWith("Cpu")&&unit!="RPM")candidates=candidates.Where(r=>r.HardwareKind=="Cpu").ToArray();
        if(kind.ToString().StartsWith("Gpu"))candidates=candidates.Where(r=>GpuTelemetrySelection.Current is {} gpu&&r.Id.StartsWith(gpu.Id+"/",StringComparison.Ordinal)).ToArray();
        if(candidates.Length==0)return "No confirmed sensor for this reading. N/A is not zero. Choose HARDWARE SENSOR for an explicit device/source.";
        return string.Join("\n",candidates.Select(r=>$"{r.Label} · {r.Unit}\n{r.Id} · {r.State}"))+"\nOnly a unique exact sensor match is accepted; no similar-name substitute.";
    }
    internal static HardwareReading[] Capture(IEnumerable<(HardwareType Hardware,ISensor Sensor)> sensors)
    {
        var snapshot=sensors.ToArray();
        var now=DateTime.UtcNow;
        return snapshot.Select(s=>{
            var sensor=s.Sensor;double scale=sensor.SensorType==SensorType.SmallData?1048576:sensor.SensorType==SensorType.Data?1073741824:1;
            string unit=sensor.SensorType switch{SensorType.Temperature=>"°C",SensorType.Fan=>"RPM",SensorType.Clock=>"MHz",SensorType.Voltage=>"V",SensorType.Power=>"W",SensorType.Load=>"%",SensorType.SmallData or SensorType.Data=>"MB",_=>""};
            double? value=sensor.Value is float v&&float.IsFinite(v)?v:null;
            if(value is {} n && ((unit=="°C"?n< -273.15:n<0)||unit=="%"&&n>100))value=null;
            if(unit=="MB")value=value*scale/1e6;
            return new HardwareReading(sensor.Identifier.ToString(),sensor.Hardware.Name,sensor.Name,unit,value,now,s.Hardware.ToString());
        }).Where(r=>r.Unit.Length>0).Concat(CpuLimits(snapshot,now)).ToArray();
    }

    // pre709 reads this SMU table but does not expose its limit entries as sensors.
    // Read the provider's table only for the documented layout; never write SMU settings.
    // Upstream mapping: LibreHardwareMonitor/Hardware/RyzenSMU.cs, 0x00621202.
    private static IEnumerable<HardwareReading> CpuLimits((HardwareType Hardware,ISensor Sensor)[] sensors,DateTime sampledAt)
    {
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
        foreach(var cpu in sensors.Where(s=>s.Hardware==HardwareType.Cpu).Select(s=>s.Sensor.Hardware).Distinct())
        {
            float[]? table=null;
            try
            {
                if(cpu.GetType().FullName!="LibreHardwareMonitor.Hardware.Cpu.Amd17Cpu")continue;
                var smu=cpu.GetType().GetField("_smu",flags)?.GetValue(cpu);
                var versionValue=smu?.GetType().GetProperty("PmTableVersion",flags)?.GetValue(smu)??smu?.GetType().GetField("_pmTableVersion",flags)?.GetValue(smu);
                if(versionValue is not uint version||version!=0x00621202)continue;
                table=smu!.GetType().GetMethod("GetPmTable",flags)?.Invoke(smu,null) as float[];
            }
            catch(System.Reflection.TargetException){continue;}
            catch(System.Reflection.TargetInvocationException){continue;}
            if(table==null||table.Length<=10)continue;
            foreach(var item in new[]{(0,"CPU PPT Limit","W"),(10,"CPU Temperature Limit","°C")})
            {
                if(sensors.Any(s=>s.Sensor.Hardware==cpu&&s.Sensor.Name==item.Item2))continue;
                double value=table[item.Item1];if(!double.IsFinite(value)||value<=0)continue;
                yield return new HardwareReading(cpu.Identifier+"/technical-limit/"+item.Item1,cpu.Name,item.Item2,item.Item3,value,sampledAt,"Cpu");
            }
        }
    }
}
