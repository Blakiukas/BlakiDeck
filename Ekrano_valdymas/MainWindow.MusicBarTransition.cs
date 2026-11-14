using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ekrano_valdymas;
public partial class MainWindow
{
    private int pendingMusicBarToggles;
    private bool musicBarTransitionRunning;
    private string musicBarTransitionPhase="idle";
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] private static extern int GetTransitionWindowStyle(IntPtr hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongW")] private static extern int SetTransitionWindowStyle(IntPtr hwnd,int index,int value);

    private async void QueueMusicBarToggle()
    {
        pendingMusicBarToggles++;
        if(musicBarTransitionRunning)return;
        musicBarTransitionRunning=true;
        try
        {
            while(pendingMusicBarToggles>0 && !applicationClosing)
            {
                pendingMusicBarToggles--;
                Window? cover=null;
                double previousOpacity=Opacity;
                try
                {
                    if(IsVisible && !isCompact && activeDashboardLayoutTab==0 && musicConsole?.IsVisible==true)
                    {
                        // Resizing/moving a layered HWND briefly moves its old backing
                        // image before WPF submits the new one. Hold only the unchanged
                        // console in a non-activating, click-through owned surface.
                        UpdateLayout();
                        var dpi=VisualTreeHelper.GetDpi(this);
                        var p=musicConsole.Surface.PointToScreen(new Point());
                        var end=musicConsole.Surface.PointToScreen(new Point(musicConsole.Surface.ActualWidth,musicConsole.Surface.ActualHeight));
                        // Include antialiased edge ink beyond rounded layout bounds.
                        // PointToScreen rounds the arranged edge; its final partly
                        // covered pixel must survive while the owner is transparent.
                        int left=(int)Math.Floor(p.X)-2,top=(int)Math.Floor(p.Y)-2;
                        int width=(int)Math.Ceiling(end.X)+2-left,height=(int)Math.Ceiling(end.Y)+2-top;
                        var snapshot=ConsoleDesktopSnapshot.Capture(left,top,width,height);
                        cover=new Window{Name="MusicBarTransitionCover",Owner=this,ShowActivated=false,ShowInTaskbar=false,Focusable=false,IsHitTestVisible=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=p.X/dpi.DpiScaleX,Top=p.Y/dpi.DpiScaleY,Opacity=0,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent,Width=width/dpi.DpiScaleX,Height=height/dpi.DpiScaleY,UseLayoutRounding=true,SnapsToDevicePixels=true,Content=new Image{Source=snapshot,Stretch=Stretch.Fill,SnapsToDevicePixels=true}};
                        cover.SourceInitialized+=(_,_)=>
                        {
                            IntPtr handle=new WindowInteropHelper(cover).Handle;
                            SetTransitionWindowStyle(handle,-20,(GetTransitionWindowStyle(handle,-20)|0x080000A0)&~0x00040000); // NOACTIVATE | TRANSPARENT | TOOLWINDOW, never APPWINDOW
                            SetWindowPos(handle,IntPtr.Zero,left,top,width,height,SwpNoActivate);
                        };
                        RenderOptions.SetBitmapScalingMode((Image)cover.Content,BitmapScalingMode.NearestNeighbor);
                        cover.Show();
                        SetWindowPos(new WindowInteropHelper(cover).Handle,IntPtr.Zero,left,top,width,height,SwpNoActivate);
                        // Show() may apply WPF startup placement after SourceInitialized.
                        // Do not expose that provisional position on another monitor.
                        cover.UpdateLayout();
                        cover.Opacity=1;
                        musicBarTransitionPhase="cover visible";
                        await AwaitMusicBarFrames();
                        // The cover protects the console, but cannot occlude old
                        // owner pixels that leak below it while the HWND moves.
                        // Submit a transparent backing before changing geometry.
                        Opacity=0;
                        musicBarTransitionPhase="owner transparent";
                        await AwaitMusicBarFrames();
                    }
                    if(applicationClosing)break;
                    musicTopBarHidden=!musicTopBarHidden;
                    UpdateMusicConsoleVisibility();
                    musicBarTransitionPhase="geometry changed";
                    if(cover!=null)
                    {
                        // Keep the unchanged console above the owner's new backing
                        // image until it has reached the desktop compositor.
                        await AwaitMusicBarFrames();
                        Opacity=previousOpacity;
                        musicBarTransitionPhase="owner restored";
                        await AwaitMusicBarFrames();
                    }
                    SaveWindowSettings();
                }
                finally{Opacity=previousOpacity;cover?.Close();musicBarTransitionPhase="idle";}
            }
        }
        catch(Exception error){InteractionDiagnostics.RecordFailure("Music bar transition",error);}
        finally{musicBarTransitionRunning=false;pendingMusicBarToggles=0;}
    }

    private static async Task AwaitMusicBarFrames()
    {
        var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int frames=0;
        EventHandler rendered=(_,_)=>{if(++frames>=2)completion.TrySetResult();};
        CompositionTarget.Rendering+=rendered;
        // Rendering can run at another monitor's 240 Hz while the destination
        // composes at 60 Hz. Two callbacks alone can finish before one present.
        try{await Task.WhenAll(Task.WhenAny(completion.Task,Task.Delay(200)),Task.Delay(50));DwmFlush();}
        finally{CompositionTarget.Rendering-=rendered;}
    }
}

