using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Ekrano_valdymas;
// Metal and saved inlay values. No runtime dependency on lab files.
internal static class InstalledIconMaterials
{
 internal static BitmapSource Render(FrameworkElement owner,ImageSource source,bool dimmed,string? style=null,double? sampling=null,Brush? material=null,bool glyph=false)
 {
  bool light=CommonMetal.IsLight;
  return (style??IconAppearance.Style) switch {
   "inlay"=>light?ExperimentalIconRelief.Render(owner,source,dimmed,1.76,1.35,.85,0,"inlay",.48,material,sampling):ExperimentalIconRelief.Render(owner,source,dimmed,1.81,1.4,1.4,0,"inlay",.25,material,sampling),
   _=>PressedMetalRelief.Render(owner,source,dimmed,depthMultiplier:glyph?1:1.8,material:material,sampling:sampling,normalizeIcon:!glyph)
  };
 }
}


