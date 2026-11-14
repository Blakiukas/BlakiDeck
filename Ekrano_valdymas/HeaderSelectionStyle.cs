using System.Windows;
using System.Windows.Controls;

namespace Ekrano_valdymas;
internal static class HeaderSelectionStyle
{
    internal static Style Create()
    {
        var style=new Style(typeof(Button),(Style)Application.Current.FindResource("ModuleOptionsButtonStyle"));
        style.Setters.Add(new Setter(Control.TemplateProperty,(ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
          <Border x:Name="surface" CornerRadius="7" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="surface" Property="Background" Value="#28FFFFFF"/></Trigger>
            <Trigger Property="Selector.IsSelected" Value="True"><Setter TargetName="surface" Property="Background" Value="#48FFFFFF"/></Trigger>
            <Trigger Property="IsPressed" Value="True"><Setter TargetName="surface" Property="Background" Value="#60FFFFFF"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """)));
        return style;
    }
}
