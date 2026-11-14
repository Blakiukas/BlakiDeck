using System.IO;
using System.Text.Json;
using System.Windows.Controls;

namespace Ekrano_valdymas;

// Target identity follows a shortcut when it moves between the rail and modules.
// Missing entries preserve each host's existing appearance policy.
internal static class ShortcutIconEffect
{
    private static string FilePath => Path.Combine(DiagnosticState.LocalApplicationData,"Ekrano_valdymas","shortcut-icon-effects.json");
    private static Dictionary<string,string>? saved;
    private static readonly Dictionary<string,string> previews=new(StringComparer.OrdinalIgnoreCase);
    private static int activeTab;
    internal static int ActiveTab { get=>activeTab; set { if(activeTab==value)return;activeTab=value;IconAppearance.Refresh(); } }
    private static string Scope(int tab)=>$"tab:{tab}:";
    private static string Key(string target)=>Scope(ActiveTab)+target;
    private static Dictionary<string,string> Saved
    {
        get
        {
            if(saved!=null)return saved;
            try { saved=new(JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(FilePath))??new(),StringComparer.OrdinalIgnoreCase); }
            catch { saved=new(StringComparer.OrdinalIgnoreCase); }
            return saved;
        }
    }
    internal static string Normalize(string? value)=>value is "original" or "mono" or "metal" or "inlay"?value:"inherit";
    internal static bool IsWebTarget(string? target)=>Uri.TryCreate(target,UriKind.Absolute,out var uri)&&uri.Scheme is "http" or "https";
    internal static bool IndependentDefault(string? target)=>MacroLibrary.IsReference(target)||IsWebTarget(target);
    internal static string Get(string? target)
    {
        if(target==null)return "inherit";
        if(previews.TryGetValue(Key(target),out var preview))return preview;
        if(Saved.TryGetValue(Key(target),out var value))return Normalize(value);
        if(!IndependentDefault(target)&&Saved.TryGetValue(Scope(ActiveTab),out var common))return Normalize(common);
        return Saved.TryGetValue(target,out var legacy)?Normalize(legacy):"inherit";
    }
    internal static void SetCommon(IEnumerable<int> tabs,string value)
    {
        var candidate=new Dictionary<string,string>(Saved,StringComparer.OrdinalIgnoreCase);
        foreach(int tab in tabs)
        {
            string prefix=Scope(tab);
            // Macro appearance belongs to the macro editor, not the tab's icon choice.
            foreach(string key in candidate.Keys.Where(k=>k.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)&&!IndependentDefault(k[prefix.Length..])).ToArray())candidate.Remove(key);
            candidate[prefix]=Normalize(value);
        }
        AtomicJson.Write(FilePath,candidate);saved=candidate;IconAppearance.Refresh();
    }
    internal static void Preview(string target,string? value)
    {
        if(value==null)previews.Remove(Key(target));else previews[Key(target)]=Normalize(value);
        IconAppearance.Refresh();
    }
    internal static void Save(string target,string value)
    {
        var candidate=new Dictionary<string,string>(Saved,StringComparer.OrdinalIgnoreCase);
        if(Normalize(value)=="inherit")
        {
            candidate.Remove(Key(target));
            if((IndependentDefault(target)||!candidate.ContainsKey(Scope(ActiveTab)))&&candidate.ContainsKey(target))candidate[Key(target)]="inherit";
        }
        else candidate[Key(target)]=Normalize(value);
        AtomicJson.Write(FilePath,candidate);saved=candidate;IconAppearance.Refresh();
    }
    internal static ComboBox Editor(Panel panel,string target,bool web=false)
    {
        // A new web link has no URL yet: its empty target must not resolve to
        // the tab-wide key and turn that effect into an individual selection.
        var choice=AppearanceEditorUi.Choice(web&&string.IsNullOrWhiteSpace(target)?"inherit":Get(target),("inherit",web||IsWebTarget(target)?"Default — original colors":"Default — follow host"),("original","Original"),("mono","Monochrome"),("metal","Metal"),("inlay","Inlay"));
        choice.Name="ShortcutIconEffect";
        EditorUi.Field(panel,"Icon effect",choice);
        return choice;
    }
}
