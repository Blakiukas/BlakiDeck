using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ekrano_valdymas
{
    internal sealed class ShortcutTransfer
    {
        public ShortcutTransfer(
            string target,
            string displayName,
            Action<string?, string?> completeMove)
        {
            Target = target;
            DisplayName = displayName;
            CompleteMove = completeMove;
        }

        public string? SourceLocation {get;init;}
        internal object? SourceOwner {get;init;}
        internal Size MinimumTargetSize {get;set;}
        internal bool FitsTarget(FrameworkElement owner,object slot)
        {
            if(MinimumTargetSize.Width<=0&&MinimumTargetSize.Height<=0)return true;
            bool Visit(DependencyObject node)
            {
                if(node is Border b&&ReferenceEquals(b.DataContext,slot)&&b.ActualWidth>0&&Window.GetWindow(b) is {} window)
                {
                    var size=b.TransformToAncestor(window).TransformBounds(new Rect(b.RenderSize)).Size;
                    return size.Width+1>=MinimumTargetSize.Width&&size.Height+1>=MinimumTargetSize.Height;
                }
                for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)if(Visit(VisualTreeHelper.GetChild(node,i)))return true;
                return false;
            }
            return Visit(owner);
        }
        internal object? SourceSlot {get;init;}
        internal Point? SourceGridOffset {get;init;}
        internal Point? SourceGrab {get;set;}
        internal Func<bool>? SourceIsCurrent {get;init;}
        internal Func<string?,bool>? AcceptReplacement {get;init;}
        internal bool IsCompleted {get;private set;}
        internal bool CanMoveTo(string? replacement)=>!IsCompleted&&(SourceIsCurrent?.Invoke()??true)&&(AcceptReplacement?.Invoke(replacement)??true);
        internal void AcceptWithoutReplacement()=>IsCompleted=true;
        internal void ExchangeSensorPreferences(string location,string? target)
        {
            var appearance=SensorCellPreferences.Capture(SourceLocation,Target);
            if(SourceSlot is SystemMonitorWidget.MonitorSlot {LargeGraph:true,IsShortcut:false}&&appearance!=null)appearance=appearance with{Mode="Graph"};
            SensorCellPreferences.Exchange(SourceLocation,Target,location,target,appearance);
        }
        public string Target { get; }
        public string DisplayName { get; }
        public Action<string?, string?> CompleteMove { get; }
        public string? Presentation { get; }
        public string? IconPath {get;}
        private readonly Action<string?,string?,string?,string?>? completeIconMove;
        public ShortcutTransfer(string target,string name,string? presentation,string? iconPath,Action<string?,string?,string?,string?> complete):this(target,name,presentation,(t,n,p)=>complete(t,n,p,null)){IconPath=iconPath;completeIconMove=complete;}

        private readonly Action<string?,string?,string?>? completeAppearanceMove;
        public ShortcutTransfer(string target,string displayName,string? presentation,Action<string?,string?,string?> completeMove)
            :this(target,displayName,(target,name)=>completeMove(target,name,null))
        { Presentation=presentation;completeAppearanceMove=completeMove; }
        public void CompleteWithPresentation(string? target,string? name,string? presentation,string? iconPath=null)
        {
            if(IsCompleted)return;
            IsCompleted=true;
            if(completeIconMove!=null)completeIconMove(target,name,presentation,iconPath);
            else if(completeAppearanceMove!=null)completeAppearanceMove(target,name,presentation);
            else CompleteMove(target,name);
        }
    }

    internal static class ShortcutDragSupport
    {
        internal const string Format = "BlakiDeck.ShortcutTransfer";
        private static readonly System.Collections.Generic.Dictionary<string,ShortcutTransfer> ActiveTransfers=new();
        internal static ShortcutTransfer? ResolveTransfer(IDataObject data)
        {
            try
            {
                if(!data.GetDataPresent(Format))return null;
                object? value=data.GetData(Format);
                return value is ShortcutTransfer direct?direct:value is string token&&ActiveTransfers.TryGetValue(token,out var transfer)?transfer:null;
            }
            catch{return null;}
        }

        private static QuickLaunchBar? TabBar(Window? owner)=>owner?.FindName("quickLaunchBar") as QuickLaunchBar;
        private static Brush SnapshotBrush(FrameworkElement source)
        {
            // The source's tab can unload during the drag. Keep one temporary
            // raster instead of a VisualBrush which follows the detached tree.
            var dpi=VisualTreeHelper.GetDpi(source);double width=Math.Max(1,source.ActualWidth),height=Math.Max(1,source.ActualHeight);
            var drawing=new DrawingVisual();using(var dc=drawing.RenderOpen())dc.DrawRectangle(new VisualBrush(source){Stretch=Stretch.Fill},null,new Rect(0,0,width,height));
            var image=new RenderTargetBitmap((int)Math.Ceiling(width*dpi.DpiScaleX),(int)Math.Ceiling(height*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);
            image.Render(drawing);image.Freeze();var brush=new ImageBrush(image){Stretch=Stretch.Fill};brush.Freeze();return brush;
        }

        internal static bool DropAt(Window owner,Point point,ShortcutTransfer transfer)
        {
            for(DependencyObject? hit=owner.InputHitTest(point) as DependencyObject;hit!=null;hit=hit is Visual?VisualTreeHelper.GetParent(hit):LogicalTreeHelper.GetParent(hit))
            {
                if(hit is Border {DataContext:QuickLaunchBar.QuickLaunchSlot quick})
                    return TabBar(owner)?.AcceptShortcutTransfer(quick,transfer)==true;
                if(hit is Border {DataContext:ShortcutsWidget.Slot slot})
                {
                    for(DependencyObject? p=hit;p!=null;p=VisualTreeHelper.GetParent(p))
                        if(p is ShortcutsWidget widget)return widget.AcceptShortcutTransfer(slot,transfer);
                }
                if(hit is Border {DataContext:WebShortcutsWidget.WebShortcut web})
                {
                    for(DependencyObject? p=hit;p!=null;p=VisualTreeHelper.GetParent(p))
                        if(p is WebShortcutsWidget widget)return widget.AcceptShortcutTransfer(transfer,web);
                }
                if(hit is Border {DataContext:SystemMonitorWidget.MonitorSlot monitorSlot})
                {
                    for(DependencyObject? p=hit;p!=null;p=VisualTreeHelper.GetParent(p))
                        if(p is SystemMonitorWidget monitor)return ReferenceEquals(transfer.SourceOwner,monitor)?monitor.AcceptFreeShortcutFromWindow(owner,point,transfer):monitor.AcceptShortcutTransfer(monitorSlot,transfer);
                }
                if(hit is WebShortcutsWidget list)return list.AcceptShortcutTransfer(transfer,null);
                if(hit is SystemMonitorWidget monitorArea)return monitorArea.AcceptFreeShortcutFromWindow(owner,point,transfer);
            }
            return false;
        }

        private static TouchDragSession? touchSession;
        internal static bool BeginTouchDrag(FrameworkElement source,TouchDevice device,Point point,ShortcutTransfer transfer,Action? completed=null)
        {
            var owner=Window.GetWindow(source);if(owner==null||!transfer.CanMoveTo(null))return false;
            touchSession?.Close();
            try{touchSession=new TouchDragSession(owner,source,device,point,transfer,completed);return true;}
            catch(Exception ex){InteractionDiagnostics.RecordFailure("Shortcut touch drag",ex);return false;}
        }
        private sealed class TouchDragSession
        {
            private readonly Window owner;private readonly TouchDevice device;private readonly ShortcutTransfer transfer;private readonly Action? completed;
            private readonly Popup preview;private readonly QuickLaunchBar? bar;private bool closed;
            internal TouchDragSession(Window owner,FrameworkElement source,TouchDevice device,Point point,ShortcutTransfer transfer,Action? completed)
            {
                this.owner=owner;this.device=device;this.transfer=transfer;this.completed=completed;bar=TabBar(owner);
                var bounds=source.TransformToAncestor(owner).TransformBounds(new Rect(source.RenderSize));
                preview=new Popup{PlacementTarget=owner,Placement=PlacementMode.Relative,AllowsTransparency=true,IsHitTestVisible=false,StaysOpen=true,
                    Tag=PreviewGrab(source,owner,transfer.SourceGrab,point),Child=new Border{Width=bounds.Width,Height=bounds.Height,Background=SnapshotBrush(source),Opacity=.9,IsHitTestVisible=false}};
                if(!device.Capture(owner))throw new InvalidOperationException("The dashboard could not capture the shortcut touch gesture.");
                owner.AddHandler(UIElement.PreviewTouchMoveEvent,new EventHandler<TouchEventArgs>(Move),true);
                owner.AddHandler(UIElement.PreviewTouchUpEvent,new EventHandler<TouchEventArgs>(Up),true);
                owner.LostTouchCapture+=Lost;owner.PreviewKeyDown+=Key;
                MoveTo(point);preview.IsOpen=true;
            }
            private void MoveTo(Point point){MoveAnchoredPreview(preview,point);bar?.UpdateShortcutTabHover(transfer,bar.ShortcutTabAt(owner.TranslatePoint(point,bar)));}
            private void Move(object? sender,TouchEventArgs e){if(e.TouchDevice!=device||closed)return;MoveTo(e.GetTouchPoint(owner).Position);e.Handled=true;}
            private void Up(object? sender,TouchEventArgs e){if(e.TouchDevice!=device||closed)return;var point=e.GetTouchPoint(owner).Position;try{DropAt(owner,point,transfer);}finally{Close();}e.Handled=true;}
            private void Lost(object? sender,TouchEventArgs e){if(e.TouchDevice==device)Close();}
            private void Key(object sender,KeyEventArgs e){if(e.Key==System.Windows.Input.Key.Escape){e.Handled=true;Close();}}
            internal void Close()
            {
                if(closed)return;closed=true;bar?.CancelShortcutTabHover();preview.IsOpen=false;preview.Child=null;
                owner.RemoveHandler(UIElement.PreviewTouchMoveEvent,new EventHandler<TouchEventArgs>(Move));owner.RemoveHandler(UIElement.PreviewTouchUpEvent,new EventHandler<TouchEventArgs>(Up));
                owner.LostTouchCapture-=Lost;owner.PreviewKeyDown-=Key;if(device.Captured==owner)device.Capture(null);
                if(ReferenceEquals(touchSession,this))touchSession=null;completed?.Invoke();
            }
        }

        internal static Popup CreateAnchoredPreview(FrameworkElement source,Point grabInOwner)
        {
            var owner=Window.GetWindow(source);
            var bounds=source.TransformToAncestor(owner).TransformBounds(new Rect(0,0,source.ActualWidth,source.ActualHeight));
            return new Popup{PlacementTarget=owner,Placement=PlacementMode.Relative,AllowsTransparency=true,IsHitTestVisible=false,StaysOpen=true,
                Tag=grabInOwner-new Vector(bounds.X,bounds.Y),Child=new Border{Width=bounds.Width,Height=bounds.Height,Background=SnapshotBrush(source),Opacity=.9}};
        }
        internal static void MoveAnchoredPreview(Popup preview,Point pointInOwner)
        {
            var grab=(Point)preview.Tag;preview.HorizontalOffset=pointInOwner.X-grab.X;preview.VerticalOffset=pointInOwner.Y-grab.Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CursorPoint { public int X; public int Y; }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out CursorPoint point);

        public static DragDropEffects BeginDrag(
            FrameworkElement source,
            ImageSource? icon,
            string displayName,
            ShortcutTransfer transfer)
        {
            DataObject data = new();
            data.SetData(Format, transfer);
            data.SetData(DataFormats.Text, transfer.Target);
            return BeginVisualDrag(source, data);
        }

        private sealed class DragInk : System.Windows.Documents.Adorner
        {
            private readonly Brush brush;
            private readonly Size size;
            internal Point Position;
            internal DragInk(UIElement root,FrameworkElement source,Size size):base(root)
            { this.size=size;brush=SnapshotBrush(source);IsHitTestVisible=false;Opacity=.96; }
            protected override void OnRender(DrawingContext drawing)
            { drawing.DrawRoundedRectangle(brush,new Pen(Brushes.LimeGreen,2),new Rect(Position,size),11,11); }
        }

        internal static Point PreviewGrab(FrameworkElement source,Visual root,Point? originalGrab,Point pointer)
        {
            var transform=source.TransformToAncestor(root);var origin=transform.Transform(new Point());
            var point=originalGrab is {} grab?transform.Transform(grab):pointer;
            return new Point(point.X-origin.X,point.Y-origin.Y);
        }
        public static DragDropEffects BeginVisualDrag(FrameworkElement source, IDataObject data)
        {
            string? token=null;
            Point? originalGrab=null;
            if(data.GetDataPresent(Format)&&data.GetData(Format) is ShortcutTransfer transfer)
            {
                // OLE transports a string; the source callback stays in this process.
                originalGrab=transfer.SourceGrab;token=Guid.NewGuid().ToString("N");ActiveTransfers.Add(token,transfer);data.SetData(Format,token);
            }
            var owner=Window.GetWindow(source);
            var tabBar=TabBar(owner);
            var root=owner?.Content as UIElement;
            // The preview uses root coordinates and must live above the entire
            // content, not inside a scroll viewer's clipped adorner layer.
            var layer=root==null?null:System.Windows.Documents.AdornerLayer.GetAdornerLayer(root);
            DragInk? preview=null;
            Point grab=new();
            if(root!=null&&layer!=null)
            {
                var bounds=source.TransformToAncestor(root).TransformBounds(new Rect(0,0,source.ActualWidth,source.ActualHeight));
                grab=PreviewGrab(source,root,originalGrab,Mouse.GetPosition(root));
                preview=new DragInk(root,source,bounds.Size){Position=bounds.TopLeft};
                layer.Add(preview);
            }
            // OLE owns pointer routing during the drag. The preview is an in-window,
            // non-interactive adorner, so no popup HWND can intercept its drop target.
            if(source.IsMouseCaptured)source.ReleaseMouseCapture();
            void Position()
            {
                if(preview==null||root==null||!GetCursorPos(out CursorPoint cursor))return;
                Point point=root.PointFromScreen(new Point(cursor.X,cursor.Y));
                preview.Position=point-new Vector(grab.X,grab.Y);preview.InvalidateVisual();
            }
            GiveFeedbackEventHandler feedback=(_,_)=>Position();
            source.GiveFeedback+=feedback;
            Position();
            InteractionDiagnostics.Log($"ShortcutDrag begin active={owner?.IsActive} preview={preview!=null}");
            try
            {
                var result=DragDrop.DoDragDrop(source,data,DragDropEffects.Move);
                InteractionDiagnostics.Log($"ShortcutDrag end effect={result}");return result;
            }
            finally
            {
                source.GiveFeedback-=feedback;
                tabBar?.CancelShortcutTabHover();
                if(token!=null)ActiveTransfers.Remove(token);
                if(preview!=null)layer!.Remove(preview);
                if(source.IsMouseCaptured)source.ReleaseMouseCapture();
            }
        }
        public static DragDropEffects BeginMetricDrag(
            FrameworkElement source,
            string label,
            string value,
            DataObject data)
        {
            StackPanel content = new() { Margin = new Thickness(14, 9, 14, 10) };
            content.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromArgb(205, 255, 255, 255)),
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            content.Children.Add(new TextBlock
            {
                Text = value,
                Foreground = Brushes.White,
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            });
            Popup preview = new()
            {
                Child = new Border
                {
                    Child = content,
                    MinWidth = 135,
                    Background = new SolidColorBrush(Color.FromArgb(232, 36, 36, 36)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(11),
                    Opacity = 0.92,
                    IsHitTestVisible = false
                },
                AllowsTransparency = true,
                Placement = PlacementMode.AbsolutePoint,
                IsHitTestVisible = false,
                StaysOpen = true
            };
            GiveFeedbackEventHandler feedback = (_, _) => PositionPreview(source, preview);
            source.GiveFeedback += feedback;
            PositionPreview(source, preview);
            preview.IsOpen = true;
            try { return DragDrop.DoDragDrop(source, data, DragDropEffects.Move); }
            finally { source.GiveFeedback -= feedback; preview.IsOpen = false; }
        }

        private static void PositionPreview(Visual source, Popup preview)
        {
            if (!GetCursorPos(out CursorPoint cursor)) return;
            var owner=Window.GetWindow(source);if(owner==null)return;
            Point point=owner.PointFromScreen(new Point(cursor.X,cursor.Y));
            preview.PlacementTarget=owner;preview.Placement=PlacementMode.Relative;
            var grab=preview.Child is FrameworkElement {Tag:Point anchor}?anchor:new Point(-16,-16);
            preview.HorizontalOffset=point.X-grab.X;
            preview.VerticalOffset=point.Y-grab.Y;
        }
    }
}


