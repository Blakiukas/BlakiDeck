using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Ekrano_valdymas;
public partial class MainWindow
{
    private bool panelMetalLight;
    private bool panelNoSkin;
    private bool panelMetalInitialized;
    private string panelFinish="";
    private void UpdateFinishChecks(){
        darkMusicFinishMenuItem.IsChecked=MusicFinish.Name=="dark";
        powderBlackMusicFinishMenuItem.IsChecked=MusicFinish.Name=="powder-black";
        rustIronMusicFinishMenuItem.IsChecked=MusicFinish.Name=="rust-iron";brushedLightMusicFinishMenuItem.IsChecked=MusicFinish.Name=="brushed-light";
        carbonMusicFinishMenuItem.IsChecked=MusicFinish.Name=="carbon-fiber";militarityMusicFinishMenuItem.IsChecked=MusicFinish.Name=="militarity";
        lightMusicFinishMenuItem.IsChecked=MusicFinish.Name=="light";
    }
    private void BackgroundTransparency_Changed(object sender,RoutedPropertyChangedEventArgs<double> e)
    {
        if(ContextMenu?.IsOpen!=true)return;
        backgroundTransparency=Math.Clamp(e.NewValue,0,100);
        backgroundTransparencyValue.Text=$"{backgroundTransparency:0}%";
        ApplyBaseColor(baseColor);
        SaveWindowSettings();
    }
    private void PanelMetal_Click(object sender,RoutedEventArgs e)
    {
        string choice=((MenuItem)sender).Tag as string??"dark";
        var settings=PlayerSkinLights.Switch(CaptureTabPlayer(),choice);
        foreach(int tab in SkinTargetTabs())if(dashboardLayouts[tab]!=null)
        {
            var stored=dashboardLayouts[tab].Player??new TabPlayerSettings();
            dashboardLayouts[tab].Player=tab==activeDashboardLayoutTab?settings:PlayerSkinLights.Switch(stored,choice);
        }
        SelectTabConsole(settings);UpdateMusicConsoleVisibility();SaveWindowSettings();
    }
    private void ApplyPanelMetal(bool light,bool noSkin=false)=>ApplyPanelMetal(light,noSkin,false);
    private void ApplyPanelMetal(bool light,bool noSkin,bool deferSurfaceRefresh)
    {
        // Clicking an already selected checkable command must keep it selected.
        darkPanelMetalMenu.IsChecked=!noSkin&&!light&&MusicFinish.Name=="dark";
        powderBlackPanelMetalMenu.IsChecked=!noSkin&&MusicFinish.Name=="powder-black";
        rustIronPanelMetalMenu.IsChecked=!noSkin&&MusicFinish.Name=="rust-iron";brushedLightPanelMetalMenu.IsChecked=!noSkin&&MusicFinish.Name=="brushed-light";
        carbonPanelMetalMenu.IsChecked=!noSkin&&MusicFinish.Name=="carbon-fiber";militarityPanelMetalMenu.IsChecked=!noSkin&&MusicFinish.Name=="militarity";
        lightPanelMetalMenu.IsChecked=!noSkin&&MusicFinish.Name=="light";
        noPanelSkinMenu.IsChecked=noSkin;
        // Every tab restores its finish. Reassigning the same shared resources
        // invalidates the entire visible tree and regenerates tab icon artwork.
        if (panelMetalInitialized && panelFinish==MusicFinish.Name && panelMetalLight == light && panelNoSkin == noSkin && CommonMetal.IsLight == (light&&!noSkin) && CommonMetal.NoSkin == noSkin) return;
        panelMetalInitialized=true;panelFinish=MusicFinish.Name;
        panelMetalLight=light;panelNoSkin=noSkin;
        CommonMetal.SetNoSkin(noSkin);
        ApplyPanelMetalResources(light&&!noSkin);
        quickLaunchBar?.RefreshTabVisuals();
        systemMonitorWidget?.RefreshPanelPalette();
        // A tab switch still has the outgoing module tree attached here. Its
        // surfaces would rasterize the incoming finish at obsolete bounds.
        // UpdateMusicConsoleVisibility refreshes the final tree and viewport.
        if(!deferSurfaceRefresh)CommonMetal.RefreshVisibleTree(this);
        quickLaunchBar?.RefreshClockColorResource();
    }
    internal static void ApplyPanelMetalResources(bool light)
    {
        CommonMetal.SetFinish(light);
        Application.Current.Resources["PanelMetalBrush"]=CommonMetal.DefaultBrush;
        Color textColor=light?Color.FromRgb(30,31,31):Color.FromRgb(240,239,235);
        // Keep bound neutral sensor values in sync immediately, without waiting for telemetry.
        if(Application.Current.Resources["PanelTextBrush"] is SolidColorBrush textBrush && !textBrush.IsFrozen) textBrush.Color=textColor;
        else Application.Current.Resources["PanelTextBrush"]=new SolidColorBrush(textColor);
        Application.Current.Resources["PanelDividerBrush"]=new SolidColorBrush(light?Color.FromArgb(95,20,20,20):Color.FromArgb(48,255,255,255));
        CommonMetal.ReleaseInactiveSurfaces();
    }
}


