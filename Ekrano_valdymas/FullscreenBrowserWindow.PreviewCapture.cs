using Microsoft.Web.WebView2.Core;
using System.IO;
using System.Windows;

namespace Ekrano_valdymas;

internal sealed partial class FullscreenBrowserWindow
{
    private WebPreviewCaptureSession? previewCapture;
    private ulong previewNavigationId;
    private bool previewNavigationSucceeded;
    internal event Action? PreviewCaptureStateChanged;
    internal bool CanCapturePreview=>previewCapture?.CanCapture==true;
    internal bool IsPreviewCaptureBusy=>previewCapture?.IsBusy==true;

    internal void ConfigurePreviewCapture(Func<bool> entryStillValid,Func<string> allocatePath,
        Func<string,bool> commitPreview,Func<bool> hasExistingPreview)
    {
        previewCapture?.Dispose();
        previewCapture=new(entryStillValid,
            ()=>!browserClosed&&IsVisible&&WindowState!=WindowState.Minimized&&webView.IsVisible&&
                webView.ActualWidth>0&&webView.ActualHeight>0&&webView.CoreWebView2 is {IsSuspended:false},
            hasExistingPreview,allocatePath,
            stream=>webView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream),commitPreview);
        previewCapture.Changed+=()=>PreviewCaptureStateChanged?.Invoke();
        previewCapture.NavigationStarting(previewNavigationId);
        if(previewNavigationSucceeded)previewCapture.NavigationCompleted(previewNavigationId,true);
    }

    internal async Task<bool> CapturePreviewAsync()
    {
        if(previewCapture==null)return false;
        var session=previewCapture;bool result=await session.CaptureAsync();
        if(!result&&!browserClosed&&session.LastFailure is Exception failure)
            InteractionDiagnostics.RecordFailure("Web page thumbnail capture",failure);
        return result;
    }
    internal void InvalidatePreviewCapture()=>previewCapture?.Invalidate();
    internal void RefreshPreviewCaptureState()=>previewCapture?.RefreshState();
    private void PreviewNavigationStarting(object? sender,CoreWebView2NavigationStartingEventArgs args)
    {
        previewNavigationId=args.NavigationId;previewNavigationSucceeded=false;
        previewCapture?.NavigationStarting(args.NavigationId);
    }
    private void PreviewNavigationCompleted(CoreWebView2NavigationCompletedEventArgs args)
    {
        if(args.NavigationId!=previewNavigationId)return;
        previewNavigationSucceeded=args.IsSuccess;
        previewCapture?.NavigationCompleted(args.NavigationId,args.IsSuccess);
    }
    private void ClosePreviewCapture(){previewNavigationSucceeded=false;previewCapture?.Dispose();previewCapture=null;PreviewCaptureStateChanged?.Invoke();PreviewCaptureStateChanged=null;}
}
