using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Ekrano_valdymas
{
    public partial class MainWindow : Window
    {
        private static void ScaleLoadedTextControl(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement text && Window.GetWindow(text) is MainWindow window)
                window.QueueTextFontScale(text);
        }

        // Temporary visual review state requested by the user. Remove after approval.
        private const bool PreviewMaximumUsageValues = false;
        private const uint Th32csSnapProcess = 0x00000002;
        private const int WmNcHitTest = 0x0084;
        private const int WmSizing = 0x0214;
        private const int WmSysCommand = 0x0112;
        private const int WmExitSizeMove = 0x0232;
        private const int ScSize = 0xF000;
        private const int ResizeActivationDragPixels = 12;
        private const int WmszLeft = 1;
        private const int WmszRight = 2;
        private const int WmszTop = 3;
        private const int WmszTopLeft = 4;
        private const int WmszTopRight = 5;
        private const int WmszBottom = 6;
        private const int WmszBottomLeft = 7;
        private const int WmszBottomRight = 8;
        private const int HtLeft = 10;
        private const int HtClient = 1;
        private const int HtTransparent = -1;
        private const int HtRight = 11;
        private const int HtTop = 12;
        private const int HtTopLeft = 13;
        private const int HtTopRight = 14;
        private const int HtBottom = 15;
        private const int HtBottomLeft = 16;
        private const int HtBottomRight = 17;
        private const double ResizeBorderSize = 10;
        private const double TopResizeBorderSize = 16;
        private const double BottomRightResizeCornerSize = 32;
        private const double FullDashboardCanvasHeight = 560;
        private const double TitlelessWideDashboardCanvasHeight = 499;
        private const double CompactCanvasHeight = 124;
        private const uint MonitorDefaultToNearest = 2;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoActivate = 0x0010;
        private static readonly IntPtr HwndTopmost = new(-1);
        private static readonly IntPtr HwndNoTopmost = new(-2);

        private readonly string settingsFolder;
        private readonly string settingsFile;
        private readonly string monitorSettingsFile;
        private readonly string dashboardLayoutsFile;
        private double moduleOpacity = 0.69;
        private double uiOpacity = 1.0;
        private double backgroundTransparency;
        private double fontScale = 1.15;
        private string accentColor = "#1ED760";
        private string clockColor = ProductColorDefaults.Clock;
        private string systemMonitorColor = ProductColorDefaults.Sensors;
        private string baseColor = ProductColorDefaults.Base;
        private string textColor = ProductColorDefaults.Text;
        private string[] savedPaletteColors = { "#263238", "#795548", "#607D8B", "#000000" };
        private double dashboardColorIntensity = 100;
        private double clockColorIntensity = 100;
        private double textColorOpacity = 100;
        private double menuScalePercent = 100;
        private double menuTextPercent=100,usageTextPercent=100;
        private double layoutListTextSize = 23;
        private bool updatingMenuScale;
        private DockEdge compactDockEdge = DockEdge.Bottom;
        private MusicService selectedMusicService = MusicService.Spotify;
        private bool isCompact;
        private bool restoringStartupLayout=true;
        private bool applyingDashboardProfile;
        private long compactTransitionVersion;
        private long interactiveControlResizeSuppressedUntil;
        private bool sleepComputer;
        private bool alwaysOnTopEnabled;
        private long lastTouchInputTick;
        private TouchDevice? windowDragTouchDevice;
        private bool touchMenuPending;
        private bool touchHoldMenuOpened;
        private readonly DispatcherTimer touchMenuHoldTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
        private Point windowDragTouchStart;
        private Point windowDragWindowStart;
        private bool windowTouchDragStarted;
        private double expandedWidth = 1920;
        private double expandedHeight = 560;
        private IntPtr windowHandle;
        // Presentation metadata must not keep discarded module text alive.
        private readonly ConditionalWeakTable<TextBlock, StrongBox<double>> baseTextBlockFontSizes = new();
        private readonly ConditionalWeakTable<TextBox, StrongBox<double>> baseTextBoxFontSizes = new();
        private readonly ConditionalWeakTable<FrameworkElement, StrongBox<byte>> registeredFontScaleControls = new();
        private readonly HashSet<FrameworkElement> pendingFontScaleControls = new();
        private DispatcherOperation? fontScaleOperation;
        private readonly ConditionalWeakTable<TextBlock, StrongBox<byte>> primaryTextAlpha = new();
        private readonly ConditionalWeakTable<OpticallyCenteredText, StrongBox<byte>> primaryOpticalTextAlpha = new();
        private readonly DispatcherTimer telemetryTimer;
        private readonly DispatcherTimer autoDimTimer;
        private bool autoDimElements;
        private bool elementsAutoDimmed;
        private double autoDimOpacityPercent = 70;
        private bool updatingAutoDimSettings;
        private bool autoDashHideEnabled;
        private bool autoDashHidden;
        private bool autoDashInputTransparent;
        private int autoDashAnimationVersion;
        private readonly TranslateTransform autoDashTranslate = new();
        private static readonly TimeSpan AutoDashAnimationDuration = FullscreenBrowserWindow.InteractionTransitionDuration;
        private long lastLocalActivityTick = Environment.TickCount64;
        private NativePoint lastDimCursorPosition;
        private bool hasDimCursorPosition;
        private TimeSpan telemetryLastCpuTime;
        private DateTime telemetryLastSampleUtc;
        private readonly ProcessGpuUsageReader telemetryGpu = new();
        private readonly ProcessRamReader telemetryRam = new();
        private long telemetryLastNetworkBytes;
        private readonly Queue<double> telemetryNetworkSamples = new();
        private bool telemetryUpdateRunning;
        private NativeRect startupBounds;
        private int[]? pendingBrowserPixels;
        private bool startupBoundsCaptured;
        private NativeRect fitRestoreDashboardPixels;
        private NativeRect fitRestoreBrowserPixels;
        private bool fitRestoreDashboardCaptured;
        private bool fitRestoreBrowserCaptured;
        private NativeRect linkedDashboardStartPixels;
        private NativeRect linkedBrowserStartPixels;
        private bool linkedMoveCaptured;
        private readonly ModuleKind[] selectedModules =
            { ModuleKind.Shortcuts, ModuleKind.Shortcuts2, ModuleKind.Shortcuts3 };
        private SystemMonitorWidget? systemMonitorWidget;
        private SystemMonitorWidget? wideSystemMonitor, fullSystemMonitor;
        private readonly Dictionary<(int Tab,string Kind),ShortcutsWidget> tabShortcutWidgets = new();
        private ShortcutsWidget? shortcutsWidget;
        private ShortcutsWidget? shortcutsWidget2;
        private ShortcutsWidget? wideShortcutsWidget;
        private HueWidget? hueWidget;
        private AirthingsWidget? airthingsWidget;
        private int wideShortcutsSpan;
        private readonly DashboardLayoutProfile[] dashboardLayouts = new DashboardLayoutProfile[QuickLaunchBar.MaximumLayoutTabs];
        private int activeDashboardLayoutTab;
        private MusicConsoleControl? musicConsole;
        private bool musicConsoleEnabled;
        private string musicLightColor = ProductColorDefaults.Buttons;
        private double musicLightIntensity = 100;
        private double musicVolumeLightIntensity = 50;
        private string? musicVolumeLightColor;
        private bool musicDimEnabled, musicTopBarHidden;
        private int musicDimDelaySeconds=10;
        private bool dashboardLayoutsLoaded;
        private DispatcherOperation? pendingTabPersistence;
        private AnalogVuCard? analogVuCard;
        private MusicPlayerCard? musicPlayerCard;
        private MacroLibraryCard? macroLibraryCard;
        private HudVisualizerCard? hudVisualizerCard;
        private PlaylistCard? playlistCard;
        private WebShortcutsWidget? webShortcutsWidget;
        private bool webShortcutsImagesOnly;
        private double webListRowHeight=32;
        private PlaylistSettings? webListAppearance;
        private FullscreenBrowserWindow? activeBrowser;
        private string? lastBrowserUrl;
        private string? activeWebShortcutUrl;
        private string? pendingWebShortcutUrl;
        private bool applicationClosing;
        private readonly DoubleTapDetector menuDoubleTap = new();
        private readonly AudioSpectrumService visualizerAudio = new();
        private bool showShortcutNames = true;
        private bool showModuleTitles = true;
        private bool showUsageOverlay;
        private UsageOverlayWindow? usageOverlayWindow;
        private bool aligningLinkedWindows;
        private bool resizeGesturePending;
        private NativePoint resizeGestureStart;
        private IntPtr activePhysicalMonitor;
        private string? activeMonitorKey;
        private bool updatingMonitorControls;
        private bool monitorBrightnessSupported;
        private bool monitorContrastSupported;
        private bool monitorColorTemperatureSupported;
        private bool monitorHueSupported;
        private bool monitorSaturationSupported;
        private readonly Dictionary<string, uint> savedMonitorControls =
            new(StringComparer.OrdinalIgnoreCase);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(
            IntPtr hwnd,
            uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out NativePoint point);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(
            IntPtr monitor,
            ref MonitorInfo monitorInfo);

        [DllImport("dxva2.dll", SetLastError = true)]
        private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(
            IntPtr monitorHandle, out uint numberOfPhysicalMonitors);

        [DllImport("dxva2.dll", SetLastError = true)]
        private static extern bool GetPhysicalMonitorsFromHMONITOR(
            IntPtr monitorHandle, uint physicalMonitorArraySize,
            [Out] PhysicalMonitor[] physicalMonitorArray);

        [DllImport("dxva2.dll", SetLastError = true)]
        private static extern bool DestroyPhysicalMonitor(IntPtr physicalMonitorHandle);

        [DllImport("dxva2.dll", SetLastError = true)]
        private static extern bool GetMonitorBrightness(
            IntPtr physicalMonitorHandle, out uint minimumBrightness,
            out uint currentBrightness, out uint maximumBrightness);

        [DllImport("dxva2.dll", SetLastError = true)]
        private static extern bool SetMonitorBrightness(
            IntPtr physicalMonitorHandle, uint newBrightness);

        [DllImport("dxva2.dll", SetLastError = true)]
        private static extern bool GetMonitorContrast(
            IntPtr physicalMonitorHandle, out uint minimumContrast,
            out uint currentContrast, out uint maximumContrast);

        [DllImport("dxva2.dll", SetLastError = true)]
        private static extern bool SetMonitorContrast(
            IntPtr physicalMonitorHandle, uint newContrast);

        [DllImport("dxva2.dll", SetLastError = true)]
        private static extern bool GetMonitorColorTemperature(
            IntPtr physicalMonitorHandle, out MonitorColorTemperature colorTemperature);

        [DllImport("dxva2.dll", SetLastError = true)]
        private static extern bool SetMonitorColorTemperature(
            IntPtr physicalMonitorHandle, MonitorColorTemperature colorTemperature);

        [DllImport("dxva2.dll", SetLastError = true)]
        private static extern bool GetVCPFeatureAndVCPFeatureReply(
            IntPtr physicalMonitorHandle, byte vcpCode, out VcpCodeType codeType,
            out uint currentValue, out uint maximumValue);

        [DllImport("dxva2.dll", SetLastError = true)]
        private static extern bool SetVCPFeature(
            IntPtr physicalMonitorHandle, byte vcpCode, uint newValue);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(
            IntPtr hwnd,
            out NativeRect windowRect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hwnd,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        public MainWindow()
        {
            InstallationDefaults.Prepare();
            try{PowerMacroMigration.Run();}catch(Exception ex){InteractionDiagnostics.RecordFailure("Power macro migration; original controls retained",ex);}
            // The selected tab is restored after XAML construction. Do not
            // decode a default dark material before its real skin is known.
            CommonMetal.DeferStartupMaterial();
            InitializeComponent();
            InitializeModuleOptions();
            InitializeHudVisualizerMedia();
            InitializeWeatherLocations();
            ShellIconFrames.Attach(this);
            SettingsWindow.AttachDashboard(this);
            UsageMiniState.DemandChanged+=UsageMiniDemandChanged;
            Closed+=(_,_)=>UsageMiniState.DemandChanged-=UsageMiniDemandChanged;
            dashboardHost.RenderTransform = autoDashTranslate;
            quickLaunchBar.AttachObsWidget(obsWidget);

            settingsFolder = Path.Combine(
                DiagnosticState.LocalApplicationData,
                "Ekrano_valdymas");

            settingsFile = Path.Combine(settingsFolder, "window-settings.json");
            monitorSettingsFile = Path.Combine(settingsFolder, "monitor-controls.json");
            dashboardLayoutsFile = Path.Combine(settingsFolder, "dashboard-layout-tabs.json");
            InitializeResourceModes();
            activeDashboardLayoutTab = ClampDashboardLayoutTab(quickLaunchBar.ActiveLayoutTab);
            quickLaunchBar.LayoutTabChanged += QuickLaunchBar_LayoutTabChanged;
            LoadMonitorSettings();
            telemetryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            telemetryTimer.Tick += TelemetryTimer_Tick;
            autoDimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            autoDimTimer.Tick += AutoDimTimer_Tick;
            autoDimTimer.Start();
            if(InteractionDiagnostics.Enabled)ContentRendered+=(_,_)=>TraceRuntimeStateAfterStartup();
            PreviewMouseMove += (_, _) => MarkLocalActivity();
            PreviewMouseDown += (_, _) => MarkLocalActivity();
            PreviewKeyDown += (_, _) => MarkLocalActivity();
            Activated += Window_Activated;
            StateChanged += Window_StateChanged;
            LocationChanged += (_, _) =>
            {
                usageOverlayWindow?.AlignToDashboard();
                FollowDashboardPixels();
            };
            SizeChanged += (_, _) =>
            {
                UpdateMusicViewport();
                usageOverlayWindow?.AlignToDashboard();
                SynchronizeActiveBrowser();
                if (autoDashHidden)
                    _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
                        new Action(MaintainAutoDashHiddenOffset));
            };
            AddHandler(UIElement.PreviewTouchDownEvent,
                new EventHandler<TouchEventArgs>(Window_PreviewTouchDown), true);
            AddHandler(UIElement.PreviewTouchMoveEvent,
                new EventHandler<TouchEventArgs>(Window_PreviewTouchMove), true);
            AddHandler(UIElement.PreviewTouchUpEvent,
                new EventHandler<TouchEventArgs>(Window_PreviewTouchUp), true);
            touchMenuHoldTimer.Tick += TouchMenuHoldTimer_Tick;
            AddHandler(UIElement.LostTouchCaptureEvent,
                new EventHandler<TouchEventArgs>(Window_LostTouchCapture), true);
            Closed += (_, _) => touchMenuHoldTimer.Stop();
            MusicFinish.Set(LoadValidWindowSettings()?.MusicFinish);
            ApplyPanelMetal(MusicFinish.IsLight,LoadValidWindowSettings()?.PanelMetal=="none");
            // Restore the selected layout before allocating a hardware console.
            // Most tabs do not contain it; UpdateMusicConsoleVisibility creates
            // the first instance only when the loaded layout actually needs it.
            SetMusicService(MusicService.Spotify);
            LanguageManager.Current = AppLanguage.English;
        }

        private void DashboardViewbox_PreviewMouseRightButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            // Do not put a live menu underneath a still-pressed mouse button.
            e.Handled = true;
            InteractionDiagnostics.Log($"ContextMenu right-down source={e.OriginalSource?.GetType().Name}");
        }

        private void DashboardViewbox_PreviewMouseRightButtonUp(
            object sender,
            MouseButtonEventArgs e)
        {
            e.Handled = true;
            InteractionDiagnostics.Log($"ContextMenu right-up source={e.OriginalSource?.GetType().Name}");
            if (IsMusicControlSource(e.OriginalSource as DependencyObject)) return;

            DependencyObject? current = e.OriginalSource as DependencyObject;
            while (current != null && current != dashboardViewbox)
            {
                if(current is SensorTile sensor&&sensor.OpenOptions())return;
                if(current is Border sensorBorder&&(sensorBorder.DataContext is QuickLaunchBar.QuickLaunchSlot {IsSensor:true} or ShortcutsWidget.Slot {IsSensor:true})&&SensorTile.FindIn(sensorBorder)?.OpenOptions()==true)return;
                if(current is Border {DataContext:SystemMonitorWidget.MonitorSlot metric} metricBorder)
                {
                    for(var parent=GetSafeParent(metricBorder);parent!=null;parent=GetSafeParent(parent))
                        if(parent is SystemMonitorWidget monitor){monitor.OpenMetricOptions(metricBorder,metric);return;}
                }
                if (current is ModuleOptionsButton gear)
                {
                    gear.OpenOptions();
                    return;
                }
                if (current is FrameworkElement { ContextMenu: not null } localMenuOwner)
                {
                    if(ReferenceEquals(localMenuOwner.ContextMenu,ContextMenu))
                    {
                        OpenMainMenuAtScreenPoint(PointToScreen(e.GetPosition(this)));
                        return;
                    }
                    bool emptyShortcut = localMenuOwner.DataContext is QuickLaunchBar.QuickLaunchSlot { IsEmpty: true } ||
                        localMenuOwner.DataContext is ShortcutsWidget.Slot { IsEmpty: true };
                    if (emptyShortcut)
                    {
                        current = GetSafeParent(current);
                        continue;
                    }
                    var localPoint=e.GetPosition(localMenuOwner);
                    Dispatcher.BeginInvoke(DispatcherPriority.Input,new Action(()=>OpenLocalContextMenu(localMenuOwner,localPoint)));
                    return;
                }

                current = GetSafeParent(current);
            }

            if (ContextMenu is null)
            {
                return;
            }

            OpenMainMenuAtScreenPoint(PointToScreen(e.GetPosition(this)));
        }

        private static void OpenLocalContextMenu(FrameworkElement owner,Point localPoint)
        {
            // Web tiles build their menu on demand. Opening the XAML placeholder
            // directly bypasses ContextMenuOpening and used to show no commands.
            if(owner.DataContext is WebShortcutsWidget.WebShortcut shortcut)
                for(var parent=owner as DependencyObject;parent!=null;parent=GetSafeParent(parent))
                    if(parent is WebShortcutsWidget web){if(shortcut.IsAdd)return;owner.ContextMenu=web.CreateShortcutMenu(shortcut);break;}
            var menu=owner.ContextMenu;
            if(menu==null)return;
            bool clockMenu=Equals(menu.Tag,"ClockMenu");
            var anchor=Window.GetWindow(owner);
            menu.DataContext=owner.DataContext;
            menu.CustomPopupPlacementCallback=null;
            if(clockMenu||anchor==null)
            {
                menu.PlacementTarget=owner;
                menu.Placement=PlacementMode.Bottom;
                menu.HorizontalOffset=menu.VerticalOffset=0;
            }
            else
            {
                // Anchor to the unscaled window, not a templated child which
                // can be replaced when a macro refreshes its icon/hover state.
                var point=anchor.PointFromScreen(owner.PointToScreen(localPoint));
                menu.PlacementTarget=anchor;
                menu.Placement=PlacementMode.RelativePoint;
                menu.HorizontalOffset=point.X;
                menu.VerticalOffset=point.Y;
            }
            InteractionDiagnostics.Log($"LocalMenu anchor={menu.PlacementTarget?.GetType().Name} mode={menu.Placement} offset={menu.HorizontalOffset},{menu.VerticalOffset} source={owner.GetType().Name} point={localPoint}");
            menu.IsOpen=true;
            menu.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(()=>{if(menu.IsOpen)InteractionDiagnostics.Log($"LocalMenu settled anchor={menu.PlacementTarget?.GetType().Name} mode={menu.Placement} offset={menu.HorizontalOffset},{menu.VerticalOffset} screen={menu.PointToScreen(new Point())}");}));
        }

        internal static bool IsMusicControlSource(DependencyObject? source)
        {
            bool control=false;
            for(var current=source;current!=null;current=GetSafeParent(current))
            {
                if(current is FrameworkElement {Tag: "NoWindowDrag"} || current is System.Windows.Controls.Primitives.ButtonBase)control=true;
                if(current is MusicConsoleControl or MusicPlayerCard)return control;
            }
            return false;
        }

        private static bool IsInteractiveMenuSource(DependencyObject? source)
        {
            for (DependencyObject? current = source; current != null; current = GetSafeParent(current))
                if (current is System.Windows.Controls.Primitives.ButtonBase or Slider or
                    TextBox or ComboBox or System.Windows.Controls.Primitives.Thumb)
                    return true;
            return false;
        }

        private static DependencyObject? GetSafeParent(DependencyObject child)
        {
            // Tekstas WPF valdikliuose gali būti ContentElement (pvz. Run),
            // todėl jo negalima perduoti VisualTreeHelper.GetParent.
            if (child is ContentElement content)
            {
                DependencyObject? parent = ContentOperations.GetParent(content);
                if (parent != null) return parent;
                if (content is FrameworkContentElement frameworkContent)
                    return frameworkContent.Parent;
                return null;
            }

            return child is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(child)
                : LogicalTreeHelper.GetParent(child);
        }

        private void BuyMeACoffeeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://buymeacoffee.com/blakiukas")
                {
                    UseShellExecute = true
                });
            }
            catch
            {
                // Naršyklės paleidimo klaida neturi uždaryti dashboardo.
            }
        }

        private void Window_SourceInitialized(object? sender, EventArgs e)
        {
            HwndSource? source = PresentationSource.FromVisual(this) as HwndSource;
            windowHandle = source?.Handle ?? IntPtr.Zero;
            source?.AddHook(WindowProc);
            if(source!=null&&DiagnosticState.Root==null)
            {
                var hotkeys=new MacroHotkeys(this,source);
                Closed+=(_,_)=>hotkeys.Dispose();
            }
            ApplyAlwaysOnTop();
        }

        private void Window_Activated(object? sender, EventArgs e)
        {
            if (alwaysOnTopEnabled)
                ApplyAlwaysOnTop();
        }

        private void Window_StateChanged(object? sender, EventArgs e)
        {
            UpdateUsageOverlayVisibility();
            if (WindowState == WindowState.Minimized || !alwaysOnTopEnabled)
                return;

            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(ApplyAlwaysOnTop));
        }

        private void ApplyAlwaysOnTop()
        {
            bool effectiveTopmost = alwaysOnTopEnabled || activeBrowser != null;
            if (Topmost != effectiveTopmost)
                Topmost = effectiveTopmost;

            if (windowHandle == IntPtr.Zero)
                windowHandle = new WindowInteropHelper(this).Handle;

            if (windowHandle == IntPtr.Zero)
                return;

            SetWindowPos(
                windowHandle,
                effectiveTopmost ? HwndTopmost : HwndNoTopmost,
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpNoActivate);
            activeBrowser?.SetAlwaysOnTop(false);
            SynchronizeActiveBrowser();
            usageOverlayWindow?.AlignToDashboard();
        }

        private IntPtr WindowProc(
            IntPtr hwnd,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (message == 0x02E0 && !aligningLinkedWindows && WindowState == WindowState.Normal &&
                GetWindowRect(hwnd,out NativeRect beforeDpi))
            {
                // Let WPF update its DPI, but retain physical size on monitor transfer.
                var suggested=Marshal.PtrToStructure<NativeRect>(lParam);
                var retained=PixelBounds(suggested.Left,suggested.Top,beforeDpi.Right-beforeDpi.Left,beforeDpi.Bottom-beforeDpi.Top);
                Marshal.StructureToPtr(retained,lParam,false);
                long pendingVersion=geometryVersion;
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>
                {
                    if(geometryVersion==pendingVersion && GetWindowRect(hwnd,out NativeRect landed))
                        ApplyGeometry(PixelBounds(landed.Left,landed.Top,retained.Right-retained.Left,retained.Bottom-retained.Top));
                }));
            }
            if (message == WmSysCommand &&
                (wParam.ToInt64() & 0xFFF0) == ScSize)
            {
                resizeGesturePending = GetCursorPos(out resizeGestureStart);
                InteractionDiagnostics.Log(
                    $"Resize gesture armed cursor={resizeGestureStart.X},{resizeGestureStart.Y}");
            }
            else if (message == WmExitSizeMove)
            {
                resizeGesturePending = false;
                linkedResizeBrowserStart = null;
            }

            if (InteractionDiagnostics.Enabled && message is WmSizing or 0x0112)
                InteractionDiagnostics.Log(
                    $"WindowProc message=0x{message:X4} wParam=0x{wParam.ToInt64():X} " +
                    $"handledBefore={handled} state={WindowState} compact={isCompact} " +
                    $"bounds={Left:0.###},{Top:0.###},{ActualWidth:0.###},{ActualHeight:0.###} " +
                    $"expanded={expandedWidth:0.###},{expandedHeight:0.###}");
            if (message == WmSizing &&
                WindowState == WindowState.Normal)
            {
                if (resizeGesturePending &&
                    GetCursorPos(out NativePoint resizeCursor) &&
                    Math.Abs(resizeCursor.X - resizeGestureStart.X) < ResizeActivationDragPixels &&
                    Math.Abs(resizeCursor.Y - resizeGestureStart.Y) < ResizeActivationDragPixels)
                {
                    if (GetWindowRect(hwnd, out NativeRect unchangedBounds))
                        Marshal.StructureToPtr(unchangedBounds, lParam, false);
                    InteractionDiagnostics.Log(
                        $"Resize rejected without drag start={resizeGestureStart.X},{resizeGestureStart.Y} " +
                        $"current={resizeCursor.X},{resizeCursor.Y}");
                    handled = true;
                    return new IntPtr(1);
                }

                resizeGesturePending = false;
                EnforceDashboardAspectRatio(
                    wParam.ToInt32(),
                    lParam);
                handled = true;
                return new IntPtr(1);
            }

            if (message != WmNcHitTest || WindowState != WindowState.Normal)
                return IntPtr.Zero;

            // Auto dash hide keeps the same top-level window alive so Windows
            // retains one stable Alt+Tab identity. Once its visual content is
            // outside the clipped surface, let pointer input reach Web below.
            if (autoDashInputTransparent)
            {
                handled = true;
                return new IntPtr(HtTransparent);
            }

            // A touch on an interactive control can be followed by a promoted
            // non-client mouse hit-test. On a secondary high-DPI monitor that
            // hit-test was observed as HTRIGHT and started SC_SIZE/WMSZ_RIGHT,
            // shrinking the whole dashboard. Touch gestures never resize the
            // window; mouse edge resizing remains available after this guard.
            if (Environment.TickCount64 - lastTouchInputTick < 1000 ||
                Environment.TickCount64 < interactiveControlResizeSuppressedUntil)
            {
                InteractionDiagnostics.LogHitTest(
                    $"WM_NCHITTEST suppressed-to-client touchAge={Environment.TickCount64 - lastTouchInputTick} " +
                    $"resizeSuppressed={Environment.TickCount64 < interactiveControlResizeSuppressedUntil}");
                handled = true;
                return new IntPtr(HtClient);
            }

            int packedPosition = unchecked((int)lParam.ToInt64());
            Point screenPoint = new(
                (short)(packedPosition & 0xFFFF),
                (short)((packedPosition >> 16) & 0xFFFF));
            Point windowPoint = PointFromScreen(screenPoint);

            bool enlargedTopLeft = windowPoint.X >= 0 &&
                windowPoint.X < BottomRightResizeCornerSize &&
                windowPoint.Y >= 0 && windowPoint.Y < BottomRightResizeCornerSize;
            bool enlargedTopRight = windowPoint.X <= ActualWidth &&
                windowPoint.X > ActualWidth - BottomRightResizeCornerSize &&
                windowPoint.Y >= 0 && windowPoint.Y < BottomRightResizeCornerSize;
            if (enlargedTopLeft || enlargedTopRight)
            {
                handled = true;
                return new IntPtr(enlargedTopLeft ? HtTopLeft : HtTopRight);
            }

            // Compact Web chrome reaches the exterior edge. Retain the small
            // bottom-right mouse sizing grip before the toggle's client area.
            if (isCompact && activeBrowser != null &&
                windowPoint.X > ActualWidth - ResizeBorderSize && windowPoint.X <= ActualWidth &&
                windowPoint.Y > ActualHeight - ResizeBorderSize && windowPoint.Y <= ActualHeight)
            {
                handled = true;
                return new IntPtr(HtBottomRight);
            }

            // The top bar is an interactive surface all the way to the window edge.
            // Native non-client hit testing happens before WPF input, so a click on
            // the rightmost compact toggle could otherwise start SC_SIZE instead of
            // reaching the control.
            if (IsPointWithinTopBar(windowPoint))
            {
                InteractionDiagnostics.LogHitTest(
                    $"WM_NCHITTEST top-bar-to-client point={windowPoint.X:0.###},{windowPoint.Y:0.###}");
                handled = true;
                return new IntPtr(HtClient);
            }

            bool left = windowPoint.X >= 0 && windowPoint.X < ResizeBorderSize;
            bool right = windowPoint.X <= ActualWidth &&
                         windowPoint.X > ActualWidth - ResizeBorderSize;
            bool top = windowPoint.Y >= 0 && windowPoint.Y < TopResizeBorderSize;
            bool bottom = windowPoint.Y <= ActualHeight &&
                          windowPoint.Y > ActualHeight - ResizeBorderSize;
            bool enlargedBottomRight =
                windowPoint.X <= ActualWidth &&
                windowPoint.X > ActualWidth - BottomRightResizeCornerSize &&
                windowPoint.Y <= ActualHeight &&
                windowPoint.Y > ActualHeight - BottomRightResizeCornerSize;

            int hitTest = 0;

            if (enlargedBottomRight)
                hitTest = HtBottomRight;
            else if (top && left)
                hitTest = HtTopLeft;
            else if (top && right)
                hitTest = HtTopRight;
            else if (bottom && left)
                hitTest = HtBottomLeft;
            else if (bottom && right)
                hitTest = HtBottomRight;
            else if (left)
                hitTest = HtLeft;
            else if (right)
                hitTest = HtRight;
            else if (top)
                hitTest = HtTop;
            else if (bottom)
                hitTest = HtBottom;

            if (hitTest == 0)
                return IntPtr.Zero;

            InteractionDiagnostics.LogHitTest(
                $"WM_NCHITTEST edge={hitTest} point={windowPoint.X:0.###},{windowPoint.Y:0.###}");
            handled = true;
            return new IntPtr(hitTest);
        }

        private bool IsPointWithinTopBar(Point windowPoint)
        {
            if (!quickLaunchBar.IsVisible || !quickLaunchBar.IsLoaded)
                return false;
            try
            {
                Rect transformedBounds = quickLaunchBar.TransformToAncestor(this).TransformBounds(
                    new Rect(0, 0, quickLaunchBar.ActualWidth, quickLaunchBar.ActualHeight));
                return windowPoint.X >= 0 && windowPoint.X <= ActualWidth &&
                       windowPoint.Y >= 0 && windowPoint.Y <= transformedBounds.Bottom;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private double DashboardAspectRatio => 1920.0 / ExpandedDashboardCanvasHeight;
        private double ExpandedDashboardCanvasHeight =>
            musicConsoleEnabled ? FullDashboardCanvasHeight - (musicTopBarHidden ? 113 : 0) :
            wideShortcutsSpan == 3 && !showModuleTitles
                ? TitlelessWideDashboardCanvasHeight
                : FullDashboardCanvasHeight;

        private void EnforceDashboardAspectRatio(
            int sizingEdge,
            IntPtr rectanglePointer)
        {
            NativeRect rectangle =
                Marshal.PtrToStructure<NativeRect>(rectanglePointer);
            double ratio = 1920d / (isCompact ? CompactCanvasHeight : ExpandedDashboardCanvasHeight);
            int originalWidth = rectangle.Right - rectangle.Left;
            int originalHeight = rectangle.Bottom - rectangle.Top;
            int width = Math.Max(MinimumDashboardWidthPixels,
                sizingEdge is WmszTop or WmszBottom
                    ? (int)Math.Round(originalHeight * ratio) : originalWidth);
            int height = (int)Math.Round(width / ratio);

            if (sizingEdge is WmszLeft or WmszTopLeft or WmszBottomLeft)
                rectangle.Left = rectangle.Right - width;
            else if (sizingEdge is WmszTop or WmszBottom)
            {
                rectangle.Left -= (width - originalWidth) / 2;
                rectangle.Right = rectangle.Left + width;
            }
            else rectangle.Right = rectangle.Left + width;

            if (sizingEdge is WmszTop or WmszTopLeft or WmszTopRight)
                rectangle.Top = rectangle.Bottom - height;
            else if (sizingEdge is WmszLeft or WmszRight)
            {
                rectangle.Top -= (height - originalHeight) / 2;
                rectangle.Bottom = rectangle.Top + height;
            }
            else rectangle.Bottom = rectangle.Top + height;

            ResizeLinkedBrowser(rectangle);

            Marshal.StructureToPtr(
                rectangle,
                rectanglePointer,
                false);
        }

        internal void SuppressResizeAfterInteractiveControl() =>
            interactiveControlResizeSuppressedUntil = Environment.TickCount64 + 1500;

        private void Window_MouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            // WPF promotes an unhandled touch to a mouse event. Never start the
            // blocking window drag loop from a finger gesture inside a widget.
            if (e.StylusDevice != null ||
                Environment.TickCount64 - lastTouchInputTick < 1000)
                return;

            if (IsWindowDragExcluded(e.OriginalSource as DependencyObject))
                return;

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragFromPlaylistBackground(e);
            }
        }

        internal void DragFromPlaylistBackground(MouseButtonEventArgs e)
        {
            if(e.StylusDevice!=null || Environment.TickCount64-lastTouchInputTick<1000 || e.LeftButton!=MouseButtonState.Pressed)return;
            e.Handled=true;
            BeginLinkedWindowMove();
            try { DragMove(); }
            finally { CompleteLinkedWindowMove(); }
        }

        private void Window_PreviewTouchDown(object? sender, TouchEventArgs e)
        {
            if(e.Handled&&SettingsWindow.Sessions.Count>0)return;
            InteractionDiagnostics.Log($"TouchDown device={e.TouchDevice.Id} source={e.OriginalSource?.GetType().Name} compact={isCompact} dpi={VisualTreeHelper.GetDpi(this).PixelsPerDip * 96:0}");
            MarkLocalActivity();
            lastTouchInputTick = Environment.TickCount64;
            if (IsMusicControlSource(e.OriginalSource as DependencyObject)) return;
            if(windowDragTouchDevice!=null)return;
            // Tap distance is in screen DIPs, independent of dashboard Viewbox scale.
            Point menuPoint = e.GetTouchPoint(this).Position;
            if (menuDoubleTap.IsSecondTap(lastTouchInputTick, menuPoint) &&
                !IsInteractiveMenuSource(e.OriginalSource as DependencyObject) &&
                ContextMenu != null)
            {
                // Complete the gesture before opening a popup, so its release
                // cannot become an unintended menu click or outside dismissal.
                touchMenuPending=true;
                windowDragTouchDevice=e.TouchDevice;
                windowDragTouchStart=PointToScreen(e.GetTouchPoint(this).Position);
                CaptureTouch(e.TouchDevice);
                e.Handled = true;
                return;
            }
            if (windowDragTouchDevice != null ||
                IsWindowDragExcluded(e.OriginalSource as DependencyObject, e))
                return;

            windowDragTouchDevice = e.TouchDevice;
            windowDragTouchStart = PointToScreen(e.GetTouchPoint(this).Position);
            GetWindowRect(windowHandle, out NativeRect touchStart); windowDragWindowStart = new Point(touchStart.Left, touchStart.Top);
            windowTouchDragStarted = false;
            CaptureTouch(e.TouchDevice);
            touchHoldMenuOpened = false;
            touchMenuHoldTimer.Start();
            e.Handled = true;
        }

        private void TouchMenuHoldTimer_Tick(object? sender, EventArgs e)
        {
            touchMenuHoldTimer.Stop();
            if (windowDragTouchDevice == null || windowTouchDragStarted || touchMenuPending ||
                !ReferenceEquals(windowDragTouchDevice.Captured, this)) return;
            var point = windowDragTouchDevice.GetTouchPoint(this).Position;
            var delta = PointToScreen(point) - windowDragTouchStart;
            if (TouchMovedBeyondTap(delta.X, delta.Y)) return;
            menuDoubleTap.Reset();
            touchHoldMenuOpened = true;
            // Keep the initiating finger captured until release: it must not
            // activate a menu item or start dragging underneath the popup.
            OpenTouchMainMenu(point);
        }

        private void Window_LostTouchCapture(object? sender, TouchEventArgs e)
        {
            if (!ReferenceEquals(windowDragTouchDevice, e.TouchDevice) ||
                ReferenceEquals(e.TouchDevice.Captured, this)) return;
            touchMenuHoldTimer.Stop();
            windowDragTouchDevice = null;
            touchMenuPending = false;
            touchHoldMenuOpened = false;
            menuDoubleTap.Reset();
            if (windowTouchDragStarted) CompleteLinkedWindowMove();
            windowTouchDragStarted = false;
        }

        private void OpenTouchMainMenu(Point windowPoint)
        {
            ContextMenu.PlacementTarget=this;
            ContextMenu.Placement=PlacementMode.RelativePoint;
            ContextMenu.HorizontalOffset=windowPoint.X;
            ContextMenu.VerticalOffset=windowPoint.Y;
            ContextMenu.IsOpen=true;
        }

        internal bool AreTopBarTabsDragBlocked => true;

        private bool TouchMovedBeyondTap(double deltaX, double deltaY)
        {
            // A finger rolls within its contact patch. The old 8 physical pixels
            // cancelled ordinary taps, particularly on high-DPI touchscreens.
            var dpi = VisualTreeHelper.GetDpi(this);
            return Math.Abs(deltaX) >= 18 * dpi.DpiScaleX ||
                   Math.Abs(deltaY) >= 18 * dpi.DpiScaleY;
        }

        private void Window_PreviewTouchMove(object? sender, TouchEventArgs e)
        {
            lastTouchInputTick = Environment.TickCount64;
            if (!ReferenceEquals(windowDragTouchDevice, e.TouchDevice))
                return;

            if (touchHoldMenuOpened) { e.Handled = true; return; }

            Point current = PointToScreen(e.GetTouchPoint(this).Position);
            double deltaX = current.X - windowDragTouchStart.X;
            double deltaY = current.Y - windowDragTouchStart.Y;
            if(touchMenuPending)
            {
                if(TouchMovedBeyondTap(deltaX,deltaY)){touchMenuPending=false;windowDragTouchDevice=null;ReleaseTouchCapture(e.TouchDevice);}
                e.Handled=true;return;
            }
            if (!windowTouchDragStarted &&
                !TouchMovedBeyondTap(deltaX, deltaY))
            {
                e.Handled = true;
                return;
            }

            if (!windowTouchDragStarted)
            {
                touchMenuHoldTimer.Stop();
                menuDoubleTap.Reset();
                BeginLinkedWindowMove();
                windowTouchDragStarted = true;
            }
            SetWindowPos(windowHandle,IntPtr.Zero,(int)Math.Round(windowDragWindowStart.X+deltaX),(int)Math.Round(windowDragWindowStart.Y+deltaY),0,0,SwpNoSize|SwpNoActivate);
            e.Handled = true;
        }

        private void Window_PreviewTouchUp(object? sender, TouchEventArgs e)
        {
            lastTouchInputTick = Environment.TickCount64;
            if (!ReferenceEquals(windowDragTouchDevice, e.TouchDevice))
                return;

            touchMenuHoldTimer.Stop();
            bool heldMenu = touchHoldMenuOpened;
            touchHoldMenuOpened = false;
            Point release=e.GetTouchPoint(this).Position;
            Vector releaseDelta=PointToScreen(release)-windowDragTouchStart;
            bool isTap=!windowTouchDragStarted&&!TouchMovedBeyondTap(releaseDelta.X,releaseDelta.Y);
            bool openMenu=touchMenuPending&&isTap;touchMenuPending=false;
            windowDragTouchDevice = null;
            ReleaseTouchCapture(e.TouchDevice);
            if (heldMenu) { e.Handled = true; return; }
            if(openMenu)
            {
                OpenTouchMainMenu(release);e.Handled=true;return;
            }
            if (isTap)
            {
                menuDoubleTap.RecordTapUp(lastTouchInputTick,
                    release);
            }
            else menuDoubleTap.Reset();
            if(windowTouchDragStarted) CompleteLinkedWindowMove();
            windowTouchDragStarted = false;
            e.Handled = true;
        }

        private static bool IsWindowDragExcluded(DependencyObject? source, TouchEventArgs? touch = null)
        {
            for (DependencyObject? current = source; current != null;)
            {
                if(current is ListBoxItem or System.Windows.Controls.Primitives.ScrollBar)return true;
                if(current is ListBox playlistList && Equals(playlistList.Tag,"PlaylistList"))return false;
                if (current is Slider or System.Windows.Controls.Primitives.Thumb or
                    System.Windows.Controls.Primitives.ButtonBase or TextBox or ComboBox or
                    QuickLaunchBar)
                    return true;
                if (current is MusicConsoleControl or MusicPlayerCard or AnalogVuCard or AnalogVuControl or PlaylistCard) return false;
                if (current is UserControl module)
                {
                    // Use the module's actual first row, including its upper padding.
                    // Hidden/collapsed title rows do not create a drag surface.
                    if (module.Content is Border frame && frame.Child is Grid grid &&
                        grid.RowDefinitions.Count > 0 && grid.RowDefinitions[0].ActualHeight > 0)
                    {
                        Point point = touch?.GetTouchPoint(module).Position ?? Mouse.GetPosition(module);
                        double headerBottom = grid.TranslatePoint(new Point(0,
                            grid.RowDefinitions[0].ActualHeight), module).Y;
                        return point.X < 0 || point.X > module.ActualWidth ||
                            point.Y < 0 || point.Y >= headerBottom;
                    }
                    return true;
                }
                if (current is FrameworkElement element)
                {
                    if (string.Equals(element.Tag as string, "NoWindowDrag", StringComparison.Ordinal) ||
                        element.Cursor == Cursors.Hand)
                        return true;
                }
                current = current is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(current)
                    : LogicalTreeHelper.GetParent(current);
            }
            return true;
        }

        private void CloseMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MonitorControlsMenuItem_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            ReleasePhysicalMonitor();
            updatingMonitorControls = true;
            ResetMonitorControlAvailability();

            IntPtr logicalMonitor = MonitorFromWindow(
                new WindowInteropHelper(this).Handle,
                MonitorDefaultToNearest);
            MonitorInfo info = new() { Size = Marshal.SizeOf<MonitorInfo>() };
            string logicalName = GetMonitorInfo(logicalMonitor, ref info)
                ? info.DeviceName
                : logicalMonitor.ToInt64().ToString("X");

            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(logicalMonitor, out uint count) || count == 0)
            {
                updatingMonitorControls = false;
                return;
            }

            PhysicalMonitor[] monitors = new PhysicalMonitor[count];
            if (!GetPhysicalMonitorsFromHMONITOR(logicalMonitor, count, monitors))
            {
                updatingMonitorControls = false;
                return;
            }

            for (int index = 0; index < monitors.Length; index++)
            {
                PhysicalMonitor physical = monitors[index];
                if (activePhysicalMonitor == IntPtr.Zero && SupportsAnyMonitorControl(physical.Handle))
                {
                    activePhysicalMonitor = physical.Handle;
                    activeMonitorKey = $"{logicalName}|{physical.Description}";
                    ConfigureMonitorControls();
                }
                else
                {
                    DestroyPhysicalMonitor(physical.Handle);
                }
            }

            updatingMonitorControls = false;
        }

        private void MonitorControlsMenuItem_SubmenuClosed(object sender, RoutedEventArgs e) =>
            ReleasePhysicalMonitor();

        private void MonitorBrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (updatingMonitorControls || !monitorBrightnessSupported || activePhysicalMonitor == IntPtr.Zero)
                return;

            uint value = (uint)Math.Round(e.NewValue);
            if (!SetMonitorBrightness(activePhysicalMonitor, value))
                return;

            double range = monitorBrightnessSlider.Maximum - monitorBrightnessSlider.Minimum;
            monitorBrightnessValue.Text = range <= 0
                ? "N/A"
                : $"{Math.Round((value - monitorBrightnessSlider.Minimum) * 100d / range)}%";
            if (!string.IsNullOrWhiteSpace(activeMonitorKey))
            {
                savedMonitorControls[MonitorSettingKey("Brightness")] = value;
                SaveMonitorSettings();
            }
        }

        private void MonitorContrastSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (updatingMonitorControls || !monitorContrastSupported || activePhysicalMonitor == IntPtr.Zero)
                return;
            uint value = (uint)Math.Round(e.NewValue);
            if (!SetMonitorContrast(activePhysicalMonitor, value)) return;
            monitorContrastValue.Text = FormatMonitorPercent(value, monitorContrastSlider.Minimum, monitorContrastSlider.Maximum);
            SaveMonitorControlValue("Contrast", value);
        }

        private void MonitorColorTemperatureSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (updatingMonitorControls || !monitorColorTemperatureSupported || activePhysicalMonitor == IntPtr.Zero)
                return;
            uint value = (uint)Math.Clamp(Math.Round(e.NewValue), 1, 8);
            if (!SetMonitorColorTemperature(activePhysicalMonitor, (MonitorColorTemperature)value)) return;
            monitorColorTemperatureValue.Text = ColorTemperatureText(value);
            SaveMonitorControlValue("ColorTemperature", value);
        }

        private void MonitorHueSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
            SetVcpSliderValue(0x90, "Hue", monitorHueSlider, monitorHueValue,
                monitorHueSupported, e.NewValue);

        private void MonitorSaturationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
            SetVcpSliderValue(0x8A, "Saturation", monitorSaturationSlider, monitorSaturationValue,
                monitorSaturationSupported, e.NewValue);

        private void ResetMonitorControlAvailability()
        {
            monitorBrightnessSupported = monitorContrastSupported = monitorColorTemperatureSupported =
                monitorHueSupported = monitorSaturationSupported = false;
            SetMonitorControlAvailability(monitorBrightnessMenuItem, monitorBrightnessValue, false);
            SetMonitorControlAvailability(monitorContrastMenuItem, monitorContrastValue, false);
            SetMonitorControlAvailability(monitorColorTemperatureMenuItem, monitorColorTemperatureValue, false);
            SetMonitorControlAvailability(monitorHueMenuItem, monitorHueValue, false);
            SetMonitorControlAvailability(monitorSaturationMenuItem, monitorSaturationValue, false);
        }

        private static void SetMonitorControlAvailability(MenuItem item, OpticallyCenteredText valueText, bool available)
        {
            item.IsEnabled = available;
            item.Opacity = available ? 1 : 0.42;
            if (!available) valueText.Text = "N/A";
        }

        private static bool SupportsAnyMonitorControl(IntPtr handle) =>
            (GetMonitorBrightness(handle, out uint brightnessMin, out _, out uint brightnessMax) && brightnessMax > brightnessMin) ||
            (GetMonitorContrast(handle, out uint contrastMin, out _, out uint contrastMax) && contrastMax > contrastMin) ||
            GetMonitorColorTemperature(handle, out MonitorColorTemperature temperature) && temperature != MonitorColorTemperature.Unknown ||
            (GetVCPFeatureAndVCPFeatureReply(handle, 0x90, out _, out _, out uint hueMax) && hueMax > 0) ||
            (GetVCPFeatureAndVCPFeatureReply(handle, 0x8A, out _, out _, out uint saturationMax) && saturationMax > 0);

        private void ConfigureMonitorControls()
        {
            if (GetMonitorBrightness(activePhysicalMonitor, out uint brightnessMin,
                    out uint brightnessCurrent, out uint brightnessMax) && brightnessMax > brightnessMin)
            {
                monitorBrightnessSupported = true;
                ConfigureRangeControl("Brightness", monitorBrightnessSlider, monitorBrightnessValue,
                    monitorBrightnessMenuItem, brightnessMin, brightnessCurrent, brightnessMax,
                    SetMonitorBrightness);
            }

            if (GetMonitorContrast(activePhysicalMonitor, out uint contrastMin,
                    out uint contrastCurrent, out uint contrastMax) && contrastMax > contrastMin)
            {
                monitorContrastSupported = true;
                ConfigureRangeControl("Contrast", monitorContrastSlider, monitorContrastValue,
                    monitorContrastMenuItem, contrastMin, contrastCurrent, contrastMax,
                    SetMonitorContrast);
            }

            if (GetMonitorColorTemperature(activePhysicalMonitor, out MonitorColorTemperature temperature) &&
                temperature != MonitorColorTemperature.Unknown)
            {
                monitorColorTemperatureSupported = true;
                uint current = (uint)temperature;
                if (TryGetSavedMonitorControlValue("ColorTemperature", out uint saved))
                {
                    current = Math.Clamp(saved, 1, 8);
                    SetMonitorColorTemperature(activePhysicalMonitor, (MonitorColorTemperature)current);
                }
                monitorColorTemperatureSlider.Value = current;
                monitorColorTemperatureValue.Text = ColorTemperatureText(current);
                SetMonitorControlAvailability(monitorColorTemperatureMenuItem, monitorColorTemperatureValue, true);
            }

            ConfigureVcpControl(0x90, "Hue", monitorHueSlider, monitorHueValue,
                monitorHueMenuItem, out monitorHueSupported);
            ConfigureVcpControl(0x8A, "Saturation", monitorSaturationSlider, monitorSaturationValue,
                monitorSaturationMenuItem, out monitorSaturationSupported);
        }

        private delegate bool SetRangeMonitorValue(IntPtr handle, uint value);

        private void ConfigureRangeControl(string settingName, Slider slider, OpticallyCenteredText valueText,
            MenuItem menuItem, uint minimum, uint current, uint maximum, SetRangeMonitorValue setter)
        {
            if (TryGetSavedMonitorControlValue(settingName, out uint saved))
            {
                current = Math.Clamp(saved, minimum, maximum);
                setter(activePhysicalMonitor, current);
            }
            slider.Minimum = minimum;
            slider.Maximum = maximum;
            slider.Value = current;
            valueText.Text = FormatMonitorPercent(current, minimum, maximum);
            SetMonitorControlAvailability(menuItem, valueText, true);
        }

        private void ConfigureVcpControl(byte code, string settingName, Slider slider,
            OpticallyCenteredText valueText, MenuItem menuItem, out bool supported)
        {
            supported = GetVCPFeatureAndVCPFeatureReply(activePhysicalMonitor, code, out _,
                out uint current, out uint maximum) && maximum > 0;
            if (!supported) return;
            if (TryGetSavedMonitorControlValue(settingName, out uint saved))
            {
                current = Math.Min(saved, maximum);
                SetVCPFeature(activePhysicalMonitor, code, current);
            }
            slider.Minimum = 0;
            slider.Maximum = maximum;
            slider.Value = current;
            valueText.Text = FormatMonitorPercent(current, 0, maximum);
            SetMonitorControlAvailability(menuItem, valueText, true);
        }

        private void SetVcpSliderValue(byte code, string settingName, Slider slider,
            OpticallyCenteredText valueText, bool supported, double newValue)
        {
            if (updatingMonitorControls || !supported || activePhysicalMonitor == IntPtr.Zero) return;
            uint value = (uint)Math.Round(newValue);
            if (!SetVCPFeature(activePhysicalMonitor, code, value)) return;
            valueText.Text = FormatMonitorPercent(value, slider.Minimum, slider.Maximum);
            SaveMonitorControlValue(settingName, value);
        }

        private static string FormatMonitorPercent(double value, double minimum, double maximum) =>
            maximum <= minimum ? "N/A" : $"{Math.Round((value - minimum) * 100d / (maximum - minimum))}%";

        private static string ColorTemperatureText(uint value) => value switch
        {
            1 => "4000 K", 2 => "5000 K", 3 => "6500 K", 4 => "7500 K",
            5 => "8200 K", 6 => "9300 K", 7 => "10000 K", 8 => "11500 K",
            _ => "N/A"
        };

        private string MonitorSettingKey(string settingName) =>
            settingName == "Brightness" ? activeMonitorKey! : $"{activeMonitorKey}|{settingName}";

        private bool TryGetSavedMonitorControlValue(string settingName, out uint value)
        {
            value = 0;
            return !string.IsNullOrWhiteSpace(activeMonitorKey) &&
                savedMonitorControls.TryGetValue(MonitorSettingKey(settingName), out value);
        }

        private void SaveMonitorControlValue(string settingName, uint value)
        {
            if (string.IsNullOrWhiteSpace(activeMonitorKey)) return;
            savedMonitorControls[MonitorSettingKey(settingName)] = value;
            SaveMonitorSettings();
        }

        private void ReleasePhysicalMonitor()
        {
            if (activePhysicalMonitor != IntPtr.Zero)
                DestroyPhysicalMonitor(activePhysicalMonitor);
            activePhysicalMonitor = IntPtr.Zero;
            activeMonitorKey = null;
        }

        private void LoadMonitorSettings()
        {
            try
            {
                if (!File.Exists(monitorSettingsFile)) return;
                Dictionary<string, uint>? settings = JsonSerializer.Deserialize<Dictionary<string, uint>>(
                    File.ReadAllText(monitorSettingsFile));
                if (settings == null) return;
                foreach ((string key, uint value) in settings)
                    savedMonitorControls[key] = value;
            }
            catch { }
        }

        private void SaveMonitorSettings()
        {
            try
            {
                Directory.CreateDirectory(settingsFolder);
                File.WriteAllText(monitorSettingsFile, JsonSerializer.Serialize(savedMonitorControls));
            }
            catch { }
        }

        private void AlwaysOnTopMenuItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            alwaysOnTopEnabled = alwaysOnTopMenuItem.IsChecked;
            ApplyAlwaysOnTop();

            Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(() =>
                {
                    if (ContextMenu != null)
                        ContextMenu.IsOpen = true;
                }));
        }

        private void AutoDimMenuItem_Click(object sender, RoutedEventArgs e)
        {
            autoDimElements = autoDimMenuItem.IsChecked;
            if (!autoDimElements) SetElementsAutoDimmed(false);
            SaveWindowSettings();
        }

        private void AutoDashHideMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SetWebBarsAutoHide(autoDashHideMenuItem.IsChecked);
        }

        private void AutoDimLevelSlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            autoDimOpacityPercent = Math.Clamp(e.NewValue, 20, 70);
            if (autoDimLevelValueText != null)
                autoDimLevelValueText.Text = $"{autoDimOpacityPercent:0}%";
            if (!updatingAutoDimSettings && elementsAutoDimmed)
                ApplyAutoDimOpacity();
        }

        private void AutoDimTimer_Tick(object? sender, EventArgs e)
        {
            if (!autoDimElements && !autoDashHideEnabled && activeBrowser == null)
            {
                SetElementsAutoDimmed(false);
                SetAutoDashHidden(false);
                return;
            }

            if (GetCursorPos(out NativePoint cursor))
            {
                bool moved = !hasDimCursorPosition || cursor.X != lastDimCursorPosition.X ||
                    cursor.Y != lastDimCursorPosition.Y;
                lastDimCursorPosition = cursor;
                hasDimCursorPosition = true;
                IntPtr dashboardMonitor = MonitorFromWindow(
                    new WindowInteropHelper(this).Handle, MonitorDefaultToNearest);
                if (moved && MonitorFromPoint(cursor, MonitorDefaultToNearest) == dashboardMonitor)
                    MarkLocalActivity();
            }
            bool canIdle = openModuleOptions?.IsOpen != true && ContextMenu?.IsOpen != true && activeBrowser?.InteractionInProgress != true;
            long idleMilliseconds = Environment.TickCount64 - lastLocalActivityTick;
            bool inactive = canIdle && idleMilliseconds >= 10000;
            webPageIdle = canIdle && idleMilliseconds >= 3000;
            UpdateWebPageLayout();
            SetElementsAutoDimmed(autoDimElements && inactive);
            SetAutoDashHidden(autoDashHideEnabled && webPageIdle && activeBrowser != null && isCompact);
        }

        private void MarkLocalActivity()
        {
            lastLocalActivityTick = Environment.TickCount64;
            SetElementsAutoDimmed(false);
            SetAutoDashHidden(false);
            webPageIdle = false;
            UpdateWebPageLayout();
        }

        private void SetAutoDashHidden(bool hidden, bool immediate = false)
        {
            hidden &= autoDashHideEnabled && activeBrowser != null && isCompact;
            // The inactivity timer runs every 250 ms. Do not restart an in-flight
            // transition toward the state it is already approaching.
            if (autoDashHidden == hidden) return;
            autoDashHidden = hidden;
            int version = ++autoDashAnimationVersion;
            TimeSpan duration = immediate ? TimeSpan.Zero : AutoDashAnimationDuration;
            double startY = autoDashTranslate.Y;
            autoDashTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            autoDashTranslate.Y = startY;
            double travel = Math.Max(ActualHeight, dashboardHost.ActualHeight);
            if (travel <= 0) travel = CompactCanvasHeight;
            double targetY = hidden ? (compactDockEdge == DockEdge.Top ? -travel : travel) : 0d;
            if (!hidden) autoDashInputTransparent = false;

            void CompleteSlide()
            {
                if (version != autoDashAnimationVersion) return;
                autoDashTranslate.BeginAnimation(TranslateTransform.YProperty, null);
                autoDashTranslate.Y = targetY;
                autoDashInputTransparent = hidden;
                InteractionDiagnostics.Log(
                    $"AutoDashHidden settled hidden={hidden} visualY={targetY:0.###} " +
                    $"windowVisible={IsVisible} inputTransparent={autoDashInputTransparent} " +
                    $"windowBounds={Left:0.###},{Top:0.###},{ActualWidth:0.###},{ActualHeight:0.###}");
            }

            if (immediate)
            {
                CompleteSlide();
                return;
            }

            DoubleAnimation animation = new(startY, targetY, duration)
            {
                EasingFunction = FullscreenBrowserWindow.CreateInteractionEasing(),
                FillBehavior = FillBehavior.Stop
            };
            animation.Completed += (_, _) =>
            {
                CompleteSlide();
            };
            autoDashTranslate.BeginAnimation(
                TranslateTransform.YProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        private void MaintainAutoDashHiddenOffset()
        {
            if (!autoDashHidden) return;
            autoDashTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            autoDashTranslate.Y = (compactDockEdge == DockEdge.Top ? -1 : 1) * Math.Max(ActualHeight, dashboardHost.ActualHeight);
        }

        private void SetElementsAutoDimmed(bool dimmed)
        {
            if (elementsAutoDimmed == dimmed) return;
            elementsAutoDimmed = dimmed;
            ApplyAutoDimOpacity();
        }

        private void ApplyAutoDimOpacity()
        {
            double dimAmount = elementsAutoDimmed
                ? 1d - autoDimOpacityPercent / 100d
                : 0d;
            AnimateOpacity(autoDimOverlay, dimAmount);

            // Compact chrome has transparent margins and rounded controls. Use
            // the rendered bar alpha instead of painting a new window-sized panel.
            autoDimOverlay.OpacityMask = isCompact && quickLaunchBar.ActualWidth > 0
                ? new VisualBrush(quickLaunchBar)
                {
                    ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = new Rect(quickLaunchBar.RenderSize),
                    ViewportUnits = BrushMappingMode.Absolute,
                    Viewport = quickLaunchBar.TransformToAncestor(dashboardCanvas)
                        .TransformBounds(new Rect(quickLaunchBar.RenderSize)),
                    Stretch = Stretch.Fill,
                    TileMode = TileMode.None
                }
                : null;
            // Global idle dim covers tabs, housing and emissive surfaces with one
            // animation. The player's independent stopped/power dim stays local.
            autoDimOverlay.Clip = null;
            musicConsole?.SetExternalDimming(0);
            usageOverlayWindow?.SetAutoDimAmount(dimAmount);
            activeBrowser?.SetAutoDimAmount(dimAmount);
        }

        private void ApplyUiOpacityImmediate()
        {
            dashboardCanvas.BeginAnimation(UIElement.OpacityProperty, null);
            dashboardCanvas.Opacity = 1;
            quickLaunchBar.Opacity = uiOpacity;
            foreach (var host in new[] { leftModuleHost, centerModuleHost, rightModuleHost, wideShortcutsHost })
                if (host != null) host.Opacity = musicConsoleEnabled ? 0 : uiOpacity;
            musicConsoleHost.Opacity = uiOpacity;
            if (ContextMenu != null)
            {
                ContextMenu.BeginAnimation(UIElement.OpacityProperty, null);
                ContextMenu.Opacity = 1;
            }
            usageOverlayWindow?.SetUiOpacity(uiOpacity);
            activeBrowser?.SetUiOpacity(uiOpacity);
        }

        private static void AnimateOpacity(UIElement element, double target)
        {
            DoubleAnimation animation = new(target, TimeSpan.FromMilliseconds(650))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.HoldEnd
            };
            element.BeginAnimation(UIElement.OpacityProperty, animation,
                HandoffBehavior.SnapshotAndReplace);
        }

        private void SynchronizeActiveBrowser()
        {
            // Ordinary dashboard size changes must not implicitly fit the web
            // surface to a monitor.  Fit is an explicit user command.
        }

        private void FitActiveBrowserToCurrentMonitorPixels()
        {
            if (activeBrowser == null || windowHandle == IntPtr.Zero) return;
            IntPtr monitor = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
            MonitorInfo info = new() { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return;
            activeBrowser.ClearDashboardWorkArea();
            SetWindowPos(activeBrowser.GetNativeHandle(), IntPtr.Zero,
                info.WorkArea.Left, info.WorkArea.Top,
                info.WorkArea.Right - info.WorkArea.Left,
                info.WorkArea.Bottom - info.WorkArea.Top,
                SwpNoActivate);
            ApplyGeometry(ModeBoundsPixels());
        }

        internal void BeginLinkedWindowMove()
        {
            linkedMoveFromWeb = false;
            linkedMoveDeltaX = linkedMoveDeltaY = 0;
            linkedMoveCaptured = GetWindowRect(windowHandle, out linkedDashboardStartPixels);
            if (activeBrowser != null)
                linkedMoveCaptured &= GetWindowRect(activeBrowser.GetNativeHandle(), out linkedBrowserStartPixels);
        }

        internal void CompleteLinkedWindowMove()
        {
            if (!linkedMoveCaptured) return;
            linkedMoveCaptured = false;
            if (!GetWindowRect(windowHandle, out NativeRect currentDashboard)) return;
            IntPtr monitor = MonitorFromWindow(linkedMoveFromWeb && activeBrowser != null
                ? activeBrowser.GetNativeHandle() : windowHandle, MonitorDefaultToNearest);
            MonitorInfo info = new() { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return;

            // Preserve the starting physical size and the relative positions of
            // both surfaces. Only reduce the entire group when it cannot fit.
            FullscreenBrowserWindow? movedBrowser = activeBrowser;
            NativeRect start = linkedDashboardStartPixels;
            NativeRect web = movedBrowser == null ? start : linkedBrowserStartPixels;
            int left = Math.Min(start.Left, web.Left);
            int top = Math.Min(start.Top, web.Top);
            int right = Math.Max(start.Right, web.Right);
            int bottom = Math.Max(start.Bottom, web.Bottom);
            double scale = Math.Min(1d, Math.Min(
                (double)(info.WorkArea.Right - info.WorkArea.Left) / Math.Max(1, right - left),
                (double)(info.WorkArea.Bottom - info.WorkArea.Top) / Math.Max(1, bottom - top)));
            int groupWidth = (int)Math.Round((right - left) * scale);
            int groupHeight = (int)Math.Round((bottom - top) * scale);
            int x = Math.Clamp(currentDashboard.Left - linkedDashboardStartPixels.Left + left,
                info.WorkArea.Left, Math.Max(info.WorkArea.Left, info.WorkArea.Right - groupWidth));
            int y = Math.Clamp(currentDashboard.Top - linkedDashboardStartPixels.Top + top,
                info.WorkArea.Top, Math.Max(info.WorkArea.Top, info.WorkArea.Bottom - groupHeight));

            NativeRect Target(NativeRect original) => PixelBounds(
                x + (int)Math.Round((original.Left-left)*scale),
                y + (int)Math.Round((original.Top-top)*scale),
                (int)Math.Round((original.Right-original.Left)*scale),
                (int)Math.Round((original.Bottom-original.Top)*scale));
            ApplyGeometry(Target(start), movedBrowser == null ? null : Target(web));
        }
        private static string FormatNativeRect(NativeRect rect) =>
            $"{rect.Left},{rect.Top},{rect.Right - rect.Left},{rect.Bottom - rect.Top}";

        private void SleepModeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            sleepComputer = ReferenceEquals(sender, sleepPcMenuItem);
            quickLaunchBar.SleepComputer = sleepComputer;
            UpdateSleepModeChecks();
        }

        private void UpdateSleepModeChecks()
        {
            sleepMonitorMenuItem.IsChecked = !sleepComputer;
            sleepPcMenuItem.IsChecked = sleepComputer;
        }

        private void TopBarControlVisibility_Click(object sender, RoutedEventArgs e)
        {
            bool success = sender switch
            {
                MenuItem item when item == showClockMenuItem => quickLaunchBar.TrySetShowClock(item.IsChecked, out _),
                MenuItem item when item == showSleepMenuItem => quickLaunchBar.TrySetShowSleep(item.IsChecked, out _),
                MenuItem item when item == showShutdownMenuItem => quickLaunchBar.TrySetShowShutdown(item.IsChecked, out _),
                MenuItem item when item == showRestartMenuItem => quickLaunchBar.TrySetShowRestart(item.IsChecked, out _),
                _ => true
            };
            if (!success)
            {
                string message = sender == showClockMenuItem
                    ? "Free two shortcut slots before enabling the clock."
                    : "Free one shortcut slot before enabling this control.";
                AppMessage.Show(message, "Win controls", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            UpdateTopBarControlChecks(); showSleepMenuItem.Visibility=Visibility.Collapsed; showShutdownMenuItem.Visibility=Visibility.Collapsed; showRestartMenuItem.Visibility=Visibility.Collapsed;
            SaveWindowSettings();
        }

        private void UpdateTopBarControlChecks()
        {
            showClockMenuItem.IsChecked = quickLaunchBar.ShowClock;
            showSleepMenuItem.IsChecked = quickLaunchBar.ShowSleep;
            showShutdownMenuItem.IsChecked = quickLaunchBar.ShowShutdown;
            showRestartMenuItem.IsChecked = quickLaunchBar.ShowRestart;
        }

        private void ObsControlVisibility_Click(object sender, RoutedEventArgs e)
        {
            bool success = sender switch
            {
                MenuItem item when item == showObsRecordMenuItem => quickLaunchBar.TrySetShowObsRecord(item.IsChecked, out _),
                MenuItem item when item == showObsBufferMenuItem => quickLaunchBar.TrySetShowObsBuffer(item.IsChecked, out _),
                _ => true
            };
            if (!success)
                AppMessage.Show("Free one shortcut slot before enabling this OBS control.",
                    "OBS controls", MessageBoxButton.OK, MessageBoxImage.Information);
            UpdateObsControlChecks();
            SaveWindowSettings();
        }

        private void UpdateObsControlChecks()
        {
            showObsRecordMenuItem.IsChecked = quickLaunchBar.ShowObsRecord;
            showObsBufferMenuItem.IsChecked = quickLaunchBar.ShowObsBuffer;
        }

        private void UsageMiniDemandChanged(){if(UsageMiniState.Consumers>0){if(!telemetryTimer.IsEnabled)StartTelemetry();}else StopTelemetry();}

        private void StartTelemetry()
        {
            if(telemetryTimer.IsEnabled){_=RefreshTelemetryAsync(initialSample:false);return;}
            using Process process = Process.GetCurrentProcess();
            process.Refresh();
            telemetryLastCpuTime = process.TotalProcessorTime;
            telemetryLastSampleUtc = DateTime.UtcNow;
            telemetryLastNetworkBytes = activeBrowser?.NetworkBytesReceived ?? 0;
            telemetryNetworkSamples.Clear();
            telemetryTimer.Start();
            _ = RefreshTelemetryAsync(initialSample: true);
        }

        private void StopTelemetry()
        {
            if (UsageMiniState.Consumers==0 && ContextMenu?.IsOpen != true && (!showUsageOverlay || activeBrowser == null))
                telemetryTimer.Stop();
        }

        private void TelemetryDetailsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            showUsageOverlay = !showUsageOverlay;
            ApplyUsageOverlaySetting();
        }

        private void ApplyUsageOverlaySetting()
        {
            if(webShortcutsWidget!=null)webShortcutsWidget.TelemetryEnabled=showUsageOverlay;
            UpdateUsageOverlayVisibility();
            if (showUsageOverlay) StartTelemetry();
            else if (UsageMiniState.Consumers==0 && ContextMenu?.IsOpen != true) telemetryTimer.Stop();
            SaveWindowSettings();
        }

        private void UpdateUsageOverlayVisibility()
        {
            usageOverlayWindow?.Close();
            usageOverlayWindow = null;
            if(webShortcutsWidget!=null)webShortcutsWidget.TelemetryEnabled=showUsageOverlay;
            activeBrowser?.SetTelemetry(telemetryDetailsText.Text, showUsageOverlay);
        }

        private async void TelemetryTimer_Tick(object? sender, EventArgs e)
        {
            await RefreshTelemetryAsync(initialSample: false);
        }

        private async Task RefreshTelemetryAsync(bool initialSample)
        {
            if (telemetryUpdateRunning) return;
            telemetryUpdateRunning = true;
            try
            {
                long networkBytes = activeBrowser?.NetworkBytesReceived ?? 0;
                bool gpuRequested = UsageMiniState.NeedsGpu || ContextMenu?.IsOpen == true || (showUsageOverlay && activeBrowser != null);
                var snapshot = await Task.Run(() =>
                {
                    int[] processIds = GetProcessFamilyIds(Environment.ProcessId);
                    TimeSpan cpu = TimeSpan.Zero;
                    long memory = 0, webMemory=0;
                    bool memoryValid=true,webMemoryValid=true;
                    var privateResident=telemetryRam.Read();
                    foreach (int processId in processIds)
                    {
                        try
                        {
                            using Process process = Process.GetProcessById(processId);
                            process.Refresh();
                            cpu += process.TotalProcessorTime;
                            bool web=process.ProcessName.Equals("msedgewebview2",StringComparison.OrdinalIgnoreCase);
                            bool bd=processId==Environment.ProcessId||process.ProcessName.Equals("BlakiDeck",StringComparison.OrdinalIgnoreCase);
                            if(web||bd)
                            {
                                if(privateResident.TryGetValue(processId,out long bytes))
                                {if(web)webMemory+=bytes;else memory+=bytes;}
                                else if(web)webMemoryValid=false;else memoryValid=false;
                            }
                        }
                        catch { }
                    }
                    double? gpu = null;
                    if (gpuRequested) gpu = telemetryGpu.Read(processIds);
                    else telemetryGpu.Reset();
                    return (cpu, memory:memoryValid?(long?)memory:null, webMemory:webMemoryValid?(long?)webMemory:null, gpu, networkBytes);
                });
                if (UsageMiniState.Consumers==0 && ContextMenu?.IsOpen != true && !showUsageOverlay) return;

            DateTime sampleUtc = DateTime.UtcNow;
            double elapsedSeconds = (sampleUtc - telemetryLastSampleUtc).TotalSeconds;
            double cpuSeconds = Math.Max(0,
                (snapshot.cpu - telemetryLastCpuTime).TotalSeconds);
            double cpuPercentage = !initialSample && elapsedSeconds > 0.05
                ? cpuSeconds / (elapsedSeconds * Math.Max(1, Environment.ProcessorCount)) * 100
                : 0;

            telemetryLastCpuTime = snapshot.cpu;
            telemetryLastSampleUtc = sampleUtc;
            double networkBytesPerSecond = elapsedSeconds > 0
                ? Math.Max(0, snapshot.networkBytes - telemetryLastNetworkBytes) / elapsedSeconds
                : 0;
            telemetryLastNetworkBytes = snapshot.networkBytes;
            telemetryNetworkSamples.Enqueue(networkBytesPerSecond);
            while (telemetryNetworkSamples.Count > 5) telemetryNetworkSamples.Dequeue();
            double smoothedNetworkBytesPerSecond = telemetryNetworkSamples.Count > 0
                ? telemetryNetworkSamples.Average() : 0;
            UpdateTelemetryMemoryText(Math.Clamp(cpuPercentage, 0, 100), snapshot.gpu,
                snapshot.memory, smoothedNetworkBytesPerSecond,snapshot.webMemory);
            }
            finally
            {
                telemetryUpdateRunning = false;
            }
        }

        private void UpdateTelemetryText(
            double cpuPercentage,
            double? gpuPercentage,
            long workingSetBytes,
            double networkBytesPerSecond)
            =>UpdateTelemetryMemoryText(cpuPercentage,gpuPercentage,workingSetBytes,networkBytesPerSecond,0);
        private void UpdateTelemetryMemoryText(double cpuPercentage,double? gpuPercentage,long? privateResidentBytes,double networkBytesPerSecond,long? webPrivateResidentBytes)
        {
            string cpu = Math.Clamp(cpuPercentage, 0, 100).ToString("0.0");
            string gpu = gpuPercentage.HasValue
                ? Math.Clamp(gpuPercentage.Value, 0, 100).ToString("0.0")
                : "N/A";
            string ram = privateResidentBytes.HasValue?Math.Clamp(privateResidentBytes.Value/1048576d,0,99999).ToString("0"):"N/A";
            string web = webPrivateResidentBytes.HasValue?Math.Clamp(webPrivateResidentBytes.Value/1048576d,0,99999).ToString("0"):"N/A";
            string network = (networkBytesPerSecond / 1048576d).ToString("0.0");
            telemetryDetailsText.Text =
                $"CPU: {cpu}%    GPU: {(gpu == "N/A" ? gpu : gpu + "%")}    " +
                $"RAM: {ram} MB    NET: {network} MB/s    WEB: {web} MB";
            menuUsageView.SetMemoryValues(cpu, gpu, ram, network,web);
            UsageMiniState.PublishMemory(cpu,gpu,ram,network,web);
            if(webShortcutsWidget!=null)webShortcutsWidget.TelemetryEnabled=showUsageOverlay;
            activeBrowser?.SetTelemetry(telemetryDetailsText.Text, showUsageOverlay);
        }

        private static int[] GetProcessFamilyIds(int rootProcessId)
        {
            Dictionary<int, int> parents = new();
            IntPtr snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
            if (snapshot == new IntPtr(-1)) return new[] { rootProcessId };
            try
            {
                ProcessEntry32 entry = new() { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
                if (Process32First(snapshot, ref entry))
                {
                    // External apps launched by shortcuts are not our runtime helpers.
                    do
                    {
                        string executable = System.IO.Path.GetFileName(entry.ExeFile);
                        if (executable.Equals("BlakiDeck.exe", StringComparison.OrdinalIgnoreCase)
                            || executable.Equals("msedgewebview2.exe", StringComparison.OrdinalIgnoreCase)
                            || executable.Equals("nvidia-smi.exe", StringComparison.OrdinalIgnoreCase)
                            || executable.Equals("conhost.exe", StringComparison.OrdinalIgnoreCase))
                            parents[(int)entry.ProcessId] = (int)entry.ParentProcessId;
                    }
                    while (Process32Next(snapshot, ref entry));
                }
            }
            finally { CloseHandle(snapshot); }

            HashSet<int> family = new() { rootProcessId };
            bool changed;
            do
            {
                changed = false;
                foreach ((int processId, int parentId) in parents)
                    if (family.Contains(parentId) && family.Add(processId)) changed = true;
            } while (changed);
            return family.ToArray();
        }

        private void FitMonitorButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            CaptureFitRestoreBounds();
            if (FitCapturedLinkedCompositeToCurrentMonitorPixels()) return;
            NativeRect area=CurrentWorkPixels();
            double ratio=(isCompact?CompactCanvasHeight:ExpandedDashboardCanvasHeight)/1920d;
            int width=Math.Min(area.Right-area.Left,(int)Math.Floor((area.Bottom-area.Top)/ratio));
            int height=(int)Math.Round(width*ratio);
            ApplyGeometry(PixelBounds(area.Left,compactDockEdge==DockEdge.Top?area.Top:area.Bottom-height,width,height));
        }
        private void AlignTopButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            CaptureStartupBoundsIfNeeded();
            AlignDashboardEdge(DockEdge.Top);
        }

        private void AlignBottomButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            CaptureStartupBoundsIfNeeded();
            AlignDashboardEdge(DockEdge.Bottom);
        }

        private bool linkedMoveFromWeb;
        private double linkedMoveDeltaX, linkedMoveDeltaY;
        private void BeginLinkedWebWindowMove()
        {
            BeginLinkedWindowMove();
            linkedMoveFromWeb = true;
        }

        private void AlignDashboardEdge(DockEdge edge)
        {
            compactDockEdge = edge;
            if (activeBrowser != null)
            {
                // Dock inside the existing Web window. A plain Top assignment
                // enters the linked drag path and moves the page with the dashboard.
                ApplyGeometry(ModeBoundsPixels());
            }
            else if (GetWindowRect(windowHandle, out NativeRect current))
            {
                NativeRect area = CurrentWorkPixels();
                int height = current.Bottom - current.Top;
                ApplyGeometry(PixelBounds(current.Left, edge == DockEdge.Top ? area.Top : area.Bottom - height,
                    current.Right - current.Left, height));
            }
            MarkLocalActivity();
            UpdateWebPageLayout();
        }

        private void RestorePositionButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (fitRestoreDashboardCaptured && windowHandle != IntPtr.Zero)
            {
                ApplyCapturedFitRestoreBounds(activeBrowser);
                return;
            }
            if (!startupBoundsCaptured)
                return;

            int width=startupBounds.Right-startupBounds.Left;
            int height=(int)Math.Round(width*(isCompact?CompactCanvasHeight:ExpandedDashboardCanvasHeight)/1920d);
            NativeRect target=PixelBounds(startupBounds.Left,compactDockEdge==DockEdge.Bottom?startupBounds.Bottom-height:startupBounds.Top,width,height);
            NativeRect? webTarget=null;
            if(activeBrowser!=null && GetWindowRect(activeBrowser.GetNativeHandle(),out NativeRect web))
            {
                int webHeight=(int)Math.Round((web.Bottom-web.Top)*(double)width/(web.Right-web.Left));
                webTarget=PixelBounds(target.Left,compactDockEdge==DockEdge.Bottom?target.Bottom-webHeight:target.Top,width,webHeight);
            }
            ClampLinkedToWorkArea(ref target,ref webTarget);
            ApplyGeometry(target,webTarget);
        }

        private void CaptureFitRestoreBounds()
        {
            fitRestoreDashboardCaptured = windowHandle != IntPtr.Zero &&
                GetWindowRect(windowHandle, out fitRestoreDashboardPixels);
            fitRestoreBrowserCaptured = activeBrowser != null &&
                GetWindowRect(activeBrowser.GetNativeHandle(), out fitRestoreBrowserPixels);
            InteractionDiagnostics.Log(
                $"FitRestore capture dashboard=" +
                $"{(fitRestoreDashboardCaptured ? FormatNativeRect(fitRestoreDashboardPixels) : "UNMEASURED")} " +
                $"browser={(fitRestoreBrowserCaptured ? FormatNativeRect(fitRestoreBrowserPixels) : "NOT RENDERED")}");
        }

        private bool FitCapturedLinkedCompositeToCurrentMonitorPixels()
        {
            if (activeBrowser == null || !fitRestoreDashboardCaptured ||
                !fitRestoreBrowserCaptured || windowHandle == IntPtr.Zero) return false;

            IntPtr monitor = MonitorFromWindow(activeBrowser.GetNativeHandle(), MonitorDefaultToNearest);
            MonitorInfo info = new() { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return false;

            NativeRect fittedBrowser = info.WorkArea;
            ApplyGeometry(ModeBoundsPixels(fittedBrowser), fittedBrowser);
            return true;
        }

        private void ApplyCapturedFitRestoreBounds(FullscreenBrowserWindow? restoredBrowser)
        {
            NativeRect d=fitRestoreDashboardPixels;
            NativeRect? b=restoredBrowser!=null && fitRestoreBrowserCaptured && ReferenceEquals(restoredBrowser,activeBrowser)?fitRestoreBrowserPixels:null;
            int height=(int)Math.Round((d.Right-d.Left)*(isCompact?CompactCanvasHeight:ExpandedDashboardCanvasHeight)/1920d);
            d=PixelBounds(d.Left,compactDockEdge==DockEdge.Bottom?d.Bottom-height:d.Top,d.Right-d.Left,height);
            ClampLinkedToWorkArea(ref d,ref b);ApplyGeometry(d,b);
        }
        private void CaptureStartupBoundsIfNeeded()
        {
            if (startupBoundsCaptured)
                return;

            startupBoundsCaptured = GetWindowRect(windowHandle,out startupBounds);
        }

        private void ChangeWallpaperMenuItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            OpenFileDialog dialog = new()
            {
                Title = "Choose wallpaper for this monitor",
                Filter =
                    "Image files|*.jpg;*.jpeg;*.png;*.bmp;*.jfif|" +
                    "All files|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog(this) != true)
                return;

            try
            {
                SetWallpaperForCurrentMonitor(dialog.FileName);
            }
            catch (Exception exception)
            {
                AppMessage.Show(
                    this,
                    $"Windows could not change the wallpaper.\n\n{exception.Message}",
                    "Wallpaper error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void SetWallpaperForCurrentMonitor(string wallpaperPath)
        {
            IntPtr currentMonitor = MonitorFromWindow(
                windowHandle,
                MonitorDefaultToNearest);
            MonitorInfo currentMonitorInfo = new()
            {
                Size = Marshal.SizeOf<MonitorInfo>()
            };

            if (currentMonitor == IntPtr.Zero ||
                !GetMonitorInfo(currentMonitor, ref currentMonitorInfo))
            {
                throw new InvalidOperationException(
                    "The monitor containing the dashboard could not be detected.");
            }

            IDesktopWallpaper? desktopWallpaper = null;

            try
            {
                desktopWallpaper = (IDesktopWallpaper)new DesktopWallpaperClass();
                desktopWallpaper.GetMonitorDevicePathCount(out uint monitorCount);
                GetWindowRect(windowHandle, out NativeRect windowRect);
                double windowCenterX = (windowRect.Left + windowRect.Right) / 2.0;
                double windowCenterY = (windowRect.Top + windowRect.Bottom) / 2.0;
                string? selectedMonitorId = null;
                string? nearestMonitorId = null;
                double nearestDistance = double.MaxValue;

                for (uint index = 0; index < monitorCount; index++)
                {
                    desktopWallpaper.GetMonitorDevicePathAt(
                        index,
                        out string monitorId);
                    desktopWallpaper.GetMonitorRECT(
                        monitorId,
                        out NativeRect wallpaperMonitorRect);

                    if (AreSameRect(
                            currentMonitorInfo.Monitor,
                            wallpaperMonitorRect) ||
                        ContainsPoint(
                            wallpaperMonitorRect,
                            windowCenterX,
                            windowCenterY))
                    {
                        selectedMonitorId = monitorId;
                        break;
                    }

                    double monitorCenterX =
                        (wallpaperMonitorRect.Left + wallpaperMonitorRect.Right) / 2.0;
                    double monitorCenterY =
                        (wallpaperMonitorRect.Top + wallpaperMonitorRect.Bottom) / 2.0;
                    double distance =
                        Math.Pow(monitorCenterX - windowCenterX, 2) +
                        Math.Pow(monitorCenterY - windowCenterY, 2);

                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearestMonitorId = monitorId;
                    }
                }

                selectedMonitorId ??= nearestMonitorId;

                if (string.IsNullOrWhiteSpace(selectedMonitorId))
                    throw new InvalidOperationException("No Windows monitor ID was found.");

                int result = desktopWallpaper.SetWallpaper(
                    selectedMonitorId,
                    Path.GetFullPath(wallpaperPath));
                Marshal.ThrowExceptionForHR(result);
            }
            finally
            {
                if (desktopWallpaper != null &&
                    Marshal.IsComObject(desktopWallpaper))
                {
                    Marshal.FinalReleaseComObject(desktopWallpaper);
                }
            }
        }

        private static bool AreSameRect(NativeRect first, NativeRect second)
        {
            return first.Left == second.Left &&
                   first.Top == second.Top &&
                   first.Right == second.Right &&
                   first.Bottom == second.Bottom;
        }

        private static bool ContainsPoint(
            NativeRect rectangle,
            double x,
            double y)
        {
            return x >= rectangle.Left &&
                   x < rectangle.Right &&
                   y >= rectangle.Top &&
                   y < rectangle.Bottom;
        }

        private void MusicServiceMenuItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem &&
                menuItem.Tag is string serviceName &&
                Enum.TryParse(
                    serviceName,
                    ignoreCase: true,
                    out MusicService service))
            {
                SetMusicService(service);
                SaveWindowSettings();
            }
        }

        private void SetMusicService(MusicService service)
        {
            if(!applyingDashboardProfile)CaptureActiveVuSensitivity();
            selectedMusicService = service;
            spotifyWidget.SetMusicService(service);
            double sensitivity=selectedPlayerSettings.SensitivityFor(service==MusicService.Local);
            musicConsole?.SetVuSensitivity(sensitivity);analogVuCard?.RefreshSettings(sensitivity);
            musicConsole?.ActivateSelectedPlaylist();
            RefreshLocalAvailability();
            localServiceMenuItem.IsChecked=service==MusicService.Local;
            visualizerAudio.SetTargetProcessNames(spotifyWidget.SelectedService==MusicService.Local?[System.Diagnostics.Process.GetCurrentProcess().ProcessName]:spotifyWidget.SelectedProcessNames);

            spotifyServiceMenuItem.IsChecked =
                service == MusicService.Spotify;
            tidalServiceMenuItem.IsChecked =
                service == MusicService.Tidal;
            deezerServiceMenuItem.IsChecked =
                service == MusicService.Deezer;
            appleMusicServiceMenuItem.IsChecked =
                service == MusicService.AppleMusic;
            amazonMusicServiceMenuItem.IsChecked =
                service == MusicService.AmazonMusic;
            musicBeeServiceMenuItem.IsChecked =
                service == MusicService.MusicBee;
            UpdateDefaultSourceIcon();
        }

        private async void CoordinatesTextBox_KeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            e.Handled = true;
            await ApplyWeatherLocationAsync(useCoordinates: true);
        }

        private async void ApplyCoordinatesButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            e.Handled = true;
            await ApplyWeatherLocationAsync(useCoordinates: true);
        }

        private async void CityTextBox_KeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            e.Handled = true;
            await ApplyWeatherLocationAsync(useCoordinates: false);
        }

        private async void ApplyCityButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            e.Handled = true;
            await ApplyWeatherLocationAsync(useCoordinates: false);
        }

        private void QuickLaunchBar_CompactModeRequested(
            object? sender,
            EventArgs e)
        {
            bool compact = !isCompact;
            if (!compact) SetAutoDashHidden(false, immediate: true);
            activeBrowser?.SetDashboardExpanded(!compact);
            SetCompactMode(compact, () =>
            {
                if (!isCompact && activeBrowser != null)
                {
                    Activate();
                    ApplyAlwaysOnTop();
                }
            });
        }

        private void ModuleMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem { Tag: string tag })
                return;

            string[] parts = tag.Split('|');
            if (parts.Length != 2 ||
                !int.TryParse(parts[0], out int slot) ||
                slot is < 0 or > 2 ||
                !Enum.TryParse(parts[1], true, out ModuleKind selected))
                return;

            wideShortcutsSpan = 0;

            int existingSlot = Array.IndexOf(selectedModules, selected);
            if (existingSlot >= 0 && existingSlot != slot)
                (selectedModules[existingSlot], selectedModules[slot]) =
                    (selectedModules[slot], selectedModules[existingSlot]);
            else
                selectedModules[slot] = selected;

            ApplySelectedModules();
            UpdateModuleMenuChecks();
            _=Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>CommonMetal.ApplyResourceMode(ResourcePolicy.Mode)));
            ReleaseUnusedModuleViews();
            SaveWindowSettings();
        }

        private void ChooseModulesMenuItem_Click(object sender, RoutedEventArgs e)
        {
            InteractionDiagnostics.Log("ContextMenu Layout activated");
            ModuleKind[] initialModules = (ModuleKind[])selectedModules.Clone();
            int initialWideSpan = wideShortcutsSpan;
            bool initialPlayerMode=musicConsoleEnabled;var initialPlayerSettings=CaptureTabPlayer();
            ModuleSelectionDialog dialog = new(
                selectedModules.Select(module => module.ToString()).ToArray(),
                wideShortcutsSpan) { Owner = this };
            dialog.ListTextSize=layoutListTextSize;
            dialog.FactoryTabIndex=activeDashboardLayoutTab;
            bool initialTitles=showModuleTitles;
            dialog.ShowModuleTitles=showModuleTitles;
            dialog.ModuleTitlesPreviewChanged+=value=>{showModuleTitles=value;showModuleTitlesMenuItem.IsChecked=value;ApplyModuleTitleVisibility();};
            dialog.SetTabContext(quickLaunchBar.LayoutTabPresentation(activeDashboardLayoutTab),musicConsoleEnabled,MusicFinish.IsLight,MusicFinish.Name);
            void ApplyPreview(IReadOnlyList<string> modules, int span)
            {
                for (int slot = 0; slot < selectedModules.Length; slot++)
                    if (Enum.TryParse(modules[slot], true, out ModuleKind selected))
                        selectedModules[slot] = selected;
                wideShortcutsSpan = span;
                ApplySelectedModules();
            }
            dialog.PreviewChanged += ApplyPreview;
            dialog.LayoutPreviewChanged+=(player,finish)=>
            {
                var settings=PlayerSkinLights.Switch(CaptureTabPlayer(),finish);
                musicConsoleEnabled=player;musicConsoleMenuItem.IsChecked=player;
                SelectTabConsole(settings);UpdateMusicConsoleVisibility();ApplySelectedModules();
            };
            if (dialog.ShowDialog() != true)
            {
                showModuleTitles=initialTitles;showModuleTitlesMenuItem.IsChecked=initialTitles;ApplyModuleTitleVisibility();
                musicConsoleEnabled=initialPlayerMode;musicConsoleMenuItem.IsChecked=initialPlayerMode;
                SelectTabConsole(initialPlayerSettings);UpdateMusicConsoleVisibility();
                Array.Copy(initialModules, selectedModules, selectedModules.Length);
                wideShortcutsSpan = initialWideSpan;
                ApplySelectedModules();
                return;
            }

            layoutListTextSize=dialog.ListTextSize;
            musicConsoleEnabled=dialog.PlayerLayout;
            musicConsoleMenuItem.IsChecked=musicConsoleEnabled;
            ApplyPreview(dialog.SelectedModules, dialog.WideShortcutsSpan);
            UpdateMusicConsoleVisibility();
            ApplySelectedModules();
            UpdateModuleMenuChecks();
            SaveWindowSettings();
        }

        private void WideShortcutsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem { Tag: string tag } item ||
                !int.TryParse(tag, out int requestedSpan) || requestedSpan is not (2 or 3)) return;

            wideShortcutsSpan = requestedSpan;
            ApplySelectedModules();
            UpdateModuleMenuChecks();
            SaveWindowSettings();
        }

        private void UpdateModuleMenuChecks()
        {
            if (ContextMenu == null) return;
            foreach (object item in ContextMenu.Items)
                UpdateModuleMenuChecks(item as MenuItem);
        }

        private void UpdateModuleMenuChecks(MenuItem? item)
        {
            if (item == null) return;
            if (item.Tag is string tag)
            {
                string[] parts = tag.Split('|');
                if (parts.Length == 2 && int.TryParse(parts[0], out int slot) &&
                    slot is >= 0 and < 3 &&
                    Enum.TryParse(parts[1], true, out ModuleKind kind))
                {
                    item.IsCheckable = true;
                    item.IsChecked = wideShortcutsSpan == 0 && selectedModules[slot] == kind;
                    bool usedElsewhere = selectedModules
                        .Where((_, index) => index != slot)
                        .Contains(kind);
                    item.IsEnabled = item.IsChecked || !usedElsewhere;
                    item.Opacity = usedElsewhere && !item.IsChecked ? 0.30 : 1.0;
                    item.Foreground = usedElsewhere && !item.IsChecked
                        ? new SolidColorBrush(Color.FromRgb(105, 105, 105))
                        : new SolidColorBrush(Color.FromRgb(244, 244, 244));
                }
            }
            foreach (object child in item.Items)
                UpdateModuleMenuChecks(child as MenuItem);
        }

        private ShortcutsWidget GetTabShortcuts(string kind)
        {
            var key=(activeDashboardLayoutTab,kind);
            if(!tabShortcutWidgets.TryGetValue(key,out var widget))
                tabShortcutWidgets[key]=widget=new ShortcutsWidget(TabShortcutStorage.Key(key.Item1,kind),kind=="wide"?12:4);
            return widget;
        }

        private static ModuleKind MigrateRetiredModule(ModuleKind module)=>module is ModuleKind.Visualizer or ModuleKind.Visualizer2 or ModuleKind.Speaker or ModuleKind.Speaker2?ModuleKind.None:module;
        private void RefreshLocalAvailability()
        {
            bool playlist=selectedModules.Select((kind,index)=>(kind,index)).Any(x=>x.kind==ModuleKind.Playlist&&!ModuleLayoutPlanner.IsBlockedByWide(x.index,wideShortcutsSpan));
            bool player=musicConsoleEnabled||selectedModules.Select((kind,index)=>(kind,index)).Any(x=>x.kind==ModuleKind.MusicPlayerCard&&!ModuleLayoutPlanner.IsBlockedByWide(x.index,wideShortcutsSpan));
            spotifyWidget.LocalAvailable=musicConsoleEnabled||playlist&&player;
            localServiceMenuItem.Header=musicConsoleEnabled?"Local player":"Local player — requires Playlist";
            playlistCard?.SetActive(playlist&&player&&selectedMusicService==MusicService.Local);
        }
        internal SystemMonitorWidget EnsureSystemMonitor()=>systemMonitorWidget??=new SystemMonitorWidget();
        private void ApplySelectedModules()
        {
            for(int i=0;i<selectedModules.Length;i++)selectedModules[i]=MigrateRetiredModule(selectedModules[i]);
            bool systemWide=selectedModules[0] is ModuleKind.SystemWide or ModuleKind.SystemFull&&wideShortcutsSpan==0;
            int systemSpan=selectedModules[0]==ModuleKind.SystemFull?3:2;
            systemMonitorWidget?.SetWideLayout(false);
            // Replace the retired player in its existing slot; remove a duplicate trial card.
            int retiredPlayerSlot=Array.IndexOf(selectedModules,ModuleKind.Music);
            if(retiredPlayerSlot>=0){for(int slot=0;slot<selectedModules.Length;slot++)if(selectedModules[slot]==ModuleKind.MusicPlayerCard)selectedModules[slot]=ModuleKind.None;selectedModules[retiredPlayerSlot]=ModuleKind.MusicPlayerCard;}
            ContentControl[] hosts =
                { leftModuleHost, centerModuleHost, rightModuleHost };

            foreach (ContentControl host in hosts)
                host.Content = null;

            wideShortcutsHost.Content = null;
            wideShortcutsHost.Visibility = Visibility.Collapsed;

            for (int i = 0; i < hosts.Length; i++)
            {
                if (ModuleLayoutPlanner.IsBlockedByWide(i, systemWide?systemSpan:wideShortcutsSpan)) continue;
                hosts[i].Content = selectedModules[i] switch
                {
                    ModuleKind.Obs => obsWidget,
                    ModuleKind.Weather => weatherWidget,
                    ModuleKind.System => EnsureSystemMonitor(),
                    ModuleKind.Shortcuts => shortcutsWidget = GetTabShortcuts("regular"),
                    ModuleKind.Shortcuts2 => shortcutsWidget2 = GetTabShortcuts("second"),
                    ModuleKind.Shortcuts3 => GetThirdShortcuts(),
                    ModuleKind.Hue => hueWidget ??= new HueWidget(),
                    ModuleKind.Airthings => airthingsWidget ??= new AirthingsWidget(),
                    ModuleKind.AnalogVuCard => analogVuCard ??= CreateAnalogVuCard(),
                    ModuleKind.MusicPlayerCard => musicPlayerCard ??= new MusicPlayerCard(spotifyWidget,visualizerAudio),
                    ModuleKind.MacroLibrary => macroLibraryCard ??= new MacroLibraryCard(),
                    ModuleKind.HudVisualizer => hudVisualizerCard ??= CreateHudVisualizer(),
                    ModuleKind.Playlist => playlistCard ??= new PlaylistCard(),
                    ModuleKind.WebShortcuts => webShortcutsWidget ??= CreateWebShortcutsWidget(),
                    _ => null
                };
            }
            if (wideShortcutsSpan is 2 or 3)
            {
                wideShortcutsWidget = GetTabShortcuts("wide");
                wideShortcutsWidget.SetGridColumns(wideShortcutsSpan == 2 ? 8 : 12);
                wideShortcutsHost.Content = wideShortcutsWidget;
                Grid.SetColumnSpan(wideShortcutsHost, wideShortcutsSpan == 2 ? 3 : 5);
                wideShortcutsHost.Visibility = Visibility.Visible;
            }
            if(systemWide){var source=EnsureSystemMonitor();var monitor=systemSpan==3?(fullSystemMonitor??=new SystemMonitorWidget(source,"full")):(wideSystemMonitor??=new SystemMonitorWidget(source,"wide"));monitor.SetWideLayout(true);wideShortcutsHost.Content=monitor;Grid.SetColumnSpan(wideShortcutsHost,systemSpan==3?5:3);wideShortcutsHost.Visibility=Visibility.Visible;}
            playlistCard?.ApplyAppearance(selectedPlayerSettings.Playlist??new());
            analogVuCard?.RefreshSettings(selectedPlayerSettings.SensitivityFor(selectedMusicService==MusicService.Local));
            musicPlayerCard?.SetLighting((Color)ColorConverter.ConvertFromString(musicLightColor),musicLightIntensity);
            foreach(var card in new[]{musicPlayerCard}.OfType<MusicPlayerCard>()){card.ContextMenu=ContextMenu;card.SetLighting((Color)ColorConverter.ConvertFromString(musicLightColor),musicLightIntensity);card.ApplyTypography(selectedPlayerSettings.Typography??new());}
            RefreshLocalAvailability();
            UpdateBackgroundModuleActivity();
            ApplyWideShortcutsTitleLayout();
            ApplyShortcutNameVisibility();
            ApplyModuleTitleVisibility();
            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() =>
                {
                    ApplyCurrentTextColorToVisualTree();
                    ApplyModuleTitleVisibility();
                }));
        }

        private string webShortcutsViewMode="List";
        private double webShortcutsIconSize=48;
        private WebShortcutsWidget CreateWebShortcutsWidget()
        {
            WebShortcutsWidget widget = new() { ViewMode=webShortcutsViewMode,IconSize=webShortcutsIconSize };
            widget.ClearWebCacheRequested=()=>activeBrowser!=null?activeBrowser.ClearWebCacheAsync():WebCacheMaintenance.ClearDiskCacheAsync(this);
            widget.SetBrowserOpen(activeBrowser!=null);
            widget.BrowserToggleRequested+=()=>{
                if(activeBrowser!=null){activeBrowser.Close();return;}
                var url=lastBrowserUrl??"about:blank";
                OpenWebShortcut(widget,widget.FindByUrl(url)??new WebShortcutsWidget.WebShortcut("Last browser page",url));
            };
            widget.SetListSize(webListRowHeight);widget.ApplyListAppearance(webListAppearance);widget.TelemetryEnabled=showUsageOverlay;
            widget.AppearanceChanged+=value=>{webListAppearance=value;webShortcutsWidget?.ApplyListAppearance(value);SaveWindowSettings();};
            widget.TelemetryChanged+=value=>{showUsageOverlay=value;ApplyUsageOverlaySetting();};
            widget.OpenRequested += item => OpenWebShortcut(widget, item);
            widget.ViewModeChanged += value => {webShortcutsViewMode=value;webShortcutsImagesOnly=value=="Thumbs";webImagesOnlyMenuItem.IsChecked=webShortcutsImagesOnly;SaveWindowSettings();};
            widget.IconSizeChanged+=value=>{webShortcutsIconSize=value;SaveWindowSettings();};
            InitializeWebPreviewWidget(widget);
            return widget;
        }

        private void WebImagesOnlyMenuItem_Click(object sender,RoutedEventArgs e)
        {
            webShortcutsImagesOnly=webImagesOnlyMenuItem.IsChecked;
            webShortcutsViewMode=webShortcutsImagesOnly?"Thumbs":"List";
            if(webShortcutsWidget!=null){webShortcutsWidget.ImagesOnly=webShortcutsImagesOnly;webShortcutsWidget.SetListSize(webListRowHeight);webShortcutsWidget.ApplyListAppearance(webListAppearance);}
            SaveWindowSettings();
        }

        private void OpenWebShortcut(WebShortcutsWidget widget, WebShortcutsWidget.WebShortcut item)
        {
            activeBrowser?.Close();
            activeWebShortcutUrl = item.Url;
            lastBrowserUrl=widget.GetOpenUrl(item);
            GetWindowRect(windowHandle,out NativeRect openedFrom);
            NativeRect work=CurrentWorkPixels();
            NativeRect initialWeb=InitialBrowserBounds(openedFrom,work,pendingBrowserPixels);
            pendingBrowserPixels=null;
            void ShowBrowser()
            {
                FullscreenBrowserWindow browser = new(widget.GetOpenUrl(item), this, null, null,
                    url => {widget.SetLastUrl(item, url);lastBrowserUrl=url;SaveWindowSettings();}, MarkLocalActivity,
                    BeginLinkedWebWindowMove, MoveLinkedCompositeBy,
                    CompleteLinkedWindowMove, OpenMainMenuAtScreenPoint);
                ConnectWebPreview(widget,item,browser);
                browser.InitialPhysicalBounds=new Int32Rect(initialWeb.Left,initialWeb.Top,initialWeb.Right-initialWeb.Left,initialWeb.Bottom-initialWeb.Top);
                browser.SavePageRequested += (url,title) => widget.SavePage(url,title);
                browser.TelemetryChanged+=value=>{showUsageOverlay=value;ApplyUsageOverlaySetting();};
                activeBrowser = browser;
                browser.SetResourceMode(ResourcePolicy.Mode);
                browser.SetDashboardExpanded(!isCompact);
                widget.SetBrowserOpen(true);webShortcutsWidget?.SetBrowserOpen(true);
                browser.AutoHideBarsEnabled = autoDashHideEnabled;
                browser.AutoHideBarsChanged += SetWebBarsAutoHide;
                webPageIdle = false;
                browser.SetTelemetry(telemetryDetailsText.Text, showUsageOverlay);
                Color browserBase = (Color)ColorConverter.ConvertFromString(baseColor);
                browser.SetDashboardAppearance(browserBase, 1.0);
                browser.SetUiOpacity(uiOpacity);
                browser.SetAutoDimAmount(elementsAutoDimmed
                    ? 1d - autoDimOpacityPercent / 100d
                    : 0d);
                browser.SetTextOpacity(textColorOpacity / 100d);
                browser.SetAlwaysOnTop(alwaysOnTopEnabled);
                if (showUsageOverlay) StartTelemetry();
                browser.Closed += (_, _) =>
                {
                    if (!ReferenceEquals(activeBrowser, browser)) return;
                    SetAutoDashHidden(false, immediate: true);
                    activeBrowser = null;
                    widget.SetBrowserOpen(false);webShortcutsWidget?.SetBrowserOpen(false);
                    if (!applicationClosing)
                        activeWebShortcutUrl = null;
                    ApplyAlwaysOnTop();
                    if (ContextMenu?.IsOpen != true) StopTelemetry();
                    if (isCompact && !applicationClosing) SetCompactMode(false);
                };
                browser.Show();
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
                    // Both surfaces must use the same physical target. The newly
                    // shown browser can still have a transient DPI-scaled HWND
                    // rectangle until WPF finishes its first layout.
                    new Action(() => ApplyGeometry(ModeBoundsPixels(initialWeb),initialWeb)));
                ApplyAlwaysOnTop();
            }

            if (!isCompact)
                SetCompactMode(true, ShowBrowser);
            else
                ShowBrowser();
        }

        private void MoveLinkedCompositeBy(double deltaX, double deltaY)
        {
            if (Math.Abs(deltaX) < 0.01 && Math.Abs(deltaY) < 0.01) return;
            if (linkedMoveCaptured && activeBrowser != null)
            {
                // Every move uses the original physical rectangles. Never feed
                // WPF's intermediate DPI-scaled size back into the next move.
                linkedMoveDeltaX += deltaX;
                linkedMoveDeltaY += deltaY;
                NativeRect Offset(NativeRect start) => PixelBounds(
                    start.Left + (int)Math.Round(linkedMoveDeltaX),
                    start.Top + (int)Math.Round(linkedMoveDeltaY),
                    start.Right-start.Left, start.Bottom-start.Top);
                ApplyGeometry(Offset(linkedDashboardStartPixels), Offset(linkedBrowserStartPixels));
                return;
            }
            if(GetWindowRect(windowHandle,out NativeRect current))
                SetWindowPos(windowHandle,IntPtr.Zero,current.Left+(int)Math.Round(deltaX),current.Top+(int)Math.Round(deltaY),0,0,SwpNoSize|SwpNoActivate);
        }

        private void OpenMainMenuAtScreenPoint(Point screenPoint)
        {
            InteractionDiagnostics.Log($"ContextMenu main-open screen={screenPoint} right={Mouse.RightButton}");
            if (ContextMenu == null) return;
            var anchor=dashboardViewbox;
            var local=anchor.PointFromScreen(screenPoint);
            ContextMenu.PlacementTarget = anchor;
            ContextMenu.CustomPopupPlacementCallback=(size,_,_)=>new[]{new CustomPopupPlacement(new Point(Math.Clamp(local.X,0,Math.Max(0,anchor.ActualWidth-size.Width)),Math.Clamp(local.Y,0,Math.Max(0,anchor.ActualHeight-size.Height))),PopupPrimaryAxis.None)};
            ContextMenu.Placement = PlacementMode.Custom;
            ContextMenu.HorizontalOffset = 0;
            ContextMenu.VerticalOffset = 0;
            ContextMenu.IsOpen = true;
        }

        private void ApplyCurrentTextColorToVisualTree()
        {
            try
            {
                Color parsed = (Color)ColorConverter.ConvertFromString(textColor);
                foreach(var host in new[]{leftModuleHost,centerModuleHost,rightModuleHost,wideShortcutsHost})
                    ApplyTextColorToVisualTree(host, parsed);
                musicPlayerCard?.SyncSymbolColor();
            }
            catch
            {
                // A module must remain usable even if a saved color is invalid.
            }
        }

        private void ShowShortcutNamesMenuItem_Click(object sender, RoutedEventArgs e)
        {
            showShortcutNames = showShortcutNamesMenuItem.IsChecked;
            ApplyShortcutNameVisibility();
            SaveWindowSettings();
        }

        private void ShowModuleTitlesMenuItem_Click(object sender, RoutedEventArgs e)
        {
            showModuleTitles = showModuleTitlesMenuItem.IsChecked;
            ApplyModuleTitleVisibility();
            SaveWindowSettings();
        }


        private void ApplyModuleTitleVisibility()
        {
            ApplyModuleTitleVisibility(dashboardCanvas);
            ApplyWideShortcutsTitleLayout();
        }

        private void ApplyWideShortcutsTitleLayout()
        {
            wideShortcutsWidget?.SetModuleTitleLayout(
                showModuleTitles,
                collapseHiddenTitleSpace: wideShortcutsSpan == 3);

            wideShortcutsHost.Height = double.NaN;
            wideShortcutsHost.VerticalAlignment = VerticalAlignment.Stretch;

            double targetCanvasHeight = ExpandedDashboardCanvasHeight;
            if (isCompact)
            {
                expandedHeight = expandedWidth * targetCanvasHeight / 1920.0;
                return;
            }

            if (Math.Abs(dashboardCanvas.Height - targetCanvasHeight) < 0.1) return;

            if(musicConsoleEnabled&&windowHandle!=IntPtr.Zero&&GetWindowRect(windowHandle,out NativeRect panelBounds))
            {
                int physicalWidth=panelBounds.Right-panelBounds.Left;
                int physicalHeight=(int)Math.Round(physicalWidth*targetCanvasHeight/1920);
                dashboardCanvas.Height=targetCanvasHeight;
                ApplyGeometry(PixelBounds(panelBounds.Left,panelBounds.Bottom-physicalHeight,physicalWidth,physicalHeight));
                expandedWidth=Width;expandedHeight=physicalHeight/(GetDpiForWindow(windowHandle)/96d);
                return;
            }
            // During startup WPF can still report the XAML design ActualHeight
            // after the persisted Height property has already been restored.
            // Anchoring to that stale value shifts the whole window downward by
            // exactly the later height correction. Prefer the authoritative
            // explicit Height and fall back to ActualHeight only for Auto/invalid.
            double currentHeight = double.IsFinite(Height) && Height > 0
                ? Height
                : ActualHeight;
            double bottom = Top + currentHeight;
            dashboardCanvas.Height = targetCanvasHeight;
            double targetWindowHeight = Width / DashboardAspectRatio;
            Height = targetWindowHeight;
            Top = bottom - targetWindowHeight;
            expandedWidth = Width;
            expandedHeight = targetWindowHeight;
        }

        private void ApplyModuleTitleVisibility(DependencyObject root)
        {
            if (root is FrameworkElement { Tag: "ModuleTitle" } title)
                {
                if(title is OpticallyCenteredText heading)ModuleHeadingTypography.Apply(heading);
                title.Visibility = showModuleTitles ? Visibility.Visible : Visibility.Collapsed;
                }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                ApplyModuleTitleVisibility(VisualTreeHelper.GetChild(root, i));
        }

        private void ApplyShortcutNameVisibility()
        {
            quickLaunchBar.ShowShortcutNames = showShortcutNames;
            if (shortcutsWidget != null) shortcutsWidget.ShowShortcutNames = showShortcutNames;
            if (shortcutsWidget2 != null) shortcutsWidget2.ShowShortcutNames = showShortcutNames;
            ApplyThirdShortcutNameVisibility();
            if (wideShortcutsWidget != null) wideShortcutsWidget.ShowShortcutNames = showShortcutNames;
        }

        private void SystemMonitorMenu_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            PopulateSystemMonitorSensorMenu();
        }

        private void PopulateSystemMonitorSensorMenu(SystemMonitorWidget? target=null)
        {
            var monitor=target??(wideShortcutsHost.Content as SystemMonitorWidget)??EnsureSystemMonitor();
            ItemsControl sensorItems = sensorOptions ?? (ItemsControl)sensorsMenuItem;
            sensorItems.Items.Clear();
            var support=new MenuItem{Header="Sensor support…",Icon=MonitorMenuIcon("\uE946")};
            support.Click+=(_,_)=>monitor.OpenSensorSupport();
            sensorItems.Items.Add(support);
            sensorItems.Items.Add(new Separator());
            var indices=monitor.GridLayout?.Cells.Select(c=>c.Slot).ToArray()??Enumerable.Range(0,monitor.SlotCapacity).ToArray();
            int capacity=indices.Length;
            int activeCount=monitor.ActiveSlots.Count(s=>s.IsActive&&!s.IsShortcut);
            bool available=indices.Select(i=>monitor.Slots[i]).Any(s=>!s.IsActive||s.Shortcut?.IsEmpty==true)||monitor.FindFreeSensorCell()!=null;
            sensorsCountText.Text = $"{activeCount}/{capacity}";

            foreach (SystemMonitorWidget.MetricKind kind in
                     SystemMonitorWidget.SelectableMetrics)
            {
                int activeSlot = indices
                    .FirstOrDefault(index => monitor.IsSlotActive(index) && !monitor.Slots[index].IsShortcut &&
                                             SystemMonitorWidget.NormalizeMetric(monitor.GetSlotMetric(index)) == kind, -1);
                bool selected = activeSlot >= 0;
                MenuItem item = new()
                {
                    Header = SystemMonitorWidget.GetMetricLabel(kind),
                    IsCheckable = true,
                    IsChecked = selected,
                    StaysOpenOnClick = true,
                    IsEnabled = selected || available,
                    Opacity = selected || available ? 1.0 : 0.35
                };
                item.Click += (_, _) =>
                {
                    int selectedSlot = indices
                        .FirstOrDefault(index => monitor.IsSlotActive(index) && !monitor.Slots[index].IsShortcut &&
                                                 SystemMonitorWidget.NormalizeMetric(monitor.GetSlotMetric(index)) == kind, -1);
                    if (selectedSlot >= 0)
                    {
                        monitor.SetSlotActive(selectedSlot, false);
                    }
                    else
                    {
                        int freeSlot = indices
                            .FirstOrDefault(index => !monitor.IsSlotActive(index)||monitor.Slots[index].Shortcut?.IsEmpty==true, -1);
                        if (freeSlot >= 0)
                        {
                            monitor.SetSlotMetric(freeSlot, kind);
                            monitor.SetSlotActive(freeSlot, true);
                            monitor.FitNewStatusCell(freeSlot,kind);
                        }
                        else monitor.AddSensorInFreeSpace(kind);
                    }
                    PopulateSystemMonitorSensorMenu(monitor);
                };
                SystemMonitorWidget.AddGroupedSensor(sensorItems,kind,item);
            }
            sensorItems.Items.Add(new Separator());
            sensorItems.Items.Add(monitor.LayoutOptions());
            MenuItem resetItem = new() { Header = "Reset", Icon = MonitorMenuIcon("\uE777") };
            resetItem.Click += (_, _) =>
            {
                monitor.ResetLayout();
                PopulateSystemMonitorSensorMenu(monitor);
            };

            sensorItems.Items.Add(resetItem);
        }

        internal static TextBlock MonitorMenuIcon(string glyph)=>new(){Text=glyph,FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=18,VerticalAlignment=VerticalAlignment.Center};

        private void ResetSystemMonitorLayout_Click(object sender, RoutedEventArgs e)
        {
            systemMonitorWidget ??= new SystemMonitorWidget();
            systemMonitorWidget.ResetLayout();
        }

        private void ApplyStartupCompactState(bool compact)
        {
            isCompact = compact;
            if (!compact)
            {
                quickLaunchBar.SetCompactMode(false);
                return;
            }

            widgetsGrid.BeginAnimation(OpacityProperty, null);
            widgetsGrid.Opacity = 0;
            widgetsGrid.Visibility = Visibility.Collapsed;
            widgetsRow.Height = new GridLength(0);
            moduleGapRow.Height = new GridLength(0);
            dashboardCanvas.Height = CompactCanvasHeight;
            MinHeight = 66;

            ApplyGeometry(ModeBoundsPixels());
            quickLaunchBar.SetCompactMode(true);
        }

        private void SetCompactMode(bool compact, Action? completed = null)
        {
            long version=++compactTransitionVersion;
            widgetsGrid.BeginAnimation(OpacityProperty,null);
            if(compact==isCompact) { completed?.Invoke(); return; }
            if(compact) { expandedWidth=ActualWidth; expandedHeight=ActualHeight; }
            isCompact=compact;
            void Commit()
            {
                if(version!=compactTransitionVersion) return;
                using (Dispatcher.DisableProcessing())
                {
                    // Capture the native anchor before changing WPF layout. A
                    // layout callback must not become the next monitor/size anchor.
                    NativeRect targetBounds=ModeBoundsPixels();
                    widgetsGrid.BeginAnimation(OpacityProperty,null);
                    widgetsGrid.Opacity=compact?0:1;
                    widgetsGrid.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
                    widgetsRow.Height=compact?new GridLength(0):new GridLength(1,GridUnitType.Star);
                    bool hiddenMusicBar=!compact&&musicConsoleEnabled&&musicTopBarHidden;
                    quickLaunchBar.Visibility=hiddenMusicBar?Visibility.Collapsed:Visibility.Visible;
                    musicTopBarRow.Height=new GridLength(hiddenMusicBar?0:104);
                    moduleGapRow.Height=new GridLength(compact||hiddenMusicBar?0:9);
                    dashboardCanvas.Height=compact?CompactCanvasHeight:ExpandedDashboardCanvasHeight;
                    UpdateBackgroundModuleActivity();
                    ApplyGeometry(targetBounds);
                }
                // UpdateLayout also raises UI Automation events. Their COM
                // callbacks need dispatcher processing, especially on first use.
                UpdateLayout();
                ApplyAutoDimOpacity();
                completed?.Invoke();
            }
            quickLaunchBar.SetCompactMode(compact);
            // A single layout/placement transaction avoids a faded full-size
            // window followed by a second resize on the compact button path.
            Commit();
        }
        private void AnimateWindowBounds(
            double left,
            double top,
            double width,
            double height,
            Action completed)
        {
            TimeSpan duration = TimeSpan.FromMilliseconds(260);
            DoubleAnimation heightAnimation = new(height, duration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.Stop
            };
            heightAnimation.Completed += (_, _) =>
            {
                Left = left;
                Top = top;
                Width = width;
                Height = height;
                completed();
            };

            BeginAnimation(LeftProperty, CreateBoundsAnimation(left, duration));
            BeginAnimation(TopProperty, CreateBoundsAnimation(top, duration));
            BeginAnimation(WidthProperty, CreateBoundsAnimation(width, duration));
            BeginAnimation(HeightProperty, heightAnimation);
        }

        private static DoubleAnimation CreateBoundsAnimation(
            double target,
            TimeSpan duration) =>
            new(target, duration)
            {
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseInOut
                },
                FillBehavior = FillBehavior.Stop
            };

        private Rect GetCurrentMonitorWorkArea()
        {
            if (windowHandle == IntPtr.Zero)
                return SystemParameters.WorkArea;

            IntPtr monitor = MonitorFromWindow(
                windowHandle,
                MonitorDefaultToNearest);
            MonitorInfo info = new()
            {
                Size = Marshal.SizeOf<MonitorInfo>()
            };

            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
                return SystemParameters.WorkArea;

            HwndSource? source = HwndSource.FromHwnd(windowHandle);
            if (source?.CompositionTarget == null)
                return SystemParameters.WorkArea;

            Point topLeft = source.CompositionTarget.TransformFromDevice.Transform(
                new Point(info.WorkArea.Left, info.WorkArea.Top));
            Point bottomRight = source.CompositionTarget.TransformFromDevice.Transform(
                new Point(info.WorkArea.Right, info.WorkArea.Bottom));

            return new Rect(topLeft, bottomRight);
        }

        internal bool ShouldOpenTopBarPopupUpward()
        {
            Rect workArea = GetCurrentMonitorWorkArea();
            return Top + ActualHeight / 2 > workArea.Top + workArea.Height / 2;
        }

        private void OpacitySlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            // Kuriant ContextMenu jo pradinė reikšmė neturi
            // perrašyti jau nustatyto modulių permatomumo.
            if (ContextMenu?.IsOpen != true)
                return;

            uiOpacity = Math.Clamp(e.NewValue / 100d, 0.60, 1.0);
            ApplyUiOpacityImmediate();
            if (txtOpacityValue != null)
                txtOpacityValue.Text = $"{Math.Round(e.NewValue)}%";
        }

        private void DashboardGrid_LayoutUpdated(object? sender, EventArgs e)
        {
            // Discover only: do not read or change formatting properties from
            // a layout-completion callback. Each new control is handled once.
            DiscoverFontScaleControls(this);
        }

        private void DiscoverFontScaleControls(DependencyObject root)
        {
            if (root is TextBlock or TextBox)
            {
                var text = (FrameworkElement)root;
                if (!registeredFontScaleControls.TryGetValue(text, out _))
                {
                    registeredFontScaleControls.Add(text, new StrongBox<byte>());
                    // An instance handler makes WPF include this element in its
                    // Loaded broadcast; a class handler alone does not do that.
                    text.AddHandler(FrameworkElement.LoadedEvent,
                        new RoutedEventHandler(ScaleLoadedTextControl), true);
                    if (text.IsLoaded) QueueTextFontScale(text);
                }
                return;
            }
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
                DiscoverFontScaleControls(VisualTreeHelper.GetChild(root, index));
        }

        private void QueueTextFontScale(FrameworkElement text)
        {
            pendingFontScaleControls.Add(text);
            if (fontScaleOperation != null) return;
            fontScaleOperation = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                fontScaleOperation = null;
                var controls = pendingFontScaleControls.ToArray();
                pendingFontScaleControls.Clear();
                foreach (var control in controls)
                    if (control.IsLoaded && ReferenceEquals(PresentationSource.FromVisual(control)?.RootVisual, this))
                        ApplyFontScaleToTextControl(control);
            }));
        }

        private void ApplyFontScaleToVisualTree(DependencyObject root)
        {
            if (root is FrameworkElement { IsLoaded: false }) return;
            if (root is TextBlock or TextBox)
            {
                ApplyFontScaleToTextControl((FrameworkElement)root);
                return;
            }
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
                ApplyFontScaleToVisualTree(VisualTreeHelper.GetChild(root, index));
        }

        private void ApplyFontScaleToTextControl(FrameworkElement control)
        {
            if (control is TextBlock textBlock)
            {
                LabelTypography? explicitTypography=textBlock.DataContext switch
                {
                    ShortcutsWidget.Slot slot=>slot.MacroTypography,
                    WebShortcutsWidget.WebShortcut shortcut=>shortcut.MacroTypography,
                    _=>null
                };
                explicitTypography??=textBlock.Name switch{"txtTitle"=>selectedPlayerSettings.Typography?.Title,"txtArtist"=>selectedPlayerSettings.Typography?.Artist,"txtTimeCurrent" or "txtTimeTotal"=>selectedPlayerSettings.Typography?.Time,_=>null};
                if (explicitTypography != null)
                {
                    if (Math.Abs(textBlock.FontSize - explicitTypography.SafeSize) > 0.01)
                        textBlock.SetCurrentValue(TextBlock.FontSizeProperty, explicitTypography.SafeSize);
                    return;
                }
                double baseSize = baseTextBlockFontSizes.GetValue(
                    textBlock, static text => new(text.FontSize)).Value;
                double effectiveScale = Equals(textBlock.Tag, "CappedWeatherText")
                    ? Math.Min(fontScale, 1.25) : fontScale;
                double scaledSize = baseSize * effectiveScale;
                if (Math.Abs(textBlock.FontSize - scaledSize) > 0.01)
                    textBlock.SetCurrentValue(TextBlock.FontSizeProperty, scaledSize);
            }
            else if (control is TextBox textBox)
            {
                double baseSize = baseTextBoxFontSizes.GetValue(
                    textBox, static text => new(text.FontSize)).Value;
                double scaledSize = baseSize * fontScale;
                if (Math.Abs(textBox.FontSize - scaledSize) > 0.01)
                    textBox.FontSize = scaledSize;
            }
        }

        private void SettingsMenuLayerChanged(object sender,RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(()=>{if(ContextMenu!=null)SettingsWindow.RefreshMenuLayer(ContextMenu);}));
        }
        private void ContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            SetPlayerMenuContext(IsPlayerMenuTarget());
            ApplyContextMenuScale(ContextMenu);
            ApplyMusicDimSettings();
            backgroundTransparencySlider.Value=backgroundTransparency;
            backgroundTransparencyValue.Text=$"{backgroundTransparency:0}%";
            updatingMenuScale = true;
            menuSizeSlider.Value = menuScalePercent;
            menuSizeValueText.Text = $"{menuScalePercent:0}%";
            updatingMenuScale = false;

            double percentage = uiOpacity * 100;
            opacitySlider.Value = percentage;
            txtOpacityValue.Text = $"{Math.Round(percentage)}%";
            alwaysOnTopMenuItem.IsChecked = alwaysOnTopEnabled;
            updatingAutoDimSettings = true;
            autoDimLevelSlider.Value = autoDimOpacityPercent;
            autoDimLevelValueText.Text = $"{autoDimOpacityPercent:0}%";
            updatingAutoDimSettings = false;
            PopulateSystemMonitorSensorMenu();
            StartTelemetry();
        }

        private void ContextMenu_Closed(object sender, RoutedEventArgs e)
        {
            SetPlayerMenuContext(false);
            SettingsWindow.RefreshMenuLayer(ContextMenu,false);
            StopTelemetry();
            SaveWindowSettings();
        }

        private void ApplyModuleOpacity(double opacity)
        {
            moduleOpacity = Math.Clamp(opacity, 0, 1);
            ApplyBaseColor(baseColor);
        }

        private void MenuSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (updatingMenuScale) return;
            menuScalePercent = Math.Clamp(e.NewValue, 70, 150);
            if (menuSizeValueText != null) menuSizeValueText.Text = $"{menuScalePercent:0}%";
            if (ContextMenu is ContextMenu menu)
                ApplyContextMenuScale(menu);
        }

        private void ApplyContextMenuScale(ContextMenu menu)
        {
            NormalizeMenuGlyphs(menu);
            Rect workArea = GetCurrentMonitorWorkArea();
            double progress = .4; // Fixed 100% geometry, uniformly scaled below.
            double scale = .768 * menuScalePercent / 100d;
            menu.Resources["MainMenuScale"] = new ScaleTransform(scale, scale);
            Application.Current.Resources["UnifiedMenuScale"]=new ScaleTransform(scale,scale);
            Application.Current.Resources["DashboardMenuFontSize"] = (18 + progress * 4)*1.56*menuTextPercent/100;
            Application.Current.Resources["MenuActionFontSize"]=20*1.56*menuTextPercent/100;Application.Current.Resources["MenuCoffeeFontSize"]=16*1.56*menuTextPercent/100;
            // Scale the root surface once. WPF popups inherit its screen transform.
            menu.LayoutTransform = Transform.Identity;
            menu.FontSize = (18 + progress * 4)*1.56*menuTextPercent/100;
            menu.Padding = new Thickness(6, 4, 6, 4);
            menu.MaxHeight = Math.Max(280, (workArea.Height - 24) / scale);

            // ValueChanged suveikia dar kraunant XAML, kai žemiau esantys
            // meniu valdikliai dar nesukurti. Geometriją pritaikysime meniu
            // atidarymo metu, kai visas vardų medis jau paruoštas.
            if (fitMonitorButtonsGrid == null ||
                dashboardColorsButton == null ||
                cityApplyButton == null)
                return;

            // Tekstui augant rezervuojame jam vietą horizontaliai, bet
            // nepučiame eilučių vertikaliai.
            // Visos turinio eilutės naudoja tą pačią koordinačių sistemą:
            // funkcija | valdiklis / reikšmė | veiksmas / procentas.
            double rowWidth = 468 + progress * 92;
            double labelWidth = 170 + progress * 34;
            double controlWidth = 220 + progress * 44;
            double actionWidth = 78 + progress * 14;
            foreach (Grid row in new[] { opacityRowGrid, menuSizeRowGrid,
                                         cityRowGrid, coordinatesRowGrid })
                row.Width = rowWidth;
            sensorsHeaderGrid.Width = rowWidth - 52;

            foreach (Grid row in new[] { backgroundTransparencyRowGrid, opacityRowGrid, menuSizeRowGrid, dimLevelRowGrid })
            {
                row.Width=rowWidth;
                row.ColumnDefinitions[0].Width = new GridLength(230+progress*46);
                row.ColumnDefinitions[1].Width = new GridLength(170+progress*34);
                row.ColumnDefinitions[2].Width = new GridLength(68+progress*12);
            }
            foreach (Grid row in new[] { cityRowGrid, coordinatesRowGrid })
            {
                row.Width = rowWidth + 40;
                row.ColumnDefinitions[0].Width = new GridLength(labelWidth);
                row.ColumnDefinitions[1].Width = new GridLength(controlWidth);
                row.ColumnDefinitions[2].Width = new GridLength(actionWidth);
            }


            // Settings needs less horizontal space than appearance/weather editors.
            menuSizeRowGrid.Width = 340 + progress * 68;
            menuSizeRowGrid.ColumnDefinitions[0].Width = new GridLength(148 + progress * 30);
            menuSizeRowGrid.ColumnDefinitions[1].Width = new GridLength(132 + progress * 26);
            menuSizeRowGrid.ColumnDefinitions[2].Width = new GridLength(60 + progress * 12);
            double actionHeight = 32 + progress * 5;
            opacitySlider.Height = actionHeight;
            menuSizeSlider.Height = actionHeight;

            cityTextBox.Height = actionHeight;
            coordinatesTextBox.Height = actionHeight;
            cityApplyButton.Height = actionHeight;
            coordinatesApplyButton.Height = actionHeight;
            cityApplyButton.MinWidth = 68 + progress * 12;
            coordinatesApplyButton.MinWidth = 68 + progress * 12;

            fitMonitorButtonsGrid.Width = 560;
            menuHeaderGrid.Width = 800;
            menuUsageView.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
            telemetryDetailsGrid.Width = menuHeaderGrid.Width;
            foreach (Separator separator in new[] { appearanceSectionSeparator,
                                                     weatherSectionSeparator,
                                                     systemSectionSeparator })
                separator.Width = rowWidth;

            cityTextBox.FontSize = 17 + progress * 3;
            coordinatesTextBox.FontSize = 17 + progress * 3;
            if (weatherNameRowGrid != null)
            {
                weatherNameRowGrid.Width = rowWidth + 40;
                weatherNameRowGrid.ColumnDefinitions[0].Width = new GridLength(labelWidth);
                weatherNameRowGrid.ColumnDefinitions[1].Width = new GridLength(controlWidth);
                weatherNameRowGrid.ColumnDefinitions[2].Width = new GridLength(actionWidth);
                weatherNameEditor.Height = weatherRenameButton.Height = actionHeight;
                weatherNameEditor.FontSize = 17 + progress * 3;
                weatherRenameButton.MinWidth = 68 + progress * 12;
            }
            telemetryDetailsText.FontSize = 16 + progress * 3;
            ApplyWeatherEditorMetrics();
            ApplyMenuTextSizes();

        }

        private void ChooseAccentColorButton_Click(object sender, RoutedEventArgs e)
        {
            Color initial = Color.FromRgb(30, 215, 96);
            Color initialClock = Color.FromRgb(244, 255, 255);
            Color initialSystemMonitor = Color.FromRgb(30, 215, 96);
            Color initialBase = Color.FromRgb(44, 44, 44);
            Color initialText = Colors.White;
            try
            {
                initial = (Color)ColorConverter.ConvertFromString(accentColor);
                initialClock = (Color)ColorConverter.ConvertFromString(clockColor);
                initialSystemMonitor = (Color)ColorConverter.ConvertFromString(systemMonitorColor);
                initialBase = (Color)ColorConverter.ConvertFromString(baseColor);
                initialText = (Color)ColorConverter.ConvertFromString(textColor);
            }
            catch { }

            systemMonitorWidget ??= new SystemMonitorWidget();
            double initialDashboardIntensity = dashboardColorIntensity;
            double initialClockIntensity = clockColorIntensity;
            double initialSystemMonitorIntensity = systemMonitorWidget.ColorIntensity;
            double initialTextIntensity = textColorOpacity;
            double initialModuleOpacity = moduleOpacity;
            AccentColorDialog dialog = new(
                initial,
                initialClock,
                initialSystemMonitor,
                initialBase,
                initialText,
                initialDashboardIntensity,
                initialClockIntensity,
                initialSystemMonitorIntensity,
                initialTextIntensity,
                initialModuleOpacity * 100,
                savedPaletteColors, (Color)ColorConverter.ConvertFromString(musicLightColor), musicLightIntensity) { Owner = this };
            string? initialPanelLabels=panelLabelColor, initialModuleTitles=moduleTitleColor;
            dialog.ConfigurePanelColors(((SolidColorBrush)Application.Current.Resources["ModuleTitleBrush"]).Color,((SolidColorBrush)Application.Current.Resources["PanelTextBrush"]).Color);
            if(sender is AccentColorDialog.ColorTarget targetSelection)dialog.SelectTarget(targetSelection);
            string? initialKnobColor=musicVolumeLightColor, initialPowerColor=musicPowerLightColor;
            double initialKnobIntensity=musicVolumeLightIntensity, initialPowerIntensity=musicPowerLightIntensity;
            Color Parse(string? value,Color fallback){try{return value==null?fallback:(Color)ColorConverter.ConvertFromString(value);}catch{return fallback;}}
            dialog.ConfigurePlayerLights(Parse(musicVolumeLightColor,Parse(musicLightColor,Colors.Orange)),musicVolumeLightIntensity,Parse(musicPowerLightColor,MusicConsoleControl.DefaultPowerColor(MusicFinish.Name)),musicPowerLightIntensity);
            dialog.ConfigureAvailability(ColorIsVisible);
            var touchedLights=new System.Collections.Generic.HashSet<AccentColorDialog.ColorTarget>();
            string initialMusicLight = musicLightColor;
            double initialMusicIntensity = musicLightIntensity;
            string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            dialog.PreviewChanged += (target, color, intensity) =>
            {
                touchedLights.Add(target);
                if(target==AccentColorDialog.ColorTarget.Knob){ApplyVolumeLightColor(Hex(color));ApplyVolumeLighting(intensity);}
                else if(target==AccentColorDialog.ColorTarget.Power)ApplyPowerLighting(Hex(color),intensity);
                else if (target is AccentColorDialog.ColorTarget.Titles or AccentColorDialog.ColorTarget.Labels)
                    SetPanelTextColor(target,color,intensity);
                else if (target == AccentColorDialog.ColorTarget.Music)
                    ApplyMusicLighting(Hex(color), intensity);
                else if (target == AccentColorDialog.ColorTarget.Clock)
                    ApplyClockColor(Hex(color), intensity);
                else if (target == AccentColorDialog.ColorTarget.SystemMonitor)
                {
                    ApplySystemMonitorColor(Hex(color), intensity);
                    systemMonitorWidget.ColorIntensity = intensity;
                }
                else if (target == AccentColorDialog.ColorTarget.Base)
                {
                    ApplyModuleOpacity(intensity / 100);
                    ApplyBaseColor(Hex(color));
                }
                else if (target == AccentColorDialog.ColorTarget.Text)
                    ApplyTextColor(Hex(color), intensity);
                else ApplyAccentColor(Hex(color), intensity);
            };
            if (dialog.ShowDialog() != true)
            {
                panelLabelColor=initialPanelLabels;moduleTitleColor=initialModuleTitles;ApplyPanelTextOverrides();
                ApplyMusicLighting(initialMusicLight, initialMusicIntensity);
                ApplyVolumeLightColor(initialKnobColor);ApplyVolumeLighting(initialKnobIntensity);ApplyPowerLighting(initialPowerColor,initialPowerIntensity);
                ApplyAccentColor(Hex(initial), initialDashboardIntensity);
                ApplyClockColor(Hex(initialClock), initialClockIntensity);
                ApplySystemMonitorColor(Hex(initialSystemMonitor), initialSystemMonitorIntensity);
                ApplyBaseColor(Hex(initialBase));
                ApplyModuleOpacity(initialModuleOpacity);
                ApplyTextColor(Hex(initialText), initialTextIntensity);
                systemMonitorWidget.ColorIntensity = initialSystemMonitorIntensity;
                return;
            }
            ApplyMusicLighting(Hex(dialog.MusicColor), dialog.MusicIntensity);
            if(touchedLights.Contains(AccentColorDialog.ColorTarget.Knob)){ApplyVolumeLightColor(Hex(dialog.KnobColor));ApplyVolumeLighting(dialog.KnobIntensity);}
            if(touchedLights.Contains(AccentColorDialog.ColorTarget.Power))ApplyPowerLighting(Hex(dialog.PowerColor),dialog.PowerIntensity);
            ApplyAccentColor(Hex(dialog.DashboardColor), dialog.DashboardIntensity);
            ApplyClockColor(Hex(dialog.ClockColor), dialog.ClockIntensity);
            ApplySystemMonitorColor(Hex(dialog.SystemMonitorColor), dialog.SystemMonitorIntensity);
            ApplyBaseColor(Hex(dialog.BaseColor));
            ApplyModuleOpacity(dialog.BaseOpacity / 100);
            ApplyTextColor(Hex(dialog.TextColor), dialog.TextIntensity);
            systemMonitorWidget.ColorIntensity = dialog.SystemMonitorIntensity;
            savedPaletteColors = dialog.SavedColors;
            SaveWindowSettings();
        }

        private void ApplyMusicLighting(string color,double intensity)
        {
            musicLightColor=color;musicLightIntensity=Math.Clamp(intensity,0,100);
            musicConsole?.SetLighting((Color)ColorConverter.ConvertFromString(color),musicLightIntensity);
            musicPlayerCard?.SetLighting((Color)ColorConverter.ConvertFromString(color),musicLightIntensity);
        }

        private void ApplyDefaultKnobColor(string color)
        {
            // Keep each player's explicit VolumeLightColor and intensity intact.
            var parsed=(Color)ColorConverter.ConvertFromString(color);
            for(int i=0;i<dashboardLayouts.Length;i++){
                var profile=dashboardLayouts[i];if(profile==null)continue;
                var settings=(profile.Player??new TabPlayerSettings()) with{LightColor=color};
                profile.Player=settings;if(i!=activeDashboardLayoutTab)tabConsoles[i]?.SetLighting(parsed,settings.LightIntensity);
            }
            selectedPlayerSettings=selectedPlayerSettings with{LightColor=color};
        }

        private void ApplyBaseColor(string color)
        {
            try
            {
                Color parsed = (Color)ColorConverter.ConvertFromString(color);
                baseColor = color;
                byte alpha = 255;
                Application.Current.Resources["ModuleBackgroundBrush"] =
                    new SolidColorBrush(Color.FromArgb(alpha, parsed.R, parsed.G, parsed.B));
                byte backdropAlpha = (byte)Math.Round(255*(1-backgroundTransparency/100));
                Application.Current.Resources["DashboardBackdropBrush"] =
                    new SolidColorBrush(Color.FromArgb(backdropAlpha, 0, 0, 0));
                activeBrowser?.SetDashboardAppearance(parsed, 1.0);
            }
            catch { }
        }

        private void ApplyTextColor(string color, double? opacity = null)
        {
            try
            {
                Color parsed = (Color)ColorConverter.ConvertFromString(color);
                textColor = color;
                if (opacity.HasValue) textColorOpacity = Math.Clamp(opacity.Value, 35, 100);
                byte alpha = (byte)Math.Round(255 * textColorOpacity / 100d);
                Application.Current.Resources["PrimaryTextBrush"] = new SolidColorBrush(
                    Color.FromArgb(alpha, parsed.R, parsed.G, parsed.B));
                Application.Current.Resources["PrimaryTextOpacity"] = textColorOpacity / 100d;
                byte controlAlpha = (byte)Math.Round(
                    255 * Math.Clamp(textColorOpacity, 35, 100) / 100d);
                Application.Current.Resources["SpotifyControlBrush"] = new SolidColorBrush(
                    Color.FromArgb(controlAlpha, parsed.R, parsed.G, parsed.B));
                ApplyTextColorToVisualTree(dashboardCanvas, parsed);
                quickLaunchBar.RefreshTabVisuals();
                activeBrowser?.SetTextOpacity(textColorOpacity / 100d);
            }
            catch { }
        }

        private void ApplyTextColorToVisualTree(DependencyObject root, Color color)
        {
            // Imported visualizer owns its original BlakiHUD palette.
            if(root is HudVisualizerCard or PlaylistCard)return;
            if (root is TextBlock textBlock)
            {
                if(ReferenceEquals(textBlock.Foreground,Application.Current.TryFindResource("PanelTextBrush")))return;
                Brush? primaryBrush = Application.Current.TryFindResource("PrimaryTextBrush") as Brush;
                if (ReferenceEquals(textBlock.Foreground, primaryBrush))
                    primaryTextAlpha.GetOrCreateValue(textBlock).Value = 255;
                if (!primaryTextAlpha.TryGetValue(textBlock, out var cachedAlpha) &&
                    textBlock.Foreground is SolidColorBrush brush &&
                    Math.Max(brush.Color.R, Math.Max(brush.Color.G, brush.Color.B)) -
                    Math.Min(brush.Color.R, Math.Min(brush.Color.G, brush.Color.B)) <= 22 &&
                    (brush.Color.R + brush.Color.G + brush.Color.B) / 3 >= 135)
                {
                    cachedAlpha = new(brush.Color.A);
                    primaryTextAlpha.Add(textBlock, cachedAlpha);
                }
                if (cachedAlpha != null)
                    textBlock.Foreground = new SolidColorBrush(Color.FromArgb(
                        (byte)Math.Round(cachedAlpha.Value * textColorOpacity / 100d), color.R, color.G, color.B));
            }
            else if (root is OpticallyCenteredText opticalText)
            {
                if(Equals(opticalText.Tag,"SensorValue")||Equals(opticalText.Tag,"ModuleTitle")||ReferenceEquals(opticalText.TextBrush,Application.Current.TryFindResource("PanelTextBrush")))return;
                if (string.Equals(opticalText.Tag as string, "ClockText", StringComparison.Ordinal))
                    return;
                Brush? primaryBrush = Application.Current.TryFindResource("PrimaryTextBrush") as Brush;
                if (ReferenceEquals(opticalText.TextBrush, primaryBrush))
                    primaryOpticalTextAlpha.GetOrCreateValue(opticalText).Value = 255;
                if (!primaryOpticalTextAlpha.TryGetValue(opticalText, out var cachedAlpha) &&
                    opticalText.TextBrush is SolidColorBrush brush &&
                    Math.Max(brush.Color.R, Math.Max(brush.Color.G, brush.Color.B)) -
                    Math.Min(brush.Color.R, Math.Min(brush.Color.G, brush.Color.B)) <= 22 &&
                    (brush.Color.R + brush.Color.G + brush.Color.B) / 3 >= 135)
                {
                    cachedAlpha = new(brush.Color.A);
                    primaryOpticalTextAlpha.Add(opticalText, cachedAlpha);
                }
                if (cachedAlpha != null)
                    opticalText.TextBrush = new SolidColorBrush(Color.FromArgb(
                        (byte)Math.Round(cachedAlpha.Value * textColorOpacity / 100d), color.R, color.G, color.B));
            }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                ApplyTextColorToVisualTree(VisualTreeHelper.GetChild(root, i), color);
        }

        private void ApplyAccentColor(string color, double? intensity = null)
        {
            try
            {
                Color parsed = (Color)ColorConverter.ConvertFromString(color);
                accentColor = color;
                if (intensity.HasValue)
                    dashboardColorIntensity = Math.Clamp(intensity.Value, 0, 100);
                Application.Current.Resources["AccentBrush"] =
                    new SolidColorBrush(ApplyColorIntensity(parsed, dashboardColorIntensity));
            }
            catch { }
        }

        private void ApplyClockColor(string color, double? intensity = null)
        {
            try
            {
                Color parsed = (Color)ColorConverter.ConvertFromString(color);
                clockColor = color;
                if (intensity.HasValue)
                    clockColorIntensity = Math.Clamp(intensity.Value, 0, 100);
                Application.Current.Resources["ClockBrush"] =
                    new SolidColorBrush(ApplyColorIntensity(parsed, clockColorIntensity));
                quickLaunchBar.RefreshClockColorResource();
            }
            catch { }
        }

        private static Color ApplyColorIntensity(Color color, double intensity)
        {
            double amount = Math.Clamp(intensity, 0, 100) / 100d;
            return Color.FromArgb((byte)Math.Round(255 * amount), color.R, color.G, color.B);
        }

        private void ApplySystemMonitorColor(string color, double opacity = 100)
        {
            try
            {
                Color parsed = (Color)ColorConverter.ConvertFromString(color);
                systemMonitorColor = color;
                Application.Current.Resources["SystemMonitorBrush"] = new SolidColorBrush(
                    Color.FromArgb((byte)Math.Round(255 * Math.Clamp(opacity, 35, 100) / 100d),
                        parsed.R, parsed.G, parsed.B));
            }
            catch { }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                WindowSettings? settings = LoadValidWindowSettings();
                if (settings == null)
                    return;

                if (settings.SavedPaletteColors is { Length: 4 })
                    savedPaletteColors = (string[])settings.SavedPaletteColors.Clone();

                RestorePhysicalSettingsBounds(settings);
                expandedWidth = Width;
                expandedHeight = Height;
                alwaysOnTopEnabled = settings.AlwaysOnTop;
                ApplyAlwaysOnTop();
                alwaysOnTopMenuItem.IsChecked = alwaysOnTopEnabled;
                autoDimElements = settings.AutoDimElements;
                autoDimMenuItem.IsChecked = autoDimElements;
                autoDashHideEnabled = settings.AutoDashHide;
                webListRowHeight=settings.WebListRowHeight;
                webListAppearance=settings.WebListAppearance;
                lastBrowserUrl=settings.LastBrowserUrl??settings.OpenWebShortcutUrl;
                webShortcutsViewMode=WebShortcutsWidget.NormalizeViewMode(settings.WebShortcutsViewMode,settings.WebShortcutsImagesOnly);
                webShortcutsIconSize=WebShortcutsWidget.NormalizeIconSize(settings.WebShortcutsIconSize);
                webShortcutsImagesOnly=webShortcutsViewMode=="Thumbs";
                webImagesOnlyMenuItem.IsChecked=webShortcutsImagesOnly;
                if(webShortcutsWidget!=null){webShortcutsWidget.RestoreViewSettings(webShortcutsViewMode,webShortcutsIconSize);webShortcutsWidget.SetListSize(webListRowHeight);webShortcutsWidget.ApplyListAppearance(webListAppearance);}
                autoDashHideMenuItem.IsChecked = autoDashHideEnabled;
                updatingAutoDimSettings = true;
                autoDimOpacityPercent = Math.Clamp(
                    settings.AutoDimOpacityPercent is >= 20 and <= 70
                        ? settings.AutoDimOpacityPercent : 70, 20, 70);
                autoDimLevelSlider.Value = autoDimOpacityPercent;
                autoDimLevelValueText.Text = $"{autoDimOpacityPercent:0}%";
                updatingAutoDimSettings = false;

                if (settings.ModuleOpacity is >= 0 and <= 1)
                {
                    ApplyModuleOpacity(settings.ModuleOpacity);
                }
                uiOpacity = settings.UiOpacity is >= 0.60 and <= 1.0
                    ? settings.UiOpacity : 1.0;
                backgroundTransparency=double.IsFinite(settings.BackgroundTransparency)?Math.Clamp(settings.BackgroundTransparency,0,100):0;
                ApplyUiOpacityImmediate();
                ApplyAutoDimOpacity();
                ApplyAccentColor(
                    settings.AccentColor ?? "#1ED760",
                    settings.DashboardColorIntensity);
                ApplyClockColor(
                    settings.ClockColor ?? "#F4FFFFFF",
                    settings.ClockColorIntensity);
                ApplySystemMonitorColor(settings.SystemMonitorColor ?? "#1ED760",
                    settings.SystemMonitorOpacity);
                ApplyBaseColor(settings.BaseColor ?? "#2C2C2C");
                ApplyTextColor(settings.TextColor ?? "#FFFFFF", settings.TextColorOpacity);
                layoutListTextSize=settings.LayoutListTextSize >=16 && settings.LayoutListTextSize <=ModuleSelectionDialog.MaximumListTextSize?settings.LayoutListTextSize:23;
                menuTextPercent=double.IsFinite(settings.MenuTextPercent)?Math.Clamp(settings.MenuTextPercent/(settings.MenuScaleBaseline>=2?1:1.56),80,200):100;usageTextPercent=double.IsFinite(settings.UsageTextPercent)?Math.Clamp(settings.UsageTextPercent/(settings.MenuScaleBaseline>=2?1:1.13),80,200):100;
                menuScalePercent = double.IsFinite(settings.MenuScalePercent) ? Math.Clamp(settings.MenuScalePercent / (settings.MenuScaleBaseline >= 2 ? 1 : settings.MenuScaleBaseline==1 ? 1.2 : .768),70,150) : 100;
                if (Enum.TryParse(
                        settings.CompactDockEdge,
                        true,
                        out DockEdge savedDockEdge))
                {
                    compactDockEdge = savedDockEdge;
                }
                ApplyStartupCompactState(settings.IsCompact);

                sleepComputer = settings.SleepComputer;
                quickLaunchBar.SleepComputer = sleepComputer;
                quickLaunchBar.TrySetShowClock(settings.ShowClock, out _);
                quickLaunchBar.TrySetShowSleep(settings.ShowSleep, out _);
                quickLaunchBar.TrySetShowShutdown(settings.ShowShutdown, out _);
                quickLaunchBar.TrySetShowRestart(settings.ShowRestart, out _);
                quickLaunchBar.TrySetShowObsRecord(settings.ShowObsRecord, out _);
                quickLaunchBar.TrySetShowObsBuffer(settings.ShowObsBuffer, out _);
                UpdateSleepModeChecks();
                UpdateTopBarControlChecks(); showSleepMenuItem.Visibility=Visibility.Collapsed; showShutdownMenuItem.Visibility=Visibility.Collapsed; showRestartMenuItem.Visibility=Visibility.Collapsed;
                UpdateObsControlChecks();

                showShortcutNames = settings.ShowShortcutNames;
                showShortcutNamesMenuItem.IsChecked = showShortcutNames;
                ApplyShortcutNameVisibility();
                showModuleTitles = settings.ShowModuleTitles;
                showModuleTitlesMenuItem.IsChecked = showModuleTitles;
                ApplyModuleTitleVisibility();
                showUsageOverlay = settings.ShowUsageOverlay;
                pendingWebShortcutUrl = settings.OpenWebShortcutUrl;
                pendingBrowserPixels=settings.BrowserPhysicalBounds;
                telemetryDetailsMenuItem.IsChecked = false;
                UpdateUsageOverlayVisibility();
                if (showUsageOverlay) StartTelemetry();

                ApplyFontScaleToVisualTree(this);

                RestoreWeatherLocations(settings);
                weatherWidget.SetDisplayLanguage(settings.WeatherLanguage);
                weatherWidget.SetTemperatureDecimals(settings.WeatherTemperatureDecimals);


                if (Enum.TryParse(
                        settings.MusicService,
                        ignoreCase: true,
                        out MusicService savedMusicService))
                {
                    SetMusicService(savedMusicService);
                }
                else
                {
                    SetMusicService(MusicService.Spotify);
                }

                if (settings.Modules is { Length: 3 })
                {
                    for (int i = 0; i < 3; i++)
                    {
                        if (Enum.TryParse(settings.Modules[i], true, out ModuleKind module))
                            selectedModules[i] = module;
                    }
                    if (settings.WideShortcutsSpan is 2 or 3 &&
                        selectedModules.All(module => module == ModuleKind.None))
                    {
                        // Migrate the short-lived manual-empty layout while restoring the
                        // exact modules that were present before that workflow was tested.
                        selectedModules[0] = ModuleKind.Shortcuts2;
                        selectedModules[1] = ModuleKind.Shortcuts;
                        selectedModules[2] = ModuleKind.Weather;
                    }
                    bool uniqueModules = selectedModules
                        .Where(module => module != ModuleKind.None)
                        .GroupBy(module => module)
                        .All(group => group.Count() == 1);
                    int savedWideSpan = settings.WideShortcutsSpan is 2 or 3
                        ? settings.WideShortcutsSpan : 0;
                    if (uniqueModules)
                    {
                        wideShortcutsSpan = savedWideSpan;
                        // Modules are created after tab storage migration in LoadDashboardLayouts.
                    }
                }

                ApplyMusicLighting(settings.MusicLightColor,settings.MusicLightIntensity);
                ApplyVolumeLighting(settings.MusicVolumeLightIntensity);
                ApplyVolumeLightColor(settings.MusicVolumeLightColor);
                MusicFinish.Set(settings.MusicFinish);
                IconAppearance.SetStyle(settings.IconStyle);UpdateIconStyleChecks();
                UpdateFinishChecks();
                musicConsoleEnabled = settings.MusicConsoleEnabled;
                musicTopBarHidden=settings.MusicTopBarHidden;musicDimEnabled=settings.MusicDimEnabled;
                musicDimDelaySeconds=settings.MusicDimDelaySeconds is 10 or 30 or 120 or 300?settings.MusicDimDelaySeconds:10;
                ApplyMusicDimSettings();
                musicConsoleMenuItem.IsChecked = musicConsoleEnabled;
                UpdateMusicConsoleVisibility();
                // Retired visualizer selections are migrated to empty slots;
                // legacy mode strings are ignored, never reactivated.
                

                RestorePhysicalSettingsBounds(settings);
                LanguageManager.Current = AppLanguage.English;
            }
            catch
            {
                // Jei nustatymų failas sugadintas, programa vis tiek pasileis.
            }
            finally
            {
                restoringStartupLayout=false;
                LoadDashboardLayouts();
                quickLaunchBar.CompleteSettingsLoad();
                _ = Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(() => { startupBoundsCaptured=GetWindowRect(windowHandle,out startupBounds); }));
                if (ContextMenu is ContextMenu menu)
                    ApplyContextMenuScale(menu);
                if (!string.IsNullOrWhiteSpace(pendingWebShortcutUrl))
                    _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
                    {
                        string? url = pendingWebShortcutUrl;
                        pendingWebShortcutUrl = null;
                        WebShortcutsWidget startupWebWidget=webShortcutsWidget??new WebShortcutsWidget();
                        WebShortcutsWidget.WebShortcut? item = url == null
                            ? null : startupWebWidget.FindByUrl(url);
                        if (item != null)
                            OpenWebShortcut(startupWebWidget, item);
                    }));
            }
        }

        private WindowSettings? LoadValidWindowSettings()
        {
            WindowSettings? preferencesWithoutBounds = null;
            string backup = settingsFile + ".previous";
            foreach (string candidate in new[] { settingsFile, backup })
            {
                try
                {
                    if (!File.Exists(candidate)) continue;
                    WindowSettings? loaded = JsonSerializer.Deserialize<WindowSettings>(
                        File.ReadAllText(candidate));
                    if (loaded == null) continue;
                    // Geometry can be absent on first run or invalid after a monitor
                    // change. Keep non-geometric preferences and migration results.
                    if (!IsValid(loaded)) { preferencesWithoutBounds ??= loaded; continue; }
                    InteractionDiagnostics.Log(
                        $"WindowSettings loaded source={candidate} bounds={loaded.Left:0.###}," +
                        $"{loaded.Top:0.###},{loaded.Width:0.###},{loaded.Height:0.###}");
                    return loaded;
                }
                catch (Exception ex)
                {
                    InteractionDiagnostics.Log(
                        $"WindowSettings load failed source={candidate} error={ex.GetType().Name}");
                }
            }
            return preferencesWithoutBounds;
        }


        private bool IsValid(WindowSettings settings)
        {
            if(settings.PhysicalBounds is {Length:4} pixels && pixels[2]>0 && pixels[3]>0 &&
                double.IsFinite(settings.Width) && double.IsFinite(settings.Height) &&
                double.IsFinite(settings.Left) && double.IsFinite(settings.Top)) return true;
            if (!double.IsFinite(settings.Left) ||
                !double.IsFinite(settings.Top) ||
                !double.IsFinite(settings.Width) ||
                !double.IsFinite(settings.Height) ||
                settings.Width < MinWidth ||
                settings.Height < MinHeight)
            {
                return false;
            }

            const double visibleArea = 50;

            return settings.Left + settings.Width >=
                       SystemParameters.VirtualScreenLeft + visibleArea &&
                   settings.Left <=
                       SystemParameters.VirtualScreenLeft +
                       SystemParameters.VirtualScreenWidth - visibleArea &&
                   settings.Top + settings.Height >=
                       SystemParameters.VirtualScreenTop + visibleArea &&
                   settings.Top <=
                       SystemParameters.VirtualScreenTop +
                       SystemParameters.VirtualScreenHeight - visibleArea;
        }

        private void Window_Closing(
            object? sender,
            System.ComponentModel.CancelEventArgs e)
        {
            pendingTabPersistence?.Abort();
            pendingTabPersistence = null;
            telemetryTimer.Stop();
            autoDimTimer.Stop();
            applicationClosing = true;
            telemetryRam.Dispose();
            quickLaunchBar.SavePersistentLayoutState();
            SaveWindowSettings();
            activeBrowser?.Close();
            activeBrowser = null;
            usageOverlayWindow?.Close();
            ReleasePhysicalMonitor();
            wideSystemMonitor?.Dispose();fullSystemMonitor?.Dispose();systemMonitorWidget?.Dispose();
            AirthingsService.Shared.Dispose();
            visualizerAudio.Dispose();

        }

        private void SaveWindowSettings()
        {
            try
            {
                Directory.CreateDirectory(settingsFolder);
                SaveDashboardLayouts();

                Rect bounds = WindowState == WindowState.Normal
                    ? new Rect(Left, Top, ActualWidth, ActualHeight)
                    : RestoreBounds;

                if (isCompact)
                {
                    bounds.Width = expandedWidth;
                    bounds.Height = expandedHeight;
                }

                WindowSettings settings = new()
                {
                    PhysicalBounds = WindowState == WindowState.Normal && GetWindowRect(windowHandle,out NativeRect physical)
                        ? new[]{physical.Left,physical.Top,physical.Right-physical.Left,physical.Bottom-physical.Top} : null,
                    Left = bounds.Left,
                    Top = bounds.Top,
                    Width = bounds.Width,
                    Height = bounds.Height,
                    ModuleOpacity = moduleOpacity,
                    UiOpacity = uiOpacity,
                    BackgroundTransparency = backgroundTransparency,
                    FontScale = fontScale,
                    Coordinates = ActiveWeatherLocation?.Coordinates,
                    City = ActiveWeatherLocation?.Name,


                    WeatherLanguage = weatherWidget.DisplayLanguage,
                    WeatherTemperatureDecimals = weatherWidget.ShowTemperatureDecimals,
                    WeatherLocations = weatherLocations.ToList(),
                    ActiveWeatherLocationId = activeWeatherLocationId,
                    AlwaysOnTop = alwaysOnTopEnabled,
                    MusicService = selectedMusicService.ToString(),
                    AccentColor = accentColor,
                    ClockColor = clockColor,
                    SystemMonitorColor = systemMonitorColor,
                    BaseColor = baseColor,
                    TextColor = textColor,
                    SavedPaletteColors = (string[])savedPaletteColors.Clone(),
                    TextColorOpacity = textColorOpacity,
                    SystemMonitorOpacity = systemMonitorWidget?.ColorIntensity ?? 100,
                    MenuScalePercent = menuScalePercent,MenuScaleBaseline=2,MenuTextPercent=menuTextPercent,UsageTextPercent=usageTextPercent,
                    LayoutListTextSize = layoutListTextSize,
                    DashboardColorIntensity = dashboardColorIntensity,
                    ClockColorIntensity = clockColorIntensity,
                    CompactDockEdge = compactDockEdge.ToString(),
                    IsCompact = isCompact,
                    Modules = selectedModules.Select(module => module.ToString()).ToArray()
                    ,WideShortcutsSpan = wideShortcutsSpan
                    ,MusicLightColor = musicLightColor
                    ,MusicLightIntensity = musicLightIntensity
                    ,MusicVolumeLightIntensity = musicVolumeLightIntensity
                    ,MusicVolumeLightColor = musicVolumeLightColor
                    ,MusicDimEnabled=musicDimEnabled,MusicDimDelaySeconds=musicDimDelaySeconds,MusicTopBarHidden=musicTopBarHidden
                    ,PanelMetal = panelNoSkin?"none":panelMetalLight?"light":"dark"
                    ,MusicFinish = Ekrano_valdymas.MusicFinish.Name
                    ,IconStyle = IconAppearance.Style
                    ,MusicConsoleEnabled = musicConsoleEnabled
                    ,VisualizerMode = "None"
                    ,VisualizerMode2 = "None"
                    ,SleepComputer = sleepComputer
                    ,ShowClock = quickLaunchBar.ShowClock
                    ,ShowSleep = quickLaunchBar.ShowSleep
                    ,ShowShutdown = quickLaunchBar.ShowShutdown
                    ,ShowRestart = quickLaunchBar.ShowRestart
                    ,ShowObsRecord = quickLaunchBar.ShowObsRecord
                    ,ShowObsBuffer = quickLaunchBar.ShowObsBuffer
                    ,ShowShortcutNames = showShortcutNames
                    ,ShowModuleTitles = showModuleTitles
                    ,ShowUsageOverlay = showUsageOverlay
                    ,AutoDimElements = autoDimElements
                    ,AutoDimOpacityPercent = autoDimOpacityPercent
                    ,AutoDashHide = autoDashHideEnabled
                    ,WebListRowHeight=webListRowHeight
                    ,WebListAppearance=webListAppearance
                    ,LastBrowserUrl=lastBrowserUrl
                    ,WebShortcutsImagesOnly = webShortcutsImagesOnly
                    ,WebShortcutsViewMode=webShortcutsViewMode
                    ,WebShortcutsIconSize=webShortcutsIconSize
                    ,BrowserPhysicalBounds = activeBrowser!=null && GetWindowRect(activeBrowser.GetNativeHandle(),out NativeRect savedWeb) ? new[]{savedWeb.Left,savedWeb.Top,savedWeb.Right-savedWeb.Left,savedWeb.Bottom-savedWeb.Top}:null
                    ,OpenWebShortcutUrl = activeWebShortcutUrl
                };

                string json = JsonSerializer.Serialize(settings);

                string temporary = settingsFile + ".tmp";
                string backup = settingsFile + ".previous";
                File.WriteAllText(temporary, json);
                if (File.Exists(settingsFile))
                {
                    try
                    {
                        File.Replace(temporary, settingsFile, backup, true);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Copy(settingsFile, backup, true);
                        File.Move(temporary, settingsFile, true);
                    }
                }
                else
                {
                    File.Move(temporary, settingsFile);
                }
            }
            catch (Exception ex)
            {
                InteractionDiagnostics.Log(
                    $"WindowSettings save failed error={ex.GetType().Name} message={ex.Message}");
            }
        }

        private int ClampDashboardLayoutTab(int requestedTab) => Math.Clamp(
            requestedTab, 0, Math.Clamp(quickLaunchBar.VisibleLayoutTabCount, 1, dashboardLayouts.Length) - 1);

        private void LoadDashboardLayouts()
        {
            if (dashboardLayoutsLoaded)
                return;

            DashboardLayoutProfile current = CaptureDashboardLayout();
            DashboardLayoutProfile[]? saved = null;
            foreach(string candidatePath in new[]{dashboardLayoutsFile,dashboardLayoutsFile+".previous"})
            {
                try
                {
                    if(!File.Exists(candidatePath))continue;
                    saved=JsonSerializer.Deserialize<DashboardLayoutProfile[]>(File.ReadAllText(candidatePath));
                    if(saved!=null)break;
                }
                catch(Exception ex){InteractionDiagnostics.RecordFailure("Dashboard layouts load "+candidatePath,ex);}
            }

            for (int index = 0; index < dashboardLayouts.Length; index++)
            {
                bool legacyFallback = saved == null && !InstallationDefaults.IsNewInstall &&
                    index < TabShortcutStorage.LegacyLayoutTabCount;
                DashboardLayoutProfile fallback = legacyFallback
                    ? current
                    : new DashboardLayoutProfile
                    {
                        Modules = new[] { "Shortcuts", "Shortcuts2", "Shortcuts3" },
                        SpeakerChannel = current.SpeakerChannel,
                        SpeakerChannel2 = current.SpeakerChannel2,
                        MusicConsoleEnabled = false,
                        MusicService = current.MusicService,
                        MusicTopBarHidden = false,
                        Player = current.Player is {} state ? CloneTabPlayerSettings(state) : new TabPlayerSettings()
                    };
                DashboardLayoutProfile candidate = saved != null && index < saved.Length
                    ? saved[index]
                    : fallback;
                // The first configurable-tabs version preallocated unused tabs
                // 6–11 as three None placeholders. Give those placeholders their
                // usable default without replacing any configured module layout.
                if (index >= TabShortcutStorage.LegacyLayoutTabCount &&
                    candidate is { DefaultsVersion: 0, WideShortcutsSpan: 0, MusicConsoleEnabled: false } &&
                    candidate.Modules is { Length: 3 } && candidate.Modules.All(m => m == "None"))
                    candidate = new DashboardLayoutProfile {
                        Modules = new[] { "Shortcuts", "Shortcuts2", "Shortcuts3" },
                        SpeakerChannel=candidate.SpeakerChannel,SpeakerChannel2=candidate.SpeakerChannel2,
                        Player=candidate.Player,MusicConsoleEnabled=false,MusicService=candidate.MusicService,
                        MusicTopBarHidden=false,DefaultsVersion=1 };
                dashboardLayouts[index] = NormalizeDashboardLayout(candidate, fallback);
                dashboardLayouts[index].DefaultsVersion = 1;
                dashboardLayouts[index].MusicConsoleEnabled = saved!=null && index<saved.Length && saved[index]?.MusicConsoleEnabled is bool configured ? configured : legacyFallback && index==0 && current.MusicConsoleEnabled==true;
                dashboardLayouts[index].MusicService ??= current.MusicService;
                dashboardLayouts[index].Player ??= CloneTabPlayerSettings(current.Player ?? new TabPlayerSettings());
                // Start with the per-tab power preference. Restore the other
                // saved controls without issuing Pause to an external music app.
                dashboardLayouts[index].Player!.PowerOn=dashboardLayouts[index].Player!.PowerOnAtStartup;
                // Resolve every legacy tab before switching any of them, so a
                // later selection never inherits the previously visited tab.
                if(dashboardLayouts[index].Player!.IconStyle is not ("original" or "mono" or "metal" or "metal-c" or "inlay"))
                    dashboardLayouts[index].Player!.IconStyle=current.Player?.IconStyle??IconAppearance.Style;
                dashboardLayouts[index].Player!.IconStyle=IconAppearance.Normalize(dashboardLayouts[index].Player!.IconStyle);
                dashboardLayouts[index].Player!.Finish=MusicFinish.Normalize(dashboardLayouts[index].Player!.Finish);
                dashboardLayouts[index].MusicTopBarHidden ??= index==0 && current.MusicTopBarHidden==true;
            }

            activeDashboardLayoutTab = ClampDashboardLayoutTab(quickLaunchBar.ActiveLayoutTab);
            TabShortcutStorage.Migrate(settingsFolder,activeDashboardLayoutTab,(tab,kind)=>kind=="wide"?dashboardLayouts[tab].WideShortcutsSpan>0:dashboardLayouts[tab].Modules.Contains(kind=="regular"?"Shortcuts":"Shortcuts2"));
            dashboardLayoutsLoaded = true;
            ApplyDashboardLayout(dashboardLayouts[activeDashboardLayoutTab]);
            UpdateMusicConsoleVisibility();
            WriteDashboardLayouts();
        }

        private void QuickLaunchBar_LayoutTabChanged(int requestedTab)
        {
            Stopwatch timing = Stopwatch.StartNew();
            int nextTab = ClampDashboardLayoutTab(requestedTab);
            if (!dashboardLayoutsLoaded)
            {
                activeDashboardLayoutTab = nextTab;
                return;
            }

            bool expandSelectedTab=isCompact && nextTab!=activeDashboardLayoutTab;
            dashboardLayouts[activeDashboardLayoutTab] = CaptureDashboardLayout();
            InteractionDiagnostics.Log(
                $"TabSwitch {activeDashboardLayoutTab + 1}->{nextTab + 1} captureMs={timing.Elapsed.TotalMilliseconds:0.###}");
            activeDashboardLayoutTab = nextTab;
            ApplyDashboardLayout(dashboardLayouts[activeDashboardLayoutTab]);
            UpdateMusicConsoleVisibility();
            if(expandSelectedTab) SetCompactMode(false);
            InteractionDiagnostics.Log(
                $"TabSwitch tab={nextTab + 1} applyMs={timing.Elapsed.TotalMilliseconds:0.###}");
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                InteractionDiagnostics.Log(
                    $"TabSwitch tab={nextTab + 1} firstRenderMs={timing.Elapsed.TotalMilliseconds:0.###}")));
            pendingTabPersistence?.Abort();
            pendingTabPersistence = Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() =>
                {
                    pendingTabPersistence = null;
                    Stopwatch persistence = Stopwatch.StartNew();
                    SaveWindowSettings();
                    InteractionDiagnostics.Log(
                        $"TabSwitch tab={activeDashboardLayoutTab + 1} persistenceMs={persistence.Elapsed.TotalMilliseconds:0.###}");
                }));
        }

        private void ApplyMusicDimSettings()
        {
            ApplyPlayerTypography(selectedPlayerSettings.Typography??new());
            foreach(MenuItem item in ModuleMenuItems(musicModuleMenu))
                if(item.Tag is string delay)item.IsChecked=delay=="0"?!musicDimEnabled:musicDimEnabled && delay==musicDimDelaySeconds.ToString();
            musicConsole?.SetDimming(musicDimEnabled,musicDimDelaySeconds);
        }
        private void MusicDimDelay_Click(object sender,RoutedEventArgs e){int delay=int.Parse((string)((MenuItem)sender).Tag);musicDimEnabled=delay>0;if(delay>0)musicDimDelaySeconds=delay;ApplyMusicDimSettings();SaveWindowSettings();}
        private void MusicFinish_Click(object sender,RoutedEventArgs e)
        {
            string finish=((MenuItem)sender).Tag as string??"dark";
            if(MusicFinish.Name!=finish){SelectTabConsole(PlayerSkinLights.Switch(CaptureTabPlayer(),finish),true);UpdateMusicConsoleVisibility();}
            UpdateFinishChecks();
            SaveWindowSettings();
        }
        private void MusicConsoleMenuItem_Click(object sender, RoutedEventArgs e)
        {
            musicConsoleEnabled = musicConsoleMenuItem.IsChecked;
            UpdateMusicConsoleVisibility();
            SaveWindowSettings();
        }
        private void UpdateMusicConsoleVisibility()
        {
            if (musicConsoleHost == null || !IsLoaded || restoringStartupLayout || applyingDashboardProfile) return;
            RefreshLocalAvailability();
            if(musicConsoleEnabled&&musicConsole==null)SelectTabConsole(CaptureTabPlayer());
            var visibilityTiming=Stopwatch.StartNew();
            // SetWindowPos can pump messages while a transparent window is resized.
            // Do not expose a layout with the new rows but the previous viewport.
            using (Dispatcher.DisableProcessing())
            {
                musicConsoleHost.Visibility = musicConsoleEnabled
                    ? Visibility.Visible : Visibility.Collapsed;
                // Keep layout and the shared player service, but suspend hidden sensor/visualizer work.
                double moduleOpacity=musicConsoleEnabled?0:uiOpacity;
                musicConsoleHost.Opacity=uiOpacity;
                leftModuleHost.Opacity=centerModuleHost.Opacity=rightModuleHost.Opacity=wideShortcutsHost.Opacity=moduleOpacity;
                UpdateBackgroundModuleActivity();
                InteractionDiagnostics.Log($"TabPhase activity tab={activeDashboardLayoutTab+1} ms={visibilityTiming.Elapsed.TotalMilliseconds:0.###}");visibilityTiming.Restart();
                bool hide=musicConsoleEnabled&&musicTopBarHidden&&!isCompact;
                quickLaunchBar.Visibility=hide?Visibility.Collapsed:Visibility.Visible;
                musicTopBarRow.Height=new GridLength(hide?0:104);moduleGapRow.Height=new GridLength(hide?0:9);
                musicConsole?.SetTopBarHidden(hide);
                quickLaunchBar.SetMusicBackgroundContext(musicConsoleEnabled);
                if (dashboardCanvas != null) ApplyWideShortcutsTitleLayout();
                UpdateMusicViewport();
                InteractionDiagnostics.Log($"TabPhase viewport tab={activeDashboardLayoutTab+1} ms={visibilityTiming.Elapsed.TotalMilliseconds:0.###}");visibilityTiming.Restart();
            }
            // Keep the geometry transaction atomic, then let accessibility
            // callbacks complete while WPF publishes the finished layout.
            UpdateLayout();
            InteractionDiagnostics.Log($"TabPhase layout tab={activeDashboardLayoutTab+1} ms={visibilityTiming.Elapsed.TotalMilliseconds:0.###}");
            // Publish the final material viewport with the final layout. A queued
            // LayoutUpdated callback can otherwise leave one composed flat/stale frame.
            if(dashboardCanvas!=null)CommonMetal.RefreshVisibleTree(dashboardCanvas);
            ApplyAutoDimOpacity();
        }

        private void UpdateBackgroundModuleActivity()
        {
            bool covered=isCompact||musicConsoleEnabled;
            systemMonitorWidget?.SetSuspended(covered);wideSystemMonitor?.SetSuspended(covered);fullSystemMonitor?.SetSuspended(covered);
            bool pauseVisuals=covered||ResourcePolicy.IsGame;
            analogVuCard?.SetSuspended(pauseVisuals);
            musicPlayerCard?.SetSuspended(covered);
            hudVisualizerCard?.SetSuspended(pauseVisuals);
            activeBrowser?.SetDashboardExpanded(!isCompact);
        }

        private void UpdateMusicViewport()
        {
            if(dashboardViewbox==null)return;
            bool music=musicConsoleEnabled&&!isCompact;
            // Both bar states retain one width-derived scale and a bottom anchor.
            // Rounding 447 design units at 144 DPI previously changed the VU,
            // artwork and legend rasterization relative to the 560-unit state.
            if(music)
            {
                dashboardViewbox.UseLayoutRounding=false;
                dashboardViewbox.VerticalAlignment=VerticalAlignment.Bottom;
                dashboardViewbox.Height=ActualWidth*ExpandedDashboardCanvasHeight/1920;
            }
            else
            {
                dashboardViewbox.ClearValue(UseLayoutRoundingProperty);
                dashboardViewbox.VerticalAlignment=VerticalAlignment.Stretch;
                dashboardViewbox.Height=double.NaN;
            }
        }

        private DashboardLayoutProfile CaptureDashboardLayout() => new()
        {
            DefaultsVersion = 1,
            Modules = selectedModules.Select(module => module.ToString()).ToArray(),
            WideShortcutsSpan = wideShortcutsSpan,
            MusicConsoleEnabled=musicConsoleEnabled, MusicService=selectedMusicService.ToString(), MusicTopBarHidden=musicTopBarHidden, Player=CaptureTabPlayer()
        };

        private DashboardLayoutProfile NormalizeDashboardLayout(
            DashboardLayoutProfile? candidate,
            DashboardLayoutProfile fallback)
        {
            if (candidate?.Modules is not { Length: 3 })
                return fallback.Clone();

            ModuleKind[] parsed = new ModuleKind[3];
            for (int index = 0; index < parsed.Length; index++)
                if (!Enum.TryParse(candidate.Modules[index], true, out parsed[index]))
                    return fallback.Clone();

            if(parsed.Skip(1).Any(m=>m is ModuleKind.SystemWide or ModuleKind.SystemFull))return fallback.Clone();
            bool unique = parsed.Where(module => module != ModuleKind.None)
                .GroupBy(module => module).All(group => group.Count() == 1);
            if (!unique)
                return fallback.Clone();

            return new DashboardLayoutProfile
            {
                DefaultsVersion = candidate.DefaultsVersion,
                Modules = parsed.Select(module => MigrateRetiredModule(module).ToString()).ToArray(),
                SpeakerChannel=candidate.SpeakerChannel,SpeakerChannel2=candidate.SpeakerChannel2,
                WideShortcutsSpan = candidate.WideShortcutsSpan is 2 or 3
                    ? candidate.WideShortcutsSpan : 0,
                MusicConsoleEnabled=candidate.MusicConsoleEnabled, MusicService=candidate.MusicService, MusicTopBarHidden=candidate.MusicTopBarHidden, Player=candidate.Player is {} state?CloneTabPlayerSettings(state):null
            };
        }

        private void ApplyDashboardLayout(DashboardLayoutProfile profile)
        {
            for (int index = 0; index < selectedModules.Length; index++)
                if (Enum.TryParse(profile.Modules[index], true, out ModuleKind module))
                    selectedModules[index] = MigrateRetiredModule(module);
            wideShortcutsSpan = profile.WideShortcutsSpan;
            musicConsoleEnabled=profile.MusicConsoleEnabled==true;musicTopBarHidden=profile.MusicTopBarHidden==true;
            if(Enum.TryParse<MusicService>(profile.MusicService,out var service))
            {
                applyingDashboardProfile=true;
                try{SetMusicService(service);}finally{applyingDashboardProfile=false;}
            }
            musicConsoleMenuItem.IsChecked=musicConsoleEnabled;
            var phase=Stopwatch.StartNew();
            profile.Player ??= new TabPlayerSettings();SelectTabConsole(profile.Player);
            InteractionDiagnostics.Log($"TabPhase player tab={activeDashboardLayoutTab+1} ms={phase.Elapsed.TotalMilliseconds:0.###}");phase.Restart();
            ApplySelectedModules();
            InteractionDiagnostics.Log($"TabPhase modules tab={activeDashboardLayoutTab+1} ms={phase.Elapsed.TotalMilliseconds:0.###}");
            ReleaseUnusedModuleViews();
            UpdateDefaultSourceIcon();
            UpdateModuleMenuChecks();
            _=Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>CommonMetal.ApplyResourceMode(ResourcePolicy.Mode)));
        }

        private void SaveDashboardLayouts()
        {
            if (!dashboardLayoutsLoaded)
                return;
            dashboardLayouts[activeDashboardLayoutTab] = CaptureDashboardLayout();
            ReleaseUnusedModuleViews();
            WriteDashboardLayouts();
        }

        private void ReleaseUnusedModuleViews()
        {
            if(!dashboardLayoutsLoaded)return;
            TrimTabConsoles();
            // The active selection can differ from its last saved profile.
            // Keep views needed by ANY other tab, including stored hidden tabs.
            bool Uses(string module)=>selectedModules.Any(m=>m.ToString()==module)||
                dashboardLayouts.Where((_,i)=>i!=activeDashboardLayoutTab)
                    .Any(p=>p?.Modules?.Contains(module)==true);
            bool playerNeeded=Uses("MusicPlayerCard"),vuNeeded=Uses("AnalogVuCard");
            bool wideNeeded=musicConsoleEnabled||dashboardLayouts.Where((_,i)=>i!=activeDashboardLayoutTab)
                .Any(p=>p?.MusicConsoleEnabled==true);
            if(!playerNeeded&&musicPlayerCard is {} player){player.Release();musicPlayerCard=null;}
            if(!vuNeeded&&analogVuCard is {} vu){vu.SetSuspended(true);analogVuCard=null;}
            MusicConsoleControl.ReleaseUnusedModuleImages(playerNeeded,vuNeeded,wideNeeded);
        }

        private void WriteDashboardLayouts()
        {
            try
            {
                Directory.CreateDirectory(settingsFolder);
                AtomicJson.Write(dashboardLayoutsFile, dashboardLayouts);
            }
            catch { }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LastInputInfo
        {
            public uint Size;
            public uint Time;
        }

        private sealed class DashboardLayoutProfile
        {
            public int DefaultsVersion { get; set; }
            public string[] Modules { get; set; } =
                { "Shortcuts", "Shortcuts2", "Shortcuts3" };
            public int WideShortcutsSpan { get; set; }

            public string? SpeakerChannel {get;set;}
            public string? SpeakerChannel2 {get;set;}
            public TabPlayerSettings? Player {get;set;}
            public bool? MusicConsoleEnabled {get;set;}
            public string? MusicService {get;set;}
            public bool? MusicTopBarHidden {get;set;}
            public DashboardLayoutProfile Clone() => new()
            {
                Modules = (string[])Modules.Clone(),
                DefaultsVersion=DefaultsVersion,
                SpeakerChannel=SpeakerChannel,SpeakerChannel2=SpeakerChannel2,
                WideShortcutsSpan = WideShortcutsSpan,
                MusicConsoleEnabled=MusicConsoleEnabled,MusicService=MusicService,MusicTopBarHidden=MusicTopBarHidden,Player=Player is {} state?CloneTabPlayerSettings(state):null
            };
        }

        private class WindowSettings
        {
            public int[]? PhysicalBounds { get; set; }
            public double Left { get; set; }
            public double Top { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public double ModuleOpacity { get; set; } = 0.7;
            public double UiOpacity { get; set; } = 1.0;
            public double BackgroundTransparency { get; set; }
            public double FontScale { get; set; } = 1.15;
            public string? Coordinates { get; set; }
            public string? City { get; set; }


            public string WeatherLanguage { get; set; } = "en";
            public bool WeatherTemperatureDecimals { get; set; }
            public double SpeakerSignalGain {get;set;}=4;
            public List<WeatherLocation>? WeatherLocations { get; set; }
            public string? ActiveWeatherLocationId { get; set; }
            public bool AlwaysOnTop { get; set; }
            public string MusicLightColor { get; set; } = ProductColorDefaults.Buttons;
            public double MusicLightIntensity { get; set; } = 100;
            public double MusicVolumeLightIntensity { get; set; } = 50;
            public string? MusicVolumeLightColor { get; set; }
            public bool MusicDimEnabled {get;set;}
            public int MusicDimDelaySeconds {get;set;}=10;
            public bool MusicTopBarHidden {get;set;}
            public string PanelMetal {get;set;}="dark";
            public string MusicFinish {get;set;}=Ekrano_valdymas.MusicFinish.ProfileDefaultName;
            public string IconStyle {get;set;}="metal";
            public bool MusicConsoleEnabled { get; set; }
            public string MusicService { get; set; } = "Spotify";
            public string? AccentColor { get; set; } = "#1ED760";
            public string? ClockColor { get; set; } = ProductColorDefaults.Clock;
            public string? SystemMonitorColor { get; set; } = ProductColorDefaults.Sensors;
            public string? BaseColor { get; set; } = ProductColorDefaults.Base;
            public string? TextColor { get; set; } = ProductColorDefaults.Text;
            public string[] SavedPaletteColors { get; set; } =
                { "#263238", "#795548", "#607D8B", "#000000" };
            public double TextColorOpacity { get; set; } = 100;
            public double SystemMonitorOpacity { get; set; } = 100;
            public int MenuScaleBaseline {get;set;}
            public double MenuScalePercent { get; set; } = 100;
            public double MenuTextPercent {get;set;}=100; public double UsageTextPercent {get;set;}=100;
            public double LayoutListTextSize {get;set;}=23;
            public double DashboardColorIntensity { get; set; } = 100;
            public double ClockColorIntensity { get; set; } = 100;
            public string CompactDockEdge { get; set; } = "Bottom";
            public bool IsCompact { get; set; }
            public string[] Modules { get; set; } =
                { "Shortcuts", "Shortcuts2", "Shortcuts3" };
            public int WideShortcutsSpan { get; set; }
            public string VisualizerMode { get; set; } = "None";
            public string VisualizerMode2 { get; set; } = "None";
            public string? VisualizerVideoPath { get; set; }
            public bool SleepComputer { get; set; }
            public bool ShowClock { get; set; } = true;
            public bool ShowSleep { get; set; }
            public bool ShowShutdown { get; set; }
            public bool ShowRestart { get; set; }
            public bool ShowObsRecord { get; set; }
            public bool ShowObsBuffer { get; set; }
            public bool ShowShortcutNames { get; set; } = true;
            public bool ShowModuleTitles { get; set; } = true;
            public bool ShowUsageOverlay { get; set; }
            public bool AutoDimElements { get; set; }
            public double AutoDimOpacityPercent { get; set; } = 70;
            public bool AutoDashHide { get; set; }
            public double WebListRowHeight { get; set; }=32;
            public PlaylistSettings? WebListAppearance {get;set;}
            public string? LastBrowserUrl {get;set;}
            public bool WebShortcutsImagesOnly { get; set; }
            public string? WebShortcutsViewMode {get;set;}
            public double WebShortcutsIconSize {get;set;}=48;
            public string? OpenWebShortcutUrl { get; set; }
            public int[]? BrowserPhysicalBounds {get;set;}
        }

        private enum ModuleKind
        {
            Music,
            Obs,
            Weather,
            System,
            Shortcuts
            ,Shortcuts2
            ,None
            ,Hue
            ,Visualizer
            ,Visualizer2
            ,WebShortcuts
            ,MusicPlayerCard
            ,AnalogVuCard
            ,MacroLibrary
            ,HudVisualizer
            ,Speaker
            ,Speaker2
            ,Playlist
            ,SystemWide, SystemFull
            ,Airthings
            ,Shortcuts3
        }

        private enum DockEdge
        {
            Top,
            Bottom
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct ProcessEntry32
        {
            public uint Size;
            public uint Usage;
            public uint ProcessId;
            public IntPtr DefaultHeapId;
            public uint ModuleId;
            public uint ThreadCount;
            public uint ParentProcessId;
            public int BasePriority;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string ExeFile;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect WorkArea;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PhysicalMonitor
        {
            public IntPtr Handle;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Description;
        }

        private enum VcpCodeType
        {
            Momentary,
            SetParameter
        }

        private enum MonitorColorTemperature
        {
            Unknown,
            Kelvin4000,
            Kelvin5000,
            Kelvin6500,
            Kelvin7500,
            Kelvin8200,
            Kelvin9300,
            Kelvin10000,
            Kelvin11500
        }

        [ComImport]
        [Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD")]
        private class DesktopWallpaperClass
        {
        }

        [ComImport]
        [Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDesktopWallpaper
        {
            [PreserveSig]
            int SetWallpaper(
                [MarshalAs(UnmanagedType.LPWStr)] string monitorId,
                [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);

            void GetWallpaper(
                [MarshalAs(UnmanagedType.LPWStr)] string monitorId,
                [MarshalAs(UnmanagedType.LPWStr)] out string wallpaper);

            void GetMonitorDevicePathAt(
                uint monitorIndex,
                [MarshalAs(UnmanagedType.LPWStr)] out string monitorId);

            void GetMonitorDevicePathCount(out uint count);

            void GetMonitorRECT(
                [MarshalAs(UnmanagedType.LPWStr)] string monitorId,
                out NativeRect displayRect);
        }
    }
}
















