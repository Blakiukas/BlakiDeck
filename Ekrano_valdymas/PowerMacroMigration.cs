using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ekrano_valdymas;

internal static class PowerMacroMigration
{
    internal static void Run()
    {
        Migrate(false);
        Migrate(true);
    }
    private static void Migrate(bool obs)
    {
        string folder=Path.Combine(DiagnosticState.LocalApplicationData,"Ekrano_valdymas");
        Directory.CreateDirectory(folder);string marker=Path.Combine(folder,obs?"obs-macros-v1.json":"power-macros-v1.json");if(File.Exists(marker))return;
        string windowFile=Path.Combine(folder,"window-settings.json");
        var settings=File.Exists(windowFile)?JsonNode.Parse(File.ReadAllText(windowFile))!.AsObject():new JsonObject();
        bool sleepComputer=settings["SleepComputer"]?.GetValue<bool>()??false;
        var library=MacroLibrary.Current;
        var targets=obs?new Dictionary<string,string>{["obs-record"]=MacroLibrary.Target(library.EnsureObsAction("Record")),["obs-buffer"]=MacroLibrary.Target(library.EnsureObsAction("SaveReplay"))}:new Dictionary<string,string>
        {
            ["sleep"]=MacroLibrary.Target(library.EnsureSystemAction("Sleep",sleepComputer?"Computer":"Monitors")),
            ["shutdown"]=MacroLibrary.Target(library.EnsureSystemAction("Shutdown")),
            ["restart"]=MacroLibrary.Target(library.EnsureSystemAction("Restart"))
        };
        if(obs)library.EnsureObsAction("ReplayBuffer");
        else {library.EnsureSystemAction("Sleep","Computer");library.EnsureSystemAction("Sleep","Monitors");}
        var writes=new Dictionary<string,JsonNode>();
        void Write(string path,JsonNode value)=>writes[path]=value;
        var external=new HashSet<string>();
        foreach(string file in Directory.GetFiles(folder,"module-shortcuts*.json").Where(p=>System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(p),@"^module-shortcuts(-2|-wide|-wide-2|-wide-3|-tab[2-5])?\.json$")))
        {
            var entries=JsonNode.Parse(File.ReadAllText(file))!.AsArray();bool changed=false;
            if(File.Exists(file+".before-power-macros"))
                foreach(var old in JsonNode.Parse(File.ReadAllText(file+".before-power-macros"))!.AsArray())
                    if(old?.GetValue<string>() is string value&&value.StartsWith("control:")&&targets.ContainsKey(value[8..]))external.Add(value[8..]);
            for(int i=0;i<entries.Count;i++)if(entries[i]?.GetValue<string>() is string value&&value.StartsWith("control:")&&targets.TryGetValue(value[8..],out string? target)){external.Add(value[8..]);entries[i]=target;changed=true;}
            if(changed)Write(file,entries);
        }
        string barFile=Path.Combine(folder,"quick-launch.json"),orderFile=Path.Combine(folder,"top-bar-layout.json");
        var bar=File.Exists(barFile)?JsonNode.Parse(File.ReadAllText(barFile))!.AsArray():new JsonArray();
        var order=File.Exists(orderFile)?JsonNode.Parse(File.ReadAllText(orderFile))!.AsArray():new JsonArray();
        bool barChanged=false;
        foreach(var pair in targets)
        {
            int index=-1;for(int i=0;i<order.Count;i++)if(order[i]?.GetValue<string>()==pair.Key){index=i;break;}
            string setting=pair.Key switch {"sleep"=>"ShowSleep","shutdown"=>"ShowShutdown","restart"=>"ShowRestart","obs-record"=>"ShowObsRecord",_=>"ShowObsBuffer"};
            bool visible=settings[setting]?.GetValue<bool>()??(!InstallationDefaults.IsNewInstall && pair.Key is "sleep" or "shutdown");
            if(index>=0||(visible&&!external.Contains(pair.Key)))
            {
                // A legacy control becomes a reference at the same ordering position.
                int slot=-1;for(int i=0;i<bar.Count;i++)if(bar[i]?["Target"]?.GetValue<string>()==pair.Value){slot=i;break;}
                if(slot<0){slot=bar.Count;bar.Add(new JsonObject{["Target"]=pair.Value,["DisplayName"]=library.Title(pair.Value)});}
                if(index>=0)order[index]="slot:"+slot;else if(!order.Any(n=>n?.GetValue<string>()=="slot:"+slot))order.Add("slot:"+slot);barChanged=true;
            }
            settings[setting]=false;
        }
        if(barChanged){Write(barFile,bar);Write(orderFile,order);}
        Write(windowFile,settings);
        // Validate the whole input before replacing any settings, then preserve originals.
        string backup=obs?".before-obs-macros":".before-power-macros";
        foreach(string path in writes.Keys)if(File.Exists(path)&&!File.Exists(path+backup))File.Copy(path,path+backup);
        foreach(var write in writes)AtomicJson.Write(write.Key,write.Value);
        AtomicJson.Write(marker,new{version=1,actions=targets});
    }
}
