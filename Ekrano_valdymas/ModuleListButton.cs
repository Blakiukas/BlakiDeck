using System.Windows;
using System.Windows.Controls;
namespace Ekrano_valdymas;
public sealed class ModuleListButton : Button
{
    public ModuleListButton()
    {
        Width=38;Height=36;Padding=new Thickness(0);VerticalAlignment=VerticalAlignment.Center;
        Tag="NoWindowDrag";ToolTip="Switch icons / list";
        SetResourceReference(StyleProperty,"ModuleOptionsButtonStyle");
        Content=MacroLibraryCard.ToolbarIcon("List");
        System.Windows.Automation.AutomationProperties.SetName(this,"Switch icons / list");
    }
}
