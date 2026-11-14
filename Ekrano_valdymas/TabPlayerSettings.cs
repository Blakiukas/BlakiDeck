namespace Ekrano_valdymas;
internal sealed record TabPlayerSettings
{
    public string? ClockStyle {get;init;}
    public string? PanelLabelColor {get;set;}=ProductColorDefaults.Labels;
    public string? ModuleTitleColor {get;set;}=ProductColorDefaults.Titles;
    public bool PowerOnAtStartup {get;set;}
    public bool PowerOn {get;set;}=false;
    public double Sensitivity {get;set;}=MusicConsoleControl.DefaultVuSensitivity;
    public double? LocalSensitivity {get;set;}
    public double? StreamingSensitivity {get;set;}
    internal double SensitivityFor(bool local)=> (local?LocalSensitivity:StreamingSensitivity)??Sensitivity;
    public bool BackgroundSkinEnabled {get;set;}=true;
    public string Finish {get;set;}=MusicFinish.ProfileDefaultName;
    // Null is legacy data; resolve once from the old window-wide preference.
    public string? IconStyle {get;set;}
    public string LightColor {get;set;}=ProductColorDefaults.Buttons;
    public double LightIntensity {get;set;}=100;
    public double VolumeLightIntensity {get;set;}=50;
    public string? VolumeLightColor {get;set;}=ProductColorDefaults.Knob;
    public string? PowerLightColor {get;set;}
    public double PowerLightIntensity {get;set;}=100;
    public Dictionary<string,PlayerSkinLights>? SkinLights {get;set;}
    public bool DimEnabled {get;set;}
    public int DimSeconds {get;set;}=10;
    public PlayerTypography Typography {get;set;}=new();
    public PlaylistSettings Playlist {get;set;}=new();
    public PlaylistSettings WidePlaylist {get;set;}=new();
}
internal sealed partial class MusicConsoleControl
{
    internal bool UsesTabSettings {get;set;}
    internal event EventHandler? SettingsChanged;
    internal string FinishName=>instanceFinish;
    internal void RestoreTabControls(TabPlayerSettings settings)
    {
        widePlaylist.ApplyAppearance(settings.WidePlaylist??new());
        SetVuSensitivity(settings.SensitivityFor(player.SelectedService==MusicService.Local));
        SetVolumeLighting(settings.VolumeLightIntensity);
        try{SetVolumeLightColor(settings.VolumeLightColor is string color?(System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color):null);}catch{SetVolumeLightColor(null);}
        SetPowerLighting(settings.PowerLightColor,settings.PowerLightIntensity);
        powerOn=settings.PowerOn;
        // Restoring a saved panel state must never send Pause to the shared source.
        screenPowerFrom=songPowerFrom=screenPowerTarget=powerOn?1:0;
        screenPowerStart=-10;powerOffTimestamp=0;
        if(!powerOn)foreach(var needle in needles)needle.Angle=-50;
        UpdatePowerVisual();
        UpdateAudioActivity();
    }
}
