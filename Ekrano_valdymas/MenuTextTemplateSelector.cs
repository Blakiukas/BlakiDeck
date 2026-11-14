using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Ekrano_valdymas;
public sealed class MenuTextTemplateSelector:DataTemplateSelector
{
    private static readonly DataTemplate textTemplate=Create();
    private static DataTemplate Create()
    {
        var text=new FrameworkElementFactory(typeof(OpticallyCenteredText));text.SetBinding(OpticallyCenteredText.TextProperty,new Binding());
        void Bind(DependencyProperty property,string source)=>text.SetBinding(property,new Binding(source){RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(MenuItem),1)});
        Bind(OpticallyCenteredText.TextBrushProperty,"Foreground");Bind(OpticallyCenteredText.TextFontFamilyProperty,"FontFamily");Bind(OpticallyCenteredText.TextFontSizeProperty,"FontSize");Bind(OpticallyCenteredText.TextFontWeightProperty,"FontWeight");
        text.SetValue(OpticallyCenteredText.InkAlignmentProperty,HorizontalAlignment.Left);
        text.SetValue(OpticallyCenteredText.CompactLineBoxProperty,true);
        text.SetValue(FrameworkElement.MarginProperty,new Thickness(0,1,0,1));
        return new DataTemplate{VisualTree=text};
    }
    public override DataTemplate? SelectTemplate(object item,DependencyObject container)=>item is string?textTemplate:null;
}
