using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace Ekrano_valdymas;

// Scoped editor tabs share the existing dialog typography and action colors.
// Their content has no native TabControl border or opaque system background.
internal static class SettingsEditorTabs
{
    internal static TabControl Create(string name)
    {
        var tabs=new TabControl
        {
            Name=name,Background=Brushes.Transparent,BorderThickness=new Thickness(0),
            Padding=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Stretch,
            VerticalContentAlignment=VerticalAlignment.Stretch,SelectedIndex=0,
            ItemContainerStyle=ItemStyle(),Template=ContentTemplate()
        };
        // The settings frame stays dark even when the dashboard skin is light.
        tabs.SetBinding(Control.ForegroundProperty,new Binding("Foreground"){RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(Window),1)});
        tabs.Loaded+=(_,_)=>RequestFit(tabs);
        tabs.SelectionChanged+=(_,e)=>{if(ReferenceEquals(e.Source,tabs))RequestFit(tabs);};
        return tabs;
    }

    internal static void RequestFit(TabControl tabs)
    {
        if(!tabs.IsLoaded)return;
        tabs.Dispatcher.BeginInvoke(new Action(()=>
        {
            if(Window.GetWindow(tabs) is not {IsVisible:true,SizeToContent:SizeToContent.Manual} window)return;
            window.UpdateLayout();
            double Extra(DependencyObject node)
            {
                double extra=node is ScrollViewer {IsVisible:true} viewer?viewer.ScrollableHeight:0;
                for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)
                    extra=Math.Max(extra,Extra(VisualTreeHelper.GetChild(node,i)));
                return extra;
            }
            double overflow=Extra(tabs);
            if(overflow>1)DialogWorkArea.GrowToContent(window,overflow+4);
        }),DispatcherPriority.Loaded);
    }

    private static ControlTemplate ContentTemplate()
    {
        var root=new FrameworkElementFactory(typeof(DockPanel));
        root.SetValue(DockPanel.LastChildFillProperty,true);
        var headers=new FrameworkElementFactory(typeof(TabPanel)){Name="HeaderPanel"};
        headers.SetValue(DockPanel.DockProperty,Dock.Top);
        headers.SetValue(Panel.IsItemsHostProperty,true);
        headers.SetValue(FrameworkElement.MarginProperty,new Thickness(0,0,0,12));
        root.AppendChild(headers);
        var content=new FrameworkElementFactory(typeof(ContentPresenter)){Name="PART_SelectedContentHost"};
        content.SetValue(ContentPresenter.ContentSourceProperty,"SelectedContent");
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Stretch);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Stretch);
        root.AppendChild(content);
        return new ControlTemplate(typeof(TabControl)){VisualTree=root};
    }

    private static Style ItemStyle()
    {
        var style=new Style(typeof(TabItem));
        style.Setters.Add(new Setter(Control.ForegroundProperty,new Binding("Foreground"){RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(TabControl),1)}));
        style.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(38,35,31))));
        style.Setters.Add(new Setter(Control.BorderBrushProperty,new SolidColorBrush(Color.FromRgb(85,82,77))));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(1)));
        style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(14,8,14,8)));
        // TabPanel arranges each header without its external margin. Keep the
        // gap inside the template so it cannot subtract the right edge twice.
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(0)));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty,88d));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty,36d));
        style.Setters.Add(new Setter(HeaderedContentControl.HeaderTemplateProperty,HeaderTemplate()));
        var border=new FrameworkElementFactory(typeof(Border)){Name="TabBorder"};
        border.SetValue(Border.CornerRadiusProperty,new CornerRadius(7));
        border.SetValue(FrameworkElement.MarginProperty,new Thickness(0,0,8,0));
        foreach(var property in new[]{Border.BackgroundProperty,Border.BorderBrushProperty,Border.BorderThicknessProperty,Border.PaddingProperty})
            border.SetBinding(property,new Binding(property.Name){RelativeSource=RelativeSource.TemplatedParent});
        var header=new FrameworkElementFactory(typeof(ContentPresenter));
        header.SetValue(ContentPresenter.ContentSourceProperty,"Header");
        header.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);
        header.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
        border.AppendChild(header);
        var template=new ControlTemplate(typeof(TabItem)){VisualTree=border};
        var hover=new Trigger{Property=UIElement.IsMouseOverProperty,Value=true};
        hover.Setters.Add(new Setter(Border.BorderBrushProperty,new SolidColorBrush(Color.FromRgb(150,144,136)),"TabBorder"));
        template.Triggers.Add(hover);
        var selected=new Trigger{Property=TabItem.IsSelectedProperty,Value=true};
        selected.Setters.Add(new Setter(Border.BorderBrushProperty,new SolidColorBrush(Color.FromRgb(100,100,100)),"TabBorder"));
        selected.Setters.Add(new Setter(Border.BackgroundProperty,new SolidColorBrush(Color.FromRgb(76,76,76)),"TabBorder"));
        template.Triggers.Add(selected);
        var focus=new Trigger{Property=UIElement.IsKeyboardFocusedProperty,Value=true};
        focus.Setters.Add(new Setter(Border.BorderBrushProperty,new SolidColorBrush(Color.FromRgb(150,150,150)),"TabBorder"));
        template.Triggers.Add(focus);
        var disabled=new Trigger{Property=UIElement.IsEnabledProperty,Value=false};
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty,.45));template.Triggers.Add(disabled);
        style.Setters.Add(new Setter(Control.TemplateProperty,template));
        return style;
    }

    private static DataTemplate HeaderTemplate()
    {
        var text=new FrameworkElementFactory(typeof(OpticallyCenteredText));
        text.SetBinding(OpticallyCenteredText.TextProperty,new Binding());
        void Bind(DependencyProperty property,string source)=>text.SetBinding(property,new Binding(source)
            {RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(TabItem),1)});
        Bind(OpticallyCenteredText.TextBrushProperty,"Foreground");
        Bind(OpticallyCenteredText.TextFontFamilyProperty,"FontFamily");
        Bind(OpticallyCenteredText.TextFontSizeProperty,"FontSize");
        Bind(OpticallyCenteredText.TextFontWeightProperty,"FontWeight");
        Bind(OpticallyCenteredText.TextFontStyleProperty,"FontStyle");
        return new DataTemplate{VisualTree=text};
    }
}
