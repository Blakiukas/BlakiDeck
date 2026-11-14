using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace Ekrano_valdymas;
internal sealed partial class MusicConsoleControl
{
    private BitmapSource PanelBitmap(int width)
    {
        string file=$"Wide/{MusicFinish.Normalize(instanceFinish)}/panel-static.png";
        var result=BitmapDecoded(file,legacy:true,finish:instanceFinish,decodeWidth:width);
        // Keep one decoded size per skin. Existing views keep their own live sources.
        foreach(var key in images.Keys.Where(k=>k.StartsWith(file+"@",StringComparison.Ordinal)&&k!=file+"@"+width).ToArray())
        {images.Remove(key);imageFinishes.Remove(key);}
        return result;
    }
    private void TrackPanelResolution(Image chassis)
    {
        int loaded=3072,pending=3072;
        var settle=new DispatcherTimer(DispatcherPriority.Background,Dispatcher){Interval=TimeSpan.FromMilliseconds(160)};
        settle.Tick+=(_,_)=>{settle.Stop();if(!IsLoaded||loaded==pending)return;chassis.Source=PanelBitmap(pending);loaded=pending;};
        void Update(object? sender,EventArgs args)
        {
            if(!IsLoaded||Window.GetWindow(this) is not Window window)return;
            var bounds=Surface.TransformToAncestor(window).TransformBounds(new Rect(0,0,1900,426));
            double pixels=bounds.Width*VisualTreeHelper.GetDpi(window).DpiScaleX;
            if(!double.IsFinite(pixels)||pixels<=0)return;
            // 50% sampling reserve; round up, never upscale beyond the Blender master.
            int requested=Math.Clamp((int)Math.Ceiling(pixels*1.5/256)*256,256,5700);
            if(requested==pending)return;pending=requested;settle.Stop();settle.Start();
        }
        Loaded+=(_,_)=>{pending=loaded;LayoutUpdated+=Update;Update(null,EventArgs.Empty);};
        Unloaded+=(_,_)=>{LayoutUpdated-=Update;settle.Stop();};
    }
}


