using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ekrano_valdymas;

// An opaque, pixel-for-pixel copy avoids re-rasterizing text at a different DPI
// and blending the live console a second time through transparent screenshot ink.
internal static class ConsoleDesktopSnapshot
{
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window,IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc,int width,int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dst,int x,int y,int width,int height,IntPtr src,int sx,int sy,uint operation);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);

    internal static BitmapSource Capture(int left,int top,int width,int height)
    {
        IntPtr screen=GetDC(IntPtr.Zero),dc=CreateCompatibleDC(screen),bitmap=CreateCompatibleBitmap(screen,width,height);
        IntPtr previous=SelectObject(dc,bitmap);
        try
        {
            if(!BitBlt(dc,0,0,width,height,screen,left,top,0x40CC0020))
                throw new InvalidOperationException("Could not capture console desktop pixels.");
            var source=Imaging.CreateBitmapSourceFromHBitmap(bitmap,IntPtr.Zero,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());
            var opaque=new FormatConvertedBitmap(source,PixelFormats.Bgr32,null,0);opaque.Freeze();return opaque;
        }
        finally{SelectObject(dc,previous);DeleteObject(bitmap);DeleteDC(dc);ReleaseDC(IntPtr.Zero,screen);}
    }
}
