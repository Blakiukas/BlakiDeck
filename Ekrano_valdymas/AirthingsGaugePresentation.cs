using System.IO;
using System.Text.Json;

namespace Ekrano_valdymas;

// Independent of AirthingsDisplay.View: returning from gauges restores the
// existing overview/numbers/graph choice rather than overwriting that choice.
internal sealed record AirthingsGaugeConflict(string Account,string Serial,string Sensor);
internal sealed record AirthingsGaugePresentation(bool VerticalGauges=false,AirthingsStatusRoom[]? Rooms=null,AirthingsGaugeConflict[]? Conflicts=null,bool GaugeZoom=false)
{
    private static string FileName=>Path.Combine(DiagnosticState.LocalApplicationData,"Ekrano_valdymas","airthings-presentation.json");
    internal static AirthingsGaugePresentation Load()
    {
        try
        {
            var saved=JsonSerializer.Deserialize<AirthingsGaugePresentation>(File.ReadAllText(FileName));
            if(saved?.Rooms!=null)return saved;
            return ImportPlaced(saved??new());
        }
        catch{return ImportPlaced(new());}
    }
    internal void Save()=>AtomicJson.Write(FileName,this);
    internal AirthingsStatusRoom? RoomFor(AirthingsDevice device)=>Rooms?.FirstOrDefault(r=>r.Account==device.Account&&r.Serial==device.Serial);
    internal AirthingsStatusRoom RoomForEditor(AirthingsDevice device)
    {
        var room=RoomFor(device)??new AirthingsStatusRoom(device.Account,device.Serial);
        var saved=room.Sensors??[];var defaults=AirthingsStatusRules.Defaults(device);
        // A placed mini may have supplied only one custom sensor. The status
        // editor treats omitted sensors in a non-null array as excluded; gauge
        // omissions instead mean its ordinary official/default classification.
        // Materialize those defaults without changing explicit saved exclusions.
        var sensors=defaults.Select(fallback=>saved.FirstOrDefault(sensor=>AirthingsStatusRules.Canonical(sensor.Sensor)==AirthingsStatusRules.Canonical(fallback.Sensor))??fallback)
            .Concat(saved.Where(sensor=>!defaults.Any(fallback=>AirthingsStatusRules.Canonical(fallback.Sensor)==AirthingsStatusRules.Canonical(sensor.Sensor)))).ToArray();
        return room with{Sensors=sensors};
    }
    internal bool HasConflicts=>Conflicts?.Length>0;
    internal AirthingsGaugePresentation WithRoomLimits(IEnumerable<AirthingsStatusRoom> changed)
    {
        var updates=changed.ToArray();
        return this with{Rooms=(Rooms??[]).Concat(updates).GroupBy(r=>(r.Account,r.Serial)).Select(g=>g.Last()).ToArray(),
            Conflicts=(Conflicts??[]).Where(conflict=>!updates.Any(room=>room.Account==conflict.Account&&room.Serial==conflict.Serial)).ToArray()};
    }
    private static AirthingsGaugePresentation ImportPlaced(AirthingsGaugePresentation state)
    {
        var targets=new List<string>();
        string folder=Path.GetDirectoryName(FileName)!;
        var names=new List<string>{"quick-launch.json"};
        for(int tab=1;tab<=5;tab++)foreach(string kind in new[]{"regular","second","wide"})names.Add($"dashboard-tab-{tab}-shortcuts-{kind}.json");
        void Read(JsonElement element)
        {
            if(element.ValueKind==JsonValueKind.String&&element.GetString() is string value&&LegacyAirthingsStatusTarget.IsTarget(value))targets.Add(value);
            else if(element.ValueKind==JsonValueKind.Array)foreach(var child in element.EnumerateArray())Read(child);
            else if(element.ValueKind==JsonValueKind.Object)foreach(var property in element.EnumerateObject())Read(property.Value);
        }
        foreach(string name in names)
        {
            try{using var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,name)));Read(document.RootElement);}
            catch(IOException){}catch(JsonException){}catch(UnauthorizedAccessException){}
        }
        return ImportTargets(state,targets);
    }
    internal static AirthingsGaugePresentation ImportTargets(AirthingsGaugePresentation state,IEnumerable<string> targets)
    {
        var candidates=targets.Distinct().SelectMany(target=>LegacyAirthingsStatusTarget.Decode(target).Rooms??[])
            .SelectMany(room=>(room.Sensors??[]).Where(sensor=>sensor.CustomLimits).Select(sensor=>(room.Account,room.Serial,Sensor:sensor with{Sensor=AirthingsStatusRules.Canonical(sensor.Sensor),Included=true})));
        var rooms=new Dictionary<(string Account,string Serial),List<AirthingsStatusSensor>>();var conflicts=new List<AirthingsGaugeConflict>();
        foreach(var group in candidates.GroupBy(item=>(item.Account,item.Serial,item.Sensor.Sensor)))
        {
            var distinct=group.Select(item=>item.Sensor).Distinct().ToArray();
            if(distinct.Length!=1||AirthingsStatusRules.Validate(distinct[0])!=null){conflicts.Add(new(group.Key.Account,group.Key.Serial,group.Key.Sensor));continue;}
            var key=(group.Key.Account,group.Key.Serial);if(!rooms.TryGetValue(key,out var sensors))rooms[key]=sensors=new();sensors.Add(distinct[0]);
        }
        return state with{Rooms=rooms.Select(pair=>new AirthingsStatusRoom(pair.Key.Account,pair.Key.Serial,pair.Value.ToArray())).ToArray(),Conflicts=conflicts.ToArray()};
    }
}
