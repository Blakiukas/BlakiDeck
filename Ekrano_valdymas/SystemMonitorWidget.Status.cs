namespace Ekrano_valdymas;
public partial class SystemMonitorWidget
{
    internal (string Label,string Value)[] StatusRows(MetricKind kind,MetricPresentation appearance)
    {
        if(kind==MetricKind.GpuStatus)return GpuStatusRows();
        if(kind==MetricKind.CpuStatus){
            string V(MetricKind metric,string unit)=>Latest.GetValueOrDefault(metric) is {} v?$"{v:0.#}{unit}":"N/A";
            return [("LOAD",V(MetricKind.CpuLoad,"%")),("TEMPERATURE",V(MetricKind.CpuTemperature,"°C")),("CURRENT USAGE",V(MetricKind.CpuPower," W")),("VOLTAGE",V(MetricKind.CpuVoltage," V")),("FAN",V(MetricKind.CpuFan," RPM")),("PUMP",V(MetricKind.CpuPump," RPM"))];
        }
        var disk=SelectedDisk(appearance.DiskId);string? id=disk?.Id??appearance.DiskId;
        bool fresh=disk!=null&&DateTime.UtcNow-disk.SampledAt<TimeSpan.FromSeconds(90);
        return [("READ",disk==null?"N/A":DiskDisplay(MetricKind.DiskRead,id)),("WRITE",disk==null?"N/A":DiskDisplay(MetricKind.DiskWrite,id)),("TEMPERATURE",DiskDisplay(MetricKind.DiskTemperature,id)),("LIFE REMAINING",DiskDisplay(MetricKind.DiskHealth,id)),("WEAR",DiskDisplay(MetricKind.DiskWear,id)),("DRIVE STATUS",fresh?disk!.Health:"N/A")];
    }
}
