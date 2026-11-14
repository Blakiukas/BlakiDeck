using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Ekrano_valdymas;
internal sealed class MacroLibraryCard:UserControl
{
    internal const string ClockTarget="blakideck-widget:clock";
    private readonly MacroTilePanel tiles=new();
    private readonly Grid body=new();
    private readonly TextBlock status=new(){Foreground=Brushes.White,FontSize=14,TextTrimming=TextTrimming.CharacterEllipsis};
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(1)};
    private OpticallyCenteredText? clock;
    // The library contains a reusable clock template, independent of placements.
    // Legacy JSON Hidden is intentionally ignored; appearance is retained.
    private sealed class ClockState { public ClockAppearance Appearance {get;set;}=new(); }
    private ClockState clockState=new();
    private AirthingsMiniOptions miniState=LoadMiniTemplate();
    private UsageMiniOptions usageState=LoadUsageTemplate();
    private static string UsagePath=>Path.Combine(DiagnosticState.LocalApplicationData,"Ekrano_valdymas","macro-library-usage.json");
    private static UsageMiniOptions LoadUsageTemplate(){try{return (File.Exists(UsagePath)?JsonSerializer.Deserialize<UsageMiniOptions>(File.ReadAllText(UsagePath))??new():new UsageMiniOptions()).Normalize();}catch{return new();}}
    private static string MiniPath=>Path.Combine(DiagnosticState.LocalApplicationData,"Ekrano_valdymas","macro-library-airthings.json");
    private static AirthingsMiniOptions LoadMiniTemplate(){try{return File.Exists(MiniPath)?JsonSerializer.Deserialize<AirthingsMiniOptions>(File.ReadAllText(MiniPath))??new():new();}catch{return new();}}
    private static string ClockPath=>Path.Combine(DiagnosticState.LocalApplicationData,"Ekrano_valdymas","macro-library-clock.json");
    private static ClockAppearance LibraryClockAppearance()
    {
        try{return File.Exists(ClockPath)?JsonSerializer.Deserialize<ClockState>(File.ReadAllText(ClockPath))?.Appearance??new():new();}
        catch{return new();}
    }
    private bool listMode;
    private Button? stopButton;
    private Point start;
    private bool dragging;
    private TouchDevice? touch;
    private Popup? preview;
    private long suppressMouseUntil;
    private static string ViewPath=>Path.Combine(DiagnosticState.LocalApplicationData,"Ekrano_valdymas","macro-library-view.json");
    internal bool ListMode=>listMode;
    internal MacroLibraryCard()
    {
        try{listMode=File.Exists(ViewPath)&&JsonSerializer.Deserialize<string>(File.ReadAllText(ViewPath))=="List";}catch{}
        try{if(File.Exists(ClockPath))clockState=JsonSerializer.Deserialize<ClockState>(File.ReadAllText(ClockPath))??new();}catch(Exception ex){InteractionDiagnostics.RecordFailure("Library clock load",ex);}
        Tag="NoWindowDrag";
        var shell=new Border{Padding=new Thickness(22,22,22,20),CornerRadius=new CornerRadius(18),BorderBrush=new SolidColorBrush(Color.FromArgb(64,255,255,255)),BorderThickness=new Thickness(1.5)};
        shell.SetResourceReference(Border.BackgroundProperty,"PanelMetalBrush");CommonMetal.SetSurface(shell,true);Content=shell;
        var root=new Grid();shell.Child=root;root.RowDefinitions.Add(new(){Height=new GridLength(45)});root.RowDefinitions.Add(new(){Height=new GridLength(1)});root.RowDefinitions.Add(new());
        var header=new DockPanel();root.Children.Add(header);
        var toolbar=new StackPanel{Orientation=Orientation.Horizontal};
        var toolbarFrame=new Border{Name="MacroToolbar",Child=toolbar,Height=36,VerticalAlignment=VerticalAlignment.Center};
        DockPanel.SetDock(toolbarFrame,Dock.Right);header.Children.Add(toolbarFrame);
        Button Tool(string key,string hint,RoutedEventHandler action){var button=new Button();button.SetResourceReference(StyleProperty,"ModuleOptionsButtonStyle");button.Click+=action;button.Name="MacroTool"+key;button.ToolTip=hint;button.Width=38;button.Height=36;button.MinWidth=0;button.MinHeight=0;button.Padding=new Thickness(0);button.Margin=new Thickness(2,0,2,0);button.Content=ToolbarIcon(key);toolbar.Children.Add(button);return button;}
        Tool("Add","Add macro",(_,_)=>MacroEditor.Show(Window.GetWindow(this)));
        stopButton=Tool("Stop","Stop running macro",(_,_)=>MacroRunner.Current.Stop());
        Tool("List","Switch icons / list",(_,_)=>SetListMode(!listMode));
        toolbar.Children.Add(new ModuleOptionsButton { Margin=new Thickness(2,0,2,0) });
        var title=new OpticallyCenteredText{Tag="ModuleTitle",Text="MACROS",TextFontSize=27,TextFontWeight=FontWeights.Bold,TextBrush=Brushes.White,InkAlignment=HorizontalAlignment.Left};title.SetResourceReference(OpticallyCenteredText.TextFontFamilyProperty,"TekoFont");title.SetResourceReference(OpticallyCenteredText.TextBrushProperty,"ModuleTitleBrush");var heading=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center}; heading.Children.Add(new ModuleHeaderIcon{Width=30,Height=30,Margin=new Thickness(0,0,12,0),Tag="ModuleIcon"}); heading.Children.Add(title); header.Children.Add(heading);
        var divider=new Border{Background=new SolidColorBrush(Color.FromArgb(40,255,255,255))};divider.SetResourceReference(Border.BackgroundProperty,"PanelDividerBrush");Grid.SetRow(divider,1);root.Children.Add(divider);
        body.Margin=new Thickness(0,21,0,0);Grid.SetRow(body,2);root.Children.Add(body);
        var scroll=MacroUi.Scroll(tiles);scroll.Margin=new Thickness(0,0,-12,0);body.Children.Add(scroll);
        status.VerticalAlignment=VerticalAlignment.Bottom;status.IsHitTestVisible=false;status.Background=new SolidColorBrush(Color.FromArgb(230,26,23,20));body.Children.Add(status);
        body.SizeChanged+=(_,_)=>SizeTiles();
        timer.Tick+=(_,_)=>{if(clock!=null)clock.Text=DateTime.Now.ToString("t");};
        Loaded+=(_,_)=>{MacroLibrary.Current.Changed+=Refresh;MacroRunner.Current.Changed+=RefreshStatus;Refresh(this,EventArgs.Empty);timer.Start();};
        Unloaded+=(_,_)=>{MacroLibrary.Current.Changed-=Refresh;MacroRunner.Current.Changed-=RefreshStatus;timer.Stop();ClosePreview();};
    }
    internal static Image ToolbarIcon(string key)
    {
        var drawing=new DrawingGroup();
        using(var dc=drawing.Open()){
            var pen=new Pen(Brushes.White,1.8){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};
            if(key=="Add"){dc.DrawLine(pen,new Point(2,10),new Point(18,10));dc.DrawLine(pen,new Point(10,2),new Point(10,18));}
            else if(key=="Stop")dc.DrawRoundedRectangle(Brushes.White,null,new Rect(2,2,16,16),1,1);
            else if(key=="List")foreach(double y in new[]{2d,10d,18d}){dc.DrawEllipse(Brushes.White,null,new Point(2,y),1,1);dc.DrawLine(pen,new Point(7,y),new Point(18,y));}
            else{dc.DrawEllipse(null,pen,new Point(10,10),6.4,6.4);dc.DrawEllipse(null,pen,new Point(10,10),2.4,2.4);for(int i=0;i<8;i++){double a=i*Math.PI/4;dc.DrawLine(pen,new Point(10+6.4*Math.Cos(a),10+6.4*Math.Sin(a)),new Point(10+9*Math.Cos(a),10+9*Math.Sin(a)));}}
        }
        drawing.Freeze();return new Image{Source=new DrawingImage(drawing),Width=20,Height=20,Stretch=Stretch.Uniform,IsHitTestVisible=false};
    }
    internal bool IsListPresentation=>listMode;
    internal void SetListMode(bool value){AtomicJson.Write(ViewPath,value?"List":"Icons");listMode=value;Refresh(this,EventArgs.Empty);}
    private void RefreshStatus(object? sender,EventArgs e){if(stopButton!=null){stopButton.IsEnabled=MacroRunner.Current.IsRunning;stopButton.Opacity=stopButton.IsEnabled?1:.32;}status.Text=MacroRunner.Current.Status;status.Visibility=MacroRunner.Current.IsRunning&&!ManagerIsOpen?Visibility.Visible:Visibility.Collapsed;}
    private void SizeTiles()
    {
        // Same four-column, three-row reference grid and margins as ShortcutsWidget.
        double width=Math.Max(1,body.ActualWidth/4-10),height=Math.Max(1,body.ActualHeight/3-10);
        tiles.Columns=listMode?1:4;tiles.Pitch=listMode?Math.Max(1,body.ActualWidth):width+10;tiles.RowHeight=listMode?Math.Max(48,(height+10)/2):height+10;tiles.InvalidateMeasure();
    }
    private void Refresh(object? sender,EventArgs e)
    {
        tiles.Children.Clear();RefreshStatus(sender,e);
        foreach(var macro in MacroLibrary.Current.Items)
        {
            string target=MacroLibrary.Target(macro);var tile=Tile(target,MacroLibrary.Current.Title(target),MacroLibrary.Current.IconFor(target));
            var menu=new ContextMenu();menu.SetResourceReference(StyleProperty,"MiniContextMenuStyle");tile.ContextMenu=menu;
            void Menu(string title,RoutedEventHandler action){var item=new MenuItem{Header=title};item.SetResourceReference(StyleProperty,"MiniMenuItemStyle");item.Click+=action;menu.Items.Add(item);}
            Menu("Edit",(_,_)=>MacroEditor.Show(Window.GetWindow(this),macro));Menu("Duplicate",(_,_)=>Guard(()=>MacroLibrary.Current.Duplicate(macro)));Menu("Remove",(_,_)=>Guard(()=>MacroLibrary.Current.Remove(macro.Id)));
        }
        clock=null;
        {
            var tile=Tile(ClockTarget,$"Clock · {clockState.Appearance.Span} cell(s)",null);Grid.SetColumnSpan(tile,clockState.Appearance.Span);var menu=new ContextMenu();menu.SetResourceReference(StyleProperty,"MiniContextMenuStyle");tile.ContextMenu=menu;
            var edit=new MenuItem{Header="Edit"};edit.SetResourceReference(StyleProperty,"MiniMenuItemStyle");menu.Items.Add(edit);
            edit.Click+=(_,_)=>ClockEditor.Create(Window.GetWindow(this),clockState.Appearance.Clone(),p=>{if(clock!=null)p.Apply(clock,listMode?24/77.33:1,!listMode);foreach(var item in tiles.Children.OfType<Border>().Where(b=>Equals(b.Tag,ClockTarget)))Grid.SetColumnSpan(item,p.Span);tiles.InvalidateMeasure();},p=>{var saved=new ClockState{Appearance=p};AtomicJson.Write(ClockPath,saved);clockState=saved;Refresh(this,EventArgs.Empty);}).ShowDialog();
        }
        {
            string target=AirthingsMini.Encode(miniState);var tile=Tile(target,$"Airthings · {miniState.Span} cell(s)",null);Grid.SetColumnSpan(tile,miniState.Span);
            var menu=new ContextMenu();menu.SetResourceReference(StyleProperty,"MiniContextMenuStyle");tile.ContextMenu=menu;
            var edit=new MenuItem{Header="Edit"};edit.SetResourceReference(StyleProperty,"MiniMenuItemStyle");menu.Items.Add(edit);
            edit.Click+=(_,_)=>CreateAirthingsMiniTemplateEditor().ShowDialog();
        }
        {
            var tile=Tile(UsageMini.Encode(usageState),"BlakiDeck Usage",null);
            var menu=new ContextMenu();menu.SetResourceReference(StyleProperty,"MiniContextMenuStyle");tile.ContextMenu=menu;
            var edit=new MenuItem{Header="Edit"};edit.SetResourceReference(StyleProperty,"MiniMenuItemStyle");menu.Items.Add(edit);
            edit.Click+=(_,_)=>CreateUsageMiniTemplateEditor().ShowDialog();
        }
        SizeTiles();ReloadManager();
    }
    private Border Tile(string target,string name,ImageSource? image)
    {
        bool isClock=target==ClockTarget;bool isUsage=UsageMini.IsTarget(target);bool isMini=AirthingsMini.IsTarget(target);var panel=new Grid{Margin=listMode?new Thickness(4):isClock?new Thickness(8):new Thickness(8,4,8,2)};
        if(listMode){panel.ColumnDefinitions.Add(new(){Width=new GridLength(isUsage?320:isMini?AirthingsMini.Span(target)==2?320:170:isClock?92:40)});panel.ColumnDefinitions.Add(new());}
        FrameworkElement icon;
        if(isClock){clock=new OpticallyCenteredText{Tag="ClockText",Text=DateTime.Now.ToString("t"),TextFontSize=listMode?24:77.33,TextFontFamily=new FontFamily("Segoe UI"),TextBrush=Brushes.White};clock.SetResourceReference(OpticallyCenteredText.TextFontFamilyProperty,"TekoFont");clock.SetResourceReference(OpticallyCenteredText.TextBrushProperty,"ClockBrush");icon=clock;}
        else if(isMini)icon=new AirthingsMini{Target=target};
        else if(UsageMini.IsTarget(target))icon=new UsageMini{Target=target};
        else icon=new AdaptiveIconImage{ShortcutTarget=target,Source=image,Margin=new Thickness(listMode?0:6),Stretch=Stretch.Uniform};
        if(icon is Image) RenderOptions.SetBitmapScalingMode(icon,BitmapScalingMode.HighQuality);
        if(isClock)clockState.Appearance.Apply(clock!,listMode?24/77.33:1,!listMode);
        panel.Children.Add(icon);
        if(listMode){var label=new OpticallyCenteredText{Text=name,TextFontFamily=new FontFamily("Segoe UI"),TextFontSize=24,TextBrush=Brushes.White,Ellipsis=true,ClipToBounds=true,Margin=new Thickness(12,0,0,0),InkAlignment=HorizontalAlignment.Left};label.SetResourceReference(OpticallyCenteredText.TextBrushProperty,"PanelTextBrush");Grid.SetColumn(label,1);panel.Children.Add(label);}
        if(!listMode&&!isClock&&!isMini&&!UsageMini.IsTarget(target)&&MacroLibrary.Current.PresentationFor(target,null) is string mode)
        {
            panel.Children.Clear();panel.ColumnDefinitions.Clear();
            if(mode=="Icon")panel.Children.Add(icon);
            else
            {
                var label=new OpticallyCenteredText{Text=name,TextFontFamily=new FontFamily("Segoe UI"),TextFontSize=18,TextBrush=Brushes.White,Ellipsis=true,ClipToBounds=true,CompactLineBox=true};
                if(mode=="IconText")
                {
                    if(listMode){panel.ColumnDefinitions.Add(new(){Width=new GridLength(36)});panel.ColumnDefinitions.Add(new());Grid.SetColumn(label,1);}
                    else{panel.RowDefinitions.Add(new());panel.RowDefinitions.Add(new(){Height=GridLength.Auto});Grid.SetRow(label,1);}
                    panel.Children.Add(icon);
                }
                panel.Children.Add(label);
            }
        }
        if(!isClock&&MacroLibrary.Current.TypographyFor(target) is {} typography)
            foreach(var label in panel.Children.OfType<OpticallyCenteredText>()){typography.Apply(label);LabelTypography.SetTextEffect(label,typography);}
        Panel rowContent=panel; if(listMode&&!isMini&&!isUsage){var row=new ListRowContent{Margin=new Thickness(4)};var children=panel.Children.Cast<UIElement>().ToArray();panel.Children.Clear();foreach(var child in children){if(child is FrameworkElement element)element.Margin=new Thickness(0);row.Children.Add(child);}rowContent=row;}
        var tile=new Border{Margin=new Thickness(5),CornerRadius=new CornerRadius(11),BorderThickness=new Thickness(1),BorderBrush=new SolidColorBrush(Color.FromArgb(53,255,255,255)),Background=Brushes.Transparent,Child=new MicrophoneMacroFrame{Target=target,Body=rowContent},Cursor=Cursors.Hand,ToolTip=new ToolTip{Content=MacroLibrary.IsReference(target)?MacroLibrary.Current.Description(target):name,Background=new SolidColorBrush(Color.FromRgb(23,23,23)),Foreground=new SolidColorBrush(Color.FromRgb(221,211,193)),BorderBrush=new SolidColorBrush(Color.FromRgb(65,65,65)),Padding=new Thickness(10,7,10,7)},Tag=target};tile.SetResourceReference(Border.BackgroundProperty,"ModuleCardSurfaceBrush");tiles.Children.Add(tile);
        var singleClick=new DeferredCellClick(tile);bool editingClick=false;
        tile.MouseEnter+=(_,_)=>tile.BorderBrush=new SolidColorBrush(Color.FromRgb(30,215,96));tile.MouseLeave+=(_,_)=>tile.BorderBrush=new SolidColorBrush(Color.FromArgb(53,255,255,255));
        tile.PreviewMouseLeftButtonDown+=(_,e)=>{if(e.StylusDevice!=null||Environment.TickCount64<suppressMouseUntil)return;singleClick.Cancel();if(e.ClickCount==2&&!MacroLibrary.IsReference(target)){editingClick=true;e.Handled=true;var edit=tile.ContextMenu?.Items.OfType<MenuItem>().FirstOrDefault(i=>Equals(i.Header,"Edit"));edit?.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));return;}editingClick=false;start=e.GetPosition(this);dragging=false;tile.CaptureMouse();e.Handled=true;};
        tile.PreviewMouseMove+=(_,e)=>{if(e.StylusDevice!=null||e.LeftButton!=MouseButtonState.Pressed||dragging||Environment.TickCount64<suppressMouseUntil)return;if((e.GetPosition(this)-start).Length<8)return;singleClick.Cancel();dragging=true;ShortcutDragSupport.BeginDrag(tile,image,name,new ShortcutTransfer(target,name,isClock?JsonSerializer.Serialize(clockState.Appearance):null,(_,_,_)=>{}));e.Handled=true;};
        tile.PreviewMouseLeftButtonUp+=(_,e)=>{if(e.StylusDevice!=null||Environment.TickCount64<suppressMouseUntil)return;if(tile.IsMouseCaptured)tile.ReleaseMouseCapture();e.Handled=true;if(!dragging&&!editingClick){void Activate(){if(!isClock&&!UsageMini.IsTarget(target)&&!AirthingsMini.TryActivate(target,Window.GetWindow(this)))MacroRunner.TryActivate(target,Window.GetWindow(this));}if(MacroLibrary.IsReference(target))Activate();else singleClick.Schedule(Activate);}editingClick=false;dragging=false;};
        tile.PreviewTouchDown+=(_,e)=>{if(touch!=null)return;touch=e.TouchDevice;start=e.GetTouchPoint(Window.GetWindow(this)).Position;dragging=false;touch.Capture(tile);e.Handled=true;};
        tile.PreviewTouchMove+=(_,e)=>{if(touch!=e.TouchDevice)return;var point=e.GetTouchPoint(Window.GetWindow(this)).Position;if((point-start).Length>=8)dragging=true;if(dragging)ShowPreview(tile,point);e.Handled=true;};
        tile.PreviewTouchUp+=(_,e)=>{if(touch!=e.TouchDevice)return;var owner=Window.GetWindow(this);var point=e.GetTouchPoint(owner).Position;touch.Capture(null);touch=null;ClosePreview();suppressMouseUntil=Environment.TickCount64+750;e.Handled=true;if(dragging)DropReference(owner,point,target,name);else if(!isClock&&!UsageMini.IsTarget(target)&&!AirthingsMini.TryActivate(target,owner))MacroRunner.TryActivate(target,owner);dragging=false;};
        tile.LostTouchCapture+=(_,_)=>{ClosePreview();touch=null;};return tile;
    }
    private void Guard(Action action){try{action();}catch(Exception ex){AppMessage.Show(Window.GetWindow(this),ex.Message,"Macro library");}}
    internal SettingsWindow CreateAirthingsMiniTemplateEditor()=>AirthingsMiniEditor.Create(Window.GetWindow(this),miniState,options=>{AtomicJson.Write(MiniPath,options);miniState=options;Refresh(this,EventArgs.Empty);},options=>{miniState=options;Refresh(this,EventArgs.Empty);});
    internal SettingsWindow CreateUsageMiniTemplateEditor()=>UsageMiniEditor.Create(Window.GetWindow(this),usageState,options=>{AtomicJson.Write(UsagePath,options);usageState=options;Refresh(this,EventArgs.Empty);},options=>{usageState=options;Refresh(this,EventArgs.Empty);});
    private FrameworkElement? managerView;
    private ListBox? managerList;
    private Guid? managerSelection;
    internal bool ManagerIsOpen=>managerView!=null;
    internal void CloseManager()
    {
        if(managerList?.SelectedItem is MacroDefinition selected)managerSelection=selected.Id;
        if(managerView!=null)body.Children.Remove(managerView);
        managerView=null;managerList=null;
        foreach(UIElement child in body.Children)child.Visibility=Visibility.Visible;
        RefreshStatus(this,EventArgs.Empty);
    }
    internal void OpenManager()
    {
        if(managerView!=null)return;
        foreach(UIElement child in body.Children)child.Visibility=Visibility.Collapsed;
        managerView=CreateManagerContent();body.Children.Add(managerView);
    }
    private void ReloadManager()
    {
        if(managerList==null)return;
        var selected=(managerList.SelectedItem as MacroDefinition)?.Id??managerSelection;
        int index=managerList.SelectedIndex;
        managerList.ItemsSource=MacroLibrary.Current.Items.ToArray();
        managerList.SelectedItem=MacroLibrary.Current.Items.FirstOrDefault(m=>m.Id==selected);
        if(managerList.SelectedItem==null&&managerList.Items.Count>0)managerList.SelectedIndex=Math.Clamp(index,0,managerList.Items.Count-1);
    }
    private FrameworkElement CreateManagerContent()
    {
        var root=new DockPanel{LastChildFill=true};
        var header=new DockPanel{Margin=new Thickness(0,0,0,8)};DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var close=MacroUi.Button("Back",(_,_)=>CloseManager());
        // Preserve the original button footprint; enlarge only its rendered caption.
        close.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
        close.Width=close.DesiredSize.Width-close.Margin.Left-close.Margin.Right;
        close.Height=close.DesiredSize.Height-close.Margin.Top-close.Margin.Bottom;
        close.Padding=new Thickness(0);
        close.Content=new OpticallyCenteredText{Text="Back",TextFontFamily=close.FontFamily,TextFontWeight=FontWeights.Normal,TextFontSize=22,CenterCapToBaseline=true,UseGlyphGeometry=true};
        ((OpticallyCenteredText)close.Content).SetBinding(OpticallyCenteredText.TextBrushProperty,new System.Windows.Data.Binding("Foreground"){Source=close});
        DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
        var title=new OpticallyCenteredText{Tag="ModuleTitle",Text="MANAGE MACROS",TextFontSize=24,TextFontWeight=FontWeights.Bold,InkAlignment=HorizontalAlignment.Left};title.SetResourceReference(OpticallyCenteredText.TextFontFamilyProperty,"TekoFont");title.SetResourceReference(OpticallyCenteredText.TextBrushProperty,"ModuleTitleBrush");header.Children.Add(title);
        managerList=new ListBox{DisplayMemberPath="Name",Background=Brushes.Transparent,FontSize=20,Padding=new Thickness(4)};
        PlaylistAppearance.ApplyChrome(managerList);
        managerList.SetResourceReference(Control.ForegroundProperty,"PanelTextBrush");managerList.SetResourceReference(Control.BorderBrushProperty,"PanelDividerBrush");
        managerList.SelectionChanged+=(_,_)=>{if(managerList?.SelectedItem is MacroDefinition selected)managerSelection=selected.Id;};
        var actions=new WrapPanel();DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);root.Children.Add(managerList);
        void EditSelectedMacro(){if(managerList?.SelectedItem is MacroDefinition m){MacroEditor.Show(Window.GetWindow(this),m);ReloadManager();}}
        actions.Children.Add(MacroUi.Button("Edit",(_,_)=>EditSelectedMacro()));
        managerList.MouseDoubleClick+=(_,e)=>{if(e.ChangedButton==MouseButton.Left&&ItemsControl.ContainerFromElement(managerList,e.OriginalSource as DependencyObject) is ListBoxItem){e.Handled=true;EditSelectedMacro();}};
        actions.Children.Add(MacroUi.Button("Duplicate",(_,_)=>{if(managerList?.SelectedItem is MacroDefinition m){Guard(()=>MacroLibrary.Current.Duplicate(m));ReloadManager();}}));
        actions.Children.Add(MacroUi.Button("Remove",(_,_)=>{if(managerList?.SelectedItem is MacroDefinition m){Guard(()=>MacroLibrary.Current.Remove(m.Id));ReloadManager();}}));
        foreach(var button in actions.Children.OfType<Button>().Append(close)){button.SetResourceReference(Control.BackgroundProperty,"ModuleCardSurfaceBrush");button.SetResourceReference(Control.ForegroundProperty,"PanelTextBrush");button.SetResourceReference(Control.BorderBrushProperty,"PanelDividerBrush");}
        ReloadManager();return root;
    }
    private void ShowPreview(FrameworkElement source,Point point)
    {
        var owner=Window.GetWindow(this);
        if(preview==null){var bounds=source.TransformToAncestor(owner).TransformBounds(new Rect(0,0,source.ActualWidth,source.ActualHeight));preview=new Popup{AllowsTransparency=true,PlacementTarget=owner,Placement=PlacementMode.Relative,IsHitTestVisible=false,StaysOpen=true,Child=new Border{Width=bounds.Width,Height=bounds.Height,Tag=start-new Vector(bounds.X,bounds.Y),Background=new VisualBrush(source),Opacity=.85}};}
        var grab=(Point)((FrameworkElement)preview.Child).Tag;preview.HorizontalOffset=point.X-grab.X;preview.VerticalOffset=point.Y-grab.Y;preview.IsOpen=true;
    }
    private void ClosePreview(){if(preview!=null)preview.IsOpen=false;preview=null;}
    internal static bool DropReference(Window owner,Point point,string target,string name,string? presentation=null)
    {
        DependencyObject? hit=owner.InputHitTest(point) as DependencyObject;
        while(hit!=null)
        {
            if(hit is Border {DataContext:QuickLaunchBar.QuickLaunchSlot quick}){var bar=Ancestor<QuickLaunchBar>(hit);if(target==ClockTarget)return bar?.AddLibraryClock(quick,LibraryClockAppearance())==true;if(AirthingsMini.IsTarget(target))return bar!=null&&MiniPlacement.Place(bar,quick,AirthingsMini.Decode(target));if(quick.IsAirthingsMini||quick.IsAirthingsReserved)return false;quick.SetTarget(target,name);quick.Presentation=presentation;bar?.SaveTouchShortcuts();return true;}
            if(hit is Border {DataContext:ShortcutsWidget.Slot slot}){var widget=Ancestor<ShortcutsWidget>(hit);if(target==ClockTarget)return widget?.SetControl(slot,"clock",LibraryClockAppearance())==true;if(AirthingsMini.IsTarget(target))return widget!=null&&MiniPlacement.Place(widget,slot,AirthingsMini.Decode(target));if(slot.HasControl||slot.IsReserved||slot.IsAirthingsMini)return false;slot.SetTarget(target,name);slot.Presentation=presentation;widget?.SaveTouchShortcuts();return true;}
            if(hit is WebShortcutsWidget web&&target!=ClockTarget&&!UsageMini.IsTarget(target)&&!AirthingsMini.IsTarget(target)&&!LegacyAirthingsStatusTarget.IsTarget(target)){web.AddMacroReference(target,presentation);return true;}
            hit=hit is Visual?VisualTreeHelper.GetParent(hit):null;
        }
        if(target==ClockTarget)return SystemMonitorWidget.DropClock(owner,point,LibraryClockAppearance());
        return false;
    }
    private static T? Ancestor<T>(DependencyObject child)where T:DependencyObject
    {for(DependencyObject? p=child;p!=null;p=VisualTreeHelper.GetParent(p))if(p is T value)return value;return null;}
}

// Fixed columns avoid WrapPanel's fractional-DPI reflow at the fourth cell.
internal sealed class MacroTilePanel:Panel
{
    internal int Columns=4;
    internal double Pitch=145,RowHeight=108;
    private int Place(Action<UIElement,int,int,int> action)
    {
        int row=0,column=0;
        foreach(UIElement child in InternalChildren)
        {
            int span=Columns>1&&child is FrameworkElement item&&(Equals(item.Tag,MacroLibraryCard.ClockTarget)||AirthingsMini.IsTarget(item.Tag as string))?Math.Clamp(Grid.GetColumnSpan(child),1,2):1;
            if(column+span>Columns){row++;column=0;}
            action(child,row,column,span);column+=span;if(column==Columns){row++;column=0;}
        }
        return row+(column>0?1:0);
    }
    protected override Size MeasureOverride(Size available)
    {
        if(Columns==1){double height=0;foreach(UIElement child in InternalChildren){double row=ListHeight(child);child.Measure(new Size(Pitch,row));height+=row;}return new Size(Pitch,height);}
        int rows=Place((child,row,column,span)=>child.Measure(new Size(Pitch*span,RowHeight)));
        return new Size(Columns*Pitch,rows*RowHeight);
    }
    protected override Size ArrangeOverride(Size size)
    {
        if(Columns==1){double top=0;foreach(UIElement child in InternalChildren){double height=ListHeight(child);child.Arrange(new Rect(0,top,Pitch,height));top+=height;}return size;}
        Place((child,row,column,span)=>child.Arrange(new Rect(column*Pitch,row*RowHeight,Pitch*span,RowHeight)));return size;
    }
    // 22DIP chrome/margins +12DIP room/age +~26DIP inline cap/descender space.
    // Existing macro/clock rows retain their original height.
    private double ListHeight(UIElement child)=>child is FrameworkElement item&&(AirthingsMini.IsTarget(item.Tag as string)||UsageMini.IsTarget(item.Tag as string))?Math.Max(RowHeight,64):RowHeight;
}







