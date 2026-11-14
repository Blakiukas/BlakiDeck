using System.Windows.Media;
namespace Ekrano_valdymas;
public partial class QuickLaunchBar
{
    private static readonly Dictionary<string,Brush> ChevronBrushes=new()
    {
        ["powder-black"]=FrozenChevron(0x6E,0x6E,0x6E),
        ["light"]=FrozenChevron(0x2D,0x28,0x25),
        ["brushed-light"]=FrozenChevron(0x5C,0x53,0x4B),
        ["carbon-fiber"]=FrozenChevron(0xC2,0xB7,0xAC),
        ["dark"]=FrozenChevron(0xB8,0xB3,0xB0),
        ["rust-iron"]=FrozenChevron(0xB3,0xAF,0xAD),
        ["militarity"]=FrozenChevron(0x91,0x86,0x77)
    };
    private static Brush FrozenChevron(byte r,byte g,byte b){var brush=new SolidColorBrush(Color.FromRgb(r,g,b));brush.Freeze();return brush;}
    private static Brush SkinChevronBrush()=>!CommonMetal.NoSkin&&ChevronBrushes.TryGetValue(CommonMetal.FinishName,out var brush)?brush:Brushes.White;
}
