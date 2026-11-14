using System.IO;
using System.Windows;

namespace Ekrano_valdymas;

public partial class MainWindow
{
    private WebShortcutsWidget? captureWidget;
    private WebShortcutsWidget.WebShortcut? captureItem;
    private FullscreenBrowserWindow? captureBrowser;
    private string? captureEntryUrl;

    private void InitializeWebPreviewWidget(WebShortcutsWidget widget)
    {
        widget.CapturePreviewRequested+=async item=>
        {
            if(ReferenceEquals(captureWidget,widget)&&ReferenceEquals(captureItem,item)&&
                captureBrowser is {CanCapturePreview:true} browser&&ReferenceEquals(activeBrowser,browser))
            {
                bool result=await browser.CapturePreviewAsync();
                if(ReferenceEquals(captureWidget,widget)&&ReferenceEquals(captureItem,item)&&ReferenceEquals(captureBrowser,browser)&&
                    widget.ContainsEntry(item)&&string.Equals(item.Url,captureEntryUrl,StringComparison.Ordinal))
                    widget.SetCaptureResult(item,result);
            }
        };
        widget.EntriesChanged+=()=>
        {
            if(!ReferenceEquals(captureWidget,widget)||captureItem==null||captureBrowser==null)return;
            if(!widget.ContainsEntry(captureItem)||!string.Equals(captureItem.Url,captureEntryUrl,StringComparison.Ordinal))captureBrowser.InvalidatePreviewCapture();
            captureBrowser.RefreshPreviewCaptureState();
        };
        widget.Loaded+=(_,_)=>{if(ReferenceEquals(captureWidget,widget))captureBrowser?.RefreshPreviewCaptureState();};
        widget.Unloaded+=(_,_)=>{if(ReferenceEquals(captureWidget,widget))captureBrowser?.RefreshPreviewCaptureState();};
    }

    private void ConnectWebPreview(WebShortcutsWidget widget,WebShortcutsWidget.WebShortcut item,FullscreenBrowserWindow browser)
    {
        if(captureWidget!=null&&captureItem!=null)captureWidget.SetCaptureState(captureItem,false,false);
        captureBrowser?.InvalidatePreviewCapture();
        captureWidget=widget;captureItem=item;captureBrowser=browser;
        string associatedUrl=item.Url;
        captureEntryUrl=associatedUrl;
        bool Associated()=>ReferenceEquals(activeBrowser,browser)&&ReferenceEquals(captureBrowser,browser)&&
            widget.IsLoaded&&widget.ContainsEntry(item)&&string.Equals(item.Url,associatedUrl,StringComparison.Ordinal);
        browser.ConfigurePreviewCapture(Associated,()=>widget.CreatePreviewPath(item),
            path=>Associated()&&widget.TrySetPreview(item,path),
            ()=>!string.IsNullOrWhiteSpace(item.PreviewPath)&&File.Exists(item.PreviewPath));
        void Refresh()=>widget.SetCaptureState(item,browser.CanCapturePreview,browser.IsPreviewCaptureBusy);
        browser.PreviewCaptureStateChanged+=Refresh;
        browser.IsVisibleChanged+=(_,_)=>browser.RefreshPreviewCaptureState();
        browser.StateChanged+=(_,_)=>browser.RefreshPreviewCaptureState();
        browser.Closed+=(_,_)=>
        {
            widget.SetCaptureState(item,false,false);
            if(ReferenceEquals(captureBrowser,browser)){captureBrowser=null;captureWidget=null;captureItem=null;captureEntryUrl=null;}
        };
        Refresh();
    }
}
