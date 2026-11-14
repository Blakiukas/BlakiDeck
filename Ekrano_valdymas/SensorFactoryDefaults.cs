using System.IO;
using static Ekrano_valdymas.SystemMonitorWidget;

namespace Ekrano_valdymas;

// User-approved 27 October default 3x3 order and reference typography.
// InstallationDefaults alone decides whether a profile is a genuine new install.
internal static class SensorFactoryDefaults
{
    internal sealed record Cell(int Index,MetricKind SourceKind,MetricPresentation Presentation)
    {
        internal MetricKind FactoryKind=>NormalizeMetric(SourceKind);
    }
    internal static MetricKind[] DefaultKinds => [MetricKind.CpuLoad,MetricKind.CpuPower,MetricKind.CpuTemperature,MetricKind.GpuLoad,MetricKind.GpuPower,MetricKind.GpuTemperature,MetricKind.MemoryLoad,MetricKind.VramUsed,MetricKind.CpuCoreLoad];
    internal static MetricPresentation ForMetric(MetricKind kind) => IsStatus(kind)?new(TitleSize:16,ValueSize:20,AutoFit:true,Color:"#53B5FF"):NormalizeMetric(kind)==MetricKind.CodexFiveHour
        ? new(TitleSize:20,ValueSize:24,AutoFit:false,HideTitle:false,Color:"#FF8000",GraphTitleSize:20,GraphValueSize:20,GraphIntervalSize:10,ResetTextSize:14)
        : new(
        TitleSize:kind==MetricKind.CpuCoreLoad?20:24,ValueSize:kind==MetricKind.CpuCoreLoad?29:44,
        Color:kind switch {
            MetricKind.CpuCoreLoad=>"#F65A00",MetricKind.CpuLoad=>"#53B5FF",MetricKind.CpuPower=>"#1ED760",
            MetricKind.CpuTemperature or MetricKind.GpuTemperature=>"#F2B255",MetricKind.GpuLoad=>"#B18CFF",
            MetricKind.GpuPower=>"#35D0C8",MetricKind.MemoryLoad=>"#FF6B6B",MetricKind.VramUsed=>"#5397FF",_=>null},
        GraphTitleSize:20,GraphValueSize:20);
    internal static MetricKind[] WideKinds => [MetricKind.CpuLoad,MetricKind.GpuLoad,MetricKind.CpuTemperature,MetricKind.CpuPower,MetricKind.CpuCoreLoad,MetricKind.GpuTemperature,MetricKind.GpuPower,MetricKind.VramUsed,MetricKind.MemoryLoad,MetricKind.MemoryUsed,MetricKind.GpuHotspot];
    internal static MetricPresentation ForView(string? view,MetricKind kind)
    {
        if(CurrentMonitorDefaults.For(view).Cells.FirstOrDefault(c=>c.Target=="sensor:"+NormalizeMetric(kind)) is {} captured)return captured.Presentation;
        var value=ForMetric(NormalizeMetric(kind));if(view!="wide")return value;
        return value with{Color=kind switch{MetricKind.CpuLoad or MetricKind.MemoryUsed=>"#0066FF",MetricKind.GpuLoad=>"#00FF00",MetricKind.CpuTemperature or MetricKind.GpuTemperature=>"#FFFF00",MetricKind.GpuHotspot=>"#FF1493",_=>value.Color}};
    }
    internal static IReadOnlyList<Cell> Captured {get;}=Array.AsReadOnly(CurrentMonitorDefaults.For(null).Cells.Select(c=>new Cell(c.Index,Enum.Parse<MetricKind>(c.Target[7..]),c.Presentation)).ToArray());
    private static string Profile=>Path.Combine(DiagnosticState.LocalApplicationData,"Ekrano_valdymas");
    internal static void SeedNewInstall()
    {
        string[] names=["system-monitor.json","system-monitor.json.mixed.json","system-monitor.active.json","system-monitor-presentation.json","sensor-cells.json"];
        // Defensive no-overwrite guard; this method is called only by the
        // central new-profile bootstrap before any widget is constructed.
        if(names.Any(name=>File.Exists(Path.Combine(Profile,name))))return;
        AtomicJson.Write(Path.Combine(Profile,"system-monitor.json.grid.json"),CurrentMonitorDefaults.For(null).Grid);
        var cells=Captured.ToDictionary(cell=>"monitor:"+cell.Index,cell=>cell.Presentation);
        var metrics=Captured.GroupBy(cell=>cell.SourceKind).ToDictionary(group=>group.Key,group=>group.First().Presentation);
        AtomicJson.Write(Path.Combine(Profile,"sensor-cells.json"),cells);
        AtomicJson.Write(Path.Combine(Profile,"system-monitor-presentation.json"),metrics);
        AtomicJson.Write(Path.Combine(Profile,"system-monitor.json"),Captured.Select(cell=>cell.FactoryKind.ToString()).ToArray());
        AtomicJson.Write(Path.Combine(Profile,"system-monitor.json.mixed.json"),Captured.Select(cell=>new{Target="sensor:"+cell.FactoryKind,Name=(string?)null,Appearance=(string?)null,Icon=(string?)null}).ToArray());
        AtomicJson.Write(Path.Combine(Profile,"system-monitor.active.json"),Captured.Select(_=>true).ToArray());
    }
    internal static MetricPresentation Reference(string location,MetricKind kind,MetricPresentation fallback)
    {
        if(location.StartsWith("monitor")&&int.TryParse(location.Split(':').Last(),out var index)&&CurrentMonitorDefaults.For(location.StartsWith("monitor-full:")?"full":location.StartsWith("monitor-wide:")?"wide":null).Cells.FirstOrDefault(c=>c.Index==index&&c.Target=="sensor:"+NormalizeMetric(kind)) is {} captured)return captured.Presentation;
        return ForView(location.StartsWith("monitor-full:")?"full":location.StartsWith("monitor-wide:")?"wide":null,NormalizeMetric(kind));
    }
    internal static object SnapshotForAudit()=>Captured.Select(cell=>new
    {
        slot=cell.Index,sourceKind=cell.SourceKind.ToString(),factoryKind=cell.FactoryKind.ToString(),
        cell.Presentation.Mode,cell.Presentation.Minutes,cell.Presentation.VisibleSeconds,
        cell.Presentation.TitleSize,cell.Presentation.ValueSize,cell.Presentation.AutoFit,cell.Presentation.HideTitle,
        cell.Presentation.GraphTitleSize,cell.Presentation.GraphValueSize,cell.Presentation.GraphIntervalSize,
        cell.Presentation.NetworkDown,cell.Presentation.NetworkUp,
        palette="Per-metric factory colors; existing saved overrides remain unchanged."
    }).ToArray();
}

