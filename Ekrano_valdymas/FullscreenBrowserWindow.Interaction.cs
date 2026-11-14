using Microsoft.Web.WebView2.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Ekrano_valdymas;
internal sealed partial class FullscreenBrowserWindow
{
    private Action? pageActivity;
    private readonly HashSet<object> pointerHolders = new();
    private ContextMenu? webSettingsMenu;
    private bool pageInteractionActive = true;
    private System.Windows.Rect? dashboardInteractionBounds;
    private bool dashboardAtTop;
    internal static readonly TimeSpan InteractionTransitionDuration = TimeSpan.FromMilliseconds(650);
    internal static IEasingFunction CreateInteractionEasing() => new SineEase { EasingMode = EasingMode.EaseInOut };
    private Thickness? pageMarginTarget, toolbarMarginTarget;
    private bool autoHideBarsEnabled, hideTopBarEnabled;
    internal bool AutoHideBarsEnabled
    {
        get => autoHideBarsEnabled;
        set
        {
            autoHideBarsEnabled = value;
            if (value && hideTopBarEnabled)
            {
                hideTopBarEnabled = false;
                SaveWebZoomSettings();
            }
        }
    }
    internal bool HideTopBarEnabled
    {
        get => hideTopBarEnabled;
        set
        {
            hideTopBarEnabled = value;
            if (value && autoHideBarsEnabled)
            {
                autoHideBarsEnabled = false;
                AutoHideBarsChanged?.Invoke(false);
            }
        }
    }
    internal event Action<bool>? AutoHideBarsChanged;
    internal bool InteractionInProgress => pointerHolders.Count > 0 || toolbarMouseCaptured ||
        toolbarTouchDevice != null || webSettingsMenu?.IsOpen == true;

    internal void SetPageInteractionState(bool active, System.Windows.Rect? dashboardBounds, bool atTop)
    {
        bool interactionChanged = pageInteractionActive != active;
        pageInteractionActive = active;
        dashboardInteractionBounds = dashboardBounds;
        dashboardAtTop = atTop;
        SetAutoDashChromeHidden(!active && (AutoHideBarsEnabled || HideTopBarEnabled), InteractionTransitionDuration);
        UpdatePageInteractionBounds(interactionChanged);
    }

    private void UpdatePageInteractionBounds(bool animate = false)
    {
        if (!IsLoaded || ActualHeight <= 0) return;
        double top = 0, bottom = 0;
        if (dashboardInteractionBounds is System.Windows.Rect bounds)
        {
            if (dashboardAtTop) top = Math.Clamp(PointFromScreen(bounds.BottomLeft).Y, 0, ActualHeight);
            else bottom = Math.Clamp(ActualHeight - PointFromScreen(bounds.TopLeft).Y, 0, ActualHeight);
        }
        // Native WebView airspace must never overlap a visible WPF toolbar.
        // Web controls occupy the edge opposite the dashboard tabs.
        toolbarLayers.VerticalAlignment = dashboardAtTop ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        toolbarBar.VerticalAlignment = dashboardAtTop ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        CommonMetal.SetRestingOrigin(toolbarBar, new Point(0, dashboardAtTop ? ActualHeight - toolbarLayoutHeight : 0));
        bool dashboardHidden = !pageInteractionActive && AutoHideBarsEnabled;
        SetInteractionMargin(toolbarLayers, new Thickness(0), ref toolbarMarginTarget, animate);
        double toolbarSpace = autoDashChromeHidden ? 0 : toolbarLayoutHeight;
        double reservedTop = dashboardAtTop ? (dashboardHidden ? 0 : top) : toolbarSpace;
        double reservedBottom = dashboardAtTop ? toolbarSpace : (pageInteractionActive ? bottom : 0);
        var margin = new Thickness(0, Math.Min(ActualHeight - 1, reservedTop), 0,
            Math.Min(reservedBottom, Math.Max(0, ActualHeight - reservedTop - 1)));
        SetInteractionMargin(webView, margin, ref pageMarginTarget, animate);
    }

    private static void SetInteractionMargin(FrameworkElement element, Thickness target,
        ref Thickness? previousTarget, bool animate)
    {
        // Timer/geometry callbacks must not restart the in-flight transition.
        if (previousTarget == target) return;
        previousTarget = target;
        Thickness current = element.Margin;
        element.BeginAnimation(FrameworkElement.MarginProperty, null);
        element.Margin = animate ? current : target;
        if (animate && current != target)
            element.BeginAnimation(FrameworkElement.MarginProperty,
                new ThicknessAnimation(current, target, InteractionTransitionDuration)
                {
                    EasingFunction = CreateInteractionEasing(),
                    FillBehavior = FillBehavior.HoldEnd
                }, HandoffBehavior.SnapshotAndReplace);
    }

    private async Task InitializePageActivity(CoreWebView2 core)
    {
        core.WebMessageReceived += PageActivityMessage;
        core.FrameCreated += (_, e) => ObserveActivityFrame(e.Frame);
        core.NavigationStarting += (_, _) => { pointerHolders.Clear(); pageActivity?.Invoke(); };
        await core.AddScriptToExecuteOnDocumentCreatedAsync(PageActivityScript);
    }

    private void ObserveActivityFrame(CoreWebView2Frame frame)
    {
        frame.WebMessageReceived += PageActivityMessage;
        frame.FrameCreated += (_, e) => ObserveActivityFrame(e.Frame);
        frame.Destroyed += (_, _) => {pointerHolders.Remove(frame);ResourceFrameDestroyed(frame);};
    }

    private void PageActivityMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (browserClosed || sender == null) return;
        string message;
        try { message = e.TryGetWebMessageAsString(); } catch { return; }
        if(ResourceMediaMessage(sender,message))return;
        switch (message)
        {
            case "blakideck:input-start": pointerHolders.Add(sender); break;
            case "blakideck:input-end": pointerHolders.Remove(sender); break;
            case "blakideck:activity": break;
            default: return;
        }
        pageActivity?.Invoke();
    }

    internal const string PageActivityScript = """
        (() => {
          let last = -Infinity;
          const pointers = new Set();
          const send = value => { try { chrome.webview.postMessage('blakideck:' + value); } catch {} };
          const mediaChanged = () => send([...document.querySelectorAll('video,audio')].some(media => !media.paused && !media.ended) ? 'resource-media-start' : 'resource-media-stop');
          for (const type of ['play','pause','ended','emptied']) document.addEventListener(type, mediaChanged, true);
          const activity = event => {
            if (!event.isTrusted) return;
            const now = performance.now();
            if (now - last < 100) return;
            last = now; send('activity');
          };
          addEventListener('pointerdown', event => {
            if (!event.isTrusted) return;
            pointers.add(event.pointerId); send('input-start');
          }, {capture:true, passive:true});
          const release = event => {
            if (!event.isTrusted) return;
            if (!pointers.delete(event.pointerId)) return;
            if (!pointers.size) setTimeout(() => { if (!pointers.size) send('input-end'); }, 0);
          };
          for (const type of ['pointerup','pointercancel']) addEventListener(type, release, {capture:true, passive:true});
          // Real mouse movement is already polled in native screen coordinates.
          // Browser-generated pointermove after resize must not wake an idle page.
          for (const type of ['wheel','keydown','input','click']) addEventListener(type, activity, {capture:true, passive:true});
          addEventListener('blur', () => { if (pointers.size) { pointers.clear(); send('input-end'); } });
        })();
        """;
}

