using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Ekrano_valdymas;

internal static class SettingsBackup
{
    private const string Marker = "BlakiDeck settings 1";
    private const string Prefix = "blakideck-backup:";
    private const long MaxSize = 8L << 30;
    private static readonly HashSet<string> Extra = new(StringComparer.OrdinalIgnoreCase)
    {
        "window-settings.json","language.json","resource-mode.json","tooltip-delay.json","recent-colors.json",
        "macros.json","power-macros-v1.json","obs-macros-v1.json","web-shortcuts.json","web-zoom-settings.json",
        "interaction-feedback.json","local-playlist.json","blakihud-visualizer.json","monitor-controls.json",
        "shortcut-icon-effects.json","weather-translations-v1.json","installation-defaults.json","hue-settings.json",
        "macro-library-usage.json","vu-sensitivity.json"
    };
    private static bool Setting(string name) =>
        Regex.IsMatch(name, @"\A[a-z0-9][a-z0-9.-]{0,100}\.json\z") &&
        (Extra.Contains(name) || ApplicationProfileReset.IsLayoutFile(name) || name.StartsWith("system-monitor",StringComparison.Ordinal));
    private static bool Attachment(string name) =>
        Regex.IsMatch(name, @"\A[0-9a-f]{32}\.(png|jpg|jpeg|gif|bmp|webp|ico|mp4|wmv|avi|mov|mkv|m4v|webm|tiff?)\z");

    internal static void Export(string folder,string destination)
    {
        string temp=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var zip=ZipFile.Open(temp,ZipArchiveMode.Create))
            {
                using(var marker=new StreamWriter(zip.CreateEntry("format.txt").Open())) marker.Write(Marker);
                var files=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                long total=0;
                foreach(string path in Directory.EnumerateFiles(folder,"*.json").Where(p=>Setting(Path.GetFileName(p))))
                {
                    var node=JsonNode.Parse(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty settings file.");
                    if(Path.GetFileName(path)=="hue-settings.json")node.AsObject().Remove("ProtectedApplicationKey");
                    MapStrings(node,"",(key,value)=>
                    {
                        if(!Path.IsPathFullyQualified(value) || !File.Exists(value) ||
                            !(key.Contains("icon",StringComparison.OrdinalIgnoreCase) || key.Contains("image",StringComparison.OrdinalIgnoreCase) ||
                              key.Contains("preview",StringComparison.OrdinalIgnoreCase) || key.Contains("wallpaper",StringComparison.OrdinalIgnoreCase) ||
                              key is "File" or "VisualizerVideoPath" or "BackgroundPath" or "PhotoPath"))return value;
                        string id=Guid.NewGuid().ToString("N")+Path.GetExtension(value).ToLowerInvariant();
                        if(!Attachment(id))return value;
                        if(!files.TryGetValue(value,out string? stored))
                        {
                            total+=new FileInfo(value).Length;
                            if(total>MaxSize)throw new IOException("Selected media exceeds the 8 GB backup limit.");
                            zip.CreateEntryFromFile(value,"assets/"+id,CompressionLevel.NoCompression);
                            files[value]=stored=id;
                        }
                        return Prefix+stored;
                    });
                    using var stream=zip.CreateEntry(Path.GetFileName(path)).Open();
                    JsonSerializer.Serialize(stream,node);
                }
            }
            File.Move(temp,destination,true);
        }
        finally {File.Delete(temp);}
    }

    private static void MapStrings(JsonNode node,string key,Func<string,string,string> map)
    {
        if(node is JsonObject obj)
            foreach(var (name,child) in obj.ToArray())
            {
                if(child is JsonValue val && val.TryGetValue<string>(out var s))obj[name]=map(name,s);
                else if(child!=null)MapStrings(child,name,map);
            }
        else if(node is JsonArray array)
            for(int i=0;i<array.Count;i++)
            {
                if(array[i] is JsonValue val && val.TryGetValue<string>(out var s))array[i]=map(key,s);
                else if(array[i] is {} child)MapStrings(child,key,map);
            }
    }

    internal static void Restore(string folder,string archive)
    {
        Directory.CreateDirectory(folder);
        if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked settings folder.");
        string stage=Path.Combine(folder,"restore-"+Guid.NewGuid().ToString("N"));
        string assets="imported-"+Guid.NewGuid().ToString("N");
        string backup=Path.Combine(stage,"previous");
        Directory.CreateDirectory(backup);
        var installed=new List<string>();
        var originals=new List<string>();
        bool keepRecovery=false;
        try
        {
            using(var zip=ZipFile.OpenRead(archive))
            {
                if(zip.Entries.Count>10000 || zip.Entries.Sum(e=>e.Length)>MaxSize+(64L<<20) ||
                    zip.GetEntry("format.txt") is not {Length:<100} marker || zip.GetEntry("window-settings.json")==null ||
                    zip.GetEntry("dashboard-layout-tabs.json")==null)throw new InvalidDataException("Invalid BlakiDeck settings archive.");
                using(var reader=new StreamReader(marker.Open()))if(reader.ReadToEnd()!=Marker)throw new InvalidDataException("Unsupported backup version.");
                var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var e in zip.Entries)
                {
                    string name=e.FullName;
                    if(!seen.Add(name))throw new InvalidDataException("Duplicate backup entry.");
                    if(name=="format.txt")continue;
                    if(name.StartsWith("assets/",StringComparison.Ordinal) && Attachment(name[7..]))
                    {
                        string path=Path.Combine(stage,assets);
                        Directory.CreateDirectory(path);
                        e.ExtractToFile(Path.Combine(path,name[7..]));
                        continue;
                    }
                    if(!Setting(name) || e.Length>32L<<20)throw new InvalidDataException("Unexpected backup entry.");
                    using var input=e.Open();
                    var node=JsonNode.Parse(input) ?? throw new InvalidDataException("Empty settings.");
                    if(name=="window-settings.json" && node is not JsonObject || name=="dashboard-layout-tabs.json" && node is not JsonArray)
                        throw new InvalidDataException("Invalid layout data.");
                    if(name=="hue-settings.json")
                    {
                        node.AsObject().Remove("ProtectedApplicationKey");
                        string current=Path.Combine(folder,name);
                        if(File.Exists(current) && JsonNode.Parse(File.ReadAllText(current)) is JsonObject hue &&
                            JsonNode.DeepEquals(hue["BridgeId"],node["BridgeId"]))
                            node["ProtectedApplicationKey"]=hue["ProtectedApplicationKey"]?.DeepClone();
                    }
                    MapStrings(node,"",(_,value)=>{
                        if(!value.StartsWith(Prefix,StringComparison.Ordinal))return value;
                        string id=value[Prefix.Length..];
                        if(!Attachment(id)||zip.GetEntry("assets/"+id)==null)throw new InvalidDataException("Missing backup asset.");
                        return Path.Combine(folder,assets,id);
                    });
                    File.WriteAllText(Path.Combine(stage,name),node.ToJsonString());
                }
            }
            foreach(var path in Directory.GetFiles(folder))
            {
                string name=Path.GetFileName(path);
                string settingName = name.EndsWith(".previous",StringComparison.Ordinal) ? name[..^9] : name;
                if (!Setting(settingName)) continue;
                if (settingName == "hue-settings.json" && !File.Exists(Path.Combine(stage, settingName))) continue;
                File.Move(path,Path.Combine(backup,name)); originals.Add(name);
            }
            if(Directory.Exists(Path.Combine(stage,assets)))Directory.Move(Path.Combine(stage,assets),Path.Combine(folder,assets));
            foreach(var path in Directory.GetFiles(stage))
            {
                string dest=Path.Combine(folder,Path.GetFileName(path));
                File.Move(path,dest); installed.Add(dest);
            }
        }
        catch
        {
            keepRecovery=true;
            foreach(var path in installed)File.Delete(path);
            foreach(var name in originals)File.Move(Path.Combine(backup,name),Path.Combine(folder,name));
            string added=Path.Combine(folder,assets);
            if(Directory.Exists(added))Directory.Delete(added,true);
            keepRecovery=false;
            throw;
        }
        finally {if(!keepRecovery)Directory.Delete(stage,true);}
    }
}
