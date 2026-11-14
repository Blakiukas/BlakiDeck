using LibreHardwareMonitor.Hardware;
using static Ekrano_valdymas.SystemMonitorWidget;
namespace Ekrano_valdymas;

// Both the helper and local fallback use exactly the same metric projection.
internal static class HardwareMetricValues
{
    private static readonly MetricKind[] required=Capture(Array.Empty<(HardwareType,ISensor)>()).Keys.ToArray();
    internal static bool IsComplete(IReadOnlyDictionary<MetricKind,double?>? values)=>values!=null&&required.All(values.ContainsKey);
    internal static IReadOnlyDictionary<MetricKind,double?> Capture(IEnumerable<(HardwareType Hardware,ISensor Sensor)> sensors)
    {
        return new Dictionary<MetricKind, double?>
        {
            [MetricKind.CpuLoad] = Find(sensors, IsCpu, SensorType.Load, "CPU Total", "Total"),
            [MetricKind.CpuTemperature] = Find(sensors, IsCpu, SensorType.Temperature, "CPU Package", "Core (Tctl/Tdie)", "Tctl", "Package"),
            [MetricKind.CpuPower] = Find(sensors, IsCpu, SensorType.Power, "CPU Package", "Package", "Total"),
            [MetricKind.CpuVoltage] = Find(sensors, IsCpu, SensorType.Voltage, "CPU Core", "Vcore", "Core"),
            [MetricKind.CpuPump] = GpuTelemetrySelection.Pump(sensors,false),
            [MetricKind.GpuPump] = GpuTelemetrySelection.Pump(sensors,true),
            [MetricKind.CpuFan] = Find(sensors, _ => true, SensorType.Fan, "CPU Fan", "CPU_FAN"),
            [MetricKind.GpuLoad] = Find(sensors, IsGpu, SensorType.Load, "GPU Core", "Core"),
            [MetricKind.GpuTemperature] = Find(sensors, IsGpu, SensorType.Temperature, "GPU Core", "Core"),
            [MetricKind.GpuHotspot] = Find(sensors, IsGpu, SensorType.Temperature, "GPU Hot Spot", "Hot Spot", "Hotspot"),
            [MetricKind.GpuPower] = GpuTelemetrySelection.Power(sensors).Value,
            [MetricKind.GpuClock] = Find(sensors, IsGpu, SensorType.Clock, "GPU Core", "GPU Clock", "Core"),
            [MetricKind.GpuVoltage] = Find(sensors, IsGpu, SensorType.Voltage, "GPU Core Voltage", "GPU Core", "Core"),
            [MetricKind.GpuFan] = Find(sensors, IsGpu, SensorType.Fan, "GPU Fan", "Fan"),
            [MetricKind.VramUsed] = Find(sensors, IsGpu, SensorType.SmallData, "GPU Memory Used", "Memory Used")
        };
    }
    private static double? Find(IEnumerable<(HardwareType Hardware,ISensor Sensor)> all,Func<HardwareType,bool> filter,SensorType type,params string[] names)
        => GpuTelemetrySelection.Find(all,filter,type,names);
    private static bool IsCpu(HardwareType type)=>type==HardwareType.Cpu;
    private static bool IsGpu(HardwareType type)=>GpuTelemetrySelection.IsGpu(type);
}
