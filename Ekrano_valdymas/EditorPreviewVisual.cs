using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ekrano_valdymas;
internal static class EditorPreviewVisual
{
    internal static FrameworkElement Icon(string value,double size)
    {
        if(File.Exists(value))
        {
            try {var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.UriSource=new Uri(Path.GetFullPath(value));image.EndInit();image.Freeze();return new Image{Source=image,Width=size,Height=size,Stretch=Stretch.Uniform};}catch{}
        }
        return new OpticallyCenteredText{Text=Path.IsPathRooted(value)?"?":value,TextFontFamily=new FontFamily("Segoe UI Symbol"),TextFontSize=size*.65,TextBrush=Brushes.White,Width=size,Height=size,FitToBox=true};
    }
}
