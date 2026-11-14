using System.IO;
using System.Text.Json;

namespace Ekrano_valdymas;

internal sealed class HudVisualizerProfile
{
    internal const double MaximumOverallVisualSize = 2;
    public bool MusicVisualizerEnabled { get; set; } = true;
    public bool MusicVisualizerAuto { get; set; } = true;
    public double MusicVisualizerHeight { get; set; }
    public double MusicVisualizerSmoothing { get; set; }
    public double MusicVisualizerScale { get; set; }
    public double MusicVisualizerGlow { get; set; }
    public double MusicVisualizerCenterReaction { get; set; }
    public int MusicVisualizerStartBin { get; set; }
    public int MusicVisualizerEndBin { get; set; }
    public bool MusicVisualizerInvert { get; set; }
    public int MusicVisualizerProfileVersion { get; set; }
    public double AccentPosition { get; set; }
    public double TextColorPosition { get; set; }
    // Missing fields in an existing profile retain the imported composition.
    public double OverallVisualSize { get; set; } = 1;
    public bool MirrorSpectrum { get; set; }
    public string? Composition { get; set; }
    internal string EffectiveComposition => Composition is "Contour" or "Mirrored" or "RadialBars" ? Composition : MirrorSpectrum ? "Mirrored" : "Contour";
    public string CenterContent { get; set; } = "Music";
    public string? CustomAccentColor { get; set; }
    public string? CustomTextColor { get; set; }
    private static string FilePath => Path.Combine(DiagnosticState.LocalApplicationData, "Ekrano_valdymas", "blakihud-visualizer.json");
    internal static HudVisualizerProfile Imported()
    {
        // The resource originated in BlakiHUD; its current values are the
        // user-approved BlakiDeck factory defaults, independent of saved profiles.
        using var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/BlakiHud/imported-profile.json")).Stream;
        return JsonSerializer.Deserialize<HudVisualizerProfile>(stream)!;
    }
    internal HudVisualizerProfile Copy() => (HudVisualizerProfile)MemberwiseClone();
    internal static HudVisualizerProfile Load()
    {
        try { var result=File.Exists(FilePath) ? JsonSerializer.Deserialize<HudVisualizerProfile>(File.ReadAllText(FilePath)) ?? Imported() : Imported(); result.Normalize(); return result; }
        catch { return Imported(); }
    }
    internal void Normalize()
    {
        static double Bounded(double value,double min,double max,double fallback=0)=>double.IsFinite(value)?Math.Clamp(value,min,max):fallback;
        MusicVisualizerHeight=Bounded(MusicVisualizerHeight,0,1);
        if(Composition is not ("Contour" or "Mirrored" or "RadialBars"))Composition=null;
        MusicVisualizerSmoothing=Bounded(MusicVisualizerSmoothing,0,1);
        MusicVisualizerScale=Bounded(MusicVisualizerScale,.6,1.1,.725);
        MusicVisualizerGlow=Bounded(MusicVisualizerGlow,0,1);
        MusicVisualizerCenterReaction=Bounded(MusicVisualizerCenterReaction,0,1);
        MusicVisualizerStartBin=Math.Clamp(MusicVisualizerStartBin,0,40);
        MusicVisualizerEndBin=Math.Clamp(MusicVisualizerEndBin,MusicVisualizerStartBin+1,41);
        AccentPosition=Bounded(AccentPosition,0,8,2);TextColorPosition=Bounded(TextColorPosition,0,8);
        OverallVisualSize=Bounded(OverallVisualSize,.75,MaximumOverallVisualSize,1);
        CenterContent=CenterContent is "SourceIcon" or "AlbumCover"?CenterContent:"Music";
        static string? ValidColor(string? value)
        {
            if(string.IsNullOrWhiteSpace(value))return null;
            try{return ((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value)).ToString();}
            catch{return null;}
        }
        CustomAccentColor=ValidColor(CustomAccentColor);CustomTextColor=ValidColor(CustomTextColor);
    }
    internal void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

