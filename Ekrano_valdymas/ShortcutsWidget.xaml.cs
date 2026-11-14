using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Ekrano_valdymas
{
    public partial class ShortcutsWidget : UserControl
    {
        private readonly string settingsFile;
        internal string SensorLocation(Slot slot)=>Path.GetFileName(settingsFile)+":"+Slots.IndexOf(slot);
        private readonly string namesFile;
        public static readonly DependencyProperty ListModeProperty = DependencyProperty.Register(nameof(ListMode),typeof(bool),typeof(ShortcutsWidget),new PropertyMetadata(false));
        public bool ListMode { get=>(bool)GetValue(ListModeProperty); private set=>SetValue(ListModeProperty,value); }
        private ItemsPanelTemplate? tilePanel;
        internal void SetListMode(bool value)
        {
            tilePanel ??= shortcutsItems.ItemsPanel;
            ListMode=value;
            shortcutsItems.ItemsPanel=value?new ItemsPanelTemplate(new FrameworkElementFactory(typeof(ShortcutListPanel))):tilePanel;
            listScroll.VerticalScrollBarVisibility=value?ScrollBarVisibility.Auto:ScrollBarVisibility.Disabled;
            dropOverlayCanvas.Visibility=value?Visibility.Collapsed:Visibility.Visible;
            AtomicJson.Write(settingsFile+".view.json",value);
        }
        private void ListButton_Click(object sender,RoutedEventArgs e){e.Handled=true;SetListMode(!ListMode);}
        public static readonly DependencyProperty GridColumnsProperty = DependencyProperty.Register(
            nameof(GridColumns), typeof(int), typeof(ShortcutsWidget), new PropertyMetadata(4));

        public int GridColumns
        {
            get => (int)GetValue(GridColumnsProperty);
            private set => SetValue(GridColumnsProperty, value);
        }

        public ObservableCollection<Slot> Slots { get; } = new();
        public bool ShowShortcutNames
        {
            set
            {
                foreach (Slot slot in Slots) slot.ShowName = value;
            }
        }
        private Point dragStart;
        private Slot? pressedSlot;
        private bool dragStarted;
        private Border? touchDragSourceBorder;
        private Border? touchDropBorder;
        private Slot? touchDropSlot;
        private ShortcutsWidget? touchDropShortcutsOwner;
        private QuickLaunchBar? touchDropQuickLaunchOwner;
        private QuickLaunchBar.QuickLaunchSlot? touchDropQuickLaunchSlot;
        private Popup? controlDragPreview;
        private readonly DispatcherTimer controlClockTimer;
        private static readonly Brush NormalBorder =
            new SolidColorBrush(Color.FromArgb(0x35, 255, 255, 255));
        private static readonly Brush ActiveBorder =
            new SolidColorBrush(Color.FromRgb(30, 215, 96));

        public ShortcutsWidget() : this("module-shortcuts", 4)
        {
        }

        internal ShortcutsWidget(string storageKey, int columns = 4)
        {
            GridColumns = Math.Max(2, columns);
            settingsFile = Path.Combine(
                DiagnosticState.LocalApplicationData,
                "Ekrano_valdymas", $"{storageKey}.json");
            namesFile = settingsFile + ".names.json";
            if (storageKey == "module-shortcuts-wide" && !File.Exists(settingsFile))
            {
                try
                {
                    string folder = Path.GetDirectoryName(settingsFile)!;
                    string wide3 = Path.Combine(folder, "module-shortcuts-wide-3.json");
                    string wide2 = Path.Combine(folder, "module-shortcuts-wide-2.json");
                    string? source = File.Exists(wide3) ? wide3 : File.Exists(wide2) ? wide2 : null;
                    if (source != null)
                    {
                        Directory.CreateDirectory(folder);
                        File.Copy(source, settingsFile, overwrite: false);
                    }
                }
                catch { }
            }
            InitializeComponent();
            LoadSlots();
            try{if(File.Exists(settingsFile+".view.json"))SetListMode(JsonSerializer.Deserialize<bool>(File.ReadAllText(settingsFile+".view.json")));}catch{}
            SizeChanged+=(_,_)=>shortcutsItems.InvalidateMeasure();
            listScroll.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)]=MacroUi.Scroll(new Grid()).Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)];
            controlClockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            controlClockTimer.Tick += (_, _) => RefreshDynamicControls();
            controlClockTimer.Start();
            Loaded += (_, _) =>
            {
                MacroLibrary.Current.Changed+=MacroTypographyChanged;
                SensorCellPreferences.Changed+=NormalizeSensorSpans;
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(NormalizeSensorSpans));
                controlClockTimer.Start();
                FindQuickLaunchBar()?.RegisterExternalControls(
                    Slots.Where(slot => slot.ControlKey != null).Select(slot => slot.ControlKey!));
            };
            Unloaded += (_, _) => {controlClockTimer.Stop();MacroLibrary.Current.Changed-=MacroTypographyChanged;SensorCellPreferences.Changed-=NormalizeSensorSpans;};
        }

        private void MacroTypographyChanged(object? sender,EventArgs e)
        {foreach(var slot in Slots)if(MacroLibrary.IsReference(slot.Target))slot.RefreshMacro();}

        private void RefreshDynamicControls()
        {
            QuickLaunchBar? quickLaunch = FindQuickLaunchBar();
            var obs = quickLaunch?.GetObsControlState() ?? (false, false, false, "00:00:00");
            foreach (Slot slot in Slots)
            {
                slot.RefreshControlDisplay();
                if (MacroLibrary.IsReference(slot.Target)) slot.RefreshMacro();
                slot.UpdateObsState(obs.ObsRunning, obs.Recording, obs.BufferActive, obs.ElapsedText);
            }
        }

        private void LoadSlots()
        {
            string?[] targets = Array.Empty<string?>();
            string?[] names = Array.Empty<string?>();
            string?[] presentations=Array.Empty<string?>();string?[] icons=Array.Empty<string?>();
            try
            {
                if (File.Exists(settingsFile))
                    targets = JsonSerializer.Deserialize<string?[]>(File.ReadAllText(settingsFile)) ?? targets;
                if (File.Exists(namesFile))
                    names = JsonSerializer.Deserialize<string?[]>(File.ReadAllText(namesFile)) ?? names;
            }
            catch { }

            try { if(File.Exists(settingsFile+".presentation.json"))presentations=JsonSerializer.Deserialize<string?[]>(File.ReadAllText(settingsFile+".presentation.json"))??presentations; } catch(Exception ex){InteractionDiagnostics.RecordFailure("Shortcut presentation load",ex);}
            try{if(File.Exists(settingsFile+".icons.json"))icons=JsonSerializer.Deserialize<string?[]>(File.ReadAllText(settingsFile+".icons.json"))??icons;}catch(Exception ex){InteractionDiagnostics.RecordFailure("Shortcut icons load",ex);}
            for (int i = 0; i < GridColumns * 3; i++)
                Slots.Add(new Slot(i < targets.Length ? targets[i] : null, i < names.Length ? names[i] : null) { IconPath=i<icons.Length?icons[i]:null,Presentation=i<presentations.Length?presentations[i]:null });
            try
            {
                if(File.Exists(settingsFile+".clocks.json"))
                {
                    var clocks=JsonSerializer.Deserialize<ClockAppearance[]>(File.ReadAllText(settingsFile+".clocks.json"));
                    for(int i=0;i<Slots.Count&&i<(clocks?.Length??0);i++)Slots[i].ClockStyle=clocks![i]??new();
                }
            }
            catch(Exception ex){InteractionDiagnostics.RecordFailure("Clock appearance load",ex);}
            NormalizeSensorSpans();
            NormalizeAirthingsMiniSpans();
            if (NormalizeClockSpans()) SaveSlots();
        }

        private void SaveSlots()
        {
            NormalizeAirthingsMiniSpans();
            NormalizeSensorSpans();
            Directory.CreateDirectory(Path.GetDirectoryName(settingsFile)!);
            string?[] targets = new string?[Slots.Count];
            string?[] names = new string?[Slots.Count];
            for (int i = 0; i < Slots.Count; i++)
            {
                targets[i] = Slots[i].ControlKey is string key ? $"control:{key}" :
                    Slots[i].IsReserved ? "reserved:"+Slots[i].ReservedKey : Slots[i].Target;
                names[i] = Slots[i].CustomName;
            }
            File.WriteAllText(settingsFile, JsonSerializer.Serialize(targets));
            File.WriteAllText(namesFile, JsonSerializer.Serialize(names));
            AtomicJson.Write(settingsFile+".presentation.json",Slots.Select(slot=>slot.Presentation).ToArray());AtomicJson.Write(settingsFile+".icons.json",Slots.Select(slot=>slot.IconPath).ToArray());
            AtomicJson.Write(settingsFile+".clocks.json",Slots.Select(slot=>slot.ClockStyle).ToArray());
        }

        private static SensorTile? FindSensorTile(DependencyObject root){if(root is SensorTile tile)return tile;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var match=FindSensorTile(VisualTreeHelper.GetChild(root,i));if(match!=null)return match;}return null;}
        private void Slot_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border { DataContext: Slot slot }) return;
            if(e.ClickCount==2&&slot.IsSensor){FindSensorTile((DependencyObject)sender)?.ResetZoom();e.Handled=true;return;}
            pressedSlot = slot;
            dragStart = e.GetPosition(this);
            dragStarted = false;
            ((UIElement)sender).CaptureMouse();
            if (slot.HasControl)
            {
                FindQuickLaunchBar()?.PrepareExternalControlActivation();
                ((UIElement)sender).CaptureMouse();
            }
            e.Handled = true;
        }

        private void Slot_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (pressedSlot is null || pressedSlot.IsEmpty ||
                e.LeftButton != MouseButtonState.Pressed) return;

            Point current = e.GetPosition(this);
            if (Math.Abs(current.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(current.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            if (pressedSlot.HasControl)
            {
                if (!dragStarted && sender is Border controlBorder)
                {
                    dragStarted = true;
                    touchDragSourceBorder = controlBorder;
                    ShowControlDragPreview(controlBorder, current);
                    controlBorder.BorderBrush = ActiveBorder;
                }
                else UpdateControlDragPreview(current);
                SetTouchDropTarget(FindSlotAt(current));
                e.Handled = true;
                return;
            }
            if (string.IsNullOrWhiteSpace(pressedSlot.Target)) return;

            if(dragStarted)return;

            dragStarted = true;
            Slot source = pressedSlot;
            pressedSlot = null;
            ShortcutTransfer transfer = CreateShortcutTransfer(source);
            ShortcutDragSupport.BeginDrag(
                (FrameworkElement)sender,
                source.Icon,
                source.Name,
                transfer);
            pressedSlot = null;
            e.Handled = true;
        }

        internal ShortcutTransfer CreateShortcutTransfer(Slot source)
        {
            string target=source.Target!,name=source.Name;string? presentation=source.Presentation,icon=source.IconPath;
            // This widget and its immutable settingsFile belong to the origin
            // tab even after MainWindow replaces the visible module hosts.
            return new ShortcutTransfer(
                target,name,presentation,icon,
                (replacementTarget, replacementName, replacementPresentation,replacementIcon) =>
                {
                    source.SetTarget(replacementTarget, replacementName);
                    source.Presentation=replacementPresentation;source.IconPath=replacementIcon;
                    SaveSlots();
                }){SourceLocation=SensorLocation(source),SourceOwner=this,SourceSlot=source,
                    SourceIsCurrent=()=>Slots.Contains(source)&&source.Target==target&&source.Presentation==presentation&&source.IconPath==icon&&!source.HasControl&&!source.IsReserved};
        }

        private void Slot_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            Slot? slot = pressedSlot;
            pressedSlot = null;
            if (sender is UIElement element && element.IsMouseCaptured) element.ReleaseMouseCapture();
            if (slot?.HasControl == true && dragStarted)
            {
                CompleteControlDrag(slot, e.GetPosition(this));
                ResetTouchDragVisuals();
                e.Handled = true;
                return;
            }
            if (slot is null || dragStarted) return;

            if (slot.ControlKey is string controlKey)
            {
                if (sender is FrameworkElement activationSource)
                    FindQuickLaunchBar()?.ActivateExternalControl(controlKey, activationSource);
                return;
            }

            if (slot.IsEmpty)
            {
                OpenFileDialog dialog = new() { Title = "Choose shortcut", Filter = "Programs and files|*.exe;*.lnk;*.bat;*.cmd;*.*|All files|*.*" };
                if (dialog.ShowDialog() == true) { slot.Target = dialog.FileName; SaveSlots(); }
                return;
            }

            if(slot.IsSensor)return;
            if(UsageMini.IsTarget(slot.Target)||AirthingsMini.TryActivate(slot.Target,Window.GetWindow(this)))return;
            if (MacroRunner.TryActivate(slot.Target, Window.GetWindow(this))) return;
            try { Process.Start(new ProcessStartInfo(ShellTargetPath.Resolve(slot.Target!)) { UseShellExecute = true }); }
            catch (Exception ex) { AppMessage.Show(ex.Message, "Shortcut error", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void Slot_PreviewTouchDown(object sender, TouchEventArgs e)
        {
            if (sender is not Border { DataContext: Slot slot } border) return;
            pressedSlot = slot;
            dragStart = e.GetTouchPoint(this).Position;
            dragStarted = false;
            touchDragSourceBorder = border;
            if (slot.HasControl) FindQuickLaunchBar()?.PrepareExternalControlActivation();
            ClearTouchDropTarget();
            border.CaptureTouch(e.TouchDevice);
            e.Handled = true;
        }

        private void Slot_PreviewTouchMove(object sender, TouchEventArgs e)
        {
            if (pressedSlot is null || pressedSlot.IsEmpty || sender is not Border border) return;
            Point current = e.GetTouchPoint(this).Position;
            if (!dragStarted &&
                Math.Abs(current.X - dragStart.X) < 12 &&
                Math.Abs(current.Y - dragStart.Y) < 12)
            {
                e.Handled = true;
                return;
            }

            if (!dragStarted)
            {
                if(!pressedSlot.HasControl&&pressedSlot.Target!=null)
                {
                    var source=pressedSlot;pressedSlot=null;dragStarted=true;
                    if(ShortcutDragSupport.BeginTouchDrag(border,e.TouchDevice,e.GetTouchPoint(Window.GetWindow(this)).Position,CreateShortcutTransfer(source),ResetTouchDragVisuals))
                    {e.Handled=true;return;}
                    pressedSlot=source;dragStarted=false;
                }
                dragStarted = true;
                if (pressedSlot.HasControl) ShowControlDragPreview(border, current);
                border.BorderBrush = ActiveBorder;
            }

            if (pressedSlot.HasControl) UpdateControlDragPreview(current);

            SetTouchDropTarget(FindSlotAt(current));
            e.Handled = true;
        }

        private void Slot_PreviewTouchUp(object sender, TouchEventArgs e)
        {
            if (sender is Border border) border.ReleaseTouchCapture(e.TouchDevice);
            Slot? slot = pressedSlot;
            pressedSlot = null;
            e.Handled = true;
            if (slot is null)
            {
                ResetTouchDragVisuals();
                return;
            }

            if (dragStarted)
            {
                if(slot.IsAirthingsMini)
                {
                    if(touchDropQuickLaunchOwner!=null&&touchDropQuickLaunchSlot!=null)MiniPlacement.Place(touchDropQuickLaunchOwner,touchDropQuickLaunchSlot,AirthingsMini.Decode(slot.Target),this,slot);else if(touchDropSlot!=null)MiniPlacement.Place(touchDropShortcutsOwner??this,touchDropSlot,AirthingsMini.Decode(slot.Target),this,slot);ResetTouchDragVisuals();return;
                }
                if(touchDropSlot?.IsAirthingsMini==true||touchDropSlot?.IsReserved==true||touchDropQuickLaunchSlot?.IsAirthingsMini==true||touchDropQuickLaunchSlot?.IsAirthingsReserved==true){ResetTouchDragVisuals();return;}
                if (slot.HasControl)
                {
                    CompleteControlDrag(slot, e.GetTouchPoint(this).Position);
                    ResetTouchDragVisuals();
                    return;
                }
                if (string.IsNullOrWhiteSpace(slot.Target)) { ResetTouchDragVisuals(); return; }
                if (touchDropQuickLaunchOwner != null && touchDropQuickLaunchSlot != null)
                {
                    string? destinationTarget = touchDropQuickLaunchSlot.Target;
                    string destinationName = touchDropQuickLaunchSlot.DisplayName;
                    string? destinationPresentation=touchDropQuickLaunchSlot.Presentation;string? destinationIconPath=touchDropQuickLaunchSlot.IconPath;
                    string? sourcePresentation=slot.Presentation;string? sourceIconPath=slot.IconPath;
                    SensorCellPreferences.Exchange(SensorLocation(slot),slot.Target,"top:"+touchDropQuickLaunchSlot.Index,destinationTarget);
                    touchDropQuickLaunchSlot.SetTarget(slot.Target, slot.Name);
                    touchDropQuickLaunchSlot.Presentation=sourcePresentation;touchDropQuickLaunchSlot.IconPath=sourceIconPath;
                    slot.SetTarget(destinationTarget, destinationName);
                    slot.Presentation=destinationPresentation;slot.IconPath=destinationIconPath;
                    SensorCellPreferences.NotifyPlacement();
                    touchDropQuickLaunchOwner.SaveTouchShortcuts();
                    SaveSlots();
                }
                else if (touchDropSlot != null && !ReferenceEquals(slot, touchDropSlot))
                {
                    string? destinationTarget = touchDropSlot.Target;
                    string destinationName = touchDropSlot.Name;
                    string? destinationPresentation=touchDropSlot.Presentation;string? destinationIconPath=touchDropSlot.IconPath;
                    string? sourcePresentation=slot.Presentation;string? sourceIconPath=slot.IconPath;
                    SensorCellPreferences.Exchange(SensorLocation(slot),slot.Target,(touchDropShortcutsOwner??this).SensorLocation(touchDropSlot),destinationTarget);
                    touchDropSlot.SetTarget(slot.Target, slot.Name);
                    touchDropSlot.Presentation=sourcePresentation;touchDropSlot.IconPath=sourceIconPath;
                    slot.SetTarget(destinationTarget, destinationName);
                    slot.Presentation=destinationPresentation;slot.IconPath=destinationIconPath;
                    SensorCellPreferences.NotifyPlacement();
                    SaveSlots();
                }

                ResetTouchDragVisuals();
                return;
            }

            ResetTouchDragVisuals();

            if (slot.ControlKey is string controlKey)
            {
                if (sender is FrameworkElement activationSource)
                    FindQuickLaunchBar()?.ActivateExternalControl(controlKey, activationSource);
                return;
            }

            if (slot.IsEmpty)
            {
                OpenFileDialog dialog = new() { Title = "Choose shortcut", Filter = "Programs and files|*.exe;*.lnk;*.bat;*.cmd;*.*|All files|*.*" };
                if (dialog.ShowDialog() == true) { slot.Target = dialog.FileName; SaveSlots(); }
                return;
            }
            if(slot.IsSensor)return;
            if(UsageMini.IsTarget(slot.Target)||AirthingsMini.TryActivate(slot.Target,Window.GetWindow(this)))return;
            if (MacroRunner.TryActivate(slot.Target, Window.GetWindow(this))) return;
            try { Process.Start(new ProcessStartInfo(ShellTargetPath.Resolve(slot.Target!)) { UseShellExecute = true }); }
            catch (Exception ex) { AppMessage.Show(ex.Message, "Shortcut error", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private TouchDropTarget? FindSlotAt(Point point)
        {
            Window? window = Window.GetWindow(this);
            if (window == null) return null;

            Point windowPoint = TranslatePoint(point, window);
            for (DependencyObject? current = window.InputHitTest(windowPoint) as DependencyObject;
                 current != null;
                 current = VisualTreeHelper.GetParent(current))
            {
                if (current is Border { DataContext: Slot slot } target)
                    return new TouchDropTarget(target, slot, null, null);
                if (current is Border { DataContext: QuickLaunchBar.QuickLaunchSlot quickSlot } quickTarget)
                {
                    QuickLaunchBar? owner = FindVisualAncestor<QuickLaunchBar>(quickTarget);
                    if (owner != null)
                        return new TouchDropTarget(quickTarget, null, owner, quickSlot);
                }
            }

            return null;
        }

        private void SetTouchDropTarget(TouchDropTarget? target, string? externalControlKey = null, int externalSpan=2)
        {
            string? draggedControlKey = pressedSlot?.ControlKey ?? externalControlKey;
            int draggedSpan=pressedSlot?.ClockStyle.Span??externalSpan;
            var targetOwner=target?.ShortcutsSlot!=null?FindVisualAncestor<ShortcutsWidget>(target.Border):null;
            if (draggedControlKey == "clock" && target?.ShortcutsSlot is { } hoveredSlot && targetOwner!=null&&
                targetOwner.TryResolveControlDropTarget(target.Border, hoveredSlot, draggedControlKey,
                    out Border resolvedBorder, out Slot resolvedSlot, draggedSpan))
                target = target with { Border = resolvedBorder, ShortcutsSlot = resolvedSlot };

            if (ReferenceEquals(touchDropBorder, target?.Border)) return;
            ClearTouchDropTarget();
            if (target == null) return;

            touchDropBorder = target.Border;
            touchDropSlot = target.ShortcutsSlot;
            touchDropShortcutsOwner=targetOwner;
            touchDropQuickLaunchOwner = target.QuickLaunchOwner;
            touchDropQuickLaunchSlot = target.QuickLaunchSlot;
            if (draggedControlKey == "clock" && touchDropSlot != null)
            {
                bool canAccept = pressedSlot == null||targetOwner!=this
                    ? targetOwner?.CanAcceptControl(touchDropSlot, "clock",draggedSpan)==true
                    : CanAcceptMovingControl(pressedSlot, touchDropSlot, "clock");
                if (!canAccept)
                {
                    touchDropBorder = null;
                    touchDropSlot = null;
                    touchDropQuickLaunchOwner = null;
                    touchDropQuickLaunchSlot = null;
                    return;
                }

                Point overlayPosition = touchDropBorder.TranslatePoint(new Point(), dropOverlayCanvas);
                Canvas.SetLeft(clockDropOverlay, overlayPosition.X);
                Canvas.SetTop(clockDropOverlay, overlayPosition.Y);
                clockDropOverlay.Width = Math.Max(0, touchDropBorder.ActualWidth * draggedSpan + (draggedSpan-1)*10);
                clockDropOverlay.Height = Math.Max(0, touchDropBorder.ActualHeight);
                clockDropOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                touchDropBorder.BorderBrush = ActiveBorder;
                touchDropBorder.Background = new SolidColorBrush(Color.FromArgb(55, 30, 215, 96));
            }
        }

        private void ClearTouchDropTarget()
        {
            if (touchDropBorder != null)
            {
                if (touchDropQuickLaunchSlot != null)
                {
                    touchDropBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255));
                    touchDropBorder.SetResourceReference(
                        Border.BackgroundProperty,
                        "ModuleCardSurfaceBrush");
                }
                else
                {
                    touchDropBorder.BorderBrush = NormalBorder;
                    touchDropBorder.SetResourceReference(Border.BackgroundProperty,"ModuleCardSurfaceBrush");
                }
            }

            touchDropBorder = null;
            touchDropSlot = null;
            touchDropShortcutsOwner=null;
            touchDropQuickLaunchOwner = null;
            touchDropQuickLaunchSlot = null;
            clockDropOverlay.Visibility = Visibility.Collapsed;
        }

        internal void ShowExternalControlDropTarget(Border border, Slot slot, string key,int span=2) =>
            SetTouchDropTarget(new TouchDropTarget(border, slot, null, null), key,span);

        internal void ClearExternalControlDropTarget() => ClearTouchDropTarget();

        private bool CanAcceptMovingControl(Slot source, Slot destination, string key)
        {
            if(key!="clock"||source.ClockStyle.Span==2)destination = ResolveControlDropSlot(destination, key);
            if (ReferenceEquals(source, destination)) return true;
            if (destination.IsReserved||destination.IsAirthingsMini) return false;
            if (key != "clock"||source.ClockStyle.Span==1) return destination.ControlKey != "clock"||destination.ClockStyle.Span==1;
            int destinationIndex = Slots.IndexOf(destination);
            if (!ShortcutsGridPlanner.CanPlaceWideControl(destinationIndex, Slots.Count, GridColumns)) return false;
            Slot second = Slots[destinationIndex + 1];
            if(second.IsAirthingsMini||second.ReservedKey=="airthings")return false;
            return !second.IsReserved || ReferenceEquals(second, source) || second.ControlKey == "clock";
        }

        private void ResetTouchDragVisuals()
        {
            if (touchDragSourceBorder != null)
            {
                touchDragSourceBorder.Opacity = 1;
                touchDragSourceBorder.BorderBrush = NormalBorder;
            }
            touchDragSourceBorder = null;
            ClearTouchDropTarget();
            CloseControlDragPreview();
            dragStarted = false;
        }

        private void CompleteControlDrag(Slot source, Point point)
        {
            string? key = source.ControlKey;
            if (key == null) return;
            if(key=="clock"&&Window.GetWindow(this) is {} window&&SystemMonitorWidget.DropClock(window,TranslatePoint(point,window),source.ClockStyle))
            {ClearControl(source,key);SaveSlots();return;}

            if (touchDropSlot != null && !ReferenceEquals(source, touchDropSlot))
            {
                if(touchDropShortcutsOwner!=null&&touchDropShortcutsOwner!=this)
                {
                    MoveControlTo(source,touchDropShortcutsOwner,touchDropSlot);
                    return;
                }
                MoveControl(source, touchDropSlot, key);
                return;
            }

            QuickLaunchBar? quickLaunch = FindQuickLaunchBar();
            if (quickLaunch != null && IsPointInside(point, quickLaunch))
            {
                double dropX = TranslatePoint(point, quickLaunch).X;
                if (quickLaunch.RestoreExternalControl(key, dropX,source.ClockStyle))
                {
                    if(key=="clock")quickLaunch.SetClockAppearance(source.ClockStyle);
                    ClearControl(source, key);
                    SaveSlots();
                }
            }
        }

        private void MoveControl(Slot source, Slot destination, string key)
        {
            if(key!="clock"||source.ClockStyle.Span==2)destination = ResolveControlDropSlot(destination, key);
            if (ReferenceEquals(source, destination) || destination.IsReserved||destination.IsAirthingsMini) return;
            int sourceIndex = Slots.IndexOf(source);
            int destinationIndex = Slots.IndexOf(destination);
            if (sourceIndex < 0 || destinationIndex < 0) return;

            if (key == "clock"&&source.ClockStyle.Span==2)
            {
                Slot? reservation = sourceIndex + 1 < Slots.Count && Slots[sourceIndex + 1].IsReserved
                    ? Slots[sourceIndex + 1] : null;
                if (reservation == null ||
                    !ShortcutsGridPlanner.CanPlaceWideControl(destinationIndex, Slots.Count, GridColumns)) return;
                if (!CanAcceptMovingControl(source, destination, key)) return;
                var moved = ShortcutsGridPlanner.MovePair(Slots.ToList(), sourceIndex, destinationIndex, GridColumns);
                for (int index = 0; index < moved.Count; index++) Slots[index] = moved[index];
                SaveSlots();
                return;
            }

            Slot displaced = Slots[destinationIndex];
            if (displaced.ControlKey == "clock"&&displaced.ClockStyle.Span==2) return;
            Slots[sourceIndex] = displaced;
            Slots[destinationIndex] = source;
            SaveSlots();
        }

        private void ClearControl(Slot source, string key)
        {
            int index = Slots.IndexOf(source);
            source.Clear();
            if (key == "clock" && index + 1 < Slots.Count && Slots[index + 1].IsReserved)
                Slots[index + 1].Clear();
        }

        internal bool MoveControlTo(Slot source,ShortcutsWidget destination,Slot slot)
        {
            if(source.ControlKey is not string key||!Slots.Contains(source)||!destination.SetControl(slot,key,source.ClockStyle))return false;
            ClearControl(source,key);SaveSlots();return true;
        }

        private bool IsPointInside(Point localPoint, FrameworkElement target)
        {
            Point point = TranslatePoint(localPoint, target);
            return point.X >= 0 && point.Y >= 0 && point.X <= target.ActualWidth && point.Y <= target.ActualHeight;
        }

        private void ShowControlDragPreview(Border source, Point point)
        {
            CloseControlDragPreview();var owner=Window.GetWindow(this);
            controlDragPreview=ShortcutDragSupport.CreateAnchoredPreview(source,TranslatePoint(dragStart,owner));
            UpdateControlDragPreview(point);controlDragPreview.IsOpen=true;
        }
        private void UpdateControlDragPreview(Point point)
        {
            if(controlDragPreview!=null)ShortcutDragSupport.MoveAnchoredPreview(controlDragPreview,TranslatePoint(point,Window.GetWindow(this)));
        }
        private void CloseControlDragPreview()
        {
            if (controlDragPreview != null) controlDragPreview.IsOpen = false;
            controlDragPreview = null;
        }

        private static T? FindVisualAncestor<T>(DependencyObject start) where T : DependencyObject
        {
            for (DependencyObject? current = start; current != null; current = VisualTreeHelper.GetParent(current))
                if (current is T match) return match;
            return null;
        }

        private sealed record TouchDropTarget(
            Border Border,
            Slot? ShortcutsSlot,
            QuickLaunchBar? QuickLaunchOwner,
            QuickLaunchBar.QuickLaunchSlot? QuickLaunchSlot);

        internal void SaveTouchShortcuts() => SaveSlots();
        internal bool TryResolveControlDropTarget(Border hoveredBorder, Slot hoveredSlot, string key,
            out Border resolvedBorder, out Slot resolvedSlot,int span=2)
        {
            resolvedSlot = span==1?hoveredSlot:ResolveControlDropSlot(hoveredSlot, key);
            resolvedBorder = ReferenceEquals(resolvedSlot, hoveredSlot)
                ? hoveredBorder
                : FindSlotBorder(resolvedSlot) ?? hoveredBorder;
            return ReferenceEquals(resolvedSlot, hoveredSlot) || !ReferenceEquals(resolvedBorder, hoveredBorder);
        }

        private Slot ResolveControlDropSlot(Slot hoveredSlot, string key)
        {
            if (key != "clock") return hoveredSlot;
            int index = Slots.IndexOf(hoveredSlot);
            int resolvedIndex = ShortcutsGridPlanner.ResolveWideStart(index, Slots.Count, GridColumns);
            return resolvedIndex >= 0 && resolvedIndex < Slots.Count ? Slots[resolvedIndex] : hoveredSlot;
        }

        private Border? FindSlotBorder(Slot slot)
        {
            DependencyObject root = shortcutsItems;
            return FindVisualDescendant<Border>(root, border => ReferenceEquals(border.DataContext, slot));
        }

        private static T? FindVisualDescendant<T>(DependencyObject root, Func<T, bool> predicate)
            where T : DependencyObject
        {
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, index);
                if (child is T match && predicate(match)) return match;
                T? nested = FindVisualDescendant(child, predicate);
                if (nested != null) return nested;
            }
            return null;
        }

        internal bool CanAcceptControl(Slot slot, string key,int span=2)
        {
            if(span==1)return slot.IsEmpty;
            slot = ResolveControlDropSlot(slot, key);
            if (!slot.IsEmpty) return false;
            if (key != "clock") return true;
            int index = Slots.IndexOf(slot);
            return ShortcutsGridPlanner.CanPlaceWideControl(index, Slots.Count, GridColumns) && Slots[index + 1].IsEmpty;
        }

        internal bool SetControl(Slot slot, string key,ClockAppearance? appearance=null)
        {
            bool single=key=="clock"&&appearance?.Span==1;
            if(!single)slot = ResolveControlDropSlot(slot, key);
            if(single?!slot.IsEmpty:!CanAcceptControl(slot, key))return false;
            int index = Slots.IndexOf(slot);
            slot.SetControl(key);
            if(appearance!=null)slot.ClockStyle=appearance.Clone();
            if (key == "clock"&&slot.ClockStyle.Span==2) Slots[index + 1].SetReserved("clock");
            NormalizeClockSpans();
            SaveSlots();
            return true;
        }

        private void NormalizeSensorSpans()
        {
            foreach(var slot in Slots){slot.SensorSpan=1;if(slot.ReservedKey=="sensor")slot.Clear();}
            for(int i=0;i<Slots.Count;i++)
                if(Slots[i].IsSensor&&SensorCellPreferences.Get(SensorLocation(Slots[i]),SystemMonitorWidget.Shared?.GetPresentation(Enum.Parse<SystemMonitorWidget.MetricKind>(Slots[i].Target![7..]))??new()).Mode=="Graph"&&i%GridColumns<GridColumns-1&&i+1<Slots.Count&&Slots[i+1].IsEmpty)
                {Slots[i].SensorSpan=2;Slots[i+1].SetReserved("sensor");}
            shortcutsItems?.InvalidateMeasure();
        }
        private bool NormalizeClockSpans()
        {
            bool changed = false;
            for (int i = 0; i < Slots.Count; i++)
            {
                if (Slots[i].ControlKey != "clock") continue;
                if(Slots[i].ClockStyle.Span==1){if(i+1<Slots.Count&&Slots[i+1].IsReserved){Slots[i+1].Clear();changed=true;}continue;}
                if (i % GridColumns < GridColumns - 1 && i + 1 < Slots.Count && Slots[i + 1].IsReserved) continue;
                if (i % GridColumns == GridColumns - 1 && i + 2 < Slots.Count &&
                    Slots[i + 1].IsReserved && Slots[i + 2].IsEmpty)
                {
                    var style=Slots[i].ClockStyle;
                    Slots[i].Clear();
                    Slots[i + 1].SetControl("clock");
                    Slots[i + 1].ClockStyle=style;
                    Slots[i + 2].SetReserved("clock");
                    changed = true;
                    i++;
                    continue;
                }
                if (i % GridColumns < GridColumns - 1 && i + 1 < Slots.Count && Slots[i + 1].IsEmpty)
                {
                    Slots[i + 1].SetReserved("clock");
                    changed = true;
                    continue;
                }
                int destination = Enumerable.Range(0, Slots.Count - 1)
                    .FirstOrDefault(index => index % GridColumns < GridColumns - 1 && index != i && Slots[index].IsEmpty && Slots[index + 1].IsEmpty, -1);
                if (destination >= 0)
                {
                    var style=Slots[i].ClockStyle;
                    Slots[i].Clear();
                    Slots[destination].SetControl("clock");
                    Slots[destination].ClockStyle=style;
                    Slots[destination + 1].SetReserved("clock");
                    changed = true;
                }
            }
            return changed;
        }

        internal void SetGridColumns(int columns)
        {
            int next = Math.Max(2, columns);
            if (GridColumns == next) return;
            GridColumns = next;
            NormalizeSensorSpans();
            NormalizeAirthingsMiniSpans();
            if (NormalizeClockSpans()) SaveSlots();
        }

        internal void SetModuleTitleLayout(bool showTitle, bool collapseHiddenTitleSpace)
        {
            bool collapse = false; // Keep the module options hit target available when titles are hidden.
            moduleTitleRow.Height = collapse ? new GridLength(0) : new GridLength(45);
            moduleTitleDividerRow.Height = collapse ? new GridLength(0) : new GridLength(1);
            moduleTitleDivider.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
            shortcutsItems.Margin = collapse ? new Thickness(0) : new Thickness(0, 12, 0, 0);
        }

        private QuickLaunchBar? FindQuickLaunchBar() =>
            (Window.GetWindow(this) as MainWindow)?.FindName("quickLaunchBar") as QuickLaunchBar;

        private void Slot_DragEnter(object sender, DragEventArgs e)
        {
            if (sender is not Border { DataContext: Slot slot } border) return;
            bool internalMove=e.Data.GetDataPresent(ShortcutDragSupport.Format);
            var transfer=internalMove?ShortcutDragSupport.ResolveTransfer(e.Data):null;
            bool protectedCell=slot.IsAirthingsMini||slot.HasControl||slot.IsReserved;
            bool supported=internalMove
                ? transfer?.CanMoveTo(slot.Target)==true&&transfer.FitsTarget(this,slot)&&
                  (transfer.Target==MacroLibraryCard.ClockTarget||AirthingsMini.IsTarget(transfer.Target)||!protectedCell)
                : e.Data.GetDataPresent(DataFormats.FileDrop)&&!protectedCell;
            if (!supported)
            {
                Slot_DragLeave(sender,e);e.Effects=DragDropEffects.None;e.Handled=true;return;
            }
            border.BorderBrush = ActiveBorder;
            border.Background = new SolidColorBrush(Color.FromArgb(55, 30, 215, 96));
            e.Effects = internalMove ? DragDropEffects.Move : DragDropEffects.Link;
            e.Handled = true;
        }

        private void Slot_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is Border border)
            {
                border.BorderBrush = NormalBorder;
                border.SetResourceReference(Border.BackgroundProperty,"ModuleCardSurfaceBrush");
            }
        }

        private void Slot_Drop(object sender, DragEventArgs e)
        {
            if (sender is not Border { DataContext: Slot slot } border) return;
            border.BorderBrush = NormalBorder;
            border.SetResourceReference(Border.BackgroundProperty,"ModuleCardSurfaceBrush");

            if (e.Data.GetDataPresent(ShortcutDragSupport.Format) &&
                ShortcutDragSupport.ResolveTransfer(e.Data) is ShortcutTransfer transfer)
            {
                
                e.Effects = AcceptShortcutTransfer(slot,transfer)?DragDropEffects.Move:DragDropEffects.None;
                e.Handled = true;
                return;
            }
            if(e.Data.GetDataPresent(ShortcutDragSupport.Format)){e.Effects=DragDropEffects.None;e.Handled=true;return;}

            if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
                e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                if(slot.IsAirthingsMini||slot.IsReserved||slot.HasControl){e.Effects=DragDropEffects.None;e.Handled=true;return;}
                slot.Target = files[0];
                SaveSlots();
                e.Effects = DragDropEffects.Link;
                e.Handled = true;
            }
        }

        internal bool AcceptShortcutTransfer(Slot slot,ShortcutTransfer transfer)
        {
            if(!Slots.Contains(slot)||!transfer.CanMoveTo(slot.Target)||!transfer.FitsTarget(this,slot))return false;
            if(transfer.Target==MacroLibraryCard.ClockTarget)
            {
                if(!slot.IsEmpty||!SetControl(slot,"clock",MonitorClockContent.Decode(transfer.Presentation)))return false;
                transfer.CompleteWithPresentation(null,null,null);return true;
            }
            if(AirthingsMini.IsTarget(transfer.Target)){bool accepted=MiniPlacement.Place(this,slot,AirthingsMini.Decode(transfer.Target),transfer.SourceOwner,transfer.SourceSlot);if(accepted)transfer.AcceptWithoutReplacement();return accepted;}
            if(slot.IsAirthingsMini||slot.HasControl||slot.IsReserved)return false;
            string? target=slot.Target,presentation=slot.Presentation,icon=slot.IconPath;string name=slot.Name;
            transfer.ExchangeSensorPreferences(SensorLocation(slot),target);
            slot.SetTarget(transfer.Target,transfer.DisplayName);slot.Presentation=transfer.Presentation;slot.IconPath=transfer.IconPath;
            transfer.CompleteWithPresentation(target,name,presentation,icon);
            SaveSlots();SensorCellPreferences.NotifyPlacement();return true;
        }

        private void RemoveShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem { CommandParameter: Slot slot } || slot.IsEmpty) return;
            if(slot.ControlKey=="clock")
            {
                ClearControl(slot,"clock");SaveSlots();e.Handled=true;return;
            }
            if (slot.ControlKey is string key)
            {
                QuickLaunchBar? quickLaunch = FindQuickLaunchBar();
                if (quickLaunch?.RestoreExternalControl(key) != true)
                {
                    AppMessage.Show(key == "clock" ? "Free two top-bar shortcut slots first." :
                        "Free one top-bar shortcut slot first.", "Top bar is full",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                int index = Slots.IndexOf(slot);
                if (key == "clock" && index + 1 < Slots.Count && Slots[index + 1].IsReserved)
                    Slots[index + 1].Clear();
            }
            slot.Clear();
            SaveSlots();
            e.Handled = true;
        }

        private void EditMacro_Click(object sender, RoutedEventArgs e) { if(sender is MenuItem {CommandParameter:Slot slot} && MacroLibrary.Current.Find(slot.Target) is {} macro) MacroEditor.Show(Window.GetWindow(this),macro); e.Handled=true; }

        private void SensorContextMenu_Opening(object sender,ContextMenuEventArgs e)
        {
            if(sender is Border {DataContext:Slot {IsSensor:true}} border&&FindVisualDescendant<SensorTile>(border,_=>true) is {} tile){e.Handled=true;Dispatcher.BeginInvoke(new Action(()=>tile.OpenOptions()));}
        }

        private void ShortcutContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu { DataContext: Slot slot } menu) return;
            foreach (MenuItem item in menu.Items.OfType<MenuItem>())
            {
                string header = item.Header?.ToString() ?? ""; item.Visibility = (header=="Edit macro" && !MacroLibrary.IsReference(slot.Target) || header=="Edit" && MacroLibrary.IsReference(slot.Target)) ? Visibility.Collapsed : Visibility.Visible;
                item.IsEnabled = !slot.IsEmpty &&
                    (header == "Delete" || header=="Edit"&&slot.ControlKey=="clock" || !slot.HasControl) &&
                    (header != "Properties" || ShortcutShellActions.IsFileTarget(slot.Target));
            }
        }

        private void EditShortcut_Click(object sender, RoutedEventArgs e)
        {
            if(sender is MenuItem {CommandParameter:Slot {IsAirthingsMini:true} miniSlot}){EditAirthingsMini(miniSlot);e.Handled=true;return;}
            if(sender is MenuItem {CommandParameter:Slot {IsUsageMini:true} usageSlot}){CreateUsageMiniEditor(usageSlot).ShowDialog();e.Handled=true;return;}
            if(sender is MenuItem {CommandParameter:Slot clockSlot}&&clockSlot.ControlKey=="clock")
            {
                int originalCells=clockSlot.ClockStyle.Span;
                ClockEditor.Create(Window.GetWindow(this),clockSlot.ClockStyle.Clone(),p=>{var preview=p.Clone();preview.Cells=originalCells;clockSlot.ClockStyle=preview;},p=>{int index=Slots.IndexOf(clockSlot);if(p.Span==2&&originalCells==1&&(!ShortcutsGridPlanner.CanPlaceWideControl(index,Slots.Count,GridColumns)||!Slots[index+1].IsEmpty))throw new InvalidOperationException("Free the next cell in this row before choosing two cells.");clockSlot.ClockStyle=p;NormalizeClockSpans();FindVisualDescendant<ShortcutsSpanPanel>(shortcutsItems,_=>true)?.InvalidateMeasure();SaveSlots();}).ShowDialog();e.Handled=true;return;
            }
            if(sender is not MenuItem { CommandParameter:Slot slot } || slot.IsEmpty || slot.HasControl)return;
            ShortcutAppearanceEditor.Create(Window.GetWindow(this),slot.Name,slot.Target!,slot.Presentation??(slot.ShowName?"IconText":"Icon"),(name,target,presentation,chosenIcon)=>
            {slot.SetTarget(target,name);slot.Presentation=presentation;slot.IconPath=chosenIcon;SaveSlots();},preview:slot.PreviewAppearance,clearPreview:slot.ClearAppearancePreview,iconPath:slot.IconPath,previewIcon:slot.PreviewIcon).ShowDialog();
            e.Handled=true;
        }

        private void RenameShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem { CommandParameter: Slot slot } || slot.IsEmpty || slot.HasControl) return;
            ShortcutRenameDialog dialog = new(slot.Name) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() != true) return;
            slot.Rename(dialog.ShortcutName);
            SaveSlots();
            e.Handled = true;
        }

        private void ShortcutProperties_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { CommandParameter: Slot slot } && !slot.HasControl)
                ShortcutShellActions.ShowProperties(Window.GetWindow(this), slot.Target);
            e.Handled = true;
        }

        public sealed class Slot : INotifyPropertyChanged
        {
            private string? target;
            private string? customName;
            private string? controlKey;
            private string? reservedFor;
            private bool showName = true;
            public Slot(string? value, string? savedName = null) { customName = savedName; if (value?.StartsWith("control:") == true) controlKey = value[8..]; else if (value?.StartsWith("reserved:") == true) reservedFor = value[9..]; else target = LegacyAirthingsStatusTarget.IsTarget(value)?null:value; }
            public string? Target { get => target; set { presentation=null;iconPath=null;target = LegacyAirthingsStatusTarget.IsTarget(value)?null:value; customName = null; if (value != null) controlKey = null; NotifyAll(); } }
            public string? CustomName => customName;
            public string? ControlKey => controlKey;
            public bool HasControl => controlKey != null;
            public bool IsReserved => reservedFor != null;
            internal string? ReservedKey=>reservedFor;
            public int SensorSpan {get;internal set;}=1;
            public string ControlDisplay => controlKey switch { "clock" => DateTime.Now.ToString("t"), "sleep" => "SLEEP\nMODE", "shutdown" => "SHUT\nDOWN", "restart" => "RESTART", _ => "" };
            public bool IsObsControl => controlKey is "obs-record" or "obs-buffer";
            public string ObsMode => controlKey == "obs-buffer" ? "buffer" : "record";
            public bool ObsRecording { get; private set; }
            public bool ObsStatusActive { get; private set; }
            public string ObsElapsedText { get; private set; } = "00:00:00";
            private ClockAppearance clockStyle=new();
            public ClockAppearance ClockStyle {get=>clockStyle;set{clockStyle=value??new();NotifyAll();}}
            public FontFamily ControlFontFamily=>controlKey=="clock"?ClockStyle.Face:(FontFamily)Application.Current.FindResource("TekoFont");
            public FontWeight ControlFontWeight=>controlKey=="clock"?ClockStyle.Weight:FontWeights.Bold;
            public FontStyle ControlFontStyle=>controlKey=="clock"?ClockStyle.Style:FontStyles.Normal;
            public Brush ControlBrush=>controlKey=="clock"?ClockStyle.Brush:Brushes.White;
            public bool ClockInset=>controlKey=="clock"&&ClockStyle.EffectStrength>0;
            public bool ClockFit=>controlKey=="clock";
            public double ClockStrength=>ClockStyle.EffectStrength;
            public System.Windows.Media.Effects.Effect? ClockShadow=>controlKey=="clock"?ClockStyle.ShadowEffect:null;
            public double ControlFontSize => controlKey == "clock" ? ClockStyle.CellSize : 37.33;
            public double ControlLineHeight => controlKey == "clock" ? 80 : 36;
            public string ToolTip => IsUsageMini?UsageMini.Description(Target):IsAirthingsMini?AirthingsMini.Description(Target):IsSensor?SystemMonitorWidget.GetMetricLabel(Enum.Parse<SystemMonitorWidget.MetricKind>(Target![7..]))+" — right-click for options; graphs: wheel to zoom, double-click to reset":MacroLibrary.IsReference(Target) ? MacroLibrary.Current.Description(Target) : Target ?? "";
            public bool HasToolTip => !string.IsNullOrWhiteSpace(ToolTip);
            public string Name => previewName??SavedName;
            private string SavedName => !string.IsNullOrWhiteSpace(customName) ? customName : MacroLibrary.IsReference(Target) ? MacroLibrary.Current.Title(Target) : HasControl ? ControlDisplay : string.IsNullOrWhiteSpace(Target) ? "" :
                string.IsNullOrWhiteSpace(customName) ? Path.GetFileNameWithoutExtension(Target) : customName;
            public bool IsSensor=>Target?.StartsWith("sensor:")==true&&Enum.TryParse<SystemMonitorWidget.MetricKind>(Target[7..],out _);
            public bool IsUsageMini=>UsageMini.IsTarget(Target);
            public bool IsAirthingsMini=>AirthingsMini.IsTarget(Target);
            public bool IsEmpty => !HasControl && !IsReserved && string.IsNullOrWhiteSpace(Target);
            private (string Target,string? Custom)? requestedIcon;
            private ImageSource? resolvedIcon;
            private int iconGeneration;
            public ImageSource? Icon {
                get {
                    if(string.IsNullOrWhiteSpace(Target)||IsSensor||IsUsageMini||IsAirthingsMini)return null;
                    if(MacroLibrary.IsReference(Target))return QuickLaunchBar.GetShellIcon(Target!);
                    var key=(Target!,previewIconPath??IconPath);
                    if(requestedIcon!=key){requestedIcon=key;resolvedIcon=null;ResolveIcon(key,++iconGeneration);}
                    return resolvedIcon;
                }
            }
            private async void ResolveIcon((string Target,string? Custom) key,int generation)
            {
                var image=await ShortcutIconLoader.LoadAsync(key.Target,key.Custom);
                if(generation!=iconGeneration||requestedIcon!=key)return;
                resolvedIcon=image;PropertyChanged?.Invoke(this,new(nameof(Icon)));
            }

            private string? presentation;
            private string? previewName,previewPresentation;
            private string? iconPath,previewIconPath;
            public string? IconPath {get=>iconPath;set{iconPath=value;requestedIcon=null;NotifyAll();}}
            internal void PreviewIcon(string? path){previewIconPath=path??"";requestedIcon=null;NotifyAll();}

            internal void PreviewAppearance(string name,string target,string presentation){previewName=name;previewPresentation=presentation;NotifyAll();}
            internal void ClearAppearancePreview(){previewIconPath=null;requestedIcon=null;previewName=null;previewPresentation=null;NotifyAll();}
            public string? Presentation { get=>presentation;set { presentation=ShortcutAppearanceEditor.Normalize(value);NotifyAll(); } }
            public bool ShowIcon => (previewPresentation??MacroLibrary.Current.PresentationFor(Target,Presentation))!="Text";
            public bool ShowName { get => (previewPresentation??MacroLibrary.Current.PresentationFor(Target,Presentation)) is string mode?mode!="Icon":showName; set { if (showName == value) return; showName = value; PropertyChanged?.Invoke(this, new(nameof(ShowName))); } }
            public event PropertyChangedEventHandler? PropertyChanged;
            public void SetTarget(string? value, string? name) { if(!string.Equals(target,value,StringComparison.Ordinal)){presentation=null;iconPath=null;}target = LegacyAirthingsStatusTarget.IsTarget(value)?null:value; customName = target==null?null:name; if (value != null) controlKey = null; NotifyAll(); }
            public void Rename(string name) { customName = name.Trim(); NotifyAll(); }
            public void SetControl(string key) { clockStyle=new();presentation=null;iconPath=null;target = null; reservedFor = null; controlKey = key; NotifyAll(); }
            public void SetReserved(string key) { presentation=null;iconPath=null;target = null; controlKey = null; reservedFor = key; NotifyAll(); }
            public void Clear() { presentation=null;iconPath=null;target = null; customName = null; controlKey = null; reservedFor = null; NotifyAll(); }
            public void RefreshControlDisplay() => PropertyChanged?.Invoke(this, new(nameof(ControlDisplay)));
            public void UpdateObsState(bool obsRunning, bool recording, bool bufferActive, string elapsedText)
            {
                if (!IsObsControl) return;
                ObsRecording = recording;
                ObsStatusActive = controlKey == "obs-buffer" ? bufferActive : obsRunning;
                ObsElapsedText = elapsedText;
                foreach (string name in new[] { nameof(ObsRecording), nameof(ObsStatusActive), nameof(ObsElapsedText) }) PropertyChanged?.Invoke(this, new(name));
            }
            public LabelTypography? MacroTypography=>MacroLibrary.Current.TypographyFor(Target);
            internal void RefreshMacro() {NotifyAll();PropertyChanged?.Invoke(this,new(nameof(MacroTypography)));}
            private void NotifyAll() { foreach (string name in new[] { nameof(IsUsageMini), nameof(IsAirthingsMini), nameof(IsSensor), nameof(MacroTypography), nameof(ToolTip), nameof(HasToolTip), nameof(Presentation), nameof(ShowIcon), nameof(ShowName), nameof(Target), nameof(ControlKey), nameof(HasControl), nameof(IsReserved), nameof(ControlDisplay), nameof(ControlFontSize), nameof(ControlFontFamily), nameof(ControlFontWeight), nameof(ControlFontStyle), nameof(ControlBrush), nameof(ClockInset), nameof(ClockStrength), nameof(ClockShadow), nameof(ClockFit), nameof(ControlLineHeight), nameof(IsObsControl), nameof(ObsMode), nameof(Name), nameof(IsEmpty), nameof(Icon) }) PropertyChanged?.Invoke(this, new(name)); }
        }
    }
}







