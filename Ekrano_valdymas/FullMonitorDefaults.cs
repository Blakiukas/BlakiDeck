using static Ekrano_valdymas.SystemMonitorWidget;
namespace Ekrano_valdymas;
internal static class FullMonitorDefaults
{
    internal sealed record Cell(int Index,string Target,MetricPresentation Presentation);
    internal static MonitorGridLayout Grid()=>CurrentMonitorDefaults.For("full").Grid;
    internal static Cell[] Cells=>CurrentMonitorDefaults.For("full").Cells;
}