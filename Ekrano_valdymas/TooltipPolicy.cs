using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Ekrano_valdymas;

// WPF's tooltip safe-area can keep a popup open while the pointer moves slowly
// toward it across another menu row. Explicitly end ownership on mouse leave.
internal static class TooltipPolicy
{
    internal static int DelaySeconds {get;private set;}=3;
    private static string SettingsPath=>System.IO.Path.Combine(DiagnosticState.LocalApplicationData,"Ekrano_valdymas","tooltip-delay.json");
    internal static void SetDelay(int seconds){DelaySeconds=seconds is 2 or 3 or 5?seconds:0;AtomicJson.Write(SettingsPath,DelaySeconds);CloseCurrent();}
    private static bool initialized,applicationHooked;
    private static WeakReference<ToolTip>? current;
    private static WeakReference<FrameworkElement>? owner,pendingOwner;
    internal static void Initialize()
    {
        if(initialized){HookApplication();return;}initialized=true;
        try{if(System.IO.File.Exists(SettingsPath))DelaySeconds=System.Text.Json.JsonSerializer.Deserialize<int>(System.IO.File.ReadAllText(SettingsPath));}catch{}
        foreach(var type in new[]{typeof(FrameworkElement),typeof(FrameworkContentElement)})
        {
            ToolTipService.InitialShowDelayProperty.OverrideMetadata(type,new FrameworkPropertyMetadata(Math.Max(1,DelaySeconds)*1000));
            ToolTipService.BetweenShowDelayProperty.OverrideMetadata(type,new FrameworkPropertyMetadata(0));
            ToolTipService.ShowDurationProperty.OverrideMetadata(type,new FrameworkPropertyMetadata(30000));
        }
        EventManager.RegisterClassHandler(typeof(ToolTip),ToolTip.OpenedEvent,new RoutedEventHandler(Opened),true);
        EventManager.RegisterClassHandler(typeof(FrameworkElement),Mouse.MouseEnterEvent,new MouseEventHandler((sender,_)=>{if(sender is FrameworkElement target)ToolTipService.SetInitialShowDelay(target,Math.Max(1,DelaySeconds)*1000);}),true);
        EventManager.RegisterClassHandler(typeof(ToolTip),ToolTip.ClosedEvent,new RoutedEventHandler(Closed),true);
        EventManager.RegisterClassHandler(typeof(FrameworkElement),ToolTipService.ToolTipOpeningEvent,new ToolTipEventHandler((sender,e)=>
        {if(DelaySeconds==0){e.Handled=true;return;}if(ReferenceEquals(sender,e.Source)&&sender is FrameworkElement target&&target.ToolTip!=null)pendingOwner=new(target);}),true);
        EventManager.RegisterClassHandler(typeof(ContextMenu),ContextMenu.ClosedEvent,new RoutedEventHandler((_,_)=>CloseCurrent()),true);
        EventManager.RegisterClassHandler(typeof(MenuItem),MenuItem.SubmenuClosedEvent,new RoutedEventHandler((_,_)=>CloseCurrent()),true);
        EventManager.RegisterClassHandler(typeof(UIElement),Mouse.PreviewMouseDownEvent,new MouseButtonEventHandler((_,_)=>CloseCurrent()),true);
        HookApplication();
    }
    private static void HookApplication(){if(!applicationHooked&&Application.Current!=null){applicationHooked=true;Application.Current.Deactivated+=(_,_)=>CloseCurrent();}}
    private static void Opened(object sender,RoutedEventArgs e)
    {
        if(sender is not ToolTip tip)return;
        var available=DialogWorkArea.AvailableSize(tip.PlacementTarget as System.Windows.Media.Visual??tip);
        tip.MaxWidth=Math.Min(440,available.Width);
        tip.ApplyTemplate();
        Wrap(tip,Math.Max(1,tip.MaxWidth-tip.Padding.Left-tip.Padding.Right-2));
        if(current?.TryGetTarget(out var existing)==true&&ReferenceEquals(existing,tip))return;
        var target=tip.PlacementTarget as FrameworkElement;
        if(target==null&&pendingOwner?.TryGetTarget(out var pending)==true)target=pending;
        CloseCurrent();current=new(tip);
        if(target!=null){owner=new(target);target.MouseLeave+=OwnerLeft;target.Unloaded+=OwnerUnloaded;target.IsVisibleChanged+=OwnerVisibilityChanged;}
    }
    private static void OwnerLeft(object sender,MouseEventArgs e)=>CloseCurrent();
    private static void Wrap(DependencyObject node,double width)
    {
        if(node is TextBlock text){text.SetCurrentValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);text.MaxWidth=width;}
        for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);i++)Wrap(System.Windows.Media.VisualTreeHelper.GetChild(node,i),width);
    }
    private static void OwnerUnloaded(object sender,RoutedEventArgs e)=>CloseCurrent();
    private static void OwnerVisibilityChanged(object sender,DependencyPropertyChangedEventArgs e){if(e.NewValue is false)CloseCurrent();}
    private static void Closed(object sender,RoutedEventArgs e){if(current?.TryGetTarget(out var tip)==true&&ReferenceEquals(sender,tip))Release();}
    private static void Release()
    {
        if(owner?.TryGetTarget(out var target)==true){target.MouseLeave-=OwnerLeft;target.Unloaded-=OwnerUnloaded;target.IsVisibleChanged-=OwnerVisibilityChanged;}
        owner=null;current=null;pendingOwner=null;
    }
    private static void CloseCurrent(){ToolTip? tip=null;if(current!=null)current.TryGetTarget(out tip);Release();if(tip!=null)tip.IsOpen=false;}
    internal static void InvalidateOwner(FrameworkElement root)
    {
        if(owner?.TryGetTarget(out var target)!=true)return;
        for(DependencyObject? node=target;node!=null;node=node is System.Windows.Media.Visual?System.Windows.Media.VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node))
            if(ReferenceEquals(node,root)){CloseCurrent();return;}
    }
}
