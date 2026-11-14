using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Ekrano_valdymas;
internal sealed partial class MusicConsoleControl
{
    private Image? skinCapLayer,skinGlassLayer;
    private static bool HasSkinAsset(string finish,string file) => file is "vu-card-housing.png" or "player-card-static.png" or "panel-static.png" or "shared-material.png" or "previous-off.png" or "play-off.png" or "next-off.png"
        || MusicFinish.IsNewSkin(finish) && file is "power-push-on.png" or "power-push-off.png" or "power-brand.png" or "power-label.png" or "screw-head.png";
    private static BitmapSource SkinBitmap(string finish,string file)
    {
        if(finish=="militarity" && file=="power-push-on.png")file="power-push-amber.png";
        string key=$"Previews/{finish}/{file}";
        if(TryCachedImage(key,out var cached))return cached;
        var image=new BitmapImage(new Uri("pack://application:,,,/Assets/MusicConsole/"+key));image.Freeze();CacheImage(key,image,finish);return image;
    }
    private void AddInstalledSkinLayers()
    {
        if(!MusicFinish.IsNewSkin(instanceFinish))return;
        bool military=instanceFinish=="militarity";
        var cap=new Image{Source=SkinBitmap(instanceFinish,"volume-cap-0.png"),Width=military?1900:180,Height=military?426:180,IsHitTestVisible=false};
        RenderOptions.SetBitmapScalingMode(cap,BitmapScalingMode.HighQuality);
        Canvas.SetLeft(cap,military?0:1705);Canvas.SetTop(cap,military?0:105);Surface.Children.Insert(2,cap);skinCapLayer=cap;
        if(military){
            var glass=new Image{Source=WideBitmap("aged-glass.png",instanceFinish),Width=1900,Height=426,IsHitTestVisible=false};
            RenderOptions.SetBitmapScalingMode(glass,BitmapScalingMode.HighQuality);Canvas.SetLeft(glass,0);Canvas.SetTop(glass,0);Surface.Children.Add(glass);skinGlassLayer=glass;
        }
    }
}

