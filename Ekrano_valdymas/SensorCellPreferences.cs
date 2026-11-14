using System.IO;
using System.Text.Json;
using static Ekrano_valdymas.SystemMonitorWidget;
namespace Ekrano_valdymas;

// A location override is opt-in; untouched cells inherit the legacy metric preference.
internal static class SensorCellPreferences
{
    private static string? loadedPath;
    private static Dictionary<string,MetricPresentation> values=new();
    internal static event Action? Changed;
    private static string FilePath=>Path.Combine(DiagnosticState.LocalApplicationData,"Ekrano_valdymas","sensor-cells.json");
    private static void Load(){if(loadedPath==FilePath)return;loadedPath=FilePath;try{values=File.Exists(FilePath)?JsonSerializer.Deserialize<Dictionary<string,MetricPresentation>>(File.ReadAllText(FilePath))??new():new();}catch{values=new();}}
    internal static MetricPresentation Get(string key,MetricPresentation fallback){Load();return values.GetValueOrDefault(key)??fallback;}
    internal static void Set(string key,MetricPresentation value){Load();AtomicJson.Write(FilePath,values.Append(new KeyValuePair<string,MetricPresentation>(key,value)).GroupBy(p=>p.Key).ToDictionary(g=>g.Key,g=>g.Last().Value));values[key]=value;Changed?.Invoke();}
    internal static MetricPresentation? Capture(string? key,string? target)
    {
        if(target?.StartsWith("sensor:")!=true||!Enum.TryParse<MetricKind>(target[7..],out var kind))return null;
        var fallback=SystemMonitorWidget.Shared?.GetPresentation(kind)??new();
        return SystemMonitorWidget.NormalizeNetworkPresentation(kind,key==null?fallback:Get(key,fallback));
    }
    internal static void ApplyPlacement(IReadOnlyDictionary<string,MetricPresentation?> updates)
    {
        Load();var next=new Dictionary<string,MetricPresentation>(values);
        foreach(var (key,value) in updates){if(value==null)next.Remove(key);else next[key]=value;}
        AtomicJson.Write(FilePath,next);values=next;Changed?.Invoke();
    }
    internal static void Exchange(string? source,string? sourceTarget,string destination,string? destinationTarget,MetricPresentation? sourceAppearance=null)
    {
        if(source==destination)return;
        var incoming=sourceAppearance??Capture(source,sourceTarget);var outgoing=Capture(destination,destinationTarget);
        Load();var next=new Dictionary<string,MetricPresentation>(values);
        if(source!=null){if(outgoing!=null)next[source]=outgoing;else next.Remove(source);}
        if(incoming!=null)next[destination]=incoming;else next.Remove(destination);
        AtomicJson.Write(FilePath,next);values=next;
    }
    internal static void NotifyPlacement()=>Changed?.Invoke();
    internal static void ReloadForProbe(){if(DiagnosticState.Root==null)throw new InvalidOperationException("Preference reload probe requires isolated diagnostic state.");loadedPath=null;Load();Changed?.Invoke();}
}
