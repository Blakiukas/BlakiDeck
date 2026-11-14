namespace Ekrano_valdymas;

// Separate lighting memories within each dashboard tab. Never share a mutable
// dictionary between a live preview and the snapshot used by Cancel.
internal sealed record PlayerSkinLights(string Buttons, double ButtonsIntensity,
    string? Knob, double KnobIntensity, string? Power, double PowerIntensity)
{
    internal static string Key(TabPlayerSettings settings) => settings.BackgroundSkinEnabled ? MusicFinish.Normalize(settings.Finish) : "none";
    private static readonly IReadOnlyDictionary<string,PlayerSkinLights> factory = new Dictionary<string,PlayerSkinLights>
    {
        ["light"] = new("#FF9800",100,"#FF9800",80.7843137254902,"#FF9800",43.13725490196079),
        ["dark"] = new("#FF9800",100,"#FF9800",69.41176470588235,"#095701",100),
        ["rust-iron"] = new("#FF9800",100,"#FF9800",73.33333333333333,"#095701",100),
        ["powder-black"] = new("#FF9800",100,"#FF9800",50,null,100),
        ["militarity"] = new("#FF9800",100,"#FF9800",63.13725490196079,"#D69744",76.47058823529412),
        ["brushed-light"] = new("#FF9800",100,"#FF9800",63.13725490196079,"#107606",100),
        ["carbon-fiber"] = new("#FF9800",100,"#FF8000",92.54901960784314,"#0064FF",65.88235294117646),
    };
    internal static PlayerSkinLights Defaults(string skin) => factory.TryGetValue(skin,out var value)?value:new(ProductColorDefaults.Buttons,100,ProductColorDefaults.Knob,50,skin=="none"?ProductColorDefaults.Power:null,100);
    internal static PlayerSkinLights Capture(TabPlayerSettings settings) => new(settings.LightColor,settings.LightIntensity,
        settings.VolumeLightColor,settings.VolumeLightIntensity,settings.PowerLightColor,settings.PowerLightIntensity);
    internal TabPlayerSettings Apply(TabPlayerSettings settings) => settings with {
        LightColor=Buttons,LightIntensity=ButtonsIntensity,VolumeLightColor=Knob,VolumeLightIntensity=KnobIntensity,
        PowerLightColor=Power,PowerLightIntensity=PowerIntensity };
    internal static TabPlayerSettings Remember(TabPlayerSettings settings)
    {
        var memories=settings.SkinLights==null?new Dictionary<string,PlayerSkinLights>():new(settings.SkinLights);
        memories[Key(settings)]=Capture(settings);
        return settings with {SkinLights=memories};
    }
    internal static TabPlayerSettings Switch(TabPlayerSettings settings,string skin)
    {
        settings=Remember(settings); // Legacy values belong only to their saved skin.
        var next=settings with {BackgroundSkinEnabled=skin!="none",Finish=skin=="none"?settings.Finish:MusicFinish.Normalize(skin)};
        string key=Key(next);
        return (settings.SkinLights!.TryGetValue(key,out var saved)?saved:Defaults(key)).Apply(next);
    }
}
