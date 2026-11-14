using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Ekrano_valdymas;
internal static class AirthingsEditorVisuals
{
    internal static Style DisclosureStyle() => (Style)System.Windows.Markup.XamlReader.Parse("""
    <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Expander">
      <Setter Property="Foreground" Value="White"/>
      <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Expander">
        <Grid><Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
          <Grid><Grid.ColumnDefinitions><ColumnDefinition Width="32"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
            <ToggleButton Width="30" Height="32" VerticalAlignment="Top" IsChecked="{Binding IsExpanded,Mode=TwoWay,RelativeSource={RelativeSource TemplatedParent}}" ToolTip="Expand / collapse">
              <ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="Transparent"><Path x:Name="Arrow" Data="M1,1 L8,8 L15,1" Width="16" Height="9" Stroke="White" StrokeThickness="2" StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round" HorizontalAlignment="Center" VerticalAlignment="Center" RenderTransformOrigin="0.5,0.5"><Path.RenderTransform><RotateTransform Angle="-90"/></Path.RenderTransform></Path></Border><ControlTemplate.Triggers><Trigger Property="IsChecked" Value="True"><Setter TargetName="Arrow" Property="RenderTransform"><Setter.Value><RotateTransform Angle="0"/></Setter.Value></Setter></Trigger></ControlTemplate.Triggers></ControlTemplate></ToggleButton.Template>
            </ToggleButton>
            <ContentPresenter Grid.Column="1" ContentSource="Header" VerticalAlignment="Center"/>
          </Grid>
          <ContentPresenter x:Name="Body" Grid.Row="1" ContentSource="Content" Visibility="Collapsed"/>
        </Grid><ControlTemplate.Triggers><Trigger Property="IsExpanded" Value="True"><Setter TargetName="Body" Property="Visibility" Value="Visible"/></Trigger></ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>
    """);
    internal static FrameworkElement SensorIcon(string? sensor,double size=24,Brush? brush=null)
    {
        string canonical=AirthingsStatusRules.Canonical(sensor??"");
        string label=canonical switch {"co2"=>"CO₂","voc"=>"VOC","radonshorttermavg" or "radonlongtermavg" or "radon"=>"Rn","humidity"=>"%","temp"=>"°C","pressure"=>"hPa","mold"=>"M","pm25"=>"PM",_=>"A"};
        var icon=new Border {Width=size,Height=size,CornerRadius=new CornerRadius(5),BorderBrush=brush??Brushes.Silver,BorderThickness=new Thickness(1),ToolTip=string.IsNullOrEmpty(sensor)?"Automatic":AirthingsMini.SensorLabel(sensor),Tag="AirthingsSensorIcon"};
        icon.Child=new OpticallyCenteredText {Text=label,TextFontFamily=new FontFamily("Segoe UI"),TextFontSize=size*.48,TextFontWeight=FontWeights.SemiBold,TextBrush=brush??Brushes.White,FitToBox=true,CenterSymbolsOnCapReference=true};
        string? shape=canonical switch
        {
            "humidity"=>"M12,2 C10,6 5,11 5,15 A7,7 0 0 0 19,15 C19,11 14,6 12,2 Z M8,15 C8,18 10,19 12,19",
            "temp"=>"M9,14 L9,5 A3,3 0 0 1 15,5 L15,14 A5,5 0 1 1 9,14 M12,7 L12,18 M10,18 L14,18",
            "pressure"=>"M4,19 A10,10 0 1 1 20,19 M12,12 L18,6 M6,12 L4,12 M12,6 L12,4 M18,12 L20,12 M7,20 L17,20",
            "mold"=>"M12,5 L12,19 M5,12 L19,12 M7,7 L17,17 M7,17 L17,7 M12,2 A3,3 0 1 0 12,8 A3,3 0 1 0 12,2 M3,10 A2,2 0 1 0 3,14 A2,2 0 1 0 3,10 M19,10 A2,2 0 1 0 19,14 A2,2 0 1 0 19,10",
            _=>null
        };
        if(shape!=null)icon.Child=new Viewbox{Margin=new Thickness(3),Child=new System.Windows.Shapes.Path{Data=Geometry.Parse(shape),Stroke=brush??Brushes.White,StrokeThickness=1.6,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Stretch=Stretch.Uniform,Width=22,Height=22}};
        return icon;
    }
}
