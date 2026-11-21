using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;

namespace Ekrano_valdymas;

public partial class MainWindow
{
    private readonly TranslateTransform dashboardRevealTranslation = new();
    private RectangleGeometry? dashboardRevealClip;
    private EventHandler? dashboardRevealStart;
    private bool dashboardRevealRegion;
    private Window? dashboardRevealCover;

    private void PreserveCompactFrame()
    {
        var dpi=VisualTreeHelper.GetDpi(this);
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(ActualHeight*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);
        bitmap.Render(this);bitmap.Freeze();
        if(!GetWindowRect(new WindowInteropHelper(this).Handle,out NativeRect rect))return;
        dashboardRevealCover=new Window { Owner=this,ShowInTaskbar=false,ShowActivated=false,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent,IsHitTestVisible=false,Width=ActualWidth,Height=ActualHeight,Left=Left,Top=Top,Content=new Image{Source=bitmap,Stretch=Stretch.Fill} };
        dashboardRevealCover.Show();
        SetWindowPos(new WindowInteropHelper(dashboardRevealCover).Handle,IntPtr.Zero,rect.Left,rect.Top,rect.Right-rect.Left,rect.Bottom-rect.Top,SwpNoActivate);
    }
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left,int top,int right,int bottom);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr window,IntPtr region,bool redraw);
    [DllImport("gdi32.dll",EntryPoint="DeleteObject")] private static extern bool DeleteRevealRegion(IntPtr region);

    private void StopDashboardReveal()
    {
        if(dashboardRevealStart!=null)CompositionTarget.Rendering-=dashboardRevealStart;
        dashboardRevealStart=null;
        dashboardRevealCover?.Close();dashboardRevealCover=null;
        dashboardRevealTranslation.Y=0;
        if(ReferenceEquals(dashboardCanvas.Clip,dashboardRevealClip))dashboardCanvas.Clip=null;
        dashboardRevealClip=null;
        if(ReferenceEquals(dashboardCanvas.RenderTransform,dashboardRevealTranslation))
            dashboardCanvas.RenderTransform=Transform.Identity;
        if(dashboardRevealRegion)SetWindowRgn(new WindowInteropHelper(this).Handle,IntPtr.Zero,true);
        dashboardRevealRegion=false;
    }

    // Keep the native window's old compact surface from flashing at the top
    // during resize; reveal rigid content without resizing the Viewbox per frame.
    private void RevealDashboard(long version,Action? completed,NativeRect bounds)
    {
        double width=dashboardCanvas.Width,height=dashboardCanvas.Height;
        double rail=Math.Min(CompactCanvasHeight,height);
        if(width<=0||height<=rail){completed?.Invoke();return;}
        var clip=new RectangleGeometry(new Rect(0,0,width,rail));
        dashboardRevealClip=clip;
        dashboardCanvas.Clip=clip;
        dashboardCanvas.RenderTransform=dashboardRevealTranslation;
        double offset=compactDockEdge==DockEdge.Bottom?height-rail:0;
        int pixelWidth=bounds.Right-bounds.Left,pixelHeight=bounds.Bottom-bounds.Top;
        void Frame(double progress)
        {
            double exposed=rail+(height-rail)*progress;
            dashboardRevealTranslation.Y=offset*(1-progress);
            clip.Rect=new Rect(0,0,width,exposed);
            if(progress>0)return;
            int visible=(int)Math.Ceiling(pixelHeight*exposed/height);
            var region=CreateRectRgn(0,compactDockEdge==DockEdge.Bottom?pixelHeight-visible:0,pixelWidth,
                compactDockEdge==DockEdge.Bottom?pixelHeight:visible);
            if(SetWindowRgn(new WindowInteropHelper(this).Handle,region,true)==0)DeleteRevealRegion(region);
            else dashboardRevealRegion=true;
        }
        Frame(0);
        TimeSpan? previous=null;
        double elapsed=0;
        dashboardRevealStart=(_,args)=>
        {
            if(version!=compactTransitionVersion)return;
            var now=((RenderingEventArgs)args).RenderingTime;
            // Layout/skin preparation may block a frame. Do not skip half of
            // the visible transition when that happens.
            if(previous.HasValue)elapsed+=Math.Clamp((now-previous.Value).TotalMilliseconds,0,25);
            previous=now;
            if(elapsed>=25)
            {
                dashboardRevealCover?.Close();dashboardRevealCover=null;
                if(dashboardRevealRegion)SetWindowRgn(new WindowInteropHelper(this).Handle,IntPtr.Zero,true);
                dashboardRevealRegion=false;
            }
            double t=Math.Min(1,elapsed/240);
            double eased=t<.5?4*t*t*t:1-Math.Pow(-2*t+2,3)/2;
            Frame(eased);
            if(t>=1){StopDashboardReveal();UpdateWebPageLayout();completed?.Invoke();}
        };
        CompositionTarget.Rendering+=dashboardRevealStart;
    }
}
