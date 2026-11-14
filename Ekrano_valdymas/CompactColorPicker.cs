using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Ekrano_valdymas;
internal static class CompactColorPicker
{
    internal static SettingsWindow Create(Window? owner,string? initial,Action<string?> change) => CreateCore(owner,initial,change,true);
    internal static SettingsWindow CreateOpaque(Window? owner,string? initial,Action<string?> change) => CreateCore(owner,initial,change,false);
    private static SettingsWindow CreateCore(Window? owner,string? initial,Action<string?> change,bool includeAlpha)
    {
        bool accepted=false,valid=true;string? current=initial;
        var history=new ColorEditHistory<string?>();history.Record(initial);
        var window=MacroUi.Window("Choose color",owner,430,510);window.MinWidth=390;window.MinHeight=400;window.ShowInTaskbar=false;
        var root=new DockPanel{Margin=new Thickness(16)};window.Content=root;
        var footer=new Grid{Margin=new Thickness(0,14,0,0)};footer.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});footer.ColumnDefinitions.Add(new(){Width=GridLength.Auto});footer.ColumnDefinitions.Add(new(){Width=GridLength.Auto});DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var save=MacroUi.Button("Save",(_,_)=>{if(!valid)return;accepted=true;RecentColors.Save(new[]{current});change(current);window.DialogResult=true;});save.Name="ColorSave";save.Width=80;Grid.SetColumn(save,2);footer.Children.Add(save);
        var cancel=MacroUi.Button("Cancel",(_,_)=>window.DialogResult=false);cancel.Width=70;Grid.SetColumn(cancel,1);footer.Children.Add(cancel);
        var body=Surface(window,initial,value=>{current=value;history.Record(value);change(value);},out var refresh,ok=>{valid=ok;save.IsEnabled=ok;},includeAlpha);
        var scroll=MacroUi.Scroll(body);window.AutoFitScroll=scroll;root.Children.Add(scroll);
        var defaults=MacroUi.Button("Default",(_,_)=>{current=null;history.Record(null);refresh(null);change(null);});defaults.Width=70;defaults.MinWidth=0;defaults.HorizontalAlignment=HorizontalAlignment.Left;footer.Children.Add(history.Buttons(defaults,value=>{current=value;refresh(value);change(value);}));
        window.Closed+=(_,_)=>{if(!accepted)change(initial);};return window;
    }
    internal static FrameworkElement Surface(Window window,string? initial,Action<string?> change,out Action<string?> refresh,Action<bool> validity,bool includeAlpha=true,Func<string>? alphaLabel=null)
    {
        Color color=Parse(initial,Colors.White);bool updating=false,isDefault=initial==null;double h=0,s=0,v=0;
        var body=new StackPanel();
        var field=new Grid{Height=210,Margin=new Thickness(0,0,0,12)};field.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});field.ColumnDefinitions.Add(new(){Width=new GridLength(48)});field.ColumnDefinitions.Add(new(){Width=new GridLength(48)});body.Children.Add(field);
        var plane=new ColorPlane{Focusable=true,Margin=new Thickness(0,0,10,0)};field.Children.Add(plane);
        System.Windows.Automation.AutomationProperties.SetName(plane,"Saturation and brightness; arrow keys adjust");
        Slider Strip(string name,double max)
        {
            var slider=new Slider{Name=name,Height=210,VerticalAlignment=VerticalAlignment.Stretch,Minimum=0,Maximum=max,Orientation=Orientation.Vertical,IsMoveToPointEnabled=true,Margin=new Thickness(5,0,5,0),SmallChange=1,LargeChange=15};
            slider.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
                <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Slider">
                  <Grid Background="Transparent">
                    <Border Margin="4,5" CornerRadius="5" BorderBrush="#777777" BorderThickness="1" Background="{TemplateBinding Background}"/>
                    <Track Name="PART_Track" Orientation="Vertical" IsDirectionReversed="False">
                      <Track.DecreaseRepeatButton><RepeatButton Command="Slider.DecreaseLarge" Opacity="0"/></Track.DecreaseRepeatButton>
                      <Track.Thumb><Thumb Width="36" Height="12" Cursor="SizeNS"><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="Transparent" BorderBrush="#222222" BorderThickness="1" CornerRadius="5"><Border BorderBrush="White" BorderThickness="2" CornerRadius="4"/></Border></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
                      <Track.IncreaseRepeatButton><RepeatButton Command="Slider.IncreaseLarge" Opacity="0"/></Track.IncreaseRepeatButton>
                    </Track>
                  </Grid>
                </ControlTemplate>
                """);return slider;
        }
        var hue=Strip("HueSlider",360);var alpha=Strip("AlphaSlider",255);if(!includeAlpha){alpha.Visibility=Visibility.Collapsed;field.ColumnDefinitions[2].Width=new GridLength(0);}Grid.SetColumn(hue,1);Grid.SetColumn(alpha,2);field.Children.Add(hue);field.Children.Add(alpha);hue.ToolTip="Hue";
        var rainbow=new LinearGradientBrush{StartPoint=new Point(0,1),EndPoint=new Point(0,0)};
        foreach(var stop in new[]{(Colors.Red,0d),(Colors.Yellow,1d/6),(Colors.Lime,2d/6),(Colors.Cyan,.5),(Colors.Blue,4d/6),(Colors.Magenta,5d/6),(Colors.Red,1d)})rainbow.GradientStops.Add(new(stop.Item1,stop.Item2));hue.Background=rainbow;
        var palette=new System.Windows.Controls.Primitives.UniformGrid{Columns=7,Rows=2,Margin=new Thickness(0,0,0,12)};body.Children.Add(palette);
        var label=new TextBlock{Text="Custom color",Margin=new Thickness(0,0,0,6)};body.Children.Add(label);
        var custom=new Grid{Height=48};custom.ColumnDefinitions.Add(new(){Width=new GridLength(48)});custom.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});custom.ColumnDefinitions.Add(new(){Width=new GridLength(48)});body.Children.Add(custom);
        var preview=new Border{Margin=new Thickness(0,0,8,0),BorderBrush=Brushes.Gray,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(5)};custom.Children.Add(preview);
        var hex=new TextBox{Name="ColorHex",MaxLength=9,MinWidth=0,VerticalContentAlignment=VerticalAlignment.Center,Padding=new Thickness(8,0,4,0)};Grid.SetColumn(hex,1);custom.Children.Add(hex);
        var pipette=MacroUi.Button("",(_,_)=>{});pipette.Width=40;pipette.Height=40;pipette.MinWidth=0;pipette.MinHeight=0;pipette.Padding=new Thickness(8);pipette.ToolTip="Pick a color from the screen · Esc ends sampling";
        custom.ColumnDefinitions.Insert(2,new(){Width=new GridLength(48)});
        var copy=CopyFeedback.Create(()=>hex.Text,copied=>{if(copied)hex.BorderBrush=new SolidColorBrush(Color.FromRgb(59,107,245));else hex.ClearValue(Control.BorderBrushProperty);});copy.Name="CopyColorCode";Grid.SetColumn(copy,2);custom.Children.Add(copy);
        copy.Width=pipette.Width;copy.Height=pipette.Height;copy.Margin=pipette.Margin;copy.VerticalAlignment=pipette.VerticalAlignment;
        pipette.Name="ColorPipette";pipette.Content=PipetteCursorFactory.Icon();Grid.SetColumn(pipette,3);custom.Children.Add(pipette);
        var error=new TextBlock{Foreground=Brushes.Salmon,TextWrapping=TextWrapping.Wrap,Visibility=Visibility.Collapsed};body.Children.Add(error);
        body.Children.Add(RecentColors.Row(value=>{color=Parse(value,Colors.White);isDefault=false;Publish();}));
        void Publish(bool notify=true,bool readColor=true,bool editing=false)
        {
            updating=true;try{if(readColor)ToHsv(color,out h,out s,out v);plane.Hue=h;plane.Saturation=s;plane.Brightness=v;plane.InvalidateVisual();hue.Value=h;alpha.Value=color.A;preview.Background=new SolidColorBrush(color);if(!editing)hex.Text=color.A==255?$"#{color.R:X2}{color.G:X2}{color.B:X2}":color.ToString();label.Text=isDefault?"Custom color · Default":"Custom color";error.Text="";error.Visibility=Visibility.Collapsed;validity(true);
                var drawing=new DrawingGroup();drawing.Children.Add(new GeometryDrawing(Checkerboard(),null,new RectangleGeometry(new Rect(0,0,24,200))));drawing.Children.Add(new GeometryDrawing(new LinearGradientBrush(Color.FromArgb(0,color.R,color.G,color.B),Color.FromRgb(color.R,color.G,color.B),new Point(0,1),new Point(0,0)),null,new RectangleGeometry(new Rect(0,0,24,200))));alpha.Background=new DrawingBrush(drawing);alpha.ToolTip=$"{alphaLabel?.Invoke()??"Opacity"} · {color.A*100/255d:0}%";
            }finally{updating=false;}if(notify)change(isDefault?null:color.ToString());
        }
        foreach(bool pastel in new[]{false,true})foreach(var item in new[]{("Red",Colors.Red),("Orange",Color.FromRgb(255,128,0)),("Yellow",Colors.Yellow),("Green",Colors.Lime),("Cyan",Colors.Cyan),("Blue",Color.FromRgb(0,100,255)),("Purple",Color.FromRgb(160,0,255))})
        {
            Color c=pastel?Color.FromRgb((byte)((item.Item2.R+255)/2),(byte)((item.Item2.G+255)/2),(byte)((item.Item2.B+255)/2)):item.Item2;if(pastel&&item.Item1=="White")c=Color.FromRgb(190,190,190);
            var b=MacroUi.Button("",(_,_)=>{isDefault=false;color=c;Publish();});b.Name="ColorPreset";b.MinWidth=0;b.MinHeight=0;b.Height=32;b.Margin=new Thickness(2);b.Padding=new Thickness(0);b.Background=new SolidColorBrush(c);b.ToolTip=(pastel?"Pastel ":"")+item.Item1;palette.Children.Add(b);
        }
        plane.Changed+=()=>{isDefault=false;s=plane.Saturation;v=plane.Brightness;color=FromHsv(h,s,v,color.A);Publish(readColor:false);};
        hue.ValueChanged+=(_,_)=>{if(updating)return;isDefault=false;h=hue.Value;color=FromHsv(h,s,v,color.A);Publish(readColor:false);};
        alpha.ValueChanged+=(_,_)=>{if(updating)return;isDefault=false;color.A=(byte)Math.Round(alpha.Value);Publish(readColor:false);};
        hex.TextChanged+=(_,_)=>{if(updating)return;try{if(hex.Text.Length is not(7 or 9)||hex.Text[0]!='#')throw new FormatException();color=(Color)ColorConverter.ConvertFromString(hex.Text);isDefault=false;Publish(editing:true);}catch{error.Text="Enter #RRGGBB or #AARRGGBB.";error.Visibility=Visibility.Visible;validity(false);}};
        ScreenColorSamplerWindow? sampler=null;
        pipette.Click+=(_,_)=>{if(sampler!=null){sampler.Close();return;}sampler=new ScreenColorSamplerWindow{Owner=window};sampler.ColorSampled+=c=>{isDefault=false;color=c;Publish();};sampler.SamplingEnded+=()=>sampler=null;sampler.Show();};
        window.Closed+=(_,_)=>sampler?.Close();refresh=value=>{isDefault=value==null;color=Parse(value,Colors.White);Publish(false);};Publish(false);return body;
    }
    private static Brush Checkerboard()
    {
        var group=new DrawingGroup();group.Children.Add(new GeometryDrawing(Brushes.LightGray,null,new RectangleGeometry(new Rect(0,0,16,16))));foreach(var r in new[]{new Rect(0,0,8,8),new Rect(8,8,8,8)})group.Children.Add(new GeometryDrawing(Brushes.Gray,null,new RectangleGeometry(r)));
        return new DrawingBrush(group){TileMode=TileMode.Tile,Viewport=new Rect(0,0,16,16),ViewportUnits=BrushMappingMode.Absolute,Stretch=Stretch.None};
    }
    internal static Color Parse(string? text,Color fallback){try{return text==null?fallback:(Color)ColorConverter.ConvertFromString(text);}catch{return fallback;}}
    internal static Color FromHsv(double hue,double saturation,double brightness,byte alpha=255)
    {
        double c=brightness*saturation,x=c*(1-Math.Abs(hue/60%2-1)),m=brightness-c;var rgb=((int)(hue/60)%6) switch{0=>(c,x,0d),1=>(x,c,0d),2=>(0d,c,x),3=>(0d,x,c),4=>(x,0d,c),_=>(c,0d,x)};
        return Color.FromArgb(alpha,(byte)Math.Round((rgb.Item1+m)*255),(byte)Math.Round((rgb.Item2+m)*255),(byte)Math.Round((rgb.Item3+m)*255));
    }
    private static void ToHsv(Color color,out double h,out double s,out double v)
    {
        double r=color.R/255d,g=color.G/255d,b=color.B/255d,max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),d=max-min;
        h=d==0?0:max==r?60*((g-b)/d%6):max==g?60*((b-r)/d+2):60*((r-g)/d+4);if(h<0)h+=360;s=max==0?0:d/max;v=max;
    }
}
internal sealed class ColorPlane:FrameworkElement
{
    internal double Hue,Saturation,Brightness;
    internal event Action? Changed;
    internal ColorPlane(){Cursor=Cursors.Cross;MouseLeftButtonDown+=(_,e)=>{Focus();CaptureMouse();Select(e.GetPosition(this));e.Handled=true;};MouseMove+=(_,e)=>{if(IsMouseCaptured)Select(e.GetPosition(this));};MouseLeftButtonUp+=(_,e)=>{if(IsMouseCaptured){ReleaseMouseCapture();e.Handled=true;}};TouchDown+=(_,e)=>{e.TouchDevice.Capture(this);Select(e.GetTouchPoint(this).Position);e.Handled=true;};TouchMove+=(_,e)=>{if(e.TouchDevice.Captured==this){Select(e.GetTouchPoint(this).Position);e.Handled=true;}};TouchUp+=(_,e)=>{e.TouchDevice.Capture(null);e.Handled=true;};KeyDown+=(_,e)=>{double step=Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)?.1:.01;if(e.Key is Key.Left or Key.Right)Saturation=Math.Clamp(Saturation+(e.Key==Key.Left?-step:step),0,1);else if(e.Key is Key.Up or Key.Down)Brightness=Math.Clamp(Brightness+(e.Key==Key.Down?-step:step),0,1);else return;Changed?.Invoke();InvalidateVisual();e.Handled=true;};}
    private void Select(Point p){Saturation=Math.Clamp(p.X/Math.Max(1,ActualWidth),0,1);Brightness=1-Math.Clamp(p.Y/Math.Max(1,ActualHeight),0,1);Changed?.Invoke();InvalidateVisual();}
    protected override void OnRender(DrawingContext dc){var rect=new Rect(RenderSize);dc.DrawRectangle(new LinearGradientBrush(Colors.White,CompactColorPicker.FromHsv(Hue,1,1),0),null,rect);dc.DrawRectangle(new LinearGradientBrush(Colors.Transparent,Colors.Black,90),null,rect);var point=new Point(Saturation*ActualWidth,(1-Brightness)*ActualHeight);dc.DrawEllipse(null,new Pen(Brushes.Black,3),point,6,6);dc.DrawEllipse(null,new Pen(Brushes.White,1.5),point,6,6);}
}
