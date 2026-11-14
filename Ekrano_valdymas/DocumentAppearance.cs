using System.Windows.Media;
namespace Ekrano_valdymas;
internal static class DocumentAppearance
{
    internal static readonly Brush Background=Frozen(25,23,19);
    internal static readonly Brush Border=Frozen(104,100,94);
    private static Brush Frozen(byte r,byte g,byte b){var brush=new SolidColorBrush(Color.FromRgb(r,g,b));brush.Freeze();return brush;}
}
