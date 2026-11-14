using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Data;
namespace Ekrano_valdymas;

internal static class PlaylistAppearance
{
    internal static double CapSize(double height,FontFamily? family=null)
    {
        var face=new Typeface(family??new FontFamily("Segoe UI"),FontStyles.Normal,FontWeights.Normal,FontStretches.Normal);
        if(!face.TryGetGlyphTypeface(out var glyph))throw new InvalidOperationException("Playlist font unavailable");
        return height/glyph.CapsHeight;
    }
    private sealed class RowNumber : IValueConverter
    {
        public object Convert(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture)=>((int)value+1).ToString()+((parameter as string)=="dot"?".":"");
        public object ConvertBack(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture)=>Binding.DoNothing;
    }
    internal static string Summary(IEnumerable<LocalPlayback.Track> source)
    {
        var tracks=source.ToArray();double seconds=tracks.Sum(t=>Math.Max(0,t.Seconds));
        var duration=TimeSpan.FromSeconds(seconds);
        string time=seconds>=3600?$"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}":$"{(int)duration.TotalMinutes}:{duration.Seconds:00}";
        if(tracks.Any(t=>t.Seconds<=0))time+=" + ?";
        return $"{tracks.Length} {(tracks.Length==1?"track":"tracks")}  ·  Total {time}";
    }
    internal static void Apply(ListBox list,bool wide,PlaylistSettings? settings=null)
    {
        ApplyChrome(list,true);
        list.ItemTemplate=Row(wide,settings??new());
        list.AlternationCount=int.MaxValue;
        ScrollViewer.SetVerticalScrollBarVisibility(list,ScrollBarVisibility.Auto);
        list.SetResourceReference(Control.ForegroundProperty,"PanelTextBrush");
    }
    internal static void ApplyChrome(ListBox list,bool playlist=false)
    {
        string resources=playlist?Resources.Replace("</ControlTemplate.Triggers>","<DataTrigger Binding=\"{Binding IsCurrentTrack}\" Value=\"True\"><Setter TargetName=\"Row\" Property=\"Background\" Value=\"Transparent\"/></DataTrigger></ControlTemplate.Triggers>"):Resources;
        list.Resources=(ResourceDictionary)XamlReader.Parse(resources);
        list.ItemContainerStyle=(Style)list.Resources[typeof(ListBoxItem)];
    }
    private static DataTemplate Row(bool wide,PlaylistSettings settings)
    {
        double size=settings.Size(wide);double ratio=size/(wide?CapSize(16):17);
        var face=new Typeface(new FontFamily("Segoe UI"),FontStyles.Normal,FontWeights.Normal,FontStretches.Normal);
        face.TryGetGlyphTypeface(out var glyph);
        double titleHeight=size*(glyph?.Height??1.4);
        bool modern=settings.Layout!="Winamp style";
        var template=new DataTemplate(typeof(LocalPlayback.Track));
        var border=new FrameworkElementFactory(typeof(Border));border.Name="TrackRow";border.SetValue(FrameworkElement.HeightProperty,titleHeight*(modern?1.7:1)+settings.Gap);
        border.SetValue(Border.BorderThicknessProperty,new Thickness(0,0,0,1));border.SetValue(Border.BorderBrushProperty,new SolidColorBrush(Color.FromArgb(20,190,175,150)));
        var row=new FrameworkElementFactory(typeof(DockPanel));border.AppendChild(row);
        int textIndex=0;
        var playing=new DataTrigger{Binding=new Binding("IsCurrentTrack"),Value=true};
        var playingBrush=(Brush)new BrushConverter().ConvertFromString(wide||!MusicFinish.IsLight?"#FFD78A":"#154E7D")!;
        template.Triggers.Add(playing);
        FrameworkElementFactory Text(string property,double size,double width,HorizontalAlignment alignment,string? color=null)
        {
            var t=new FrameworkElementFactory(typeof(OpticallyCenteredText));t.SetBinding(OpticallyCenteredText.TextProperty,new Binding(property));
            t.Name="TrackText"+(textIndex++);playing.Setters.Add(new Setter(OpticallyCenteredText.TextBrushProperty,playingBrush,t.Name));
            t.SetValue(OpticallyCenteredText.TextFontFamilyProperty,new FontFamily("Segoe UI"));t.SetValue(OpticallyCenteredText.TextFontWeightProperty,FontWeights.Normal);
            t.SetValue(OpticallyCenteredText.TextFontSizeProperty,size);t.SetValue(OpticallyCenteredText.InkAlignmentProperty,alignment);
            if(width>0)t.SetValue(FrameworkElement.WidthProperty,width*ratio);
            string? ink=property=="PlaybackMark"?color:settings.TextColor??color;
            if(ink==null)t.SetResourceReference(OpticallyCenteredText.TextBrushProperty,"PanelTextBrush");else t.SetValue(OpticallyCenteredText.TextBrushProperty,(Brush)new BrushConverter().ConvertFromString(ink)!);
            t.AddHandler(FrameworkElement.LoadedEvent,new RoutedEventHandler((s,_)=>{((OpticallyCenteredText)s).Ellipsis=true;((OpticallyCenteredText)s).InvalidateVisual();}));
            return t;
        }
        var duration=Text("DurationText",(wide?CapSize(15):14)*ratio,62,HorizontalAlignment.Right,wide?"#BEA989":null);duration.SetValue(DockPanel.DockProperty,Dock.Right);row.AppendChild(duration);
        var number=Text("",size,0,HorizontalAlignment.Left,wide?"#BEA989":null);
        number.SetValue(FrameworkElement.WidthProperty,size*(modern?1.7:2));
        number.SetBinding(OpticallyCenteredText.TextProperty,new Binding("(ItemsControl.AlternationIndex)"){RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(ListBoxItem),1),Converter=new RowNumber(),ConverterParameter=modern?"plain":"dot"});
        if(!modern){
            number.SetValue(DockPanel.DockProperty,Dock.Left);row.AppendChild(number);
            row.AppendChild(Text("ArtistTitle",size,0,HorizontalAlignment.Left,wide?"#BEA989":null));
        }
        else {
            var numberStack=new FrameworkElementFactory(typeof(StackPanel));numberStack.SetValue(DockPanel.DockProperty,Dock.Left);numberStack.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);row.AppendChild(numberStack);
            number.SetValue(FrameworkElement.HeightProperty,titleHeight);numberStack.AppendChild(number);
            var spacer=new FrameworkElementFactory(typeof(Border));spacer.Name="NumberArtistSpace";spacer.SetValue(FrameworkElement.HeightProperty,titleHeight*.7);numberStack.AppendChild(spacer);
            var stack=new FrameworkElementFactory(typeof(StackPanel));stack.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);row.AppendChild(stack);
            var title=Text("Title",size,0,HorizontalAlignment.Left,wide?"#BEA989":null);title.SetValue(FrameworkElement.HeightProperty,titleHeight);stack.AppendChild(title);
            var artist=Text("Artist",size*.7,0,HorizontalAlignment.Left,wide?"#BEA989":null);artist.SetValue(FrameworkElement.HeightProperty,titleHeight*.7);artist.SetValue(UIElement.OpacityProperty,.6);stack.AppendChild(artist);
            var empty=new DataTrigger{Binding=new Binding("Artist"),Value=""};empty.Setters.Add(new Setter(UIElement.VisibilityProperty,Visibility.Collapsed,artist.Name));empty.Setters.Add(new Setter(UIElement.VisibilityProperty,Visibility.Collapsed,"NumberArtistSpace"));empty.Setters.Add(new Setter(FrameworkElement.HeightProperty,titleHeight+settings.Gap,"TrackRow"));template.Triggers.Add(empty);
        }
        template.VisualTree=border;return template;
    }
    private const string Resources="""
    <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
      <Style TargetType="ListBoxItem">
        <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
        <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
        <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ListBoxItem">
          <Border x:Name="Row" Background="Transparent" CornerRadius="3" Padding="5,0">
            <ContentPresenter HorizontalAlignment="Stretch"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Row" Property="Background" Value="#12FFFFFF"/></Trigger>
            <Trigger Property="IsSelected" Value="True"><Setter TargetName="Row" Property="Background" Value="#28E5AD61"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate></Setter.Value></Setter>
      </Style>
      <Style TargetType="ScrollBar">
        <Setter Property="Width" Value="9"/><Setter Property="Margin" Value="6,0,0,0"/>
        <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar">
          <Grid Background="Transparent">
            <Border Width="3" Background="#18FFFFFF" CornerRadius="1.5" HorizontalAlignment="Center"/>
            <Track x:Name="PART_Track" IsDirectionReversed="True" Orientation="Vertical">
              <Track.DecreaseRepeatButton><RepeatButton Command="{x:Static ScrollBar.PageUpCommand}" Opacity="0"/></Track.DecreaseRepeatButton>
              <Track.Thumb><Thumb MinHeight="30"><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Width="5" Background="#85B9B5AE" CornerRadius="2.5"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
              <Track.IncreaseRepeatButton><RepeatButton Command="{x:Static ScrollBar.PageDownCommand}" Opacity="0"/></Track.IncreaseRepeatButton>
            </Track>
          </Grid>
        </ControlTemplate></Setter.Value></Setter>
      </Style>
    </ResourceDictionary>
    """;
}
