using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Ekrano_valdymas
{
    public partial class WebShortcutsWidget : UserControl
    {
        public static readonly DependencyProperty ImagesOnlyProperty = DependencyProperty.Register(
            nameof(ImagesOnly),typeof(bool),typeof(WebShortcutsWidget),new PropertyMetadata(false,(d,e)=>((WebShortcutsWidget)d).LegacyViewChanged((bool)e.NewValue)));
        public bool ImagesOnly { get => (bool)GetValue(ImagesOnlyProperty); set { if(!synchronizingView)ViewMode=value?"Thumbs":"List";SetValue(ImagesOnlyProperty,value); } }
        public static readonly DependencyProperty ListRowHeightProperty=DependencyProperty.Register(nameof(ListRowHeight),typeof(double),typeof(WebShortcutsWidget),new PropertyMetadata(32d));
        public double ListRowHeight { get=>(double)GetValue(ListRowHeightProperty); set=>SetValue(ListRowHeightProperty,value); }
        public static readonly DependencyProperty ListTextSizeProperty=DependencyProperty.Register(nameof(ListTextSize),typeof(double),typeof(WebShortcutsWidget),new PropertyMetadata(14d,(d,e)=>d.SetValue(AddWebLinkListTextSizeProperty,(double)e.NewValue*2/3)));
        public double ListTextSize { get=>(double)GetValue(ListTextSizeProperty); set=>SetValue(ListTextSizeProperty,value); }
        public static readonly DependencyProperty AddWebLinkListTextSizeProperty=DependencyProperty.Register(nameof(AddWebLinkListTextSize),typeof(double),typeof(WebShortcutsWidget),new PropertyMetadata(14d*2/3));
        public double AddWebLinkListTextSize => (double)GetValue(AddWebLinkListTextSizeProperty);
        public static readonly DependencyProperty ListTextBrushProperty=DependencyProperty.Register(nameof(ListTextBrush),typeof(Brush),typeof(WebShortcutsWidget),new PropertyMetadata(Brushes.White));
        public Brush ListTextBrush {get=>(Brush)GetValue(ListTextBrushProperty);set=>SetValue(ListTextBrushProperty,value);}
        internal PlaylistSettings? ListAppearance {get;private set;}
        private double baseListTextSize=14;
        internal event Action<PlaylistSettings>? AppearanceChanged;
        internal void ApplyListAppearance(PlaylistSettings? value)
        {
            ListAppearance=value;if(value==null)return;
            ListTextSize=value.Size(false);
            var face=new Typeface(new FontFamily("Segoe UI"),FontStyles.Normal,FontWeights.Normal,FontStretches.Normal);
            double line=face.TryGetGlyphTypeface(out var glyph)?glyph.Height*ListTextSize:ListTextSize*1.5;
            ListRowHeight=Math.Max(20,line)+value.Gap;
            ListTextBrush=value.TextColor is string color?(Brush)new BrushConverter().ConvertFromString(color)!:Brushes.White;
        }
        public bool TelemetryEnabled { get; set; }
        public event Action<bool>? TelemetryChanged;
        internal void SetListSize(double value) { ListRowHeight=Math.Clamp(double.IsFinite(value)?value:32,28,64); baseListTextSize=Math.Round(13+(ListRowHeight-28)*.25);ListTextSize=baseListTextSize;ApplyListAppearance(ListAppearance); }
        internal event Action? BrowserToggleRequested;
        internal Func<Task>? ClearWebCacheRequested { get; set; }
        internal void SetBrowserOpen(bool open){browserToggle.ToolTip=open?"Close browser":"Open last used browser page";browserToggle.Opacity=open?1:.65;}
        private void BrowserToggle_Click(object sender,RoutedEventArgs e){e.Handled=true;BrowserToggleRequested?.Invoke();}
        private void Settings_Click(object sender,RoutedEventArgs e)
        {
            e.Handled=true;var menu=CreateSettingsMenu();menu.PlacementTarget=(UIElement)sender;menu.IsOpen=true;
        }
        internal ContextMenu CreateSettingsMenu()
        {
            var menu=ListAppearanceUi.Create(this);AddViewChoices(menu);
            PlaylistSettings Current()=>ListAppearance??new(){TitleSize=ListTextSize,RowGap=0};
            void Change(PlaylistSettings value){ApplyListAppearance(value);AppearanceChanged?.Invoke(value);}
            if(ViewMode=="List")
            {
            ListAppearanceUi.SliderRow(menu,"Text size","WebTitleSize",12,32,ListTextSize,v=>Change(Current() with{TitleSize=v}),baseListTextSize,"Base list size");
            ListAppearanceUi.SliderRow(menu,"Spacing","WebRowGap",0,32,Current().Gap,v=>Change(Current() with{RowGap=v}));
            var color=new MenuItem{Header="Text color…"};color.SetResourceReference(StyleProperty,"MiniMenuItemStyle");
            color.Click+=(_,_)=>{var original=Current();var initial=(ListTextBrush as SolidColorBrush)?.Color??Colors.White;if(ListAppearanceUi.PickColor(Window.GetWindow(this),initial,c=>ApplyListAppearance(original with{TextColor=c.ToString()}),out var chosen))Change(original with{TextColor=chosen.ToString()});else ApplyListAppearance(original);};menu.Items.Add(color);
            }
            if(ViewMode=="Icons")ListAppearanceUi.SliderRow(menu,"Icon size","WebIconSize",32,128,IconSize,v=>IconSize=v,48,"Default icon size");
            var check=new MenuItem{Header="Show telemetry",IsCheckable=true,IsChecked=TelemetryEnabled,StaysOpenOnClick=true,ToolTip="Show CPU, GPU, RAM and network usage in the browser's upper toolbar."};
            check.Click+=(_,_)=>{TelemetryEnabled=check.IsChecked;TelemetryChanged?.Invoke(TelemetryEnabled);};menu.Items.Add(check);
            var owner=Window.GetWindow(this);
            menu.Items.Add(new Separator());
            menu.Items.Add(WebCacheMaintenance.CreateCommand(owner,()=>ClearWebCacheRequested?.Invoke()??WebCacheMaintenance.ClearDiskCacheAsync(owner)));
            ListAppearanceUi.Compact(menu);SizeWebMenuRows(menu);return menu;
        }
        private static void SizeWebMenuRows(ContextMenu menu)
        {
            foreach(var item in menu.Items.OfType<MenuItem>())item.SetResourceReference(HeightProperty,"UnifiedMenuRowHeight");
        }
        internal ContextMenu CreateShortcutMenu(WebShortcut shortcut)
        {
            var menu=ListAppearanceUi.Create(this);
            void Add(string title,RoutedEventHandler action){var item=new MenuItem{Header=title,DataContext=shortcut};item.Click+=action;menu.Items.Add(item);}
            Add("Edit",Edit_Click);if(shortcut.IsMacro)Add("Edit macro",EditMacro_Click);
            Add("Move up",MoveUp_Click);Add("Move down",MoveDown_Click);menu.Items.Add(new Separator());Add("Remove",Remove_Click);
            SizeWebMenuRows(menu);return menu;
        }
        private void Tile_ContextMenuOpening(object sender,ContextMenuEventArgs e)
        {
            e.Handled=true;
            if(sender is not FrameworkElement{DataContext:WebShortcut shortcut} tile||shortcut.IsAdd)return;
            var menu=CreateShortcutMenu(shortcut);menu.PlacementTarget=tile;menu.Placement=System.Windows.Controls.Primitives.PlacementMode.MousePoint;menu.IsOpen=true;
        }
        private readonly ObservableCollection<WebShortcut> items = new();
        private Point mouseDownPoint;
        private WebShortcut? mouseDownItem;
        private WebShortcut? touchDragItem;
        private TouchDevice? touchDevice;
        private Point touchDownPoint;
        private bool mouseDragging;
        private long suppressPromotedMouseUntil;
        private Popup? touchDragPreview;
        private bool previewDemandQueued;
        private static readonly Brush NormalTileBorder = new SolidColorBrush(Color.FromArgb(0x48, 255, 255, 255));
        private readonly string settingsFile = Path.Combine(DiagnosticState.LocalApplicationData, "Ekrano_valdymas", "web-shortcuts.json");
        public event Action<WebShortcut>? OpenRequested;
        public event Action<bool>? ViewChanged;
        private void ListButton_Click(object sender,RoutedEventArgs e){e.Handled=true;ViewMode=ViewMode switch{"List"=>"Thumbs","Thumbs"=>"Icons",_=>"List"};}
        internal bool ExistingPreviewsDecoded => items
            .Where(item => !string.IsNullOrWhiteSpace(item.PreviewPath) && File.Exists(item.PreviewPath))
            .All(item => WebPreviewDecode.Load(item.PreviewPath,64,64) != null);

        public WebShortcutsWidget()
        {
            InitializeComponent();
            Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)]=MacroUi.Scroll(new Grid()).Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)];
            Load();
            RefreshTiles();
            Loaded+=(_,_)=>{MacroLibrary.Current.Changed+=MacrosChanged;MacrosChanged(this,EventArgs.Empty);QueuePreviewDemand();};
            Unloaded+=(_,_)=>{MacroLibrary.Current.Changed-=MacrosChanged;foreach(var item in items)item.SetPreviewDemand(0,0);};
            IsVisibleChanged+=(_,_)=>QueuePreviewDemand();
            SizeChanged+=(_,_)=>QueuePreviewDemand();
            tiles.ItemContainerGenerator.StatusChanged+=(_,_)=>QueuePreviewDemand();
        }

        private void PreviewScrollChanged(object sender,ScrollChangedEventArgs e)=>QueuePreviewDemand();
        private void QueuePreviewDemand()
        {
            if(previewDemandQueued)return;
            previewDemandQueued=true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>{previewDemandQueued=false;UpdatePreviewDemand();}));
        }

        private void UpdatePreviewDemand()
        {
            var viewport=new Rect(webScroll.RenderSize);
            foreach(var item in items)
            {
                if(IsLoaded&&IsVisible)item.RetryFaviconIfDue();
                int width=0,height=0;
                if(IsLoaded&&IsVisible&&ImagesOnly&&tiles.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement container&&container.IsVisible&&container.ActualWidth>0)
                {
                    var bounds=container.TransformToAncestor(webScroll).TransformBounds(new Rect(container.RenderSize));
                    if(bounds.IntersectsWith(viewport))
                    {
                        // Use physical display demand, including dashboard scale
                        // and monitor DPI; scrolling out releases decoded pixels.
                        var from=container.PointToScreen(new Point());
                        var to=container.PointToScreen(new Point(container.ActualWidth,container.ActualHeight));
                        width=WebPreviewDecode.Bucket(Math.Abs(to.X-from.X));
                        height=WebPreviewDecode.Bucket(Math.Abs(to.Y-from.Y));
                    }
                }
                item.SetPreviewDemand(width,height);
            }
        }

        private void MacrosChanged(object? sender,EventArgs e){foreach(var item in items)item.RefreshMacro();}
        internal void SavePage(string url,string title)
        {
            if(!NormalizeUrl(url,out string normalized))return;
            if(FindByUrl(normalized)==null)items.Add(new WebShortcut(string.IsNullOrWhiteSpace(title)?new Uri(normalized).Host:title,normalized));
            Save(true);RefreshTiles();
        }
        internal void AddMacroReference(string target,string? presentation=null)
        {
            if(!MacroLibrary.IsReference(target))return;
            items.Add(new WebShortcut(MacroLibrary.Current.Title(target),target){Presentation=presentation});Save();RefreshTiles();
        }
        internal ShortcutTransfer CreateShortcutTransfer(WebShortcut item)
        {
            string url=item.Url;string? presentation=item.Presentation,icon=item.IconPath;
            return new ShortcutTransfer(url,item.Name,presentation,icon,(target,name,appearance,replacementIcon)=>
            {
                if(string.IsNullOrWhiteSpace(target))items.Remove(item);
                else {item.Url=target;item.Name=name??"";item.Presentation=appearance;item.IconPath=replacementIcon;item.LastUrl="";item.PreviewPath="";}
                Save();RefreshTiles();
            }){SourceOwner=this,SourceSlot=item,
                SourceIsCurrent=()=>items.Contains(item)&&item.Url==url&&item.Presentation==presentation&&item.IconPath==icon,
                AcceptReplacement=target=>string.IsNullOrWhiteSpace(target)||CanStoreTransfer(target)};
        }
        private static bool CanStoreTransfer(string target)=>MacroLibrary.IsReference(target)||Uri.TryCreate(target,UriKind.Absolute,out var uri)&&uri.Scheme is "https" or "http";
        internal bool AcceptShortcutTransfer(ShortcutTransfer transfer,WebShortcut? before)
        {
            if(!transfer.CanMoveTo(null)||!CanStoreTransfer(transfer.Target))return false;
            if(ReferenceEquals(transfer.SourceOwner,this)&&transfer.SourceSlot is WebShortcut item&&items.Contains(item))
            {
                if(before==null||before.IsAdd)MoveToEnd(item);else MoveBefore(item,before);
                transfer.AcceptWithoutReplacement();return true;
            }
            var added=new WebShortcut(transfer.DisplayName,transfer.Target){Presentation=transfer.Presentation,IconPath=transfer.IconPath};
            int index=before==null||before.IsAdd?items.Count:items.IndexOf(before);
            if(index<0)return false;
            items.Insert(index,added);Save();RefreshTiles();transfer.CompleteWithPresentation(null,null,null,null);return true;
        }

        private void RefreshTiles()
        {
            tiles.ItemsSource = BuildVisibleTiles(items);
            QueuePreviewDemand();
        }

        internal static List<WebShortcut> BuildVisibleTiles(IEnumerable<WebShortcut> source)
        {
            List<WebShortcut> visible = source.Where(item => !item.IsAdd).ToList();
            visible.Add(WebShortcut.AddTile);
            return visible;
        }

        private void Tile_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if(IsCaptureInput(e.OriginalSource as DependencyObject)){mouseDownItem=null;mouseDragging=false;return;}
            if (e.ChangedButton != MouseButton.Left || sender is not Border { DataContext: WebShortcut item } ||
                Environment.TickCount64 < suppressPromotedMouseUntil) return;
            mouseDownPoint = e.GetPosition(this);
            mouseDownItem = item;
            mouseDragging = false;
        }

        private void Tile_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if(IsCaptureInput(e.OriginalSource as DependencyObject))return;
            if (e.LeftButton != MouseButtonState.Pressed || mouseDownItem is not { IsAdd: false } item || mouseDragging) return;
            Point point = e.GetPosition(this);
            if (Math.Abs(point.X - mouseDownPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - mouseDownPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            mouseDragging = true;
            DataObject data = new();
            data.SetData(ShortcutDragSupport.Format,CreateShortcutTransfer(item));
            ShortcutDragSupport.BeginVisualDrag((FrameworkElement)sender, data);
            mouseDownItem = null;
        }

        private void Tile_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if(IsCaptureInput(e.OriginalSource as DependencyObject)){mouseDownItem=null;mouseDragging=false;return;}
            if (e.ChangedButton != MouseButton.Left || sender is not Border { DataContext: WebShortcut item }) return;
            bool open = !mouseDragging && ReferenceEquals(item, mouseDownItem);
            mouseDownItem = null;
            mouseDragging = false;
            if (!open) return;
            e.Handled = true;
            Activate(item);
        }

        private void Activate(WebShortcut item)
        {
            if (item.IsAdd) { EditShortcut(null); return; }
            if(MacroRunner.TryActivate(item.Url,Window.GetWindow(this)))return;
            if (NormalizeUrl(item.Url, out string url)) { item.Url = url; OpenRequested?.Invoke(item); }
        }

        private void Tile_DragOver(object sender, DragEventArgs e)
        {
            var transfer=ShortcutDragSupport.ResolveTransfer(e.Data);
            e.Effects = transfer?.CanMoveTo(null)==true&&CanStoreTransfer(transfer.Target) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void Tile_DragEnter(object sender, DragEventArgs e)
        {
            if (sender is Border border && ShortcutDragSupport.ResolveTransfer(e.Data) is ShortcutTransfer t&&t.CanMoveTo(null)&&CanStoreTransfer(t.Target))
            {
                border.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                border.BorderThickness = new Thickness(2.5);
            }
        }

        private void Tile_DragLeave(object sender, DragEventArgs e) => ResetDropVisual(sender);

        private static void ResetDropVisual(object sender)
        {
            if (sender is not Border border) return;
            border.ClearValue(Border.BorderBrushProperty);
            border.ClearValue(Border.BorderThicknessProperty);
        }

        private void Tile_Drop(object sender, DragEventArgs e)
        {
            ResetDropVisual(sender);
            if(ShortcutDragSupport.ResolveTransfer(e.Data) is ShortcutTransfer transfer)
            {e.Effects=AcceptShortcutTransfer(transfer,(sender as Border)?.DataContext as WebShortcut)?DragDropEffects.Move:DragDropEffects.None;e.Handled=true;return;}
            if(e.Data.GetDataPresent(ShortcutDragSupport.Format)){e.Effects=DragDropEffects.None;e.Handled=true;return;}
            if (sender is Border { DataContext: WebShortcut target } && e.Data.GetData(typeof(WebShortcut)) is WebShortcut source)
                MoveBefore(source, target);
            e.Handled = true;
        }

        private void Tiles_Drop(object sender, DragEventArgs e)
        {
            if(ShortcutDragSupport.ResolveTransfer(e.Data) is ShortcutTransfer transfer)
            {e.Effects=AcceptShortcutTransfer(transfer,null)?DragDropEffects.Move:DragDropEffects.None;e.Handled=true;return;}
            if(e.Data.GetDataPresent(ShortcutDragSupport.Format)){e.Effects=DragDropEffects.None;e.Handled=true;return;}
            if (e.Data.GetData(typeof(WebShortcut)) is WebShortcut source) MoveToEnd(source);
            e.Handled = true;
        }

        private void Tile_PreviewTouchDown(object sender, TouchEventArgs e)
        {
            if(IsCaptureInput(e.OriginalSource as DependencyObject))return;
            if (sender is not Border { DataContext: WebShortcut item }) return;
            touchDevice = e.TouchDevice;
            touchDragItem = item;
            touchDownPoint = e.GetTouchPoint(tiles).Position;
            ((UIElement)sender).CaptureTouch(e.TouchDevice);
            e.Handled = true;
        }

        private void Tile_PreviewTouchMove(object sender, TouchEventArgs e)
        {
            if(IsCaptureInput(e.OriginalSource as DependencyObject))return;
            if (touchDevice != e.TouchDevice || touchDragItem is not { IsAdd: false } source) return;
            Point point = e.GetTouchPoint(tiles).Position;
            if (Math.Abs(point.X-touchDownPoint.X)<12&&Math.Abs(point.Y-touchDownPoint.Y)<12) return;
            if(sender is FrameworkElement sourceElement)
            {
                var owner=Window.GetWindow(this);var transfer=CreateShortcutTransfer(source);
                if(ShortcutDragSupport.BeginTouchDrag(sourceElement,e.TouchDevice,e.GetTouchPoint(owner).Position,transfer,
                    ()=>{suppressPromotedMouseUntil=Environment.TickCount64+750;CloseTouchDragPreview();}))
                {touchDevice=null;touchDragItem=null;mouseDownItem=null;mouseDragging=true;}
            }
            e.Handled = true;
        }

        private void Tile_PreviewTouchUp(object sender, TouchEventArgs e)
        {
            if(IsCaptureInput(e.OriginalSource as DependencyObject))return;
            if (touchDevice != e.TouchDevice || touchDragItem == null) return;
            WebShortcut item = touchDragItem;
            Point point = e.GetTouchPoint(tiles).Position;
            bool tapped = Math.Abs(point.X - touchDownPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                          Math.Abs(point.Y - touchDownPoint.Y) < SystemParameters.MinimumVerticalDragDistance;
            ((UIElement)sender).ReleaseTouchCapture(e.TouchDevice);
            touchDevice = null;
            touchDragItem = null;
            CloseTouchDragPreview();
            suppressPromotedMouseUntil = Environment.TickCount64 + 750;
            e.Handled = true;
            if (tapped) Activate(item);
            else if(MacroLibrary.IsReference(item.Url))
            {
                Window owner=Window.GetWindow(this);
                Point destination=e.GetTouchPoint(owner).Position;
                // Reordering within this Web collection already happened during movement.
                Point local=e.GetTouchPoint(this).Position;
                if(!new Rect(0,0,ActualWidth,ActualHeight).Contains(local))MacroLibraryCard.DropReference(owner,destination,item.Url,item.Name,item.Presentation);
            }
        }

        private void ShowTouchDragPreview(FrameworkElement source)
        {
            touchDragPreview = new Popup
            {
                AllowsTransparency = true, Placement = PlacementMode.AbsolutePoint,
                IsHitTestVisible = false, StaysOpen = true,
                Child = new Border
                {
                    Width = Math.Max(1, source.ActualWidth), Height = Math.Max(1, source.ActualHeight),
                    Background = new VisualBrush(source) { Stretch = Stretch.Fill },
                    BorderBrush = Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.White,
                    BorderThickness = new Thickness(2.5), CornerRadius = new CornerRadius(13),
                    Opacity = 0.96, IsHitTestVisible = false
                }
            };
            touchDragPreview.IsOpen = true;
        }

        private void UpdateTouchDragPreview(Point point)
        {
            if (touchDragPreview == null) return;
            Point screen = tiles.PointToScreen(point);
            PresentationSource? presentation = PresentationSource.FromVisual(this);
            if (presentation?.CompositionTarget != null)
                screen = presentation.CompositionTarget.TransformFromDevice.Transform(screen);
            touchDragPreview.HorizontalOffset = screen.X + 16;
            touchDragPreview.VerticalOffset = screen.Y + 16;
        }

        private void CloseTouchDragPreview()
        {
            if (touchDragPreview == null) return;
            touchDragPreview.IsOpen = false;
            touchDragPreview = null;
        }

        private WebShortcut? FindTileAt(Point point)
        {
            DependencyObject? hit = tiles.InputHitTest(point) as DependencyObject;
            while (hit != null && hit != tiles)
            {
                if (hit is Border { DataContext: WebShortcut item }) return item;
                hit = VisualTreeHelper.GetParent(hit);
            }
            return null;
        }

        private void MoveBefore(WebShortcut source, WebShortcut target)
        {
            if (source.IsAdd || ReferenceEquals(source, target)) return;
            int from = items.IndexOf(source);
            int to = target.IsAdd ? items.Count - 1 : items.IndexOf(target);
            if (from < 0 || to < 0 || from == to) return;
            if (!target.IsAdd && from < to) to--;
            items.Move(from, Math.Clamp(to, 0, items.Count - 1));
            Save(); RefreshTiles();
        }

        private void MoveToEnd(WebShortcut source)
        {
            int from = items.IndexOf(source);
            if (source.IsAdd || from < 0 || from == items.Count - 1) return;
            items.Move(from, items.Count - 1); Save(); RefreshTiles();
        }

        private void EditMacro_Click(object sender,RoutedEventArgs e)
        {if(ContextItem(sender) is {} item && MacroLibrary.Current.Find(item.Url) is {} macro)MacroEditor.Show(Window.GetWindow(this),macro);}
        private void Edit_Click(object sender, RoutedEventArgs e) { if (ContextItem(sender) is { IsAdd: false } item) EditShortcut(item); }
        private void Remove_Click(object sender, RoutedEventArgs e) { if (ContextItem(sender) is { IsAdd: false } item) { items.Remove(item); Save(); RefreshTiles(); } }
        private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(ContextItem(sender), -1);
        private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(ContextItem(sender), 1);
        private static WebShortcut? ContextItem(object sender) =>
            sender is MenuItem { DataContext: WebShortcut item } ? item : null;

        private void Move(WebShortcut? item, int delta)
        {
            if (item == null || item.IsAdd) return;
            int from = items.IndexOf(item), to = Math.Clamp(from + delta, 0, items.Count - 1);
            if (from == to) return;
            items.Move(from, to); Save(); RefreshTiles();
        }

        private void EditShortcut(WebShortcut? existing)
        {
            bool macro=existing!=null&&MacroLibrary.IsReference(existing.Url);
            var dialog=ShortcutAppearanceEditor.Create(Window.GetWindow(this),existing?.Name??"",existing?.Url??"",existing?.Presentation??(macro?(ImagesOnly?"Icon":"IconText"):"Original"),(name,url,presentation,chosenIcon)=>
            {
                if(existing==null)items.Add(new WebShortcut(name,url){Presentation=presentation,IconPath=chosenIcon});
                else { existing.Name=name;if(!macro&&existing.Url!=url){existing.Url=url;existing.LastUrl="";existing.PreviewPath="";}existing.Presentation=presentation;existing.IconPath=chosenIcon; }
                Save(true);RefreshTiles();
            },web:!macro,preview:existing==null?null:existing.PreviewAppearance,clearPreview:existing==null?null:existing.ClearAppearancePreview,iconPath:existing?.IconPath,previewIcon:existing==null?null:existing.PreviewIcon);
            dialog.ShowDialog();
        }

        private static bool NormalizeUrl(string input, out string url)
        {
            string value = input.Trim();
            if (!value.Contains("://", StringComparison.Ordinal)) value = "https://" + value;
            bool valid = Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.Scheme is "https" or "http";
            url = valid ? uri!.AbsoluteUri : "";
            return valid;
        }

        private void Load()
        {
            try { if (File.Exists(settingsFile)) foreach (WebShortcut item in JsonSerializer.Deserialize<List<WebShortcut>>(File.ReadAllText(settingsFile)) ?? new()) items.Add(item); } catch { }
        }
        private void Save(bool throwOnError=false)
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(settingsFile)!); AtomicJson.Write(settingsFile,items); } catch(Exception ex) { if(throwOnError)throw;InteractionDiagnostics.RecordFailure("Web shortcuts save",ex);return; }
            try{EntriesChanged?.Invoke();}catch(Exception ex){InteractionDiagnostics.RecordFailure("Web shortcut observer",ex);}
        }

        internal string CreatePreviewPath(WebShortcut item)
        {
            string folder = Path.Combine(Path.GetDirectoryName(settingsFile)!, "web-preview-cache");
            return Path.Combine(folder, $"preview-{DateTime.UtcNow.Ticks}-{Guid.NewGuid():N}.png");
        }

        internal void SetPreview(WebShortcut item, string path)
        {
            TrySetPreview(item,path);
        }

        internal string GetOpenUrl(WebShortcut item) =>
            NormalizeUrl(item.LastUrl, out string lastUrl) ? lastUrl : item.Url;

        internal WebShortcut? FindByUrl(string url) => items.FirstOrDefault(item =>
            !item.IsAdd && string.Equals(item.Url, url, StringComparison.OrdinalIgnoreCase));

        internal void SetLastUrl(WebShortcut item, string url)
        {
            if (!NormalizeUrl(url, out string normalized)) return;
            item.LastUrl = normalized;
            Save();
        }

        public sealed partial class WebShortcut : INotifyPropertyChanged
        {
            private string previewPath = "";
            private string name="";
            public string Name { get=>!string.IsNullOrWhiteSpace(name)?name:MacroLibrary.IsReference(Url)?MacroLibrary.Current.Title(Url):name; set{name=value;NotifyAppearance();} }
            private string url="";
            public string Url { get=>url; set { if(url==value)return;url=value??"";favicon=null;faviconRequested=false;PropertyChanged?.Invoke(this,new(nameof(Favicon)));PropertyChanged?.Invoke(this,new(nameof(IconOnlyImage)));PropertyChanged?.Invoke(this,new(nameof(ThumbnailImage))); } }
            private string? presentation;
            private string? previewName,previewPresentation,previewUrl;
            private string? iconPath,previewIconPath;
            public string? IconPath {get=>iconPath;set{iconPath=value;NotifyAppearance();}}
            internal void PreviewIcon(string? path){previewIconPath=path??"";NotifyAppearance();}

            [JsonIgnore] public string DisplayName=>previewName??Name;
            [JsonIgnore] public LabelTypography? MacroTypography=>MacroLibrary.Current.TypographyFor(Url);
            [JsonIgnore] public string? DisplayedPresentation=>previewPresentation??MacroLibrary.Current.PresentationFor(Url,Presentation);
            internal void PreviewAppearance(string name,string target,string presentation)
            {previewName=name;previewUrl=target;previewPresentation=presentation;NotifyAppearance();}
            internal void ClearAppearancePreview(){previewIconPath=null;previewName=null;previewUrl=null;previewPresentation=null;NotifyAppearance();}
            private void NotifyAppearance(){foreach(string property in new[]{nameof(MacroTypography),nameof(Name),nameof(DisplayName),nameof(Presentation),nameof(DisplayedPresentation),nameof(PreviewImage),nameof(IconOnlyImage),nameof(ThumbnailImage),nameof(ThumbnailStretch),nameof(PreviewEffectTarget),nameof(ThumbnailEffectTarget)})PropertyChanged?.Invoke(this,new(property));}
            public string? Presentation { get=>presentation;set { presentation=value=="Original"?"Original":ShortcutAppearanceEditor.Normalize(value);NotifyAppearance(); } }
            public string LastUrl { get; set; } = "";
            public bool IsAdd { get; set; }
            public string PreviewPath
            {
                get => previewPath;
                set
                {
                    previewPath = value ?? "";
                    previewImage = WebPreviewDecode.Load(previewPath,previewDecodeWidth,previewDecodeHeight);
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PreviewPath)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PreviewImage)));
                    PropertyChanged?.Invoke(this,new(nameof(ThumbnailImage)));PropertyChanged?.Invoke(this,new(nameof(ThumbnailStretch)));PropertyChanged?.Invoke(this,new(nameof(PreviewEffectTarget)));PropertyChanged?.Invoke(this,new(nameof(ThumbnailEffectTarget)));
                }
            }
            private ImageSource? previewImage;
            private int previewDecodeWidth,previewDecodeHeight;
            internal void SetPreviewDemand(int width,int height)
            {
                if(previewDecodeWidth==width&&previewDecodeHeight==height)return;
                previewDecodeWidth=width;previewDecodeHeight=height;
                previewImage=WebPreviewDecode.Load(previewPath,width,height);
                PropertyChanged?.Invoke(this,new(nameof(PreviewImage)));
                PropertyChanged?.Invoke(this,new(nameof(ThumbnailImage)));PropertyChanged?.Invoke(this,new(nameof(ThumbnailStretch)));PropertyChanged?.Invoke(this,new(nameof(PreviewEffectTarget)));PropertyChanged?.Invoke(this,new(nameof(ThumbnailEffectTarget)));
            }
            [JsonIgnore] public ImageSource? PreviewImage { get=>MacroLibrary.IsReference(Url)?MacroLibrary.Current.IconFor(Url):(DisplayedPresentation=="Original"?null:ShortcutIcon.Load(previewIconPath??IconPath))??(previewUrl!=null&&previewUrl!=Url?null:previewImage); }
            internal void RefreshMacro(){if(!MacroLibrary.IsReference(Url))return;NotifyAppearance();PropertyChanged?.Invoke(this,new(nameof(MacroTypography)));}
            [JsonIgnore] public bool IsMacro => MacroLibrary.IsReference(Url);
            private ImageSource? favicon;
            private bool faviconRequested;
            private DateTime faviconRetryAfter;
            internal void RetryFaviconIfDue()
            {
                if(IsAdd||IsMacro||favicon!=null||!faviconRequested||DateTime.UtcNow<faviconRetryAfter)return;
                faviconRequested=false;
                PropertyChanged?.Invoke(this,new(nameof(Favicon)));
                PropertyChanged?.Invoke(this,new(nameof(IconOnlyImage)));
                PropertyChanged?.Invoke(this,new(nameof(ThumbnailImage)));
            }
            [JsonIgnore] public ImageSource? Favicon { get { if(!faviconRequested&&!IsAdd){faviconRequested=true;faviconRetryAfter=DateTime.MaxValue;LoadFavicon();}return favicon; } }
            private async void LoadFavicon()
            {
                string requestedUrl=Url;var loaded=IsMacro?MacroLibrary.Current.IconFor(Url):await WebsiteIcon.LoadAsync(Url);
                if(requestedUrl!=Url)return;favicon=loaded;
                faviconRetryAfter=loaded==null?DateTime.UtcNow.AddSeconds(30):DateTime.MaxValue;
                PropertyChanged?.Invoke(this,new(nameof(Favicon)));
                PropertyChanged?.Invoke(this,new(nameof(IconOnlyImage)));
                PropertyChanged?.Invoke(this,new(nameof(ThumbnailImage)));
            }
            public string Symbol => IsAdd ? "+" : "◎";
            public WebShortcut() { }
            public WebShortcut(string name, string url) { Name = string.IsNullOrWhiteSpace(name) ? new Uri(url).Host : name.Trim(); Url = url; }
            public static WebShortcut AddTile => new() { Name = "ADD WEB LINK", IsAdd = true };
            public event PropertyChangedEventHandler? PropertyChanged;

        }
    }
}





