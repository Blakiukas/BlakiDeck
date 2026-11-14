using System.Runtime.InteropServices;
namespace Ekrano_valdymas;
internal sealed partial class FullscreenBrowserWindow
{
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left,int top,int right,int bottom,int ellipseWidth,int ellipseHeight);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr window,IntPtr region,bool redraw);
    private (int Width,int Height,int Radius) outerCornerRegion;
    internal void SetOuterCorners(int width,int height,int radius)
    {
        var next=(width,height,radius);if(next==outerCornerRegion)return;
        var region=CreateRoundRectRgn(0,0,width+1,height+1,radius*2,radius*2);
        if(region==IntPtr.Zero)return;
        if(SetWindowRgn(GetNativeHandle(),region,true)==0){DeleteObject(region);return;}
        // Windows owns the region after a successful SetWindowRgn.
        outerCornerRegion=next;
    }
}
