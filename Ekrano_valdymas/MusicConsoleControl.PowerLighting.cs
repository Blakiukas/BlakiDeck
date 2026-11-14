using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
namespace Ekrano_valdymas;
internal sealed partial class MusicConsoleControl
{
    private readonly SolidColorBrush powerLightBrush=new(Colors.LimeGreen);
    private Rectangle? powerLightLayer;
    private string? powerLightColor;
    private double powerLightIntensity=100;
    private ImageBrush? powerEmissionMask;
    internal double PowerLightIntensity=>powerLightIntensity;
    internal Color PowerLightingColor=>powerLightBrush.Color;
    internal void SetPowerLighting(string? color,double intensity)
    {
        powerLightColor=color;powerLightIntensity=double.IsFinite(intensity)?Math.Clamp(intensity,0,100):100;
        try{powerLightBrush.Color=color==null?DefaultPowerColor(instanceFinish):(Color)ColorConverter.ConvertFromString(color);}catch{powerLightColor=null;powerLightBrush.Color=DefaultPowerColor(instanceFinish);}
        RefreshPowerLight();
    }
    internal static Color DefaultPowerColor(string finish)=>finish=="militarity"?Color.FromRgb(214,151,68):Color.FromRgb(87,190,64);
    private void RefreshPowerLight()
    {
        if(powerLightLayer==null||powerCap==null)return;
        if(powerLightColor==null&&powerLightIntensity==100){powerCap.Source=Bitmap(powerOn?"power-push-on.png":"power-push-off.png",finish:instanceFinish);powerLightLayer.Opacity=0;return;}
        powerCap.Source=Bitmap("power-push-off.png",finish:instanceFinish);
        if(powerEmissionMask==null){
            // Direct Blender coverage of the original light-guide geometry.
            // Avoid subtracting independently sampled renders at the rim.
            powerEmissionMask=new ImageBrush(Bitmap("power-light-mask.png",finish:"dark"));powerEmissionMask.Freeze();
            powerLightLayer.OpacityMask=powerEmissionMask;
            RenderOptions.SetBitmapScalingMode(powerLightLayer,BitmapScalingMode.HighQuality);
        }
        powerLightLayer.Opacity=powerOn?powerLightIntensity/100*(1-externalDim):0;
    }
}
public partial class MainWindow
{
    private string? musicPowerLightColor;
    private double musicPowerLightIntensity=100;
    private void ApplyPowerLighting(string? color,double intensity){musicPowerLightColor=color;musicPowerLightIntensity=double.IsFinite(intensity)?Math.Clamp(intensity,0,100):100;musicConsole?.SetPowerLighting(color,musicPowerLightIntensity);}
}
