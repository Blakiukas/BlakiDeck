using System.Windows.Media;
namespace Ekrano_valdymas;
public partial class MainWindow
{
    private readonly MusicConsoleControl?[] tabConsoles=new MusicConsoleControl?[QuickLaunchBar.MaximumLayoutTabs];
    private readonly LinkedList<int> recentConsoleTabs=new();
    private void TrimTabConsoles()
    {
        // Two recent consoles cover return visits without keeping eleven heavy
        // visual trees. Unloaded consoles retain no audio lease or timer.
        for(int i=0;i<tabConsoles.Length;i++)
            if(i!=activeDashboardLayoutTab&&dashboardLayoutsLoaded&&dashboardLayouts[i]?.MusicConsoleEnabled!=true)
            {tabConsoles[i]=null;recentConsoleTabs.Remove(i);}
        while(recentConsoleTabs.Count>2)
        {int old=recentConsoleTabs.Last!.Value;recentConsoleTabs.RemoveLast();tabConsoles[old]=null;}
    }
    private TabPlayerSettings selectedPlayerSettings=new();
    private static TabPlayerSettings CloneTabPlayerSettings(TabPlayerSettings value)=>value with
    {
        SkinLights=value.SkinLights==null?null:new(value.SkinLights),
        Typography=value.Typography is {} typography?typography with
        {
            Title=typography.Title is {} title?title with{}:null,
            Artist=typography.Artist is {} artist?artist with{}:null,
            Time=typography.Time is {} time?time with{}:null
        }:new(),
        Playlist=value.Playlist is {} playlist?playlist with{}:new(),
        WidePlaylist=value.WidePlaylist is {} widePlaylist?widePlaylist with{}:new()
    };
    private TabPlayerSettings CaptureTabPlayer()
    {
        CaptureActiveVuSensitivity();
        return PlayerSkinLights.Remember(new TabPlayerSettings(){SkinLights=selectedPlayerSettings.SkinLights,PowerOnAtStartup=selectedPlayerSettings.PowerOnAtStartup,ClockStyle=selectedPlayerSettings.ClockStyle,PanelLabelColor=panelLabelColor,ModuleTitleColor=moduleTitleColor,IconStyle=IconAppearance.Style,BackgroundSkinEnabled=!panelNoSkin,PowerOn=musicConsole?.PowerOn??selectedPlayerSettings.PowerOn,Sensitivity=selectedPlayerSettings.Sensitivity,LocalSensitivity=selectedPlayerSettings.LocalSensitivity,StreamingSensitivity=selectedPlayerSettings.StreamingSensitivity,Finish=musicConsole?.FinishName??MusicFinish.Name,LightColor=musicLightColor,LightIntensity=musicLightIntensity,VolumeLightIntensity=musicVolumeLightIntensity,VolumeLightColor=musicVolumeLightColor,PowerLightColor=musicPowerLightColor,PowerLightIntensity=musicPowerLightIntensity,DimEnabled=musicDimEnabled,DimSeconds=musicDimDelaySeconds,Typography=selectedPlayerSettings.Typography??new(),Playlist=selectedPlayerSettings.Playlist??new(),WidePlaylist=selectedPlayerSettings.WidePlaylist??new()});
    }
    private void CaptureActiveVuSensitivity()
    {
        // A close, tab switch or source switch can precede the drag's capture-loss
        // callback. Snapshot the visible value while this console still owns it.
        if(musicConsole is {UsesTabSettings:true} console&&ReferenceEquals(tabConsoles[activeDashboardLayoutTab],console))
            StoreVuSensitivity(console.VuSensitivity);
    }
    private void StoreVuSensitivity(double value)
    {
        if(selectedMusicService==MusicService.Local)selectedPlayerSettings.LocalSensitivity=value;else selectedPlayerSettings.StreamingSensitivity=value;
    }
    private void SelectTabConsole(TabPlayerSettings settings,bool rebuild=false)
    {
        int tab=activeDashboardLayoutTab;
        if(!musicConsoleEnabled){tabConsoles[tab]=null;recentConsoleTabs.Remove(tab);}
        else{recentConsoleTabs.Remove(tab);recentConsoleTabs.AddFirst(tab);}
        TrimTabConsoles();
        selectedPlayerSettings=CloneTabPlayerSettings(settings);
        ShortcutIconEffect.ActiveTab=tab;
        quickLaunchBar.RestoreTabClockStyle(settings.ClockStyle);
        panelLabelColor=settings.PanelLabelColor;moduleTitleColor=settings.ModuleTitleColor;
        IconAppearance.SetStyle(settings.IconStyle??IconAppearance.Style);
        UpdateIconStyleChecks();
        MusicFinish.Set(settings.Finish);
        if(CommonMetal.CompleteStartupMaterial())panelMetalInitialized=false;
        ApplyPanelMetal(MusicFinish.IsLight,!settings.BackgroundSkinEnabled,deferSurfaceRefresh:true);
        ApplyPanelTextOverrides();
        musicLightColor=settings.LightColor;
        try{_=(Color)ColorConverter.ConvertFromString(musicLightColor);}catch{musicLightColor="#EEB764";}
        musicLightIntensity=double.IsFinite(settings.LightIntensity)?Math.Clamp(settings.LightIntensity,0,100):100;
        musicVolumeLightIntensity=double.IsFinite(settings.VolumeLightIntensity)?Math.Clamp(settings.VolumeLightIntensity,0,100):50;
        musicVolumeLightColor=settings.VolumeLightColor??settings.LightColor;
        musicPowerLightColor=settings.PowerLightColor;musicPowerLightIntensity=settings.PowerLightIntensity;
        musicDimEnabled=settings.DimEnabled;musicDimDelaySeconds=settings.DimSeconds is 10 or 30 or 120 or 300?settings.DimSeconds:10;
        // A retained player card must not load the new tab's hardware when that
        // tab does not contain the card. Its actual Loaded path refreshes it.
        if(!musicConsoleEnabled&&selectedModules.Any(kind=>kind.ToString()=="MusicPlayerCard")&&musicPlayerCard?.IsLoaded==true&&musicPlayerCard.IsVisible)musicPlayerCard.RefreshFinish();
        analogVuCard?.RefreshSettings(settings.SensitivityFor(selectedMusicService==MusicService.Local));
        musicPlayerCard?.SetLighting((Color)ColorConverter.ConvertFromString(musicLightColor),musicLightIntensity);
        musicPlayerCard?.ApplyTypography(settings.Typography??new());
        // Module-only tabs restore their settings without constructing a hidden
        // hardware console. Build it when that tab actually enables the player.
        if(!musicConsoleEnabled){
            musicConsoleHost.Content=null;musicConsole=null;
            ApplyMusicDimSettings();UpdateFinishChecks();
            return;
        }
        var console=tabConsoles[tab];
        if(rebuild||console==null||console.FinishName!=MusicFinish.Name){
            console=new MusicConsoleControl(spotifyWidget,visualizerAudio){UsesTabSettings=true};
            console.ContextMenu=ContextMenu;
            console.TopBarToggleRequested+=(_,_)=>QueueMusicBarToggle();
            var owned=console;
            console.SettingsChanged+=(_,_)=>{
                if(!dashboardLayoutsLoaded||activeDashboardLayoutTab!=tab||!ReferenceEquals(tabConsoles[tab],owned))return;
                var saved=dashboardLayouts[tab].Player??new TabPlayerSettings();
                StoreVuSensitivity(owned.VuSensitivity);
                analogVuCard?.RefreshSettings(owned.VuSensitivity);
                dashboardLayouts[tab].Player=saved with{PowerOn=owned.PowerOn,LocalSensitivity=selectedPlayerSettings.LocalSensitivity,StreamingSensitivity=selectedPlayerSettings.StreamingSensitivity};
                SaveDashboardLayouts();
            };
            tabConsoles[tab]=console;
        }
        // Reapplying a same-finish profile can retain the existing visual tree.
        // Its controls must still follow the supplied settings.
        console.RestoreTabControls(settings);
        // Detach first: its Unloaded handler releases timers/audio lease before the new one attaches.
        if(!ReferenceEquals(musicConsole,console)){musicConsoleHost.Content=null;musicConsole=console;musicConsoleHost.Content=console;}
        console.SetLighting((Color)ColorConverter.ConvertFromString(musicLightColor),musicLightIntensity);
        console.SetVolumeLighting(musicVolumeLightIntensity);
        ApplyVolumeLightColor(musicVolumeLightColor);
        ApplyPowerLighting(musicPowerLightColor,musicPowerLightIntensity);
        ApplyMusicDimSettings();UpdateFinishChecks();
        CommonMetal.ApplyResourceMode(ResourcePolicy.Mode);
    }
}



