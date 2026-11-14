using System.Windows;
using System.Windows.Media;

namespace Ekrano_valdymas;
public partial class MainWindow
{
    private bool webPageIdle;

    private void UpdateWebPageLayout()
    {
        // In compact Web mode the 104-DIP rail meets the exterior edge of the
        // 124-DIP canvas. Keep the unused 20 DIP on its page-facing side.
        bool joined = activeBrowser != null && isCompact;
        bool atTop = compactDockEdge == DockEdge.Top;
        dashboardCanvas.SetValue(CommonMetal.FullWidthFrameProperty, joined);
        dashboardBackdrop.Visibility = joined ? Visibility.Collapsed : Visibility.Visible;
        var margin = joined ? new Thickness(0, atTop ? 0 : 20, 0, atTop ? 20 : 0) : new Thickness(10);
        if (dashboardContentFrame.Margin != margin) dashboardContentFrame.Margin = margin;
        quickLaunchBar.SetWebOuterCorners(joined,atTop);
        if (activeBrowser is not { } browser || browser.InteractionInProgress) return;
        if(GetWindowRect(browser.GetNativeHandle(),out NativeRect web))
            browser.SetOuterCorners(web.Right-web.Left,web.Bottom-web.Top,Math.Max(1,(int)Math.Round((web.Right-web.Left)*15d/1920)));
        Rect? tabs = null;
        // Expanding a top-docked dashboard must not remove the resting tab-bar
        // anchor and send the Web toolbar behind the tabs to the window origin.
        if ((isCompact || compactDockEdge == DockEdge.Top) && quickLaunchBar.IsVisible && quickLaunchBar.ActualWidth > 0)
        {
            Point start = quickLaunchBar.PointToScreen(new Point());
            Point end = quickLaunchBar.PointToScreen(new Point(quickLaunchBar.ActualWidth, quickLaunchBar.ActualHeight));
            // Hidden chrome is translated within the same native window. Reserve
            // its resting position, not its current animated screen coordinates.
            double shift = autoDashTranslate.Y * VisualTreeHelper.GetDpi(this).DpiScaleY;
            start.Y -= shift; end.Y -= shift;
            tabs = new Rect(start, end);
        }
        browser.SetPageInteractionState(!webPageIdle, tabs, compactDockEdge == DockEdge.Top);
    }

    private void SetWebBarsAutoHide(bool enabled)
    {
        autoDashHideEnabled = enabled;
        autoDashHideMenuItem.IsChecked = enabled;
        if (activeBrowser is { } browser) browser.AutoHideBarsEnabled = enabled;
        MarkLocalActivity();
        SaveWindowSettings();
    }
}
