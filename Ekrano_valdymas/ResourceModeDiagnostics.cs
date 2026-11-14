using System.Diagnostics;
using System.IO;
using System.Text.Json;
namespace Ekrano_valdymas;
internal static class ResourceModeDiagnostics
{
    private static ResourceMode? recorded;
    internal static void RecordHardwareSource(string source)
    {
        if(!InteractionDiagnostics.Enabled)return;
        try
        {
            var folder=InteractionDiagnostics.OutputDirectory!;
            Directory.CreateDirectory(folder);
            using var process=Process.GetCurrentProcess();
            string json=JsonSerializer.Serialize(new{Time=DateTimeOffset.UtcNow,PID=process.Id,StartTime=process.StartTime.ToUniversalTime(),Source=source});
            string current=Path.Combine(folder,"sensor-source-current.json");
            File.WriteAllText(current+".tmp",json);File.Move(current+".tmp",current,true);
            string events=Path.Combine(folder,"sensor-source-events.jsonl");
            if(File.Exists(events)&&new FileInfo(events).Length>1024*1024)File.Move(events,events+".previous",true);
            File.AppendAllText(events,json+Environment.NewLine);
        }
        catch(Exception error){Trace.WriteLine("Sensor source diagnostic write: "+error.Message);}
    }
    internal static void Record(ResourceMode mode)
    {
        if(!InteractionDiagnostics.Enabled||recorded==mode)return;
        try
        {
            var folder=InteractionDiagnostics.OutputDirectory!;
            Directory.CreateDirectory(folder);
            using var process=Process.GetCurrentProcess();
            var state=new {Time=DateTimeOffset.UtcNow,PID=process.Id,StartTime=process.StartTime.ToUniversalTime(),Mode=mode.ToString(),Source="ResourcePolicy.EffectiveMode"};
            string json=JsonSerializer.Serialize(state);
            var current=Path.Combine(folder,"resource-mode-current.json");
            File.WriteAllText(current+".tmp",json);File.Move(current+".tmp",current,true);
            var events=Path.Combine(folder,"resource-mode-events.jsonl");
            if(File.Exists(events)&&new FileInfo(events).Length>1024*1024)File.Move(events,events+".previous",true);
            File.AppendAllText(events,json+Environment.NewLine);
            recorded=mode;
        }
        catch(Exception error){Trace.WriteLine("Resource mode diagnostic write: "+error.Message);}
    }
}
