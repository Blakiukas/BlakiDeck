using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Ekrano_valdymas;
public partial class MainWindow
{
    private bool ColorIsVisible(AccentColorDialog.ColorTarget target)
    {
        bool wide=musicConsoleEnabled&&musicConsole?.IsVisible==true;
        bool player=wide||musicPlayerCard?.IsVisible==true;
        return target switch
        {
            AccentColorDialog.ColorTarget.Music=>player,
            AccentColorDialog.ColorTarget.Knob or AccentColorDialog.ColorTarget.Power=>wide,
            AccentColorDialog.ColorTarget.Base=>CommonMetal.NoSkin,
            AccentColorDialog.ColorTarget.Titles=>UsesColor(dashboardCanvas,"ModuleTitleBrush"),
            AccentColorDialog.ColorTarget.Labels=>UsesColor(dashboardCanvas,"PanelTextBrush"),
            AccentColorDialog.ColorTarget.Clock=>UsesColor(dashboardCanvas,"ClockBrush"),
            AccentColorDialog.ColorTarget.SystemMonitor=>systemMonitorWidget?.IsVisible==true&&!wide,
            AccentColorDialog.ColorTarget.Text=>UsesColor(dashboardCanvas,"PrimaryTextBrush"),
            _=>true
        };
    }
    private static bool UsesColor(DependencyObject node,string key)
    {
        if(node is UIElement element&&!element.IsVisible)return false;
        var brush=Application.Current.TryFindResource(key);
        if(node is OpticallyCenteredText optical&&!optical.SkinMetalText&&ReferenceEquals(optical.TextBrush,brush))return true;
        if(node is TextBlock text&&ReferenceEquals(text.Foreground,brush))return true;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)if(UsesColor(VisualTreeHelper.GetChild(node,i),key))return true;
        return false;
    }
    private Button dashboardColorsButton=null!;
    private void InitializeContextActions()
    {
        var header=new Grid();
        header.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var title=new OpticallyCenteredText{Text="Appearance",CompactLineBox=true,InkAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Stretch};
        foreach(var pair in new[]{(OpticallyCenteredText.TextFontFamilyProperty,"FontFamily"),(OpticallyCenteredText.TextFontSizeProperty,"FontSize"),(OpticallyCenteredText.TextFontWeightProperty,"FontWeight"),(OpticallyCenteredText.TextBrushProperty,"Foreground")})
            title.SetBinding(pair.Item1,new Binding(pair.Item2){Source=appearanceMenuItem});
        header.Children.Add(title);
        dashboardColorsButton=new Button{Content="Colors",Padding=new Thickness(8,2,8,2),Margin=new Thickness(16,0,12,0),Style=uninstallMenuButton.Style};
        dashboardColorsButton.Click+=ChooseAccentColorButton_Click;
        Grid.SetColumn(dashboardColorsButton,1);header.Children.Add(dashboardColorsButton);
        appearanceMenuItem.HeaderTemplate=null;appearanceMenuItem.Header=header;
        ContextActionVisibility(skinOptionsMenu,skinAllTabs,()=>skinAllTabs.IsChecked==true);
        skinAllTabs.Checked+=(_,_)=>RefreshSkinAction();skinAllTabs.Unchecked+=(_,_)=>RefreshSkinAction();
        ContextActionVisibility(appearanceMenuItem,dashboardColorsButton);
        ContextActionVisibility(helpMenuRow,uninstallMenuButton);
    }
    private void RefreshSkinAction()=>skinAllTabs.Visibility=RowActive(skinOptionsMenu)||skinAllTabs.IsChecked==true?Visibility.Visible:Visibility.Hidden;
    private static bool RowActive(MenuItem row)=>row.IsMouseOver||row.IsHighlighted||row.IsKeyboardFocusWithin||row.IsSubmenuOpen;
    private static void ContextActionVisibility(MenuItem row,FrameworkElement action,Func<bool>? keepVisible=null)
    {
        void Refresh()=>action.Visibility=RowActive(row)||keepVisible?.Invoke()==true?Visibility.Visible:Visibility.Hidden;
        // Hidden reserves the measured width. Updates happen only on interaction.
        foreach(var property in new[]{UIElement.IsMouseOverProperty,MenuItem.IsHighlightedProperty,UIElement.IsKeyboardFocusWithinProperty,MenuItem.IsSubmenuOpenProperty})
            DependencyPropertyDescriptor.FromProperty(property,typeof(MenuItem)).AddValueChanged(row,(_,_)=>Refresh());
        row.Loaded+=(_,_)=>Refresh();Refresh();
    }
}
