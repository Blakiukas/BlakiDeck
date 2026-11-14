using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace Ekrano_valdymas;

// A settings session keeps media input available without native ShowDialog disabling
// the entire dashboard. Owned child settings sessions suspend only their parent.
public class SettingsWindow:Window
{
    protected override void OnSourceInitialized(EventArgs e)
    {
        // Existing custom-chrome dialogs retain their own headers. Complete the
        // shared frame for every remaining native-chrome settings session.
        ShowInTaskbar=false;
        if(WindowStyle!=WindowStyle.None&&Content is UIElement){
            WindowStyle=WindowStyle.None;
            System.Windows.Shell.WindowChrome.SetWindowChrome(this,new(){CaptionHeight=0,ResizeBorderThickness=new Thickness(6),GlassFrameThickness=new Thickness(0),UseAeroCaptionButtons=false});
            AppearanceEditorUi.Frame(this,Math.Min(230,MinHeight>0?MinHeight:230),Math.Min(400,MinWidth>0?MinWidth:400),compact:false);
        }
        Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)]=MacroUi.Scroll(new Grid()).Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)];
        base.OnSourceInitialized(e);
    }
    private bool? result;
    internal ScrollViewer? AutoFitScroll {get;set;}
    internal void RequestContentFit()=>Dispatcher.BeginInvoke(new Action(FitContentWhenPossible),DispatcherPriority.Loaded);
    public SettingsWindow(){AddHandler(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent,new SelectionChangedEventHandler((sender,e)=>{if(e.OriginalSource is TabControl)RequestContentFit();}));AddHandler(Expander.CollapsedEvent,new RoutedEventHandler((_,_)=>{if(AutoFitScroll!=null)RequestContentFit();}));ContentRendered+=(_,_)=>Dispatcher.BeginInvoke(new Action(FitContentWhenPossible),DispatcherPriority.Loaded);AddHandler(Expander.ExpandedEvent,new RoutedEventHandler((_,_)=>Dispatcher.BeginInvoke(new Action(FitContentWhenPossible),DispatcherPriority.Loaded)));PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){e.Handled=true;Close();}};MouseLeftButtonDown+=(_,e)=>{
        if(e.Handled||WindowStyle!=WindowStyle.None||e.LeftButton!=MouseButtonState.Pressed)return;
        for(var node=e.OriginalSource as DependencyObject;node!=null&&node!=this;node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node))
            if(node is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.TextBoxBase or System.Windows.Controls.Primitives.Selector or Slider or ScrollViewer or TreeViewItem)return;
        e.Handled=true;DragMove();
    };}
    // Grow to the available work area before requiring a vertical scrollbar.
    // User resizing after opening remains independent.
    private void FitContentWhenPossible()
    {
        if(!IsLoaded||!IsVisible||SizeToContent!=SizeToContent.Manual)return;
        if(AutoFitScroll is {IsVisible:true,Content:FrameworkElement content} scroll)
        {
            UpdateLayout();
            double chrome=ActualHeight-scroll.ActualHeight;
            double wanted=Math.Clamp(content.DesiredSize.Height+chrome+2,MinHeight,MaxHeight);
            if(Math.Abs(wanted-ActualHeight)>1)DialogWorkArea.GrowToContent(this,wanted-ActualHeight,allowShrink:true);
            return;
        }
        IEnumerable<ScrollViewer> Find(DependencyObject node){if(node is ScrollViewer scroll)yield return scroll;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var child in Find(VisualTreeHelper.GetChild(node,i)))yield return child;}
        var extra=Find(this).Where(s=>s.IsVisible&&s.Content is FrameworkElement&&s.ScrollableHeight>1).Select(s=>s.ScrollableHeight).DefaultIfEmpty(0).Max();
        if(extra<=1)return;
        DialogWorkArea.GrowToContent(this,extra+4);
    }
    internal static readonly List<SettingsWindow> Sessions=new();
    private static ContextMenu? activeMenu;
    static SettingsWindow()
    {
        EventManager.RegisterClassHandler(typeof(ContextMenu),ContextMenu.OpenedEvent,new RoutedEventHandler((sender,e)=>
        {
            var menu=(ContextMenu)sender;
            menu.BeginAnimation(OpacityProperty,null);menu.Opacity=1;
            if(Sessions.Count>0&&Window.GetWindow(menu.PlacementTarget)!=Sessions[^1])menu.IsOpen=false;
            else activeMenu=menu;
        }));
    }
    public new bool? DialogResult {get=>result;set{result=value;Close();}}
    public new bool? ShowDialog()
    {
        if(Sessions.Count>0&&!ReferenceEquals(Owner,Sessions[^1])){Sessions[^1].Activate();return false;}
        ShowInTaskbar=false;
        if(Owner?.ContextMenu is ContextMenu menu)menu.IsOpen=false;
        var parent=Owner as SettingsWindow;
        var frame=new DispatcherFrame();
        void ClosedSession(object? sender,EventArgs e){frame.Continue=false;}
        Closed+=ClosedSession;Sessions.Add(this);
        if(parent!=null)parent.IsEnabled=false;
        try{Show();Dispatcher.PushFrame(frame);return result;}
        finally{Closed-=ClosedSession;Sessions.Remove(this);if(parent!=null){parent.IsEnabled=true;parent.Activate();}}
    }

    internal static void AttachDashboard(MainWindow dashboard)
    {
        var menu=dashboard.ContextMenu;
        // Popup placement can settle after IsSubmenuOpen, or change without it.
        var overlapTimer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(80)};
        overlapTimer.Tick+=(_,_)=>RefreshMenuLayer(menu);
        menu.Opened+=(_,_)=>overlapTimer.Start();
        menu.Closed+=(_,_)=>{overlapTimer.Stop();RefreshMenuLayer(menu);};
        dashboard.Closed+=(_,_)=>overlapTimer.Stop();
        System.ComponentModel.DependencyPropertyDescriptor.FromProperty(ContextMenu.IsOpenProperty,typeof(ContextMenu)).AddValueChanged(menu,(_,_)=>RefreshMenuLayer(menu));
        void Watch(ItemsControl parent)
        {
            foreach(var item in parent.Items.OfType<MenuItem>())
            {
                System.ComponentModel.DependencyPropertyDescriptor.FromProperty(MenuItem.IsSubmenuOpenProperty,typeof(MenuItem)).AddValueChanged(item,(_,_)=>RefreshMenuLayer(menu));
                Watch(item);
            }
        }
        Watch(menu);
        dashboard.PreviewMouseDown+=(_,e)=>
        {
            if(Sessions.Count>0&&!IsPlayer(e.OriginalSource as DependencyObject))e.Handled=true;
            if(activeMenu?.IsOpen==true){activeMenu.IsOpen=false;if(e.ChangedButton!=MouseButton.Left||!IsPlayer(e.OriginalSource as DependencyObject))e.Handled=true;}
        };
        dashboard.PreviewTouchDown+=(_,e)=>
        {
            if(Sessions.Count>0&&!IsPlayer(e.OriginalSource as DependencyObject))e.Handled=true;
            if(activeMenu?.IsOpen==true){activeMenu.IsOpen=false;if(!IsPlayer(e.OriginalSource as DependencyObject))e.Handled=true;}
        };
        dashboard.PreviewKeyDown+=(_,e)=>{if(Sessions.Count>0&&!IsPlayer(e.OriginalSource as DependencyObject))e.Handled=true;};
        dashboard.ContextMenuOpening+=(_,e)=>{if(Sessions.Count>0)e.Handled=true;};
    }
    private static bool IsPlayer(DependencyObject? element)
    {
        while(element!=null)
        {
            if(element is MusicConsoleControl or MusicPlayerCard or SpotifyWidget)return true;
            element=element is Visual?VisualTreeHelper.GetParent(element):LogicalTreeHelper.GetParent(element);
        }
        return false;
    }

    internal static void RefreshMenuLayer(ItemsControl container,bool active=true)
    {
        var layers=new List<(ItemsControl owner,FrameworkElement content,int depth)>();
        void Gather(ItemsControl owner,FrameworkElement? content,int depth,bool visible)
        {
            if(visible&&content?.IsVisible==true)layers.Add((owner,content,depth));
            foreach(var item in owner.Items.OfType<MenuItem>())
            {
                if(!visible&&item.Template?.FindName("itemBackground",item) is FrameworkElement row)row.Effect=null;
                var popup=item.Template?.FindName("PART_Popup",item) as System.Windows.Controls.Primitives.Popup;
                Gather(item,popup?.Child as FrameworkElement,depth+1,visible&&item.IsSubmenuOpen&&popup?.IsOpen==true);
            }
        }
        Gather(container,container,0,active&&(container is not ContextMenu context||context.IsOpen));
        Rect Bounds(FrameworkElement e)=>new(e.PointToScreen(new Point()),e.PointToScreen(new Point(e.ActualWidth,e.ActualHeight)));
        foreach(var layer in layers)
        {
            var bounds=MenuRowBounds(layer.owner);
            bool obscured=layers.Any(front=>front.depth>layer.depth&&HasAreaOverlap(bounds,Bounds(front.content)));
            foreach(var item in layer.owner.Items.OfType<MenuItem>())
                if(item.Template?.FindName("itemBackground",item) is FrameworkElement row)
                {
                    if(obscured){if(row.Effect is not BlurEffect {Radius:6})row.Effect=new BlurEffect{Radius=6};}
                    else row.Effect=null;
                }
        }
    }
    internal static bool HasAreaOverlap(Rect back,Rect front){back.Intersect(front);return !back.IsEmpty&&back.Width>1&&back.Height>1;}
    internal static Rect MenuRowBounds(ItemsControl owner)
    {
        Rect bounds=Rect.Empty;
        foreach(var item in owner.Items.OfType<MenuItem>())
            if(item.Template?.FindName("itemBackground",item) is FrameworkElement {IsVisible:true} row)
                bounds.Union(new Rect(row.PointToScreen(new Point()),row.PointToScreen(new Point(row.ActualWidth,row.ActualHeight))));
        return bounds;
    }
}
