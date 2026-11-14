using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ekrano_valdymas;

// A single header control keeps the hit target and gear consistent in every module.
public sealed class ModuleOptionsButton : Button
{
    public ModuleOptionsButton()
    {
        Width = 38; Height = 36; Padding = new Thickness(0);
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Center;
        Tag = "NoWindowDrag"; ToolTip = "Module options";
        SetResourceReference(StyleProperty, "ModuleOptionsButtonStyle");
        Content = new OpticallyCenteredText { Width = 20, Height = 20, Text = "\uE713", TextFontFamily = new FontFamily("Segoe MDL2 Assets"), TextFontSize = 20, IsHitTestVisible = false };
        ((OpticallyCenteredText)Content).SetResourceReference(OpticallyCenteredText.TextBrushProperty, "PanelTextBrush");
        System.Windows.Automation.AutomationProperties.SetName(this, "Module options");
        Click += (_, e) => {
            e.Handled = true;
            OpenOptions();
        };
    }
    internal void OpenOptions()
    {
        for (DependencyObject? p = VisualTreeHelper.GetParent(this); p != null; p = VisualTreeHelper.GetParent(p))
            if (p is UserControl module) { (Window.GetWindow(this) as MainWindow)?.OpenModuleOptions(module, this); break; }
    }
}
