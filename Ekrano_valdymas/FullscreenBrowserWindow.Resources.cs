using Microsoft.Web.WebView2.Core;
using System.Windows;
using System.Windows.Threading;

namespace Ekrano_valdymas;

internal sealed partial class FullscreenBrowserWindow
{
    private ResourceMode resourceMode;
    private bool dashboardExpanded, resourcePolicyRunning, resourcePolicyPending;
    private bool resourceNavigationPending, captureMayBeActive;
    private readonly HashSet<CoreWebView2DownloadOperation> resourceDownloads = new();
    private readonly HashSet<object> resourceMediaFrames = new();
    private string appliedResourceStrategy = "Active";

    internal void SetResourceMode(ResourceMode mode)
    {
        resourceMode = mode;
        RequestResourcePolicy();
    }

    internal Task ClearWebCacheAsync() => WebCacheMaintenance.ClearDiskCacheAsync(this, webView.CoreWebView2);

    internal static string ChooseResourceStrategy(ResourceMode mode, bool background, bool playingMedia, bool capture, bool download, bool navigating)
        => mode == ResourceMode.Normal || !background ? "Active" : playingMedia || capture || download || navigating ? "Low" : "Suspend";

    private void InitializeResourcePolicy(CoreWebView2 core)
    {
        core.IsDocumentPlayingAudioChanged += (_, _) => RequestResourcePolicy();
        core.NavigationStarting += (_, _) =>
        {
            resourceNavigationPending = true;
            captureMayBeActive = false;
            resourceMediaFrames.Clear();
            RequestResourcePolicy();
        };
        core.NavigationCompleted += (_, _) => { resourceNavigationPending = false; RequestResourcePolicy(); };
        // Be conservative after a capture request. No permission decision is
        // changed; retain execution until the document navigates away.
        core.ScreenCaptureStarting += (_, _) => { captureMayBeActive = true; RequestResourcePolicy(); };
        core.PermissionRequested += (_, request) =>
        {
            if (request.PermissionKind is CoreWebView2PermissionKind.Camera or CoreWebView2PermissionKind.Microphone)
            { captureMayBeActive = true; RequestResourcePolicy(); }
        };
        core.DownloadStarting += (_, request) =>
        {
            var download = request.DownloadOperation;
            resourceDownloads.Add(download);
            download.StateChanged += ResourceDownloadStateChanged;
            RequestResourcePolicy();
        };
        RequestResourcePolicy();
    }

    private void ResourceDownloadStateChanged(object? sender, object e)
    {
        if (sender is CoreWebView2DownloadOperation download && download.State != CoreWebView2DownloadState.InProgress)
        {
            resourceDownloads.Remove(download);
            download.StateChanged -= ResourceDownloadStateChanged;
            RequestResourcePolicy();
        }
    }

    private void ResourceVisibilityChanged(object? sender, EventArgs e) => RequestResourcePolicy();

    private async void RequestResourcePolicy()
    {
        resourcePolicyPending = true;
        if (resourcePolicyRunning || browserClosed || webView.CoreWebView2 == null) return;
        resourcePolicyRunning = true;
        try
        {
            while (resourcePolicyPending && !browserClosed)
            {
                resourcePolicyPending = false;
                var core = webView.CoreWebView2;
                if (core == null) break;
                // Expanding the dashboard covers only part of the Web window.
                // A visible upper page must retain its display and execution.
                bool background = !IsVisible || WindowState == WindowState.Minimized;
                bool protectMedia = core.IsDocumentPlayingAudio || resourceMediaFrames.Count > 0;
                if (resourceMode != ResourceMode.Normal && background && !protectMedia && !core.IsSuspended && !resourceNavigationPending)
                {
                    // Include muted video/audio in the document and accessible
                    // child frames. WebView's own suspension eligibility remains
                    // the final guard for protected cross-origin/capture activity.
                    try { protectMedia = await core.ExecuteScriptAsync(PlayingMediaScript) == "true"; }
                    catch { protectMedia = true; }
                    if (browserClosed) break;
                    if (resourcePolicyPending) continue;
                }
                string strategy = ChooseResourceStrategy(resourceMode, background, protectMedia, captureMayBeActive, resourceDownloads.Count > 0, resourceNavigationPending);
                if (strategy == "Active")
                {
                    if (core.IsSuspended) core.Resume();
                    core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
                    webView.Visibility = Visibility.Visible;
                }
                else
                {
                    webView.Visibility = Visibility.Hidden;
                    if (strategy == "Low")
                    {
                        if (core.IsSuspended) core.Resume();
                        core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
                    }
                    else if (!core.IsSuspended)
                    {
                        // WPF must first propagate Visibility to the native
                        // controller. Never call Suspend on a visible WebView.
                        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                        if (browserClosed) break;
                        if (resourcePolicyPending) continue;
                        if (appliedResourceStrategy == "Low") core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
                        if (!await core.TrySuspendAsync())
                        {
                            strategy = "Low";
                            core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
                        }
                        if (browserClosed) break;
                    }
                }
                appliedResourceStrategy = strategy;
                RefreshPreviewCaptureState();
            }
        }
        catch (Exception ex)
        {
            if (!browserClosed) InteractionDiagnostics.RecordFailure("Web resource policy", ex);
        }
        finally { resourcePolicyRunning = false; }
    }

    private void CloseResourcePolicy()
    {
        foreach (var download in resourceDownloads) download.StateChanged -= ResourceDownloadStateChanged;
        resourceDownloads.Clear();
        resourceMediaFrames.Clear();
    }

    private bool ResourceMediaMessage(object sender,string message)
    {
        if(message=="blakideck:resource-media-start")resourceMediaFrames.Add(sender);
        else if(message=="blakideck:resource-media-stop")resourceMediaFrames.Remove(sender);
        else return false;
        RequestResourcePolicy();return true;
    }

    private void ResourceFrameDestroyed(object frame)
    {if(resourceMediaFrames.Remove(frame))RequestResourcePolicy();}

    private const string PlayingMediaScript = """
        (() => {
          function playing(doc) {
            if ([...doc.querySelectorAll('video,audio')].some(media => !media.paused && !media.ended)) return true;
            for (const frame of doc.querySelectorAll('iframe')) {
              try { if (frame.contentDocument && playing(frame.contentDocument)) return true; } catch {}
            }
            return false;
          }
          return playing(document);
        })()
        """;

    internal object ResourceStateForAudit() => new
    {
        mode = resourceMode.ToString(), dashboardExpanded, appliedResourceStrategy,
        visible = webView.Visibility.ToString(), suspended = webView.CoreWebView2?.IsSuspended,
        memoryTarget = webView.CoreWebView2?.MemoryUsageTargetLevel.ToString(), captureMayBeActive,
        navigating = resourceNavigationPending, downloads = resourceDownloads.Count,mediaFrames=resourceMediaFrames.Count
    };
}
