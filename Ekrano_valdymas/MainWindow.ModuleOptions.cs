using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Ekrano_valdymas;
public partial class MainWindow
{
    private readonly Dictionary<MenuItem, ContextMenu> moduleOptionMenus = new();
    private ContextMenu? shortcutOptions, weatherOptions, webOptions, sensorOptions;
    private ContextMenu? openModuleOptions;
    private readonly MenuItem playerOptionsMenu = new() { Header="Player" };
    private readonly MenuItem skinOptionsMenu = new() { Header="Skin", ToolTip="Dashboard background. Hardware players and VU keep their controls." };
    private readonly MenuItem noPanelSkinMenu = new() { Header="No skin", Tag="none", IsCheckable=true, StaysOpenOnClick=true };
    private readonly MenuItem playerDimMenu = new() { Header="Auto dimming", ToolTip="Dim the player after playback stops." };
    private readonly MenuItem volumeLightMenu = new() { Header="Knob backlight", ToolTip="Color and brightness. Stays on while player power is on." };
    private readonly MenuItem volumeColorMenu = new() { Header="Color…" };
    private readonly MenuItem volumeFollowColorMenu = new() { Header="Follow PLAY color", IsCheckable=true, StaysOpenOnClick=true };
    private void SetPlayerMenuContext(bool enabled)
    {
        playerOptionsMenu.Visibility=musicConsoleEnabled || selectedModules.Any(m=>m is ModuleKind.Music or ModuleKind.MusicPlayerCard)
            ? Visibility.Visible : Visibility.Collapsed;
        volumeLightMenu.Visibility=musicConsoleEnabled?Visibility.Visible:Visibility.Collapsed;
    }
    private bool IsPlayerMenuTarget()
    {
        return FindPlayerMenuTarget(ContextMenu.PlacementTarget)!=null;
    }
    private static FrameworkElement? FindPlayerMenuTarget(DependencyObject? source)
    {
        for(var p=source;p!=null;p=GetSafeParent(p))
            if(p is MusicConsoleControl or MusicPlayerCard)return (FrameworkElement)p;
        return null;
    }

    internal ContextMenu NewModuleMenu()
    {
        var menu = new ContextMenu {
            Resources = ContextMenu.Resources, Template = ContextMenu.Template,
            Background = ContextMenu.Background, Foreground = ContextMenu.Foreground,
            FontFamily = ContextMenu.FontFamily, Padding = new Thickness(7), BorderThickness = new Thickness(0)
        };
        menu.Opened += (_, _) => { openModuleOptions=menu; MarkLocalActivity(); ApplyContextMenuScale(menu);  ApplyMusicDimSettings(); };
        menu.Closed += (_, _) => { if(ReferenceEquals(openModuleOptions,menu))openModuleOptions=null; MarkLocalActivity(); SettingsWindow.RefreshMenuLayer(menu, false); SaveWindowSettings(); };
        return menu;
    }
    private static void DetachMenu(MenuItem item)
    {
        if (ItemsControl.ItemsControlFromItemContainer(item) is ItemsControl parent) parent.Items.Remove(item);
        else if (item.Parent is ItemsControl owner) owner.Items.Remove(item);
    }
    private ContextMenu MoveModuleMenu(MenuItem root)
    {
        var menu = NewModuleMenu(); DetachMenu(root);
        while (root.Items.Count > 0) { object item = root.Items[0]; root.Items.RemoveAt(0); menu.Items.Add(item); }
        moduleOptionMenus[root] = menu; return menu;
    }
    private IEnumerable<MenuItem> ModuleMenuItems(MenuItem root) =>
        root==musicModuleMenu ? playerDimMenu.Items.OfType<MenuItem>() :
        (moduleOptionMenus.TryGetValue(root, out var menu) ? menu.Items : root.Items).OfType<MenuItem>();
    private void InitializeModuleOptions()
    {
        // Layout remains reachable even when this tab has no music module.
        var layouts = (MenuItem)showModuleTitlesMenuItem.Parent;
        musicModuleMenu.Items.Remove(musicConsoleMenuItem);
        var musicOptions=MoveModuleMenu(musicModuleMenu);
        var skin=musicOptions.Items.OfType<MenuItem>().First(i=>Equals(i.Header,"Wide player skin"));
        musicOptions.Items.Remove(skin);
        var dimRows=musicOptions.Items.OfType<MenuItem>().Where(i=>i.Tag is string t&&int.TryParse(t,out _)).ToArray();
        var dimLabel=musicOptions.Items.OfType<MenuItem>().First(i=>Equals(i.Header,"Auto dimming after playback stops"));
        musicOptions.Items.Remove(dimLabel);
        var off=new MenuItem{Header="Off",Tag="0",IsCheckable=true,StaysOpenOnClick=true};off.Click+=MusicDimDelay_Click;playerDimMenu.Items.Add(off);
        foreach(var row in dimRows){musicOptions.Items.Remove(row);playerDimMenu.Items.Add(row);}
        // Keep one owner for these controls; the main Player submenu is the entry point.
        while(musicOptions.Items.Count>0){var item=musicOptions.Items[0];musicOptions.Items.RemoveAt(0);playerOptionsMenu.Items.Add(item);}
        volumeColorMenu.Click+=(_,_)=>ChooseVolumeLightColor();
        volumeFollowColorMenu.Click+=(_,_)=>{ApplyVolumeLightColor(null);SaveWindowSettings();};
        volumeLightMenu.Items.Add(volumeColorMenu);volumeLightMenu.Items.Add(volumeFollowColorMenu);volumeLightMenu.Items.Add(new Separator());
        foreach(double value in new[]{0d,25d,50d,75d,100d})
        {
            var row=new MenuItem{Header=value==0?"Off":$"{value:0}%",Tag=value,IsCheckable=true,StaysOpenOnClick=true};
            row.Click+=(_,_)=>{ApplyVolumeLighting(value);SaveWindowSettings();};
            volumeLightMenu.Items.Add(row);
        }
        var startupPower=new MenuItem{Header="Power on at startup",IsCheckable=true,StaysOpenOnClick=true,ToolTip="Start this tab's player switched on. Does not start music playback; the power button still works normally."};
        startupPower.Click+=(_,_)=>{selectedPlayerSettings.PowerOnAtStartup=startupPower.IsChecked;SaveWindowSettings();};
        playerOptionsMenu.SubmenuOpened+=(_,_)=>startupPower.IsChecked=selectedPlayerSettings.PowerOnAtStartup;
        playerOptionsMenu.Items.Add(startupPower);
        playerOptionsMenu.Items.Add(playerDimMenu);
        InitializePlayerTextMenu();
        playerOptionsMenu.SubmenuOpened+=(_,_)=>{ApplyMusicDimSettings();UpdateVolumeLightChecks();};
        ContextMenu.Items.Insert(ContextMenu.Items.IndexOf(layouts)+1,playerOptionsMenu);
        var appearance=ContextMenu.Items.OfType<MenuItem>().First(i=>Equals(i.Header,"Appearance"));
        DetachMenu(darkPanelMetalMenu);DetachMenu(powderBlackPanelMetalMenu);DetachMenu(rustIronPanelMetalMenu);DetachMenu(brushedLightPanelMetalMenu);DetachMenu(carbonPanelMetalMenu);DetachMenu(militarityPanelMetalMenu);DetachMenu(lightPanelMetalMenu);
        skinOptionsMenu.Items.Add(powderBlackPanelMetalMenu);skinOptionsMenu.Items.Add(darkPanelMetalMenu);skinOptionsMenu.Items.Add(rustIronPanelMetalMenu);skinOptionsMenu.Items.Add(brushedLightPanelMetalMenu);skinOptionsMenu.Items.Add(carbonPanelMetalMenu);skinOptionsMenu.Items.Add(militarityPanelMetalMenu);skinOptionsMenu.Items.Add(lightPanelMetalMenu);skinOptionsMenu.Items.Add(noPanelSkinMenu);
        noPanelSkinMenu.Click+=PanelMetal_Click;
        InitializeIconAppearanceMenu();
        ContextMenu.Items.Insert(ContextMenu.Items.IndexOf(playerOptionsMenu)+1,skinOptionsMenu);
        var windowMenu=(MenuItem)alwaysOnTopMenuItem.Parent;DetachMenu(alwaysOnTopMenuItem);DetachMenu(windowMenu);
        ContextMenu.Items.Insert(ContextMenu.Items.IndexOf(monitorControlsMenuItem)+1,alwaysOnTopMenuItem);
        foreach(var pair in new[]{(layouts,"\uE80F"),(playerOptionsMenu,"\uE8D6"),(skinOptionsMenu,"\uE771"),(appearance,"\uE790"),(monitorControlsMenuItem,"\uE7F4")})
            pair.Item1.Icon=new TextBlock{Text=pair.Item2,FontFamily=new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),FontSize=20};
        shortcutOptions = MoveModuleMenu((MenuItem)showShortcutNamesMenuItem.Parent);
        weatherOptions = MoveModuleMenu((MenuItem)cityLocationMenuItem.Parent);
        InitializeWeatherTranslations();
        webOptions = MoveModuleMenu((MenuItem)webImagesOnlyMenuItem.Parent);
        webOptions.Items.Remove(webImagesOnlyMenuItem);
        webOptions.Items.Remove(autoDashHideMenuItem);
        sensorOptions = NewModuleMenu(); DetachMenu(sensorsMenuItem);
        DetachMenu(showModuleTitlesMenuItem);
        appearance.Items.Insert(0,showModuleTitlesMenuItem);
        DetachMenu(layouts);
        DetachMenu(layoutTabCountMenuItem);
        ContextMenu.Items.Insert(ContextMenu.Items.IndexOf(layoutMenuItem)+1,layoutTabCountMenuItem);
        quickLaunchBar.PopulateTabCountMenu(layoutTabCountMenuItem);
        // Legacy bar switches are superseded by reusable macro placements.
        DetachMenu((MenuItem)showClockMenuItem.Parent);
        DetachMenu((MenuItem)showObsRecordMenuItem.Parent);
        // Center visible glyph ink for both sides of each primary menu row.
        foreach(var item in ContextMenu.Items.OfType<MenuItem>().Where(i=>i.Header is string))
        {
            var text=new FrameworkElementFactory(typeof(OpticallyCenteredText));
            text.SetBinding(OpticallyCenteredText.TextProperty,new System.Windows.Data.Binding());
            text.SetBinding(OpticallyCenteredText.TextFontSizeProperty,new System.Windows.Data.Binding("FontSize"){RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor,typeof(MenuItem),1)});
            text.SetValue(OpticallyCenteredText.TextFontFamilyProperty,ContextMenu.FontFamily);
            text.SetValue(OpticallyCenteredText.TextFontWeightProperty,FontWeights.SemiBold);
            text.SetValue(OpticallyCenteredText.InkAlignmentProperty,HorizontalAlignment.Left);
            text.SetValue(FrameworkElement.HeightProperty,24d);
            item.HeaderTemplate=new DataTemplate{VisualTree=text};
            if(item.Icon is TextBlock glyph)
                item.Icon=new OpticallyCenteredText{Text=glyph.Text,TextFontFamily=glyph.FontFamily,TextFontSize=glyph.FontSize,TextFontWeight=FontWeights.Normal,Width=26,Height=24};
        }
        InitializeMenuRefinements();
    }

    private void AboutMenuItem_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new AboutWindow(){Owner=this};dialog.ShowDialog();

    }
    private void HelpMenuItem_Click(object sender,RoutedEventArgs e) => new HelpWindow{Owner=this}.ShowDialog();
    private AnalogVuCard CreateAnalogVuCard()
    {
        var card=new AnalogVuCard(visualizerAudio);
        card.Meter.SensitivityChanged+=(_,_)=>{
            StoreVuSensitivity(card.Meter.Sensitivity);
            musicConsole?.SetVuSensitivity(card.Meter.Sensitivity);
            tabConsoles[activeDashboardLayoutTab]?.SetVuSensitivity(card.Meter.Sensitivity);
            SaveWindowSettings();
        };
        return card;
    }
    internal void OpenModuleOptions(UserControl module, FrameworkElement anchor)
    {
        if(ContextMenu.IsOpen)ContextMenu.IsOpen=false;
        SetPlayerMenuContext(false);
        if(module is SpotifyWidget){OpenMainMenuAtScreenPoint(anchor.PointToScreen(new Point(0,anchor.ActualHeight)));return;}
        if(module is HueWidget hueEditor)
        {
            if(anchor.ContextMenu is ContextMenu oldMenu) oldMenu.IsOpen=false;
            anchor.ContextMenu=null;
            hueEditor.OpenLightOptions();
            return;
        }
        if(module is MacroLibraryCard macroEditor) {
            if(anchor.ContextMenu is ContextMenu previous)previous.IsOpen=false;
            anchor.ContextMenu=null;macroEditor.OpenManager();return;
        }
        if(module is HudVisualizerCard hudEditor) {
            if(anchor.ContextMenu is ContextMenu previous)previous.IsOpen=false;
            anchor.ContextMenu=null;hudEditor.OpenSettings(this);return;
        }
        ContextMenu? menu = module switch {
            SpotifyWidget => null,
            ShortcutsWidget => shortcutOptions,
            WebShortcutsWidget => webOptions,
            WeatherWidget => weatherOptions,
            PlaylistCard playlist => playlist.Options(),
            SystemMonitorWidget => sensorOptions,
            _ => null
        };
        if (module is SystemMonitorWidget monitor) PopulateSystemMonitorSensorMenu(monitor);
        if (module is WeatherWidget) UpdateWeatherSourceStatus();
        if (menu == null) {
            menu = NewModuleMenu();
            if (module is OBSWidget) menu.Items.Add(new MenuItem { Header = "Hotkeys are configured in OBS", IsEnabled = false });
        }
        anchor.ContextMenu = menu;
        menu.PlacementTarget = anchor; menu.Placement = PlacementMode.Bottom;
        menu.HorizontalOffset = 0; menu.VerticalOffset = 0; menu.IsOpen = true;
    }
}





