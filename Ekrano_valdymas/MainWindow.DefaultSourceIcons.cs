using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ekrano_valdymas;
public partial class MainWindow
{
    private bool CurrentTabHasPlayer=>musicConsoleEnabled||selectedModules.Any(m=>m is ModuleKind.Music or ModuleKind.MusicPlayerCard);
    private void UpdateDefaultSourceIcon()
    {
        UpdateHudVisualizerMedia();
        if(dashboardLayoutsLoaded&&CurrentTabHasPlayer&&quickLaunchBar.UsesDefaultSourceIcon(activeDashboardLayoutTab))
            quickLaunchBar.SetDefaultSourceIcon(activeDashboardLayoutTab,true,selectedMusicService);
    }
    private void AddDefaultSourceIconOption(ItemsControl menu)
    {
        var automatic=new MenuItem{Name="DefaultSourceIcons",IsCheckable=true,StaysOpenOnClick=true,
            IsChecked=quickLaunchBar.UsesDefaultSourceIcon(activeDashboardLayoutTab)};
        var label=new OpticallyCenteredText{Name="DefaultSourceIconsLabel",Text="Auto icons",TextFontFamily=ContextMenu.FontFamily,TextFontWeight=FontWeights.SemiBold,
            InkAlignment=HorizontalAlignment.Left,CompactLineBox=true,MinHeight=24,ToolTip=new ToolTip{Content=new TextBlock{Text="Changes this tab's icon with the music source.\nChoosing your own tab icon turns this off.",TextWrapping=TextWrapping.Wrap,MaxWidth=320}}};
        label.SetBinding(OpticallyCenteredText.TextFontSizeProperty,new System.Windows.Data.Binding("FontSize"){Source=automatic});
        automatic.Header=label;
        automatic.Click+=(_,_)=>quickLaunchBar.SetDefaultSourceIcon(activeDashboardLayoutTab,automatic.IsChecked,selectedMusicService);
        automatic.Loaded+=(_,_)=>automatic.IsChecked=quickLaunchBar.UsesDefaultSourceIcon(activeDashboardLayoutTab);
        menu.Items.Add(new Separator());
        menu.Items.Add(automatic);
    }
}
