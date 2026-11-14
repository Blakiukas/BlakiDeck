using System.Windows.Media;
namespace Ekrano_valdymas;
internal static class ShortcutIcon
{
    internal static ImageSource? Load(string? path)=>IconBitmap.Load(path);
}
