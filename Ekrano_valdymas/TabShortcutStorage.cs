using System.IO;
using System.Text.Json;

namespace Ekrano_valdymas;

internal static class TabShortcutStorage
{
    internal const int LegacyLayoutTabCount=5;
    internal const string ThirdKind="third";
    internal static IReadOnlyList<string> Kinds { get; } =
        Array.AsReadOnly(new[] { "regular", "second", ThirdKind, "wide" });
    internal static string Key(int tab,string kind)=>$"dashboard-tab-{tab+1}-shortcuts-{kind}";

    // Preserve each legacy file family in one owning tab. Originals remain available
    // for recovery; a completed manifest prevents old contents reappearing after moves.
    internal static void Migrate(string folder,int active,Func<int,string,bool> uses)
    {
        string manifest=Path.Combine(folder,"tab-shortcuts-migration-v2.json");
        if(File.Exists(manifest))return;
        Directory.CreateDirectory(folder);
        var owners=new Dictionary<string,int>();
        // There is no legacy third family. Never seed it from the wide family.
        foreach(var pair in new[]{("regular","module-shortcuts"),("second","module-shortcuts-2"),("wide","module-shortcuts-wide")})
        {
            // New tabs must not claim a pre-tab global shortcut file merely
            // because one is selected on the first upgraded launch.
            int owner=active is >=0 and <LegacyLayoutTabCount&&uses(active,pair.Item1)
                ?active:Enumerable.Range(0,LegacyLayoutTabCount).FirstOrDefault(t=>uses(t,pair.Item1));
            string source=pair.Item2;
            if(pair.Item1=="wide"&&!File.Exists(Path.Combine(folder,source+".json")))
                source=File.Exists(Path.Combine(folder,"module-shortcuts-wide-3.json"))?"module-shortcuts-wide-3":"module-shortcuts-wide-2";
            owners[pair.Item1]=owner+1;
            CopyFamily(source,Key(owner,pair.Item1));
        }
        AtomicJson.Write(manifest,owners);

        void CopyFamily(string source,string destination)
        {
            foreach(string suffix in new[]{".json",".json.names.json",".json.presentation.json",".json.icons.json",".json.clocks.json"})
            {
                string input=Path.Combine(folder,source+suffix),output=Path.Combine(folder,destination+suffix);
                if(File.Exists(input)&&!File.Exists(output))File.Copy(input,output);
            }
        }
    }
}
