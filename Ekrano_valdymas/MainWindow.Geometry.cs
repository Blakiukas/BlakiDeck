using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace Ekrano_valdymas;

public partial class MainWindow
{
    private const int MinimumDashboardWidthPixels = 500;

    private static NativeRect InitialBrowserBounds(NativeRect dashboard, NativeRect work, int[]? saved)
    {
        // A dashboard can straddle monitors or retain a larger physical width
        // after a DPI change. Fit dimensions before clamping its position.
        int workWidth = Math.Max(1, work.Right - work.Left);
        int workHeight = Math.Max(1, work.Bottom - work.Top);
        int width = Math.Clamp(dashboard.Right - dashboard.Left, 1, workWidth);
        int left = dashboard.Left, top = dashboard.Top;
        double height = Math.Max(dashboard.Bottom - dashboard.Top, Math.Round(width * 0.625));
        if (saved is { Length: 4 } && saved[2] > 0 && saved[3] > 0)
        {
            // Keep the dashboard and Web surface at the same physical width,
            // preserving the saved Web aspect ratio where the work area allows.
            height = Math.Round(saved[3] * (double)width / saved[2]);
            left = saved[2] == width ? saved[0] : dashboard.Left;
            top = saved[1];
        }
        int fittedHeight = (int)Math.Clamp(height, 1, workHeight);
        return PixelBounds(
            Math.Clamp(left, work.Left, work.Left + workWidth - width),
            Math.Clamp(top, work.Top, work.Top + workHeight - fittedHeight),
            width, fittedHeight);
    }

    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor,int kind,out uint x,out uint y);

    private void RestorePhysicalSettingsBounds(WindowSettings settings)
    {
        if (!IsValid(settings)) return;
        NativePoint anchor=new(){X=(int)Math.Round(settings.Left),Y=(int)Math.Round(settings.Top)};
        int[]? saved=settings.PhysicalBounds;
        if(saved is {Length:4}) anchor=new NativePoint{X=saved[0],Y=saved[1]};
        IntPtr monitor=MonitorFromPoint(anchor,MonitorDefaultToNearest);
        MonitorInfo info=new(){Size=Marshal.SizeOf<MonitorInfo>()};
        if(!GetMonitorInfo(monitor,ref info)) return;
        NativeRect area=info.WorkArea;
        NativeRect target;
        if(saved is {Length:4} && saved[2]>0 && saved[3]>0)
            target=PixelBounds(saved[0],saved[1],saved[2],saved[3]);
        else
        {
            GetDpiForMonitor(monitor,0,out uint dpi,out _);
            double scale=Math.Max(96,dpi)/96d;
            // Legacy system-aware coordinates retain the monitor's physical
            // origin; only their offset within it and dimensions were scaled.
            int x=area.Left+(int)Math.Round((settings.Left-area.Left)*scale);
            int y=area.Top+(int)Math.Round((settings.Top-area.Top)*scale);
            int width=(int)Math.Round(settings.Width*scale);
            int height=(int)Math.Round(width*(isCompact?CompactCanvasHeight:ExpandedDashboardCanvasHeight)/1920d);
            target=PixelBounds(x,y,width,height);
        }
        if(!isCompact && settings.MusicConsoleEnabled && quickLaunchBar.ActiveLayoutTab==0)
        {
            int consoleHeight=(int)Math.Round((target.Right-target.Left)*(FullDashboardCanvasHeight-(settings.MusicTopBarHidden?113d:0d))/1920);
            target=PixelBounds(target.Left,target.Bottom-consoleHeight,target.Right-target.Left,consoleHeight);
        }
        // Recover legacy bounds saved after shrinking below the usable size.
        if (target.Right-target.Left < MinimumDashboardWidthPixels)
        {
            int height=(int)Math.Round(MinimumDashboardWidthPixels*(isCompact?CompactCanvasHeight:ExpandedDashboardCanvasHeight)/1920d);
            target=PixelBounds(target.Left,target.Top,MinimumDashboardWidthPixels,height);
        }
        double fit=Math.Min(1d,Math.Min((double)(area.Right-area.Left)/(target.Right-target.Left),(double)(area.Bottom-area.Top)/(target.Bottom-target.Top)));
        int w=(int)Math.Round((target.Right-target.Left)*fit),h=(int)Math.Round((target.Bottom-target.Top)*fit);
        target=PixelBounds(Math.Clamp(target.Left,area.Left,area.Right-w),Math.Clamp(target.Top,area.Top,area.Bottom-h),w,h);
        ApplyGeometry(target);
    }
    private void ClampLinkedToWorkArea(ref NativeRect dashboard,ref NativeRect? browser)
    {
        MonitorInfo info=new(){Size=Marshal.SizeOf<MonitorInfo>()};
        if(!GetMonitorInfo(MonitorFromPoint(new NativePoint{X=dashboard.Left,Y=dashboard.Top},MonitorDefaultToNearest),ref info))return;
        NativeRect area=info.WorkArea,b=browser??dashboard;
        int left=Math.Min(dashboard.Left,b.Left),top=Math.Min(dashboard.Top,b.Top),right=Math.Max(dashboard.Right,b.Right),bottom=Math.Max(dashboard.Bottom,b.Bottom);
        double scale=Math.Min(1d,Math.Min((area.Right-area.Left)/(double)(right-left),(area.Bottom-area.Top)/(double)(bottom-top)));
        int x=Math.Clamp(left,area.Left,area.Right-(int)Math.Round((right-left)*scale));
        int y=Math.Clamp(top,area.Top,area.Bottom-(int)Math.Round((bottom-top)*scale));
        NativeRect Fit(NativeRect r)=>PixelBounds(x+(int)Math.Round((r.Left-left)*scale),y+(int)Math.Round((r.Top-top)*scale),(int)Math.Round((r.Right-r.Left)*scale),(int)Math.Round((r.Bottom-r.Top)*scale));
        dashboard=Fit(dashboard);if(browser.HasValue)browser=Fit(b);
    }
    private long geometryVersion;
    private NativeRect lastDashboardPixels;
    private bool hasLastDashboardPixels;
    private NativeRect? linkedResizeBrowserStart;

    private void ResizeLinkedBrowser(NativeRect dashboard)
    {
        if(activeBrowser==null||aligningLinkedWindows)return;
        if(linkedResizeBrowserStart==null&&GetWindowRect(activeBrowser.GetNativeHandle(),out NativeRect initial))linkedResizeBrowserStart=initial;
        if(linkedResizeBrowserStart is not NativeRect original)return;
        int width=dashboard.Right-dashboard.Left;
        int height=Math.Max(dashboard.Bottom-dashboard.Top,(int)Math.Round((original.Bottom-original.Top)*(double)width/(original.Right-original.Left)));
        int top=compactDockEdge==DockEdge.Top?dashboard.Top:dashboard.Bottom-height;
        aligningLinkedWindows=true;
        try{
            activeBrowser.ClearDashboardWorkArea();
            SetWindowPos(activeBrowser.GetNativeHandle(),IntPtr.Zero,dashboard.Left,top,width,height,SwpNoActivate);
            lastDashboardPixels=dashboard;hasLastDashboardPixels=true;
        }finally{aligningLinkedWindows=false;}
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(UpdateWebPageLayout));
    }

    private NativeRect CurrentWorkPixels()
    {
        MonitorInfo info = new() { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(windowHandle, MonitorDefaultToNearest), ref info))
            throw new InvalidOperationException("Monitor work area unavailable.");
        return info.WorkArea;
    }

    private static NativeRect PixelBounds(int x, int y, int width, int height) =>
        new() { Left=x, Top=y, Right=x+Math.Max(1,width), Bottom=y+Math.Max(1,height) };

    // Top-level placement is exclusively in physical screen pixels. WPF owns
    // its internal DIP layout and receives the ordinary per-monitor DPI event.
    private void ApplyGeometry(NativeRect dashboard, NativeRect? browser = null)
    {
        long version=++geometryVersion;
        FullscreenBrowserWindow? targetBrowser=activeBrowser;
        void Apply()
        {
            if(version!=geometryVersion || !ReferenceEquals(targetBrowser,activeBrowser)) return;
            bool previous=aligningLinkedWindows;
            aligningLinkedWindows=true;
            try
            {
                // A logical minimum must not enlarge a physical target on a
                // higher-DPI monitor. Interactive sizing enforces its own limits.
                MinWidth=1; MinHeight=1;
                if(browser is NativeRect b && targetBrowser!=null)
                {
                    targetBrowser.ClearDashboardWorkArea();
                    SetWindowPos(targetBrowser.GetNativeHandle(),IntPtr.Zero,b.Left,b.Top,b.Right-b.Left,b.Bottom-b.Top,SwpNoActivate);
                }
                // The idle DPI reconciliation normally finds the requested bounds
                // already applied. Avoid resubmitting an identical layered-window
                // resize (and changing z-order) after the completed frame.
                if(!GetWindowRect(windowHandle,out NativeRect existing) ||
                    existing.Left!=dashboard.Left || existing.Top!=dashboard.Top ||
                    existing.Right!=dashboard.Right || existing.Bottom!=dashboard.Bottom)
                    SetWindowPos(windowHandle,IntPtr.Zero,dashboard.Left,dashboard.Top,dashboard.Right-dashboard.Left,dashboard.Bottom-dashboard.Top,SwpNoActivate);
                GetWindowRect(windowHandle,out lastDashboardPixels);
                UpdateWebPageLayout();
                hasLastDashboardPixels=true;
                MaintainAutoDashHiddenOffset();
            }
            finally { aligningLinkedWindows=previous; }
        }
        Apply();
        // WPF may apply the WM_DPICHANGED suggested rectangle after SetWindowPos.
        // Only the latest transaction may reconcile its final physical target.
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(Apply));
    }

    private NativeRect ModeBoundsPixels(NativeRect? browserTarget = null)
    {
        GetWindowRect(windowHandle,out NativeRect current);
        NativeRect area=browserTarget ?? (activeBrowser!=null && GetWindowRect(activeBrowser.GetNativeHandle(),out NativeRect web)
            ? web : CurrentWorkPixels());
        int width=activeBrowser!=null ? area.Right-area.Left : current.Right-current.Left;
        double ratio=(isCompact?CompactCanvasHeight:ExpandedDashboardCanvasHeight)/1920d;
        width=Math.Min(width,Math.Min(area.Right-area.Left,(int)Math.Floor((area.Bottom-area.Top)/ratio)));
        int height=(int)Math.Round(width*ratio);
        int x=activeBrowser!=null?area.Left:Math.Clamp(current.Left,area.Left,area.Right-width);
        int y=activeBrowser!=null
            ? (compactDockEdge==DockEdge.Top?area.Top:area.Bottom-height)
            : Math.Clamp(compactDockEdge==DockEdge.Top?current.Top:current.Bottom-height,area.Top,area.Bottom-height);
        return PixelBounds(x,y,width,height);
    }

    private void FollowDashboardPixels()
    {
        if(windowHandle==IntPtr.Zero || !GetWindowRect(windowHandle,out NativeRect current)) return;
        if(!aligningLinkedWindows && hasLastDashboardPixels && activeBrowser!=null &&
            GetWindowRect(activeBrowser.GetNativeHandle(),out NativeRect web))
        {
            int dx=current.Left-lastDashboardPixels.Left,dy=current.Top-lastDashboardPixels.Top;
            SetWindowPos(activeBrowser.GetNativeHandle(),IntPtr.Zero,web.Left+dx,web.Top+dy,0,0,SwpNoSize|SwpNoActivate);
        }
        lastDashboardPixels=current; hasLastDashboardPixels=true;
    }
}
