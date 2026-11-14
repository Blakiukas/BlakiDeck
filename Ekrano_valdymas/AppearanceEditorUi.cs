using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Ekrano_valdymas;

// Scoped to tab and shortcut appearance editors; other dialogs retain their own templates.
internal static class AppearanceEditorUi
{
    internal static SettingsWindow CreateDiscardConfirmation(Window owner)
    {
        var dialog=Window("Unsaved changes",owner,190);dialog.Width=390;dialog.MinWidth=320;dialog.MinHeight=170;dialog.ResizeMode=ResizeMode.NoResize;
        var panel=new StackPanel{Margin=new Thickness(18)};
        panel.Children.Add(new OpticallyCenteredText{Text="Discard unsaved changes?",TextFontFamily=new FontFamily("Segoe UI"),TextFontWeight=FontWeights.Normal,TextFontSize=18,Height=38,InkAlignment=HorizontalAlignment.Left});
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        var keep=MacroUi.Button("Keep editing",(_,_)=>dialog.DialogResult=false);keep.IsCancel=true;keep.IsDefault=true;
        actions.Children.Add(keep);actions.Children.Add(MacroUi.Button("Discard",(_,_)=>dialog.DialogResult=true));panel.Children.Add(actions);
        dialog.Content=panel;Frame(dialog,170,320);return dialog;
    }
    internal static bool ConfirmDiscard(Window owner)=>CreateDiscardConfirmation(owner).ShowDialog()==true;

    internal static SettingsWindow Window(string title, Window? owner, double height = 670)
    {
        var window = MacroUi.Window(title, owner, 560, height);
        window.Resources[typeof(CheckBox)]=Application.Current.FindResource("DashboardCheckBox");
        window.ShowInTaskbar = false;
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.CanResize;
        System.Windows.Shell.WindowChrome.SetWindowChrome(window,new System.Windows.Shell.WindowChrome{CaptionHeight=0,ResizeBorderThickness=new Thickness(6),GlassFrameThickness=new Thickness(0),CornerRadius=new CornerRadius(12),UseAeroCaptionButtons=false});
        window.Resources[typeof(ComboBox)] = System.Windows.Markup.XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ComboBox">
          <Setter Property="Foreground" Value="White"/><Setter Property="MinHeight" Value="32"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox">
            <Grid>
              <ToggleButton Focusable="False" IsChecked="{Binding IsDropDownOpen,Mode=TwoWay,RelativeSource={RelativeSource TemplatedParent}}">
                <ToggleButton.Template><ControlTemplate TargetType="ToggleButton">
                  <Border Background="#26231F" BorderBrush="#68645E" BorderThickness="1" CornerRadius="7">
                    <Grid Width="38" HorizontalAlignment="Right"><Path x:Name="DropdownArrow" Data="M1,1 L8,8 L15,1" Width="16" Height="9" Stroke="#EEEEEE" StrokeThickness="2" StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round" HorizontalAlignment="Center" VerticalAlignment="Center"/></Grid>
                  </Border>
                </ControlTemplate></ToggleButton.Template>
              </ToggleButton>
              <ContentPresenter x:Name="SelectionText" Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" Margin="10,3,40,3" VerticalAlignment="Center" IsHitTestVisible="False"/>
              <TextBlock x:Name="AttentionText" Text="Text — choose Icon or Icon and text" Foreground="#FF4545" FontWeight="SemiBold" Margin="10,3,40,3" VerticalAlignment="Center" Visibility="Collapsed" IsHitTestVisible="False"/>
              <Popup x:Name="PART_Popup" Placement="Bottom" IsOpen="{TemplateBinding IsDropDownOpen}" AllowsTransparency="True" Focusable="False">
                <Border Background="#26231F" BorderBrush="#68645E" BorderThickness="1" CornerRadius="7" Padding="4" MinWidth="{Binding ActualWidth,RelativeSource={RelativeSource TemplatedParent}}">
                  <ScrollViewer MaxHeight="260" CanContentScroll="True"><ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/></ScrollViewer>
                </Border>
              </Popup>
            </Grid>
            <ControlTemplate.Triggers>
              <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger>
              <Trigger Property="Tag" Value="IconRequired"><Setter TargetName="SelectionText" Property="Visibility" Value="Collapsed"/><Setter TargetName="AttentionText" Property="Visibility" Value="Visible"/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """);
        window.Resources[typeof(ComboBoxItem)] = System.Windows.Markup.XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ComboBoxItem">
          <Setter Property="Background" Value="Transparent"/>
          <Setter Property="Foreground" Value="White"/><Setter Property="MinHeight" Value="30"/><Setter Property="Padding" Value="10,4"/>
          <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem">
            <Border Background="{TemplateBinding Background}" Padding="{TemplateBinding Padding}" CornerRadius="5"><ContentPresenter VerticalAlignment="Center"/></Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsHighlighted" Value="True"><Setter Property="Background" Value="#504C46"/></Trigger>
              <Trigger Property="IsSelected" Value="True"><Setter Property="Background" Value="#3B5342"/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """);
        return window;
    }

    internal static void CompactControls(DependencyObject node)
    {
        if(node is Button button&&button.Content is string){button.FontSize=14;button.MinWidth=0;button.MinHeight=32;button.Padding=new Thickness(10,4,10,4);button.Margin=new Thickness(3);if(button.Parent is StackPanel {Orientation:Orientation.Vertical})button.HorizontalAlignment=HorizontalAlignment.Left;}
        if(node is ComboBox combo){combo.MinHeight=32;combo.FontSize=14;}
        if(node is TextBox input){input.MinHeight=32;input.Padding=new Thickness(8,3,8,3);}
        if(node is TabControl tabs){foreach(var tab in tabs.Items.OfType<TabItem>())if(tab.Content is DependencyObject content)CompactControls(content);}
        foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())CompactControls(child);
    }
    internal static void Frame(Window window,double minimumHeight=230,double minimumWidth=400,bool compact=true)
    {
        var body = (UIElement)window.Content;
        // Compact only appearance editors, including inactive tab content. Do not
        // change dashboard controls or their touch targets through global styles.
        if(compact)CompactControls(body);
        if(compact&&body is FrameworkElement content)
        {
            var margin=content.Margin;
            content.Margin=new Thickness(margin.Left,Math.Min(margin.Top,4),margin.Right,margin.Bottom);
        }
        window.Content = null;
        var root = new DockPanel();
        var header = new DockPanel { Margin = new Thickness(18,8,14,0), Height=44, Background=Brushes.Transparent, LastChildFill = true, Cursor=Cursors.SizeAll };
        header.MouseLeftButtonDown += (_,e) => { if (e.LeftButton == MouseButtonState.Pressed) window.DragMove(); };
        TouchDevice? draggingTouch=null;
        Point previousTouch=default;
        header.TouchDown += (_,e) =>
        {
            if(draggingTouch!=null)return;
            for(var source=e.OriginalSource as DependencyObject;source!=null&&source!=header;source=VisualTreeHelper.GetParent(source))
                if(source is System.Windows.Controls.Primitives.ButtonBase)return;
            previousTouch=header.PointToScreen(e.GetTouchPoint(header).Position);
            if(e.TouchDevice.Capture(header))draggingTouch=e.TouchDevice;
            e.Handled=true;
        };
        header.TouchMove += (_,e) =>
        {
            if(e.TouchDevice!=draggingTouch)return;
            var current=header.PointToScreen(e.GetTouchPoint(header).Position);
            var dpi=VisualTreeHelper.GetDpi(window);
            window.Left+=(current.X-previousTouch.X)/dpi.DpiScaleX;
            window.Top+=(current.Y-previousTouch.Y)/dpi.DpiScaleY;
            previousTouch=current;
            e.Handled=true;
        };
        header.TouchUp += (_,e) =>
        {
            if(e.TouchDevice!=draggingTouch)return;
            draggingTouch=null;
            e.TouchDevice.Capture(null);
            e.Handled=true;
        };
        header.LostTouchCapture += (_,e) => { if(e.TouchDevice==draggingTouch)draggingTouch=null; };
        var close=MacroUi.Button("×",(_,_)=>window.Close());close.Width=44;close.Height=44;close.Margin=new Thickness(0);close.IsCancel=true;close.Cursor=Cursors.Arrow;
        DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
        header.Children.Add(new OpticallyCenteredText { Text=window.Title, TextFontFamily=new FontFamily("Segoe UI"),TextFontSize=21,TextFontWeight=FontWeights.SemiBold,TextBrush=Brushes.White,Ellipsis=true });
        DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);root.Children.Add(body);
        var surface=new Grid();surface.Children.Add(new Border { Background=window.Background,BorderBrush=new SolidColorBrush(Color.FromRgb(90,86,80)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Child=root });
        var grip=new System.Windows.Controls.Primitives.Thumb{Name="EditorResizeGrip",Width=18,Height=18,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,3,3),Cursor=Cursors.SizeNWSE,ToolTip="Drag to resize"};
        grip.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Thumb"><Grid Background="Transparent"><Path Data="M 4,14 L 14,4 M 9,14 L 14,9" Stroke="#A59A87" StrokeThickness="1.5"/></Grid></ControlTemplate>
        """);
        grip.DragDelta+=(_,e)=>{window.Width=Math.Clamp(window.ActualWidth+e.HorizontalChange,window.MinWidth,window.MaxWidth);window.Height=Math.Clamp(window.ActualHeight+e.VerticalChange,window.MinHeight,window.MaxHeight);};
        surface.Children.Add(grip);window.Content=surface;
        grip.Visibility=window.ResizeMode==ResizeMode.NoResize?Visibility.Collapsed:Visibility.Visible;
        // Shrink short editors to their content while retaining a bounded scrolling
        // viewport for long forms and smaller work areas.
        ((UIElement)window.Content).Measure(new Size(window.Width-2,double.PositiveInfinity));
        window.Height=Math.Max(minimumHeight,Math.Min(window.Height,((UIElement)window.Content).DesiredSize.Height+2));
        window.MinHeight=minimumHeight;
        window.ContentRendered+=(_,_)=>
        {
            // Templates and responsive columns have now reached their final size.
            var viewer=body as ScrollViewer ?? (body as Panel)?.Children.OfType<ScrollViewer>().FirstOrDefault();
            if(viewer!=null){double spare=viewer.ViewportHeight-viewer.ExtentHeight;if(spare>4)window.Height=Math.Max(minimumHeight,window.Height-spare+8);}
            DialogWorkArea.Attach(window,minimumWidth,minimumHeight);
        };
    }

    internal static Grid Columns(Panel first,Panel second)
    {
        var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new());grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
        Grid.SetColumn(second,1);grid.Children.Add(first);grid.Children.Add(second);
        grid.SizeChanged+=(_,_)=>{bool narrow=grid.ActualWidth<680;Grid.SetColumn(second,narrow?0:1);Grid.SetRow(second,narrow?1:0);Grid.SetColumnSpan(first,narrow?2:1);Grid.SetColumnSpan(second,narrow?2:1);};
        return grid;
    }

    internal static ComboBox Choice(string value, params (string Key,string Label)[] options)
    {
        var combo=new ComboBox { SelectedValuePath="Tag" };
        foreach(var option in options)combo.Items.Add(new ComboBoxItem{Tag=option.Key,Content=option.Label});
        combo.SelectedValue=value;return combo;
    }
    internal static ComboBox Presentation(string value) => Choice(value,("Text","Text"),("Icon","Icon"),("IconText","Icon and text"));
    internal static string Selected(ComboBox box) => box.SelectedValue as string ?? "Text";
    internal static TextBlock Help(Panel panel,string text)
    {
        var label=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Color.FromRgb(184,179,171)),FontSize=13,Margin=new Thickness(4,2,4,3)};
        var style=new Style(typeof(TextBlock));var empty=new Trigger{Property=TextBlock.TextProperty,Value=""};empty.Setters.Add(new Setter(UIElement.VisibilityProperty,Visibility.Collapsed));style.Triggers.Add(empty);label.Style=style;
        panel.Children.Add(label);return label;
    }
    internal static Border AttentionField(Panel panel,string label,FrameworkElement control)
    {
        var frame=new Border { CornerRadius=new CornerRadius(9),BorderThickness=new Thickness(2),BorderBrush=new SolidColorBrush(Colors.Transparent),Child=control };
        EditorUi.Field(panel,label,frame);return frame;
    }
    internal static void Pulse(Border frame,bool retain=false)
    {
        var amber=retain?Color.FromRgb(255,69,69):Color.FromRgb(255,185,75);
        var brush=new SolidColorBrush(retain?amber:Colors.Transparent);frame.BorderBrush=brush;
        brush.BeginAnimation(SolidColorBrush.ColorProperty,new ColorAnimation(retain?amber:Colors.Transparent,retain?Colors.Transparent:amber,TimeSpan.FromMilliseconds(250)){AutoReverse=true,RepeatBehavior=new RepeatBehavior(2),FillBehavior=FillBehavior.Stop});
    }
}
