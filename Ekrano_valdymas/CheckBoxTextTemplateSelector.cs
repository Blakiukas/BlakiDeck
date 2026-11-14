using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Ekrano_valdymas;
public sealed class CheckBoxTextTemplateSelector : DataTemplateSelector
{
    private static readonly DataTemplate TextTemplate = Create();
    private static DataTemplate Create()
    {
        var text = new FrameworkElementFactory(typeof(OpticallyCenteredText));
        text.SetBinding(OpticallyCenteredText.TextProperty, new Binding());
        foreach (var pair in new[] {
            (OpticallyCenteredText.TextFontFamilyProperty, "FontFamily"),
            (OpticallyCenteredText.TextFontSizeProperty, "FontSize"),
            (OpticallyCenteredText.TextFontWeightProperty, "FontWeight"),
            (OpticallyCenteredText.TextBrushProperty, "Foreground") })
            text.SetBinding(pair.Item1, new Binding(pair.Item2) { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(CheckBox), 1) });
        text.SetValue(OpticallyCenteredText.CompactLineBoxProperty, true);
        text.SetValue(OpticallyCenteredText.InkAlignmentProperty, HorizontalAlignment.Left);
        return new DataTemplate { VisualTree = text };
    }
    public override DataTemplate? SelectTemplate(object item, DependencyObject container) => item is string ? TextTemplate : null;
}
