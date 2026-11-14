using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Ekrano_valdymas;

// Dialog coordinates are fitted in physical pixels; WPF dimensions remain DIPs.
internal static class DialogWorkArea
{
    internal static Size AvailableSize(Visual visual)
    {
        var hwnd=(PresentationSource.FromVisual(visual) as HwndSource)?.Handle??IntPtr.Zero;
        var info=new Monitor{Size=Marshal.SizeOf<Monitor>()};
        var dpi=VisualTreeHelper.GetDpi(visual);
        return GetMonitorInfo(MonitorFromWindow(hwnd,2),ref info)
            ?new Size(Math.Max(1,info.Work.Right-info.Work.Left-16)/dpi.DpiScaleX,Math.Max(1,info.Work.Bottom-info.Work.Top-16)/dpi.DpiScaleY)
            :SystemParameters.WorkArea.Size;
    }
    internal static void GrowToContent(Window window,double extra,bool allowShrink=false)
    {
        var hwnd=new WindowInteropHelper(window).Handle;var info=new Monitor{Size=Marshal.SizeOf<Monitor>()};
        if(!GetMonitorInfo(MonitorFromWindow(hwnd,2),ref info)||!GetWindowRect(hwnd,out var rect))return;
        var dpi=VisualTreeHelper.GetDpi(window);int height=Math.Min(info.Work.Bottom-info.Work.Top-16,(int)Math.Ceiling(Math.Min(window.MaxHeight,window.ActualHeight+extra)*dpi.DpiScaleY));
        if(!allowShrink&&height<=rect.Bottom-rect.Top)return;
        // Keep WPF's requested height in sync with the native resize. Attach's
        // monitor-fitting callback otherwise restores the pre-growth height.
        window.Height=height/dpi.DpiScaleY;
        int y=Math.Clamp(rect.Top,info.Work.Top+8,Math.Max(info.Work.Top+8,info.Work.Bottom-height-8));SetWindowPos(hwnd,IntPtr.Zero,rect.Left,y,rect.Right-rect.Left,height,0x14);
    }
    internal static void PlaceAtOwnerTop(Window window,Window? owner)
    {
        if(owner==null)return;
        var hwnd=new WindowInteropHelper(window).Handle;
        var info=new Monitor{Size=Marshal.SizeOf<Monitor>()};
        if(!GetMonitorInfo(MonitorFromWindow(new WindowInteropHelper(owner).Handle,2),ref info)||!GetWindowRect(new WindowInteropHelper(owner).Handle,out var anchor)||!GetWindowRect(hwnd,out var rect))return;
        int height=rect.Bottom-rect.Top,width=rect.Right-rect.Left;
        int x=Math.Clamp(anchor.Left+(anchor.Right-anchor.Left-width)/2,info.Work.Left+8,Math.Max(info.Work.Left+8,info.Work.Right-width-8));
        int y=Math.Clamp(anchor.Top-12,info.Work.Top+8,Math.Max(info.Work.Top+8,info.Work.Bottom-height-8));
        SetWindowPos(hwnd,IntPtr.Zero,x,y,width,height,0x14);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Monitor { public int Size; public Rect Bounds, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet=CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref Monitor info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

    internal static void Attach(Window window,double minimumWidth=400,double minimumHeight=230,bool deferDuringNativeMoveResize=false)
    {
        double desiredWidth=window.Width, desiredHeight=window.Height;
        bool fitting=false, nativeMoveResize=false;
        window.SizeChanged+=(_,_)=>{if(!fitting){desiredWidth=window.Width;desiredHeight=window.Height;}};
        void Fit()
        {
            if(fitting||!window.IsVisible||nativeMoveResize)return;
            var hwnd=new WindowInteropHelper(window).Handle;
            var info=new Monitor { Size=Marshal.SizeOf<Monitor>() };
            if(!GetMonitorInfo(MonitorFromWindow(hwnd,2),ref info)||!GetWindowRect(hwnd,out var rect))return;
            fitting=true;
            try
            {
                var dpi=VisualTreeHelper.GetDpi(window);
                int maxWidth=Math.Max(1,info.Work.Right-info.Work.Left-16),maxHeight=Math.Max(1,info.Work.Bottom-info.Work.Top-16);
                window.MinWidth=Math.Min(minimumWidth,maxWidth/dpi.DpiScaleX);window.MinHeight=Math.Min(minimumHeight,maxHeight/dpi.DpiScaleY);
                window.MaxWidth=maxWidth/dpi.DpiScaleX;window.MaxHeight=maxHeight/dpi.DpiScaleY;
                int width=Math.Min(maxWidth,(int)Math.Ceiling(desiredWidth*dpi.DpiScaleX));
                int height=Math.Min(maxHeight,(int)Math.Ceiling(desiredHeight*dpi.DpiScaleY));
                bool moving=System.Windows.Input.Mouse.LeftButton==System.Windows.Input.MouseButtonState.Pressed||window.TouchesCapturedWithin.Any();
                int x=moving?rect.Left:Math.Clamp(rect.Left,info.Work.Left+8,info.Work.Right-8-width);
                int y=moving?rect.Top:Math.Clamp(rect.Top,info.Work.Top+8,info.Work.Bottom-8-height);
                if(x!=rect.Left||y!=rect.Top||width!=rect.Right-rect.Left||height!=rect.Bottom-rect.Top)
                    SetWindowPos(hwnd,IntPtr.Zero,x,y,width,height,0x14);
            }
            finally { fitting=false; }
        }
        window.ContentRendered+=(_,_)=>Fit();
        window.SourceInitialized+=(_,_)=>HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook((IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)=>
        {
            if(deferDuringNativeMoveResize&&message==0x0231)nativeMoveResize=true;
            if(message==0x0232)
            {
                nativeMoveResize=false;
                window.Dispatcher.BeginInvoke(new Action(()=>
                {
                    if(deferDuringNativeMoveResize){desiredWidth=window.ActualWidth;desiredHeight=window.ActualHeight;}
                    Fit();
                }));
            }
            return IntPtr.Zero;
        });
        window.LocationChanged+=(_,_)=>Fit();
        window.LostTouchCapture+=(_,_)=>window.Dispatcher.BeginInvoke(new Action(Fit));
        window.DpiChanged+=(_,_)=>window.Dispatcher.BeginInvoke(new Action(Fit));
        if(window.IsVisible)window.Dispatcher.BeginInvoke(new Action(Fit));
    }
}
