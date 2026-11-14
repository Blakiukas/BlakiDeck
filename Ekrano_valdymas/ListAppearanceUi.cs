using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Ekrano_valdymas;

internal static class ListAppearanceUi
{
    internal static ContextMenu Create(FrameworkElement owner)
    {
        var window=Window.GetWindow(owner);
        var main=window as MainWindow??window?.Owner as MainWindow??Application.Current.MainWindow as MainWindow;
        if(main!=null)return main.NewModuleMenu();
        var menu=new ContextMenu{Background=new SolidColorBrush(Color.FromRgb(44,44,44)),Foreground=Brushes.White,FontFamily=new FontFamily("Segoe UI Semibold")};
        menu.SetResourceReference(Control.FontSizeProperty,"DashboardMenuFontSize");
        return menu;
    }
    internal static void Compact(ContextMenu menu)
    {
        void Row(MenuItem item){item.ClearValue(FrameworkElement.StyleProperty);item.Padding=new Thickness(6,0,6,0);item.Margin=new Thickness(0);if(item.Header is Panel panel)foreach(var child in panel.Children.OfType<MenuItem>())Row(child);}
        foreach(var item in menu.Items.OfType<MenuItem>())Row(item);
    }
    internal static void SliderRow(ContextMenu menu,string label,string name,double min,double max,double value,Action<double> change,double? defaultValue=null,string? defaultLabel=null)
    {
        var panel=new StackPanel{Orientation=Orientation.Horizontal};
        var caption=new OpticallyCenteredText{Text=$"{label} · {value:0}",Margin=new Thickness(0,0,12,0),VerticalAlignment=VerticalAlignment.Center,TextFontFamily=new FontFamily("Segoe UI"),TextFontSize=15,TextFontWeight=FontWeights.Normal,InkAlignment=HorizontalAlignment.Left};
        caption.SetBinding(OpticallyCenteredText.TextFontSizeProperty,new System.Windows.Data.Binding("FontSize"){Source=menu});caption.SetBinding(OpticallyCenteredText.TextFontFamilyProperty,new System.Windows.Data.Binding("FontFamily"){Source=menu});caption.TextFontWeight=FontWeights.SemiBold;
        var slider=new Slider{Name=name,Minimum=min,Maximum=max,Value=value,TickFrequency=1,IsSnapToTickEnabled=true,Width=145,Height=26,VerticalAlignment=VerticalAlignment.Center,IsMoveToPointEnabled=true};slider.SetResourceReference(FrameworkElement.StyleProperty,"DashboardSlider");
        if(defaultValue is double reference)SliderDefaults.Mark(slider,reference,defaultLabel);
        slider.ValueChanged+=(_,_)=>{caption.Text=$"{label} · {slider.Value:0}";change(slider.Value);};panel.Children.Add(caption);panel.Children.Add(slider);
        var row=new MenuItem{Header=panel,StaysOpenOnClick=true};row.SetResourceReference(FrameworkElement.StyleProperty,"MiniMenuItemStyle");menu.Items.Add(row);
    }
    internal static bool PickColor(Window owner,Color initial,Action<Color> preview,out Color selected,Color? defaultColor=null,Action<bool>? defaultChanged=null)
    {
        Color current=initial;bool isDefault=false;
        var dialog=CompactColorPicker.Create(owner,initial.ToString(),value=>{isDefault=value==null;current=value==null?defaultColor??initial:CompactColorPicker.Parse(value,initial);preview(current);});
        bool accepted=dialog.ShowDialog()==true;selected=accepted?current:initial;
        if(accepted)defaultChanged?.Invoke(isDefault);else preview(initial);
        return accepted;
    }
}
