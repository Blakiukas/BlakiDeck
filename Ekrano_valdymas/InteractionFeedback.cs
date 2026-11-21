using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Ekrano_valdymas;

public static class InteractionFeedback
{
    internal const int ControlsIdleMilliseconds = 3000;
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(InteractionFeedback), new PropertyMetadata(false, Changed));
    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);
    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);
    private static readonly DependencyProperty RestingTransformProperty = DependencyProperty.RegisterAttached("RestingTransform", typeof(Transform), typeof(InteractionFeedback));
    private static readonly DependencyProperty RestingOriginProperty = DependencyProperty.RegisterAttached("RestingOrigin", typeof(Point), typeof(InteractionFeedback));
    // Alignment references describe resting layout, not a neighbour's transient feedback.
    internal static Point RestingPoint(Visual element, Point point, Visual ancestor)
    {
        Visual current = element;
        while (!ReferenceEquals(current, ancestor))
        {
            if (VisualTreeHelper.GetParent(current) is not Visual parent) return element.TransformToAncestor(ancestor).Transform(point);
            if (current is UIElement ui && ui.GetValue(RestingTransformProperty) is Transform original && ui.RenderTransform.Inverse is GeneralTransform inverse)
            {
                var origin = new Vector(ui.RenderSize.Width * ui.RenderTransformOrigin.X, ui.RenderSize.Height * ui.RenderTransformOrigin.Y);
                var resting = (Point)ui.GetValue(RestingOriginProperty);
                var restingOrigin = new Vector(ui.RenderSize.Width * resting.X, ui.RenderSize.Height * resting.Y);
                point = inverse.Transform(original.Transform(point - restingOrigin) + restingOrigin - origin) + origin;
            }
            point = current.TransformToAncestor(parent).Transform(point);
            current = parent;
        }
        return point;
    }
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not Border border || !(bool)e.NewValue) return;
        UIElement? target = null; Transform? original = null; Point origin = default;
        bool hovering = false, pressed = false;
        ScaleTransform? scale = null;
        void Update()
        {
            if (target != null && (!ReferenceEquals(target, border.Child) || !hovering && !pressed))
            {
                target.RenderTransform = original!; target.RenderTransformOrigin = origin; target.ClearValue(RestingTransformProperty); target.ClearValue(RestingOriginProperty); target = null; scale = null;
            }
            if (!hovering && !pressed || border.Child == null) return;
            if (target == null)
            {
                target = border.Child; original = target.RenderTransform; origin = target.RenderTransformOrigin;
                target.SetValue(RestingTransformProperty, original); target.SetValue(RestingOriginProperty, origin);
                scale = new ScaleTransform();
                var transforms = new TransformGroup(); transforms.Children.Add(original); transforms.Children.Add(scale);
                target.RenderTransformOrigin = new Point(.5, .5); target.RenderTransform = transforms;
            }
            scale!.ScaleX = scale.ScaleY = pressed ? .94 : 1.06;
        }
        void Release() { pressed = false; Update(); }
        void Press() { pressed = true; Update(); }
        border.MouseEnter += (_, e) => { if (e.StylusDevice == null) { hovering = true; Update(); } };
        border.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((_, e) => { if (e.StylusDevice == null) Press(); }), true);
        border.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler((_, _) => Release()), true);
        border.AddHandler(UIElement.PreviewTouchDownEvent, new EventHandler<TouchEventArgs>((_, _) => Press()), true);
        border.AddHandler(UIElement.PreviewTouchUpEvent, new EventHandler<TouchEventArgs>((_, _) => Release()), true);
        border.LostMouseCapture += (_, _) => Release(); border.LostTouchCapture += (_, _) => Release();
        border.MouseLeave += (_, _) => { hovering = pressed = false; Update(); };
        border.Unloaded += (_, _) => { hovering = pressed = false; Update(); };
    }
}
