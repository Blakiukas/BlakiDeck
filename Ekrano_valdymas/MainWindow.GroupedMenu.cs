using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Ekrano_valdymas;
public partial class MainWindow
{
    private void GroupedMenu_SubmenuOpened(object sender,RoutedEventArgs e)
    {
        if(sender is not MenuItem item || !ReferenceEquals(e.OriginalSource,item) || item.Template.FindName("PART_Popup",item) is not Popup popup)return;
        popup.PlacementTarget=item;
        popup.CustomPopupPlacementCallback=(size,target,offset)=>
        {
            // A queued popup resize can arrive after its parent menu closes.
            if(PresentationSource.FromVisual(item)==null)
                return new[]{new CustomPopupPlacement(new Point(target.Width,-7),PopupPrimaryAxis.Vertical)};
            var origin=item.PointToScreen(new Point());var dpi=VisualTreeHelper.GetDpi(item);
            var work=GetCurrentMonitorWorkArea();
            double left=work.Left-origin.X/dpi.DpiScaleX,right=work.Right-origin.X/dpi.DpiScaleX;
            double clamped=Math.Clamp(target.Width,left,Math.Max(left,right-size.Width));
            // Align the first child row with its parent, excluding popup chrome.
            double top=popup.Child is Border border?-border.Padding.Top*dpi.DpiScaleY:0;
            return new[]{new CustomPopupPlacement(new Point(target.Width,top),PopupPrimaryAxis.Vertical),new CustomPopupPlacement(new Point(-size.Width,top),PopupPrimaryAxis.Vertical),new CustomPopupPlacement(new Point(clamped,top),PopupPrimaryAxis.Vertical)};
        };
        popup.Placement=PlacementMode.Custom;
    }
}
