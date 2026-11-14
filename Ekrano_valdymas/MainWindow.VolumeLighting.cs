using System.Windows.Media;
namespace Ekrano_valdymas;
public partial class MainWindow
{
    private void ApplyVolumeLighting(double percent)
    {
        musicVolumeLightIntensity=double.IsFinite(percent)?Math.Clamp(percent,0,100):50;
        musicConsole?.SetVolumeLighting(musicVolumeLightIntensity);
        UpdateVolumeLightChecks();
    }
    private void UpdateVolumeLightChecks()
    {
        foreach(var row in volumeLightMenu.Items.OfType<System.Windows.Controls.MenuItem>())
            if(row.Tag is double value)row.IsChecked=Math.Abs(value-musicVolumeLightIntensity)<.01;
        volumeFollowColorMenu.IsChecked=musicVolumeLightColor==null;
    }
    private void ApplyVolumeLightColor(string? value)
    {
        Color? color=null;
        try{if(!string.IsNullOrWhiteSpace(value))color=(Color)ColorConverter.ConvertFromString(value);}catch{ }
        musicVolumeLightColor=color is Color c?$"#{c.R:X2}{c.G:X2}{c.B:X2}":null;
        musicConsole?.SetVolumeLightColor(color);UpdateVolumeLightChecks();
    }
    private void ChooseVolumeLightColor()
    {
        ContextMenu.IsOpen=false;
        string? previous=musicVolumeLightColor;
        var color=(Color)ColorConverter.ConvertFromString(previous??musicLightColor);
        var dialog=new AccentColorDialog(color,color,color,color,color,100,100,100,100,100,savedPaletteColors,color,100){Owner=this};
        dialog.ConfigureKnobOnly();
        dialog.PreviewChanged+=(_,chosen,_)=>ApplyVolumeLightColor($"#{chosen.R:X2}{chosen.G:X2}{chosen.B:X2}");
        if(dialog.ShowDialog()==true){var chosen=dialog.MusicColor;ApplyVolumeLightColor($"#{chosen.R:X2}{chosen.G:X2}{chosen.B:X2}");SaveWindowSettings();}
        else ApplyVolumeLightColor(previous);
    }
}
