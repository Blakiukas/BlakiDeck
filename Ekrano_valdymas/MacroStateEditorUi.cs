using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace Ekrano_valdymas;
internal static class MacroStateEditorUi
{
    internal static void StyleTabs(Window window)
    {
        window.Resources[typeof(TabControl)]=System.Windows.Markup.XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="TabControl">
          <Setter Property="Foreground" Value="White"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TabControl">
            <DockPanel><TabPanel IsItemsHost="True" DockPanel.Dock="Top" Margin="0,0,0,8"/>
              <Border BorderBrush="#68645E" BorderThickness="1" CornerRadius="7" Padding="4" Background="#1B1814">
                <ContentPresenter ContentSource="SelectedContent"/>
              </Border>
            </DockPanel>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """);
        window.Resources[typeof(TabItem)]=System.Windows.Markup.XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="TabItem">
          <Setter Property="Foreground" Value="White"/><Setter Property="Background" Value="Transparent"/>
          <Setter Property="BorderBrush" Value="#68645E"/><Setter Property="Padding" Value="12,6"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TabItem">
            <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="7" Margin="0,0,8,0" Padding="{TemplateBinding Padding}">
              <ContentPresenter ContentSource="Header" VerticalAlignment="Center"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsSelected" Value="True"><Setter Property="Background" Value="#503A2500"/><Setter Property="BorderBrush" Value="#FF8A00"/></Trigger>
              <Trigger Property="IsMouseOver" Value="True"><Setter Property="BorderBrush" Value="#FF8A00"/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """);
    }
    private static void Compact(Panel panel)
    {
        foreach(FrameworkElement child in panel.Children){child.Margin=child is TextBlock?new Thickness(4,4,4,2):new Thickness(4,2,4,2);if(child is Panel nested)Compact(nested);}
    }
    private sealed record SourceChoice(string Key,string Label){public override string ToString()=>Label;}
    internal static StackPanel Create(Window owner,MacroDefinition draft,ObservableCollection<MacroStep> steps,Action modeChanged,Action? appearanceChanged=null)
    {
        var panel=new StackPanel{Margin=new Thickness(4)};
        var mode=AppearanceEditorUi.Choice(draft.IconMode??"Single",("Single","One icon"),("State","Follow actual state"),("Alternate","Alternate actions A / B"));
        mode.Name="macroIconMode";
        EditorUi.Field(panel,"Icon mode",mode);
        var detail=new StackPanel();panel.Children.Add(detail);
        var meaning=AppearanceEditorUi.Choice(draft.IconMeaning,("Current","Show current state / last completed action"),("Next","Show the next action"));
        EditorUi.Field(detail,"What the icon shows",meaning);
        var sourcePanel=new StackPanel();detail.Children.Add(sourcePanel);
        var source=new ComboBox();EditorUi.Field(sourcePanel,"Read state from an action A step",source);
        var help=AppearanceEditorUi.Help(detail,"");help.Cursor=System.Windows.Input.Cursors.Hand;help.MouseLeftButtonUp+=(_,_)=>AppMessage.Show(owner,help.ToolTip?.ToString()??"","State icons",MessageBoxButton.OK,MessageBoxImage.Information);
        var reset=MacroUi.Button("Reset to A (saved with Save)",(_,_)=>{draft.NextActionB=false;draft.LastAction=null;UpdateHelp();appearanceChanged?.Invoke();});detail.Children.Add(reset);
        var iconsPanel=new StackPanel();detail.Children.Add(iconsPanel);
        void UpdateHelp()
        {
            help.ToolTip=draft.IconMode=="Alternate"?$"Next: {(draft.NextActionB?"B":"A")}. Switches only after all steps succeed. External changes are not tracked. Save keeps this position across restarts.":"Reads the real state, including changes outside BlakiDeck. Blank icons use the main icon. Unknown state never predicts the next action.";
            help.Text=draft.IconMode=="Alternate"?$"Next action: {(draft.NextActionB?"B":"A")}. ⓘ":"Follows the real state. Blank icons use the main icon. ⓘ";
        }
        void AddIcon(string key,string label)
        {
            var row=new Grid{Margin=new Thickness(4,3,4,3)};row.ColumnDefinitions.Add(new(){Width=new GridLength(170)});row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var caption=new TextBlock{Text=label,TextWrapping=TextWrapping.Wrap,FontSize=14,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)};row.Children.Add(caption);
            var box=new TextBox{Text=draft.StateIcons.GetValueOrDefault(key,""),ToolTip="Icon symbol or image path"};
            System.Windows.Automation.AutomationProperties.SetName(box,label);
            box.TextChanged+=(_,_)=>{draft.StateIcons[key]=box.Text.Trim();appearanceChanged?.Invoke();};
            var choose=MacroUi.Button("File…",(_,_)=>{if(DefaultIconCatalog.ChooseImage(owner) is string path)box.Text=MacroDefaultIcons.Portable(path);});
            var buttons=new StackPanel{Orientation=Orientation.Horizontal};buttons.Children.Add(choose);buttons.Children.Add(MacroUi.Button("Built-in…",(_,_)=>{if(BuiltInIconGallery.Choose(owner,"Macros") is string path)box.Text=MacroDefaultIcons.Portable(path);}));Grid.SetColumn(box,1);Grid.SetColumn(buttons,2);row.Children.Add(box);row.Children.Add(buttons);iconsPanel.Children.Add(row);
        }
        void UpdateIcons()
        {
            iconsPanel.Children.Clear();
            if(draft.IconMode=="Alternate"){AddIcon("A","Action A");AddIcon("B","Action B");}
            else if(MacroStates.Source(draft)?.Kind=="Camera")
            {AddIcon("Off","Camera disabled");AddIcon("Ready",draft.IconMeaning=="Next"?"Enable camera":"Camera ready");if(draft.IconMeaning=="Current")AddIcon("Active","Camera in use");}
            else {AddIcon("Off",draft.IconMeaning=="Next"?"Turn off / stop":"Off / muted / stopped");AddIcon("On",draft.IconMeaning=="Next"?"Turn on / start":"On / recording / running");}
            AddIcon("Unknown",draft.IconMode=="Alternate"?"No completed action yet":"State unavailable");
            UpdateHelp();Compact(panel);AppearanceEditorUi.CompactControls(iconsPanel);
        }
        void UpdateSources()
        {
            draft.Steps=steps.ToList();
            var choices=steps.Where(MacroStates.Supports).GroupBy(MacroStates.Key).Select(g=>new SourceChoice(g.Key,g.First().ToString())).ToArray();
            string? selected=draft.StateSourceKey;
            source.ItemsSource=choices;source.SelectedItem=choices.FirstOrDefault(c=>c.Key==selected)??choices.FirstOrDefault();
            draft.StateSourceKey=(source.SelectedItem as SourceChoice)?.Key;
            UpdateIcons();
        }
        source.SelectionChanged+=(_,_)=>{draft.StateSourceKey=(source.SelectedItem as SourceChoice)?.Key;UpdateIcons();appearanceChanged?.Invoke();};
        meaning.SelectionChanged+=(_,_)=>{draft.IconMeaning=AppearanceEditorUi.Selected(meaning);UpdateIcons();appearanceChanged?.Invoke();};
        void UpdateMode()
        {
            draft.IconMode=AppearanceEditorUi.Selected(mode);detail.Visibility=draft.IconMode=="Single"?Visibility.Collapsed:Visibility.Visible;
            sourcePanel.Visibility=draft.IconMode=="State"?Visibility.Visible:Visibility.Collapsed;
            reset.Visibility=draft.IconMode=="Alternate"?Visibility.Visible:Visibility.Collapsed;UpdateIcons();modeChanged();appearanceChanged?.Invoke();
        }
        mode.SelectionChanged+=(_,_)=>UpdateMode();steps.CollectionChanged+=(_,_)=>UpdateSources();UpdateSources();UpdateMode();Compact(panel);return panel;
    }
}

