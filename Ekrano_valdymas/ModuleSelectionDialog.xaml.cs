using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Ekrano_valdymas
{
    public partial class ModuleSelectionDialog : SettingsWindow
    {
        private static readonly (string Key, string Label)[] Modules =
        {
            ("MusicPlayerCard", "Player"), ("Playlist", "Playlist — requires Player"), ("HudVisualizer", "BlakiHUD music visualizer"),
            ("Obs", "OBS Controls"), ("Weather", "Weather"),
            ("Airthings", "Airthings"), ("System", "System Monitor"), ("Shortcuts", "Shortcuts 1×1"),
            ("Hue", "Philips Hue — requires Hue Bridge"),
            ("WebShortcuts", "Web shortcuts"), ("AnalogVuCard", "VU — full card"), ("MacroLibrary", "Macro library")
        };

        private readonly string[] selections;
        private readonly List<RadioButton>[] choiceButtons = { new(), new(), new() };
        private bool updating;
        internal int FactoryTabIndex {get;set;}
        private Action? refreshLayout;
        private void Default_Click(object sender,RoutedEventArgs e)
        {
            var tabs=FactoryDefaults.Read<System.Text.Json.Nodes.JsonArray>("dashboard-layout-tabs.json");
            var profile=tabs[Math.Clamp(FactoryTabIndex,0,tabs.Count-1)]!;
            var modules=profile["Modules"]!.AsArray().Select(n=>n!.GetValue<string>()).ToArray();
            Array.Copy(modules,selections,3);WideShortcutsSpan=profile["WideShortcutsSpan"]!.GetValue<int>();
            PlayerLayout=profile["MusicConsoleEnabled"]!.GetValue<bool>();SelectedFinish=profile["Player"]!["Finish"]!.GetValue<string>();
            ClearCoveredSelections();RefreshChecks();refreshLayout?.Invoke();
            PreviewChanged?.Invoke(selections,WideShortcutsSpan);LayoutPreviewChanged?.Invoke(PlayerLayout,SelectedFinish);
        }
        internal const double DefaultListTextSize = 23;
        internal double ListTextSize {get;set;}=DefaultListTextSize;
        internal const double MaximumListTextSize = 28d * 4d / 3d;
        internal static double ChoiceHeight(double size) => size * (22d / 16d);
        internal bool ShowModuleTitles {get;set;}=true;
        internal event Action<bool>? ModuleTitlesPreviewChanged;

        public IReadOnlyList<string> SelectedModules => selections;
        public int WideShortcutsSpan { get; private set; }
        public event Action<IReadOnlyList<string>, int>? PreviewChanged;

        public ModuleSelectionDialog(IReadOnlyList<string> initialModules, int initialWideSpan)
        {
            selections = Enumerable.Range(0, 3)
                .Select(index => index < initialModules.Count ? (initialModules[index]=="Music"?"MusicPlayerCard":initialModules[index]) : new[]{"MusicPlayerCard","Playlist","System"}[index])
                .ToArray();
            WideShortcutsSpan = initialWideSpan is 2 or 3 ? initialWideSpan : 0;
            ClearCoveredSelections();
            InitializeComponent();
            BuildChoices(leftChoices, 0);
            BuildChoices(centerChoices, 1);
            BuildChoices(rightChoices, 2);
            RefreshChecks();
        }

        internal bool PlayerLayout {get;private set;}
        internal string SelectedFinish {get;private set;}=MusicFinish.DefaultName;
        internal event Action<bool,string>? LayoutPreviewChanged;
        internal void SetTabContext(QuickLaunchBar.TabPresentation tab, bool player,bool lightSkin=false,string? finish=null)
        {
            PlayerLayout=player;SelectedFinish=MusicFinish.Normalize(finish??(lightSkin?"light":MusicFinish.DefaultName));Height=720;DialogWorkArea.Attach(this);
            var host=(Grid)moduleChoicesGrid.Parent;
            var context=new StackPanel{Margin=new Thickness(0,12,0,0)};Grid.SetRow(context,2);
            layoutTitle.Text="MODULES LAYOUT · "+tab.Name;
            var modeHost=new DockPanel();context.Children.Add(modeHost);
            var listOptions=new StackPanel{Width=250};DockPanel.SetDock(listOptions,Dock.Right);modeHost.Children.Add(listOptions);
            var titles=new CheckBox{Name="LayoutShowModuleTitles",Content="Show module titles",Foreground=Brushes.WhiteSmoke,IsChecked=ShowModuleTitles,FontSize=16,Margin=new Thickness(0,0,0,4)};
            titles.SetResourceReference(StyleProperty,"DashboardCheckBox");
            titles.Checked+=(_,_)=>{ShowModuleTitles=true;ModuleTitlesPreviewChanged?.Invoke(true);};
            titles.Unchecked+=(_,_)=>{ShowModuleTitles=false;ModuleTitlesPreviewChanged?.Invoke(false);};
            listOptions.Children.Add(titles);
            var textSize=new Slider{Name="LayoutListTextSize",Minimum=16,Maximum=MaximumListTextSize,Value=ListTextSize,Width=210,Height=40,ToolTip="Module list text size",IsMoveToPointEnabled=true,VerticalAlignment=VerticalAlignment.Center};SliderDefaults.Mark(textSize,DefaultListTextSize);listOptions.Children.Add(textSize);
            ScrollViewer? playerHelp=null;
            void FitChoices()=>Dispatcher.BeginInvoke(new Action(()=>{if(!IsVisible)return;UpdateLayout();double extra=PlayerLayout?playerHelp?.ScrollableHeight??0:new[]{leftScroll,centerScroll,rightScroll}.Max(scroll=>scroll.ScrollableHeight);if(extra>1)DialogWorkArea.GrowToContent(this,extra+4);}),System.Windows.Threading.DispatcherPriority.Loaded);
            void ResizeChoices(){foreach(var choice in choiceButtons.SelectMany(b=>b)){choice.FontSize=ListTextSize;choice.Height=ChoiceHeight(ListTextSize);}FitChoices();}
            textSize.ValueChanged+=(_,_)=>{ListTextSize=textSize.Value;ResizeChoices();};ResizeChoices();
            var modeRow=new StackPanel{Orientation=Orientation.Horizontal};modeHost.Children.Add(modeRow);
            var playerButton=MacroUi.Button("Wide music player",(_,_)=>{});playerButton.Name="playerLayoutChoice";
            var moduleButton=MacroUi.Button("Dashboard modules",(_,_)=>{});moduleButton.Name="moduleLayoutChoice";
            foreach(var button in new[]{playerButton,moduleButton}){button.FontSize=18;button.MinHeight=44;modeRow.Children.Add(button);}
            var skinRow=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(4,8,4,4)};
            var finishes=new[]{("powder-black","Powder coated black"),("dark","Dark metal"),("light","Light metal"),("rust-iron","Rust iron"),("brushed-light","Grey metal"),("carbon-fiber","Carbon"),("militarity","Militarity")};
            var skins=MacroUi.Button("Skins",(_,_)=>{});skins.FontSize=18;
            var skinContent=new StackPanel{Orientation=Orientation.Horizontal};
            var skinText=new TextBlock{VerticalAlignment=VerticalAlignment.Center};
            var skinChevron=new System.Windows.Shapes.Path{Data=Geometry.Parse("M1,1 L8,8 L15,1"),Width=16,Height=9,Stroke=Brushes.WhiteSmoke,StrokeThickness=2,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,0,0)};
            skinContent.Children.Add(skinText);skinContent.Children.Add(skinChevron);skins.Content=skinContent;
            var skinMenu=new ContextMenu{PlacementTarget=skins,Placement=PlacementMode.Bottom};skinMenu.SetResourceReference(StyleProperty,"MiniContextMenuStyle");
            skins.Click+=(_,_)=>skinMenu.IsOpen=true;skinRow.Children.Add(skins);context.Children.Add(skinRow);
            host.Children.Add(context);
            playerHelp=new ScrollViewer{Name="PlayerLayoutHelp",Content=PlayerLayoutDiagram.Create(SelectedFinish),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new Thickness(0,112,0,0)};
            Grid.SetRow(playerHelp,2);host.Children.Add(playerHelp);
            void Update(){skinText.Inlines.Clear();skinText.Inlines.Add(new System.Windows.Documents.Run("Skins: "){Foreground=Brushes.DarkOrange});skinText.Inlines.Add(new System.Windows.Documents.Run(finishes.Single(x=>x.Item1==SelectedFinish).Item2){Foreground=Brushes.WhiteSmoke});listOptions.Visibility=PlayerLayout?Visibility.Collapsed:Visibility.Visible;foreach(var item in skinMenu.Items.OfType<MenuItem>())item.IsChecked=Equals(item.Tag,SelectedFinish);skinRow.Visibility=PlayerLayout?Visibility.Visible:Visibility.Collapsed;moduleChoicesGrid.Visibility=PlayerLayout?Visibility.Collapsed:Visibility.Visible;playerHelp.Visibility=PlayerLayout?Visibility.Visible:Visibility.Collapsed;moduleChoicesGrid.Margin=new Thickness(0,84,0,12);playerButton.BorderBrush=PlayerLayout?Brushes.DarkOrange:Brushes.Gray;moduleButton.BorderBrush=PlayerLayout?Brushes.Gray:Brushes.DarkOrange;Height=720;}
            refreshLayout=()=>{Update();FitChoices();};
            void Preview(){Update();LayoutPreviewChanged?.Invoke(PlayerLayout,SelectedFinish);FitChoices();}
            foreach(var (key,label) in finishes){var item=new MenuItem{Header=label,Tag=key,IsCheckable=true};item.SetResourceReference(StyleProperty,"MiniMenuItemStyle");item.Click+=(_,_)=>{SelectedFinish=key;Preview();};skinMenu.Items.Add(item);}
            playerButton.Click+=(_,_)=>{PlayerLayout=true;Preview();};moduleButton.Click+=(_,_)=>{PlayerLayout=false;Preview();};Update();Loaded+=(_,_)=>FitChoices();
            // Only the context header participates in hit testing above the module choices.
            context.VerticalAlignment=VerticalAlignment.Top;
        }

        private void BuildChoices(Panel panel, int slot)
        {
            var entries=Modules.Select(m=>(Key:$"{slot}|{m.Key}",m.Label)).ToList();
            if(slot==0){entries.Add(("0|SystemWide","System Monitor 1×2"));entries.Add(("0|SystemFull","System Monitor 1×3"));entries.Add(("wide|2","Shortcuts 1×2"));entries.Add(("wide|3","Shortcuts 1×3"));}
            foreach ((string key, string label) in entries.OrderBy(m=>m.Label,StringComparer.OrdinalIgnoreCase))
            {
                RadioButton choice = new()
                {
                    Content = label, ToolTip = label,
                    Tag = key,
                    GroupName = $"ModuleSlot{slot}",
                    Style = (Style)panel.FindResource("ModuleChoice")
                };
                string module=key.Split('|')[1];
                if(key.StartsWith("wide|",StringComparison.Ordinal)||module.StartsWith("Shortcuts",StringComparison.Ordinal)||module=="WebShortcuts")
                    choice.Background=new SolidColorBrush(Color.FromRgb(43,62,78));
                else if(module is "System" or "SystemWide" or "SystemFull")
                    choice.Background=new SolidColorBrush(Color.FromRgb(44,68,55));
                if(key.StartsWith("wide|",StringComparison.Ordinal))choice.Checked+=WideChoice_Checked;else choice.Checked += ModuleChoice_Checked;
                choiceButtons[slot].Add(choice);
                panel.Children.Add(choice);
            }
        }

        private void ModuleChoice_Checked(object sender, RoutedEventArgs e)
        {
            if (updating || sender is not RadioButton { Tag: string tag }) return;
            string[] parts = tag.Split('|');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int slot)) return;
            string selected = parts[1];
            if(selected=="Shortcuts"&&selections[slot] is "Shortcuts2" or "Shortcuts3")selected=selections[slot];
            if(!CanChoose(slot,selected)){RefreshChecks();return;}
            
            int existing = selected=="None"?-1:Array.IndexOf(selections, selected);
            if (existing >= 0 && existing != slot)
                (selections[existing], selections[slot]) = (selections[slot], selections[existing]);
            else selections[slot] = selected;
            WideShortcutsSpan = 0;
            ClearCoveredSelections();
            RefreshChecks();
            PreviewChanged?.Invoke(selections, WideShortcutsSpan);
        }

        private void WideChoice_Checked(object sender, RoutedEventArgs e)
        {
            if (updating || sender is not RadioButton { Tag: string tag }) return;
            string[] parts = tag.Split('|');
            if (parts.Length == 2 && int.TryParse(parts[1], out int span))
            {if(!CanChoose(0,span==2?"Wide2":"Wide3")){RefreshChecks();return;}WideShortcutsSpan=span;selections[0]="None";ClearCoveredSelections();}
            RefreshChecks();
            PreviewChanged?.Invoke(selections, WideShortcutsSpan);
        }

        private void ChoicesScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (sender is not ScrollViewer viewer) return;
            Border? top = ReferenceEquals(viewer, leftScroll) ? leftTopMore
                : ReferenceEquals(viewer, centerScroll) ? centerTopMore : rightTopMore;
            Border? bottom = ReferenceEquals(viewer, leftScroll) ? leftBottomMore
                : ReferenceEquals(viewer, centerScroll) ? centerBottomMore : rightBottomMore;
            if (top == null || bottom == null) return;
            top.Visibility = viewer.VerticalOffset > 1 ? Visibility.Visible : Visibility.Collapsed;
            bottom.Visibility = viewer.ScrollableHeight > 1 && viewer.VerticalOffset < viewer.ScrollableHeight - 1
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RefreshChecks()
        {
            updating = true;
            for (int slot = 0; slot < choiceButtons.Length; slot++)
                foreach (RadioButton button in choiceButtons[slot])
                {
                    string tag = button.Tag as string ?? string.Empty;
                    var parts=tag.Split('|');string key=parts[0]=="wide"?(parts[1]=="2"?"Wide2":"Wide3"):parts[1];
                    bool covered=slot>0&&slot<OccupiedSpan;
                    button.IsEnabled=!covered&&CanChoose(slot,key);
                    button.Opacity=button.IsEnabled?1:.35;
                    button.IsChecked = tag.StartsWith("wide|", StringComparison.Ordinal)
                        ? slot == 0 && tag == $"wide|{WideShortcutsSpan}"
                        : !covered&&(slot>0||WideShortcutsSpan==0)&&(tag == $"{slot}|{selections[slot]}"||key=="Shortcuts"&&selections[slot] is "Shortcuts2" or "Shortcuts3");
                }
            updating = false;
        }
        private int OccupiedSpan=>WideShortcutsSpan is 2 or 3?WideShortcutsSpan:selections[0]=="SystemFull"?3:selections[0]=="SystemWide"?2:1;
        private void ClearCoveredSelections(){for(int slot=1;slot<OccupiedSpan;slot++)selections[slot]="None";}
        private bool CanChoose(int slot,string key)
        {
            if(slot>0)return slot>=OccupiedSpan;
            return true; // Left module owns its span; covered cells are cleared.
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
        private void DialogSurface_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject source) return;
            // Inline text (Run) is a content element, not a Visual. Follow its
            // logical parent so clicks reach the owning button without dragging.
            for (DependencyObject? current = source; current != null; current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            {
                if (current is ButtonBase or ScrollViewer or Slider) return;
                if (ReferenceEquals(current, this)) break;
            }
            e.Handled = true;
            DragMove();
        }
        private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
        private void Apply_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
    }
}



