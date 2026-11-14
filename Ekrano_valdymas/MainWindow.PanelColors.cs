using System.Windows;
using System.Windows.Media;
namespace Ekrano_valdymas;
public partial class MainWindow
{
    private string? panelLabelColor=ProductColorDefaults.Labels, moduleTitleColor=ProductColorDefaults.Titles;
    private void SetPanelTextColor(AccentColorDialog.ColorTarget target,Color color,double intensity)
    {
        string value=$"#{(byte)Math.Round(255*Math.Clamp(intensity,0,100)/100):X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        if(target==AccentColorDialog.ColorTarget.Titles)moduleTitleColor=value;else panelLabelColor=value;
        ApplyPanelTextOverrides();
    }
    private void ApplyPanelTextOverrides()
    {
        Color fallback=CommonMetal.IsLight?Color.FromRgb(30,31,31):Color.FromRgb(240,239,235);
        void Set(string key,string? value)
        {
            Color color=fallback;
            try { if(value!=null)color=(Color)ColorConverter.ConvertFromString(value); } catch { }
            if(Application.Current.Resources[key] is SolidColorBrush brush&&!brush.IsFrozen)brush.Color=color;
            else Application.Current.Resources[key]=new SolidColorBrush(color);
        }
        Set("PanelTextBrush",panelLabelColor);Set("ModuleTitleBrush",moduleTitleColor);
        systemMonitorWidget?.RefreshPanelPalette();
    }
}
