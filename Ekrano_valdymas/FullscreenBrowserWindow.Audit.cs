using System.Windows;
namespace Ekrano_valdymas;
internal sealed partial class FullscreenBrowserWindow
{
    internal void OpenZoomForAudit(FrameworkElement anchor)=>OpenWebZoomSettings(anchor);
    internal object ZoomStateForAudit()=>new{webZoomEnabled,webZoomAllSites,webZoomFactor,siteFactors=webSiteZoomFactors,menuOpen=webSettingsMenu?.IsOpen};
}
