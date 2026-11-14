using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ekrano_valdymas;

internal static class MacroUi
{
    internal static ScrollViewer Scroll(UIElement content)
    {
        var viewer=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,PanningMode=PanningMode.VerticalOnly};
        var style=(Style)System.Windows.Markup.XamlReader.Parse("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ScrollBar">
              <Setter Property="Width" Value="12"/><Setter Property="Background" Value="#211E1A"/>
              <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar">
                <Border Background="{TemplateBinding Background}" CornerRadius="6">
                  <Track x:Name="PART_Track" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Orientation="Vertical" IsDirectionReversed="True">
                    <Track.DecreaseRepeatButton><RepeatButton Command="ScrollBar.PageUpCommand" Opacity="0" Focusable="False"/></Track.DecreaseRepeatButton>
                    <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="#68645E" CornerRadius="5" Margin="2"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
                    <Track.IncreaseRepeatButton><RepeatButton Command="ScrollBar.PageDownCommand" Opacity="0" Focusable="False"/></Track.IncreaseRepeatButton>
                  </Track>
                </Border>
              </ControlTemplate></Setter.Value></Setter>
            </Style>
            """);
        viewer.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)]=style;return viewer;
    }
    internal static Button Button(string text,RoutedEventHandler action)
    {
        var button=EditorUi.Button(text,action);
        button.Foreground=Brushes.White;button.Background=Brushes.Transparent;
        button.BorderBrush=new SolidColorBrush(Color.FromArgb(85,255,255,255));button.BorderThickness=new Thickness(1);
        var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(8));
        border.SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Background"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});
        border.SetBinding(Border.BorderBrushProperty,new System.Windows.Data.Binding("BorderBrush"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});
        border.SetValue(Border.BorderThicknessProperty,new Thickness(1));border.SetBinding(Border.PaddingProperty,new System.Windows.Data.Binding("Padding"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});
        var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);content.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(content);
        var template=new ControlTemplate(typeof(Button)){VisualTree=border};
        var hover=new Trigger{Property=UIElement.IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(Control.BorderBrushProperty,new SolidColorBrush(Color.FromRgb(255,138,0))));template.Triggers.Add(hover);
        var pressed=new Trigger{Property=System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty,Value=true};pressed.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(90,59,0))));template.Triggers.Add(pressed);
        button.Template=template;
        button.ContentTemplateSelector=new ButtonTextTemplateSelector();
        if(text.StartsWith("Save",StringComparison.OrdinalIgnoreCase)||text.Equals("Apply",StringComparison.OrdinalIgnoreCase)){
            button.SetResourceReference(Control.BackgroundProperty,"ActionFillBrush");button.SetResourceReference(Control.BorderBrushProperty,"ActionAccentBrush");
        }
        return button;
    }
    internal static SettingsWindow Window(string title,Window? owner,double width=540,double height=610)
    {
        var window=EditorUi.Window(title,owner,width,height);ApplyTheme(window);return window;
    }
    internal static void ApplyTheme(Window window)
    {
        window.Resources[typeof(CheckBox)]=Application.Current.FindResource("DashboardCheckBox");
        window.Background=new SolidColorBrush(Color.FromRgb(26,23,20));window.Foreground=Brushes.White;window.FontFamily=new FontFamily("Segoe UI");window.FontSize=16;
        foreach(var type in new[]{typeof(TextBox),typeof(ListBox)})
        {
            var style=new Style(type);style.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(38,35,31))));style.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.White));style.Setters.Add(new Setter(Control.BorderBrushProperty,new SolidColorBrush(Color.FromRgb(85,82,77))));if(type==typeof(TextBox)){style.Setters.Add(new EventSetter(FrameworkElement.ContextMenuOpeningEvent,new ContextMenuEventHandler(TextEditMenu.Opening)));style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty,VerticalAlignment.Center));style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(8,4,8,4)));}window.Resources[type]=style;
        }
        window.Resources[typeof(ListBoxItem)]=(Style)System.Windows.Markup.XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ListBoxItem">
          <Setter Property="Foreground" Value="White"/><Setter Property="Background" Value="Transparent"/><Setter Property="HorizontalContentAlignment" Value="Stretch"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ListBoxItem"><Border x:Name="Frame" Background="{TemplateBinding Background}" BorderBrush="Transparent" BorderThickness="1" CornerRadius="4" Padding="4"><ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"/></Border>
          <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="Background" Value="#35312C"/></Trigger><Trigger Property="IsSelected" Value="True"><Setter TargetName="Frame" Property="Background" Value="#49453F"/><Setter TargetName="Frame" Property="BorderBrush" Value="#FF8A00"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
        </Style>
        """);
        window.Resources[typeof(ComboBox)]=(Style)Application.Current.FindResource("DashboardComboBox");
    }
}

internal sealed class MacroCountdown:IDisposable
{
    private readonly MacroRunner runner;
    private readonly Window? owner;
    private Window? window;
    private OpticallyCenteredText? label;
    private OpticallyCenteredText? secondsLabel;
    private readonly ImageSource? launchIcon;
    private readonly string? launchTarget;
    private IDisposable? dashboardBlur;
    private bool closing;
    internal MacroCountdown(MacroRunner runner,Window? owner,ImageSource? launchIcon=null,string? launchTarget=null){this.runner=runner;this.owner=owner;this.launchIcon=launchIcon;this.launchTarget=launchTarget;runner.Changed+=Update;}
    private void Update(object? sender,EventArgs args)
    {
        if(runner.SecondsRemaining==null){Close();return;}
        if(window==null)
        {
            bool light=CommonMetal.IsLight;
            Brush text=light?new SolidColorBrush(Color.FromRgb(30,32,33)):Brushes.WhiteSmoke;
            Brush outline=new SolidColorBrush(light?Color.FromRgb(85,88,90):Color.FromRgb(107,110,112));
            window=MacroUi.Window("Power action",owner,400,launchIcon==null?226:326);window.MinHeight=226;window.ResizeMode=ResizeMode.NoResize;window.WindowStyle=WindowStyle.None;window.AllowsTransparency=true;window.ShowInTaskbar=false;window.Topmost=true;
            window.Background=Brushes.Transparent;
            var panel=new StackPanel{Margin=new Thickness(16),VerticalAlignment=VerticalAlignment.Center};
            var frame=new Border{Background=CommonMetal.DefaultBrush,BorderBrush=outline,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Child=panel};
            CommonMetal.MatchDashboardMaterial(frame,owner);CommonMetal.SetSurface(frame,true);window.Content=frame;
            if(launchIcon!=null)panel.Children.Add(new AdaptiveIconImage{Source=launchIcon,SkinIcon=true,ShortcutTarget=launchTarget,Width=88,Height=88,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,0,12),Effect=new System.Windows.Media.Effects.DropShadowEffect{Color=Colors.Black,BlurRadius=12,ShadowDepth=4,Direction=270,Opacity=.8}});
            label=new OpticallyCenteredText{TextFontSize=30,TextFontFamily=new FontFamily("Segoe UI"),TextBrush=text,Height=44,Ellipsis=true};panel.Children.Add(label);
            secondsLabel=new OpticallyCenteredText{TextFontSize=34,TextFontFamily=new FontFamily("Segoe UI"),TextBrush=new SolidColorBrush(light?Color.FromRgb(165,15,15):Color.FromRgb(255,76,76)),Height=46,Margin=new Thickness(0,0,0,12)};panel.Children.Add(secondsLabel);
            var cancel=new Button{Content=new OpticallyCenteredText{Text="Cancel",TextFontFamily=new FontFamily("Segoe UI"),TextFontSize=24,TextBrush=text,Height=34},Height=64,Background=new SolidColorBrush(light?Color.FromRgb(194,197,198):Color.FromRgb(53,56,58)),BorderBrush=outline,BorderThickness=new Thickness(1),IsCancel=true};
            var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(9));border.SetValue(Border.BorderThicknessProperty,new Thickness(1));
            border.SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Background"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});border.SetBinding(Border.BorderBrushProperty,new System.Windows.Data.Binding("BorderBrush"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});
            var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);content.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(content);
            var template=new ControlTemplate(typeof(Button)){VisualTree=border};var pressed=new Trigger{Property=System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty,Value=true};pressed.Setters.Add(new Setter(UIElement.OpacityProperty,.72));template.Triggers.Add(pressed);cancel.Template=template;
            cancel.Click+=(_,_)=>runner.Stop();panel.Children.Add(cancel);
            window.ContentRendered+=(_,_)=>{if(window!=null)DialogWorkArea.PlaceAtOwnerTop(window,owner);};
            window.Closing+=(_,_)=>{if(!closing)runner.Stop();};window.Closed+=(_,_)=>{dashboardBlur?.Dispose();dashboardBlur=null;window=null;};
            dashboardBlur=DashboardModalBlur.Acquire(owner);
            try{window.Show();}catch{dashboardBlur?.Dispose();dashboardBlur=null;window=null;throw;}
        }
        label!.Text=runner.Status.Split(" — ")[0];secondsLabel!.Text=$"{runner.SecondsRemaining} s";
    }
    private void Close(){closing=true;try{window?.Close();window=null;}finally{dashboardBlur?.Dispose();dashboardBlur=null;closing=false;}}
    public void Dispose(){runner.Changed-=Update;Close();}
}
