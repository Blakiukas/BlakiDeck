using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Ekrano_valdymas;

public partial class QuickLaunchBar
{
    internal sealed class TabPresentation
    {
        public string Name {get;set;}="";
        public string Icon {get;set;}="◆";
        public bool? AutomaticSourceIcon {get;set;}
        public string Presentation {get;set;}="Text";
        public double TextSize {get;set;}=28;
        public string FontFamily {get;set;}="Segoe UI";
        public bool Bold {get;set;}
        public bool Italic {get;set;}
        public string? LightEffectMode {get;set;}
        public string? DarkEffectMode {get;set;}
        internal string ModeFor(bool light)=>NormalizeEffect((light?LightEffectMode:DarkEffectMode)??(EffectFor(light)>0?"Outline":"None"));
        public double EffectStrength {get;set;}=1;
        private static string NormalizeEffect(string mode)=>mode=="Engraved"?"Outline":mode;
    public double? LightEffectStrength {get;set;}
        public double? DarkEffectStrength {get;set;}
        internal double EffectFor(bool light)=> (light?LightEffectStrength:DarkEffectStrength)??EffectStrength;
        public string Background {get;set;}="Existing";
    }
    internal sealed class BarAppearance
    {
        public Dictionary<string,TabPresentation> ShortcutText {get;set;}=new(StringComparer.OrdinalIgnoreCase);
        public double? LightEffectStrength {get;set;}
        public double? DarkEffectStrength {get;set;}
        internal double EffectFor(bool light)=>(light?LightEffectStrength:DarkEffectStrength)
            ??Tabs.Select(t=>light?t.LightEffectStrength:t.DarkEffectStrength).FirstOrDefault(v=>v.HasValue)
            ??Tabs.FirstOrDefault()?.EffectStrength??1;
        public int MetalScopeVersion {get;set;}
        public TabPresentation[] Tabs {get;set;}=Enumerable.Range(1,MaximumLayoutTabs).Select(i=>new TabPresentation{Name=i.ToString()}).ToArray();
        public string Background {get;set;}="Existing";
    }
    private BarAppearance barAppearance=new();
    private string appearanceFile="";
    private TabPresentation? tabPreview;
    private string? barBackgroundPreview;
    private double? lightEffectPreview,darkEffectPreview;
    private double VisibleEffect(bool light)=>(light?lightEffectPreview:darkEffectPreview)??barAppearance.EffectFor(light);
    private int tabPreviewIndex=-1;
    internal TabPresentation LayoutTabPresentation(int index) => JsonSerializer.Deserialize<TabPresentation>(JsonSerializer.Serialize(VisibleTab(Math.Clamp(index,0,barAppearance.Tabs.Length-1))))!;
    private TabPresentation VisibleTab(int index)=>tabPreviewIndex==index&&tabPreview!=null?tabPreview:barAppearance.Tabs[index];
    private Border[] editableTabBorders=Array.Empty<Border>();
    private OpticallyCenteredText[] editableTabLabels=Array.Empty<OpticallyCenteredText>();
    private bool musicBackgroundContext;
    private readonly MetalRailBase catalogBase=new(){Height=RailGeometry.OriginalBaseThickness,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(5,0,5,0),IsHitTestVisible=false};
    private readonly CatalogRailFace catalogFace=new(){IsHitTestVisible=false};
    private readonly MetalMinimizeFace minimizeFace=new(){IsHitTestVisible=false};
    private readonly MetalRailEdge railMachining=new(){IsHitTestVisible=false};
    private readonly DispatcherTimer tabHoldTimer=new(){Interval=TimeSpan.FromMilliseconds(650)};
    private Border? heldTab;
    internal const int ShortcutTabHoverMilliseconds=600;
    private readonly DispatcherTimer shortcutTabHoverTimer=new(DispatcherPriority.Input){Interval=TimeSpan.FromMilliseconds(ShortcutTabHoverMilliseconds)};
    private ShortcutTransfer? shortcutTabHoverTransfer;
    private int shortcutTabHoverIndex=-1;

    internal int ShortcutTabAt(Point point)
    {
        for(int index=0;index<editableTabBorders.Length;index++)
        {
            var tab=editableTabBorders[index];
            if(tab.IsVisible&&new Rect(tab.TranslatePoint(new Point(),this),tab.RenderSize).Contains(point))return index;
        }
        return -1;
    }
    internal void CancelShortcutTabHover()
    {shortcutTabHoverTimer.Stop();shortcutTabHoverTransfer=null;shortcutTabHoverIndex=-1;}
    internal void UpdateShortcutTabHover(ShortcutTransfer? transfer,int tab)
    {
        if(transfer==null||!transfer.CanMoveTo(null)||tab<0||tab>=TabCount||tab==_activeTab){CancelShortcutTabHover();return;}
        if(ReferenceEquals(shortcutTabHoverTransfer,transfer)&&shortcutTabHoverIndex==tab)return;
        CancelShortcutTabHover();shortcutTabHoverTransfer=transfer;shortcutTabHoverIndex=tab;shortcutTabHoverTimer.Start();
    }
    private void InitializeShortcutTabHover()
    {
        AllowDrop=true;
        shortcutTabHoverTimer.Tick+=(_,_)=>
        {
            shortcutTabHoverTimer.Stop();var transfer=shortcutTabHoverTransfer;int tab=shortcutTabHoverIndex;
            if(transfer?.CanMoveTo(null)==true&&IsLoaded&&IsVisible&&tab>=0&&tab<TabCount&&tab!=_activeTab)SwitchTopBarTab(tab);
            shortcutTabHoverTransfer=null;shortcutTabHoverIndex=-1;
        };
        AddHandler(DragDrop.PreviewDragOverEvent,new DragEventHandler((_,e)=>
        {
            var transfer=ShortcutDragSupport.ResolveTransfer(e.Data);int tab=ShortcutTabAt(e.GetPosition(this));
            UpdateShortcutTabHover(transfer,tab);
            if(transfer!=null&&tab>=0){e.Effects=transfer.CanMoveTo(null)?DragDropEffects.Move:DragDropEffects.None;e.Handled=true;}
        }),true);
        AddHandler(DragDrop.PreviewDragLeaveEvent,new DragEventHandler((_,e)=>
        {if(ShortcutTabAt(e.GetPosition(this))<0)CancelShortcutTabHover();}),true);
        AddHandler(DragDrop.PreviewDropEvent,new DragEventHandler((_,e)=>
        {CancelShortcutTabHover();if(ShortcutTabAt(e.GetPosition(this))>=0){e.Effects=DragDropEffects.None;e.Handled=true;}}),true);
        Unloaded+=(_,_)=>CancelShortcutTabHover();
    }


    private void InitializeEditableTabs(string folder)
    {
        InitializeClockAppearance(folder);
        appearanceFile=Path.Combine(folder,"dashboard-tab-appearance.json");
        foreach(string file in new[]{appearanceFile,appearanceFile+".previous"})
        {
            try { if(!File.Exists(file))continue;barAppearance=JsonSerializer.Deserialize<BarAppearance>(File.ReadAllText(file))??new();break; }
            catch(Exception ex){InteractionDiagnostics.RecordFailure("Tab appearance load",ex);}
        }
        var saved=barAppearance.Tabs??Array.Empty<TabPresentation>();
        barAppearance.Tabs=Enumerable.Range(0,MaximumLayoutTabs).Select(i=>i<saved.Length&&saved[i]!=null?saved[i]:new TabPresentation{Name=(i+1).ToString()}).ToArray();
        if(barAppearance.MetalScopeVersion<1)
        {
            // Older per-card Always represented the current player appearance.
            barAppearance.Background=barAppearance.Tabs.Any(t=>t.Background is "Music" or "Always")||barAppearance.Background is "Music" or "Always"?"Music":"Existing";
            barAppearance.MetalScopeVersion=1;
        }
        if(barAppearance.Background=="Music")barAppearance.Background="Always";
        foreach(var p in barAppearance.Tabs){p.TextSize=double.IsFinite(p.TextSize)?Math.Clamp(p.TextSize,10,96):28;p.Name=string.IsNullOrWhiteSpace(p.Name)?"Tab":p.Name[..Math.Min(p.Name.Length,40)];}
        var borders=new List<Border>{tab1Button,tab2Button,tab3Button};
        var labels=new List<OpticallyCenteredText>{tab1Text,tab2Text,tab3Text};
        for(int i=3;i<MaximumLayoutTabs;i++)
        {
            tabRailGrid.ColumnDefinitions.Add(new ColumnDefinition());
            var b=new Border{Tag=i.ToString(),Margin=new Thickness(3,8,3,8),CornerRadius=new CornerRadius(15),Cursor=Cursors.Hand};
            b.Name=$"tab{i+1}Button";RegisterName(b.Name,b);
            b.SetResourceReference(Border.BackgroundProperty,"ModuleCardSurfaceBrush");Grid.SetColumn(b,i);tabRailGrid.Children.Add(b);
            b.PreviewMouseLeftButtonDown+=TopBarTab_PreviewMouseLeftButtonDown;b.PreviewMouseMove+=TopBarTab_PreviewMouseMove;b.PreviewMouseLeftButtonUp+=TopBarTab_PreviewMouseLeftButtonUp;
            b.PreviewTouchDown+=(s,e)=>TopBarTab_PreviewTouchDown(s!,e);b.PreviewTouchMove+=(s,e)=>TopBarTab_PreviewTouchMove(s!,e);b.PreviewTouchUp+=(s,e)=>TopBarTab_PreviewTouchUp(s!,e);
            borders.Add(b);labels.Add(new OpticallyCenteredText());
        }
        editableTabBorders=borders.ToArray();editableTabLabels=labels.ToArray();
        InitializeShortcutTabHover();
        tabRail.Child=null;
        // Metal and content need separate stacking: the plate passes behind
        // the base, while the existing icon/shadow layer keeps its own order.
        tabRail.Child=tabRailGrid;
        catalogFace.SetBinding(Grid.ColumnSpanProperty,new System.Windows.Data.Binding{Source=tabRail,Path=new PropertyPath(Grid.ColumnSpanProperty)});
        topBarGrid.Children.Add(catalogFace);Panel.SetZIndex(catalogFace,-1);
        tabRail.BorderBrush=Brushes.Transparent;tabRail.Background=Brushes.Transparent;
        tabRail.Margin=new Thickness(0);tabRail.Padding=new Thickness(0);tabRail.BorderThickness=new Thickness(0);
        compactToggle.BorderThickness=new Thickness(0);
        topBarGrid.Children.Add(catalogBase);Panel.SetZIndex(catalogBase,20);
        railMachining.Catalog=catalogFace;railMachining.Minimize=minimizeFace;railMachining.Base=catalogBase;railMachining.Host=topBarGrid;catalogFace.ConnectedEdge=railMachining;Grid.SetColumnSpan(railMachining,100);topBarGrid.Children.Add(railMachining);Panel.SetZIndex(railMachining,22);
        var minimizeIcon=compactToggle.Child;compactToggle.Child=null;
        var minimizeLayers=new Grid();minimizeLayers.Children.Add(minimizeFace);minimizeLayers.Children.Add(minimizeIcon);compactToggle.Child=minimizeLayers;
        topBarGrid.SizeChanged+=(_,_)=>ApplyBarMaterial();
        LayoutUpdated+=(_,_)=>RefreshShortcutInkReference();
        compactToggle.SizeChanged+=(_,_)=>ApplyBarMaterial();
        tabHoldTimer.Tick+=(_,_)=>{tabHoldTimer.Stop();if(heldTab==null||_tabDragStarted)return;_tabTouchDevice?.Capture(null);_tabTouchDevice=null;_pressedTab=null;OpenTabEditor(int.Parse((string)heldTab.Tag));};
        foreach(var b in editableTabBorders)
        {
            var menu=new ContextMenu();menu.SetResourceReference(FrameworkElement.StyleProperty,"MiniContextMenuStyle");var edit=new MenuItem{Header="Edit"};edit.SetResourceReference(FrameworkElement.StyleProperty,"MiniMenuItemStyle");edit.Click+=(_,_)=>OpenTabEditor(int.Parse((string)b.Tag));menu.Items.Add(edit);b.ContextMenu=menu;
            b.PreviewTouchDown+=(_,_)=>{heldTab=b;tabHoldTimer.Start();};
            b.PreviewTouchMove+=(_,_)=>{if(_tabDragStarted)tabHoldTimer.Stop();};
            b.PreviewTouchUp+=(_,_)=>{tabHoldTimer.Stop();heldTab=null;};
            b.LostTouchCapture+=(_,_)=>tabHoldTimer.Stop();
            b.SizeChanged+=(_,_)=>RenderEditableTab(int.Parse((string)b.Tag));
        }
        UpdateTabVisuals();
    }
    private readonly Dictionary<int,(string Icon,string Presentation)> renderedTabContent=new();
    private static readonly System.Windows.Media.Effects.DropShadowEffect TabIconShadow=CreateTabIconShadow();
    public static System.Windows.Media.Effects.DropShadowEffect TopBarTextShadow { get; } = CreateTabIconShadow();
    private static readonly System.Windows.Media.Effects.DropShadowEffect LightPlayerTabIconShadow=CreateLightPlayerTabIconShadow();
    private static System.Windows.Media.Effects.DropShadowEffect CreateLightPlayerTabIconShadow()
    {
        // The player icon has 15 design units below its ink before the rail edge.
        // The enlarged shadow uses that allowance without moving the icon.
        var shadow=new System.Windows.Media.Effects.DropShadowEffect{Color=Colors.Black,Opacity=1,BlurRadius=11,ShadowDepth=4,Direction=270};
        shadow.Freeze();return shadow;
    }
    public static System.Windows.Media.Effects.DropShadowEffect TopBarIconShadow => TabIconShadow;
    private static System.Windows.Media.Effects.DropShadowEffect CreateTabIconShadow()
    {
        var shadow=new System.Windows.Media.Effects.DropShadowEffect{Color=Colors.Black,Opacity=1,BlurRadius=11,ShadowDepth=4,Direction=270};
        shadow.Freeze();return shadow;
    }
    private void RenderEditableTab(int index)
    {
        if (index < 0 || index >= editableTabBorders.Length || index >= TabCount) return;
        var p=VisibleTab(index);var b=editableTabBorders[index];var text=editableTabLabels[index];
        bool dim=MusicFinish.IsLight&&index!=_activeTab;
        void ApplyInk(TabInkLayout layout){
            text.InkLuminance=dim?.52:1;text.InvalidateVisual();
            if(layout.Icon is AdaptiveIconImage image){image.Dimmed=dim;image.SkinLightOverride=dim?false:null;}
            if(layout.Icon is OpticallyCenteredText glyph){glyph.InkLuminance=dim?.52:1;glyph.InvalidateVisual();}
        }
        text.TextFontFamily=FontEditor.Family(p.FontFamily);text.TextFontWeight=p.Bold?FontWeights.Bold:FontWeights.Normal;text.TextFontStyle=p.Italic?FontStyles.Italic:FontStyles.Normal;text.InsetStrength=VisibleEffect(MusicFinish.IsLight);text.TextFontSize=p.TextSize;text.LineAdvance=p.TextSize*1.15;text.Ellipsis=false;text.ClipToBounds=false;EcoEffects.SetEffect(text,TopBarTextShadow);
        text.Text=WrapTab(p.Name,Math.Max(20,b.ActualWidth-12),p,VisualTreeHelper.GetDpi(text).PixelsPerDip);
        // Selection/size/font changes must not detach every icon or decode its
        // bitmap again. Rebuild children only when their content actually changes.
        if(b.Child is TabInkLayout existing&&renderedTabContent.TryGetValue(index,out var content)&&content==(p.Icon,p.Presentation))
        {
            existing.RequestedSize=p.TextSize;ApplyInk(existing);existing.InvalidateMeasure();
            b.ToolTip=p.Name+" — right-click / hold to edit";return;
        }
        if(b.Child is Panel old)old.Children.Clear();else b.Child=null;
        var grid=new TabInkLayout {Label=text,RequestedSize=p.TextSize,IconLimit=AdjacentShortcutInkHeight,Bottom=()=>TabInkBottom(b),IconTop=()=>AdjacentShortcutInkTop(b),ClipToBounds=false};
        if(p.Presentation=="IconText")Grid.SetRow(text,0);
        else Grid.SetRow(text,0);
        if(p.Presentation!="Icon")grid.Children.Add(text);
        if(p.Presentation!="Text")
        {
            FrameworkElement icon;
            if(File.Exists(PackagedIconPath.Resolve(p.Icon)))
            {
                try
                {
                    var bitmap=IconBitmap.Load(p.Icon);
                    icon=new AdaptiveIconImage{SkinIcon=true,ShortcutTarget=TabIconEffectTarget(index),Source=bitmap,Stretch=Stretch.Uniform};
                    // Filter the original at the final composed scale, including dashboard zoom and DPI.
                    RenderOptions.SetBitmapScalingMode(icon,BitmapScalingMode.HighQuality);
                }
                catch{icon=IconGlyph("◆");}
            }
            else { var glyph=IconGlyph(string.IsNullOrWhiteSpace(p.Icon)?"◆":p.Icon);glyph.TextFontSize=p.Presentation=="Icon"?52:30;icon=glyph; }
            EcoEffects.SetEffect(icon,TabIconShadow);
            grid.Icon=icon;grid.Children.Add(icon);
        }
        b.Child=grid;ApplyInk(grid);renderedTabContent[index]=(p.Icon,p.Presentation);b.ToolTip=p.Name+" — right-click / hold to edit";
    }
    private static OpticallyCenteredText IconGlyph(string icon)=>new(){Text=icon,TextFontFamily=new FontFamily("Segoe UI Symbol"),TextFontSize=30,TextBrush=Brushes.White,Ellipsis=true,ClipToBounds=true};
    private double TabInkBottom(Border tab)
    {
        double origin=tab.TranslatePoint(new Point(0,tab.BorderThickness.Top),topBarGrid).Y;
        bool active=Array.IndexOf(editableTabBorders,tab)==_activeTab;
        double faceTop=catalogFace.TranslatePoint(new Point(0,active?1:4),topBarGrid).Y;
        double faceBottom=catalogFace.TranslatePoint(new Point(0,active?catalogFace.ActualHeight+RailGeometry.CornerDrop:catalogFace.ActualHeight-9.5),topBarGrid).Y;
        double upperInset=origin+AdjacentShortcutInkTop(tab)-faceTop;
        return faceBottom-Math.Max(0,upperInset)-origin;
    }
    private double? shortcutInkTop;
    private double shortcutInkHeight=45;
    private sealed record IconInkFractions(double Top,double Height);
    private readonly ConditionalWeakTable<BitmapSource,IconInkFractions> iconInkFractions=new();
    private double AdjacentShortcutInkTop(Border tab)
        =>shortcutInkTop is double top?top-tab.TranslatePoint(new Point(0,tab.BorderThickness.Top),topBarGrid).Y:2;
    private double AdjacentShortcutInkHeight()=>shortcutInkHeight;
    // Commit one coherent reference after layout, never read partially arranged
    // shortcut presenters from a tab's MeasureOverride. Keep the last reference
    // while RebuildTopBarLayout replaces the presenters.
    private void RefreshShortcutInkReference()
    {
        using var callbackTiming = new UiCallbackTiming("RefreshShortcutInkReference");
        FindShortcutInkReference(this);
    }
    // Preorder matches the previous iterator: inspect each child before its
    // descendants. Search success means a usable reference was found, including
    // an unchanged reference; it does not mean that layout was invalidated.
    private bool FindShortcutInkReference(DependencyObject parent)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);
            if(child is Image image&&TryUseShortcutInkReference(image))return true;
            if(FindShortcutInkReference(child))return true;
        }
        return false;
    }
    private bool TryUseShortcutInkReference(Image image)
    {
        if(image.Name!="ShortcutImage"||!image.IsVisible||image.Source is not BitmapSource source||!(image.ActualHeight>0))return false;
        if(!image.IsArrangeValid||!image.IsMeasureValid)return false;
        if(!iconInkFractions.TryGetValue(source,out var ink))
        {
            var pixels=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);int stride=pixels.PixelWidth*4;var bytes=new byte[stride*pixels.PixelHeight];pixels.CopyPixels(bytes,stride,0);
            int first=pixels.PixelHeight,last=-1;
            for(int y=0;y<pixels.PixelHeight;y++)for(int x=0;x<pixels.PixelWidth;x++)if(bytes[y*stride+x*4+3]>16){first=Math.Min(first,y);last=Math.Max(last,y);}
            ink=last<0?new(0,0):new((double)first/pixels.PixelHeight,(last-first+1d)/pixels.PixelHeight);iconInkFractions.Add(source,ink);
        }
        double height=Math.Min(image.ActualHeight,image.ActualWidth*source.Height/source.Width);
        double rendered=height*ink.Height;
        if(rendered<=0)return false;
        double top=image.TranslatePoint(new Point(0,(image.ActualHeight-height)/2+height*ink.Top),topBarGrid).Y;
        if(shortcutInkTop is double previous&&Math.Abs(previous-top)<.01&&Math.Abs(shortcutInkHeight-rendered)<.01)return true;
        shortcutInkTop=top;shortcutInkHeight=rendered;
        foreach(var border in editableTabBorders)if(border.Child is TabInkLayout layout)layout.InvalidateMeasure();
        return true;
    }
    private static string WrapTab(string name,double width,TabPresentation p,double pixelsPerDip)
    {
        var words=name.Replace('\n',' ').Split(' ',StringSplitOptions.RemoveEmptyEntries);string first="",second="";
        foreach(var word in words)
        {
            string candidate=first.Length==0?word:first+" "+word;
            var ft=new FormattedText(candidate,System.Globalization.CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface(FontEditor.Family(p.FontFamily),p.Italic?FontStyles.Italic:FontStyles.Normal,p.Bold?FontWeights.Bold:FontWeights.Normal,FontStretches.Normal),p.TextSize,Brushes.White,pixelsPerDip);
            if(second.Length==0&&(first.Length==0||ft.Width<=width))first=candidate;else second+=(second.Length==0?"":" ")+word;
        }
        return second.Length==0?first:first+"\n"+second;
    }


    internal void SetMusicBackgroundContext(bool value){musicBackgroundContext=value;SetValue(IconShadowHost.PlayerModeProperty,value);foreach(var border in editableTabBorders)if(border.Child is TabInkLayout layout)layout.ClipToBounds=false;ApplyBarMaterial();}
    private bool webOuterCorners,webTabsAtTop;
    internal void SetWebOuterCorners(bool enabled,bool atTop)
    {
        if(webOuterCorners==enabled&&webTabsAtTop==atTop)return;
        webOuterCorners=enabled;webTabsAtTop=atTop;
        catalogFace.SquareOuterTop=minimizeFace.SquareOuterTop=enabled&&!atTop;
        ApplyBarMaterial();
    }
    private void ApplyBarMaterial()
    {
        topBarGrid.Background = null;
        if(topBarGrid.ActualWidth>20&&topBarGrid.ActualHeight>20)topBarGrid.Clip=RailGeometry.OuterClip(topBarGrid.ActualWidth,topBarGrid.ActualHeight,webOuterCorners&&webTabsAtTop);
        string background=barBackgroundPreview??barAppearance.Background;
        bool metal=background=="Always"&&!CommonMetal.NoSkin;
        RefreshShortcutText(!CommonMetal.NoSkin);
        // The continuous base used to overpaint the tab shadow at the join.
        Panel.SetZIndex(tabRail,21);
        Panel.SetZIndex(slotsControl,21);
        Panel.SetZIndex(clockButton,21);
        foreach(var border in editableTabBorders)if(border.Child is TabInkLayout {Icon:not null} layout)
            EcoEffects.SetEffect(layout.Icon,musicBackgroundContext&&MusicFinish.IsLight?LightPlayerTabIconShadow:TabIconShadow);
        for(int i=0;i<editableTabLabels.Length;i++) {editableTabLabels[i].InsetMetalText=metal;editableTabLabels[i].DarkMetalInset=!MusicFinish.IsLight;editableTabLabels[i].InsetOnBrightMetal=MusicFinish.IsLight&&i==_activeTab;editableTabLabels[i].InsetStrength=VisibleEffect(MusicFinish.IsLight);}
        catalogFace.TabCount=TabCount;
        catalogFace.Materials=Enumerable.Repeat((Brush?)CommonMetal.DefaultBrush,TabCount).ToArray();
        RenderOptions.SetBitmapScalingMode(catalogFace,BitmapScalingMode.HighQuality);
        catalogFace.MaterialHost=topBarGrid;
        catalogBase.Background=CommonMetal.DefaultBrush;
        // Both presentation choices share the same global material field.
        catalogFace.InvalidateVisual();
        compactToggle.Background=Brushes.Transparent;compactToggle.BorderBrush=Brushes.Transparent;
        compactToggleIcon.Stroke=SkinChevronBrush();
        minimizeFace.MaterialHost=topBarGrid;minimizeFace.Visibility=Visibility.Visible;minimizeFace.InvalidateVisual();railMachining.InvalidateVisual();
    }
    internal void PreviewRailCornerDrop(double value){RailGeometry.CornerDrop=value;ApplyBarMaterial();catalogBase.InvalidateVisual();}

    private void OpenTabEditor(int index)
    {
        CreateTabEditor(index).ShowDialog();
    }
    private static string TabIconEffectTarget(int index)=>$"dashboard-tab-icon:{index}";
    internal SettingsWindow CreateTabEditor(int index)
    {
        var original=barAppearance.Tabs[index];
        var draft=JsonSerializer.Deserialize<TabPresentation>(JsonSerializer.Serialize(original))!;
        draft.Background=barAppearance.Background;
        var dialog=AppearanceEditorUi.Window("Dashboard tab "+(index+1),Window.GetWindow(this),740);
        dialog.Width=820;
        var root=new DockPanel();dialog.Content=root;
        var footer=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(18,4,18,12)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var panel=new StackPanel{Margin=new Thickness(18,0,8,6)};var textFields=new StackPanel{Margin=new Thickness(8,0,18,6)};root.Children.Add(MacroUi.Scroll(AppearanceEditorUi.Columns(panel,textFields)));
        var name=new TextBox{Text=draft.Name,MaxLength=40,Padding=new Thickness(10,6,10,6)};
        var icon=new TextBox{Text=draft.Icon,Padding=new Thickness(10,6,10,6)};
        var presentation=AppearanceEditorUi.Presentation(draft.Presentation);
        var size=new ComboBox{ItemsSource=new double[]{10,12,14,18,22,28,32,36,42,48,60,72,96},SelectedItem=draft.TextSize};
        var nameFrame=AppearanceEditorUi.AttentionField(panel,"Name (up to 40 characters)",name);
        var iconFrame=AppearanceEditorUi.AttentionField(panel,"Icon: symbol or image file",icon);
        var iconButtons=new WrapPanel();panel.Children.Add(iconButtons);
        var choose=MacroUi.Button("Choose file…",(_,_)=>{});iconButtons.Children.Add(choose);
        iconButtons.Children.Add(MacroUi.Button("Built-in icons…",(_,_)=>
        {
            if(BuiltInIconGallery.Choose(dialog) is string path)icon.Text=path;
        }));
        var presentationFrame=AppearanceEditorUi.AttentionField(panel,"Show on tab",presentation);
        var hint=AppearanceEditorUi.Help(panel,"");
        EditorUi.Field(textFields,"Text size",size);
        bool editingLight=MusicFinish.IsLight;
        var font=new FontEditor(textFields,draft.FontFamily,draft.Bold,draft.Italic,barAppearance.EffectFor(editingLight),editingLight?"Light skin effect — all tabs (0 = off, 1 = default)":"Dark skin effect — all tabs (0 = off, 1 = default)");
        string effectTarget=TabIconEffectTarget(index);
        var iconEffect=ShortcutIconEffect.Editor(panel,effectTarget);
        iconEffect.SelectionChanged+=(_,_)=>ShortcutIconEffect.Preview(effectTarget,AppearanceEditorUi.Selected(iconEffect));
        AppearanceEditorUi.Help(panel,"Changes preview immediately. Save keeps them; Cancel restores the previous appearance.");
        bool iconWasChosen=false;
        void UpdatePreview(bool pulse=false)
        {
            draft.Name=name.Text.Trim();draft.Presentation=AppearanceEditorUi.Selected(presentation);
            draft.TextSize=size.SelectedItem is double value?value:original.TextSize;
            draft.FontFamily=font.SelectedFamily;draft.Bold=font.Bold;draft.Italic=font.Italic;
            if(editingLight)lightEffectPreview=font.Strength;else darkEffectPreview=font.Strength;
            string valueIcon=PackagedIconPath.Resolve(icon.Text.Trim())!;
            bool looksLikePath=valueIcon.Contains('\\')||valueIcon.Contains('/')||valueIcon.Contains(':');
            draft.Icon=looksLikePath&&!File.Exists(valueIcon)?original.Icon:valueIcon;
            bool attention=iconWasChosen&&draft.Presentation=="Text";
            presentation.Tag=attention?"IconRequired":null;
            presentationFrame.BorderBrush=new SolidColorBrush(attention?Color.FromRgb(255,69,69):Colors.Transparent);
            hint.Text=attention?"The selected icon is hidden. Choose Icon or Icon and text.":"";
            if(attention&&pulse)AppearanceEditorUi.Pulse(presentationFrame,retain:true);
            tabPreviewIndex=index;tabPreview=draft;UpdateTabVisuals();ApplyBarMaterial();
        }
        textFields.Children.Add(MacroUi.Button("Default text",(_,_)=>{font.Reset("Segoe UI",false,false,1);size.SelectedItem=28d;UpdatePreview();}));
        name.TextChanged+=(_,_)=>UpdatePreview();
        font.Changed+=()=>UpdatePreview();
        icon.TextChanged+=(_,_)=>{iconWasChosen=true;UpdatePreview(true);};
        presentation.SelectionChanged+=(_,_)=>UpdatePreview();size.SelectionChanged+=(_,_)=>UpdatePreview();
        choose.Click+=(_,_)=>
        {
                if(DefaultIconCatalog.ChooseImage(dialog) is not string path)return;
                icon.Text=path;iconWasChosen=true;UpdatePreview(true);
        };
        dialog.Closed+=(_,_)=>{ShortcutIconEffect.Preview(effectTarget,null);tabPreview=null;barBackgroundPreview=null;lightEffectPreview=null;darkEffectPreview=null;tabPreviewIndex=-1;UpdateTabVisuals();ApplyBarMaterial();};
        footer.Children.Add(MacroUi.Button("Save",(_,_)=>
        {
            if(string.IsNullOrWhiteSpace(name.Text)&&draft.Presentation!="Icon")
            {AppearanceEditorUi.Pulse(nameFrame);name.Focus();return;}
            string selectedIcon=PackagedIconPath.Resolve(icon.Text.Trim())!;
            if(draft.Presentation!="Text"&&(selectedIcon.Length==0||((selectedIcon.Contains('\\')||selectedIcon.Contains('/')||selectedIcon.Contains(':'))&&!File.Exists(selectedIcon))))
            {hint.Text="Choose an existing image file or enter an icon symbol.";AppearanceEditorUi.Pulse(iconFrame);return;}
            var candidate=JsonSerializer.Deserialize<BarAppearance>(JsonSerializer.Serialize(barAppearance))!;
            var committed=JsonSerializer.Deserialize<TabPresentation>(JsonSerializer.Serialize(draft))!;
            committed.Name=string.IsNullOrWhiteSpace(name.Text)?original.Name:name.Text.Trim();committed.Icon=selectedIcon;
            if(iconWasChosen)committed.AutomaticSourceIcon=false;
            candidate.Tabs[index]=committed;
            if(editingLight)candidate.LightEffectStrength=font.Strength;else candidate.DarkEffectStrength=font.Strength;
            try{ShortcutIconEffect.Save(effectTarget,AppearanceEditorUi.Selected(iconEffect));AtomicJson.Write(appearanceFile,candidate);barAppearance=candidate;dialog.DialogResult=true;}
            catch(Exception ex){hint.Text="Could not save: "+ex.Message;}
        }));
        footer.Children.Add(MacroUi.Button("Cancel",(_,_)=>dialog.DialogResult=false));AppearanceEditorUi.Frame(dialog);return dialog;
    }
}







