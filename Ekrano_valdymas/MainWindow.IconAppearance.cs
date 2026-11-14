using System.Windows;
using System.Windows.Controls;
namespace Ekrano_valdymas;
public partial class MainWindow
{
    private readonly MenuItem iconStyleMenu=new(){Header="Icons",Icon=MenuGlyph("\uE91B")};
    private CheckBox skinAllTabs=null!;
    private IEnumerable<int> SkinTargetTabs()=>skinAllTabs?.IsChecked==true?Enumerable.Range(0,dashboardLayouts.Length):new[]{activeDashboardLayoutTab};
    private void ApplyCommonIconStyle(string value)
    {
        var tabs=SkinTargetTabs().ToArray();
        ShortcutIconEffect.SetCommon(tabs,value);
        foreach(int tab in tabs)if(dashboardLayouts[tab]!=null)
            dashboardLayouts[tab].Player=(dashboardLayouts[tab].Player??new TabPlayerSettings()) with{IconStyle=value};
        IconAppearance.SetStyle(value);UpdateIconStyleChecks();SaveWindowSettings();
    }
    private void ApplyCommonClockStyle(string value)
    {
        foreach(int tab in SkinTargetTabs())if(dashboardLayouts[tab]!=null)
            dashboardLayouts[tab].Player=(dashboardLayouts[tab].Player??new TabPlayerSettings()) with{ClockStyle=value};
        var appearance=quickLaunchBar.CurrentClockAppearance;appearance.MaterialStyle=value;
        quickLaunchBar.SetClockAppearance(appearance);
    }

    private void InitializeIconAppearanceMenu()
    {
        var header=new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var title=new OpticallyCenteredText{Text="Skin",CompactLineBox=true,InkAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Stretch};
        foreach(var pair in new[]{(OpticallyCenteredText.TextFontFamilyProperty,"FontFamily"),(OpticallyCenteredText.TextFontSizeProperty,"FontSize"),(OpticallyCenteredText.TextFontWeightProperty,"FontWeight"),(OpticallyCenteredText.TextBrushProperty,"Foreground")})
            title.SetBinding(pair.Item1,new System.Windows.Data.Binding(pair.Item2){Source=skinOptionsMenu});
        header.Children.Add(title);
        skinAllTabs=new CheckBox{Content="All tabs",IsChecked=false,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(16,0,12,0),Style=(Style)FindResource("DashboardCheckBox"),ToolTip="Apply the next skin, icon effect or clock style choice to all tabs."};
        skinAllTabs.SetBinding(Control.ForegroundProperty,new System.Windows.Data.Binding("Foreground"){Source=skinOptionsMenu});
        skinAllTabs.Click+=(_,e)=>e.Handled=true;
        Grid.SetColumn(skinAllTabs,1);header.Children.Add(skinAllTabs);
        skinOptionsMenu.Header=header;
        quickLaunchBar.ClockStyleSaved+=(style,all)=>{
            selectedPlayerSettings=selectedPlayerSettings with{ClockStyle=style};
            if(all)foreach(var profile in dashboardLayouts)
                if(profile!=null)profile.Player=(profile.Player??new TabPlayerSettings()) with{ClockStyle=style};
            SaveDashboardLayouts();
        };
        foreach(var (label,value) in new[]{("Original","original"),("Monochrome","mono"),("Metal","metal"),("Inlay","inlay")}){
            var item=new MenuItem{Header=label,Tag=value,IsCheckable=true,StaysOpenOnClick=true};
            item.Click+=(_,_)=>ApplyCommonIconStyle(value);iconStyleMenu.Items.Add(item);
        }
        iconStyleMenu.SubmenuOpened+=(_,_)=>UpdateIconStyleChecks();skinOptionsMenu.Items.Add(new Separator());skinOptionsMenu.Items.Add(iconStyleMenu);UpdateIconStyleChecks();
        var clockMenu=new MenuItem{Header="Clock style",Icon=MenuGlyph("\uE823")};skinOptionsMenu.Items.Add(clockMenu);
        foreach(var (label,value) in new[]{("Original","original"),("Monochrome","mono"),("Metal","metal"),("Inlay","inlay")}){
            var item=new MenuItem{Header=label,Tag=value,IsCheckable=true,StaysOpenOnClick=true};clockMenu.Items.Add(item);
            item.Click+=(_,_)=>{ApplyCommonClockStyle(value);foreach(var entry in clockMenu.Items.OfType<MenuItem>())entry.IsChecked=Equals(entry.Tag,value);};
        }
        clockMenu.SubmenuOpened+=(_,_)=>{foreach(var item in clockMenu.Items.OfType<MenuItem>())item.IsChecked=Equals(item.Tag,quickLaunchBar.CurrentClockAppearance.EffectiveMaterial);};
    }
    private void UpdateIconStyleChecks(){foreach(var item in iconStyleMenu.Items.OfType<MenuItem>())item.IsChecked=Equals(item.Tag,IconAppearance.Style);}
}
