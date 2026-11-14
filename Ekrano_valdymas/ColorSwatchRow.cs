using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Ekrano_valdymas;

// Shared compact palette: visible choices keep their names in hover/accessibility text.
internal sealed class ColorSwatchRow : WrapPanel
{
    private readonly List<(Button Button,string? Color)> choices=new();
    private readonly Button custom;
    private readonly OpticallyCenteredText customLabel;
    private readonly Action<string?> change;
    private readonly Window? owner;
    private string? current;
    internal static FrameworkElement Create(string? initial,Action<string?> set,Window? owner=null,bool bright=false)=>new ColorSwatchRow(initial,set,owner,bright);
    internal static void BindMenu(string? initial,Action<string?> set,MenuItem parent,Window? owner)
    {
        string? current=initial;
        parent.Click+=(_,e)=>
        {
            if(!ReferenceEquals(e.OriginalSource,parent))return;
            e.Handled=true;
            ItemsControl? ancestor=parent;
            while(ancestor is MenuItem menu){menu.IsSubmenuOpen=false;ancestor=ItemsControl.ItemsControlFromItemContainer(menu);}
            if(ancestor is ContextMenu context)context.IsOpen=false;
            parent.Dispatcher.BeginInvoke(new Action(()=>CompactColorPicker.Create(owner??Application.Current.MainWindow,current,value=>{current=value;set(value);}).ShowDialog()),System.Windows.Threading.DispatcherPriority.Background);
        };
    }    private ColorSwatchRow(string? initial,Action<string?> set,Window? owner,bool bright)
    {
        current=initial;change=set;this.owner=owner;Orientation=Orientation.Horizontal;
        custom=Swatch("Choose color…");custom.Width=160;custom.Height=34;custom.Margin=new Thickness(0,2,0,2);
        customLabel=new OpticallyCenteredText{Text="Choose color…",Width=150,Height=24,TextFontFamily=new FontFamily("Segoe UI"),TextFontSize=14,TextBrush=Brushes.White};custom.Content=customLabel;Children.Add(custom);
        custom.Click+=(_,_)=>{var dialogOwner=owner??Window.GetWindow(this)??Application.Current.MainWindow;Dispatcher.BeginInvoke(new Action(()=>CompactColorPicker.Create(dialogOwner,current,Select).ShowDialog()),System.Windows.Threading.DispatcherPriority.Background);};RefreshSelection();
    }
    private static Button Swatch(string name)
    {
        var button=new Button{Width=36,Height=34,Margin=new Thickness(0,2,5,2),Padding=new Thickness(0),Background=Brushes.Transparent,BorderThickness=new Thickness(1),ToolTip=name,Cursor=System.Windows.Input.Cursors.Hand};
        System.Windows.Automation.AutomationProperties.SetName(button,name);
        var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(5));
        foreach(var (target,source) in new[]{(Border.BackgroundProperty,"Background"),(Border.BorderBrushProperty,"BorderBrush"),(Border.BorderThicknessProperty,"BorderThickness")})border.SetBinding(target,new System.Windows.Data.Binding(source){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});
        var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);content.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(content);
        var template=new ControlTemplate(typeof(Button)){VisualTree=border};
        var hover=new Trigger{Property=IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(OpacityProperty,.88));template.Triggers.Add(hover);button.Template=template;return button;
    }
    private static bool Same(string? left,string? right)=>left==null||right==null?left==right:CompactColorPicker.Parse(left,Colors.Transparent)==CompactColorPicker.Parse(right,Colors.Transparent);
    private void Select(string? color){current=color;RefreshSelection();change(color);}
    private void RefreshSelection()
    {
        bool known=false;
        foreach(var choice in choices){bool selected=Same(current,choice.Color);known|=selected;choice.Button.BorderBrush=selected?Brushes.Orange:new SolidColorBrush(Color.FromRgb(88,85,80));System.Windows.Automation.AutomationProperties.SetItemStatus(choice.Button,selected?"Selected":"");}
        custom.BorderBrush=!known?Brushes.Orange:new SolidColorBrush(Color.FromRgb(88,85,80));
        var color=CompactColorPicker.Parse(current,Colors.White);
        custom.Background=current==null?new LinearGradientBrush(Colors.Red,Colors.Blue,45):new SolidColorBrush(color);
        double alpha=color.A/255d;double brightness=(.2126*color.R+.7152*color.G+.0722*color.B)*alpha+35*(1-alpha);
        customLabel.TextBrush=current!=null&&brightness>150?Brushes.Black:Brushes.White;
        custom.ToolTip=current==null?"Custom color…":"Custom color… "+current;
    }
}
