using System.IO;
namespace Ekrano_valdymas;

internal static class MacroDefaultIcons
{
    internal const string Prefix="blakideck-icon:";
    internal static string Reference(string file)=>Prefix+file;
    internal static string Resolve(string value)=>value.StartsWith(Prefix,StringComparison.Ordinal)
        ? DefaultIconCatalog.GetPath(value[Prefix.Length..])??"⚡" : value;
    internal static string For(string kind,string target="Computer")=>Reference(kind switch
    {
        "Sleep"=>target=="Monitors"?"Sleep monitor.png":"Sleep pc.png",
        "Shutdown"=>"Shut down PC.png", "Restart"=>"Restart PC2.png",
        "Record"=>"OBS recording.png", "SaveReplay"=>"OBS save replay.png",
        "ReplayBuffer"=>"OBS replay buffer ON.png", "MicMute"=>"mic.png", _=>"mic.png"
    });
    // Only identical bundled files are converted; custom images remain untouched.
    internal static string Portable(string value)
    {
        if(!Path.IsPathFullyQualified(value))return value;
        string name=Path.GetFileName(value);string? packaged=DefaultIconCatalog.GetPath(name);
        if(packaged==null||!File.Exists(value))return value;
        try { return File.ReadAllBytes(value).AsSpan().SequenceEqual(File.ReadAllBytes(packaged))?Reference(name):value; }
        catch(IOException){return value;}
    }
}
