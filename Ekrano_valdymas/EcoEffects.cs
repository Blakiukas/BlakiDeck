using System.Windows;
using System.Windows.Media.Effects;

namespace Ekrano_valdymas;

// Compatibility name for existing XAML. Effects now follow appearance only;
// there is no global mode binding or retired Eco toggle.
public sealed class EcoEffects : DependencyObject
{
    public static readonly DependencyProperty EffectProperty=DependencyProperty.RegisterAttached("Effect",typeof(Effect),typeof(EcoEffects),new PropertyMetadata(null,Changed));
    public static Effect? GetEffect(DependencyObject owner)=>(Effect?)owner.GetValue(EffectProperty);
    public static void SetEffect(DependencyObject owner,Effect? effect)=>owner.SetValue(EffectProperty,effect);
    private static void Changed(DependencyObject owner,DependencyPropertyChangedEventArgs args)
        =>owner.SetValue(UIElement.EffectProperty,args.NewValue);
}
