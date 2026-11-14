using System.IO;
using System.Text.Json;

namespace Ekrano_valdymas;
public partial class QuickLaunchBar
{
    internal bool UsesDefaultSourceIcon(int tab)
    {
        var value=barAppearance.Tabs[tab];
        // Existing manually assigned symbols/images retain ownership.
        return value.AutomaticSourceIcon??(string.IsNullOrWhiteSpace(value.Icon)||value.Icon=="◆");
    }
    internal void SetDefaultSourceIcon(int tab,bool enabled,MusicService service)
    {
        var candidate=JsonSerializer.Deserialize<BarAppearance>(JsonSerializer.Serialize(barAppearance))!;
        var value=candidate.Tabs[tab];
        string? name=DefaultIconCatalog.MusicSourceFileName(service);
        string? path=name==null?null:DefaultIconCatalog.GetPath(name);
        bool changed=value.AutomaticSourceIcon!=enabled;
        value.AutomaticSourceIcon=enabled;
        if(enabled)
        {
            // A missing source icon must never leave another source's logo.
            string icon=path??"♫";
            changed|=value.Icon!=icon||value.Presentation=="Text";
            value.Icon=icon;
            if(value.Presentation=="Text")value.Presentation="Icon";
        }
        if(!changed)return;
        AtomicJson.Write(appearanceFile,candidate);barAppearance=candidate;UpdateTabVisuals();
    }
}
