using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ekrano_valdymas;

// Lists retain the same item containers and drag/drop handlers as the tile view.
internal sealed class ShortcutListPanel : Panel
{
    private ScrollViewer? viewport;
    public ShortcutListPanel()
    {
        Loaded+=(_,_)=>{viewport=FindAncestor<ScrollViewer>();if(viewport!=null)viewport.SizeChanged+=ViewportChanged;InvalidateMeasure();};
        Unloaded+=(_,_)=>{if(viewport!=null)viewport.SizeChanged-=ViewportChanged;viewport=null;};
    }
    private void ViewportChanged(object sender,SizeChangedEventArgs e)=>InvalidateMeasure();
    private int Columns => FindAncestor<ShortcutsWidget>() is { } shortcuts ? Math.Max(1,shortcuts.GridColumns/4) :
        Math.Clamp((int)Math.Round((FindAncestor<WebShortcutsWidget>()?.ActualWidth ?? 624)/624),1,3);
    private double RowHeight => Math.Max(48,(FindAncestor<ScrollViewer>()?.ActualHeight ?? 320)/6);
    private T? FindAncestor<T>() where T:DependencyObject
    { for(DependencyObject? p=VisualTreeHelper.GetParent(this);p!=null;p=VisualTreeHelper.GetParent(p))if(p is T value)return value;return null; }
    private int Limit=>FindAncestor<ShortcutsWidget>() is {} widget?widget.GridColumns*3:InternalChildren.Count;
    private IEnumerable<UIElement> Visible => InternalChildren.Cast<UIElement>().Take(Limit).Where(c=>(c as FrameworkElement)?.DataContext is not ShortcutsWidget.Slot {IsReserved:true});
    protected override Size MeasureOverride(Size available)
    {
        double width=double.IsFinite(available.Width)?available.Width:600;
        foreach(var child in InternalChildren.Cast<UIElement>())child.Measure(new Size(width/Columns,RowHeight));
        return new Size(width,Math.Ceiling(Visible.Count()/(double)Columns)*RowHeight);
    }
    protected override Size ArrangeOverride(Size size)
    {
        int index=0,slotIndex=0;foreach(var child in InternalChildren.Cast<UIElement>()) {
            if(slotIndex++>=Limit||(child as FrameworkElement)?.DataContext is ShortcutsWidget.Slot {IsReserved:true}){child.Arrange(new Rect());continue;}
            child.Arrange(new Rect((index%Columns)*size.Width/Columns,(index/Columns)*RowHeight,size.Width/Columns,RowHeight));index++;
        }return size;
    }
}

// Arrange against the final row height, including images whose natural size is
// larger than their slot. This avoids Grid's implicit clipping of oversized ink.
internal sealed class ListRowContent : Panel
{
    private double IconWidth(double height)
    {
        if(InternalChildren.Count==0)return 0;
        if(InternalChildren[0] is OpticallyCenteredText)return 92;
        return InternalChildren[0] is Image {Source:{ } image} && image.Height>0
            ? height*image.Width/image.Height : 68;
    }
    protected override Size MeasureOverride(Size available)
    {
        double height=double.IsFinite(available.Height)?available.Height:32;
        double width=double.IsFinite(available.Width)?available.Width:400;
        double icon=Math.Min(width/2,IconWidth(height));
        if(InternalChildren.Count>0)InternalChildren[0].Measure(new Size(icon,height));
        if(InternalChildren.Count>1){if(InternalChildren[1] is OpticallyCenteredText text){text.UseGlyphGeometry=true;text.CenterCapToBaseline=true;}InternalChildren[1].Measure(new Size(Math.Max(0,width-icon-12),height));}
        return new Size(width,height);
    }
    protected override Size ArrangeOverride(Size size)
    {
        double icon=Math.Min(size.Width/2,IconWidth(size.Height));
        if(InternalChildren.Count>0)InternalChildren[0].Arrange(new Rect(0,0,icon,size.Height));
        if(InternalChildren.Count>1)InternalChildren[1].Arrange(new Rect(icon+12,0,Math.Max(0,size.Width-icon-12),size.Height));
        return size;
    }
}
