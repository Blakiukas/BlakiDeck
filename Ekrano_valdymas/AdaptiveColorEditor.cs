using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static Ekrano_valdymas.SystemMonitorWidget;
namespace Ekrano_valdymas;

internal sealed class AdaptiveColorEditor:SettingsWindow
{
    internal AdaptiveColorEditor(Window owner,MetricKind kind,MetricPresentation original,Action<MetricPresentation> apply)
    {
        Owner=owner;ShowInTaskbar=false;Title="Adaptive colors — "+GetMetricLabel(kind);Width=480;Height=360;MinWidth=430;MinHeight=300;WindowStartupLocation=WindowStartupLocation.CenterOwner;MacroUi.ApplyTheme(this);
        var current=original;bool saved=false,previewed=false;
        void Preview(AdaptiveColorRule rule,MetricKind channel){var rules=new Dictionary<string,AdaptiveColorRule>(current.AdaptiveRules??new()){[channel.ToString()]=rule};current=current with{AdaptiveRules=rules};previewed=true;apply(current);}
        Closed+=(_,_)=>{if(!saved&&previewed)apply(original);};
        var root=new DockPanel{Margin=new Thickness(18)};Content=root;
        var footer=new Grid{Margin=new Thickness(0,16,0,0)};footer.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});footer.ColumnDefinitions.Add(new(){Width=GridLength.Auto});footer.ColumnDefinitions.Add(new(){Width=GridLength.Auto});DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        Action flush=()=>{};
        var save=MacroUi.Button("Save",(_,_)=>{flush();});save.Name="AdaptiveSave";save.Width=90;Grid.SetColumn(save,2);footer.Children.Add(save);
        var cancel=MacroUi.Button("Cancel",(_,_)=>Close());cancel.Width=90;Grid.SetColumn(cancel,1);footer.Children.Add(cancel);
        Action reset=()=>{};var defaults=MacroUi.Button("Default",(_,_)=>reset());defaults.Width=90;defaults.HorizontalAlignment=HorizontalAlignment.Left;footer.Children.Add(defaults);
        var body=new StackPanel();var scroll=MacroUi.Scroll(body);AutoFitScroll=scroll;root.Children.Add(scroll);
        var channels=AdaptiveSensorColors.Channels(kind);var selector=new ComboBox{Name="AdaptiveChannel",Visibility=channels.Length>1?Visibility.Visible:Visibility.Collapsed,Margin=new Thickness(0,0,0,10)};
        foreach(var channel in channels)selector.Items.Add(new ComboBoxItem{Content=channel==MetricKind.NetworkDownload?"DL":channel==MetricKind.NetworkUpload?"UP":GetMetricLabel(channel),Tag=channel});selector.SelectedIndex=0;body.Children.Add(selector);
        var fields=new StackPanel();body.Children.Add(fields);
        void Build()
        {
            fields.Children.Clear();var channel=(MetricKind)((ComboBoxItem)selector.SelectedItem).Tag;
            var domain=AdaptiveSensorColors.TechnicalRange(channel,current);string unit=AdaptiveSensorColors.Unit(channel,current);
            var baseline=AdaptiveSensorColors.FixedColor(channel,current)??SensorFactoryDefaults.ForMetric(channel).Color??"#FF1ED760";
            var rule=current.AdaptiveRules?.GetValueOrDefault(channel.ToString())??new();
            double? maximum=rule.RangeMaximum??domain.Maximum;
            var stops=(rule.Stops??[]).OrderBy(s=>s.Threshold).ToArray();
            bool haveRange=domain.Minimum.HasValue&&domain.Maximum.HasValue&&domain.Maximum>domain.Minimum;
            double?[] edges=stops.Length>0?stops.Select(s=>(double?)s.Threshold).ToArray():haveRange?[domain.Minimum+(domain.Maximum-domain.Minimum)/3,domain.Minimum+2*(domain.Maximum-domain.Minimum)/3]:[null,null];
            var colors=rule.Below?stops.Select(s=>s.Color).Concat([rule.NormalColor??baseline]).ToArray():new[]{rule.NormalColor??baseline}.Concat(stops.Select(s=>s.Color)).ToArray();
            if(stops.Length==0)colors=[baseline,"#FFFFCC33","#FFFF5544"];
            bool normalEdited=rule.NormalColor!=null||rule.Below;
            double scaleMin=domain.Minimum??0;double fallback=channel==MetricKind.CpuPower?400:unit=="W"?600:unit=="°C"?120:unit=="MHz"?10000:unit=="RPM"?10000:unit=="V"?5:100;
            double scaleMax=domain.Maximum??Math.Max(fallback,Math.Max(maximum??0,edges.Where(n=>n.HasValue).Select(n=>n!.Value).DefaultIfEmpty(0).Max()));
            double step=unit=="V"?.01:1;bool syncing=false;var sliders=new Slider[edges.Length+1];var inputs=new TextBox[edges.Length+1];var error=new TextBlock{Foreground=Brushes.OrangeRed,TextWrapping=TextWrapping.Wrap,Visibility=Visibility.Collapsed};
            bool valuesValid=true,hysteresisValid=true;double hysteresis=rule.Hysteresis;
            string Number(double? n)=>n?.ToString("0.##",CultureInfo.CurrentCulture)??"—";
            void ReflectRanges(){syncing=true;try{for(int i=0;i<sliders.Length;i++)if(sliders[i]!=null)sliders[i].Value=Math.Clamp((i==edges.Length?maximum:edges[i])??scaleMin,scaleMin,scaleMax);}finally{syncing=false;}}
            (double Min,double Max) Bounds(int i)=>((i==0?scaleMin:edges[i-1]??scaleMin)+step,(i==edges.Length?scaleMax:(i+1==edges.Length?maximum:edges[i+1])??scaleMax)-(i==edges.Length?0:step));
            void Slide(int index,double value)
            {
                if(syncing)return;
                var bounds=Bounds(index);if(bounds.Max<bounds.Min)return;
                var values=edges.Concat(new[]{maximum}).ToArray();values[index]=Math.Clamp(Math.Round(value/step)*step,bounds.Min,bounds.Max);values[^1]??=scaleMax;
                for(int j=0;j<values.Length;j++)if(values[j]==null){int next=j+1;while(next<values.Length&&values[next]==null)next++;double left=j==0?scaleMin:values[j-1]!.Value;values[j]=left+(values[next]!.Value-left)/(next-j+1);}
                syncing=true;try{for(int j=0;j<edges.Length;j++)edges[j]=values[j];maximum=values[^1];for(int j=0;j<inputs.Length;j++)inputs[j].Text=Number(values[j]);}finally{syncing=false;}Commit();
            }
            void Commit()
            {
                bool empty=edges.All(n=>!n.HasValue)&&!maximum.HasValue;
                valuesValid=empty||edges.All(n=>n.HasValue&&double.IsFinite(n.Value))&&edges.Zip(edges.Skip(1),(a,b)=>a<b).All(b=>b)&&(!domain.Minimum.HasValue||edges[0]>domain.Minimum)&&maximum.HasValue&&double.IsFinite(maximum.Value)&&edges[^1]<maximum&&(!domain.Maximum.HasValue||maximum<=domain.Maximum)&&maximum<=scaleMax;
                save.IsEnabled=selector.IsEnabled=valuesValid&&hysteresisValid;
                error.Text=!valuesValid?"Enter increasing boundaries within the available technical range.":!hysteresisValid?"Flicker suppression must be nonnegative.":"";error.Visibility=error.Text.Length==0?Visibility.Collapsed:Visibility.Visible;
                ReflectRanges();RequestContentFit();
                if(save.IsEnabled)Preview(new AdaptiveColorRule(empty?[]:edges.Select((edge,i)=>new AdaptiveColorStop(edge!.Value,colors[i+1])).ToArray(),false,hysteresis,rule.UnavailableColor,normalEdited?colors[0]:null,maximum),channel);
            }
            flush=()=>{Commit();if(save.IsEnabled){saved=true;Close();}};
            for(int i=0;i<=edges.Length;i++)
            {
                int index=i;var row=new Grid{Height=62};row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=new GridLength(96)});row.ColumnDefinitions.Add(new(){Width=new GridLength(88)});
                fields.Children.Add(new Border{BorderBrush=new SolidColorBrush(Color.FromRgb(53,55,58)),BorderThickness=new Thickness(0,0,0,1),Child=row});
                var track=new Grid{Margin=new Thickness(4,4,14,4)};track.RowDefinitions.Add(new(){Height=GridLength.Auto});track.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});row.Children.Add(track);
                var limits=new DockPanel();limits.Children.Add(new TextBlock{Text=Number(scaleMin),FontSize=12});limits.Children.Add(new TextBlock{Text=Number(scaleMax)+" "+unit,FontSize=12,HorizontalAlignment=HorizontalAlignment.Right});track.Children.Add(limits);
                var slider=new Slider{Name="AdaptiveBoundarySlider",Minimum=scaleMin,Maximum=scaleMax,Value=(i==edges.Length?maximum:edges[i])??scaleMin,Height=28,SmallChange=step,LargeChange=step*10,IsMoveToPointEnabled=true,ToolTip=domain.Maximum.HasValue?domain.Source:"Editing scale; technical maximum unavailable"};slider.SetResourceReference(StyleProperty,"DashboardSlider");Grid.SetRow(slider,1);track.Children.Add(slider);sliders[i]=slider;
                var spin=Numeric(Number(i==edges.Length?maximum:edges[i]),i==edges.Length?"AdaptiveTechnicalMax":"AdaptiveThreshold",text=>{if(syncing)return;double? n=double.TryParse(text,NumberStyles.Float,CultureInfo.CurrentCulture,out var value)&&double.IsFinite(value)?value:null;if(index==edges.Length)maximum=n;else edges[index]=n;Commit();},()=>index==edges.Length?maximum:edges[index],scaleMin,scaleMax,()=>Bounds(index),step);inputs[i]=spin.Input;Grid.SetColumn(spin.Panel,1);row.Children.Add(spin.Panel);
                slider.ValueChanged+=(_,e)=>Slide(index,e.NewValue);
                var swatch=new Border{Width=24,Height=24,CornerRadius=new CornerRadius(4),Background=ColorBrush(colors[i])};
                var button=MacroUi.Button("",(_,_)=>{var beforePicker=current;var result=CompactColorPicker.Create(this,colors[index],color=>{var selected=color??(index==0?baseline:index==colors.Length-1?"#FFFF5544":"#FFFFCC33");if(color!=null&&CompactColorPicker.Parse(selected,Colors.White)==CompactColorPicker.Parse(colors[index],Colors.White))return;colors[index]=selected;if(index==0)normalEdited=color!=null;swatch.Background=ColorBrush(colors[index]);Commit();}).ShowDialog();if(result!=true){current=beforePicker;apply(current);Build();}});button.Name="AdaptiveRangeColor";button.MinHeight=0;button.Height=34;button.Width=72;button.Padding=new Thickness(5);button.ToolTip="Range color";
                var choice=new Grid();choice.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});choice.ColumnDefinitions.Add(new(){Width=new GridLength(18)});choice.Children.Add(swatch);var chevron=Chevron(false);Grid.SetColumn(chevron,1);choice.Children.Add(chevron);button.Content=choice;Grid.SetColumn(button,2);row.Children.Add(button);
            }
            ReflectRanges();
            var flicker=new Grid{Margin=new Thickness(4,14,4,0)};flicker.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});flicker.ColumnDefinitions.Add(new(){Width=new GridLength(106)});fields.Children.Add(flicker);
            flicker.Children.Add(new TextBlock{Text="Flicker suppression · "+unit,VerticalAlignment=VerticalAlignment.Center});
            var h=Numeric(hysteresis.ToString(CultureInfo.CurrentCulture),"AdaptiveHysteresis",text=>{hysteresisValid=double.TryParse(text,out var value)&&double.IsFinite(value)&&value>=0;hysteresis=value;Commit();},()=>hysteresis,0,null,step:.1);Grid.SetColumn(h.Panel,1);flicker.Children.Add(h.Panel);fields.Children.Add(error);
            reset=()=>{Preview(new AdaptiveColorRule(),channel);Build();if(haveRange){var generated=new AdaptiveColorRule([new(domain.Minimum!.Value+(domain.Maximum!.Value-domain.Minimum.Value)/3,"#FFFFCC33"),new(domain.Minimum.Value+2*(domain.Maximum.Value-domain.Minimum.Value)/3,"#FFFF5544")]);Preview(generated,channel);}};
            selector.IsEnabled=save.IsEnabled=true;RequestContentFit();
        }
        selector.SelectionChanged+=(_,_)=>Build();Build();
    }
    private static Brush ColorBrush(string color)=>new SolidColorBrush(CompactColorPicker.Parse(color,Colors.White));
    private static System.Windows.Shapes.Path Chevron(bool up)=>new(){Data=Geometry.Parse(up?"M 0,4 L 4,0 L 8,4":"M 0,0 L 4,4 L 8,0"),Stroke=Brushes.White,StrokeThickness=1.3,Width=8,Height=4,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
    private static (Grid Panel,TextBox Input) Numeric(string initial,string name,Action<string> change,Func<double?> value,double? minimum,double? maximum,Func<(double Min,double Max)>? bounds=null,double step=1)
    {
        var panel=new Grid{ClipToBounds=true,Width=88,Height=34,Margin=new Thickness(0,0,8,0)};
        var input=new TextBox{ClipToBounds=true,SelectionBrush=new SolidColorBrush(Color.FromRgb(105,75,35)),MinWidth=0,Width=88,MaxLength=12,HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden,Name=name,Text=initial=="—"?"":initial,VerticalContentAlignment=VerticalAlignment.Center,Padding=new Thickness(8,0,3,0)};panel.Children.Add(input);
        input.PreviewMouseWheel+=(_,e)=>{var range=bounds?.Invoke()??(minimum??0,maximum??double.MaxValue);if(range.Item2>=range.Item1)input.Text=Math.Clamp((value()??minimum??0)+Math.Sign(e.Delta)*step,range.Item1,range.Item2).ToString("0.##",CultureInfo.CurrentCulture);e.Handled=true;};
        input.TextChanged+=(_,_)=>change(input.Text);return(panel,input);
    }
    internal static Button Pencil(Action action)
    {
        var button=MacroUi.Button("",(_,e)=>{e.Handled=true;action();});button.Width=26;button.Height=26;button.MinWidth=0;button.MinHeight=0;button.Margin=new Thickness(5,0,0,0);button.VerticalAlignment=VerticalAlignment.Center;button.Padding=new Thickness(4);button.ToolTip="Edit adaptive colors";
        var icon=new OpticallyCenteredText{Text="\uE70F",TextFontFamily=new FontFamily("Segoe MDL2 Assets"),TextFontSize=16,TextBrush=AdaptiveSensorColors.PencilBrush};button.Content=icon;
        button.SizeChanged+=(_,_)=>icon.TextFontSize=Math.Max(1,Math.Min(button.ActualWidth,button.ActualHeight)*.65);return button;
    }
}
