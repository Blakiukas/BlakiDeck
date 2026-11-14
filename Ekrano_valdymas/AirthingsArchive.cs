using System.IO;
using System.Globalization;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;
namespace Ekrano_valdymas;

internal sealed record AirthingsPoint(DateTimeOffset Time,double Value,string Unit);
internal sealed class AirthingsArchive
{
    private readonly string path=Path.Combine(DiagnosticState.LocalApplicationData,"Ekrano_valdymas","airthings-history.json");
    private Dictionary<string,List<AirthingsPoint>> points=new();
    private readonly object writers=new();
    private Dictionary<string,AirthingsPoint[]> snapshot=new();
    internal AirthingsArchive(){try{points=JsonSerializer.Deserialize<Dictionary<string,List<AirthingsPoint>>>(File.ReadAllText(path))??new();}catch{}Prune();PublishSnapshot();}
    private void PublishSnapshot()=>Volatile.Write(ref snapshot,points.ToDictionary(p=>p.Key,p=>p.Value.ToArray()));
    private static string Key(AirthingsDevice d,string sensor)=>d.Account+":"+d.Serial+":"+sensor.ToLowerInvariant();
    internal AirthingsPoint[] Read(AirthingsDevice d,string sensor)=>Volatile.Read(ref snapshot).TryGetValue(Key(d,sensor),out var list)?list.ToArray():[];
    internal void Record(IEnumerable<AirthingsDevice> devices){lock(writers){bool changed=false;foreach(var d in devices)if(d.Recorded is {} time)foreach(var r in d.Readings)if(r.Value is {} value&&double.IsFinite(value)){var key=Key(d,r.SensorType);if(!points.TryGetValue(key,out var list))points[key]=list=new();if(list.Count==0||list[^1].Time<time){list.Add(new(time,value,r.Unit));changed=true;}}if(changed)Save();}}
    private void Prune(){var cutoff=DateTimeOffset.UtcNow.AddDays(-366);foreach(var key in points.Keys.ToArray()){points[key]=points[key].Where(p=>p.Time>=cutoff&&p.Time<=DateTimeOffset.UtcNow.AddMinutes(10)&&double.IsFinite(p.Value)).GroupBy(p=>p.Time).Select(g=>g.Last()).OrderBy(p=>p.Time).TakeLast(110000).ToList();if(points[key].Count==0)points.Remove(key);}}
    private void Save(){Prune();PublishSnapshot();AtomicJson.Write(path,points);}
    internal int Import(string file,AirthingsDevice device)
    {
        using var csv=new TextFieldParser(file){TextFieldType=FieldType.Delimited,HasFieldsEnclosedInQuotes=true};
        using(var reader=new StreamReader(file)){string first=reader.ReadLine()??"";csv.SetDelimiters(first.Count(c=>c==';')>first.Count(c=>c==',')?";":",");}
        var headers=csv.ReadFields()??[];string Clean(string text)=>new(text.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        int timeIndex=Array.FindIndex(headers,h=>new[]{"date","time","timestamp","datetime","recorded","dateutc","timeutc","datetimeutc","timestamputc"}.Contains(Clean(h)));
        if(timeIndex<0)throw new InvalidDataException("No UTC date/time column found. No data imported.");
        var columns=new List<(int Index,string Sensor,string Unit)>();
        for(int i=0;i<headers.Length;i++){string h=Clean(headers[i]);string? sensor=h.Contains("radon")?(h.Contains("long")?"radonLongTermAvg":"radonShortTermAvg"):h.Contains("humidity")?"humidity":h.Contains("temp")?device.Supported.FirstOrDefault(s=>s is "temp" or "temperature"):h.Contains("pressure")?"pressure":h.Contains("co2")?"co2":h.Contains("voc")?"voc":null;if(sensor==null||!device.Supported.Contains(sensor,StringComparer.OrdinalIgnoreCase))continue;string unit=sensor=="pressure"?"hPa":sensor=="humidity"?"%":sensor is "temp" or "temperature"?"°C":sensor.Contains("radon")?"Bq/m³":sensor=="co2"?"ppm":"ppb";if(h.Contains("pci"))unit="pCi/L";if(h.Contains("fahrenheit")||h.EndsWith("f"))unit="°F";if(h.Contains("inhg"))unit="inHg";if(h.Contains("mmhg"))unit="mmHg";columns.Add((i,sensor,unit));}
        if(columns.Count==0)throw new InvalidDataException("No supported sensor columns found. No data imported.");
        var pending=new List<(string Key,AirthingsPoint Point)>();while(!csv.EndOfData){var row=csv.ReadFields()??[];if(row.Length<=timeIndex||AirthingsService.ParseRecorded(row[timeIndex]) is not {} time)continue;foreach(var c in columns)if(row.Length>c.Index&&double.TryParse(row[c.Index],NumberStyles.Float,CultureInfo.InvariantCulture,out double value)&&double.IsFinite(value)){if(c.Unit=="pCi/L")value*=37;else if(c.Unit=="°F")value=(value-32)*5/9;else if(c.Unit=="inHg")value*=33.8638866667;else if(c.Unit=="mmHg")value*=1.33322387415;string unit=c.Unit is "pCi/L"?"Bq/m³":c.Unit=="°F"?"°C":c.Unit is "inHg" or "mmHg"?"hPa":c.Unit;pending.Add((Key(device,c.Sensor),new(time,value,unit)));}if(pending.Count>1000000)throw new InvalidDataException("CSV exceeds the import limit.");}
        lock(writers){foreach(var p in pending){if(!points.TryGetValue(p.Key,out var list))points[p.Key]=list=new();list.Add(p.Point);}Save();}return pending.Count;
    }
}
