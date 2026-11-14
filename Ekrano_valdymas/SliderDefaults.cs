using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Ekrano_valdymas;

public static class SliderDefaults
{
    private static bool initialized;
    internal static void InitializeWheel()
    {
        if(initialized)return;initialized=true;
        EventManager.RegisterClassHandler(typeof(Slider),System.Windows.Input.Mouse.PreviewMouseWheelEvent,
            new System.Windows.Input.MouseWheelEventHandler((sender,e)=>
            {
                var slider=(Slider)sender;
                if(!slider.IsEnabled)return; // The routed wheel event belongs to the slider under the pointer.
                double step=slider.IsSnapToTickEnabled&&slider.TickFrequency>0?slider.TickFrequency:slider.SmallChange;
                slider.SetCurrentValue(Slider.ValueProperty,Math.Clamp(slider.Value+Math.Sign(e.Delta)*step,slider.Minimum,slider.Maximum));
                e.Handled=true;
            }),handledEventsToo:true);
    }
    public static readonly DependencyProperty ValueProperty=DependencyProperty.RegisterAttached("Value",typeof(double),typeof(SliderDefaults),new FrameworkPropertyMetadata(double.NaN));
    private static readonly DependencyProperty OriginalToolTipProperty=DependencyProperty.RegisterAttached("OriginalToolTip",typeof(object),typeof(SliderDefaults));
    public static double GetValue(DependencyObject owner)=>(double)owner.GetValue(ValueProperty);
    public static void SetValue(DependencyObject owner,double value)=>owner.SetValue(ValueProperty,value);
    internal static void Mark(Slider slider,double value,string? label=null)
    {
        SetValue(slider,Math.Clamp(value,slider.Minimum,slider.Maximum));
        string caption=$"{label??"Default"}: {value:0.#}";
        if(slider.ReadLocalValue(OriginalToolTipProperty)==DependencyProperty.UnsetValue)slider.SetValue(OriginalToolTipProperty,slider.ToolTip);
        slider.ToolTip=slider.GetValue(OriginalToolTipProperty) is string previous&&!string.IsNullOrWhiteSpace(previous)?previous+" · "+caption:caption;
        System.Windows.Automation.AutomationProperties.SetHelpText(slider,caption);
    }
}

public sealed class SliderDefaultMarker:FrameworkElement
{
    public static readonly DependencyProperty ValueProperty=DependencyProperty.Register(nameof(Value),typeof(double),typeof(SliderDefaultMarker),new FrameworkPropertyMetadata(double.NaN,FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty OwnerProperty=DependencyProperty.Register(nameof(Owner),typeof(Slider),typeof(SliderDefaultMarker),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    public double Value {get=>(double)GetValue(ValueProperty);set=>SetValue(ValueProperty,value);}
    public Slider Owner {get=>(Slider)GetValue(OwnerProperty);set=>SetValue(OwnerProperty,value);}
    private Point renderedCenter=new(double.NaN,double.NaN);
    public SliderDefaultMarker()
    {
        IsHitTestVisible=false;Loaded+=(_,_)=>InvalidateVisual();
        // Track arranges its thumb after this overlay is arranged. Repaint only
        // after its actual reference position changes, including DPI/size changes.
        LayoutUpdated+=(_,_)=>{if(TryCenter(out var center)&&center!=renderedCenter)InvalidateVisual();};
    }
    internal Rect MarkerBounds {get;private set;}=Rect.Empty;
    private bool TryCenter(out Point center)
    {
        center=default;
        if(!double.IsFinite(Value)||Owner is not {} owner||owner.Maximum<=owner.Minimum||owner.Template?.FindName("PART_Track",owner) is not Track track||track.Thumb is not {} thumb||thumb.ActualWidth<=0||thumb.ActualHeight<=0)return false;
        // Use the real arranged thumb as the anchor and Track's own value/DIP
        // conversion. This includes its effective travel, direction and rounding;
        // no assumed thumb inset or duplicated RTL arithmetic is required.
        bool horizontal=track.Orientation==Orientation.Horizontal;
        double valuePerDip=horizontal?track.ValueFromDistance(1,0):track.ValueFromDistance(0,1);
        if(!double.IsFinite(valuePerDip)||valuePerDip==0)return false;
        var point=thumb.TranslatePoint(new Point(thumb.ActualWidth/2,thumb.ActualHeight/2),track);
        double delta=(Math.Clamp(Value,track.Minimum,track.Maximum)-track.Value)/valuePerDip;
        if(horizontal)point.X+=delta;else point.Y+=delta;
        center=track.TranslatePoint(point,this);return double.IsFinite(center.X)&&double.IsFinite(center.Y);
    }
    protected override void OnRender(DrawingContext dc)
    {
        MarkerBounds=Rect.Empty;renderedCenter=new(double.NaN,double.NaN);if(!TryCenter(out var center))return;
        renderedCenter=center;
        if(Owner.Orientation==Orientation.Vertical){
            MarkerBounds=new Rect(center.X-7,center.Y-1,14,2);
            dc.DrawLine(new Pen(Brushes.Black,4),new Point(center.X-7,center.Y),new Point(center.X+7,center.Y));
            dc.DrawLine(new Pen(Brushes.WhiteSmoke,2),new Point(center.X-7,center.Y),new Point(center.X+7,center.Y));return;
        }
        MarkerBounds=new Rect(center.X-1,center.Y-9,2,18);
        dc.DrawLine(new Pen(Brushes.Black,4),new Point(center.X,center.Y-9),new Point(center.X,center.Y+9));
        dc.DrawLine(new Pen(Brushes.WhiteSmoke,2),new Point(center.X,center.Y-9),new Point(center.X,center.Y+9));
    }
}
