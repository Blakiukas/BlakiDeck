using System.Diagnostics;
namespace Ekrano_valdymas;

// One bounded, memory-only series per activated metric, independent of its tiles.
internal sealed class MonitorHistory
{
    internal const int MaximumSeconds=3600;
    // Record accepts at most one reading per0.9s. Keep a true hour at that
    // fastest accepted cadence, with a tiny endpoint allowance.
    private const int MaximumSamples=4002;
    internal readonly record struct Sample(double Time, double? Value,double? IntervalStart=null);
    private readonly Dictionary<SystemMonitorWidget.MetricKind, Queue<Sample>> series = new();
    internal static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    internal void Reset()=>series.Clear();
    internal void ClearGpu(){foreach(var (kind,queue) in series)if(SystemMonitorWidget.MetricGroup(kind)=="GPU")queue.Clear();}
    internal IEnumerable<SystemMonitorWidget.MetricKind> Activated => series.Keys;
    internal void Activate(SystemMonitorWidget.MetricKind kind) => series.TryAdd(kind, new());
    internal void Record(double time, IReadOnlyDictionary<SystemMonitorWidget.MetricKind,double?> values,double? networkIntervalStart=null)
    {
        foreach (var (kind, samples) in series)
        {
            while (samples.TryPeek(out var old) && old.Time < time - MaximumSeconds) samples.Dequeue();
            if (samples.Count > 0 && time - samples.Last().Time < .9) continue;
            var value = values.GetValueOrDefault(kind);
            bool network=kind is SystemMonitorWidget.MetricKind.Network or SystemMonitorWidget.MetricKind.NetworkDownload or SystemMonitorWidget.MetricKind.NetworkUpload;
            samples.Enqueue(new(time, value.HasValue && double.IsFinite(value.Value) ? value : null,network?networkIntervalStart:null));
            while (samples.Count > MaximumSamples) samples.Dequeue();
        }
    }
    internal Sample[] Read(SystemMonitorWidget.MetricKind kind) => series.TryGetValue(kind, out var q) ? q.ToArray() : [];
    internal Sample[] Read(SystemMonitorWidget.MetricKind kind,double sinceTime)
    {
        if(!series.TryGetValue(kind,out var samples))return [];
        // Record and tile snapshots run synchronously on the UI dispatcher.
        // Count first so a short viewport never allocates the full-hour array.
        int count=0;foreach(var sample in samples)if(sample.Time>=sinceTime)count++;
        if(count==0)return [];
        var result=new Sample[count];int index=0;
        foreach(var sample in samples)if(sample.Time>=sinceTime)result[index++]=sample;
        return result;
    }
}
