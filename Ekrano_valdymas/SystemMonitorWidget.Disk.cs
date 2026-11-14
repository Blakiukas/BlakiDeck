namespace Ekrano_valdymas;

public partial class SystemMonitorWidget
{
    private DiskReadRateReader? diskReader, diskWriteReader;
    private IReadOnlyDictionary<string,double?> diskWriteSnapshot=new Dictionary<string,double?>();
    private readonly Dictionary<string,Queue<MonitorHistory.Sample>> diskWriteHistory=new(StringComparer.OrdinalIgnoreCase);
    private StorageInfo[] storageSnapshot=[];
    internal StorageInfo[] Storage=>readingSource?.Storage??storageSnapshot;
    private DateTime diskSampledAt;
    private int diskPending;
    private IReadOnlyDictionary<string, double?> diskSnapshot = new Dictionary<string, double?>();
    private readonly Dictionary<string, Queue<MonitorHistory.Sample>> diskHistory = new(StringComparer.OrdinalIgnoreCase);
    internal IReadOnlyDictionary<string, double?> DiskReadings => readingSource?.DiskReadings ?? diskSnapshot;
    internal static string DiskLabel(string key) => key == "_Total" ? "All disks" : Shared?.SelectedDisk(key)?.Description??"Disk " + key;
    internal static bool IsDisk(MetricKind kind)=>kind is MetricKind.DiskRead or MetricKind.DiskWrite or MetricKind.DiskTemperature or MetricKind.DiskWear or MetricKind.DiskHealth or MetricKind.DiskStatus;
    internal static bool IsDiskRate(MetricKind kind)=>kind is MetricKind.DiskRead or MetricKind.DiskWrite;
    internal IEnumerable<string> DiskChoices(MetricKind kind,string? selected)=> (IsDiskRate(kind)?new[]{"_Total"}:Array.Empty<string>()).Concat(Storage.Select(d=>d.SelectionId)).Concat(DiskReadings.Keys.Where(k=>k!="_Total"&&!Storage.Any(d=>d.Id==k))).Concat(selected is {} id?new[]{id}:Array.Empty<string>()).Distinct().OrderBy(k=>k=="_Total"?"":k);
    internal static string DiskReadLabel(string? key) => key is null or "_Total" ? "DISK READ" : "DISK " + key + " READ";
    internal double? DiskValue(string? key,MetricKind kind=MetricKind.DiskRead)
    {
        if(readingSource!=null)return readingSource.DiskValue(key,kind);
        if(IsDiskRate(kind)){if(DiagnosticState.Root==null&&DateTime.UtcNow-diskSampledAt>TimeSpan.FromSeconds(5))return null;var physical=key?.StartsWith("device:")==true?SelectedDisk(key)?.Id:key;if(key?.StartsWith("device:")==true&&physical==null)return null;return (kind==MetricKind.DiskWrite?diskWriteSnapshot:diskSnapshot).GetValueOrDefault(physical??"_Total");}
        var disk=SelectedDisk(key);if(disk==null||DateTime.UtcNow-disk.SampledAt>TimeSpan.FromSeconds(90))return null;
        return kind==MetricKind.DiskTemperature?disk.Temperature:kind==MetricKind.DiskWear?disk.Wear:kind==MetricKind.DiskHealth&&disk.Wear is {} wear?Math.Clamp(100-wear,0,100):null;
    }
    internal StorageInfo? SelectedDisk(string? key)
    {
        var matches=Storage.Where(d=>d.SelectionId==key||d.Id==key).ToArray();
        return key==null?Storage.FirstOrDefault():matches.Length==1?matches[0]:null;
    }
    internal string DiskDisplay(MetricKind kind,string? key)
    {
        if(kind==MetricKind.DiskHealth)return DiskValue(key,kind) is {} remaining?$"{remaining:0}%":"N/A";
        var v=DiskValue(key,kind);return v==null?"N/A":IsDiskRate(kind)?$"{v/1048576d:0.00} MB/s":kind==MetricKind.DiskTemperature?$"{v:0}°C":$"{v:0}% used";
    }
    internal MonitorHistory.Sample[] DiskSamples(string? key, double since)
        => DiskMetricSamples(key,since,MetricKind.DiskRead);
    internal MonitorHistory.Sample[] DiskMetricSamples(string? key,double since,MetricKind kind)
        => readingSource?.DiskMetricSamples(key,since,kind)??((kind==MetricKind.DiskWrite?diskWriteHistory:diskHistory).TryGetValue((key?.StartsWith("device:")==true?SelectedDisk(key)?.Id:key)??(key==null?"_Total":"missing"),out var samples)?samples.Where(s=>s.Time>=since).ToArray():[]);
    private void QueueDiskRefresh()
    {
        if (readingSource != null || isDisposed || DiagnosticState.Root != null || !sensorsActive ||
            !History.Activated.Any(IsDiskRate) || Interlocked.Exchange(ref diskPending, 1) != 0) return;
        _ = Task.Run(() =>
        {
            try
            {
                diskReader ??= new();
                diskWriteReader ??= new(true);
                var result = diskReader.Read();
                // Preserve the physical disk number as the selection key; volume
                // letters can change independently of the device instance.
                var normalized = result.ToDictionary(p => p.Key == "_Total" ? p.Key : p.Key.Split(' ')[0], p => p.Value, StringComparer.OrdinalIgnoreCase);
                var writes=diskWriteReader.Read().ToDictionary(p=>p.Key=="_Total"?p.Key:p.Key.Split(' ')[0],p=>p.Value,StringComparer.OrdinalIgnoreCase);
                if (!isDisposed){Volatile.Write(ref diskSnapshot, normalized);Volatile.Write(ref diskWriteSnapshot,writes);diskSampledAt=DateTime.UtcNow;}
            }
            catch { if(!isDisposed){Volatile.Write(ref diskSnapshot,new Dictionary<string,double?>());Volatile.Write(ref diskWriteSnapshot,new Dictionary<string,double?>());} }
            finally
            {
                if (isDisposed) { diskReader?.Dispose(); diskReader = null; diskWriteReader?.Dispose();diskWriteReader=null; }
                Interlocked.Exchange(ref diskPending, 0);
            }
        });
    }
    private void RecordDiskHistory(double now)
    {
        Record(diskHistory,Volatile.Read(ref diskSnapshot));Record(diskWriteHistory,Volatile.Read(ref diskWriteSnapshot));
        void Record(Dictionary<string,Queue<MonitorHistory.Sample>> diskHistory,IReadOnlyDictionary<string,double?> readings){
        foreach (string key in readings.Keys) diskHistory.TryAdd(key, new());
        foreach (var (key, samples) in diskHistory)
        {
            while (samples.TryPeek(out var old) && old.Time < now - MonitorHistory.MaximumSeconds) samples.Dequeue();
            samples.Enqueue(new(now, readings.GetValueOrDefault(key)));
            while (samples.Count > 4002) samples.Dequeue();
        }}
    }
}
