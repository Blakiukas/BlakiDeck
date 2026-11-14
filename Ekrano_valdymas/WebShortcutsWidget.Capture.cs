using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text.Json.Serialization;
namespace Ekrano_valdymas;
public partial class WebShortcutsWidget
{
    internal event Action<WebShortcut>? CapturePreviewRequested;
    internal event Action? EntriesChanged;
    internal bool ContainsEntry(WebShortcut item)=>!item.IsAdd&&!item.IsMacro&&items.Contains(item);
    internal void SetCaptureState(WebShortcut item,bool ready,bool busy)=>item.UpdateCaptureState(ContainsEntry(item)&&ready,busy);
    internal async void SetCaptureResult(WebShortcut item,bool success)
    {
        int version=item.ShowCaptureResult(success);
        await Task.Delay(1500);
        item.ClearCaptureResult(version);
    }
    private void CapturePreview_Click(object sender,RoutedEventArgs e)
    {
        e.Handled=true;mouseDownItem=null;mouseDragging=false;
        if(sender is Button{DataContext:WebShortcut item}&&ContainsEntry(item)&&item.CanCapturePreview)CapturePreviewRequested?.Invoke(item);
    }
    private bool IsCaptureInput(DependencyObject? child)
    {
        while(child!=null&&child!=this){if(child is Button{Name:"capturePreviewButton"})return true;child=child is Visual?VisualTreeHelper.GetParent(child):LogicalTreeHelper.GetParent(child);}return false;
    }
    internal bool TrySetPreview(WebShortcut item,string path)
    {
        if(!ContainsEntry(item)||!OwnsPreview(path)||!File.Exists(path))return false;
        try{using var file=File.OpenRead(path);var image=BitmapDecoder.Create(file,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None);if(image is not PngBitmapDecoder||image.Frames.Count==0||image.Frames[0].PixelWidth<=0||image.Frames[0].PixelHeight<=0)return false;}catch{return false;}
        string old=item.PreviewPath;
        try{item.PreviewPath=path;Save(true);}
        catch(Exception error){item.PreviewPath=old;InteractionDiagnostics.RecordFailure("Web preview commit",error);return false;}
        if(!string.Equals(old,path,StringComparison.OrdinalIgnoreCase)&&OwnsPreview(old)&&!items.Any(other=>!ReferenceEquals(other,item)&&string.Equals(other.PreviewPath,old,StringComparison.OrdinalIgnoreCase)))try{File.Delete(old);}catch{}
        return true;
    }
    private bool OwnsPreview(string path)
    {
        try{string folder=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(settingsFile)!,"web-preview-cache"))+Path.DirectorySeparatorChar;return Path.GetFullPath(path).StartsWith(folder,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(path).StartsWith("preview-",StringComparison.OrdinalIgnoreCase)&&Path.GetExtension(path).Equals(".png",StringComparison.OrdinalIgnoreCase);}catch{return false;}
    }
    public sealed partial class WebShortcut
    {
        private bool captureReady,captureBusy;
        private int captureFeedback,captureFeedbackVersion;
        [JsonIgnore] public bool CanCapturePreview=>captureReady&&!captureBusy&&!IsAdd&&!IsMacro;
        [JsonIgnore] public bool CaptureBusy=>captureBusy;
        [JsonIgnore] public string CaptureGlyph=>captureFeedback==1?"\uE73E":"\uE722";
        [JsonIgnore] public string CaptureHint=>captureBusy?"Capturing the open page…":captureFeedback==1?"Thumbnail updated":captureFeedback==-1?"Capture failed — the previous thumbnail was kept.":captureReady?"Capture this open page now":"Open this link in BlakiDeck and wait for the page to finish loading before capturing.";
        private void NotifyCapture(){foreach(string property in new[]{nameof(CanCapturePreview),nameof(CaptureBusy),nameof(CaptureHint),nameof(CaptureGlyph)})PropertyChanged?.Invoke(this,new(property));}
        internal void UpdateCaptureState(bool ready,bool busy){captureReady=ready;captureBusy=busy;if(busy){captureFeedback=0;captureFeedbackVersion++;}NotifyCapture();}
        internal int ShowCaptureResult(bool success){captureFeedback=success?1:-1;NotifyCapture();return ++captureFeedbackVersion;}
        internal void ClearCaptureResult(int version){if(version!=captureFeedbackVersion)return;captureFeedback=0;NotifyCapture();}
    }
}
