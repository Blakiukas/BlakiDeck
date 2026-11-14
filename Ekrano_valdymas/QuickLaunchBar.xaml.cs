using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Ekrano_valdymas
{
    public partial class QuickLaunchBar : UserControl
    {
        internal static string LayoutPersistenceProbeDetails { get; private set; } = "not run";
        private const int SlotCount = 6;
        private const int TabRailUnits = 3;
        private const int LegacyTabOffset = 3;
        private static readonly Brush DefaultBorder = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255));
        private static Brush DashboardAccent =>
            Application.Current?.TryFindResource("AccentBrush") as Brush ??
            new SolidColorBrush(Color.FromRgb(30, 215, 96));
        private static Brush DashboardAccentBackground
        {
            get
            {
                Color color = DashboardAccent is SolidColorBrush brush
                    ? brush.Color : Color.FromRgb(30, 215, 96);
                return new SolidColorBrush(Color.FromArgb(80, color.R, color.G, color.B));
            }
        }
        private const uint ShgfiIcon = 0x000000100;
        private const uint ShgfiLargeIcon = 0x000000000;
        private const uint ShgfiUseFileAttributes = 0x000000010;
        private const uint FileAttributeNormal = 0x00000080;
        private const int HwndBroadcast = 0xFFFF;
        private const int WmSysCommand = 0x0112;
        private const int ScMonitorPower = 0xF170;
        private const int MonitorPowerOff = 2;

        private readonly ObservableCollection<QuickLaunchSlot> _slots = new();
        private readonly List<ContentControl> _slotPresenters = new();
        private readonly List<object> _layoutItems = new();
        private readonly Queue<QuickLaunchEntry?> _savedOverflow = new();
        private readonly string _settingsFile;
        private readonly string _layoutFile;
        private readonly string _activeTabFile;
        private int _activeTab;
        private Border? _pressedTab;
        private Point _pressedTabPoint;
        private Point _pressedWindowPoint;
        private bool _tabDragStarted;
        private TouchDevice? _tabTouchDevice;
        private readonly DispatcherTimer _shutdownConfirmationTimer;
        private readonly DispatcherTimer _sleepConfirmationTimer;
        private readonly DispatcherTimer _restartConfirmationTimer;
        private readonly DispatcherTimer _clockTimer;
        private readonly DispatcherTimer _obsDisplayTimer;
        private OBSWidget? _obsWidget;
        private int _shutdownConfirmationSeconds;
        private int _sleepConfirmationSeconds;
        private int _restartConfirmationSeconds;
        private Point _shortcutDragStart;
        private QuickLaunchSlot? _pressedShortcut;
        private bool _shortcutDragStarted;
        private Border? _touchDragSourceBorder;
        private Border? _touchDropBorder;
        private QuickLaunchSlot? _touchDropSlot;
        private ShortcutsWidget? _touchDropShortcutsOwner;
        private ShortcutsWidget.Slot? _touchDropShortcutsSlot;
        private string? _topBarControlDragKey;
        private Point _topBarControlDragStart;
        private bool _topBarControlDragMoved;
        private TouchDevice? _topBarControlTouchDevice;
        private Border? _topBarControlDragSource;
        private Border? _topBarLayoutDropBorder;
        private int _topBarDragInitialUnit;
        private int? _pendingTopBarDropUnit;
        private int? _pendingTopBarDropIndex;
        private bool _pendingTopBarFixedSwap;
        private ShortcutsWidget? _controlDropShortcutsOwner;
        private ShortcutsWidget.Slot? _controlDropShortcutsSlot;
        private readonly HashSet<string> _externalControlKeys = new();
        private readonly HashSet<string> _persistedExternalControlKeys = new();
        private bool _showShortcutNames = true;
        private Popup? _dragPreviewPopup;
        private long _compactModeSuppressedUntil;
        private bool _loadedTabUsesLegacyEightSlots;
        private bool _settingsLoadCompleted;
        private DispatcherOperation? _pendingActiveTabSave;
        private long _lastCompactToggleTick;

        public event EventHandler? CompactModeRequested;
        public event Action<int>? LayoutTabChanged;
        public int ActiveLayoutTab => _activeTab;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(
            string pszPath,
            uint dwFileAttributes,
            out ShellFileInfo psfi,
            uint cbFileInfo,
            uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ExtractIconEx(
            string fileName,
            int iconIndex,
            IntPtr[]? largeIcons,
            IntPtr[]? smallIcons,
            uint iconCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint PrivateExtractIcons(
            string fileName,
            int iconIndex,
            int iconWidth,
            int iconHeight,
            IntPtr[] icons,
            uint[] iconIds,
            uint iconCount,
            uint flags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(
            string path,
            IntPtr bindingContext,
            ref Guid interfaceId,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? imageFactory);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(
            IntPtr hWnd,
            int message,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern bool SetSuspendState(
            bool hibernate,
            bool forceCritical,
            bool disableWakeEvent);

        public bool SleepComputer { get; set; }
        public bool ShowClock { get; private set; } = true;
        public bool ShowSleep { get; private set; }
        public bool ShowShutdown { get; private set; }
        public bool ShowRestart { get; private set; }
        public bool ShowObsRecord { get; private set; }
        public bool ShowObsBuffer { get; private set; }

        public bool TrySetShowClock(bool value, out string message) =>
            TrySetControl(value, 2, ShowClock, v => ShowClock = v, ApplyClockVisibility, out message);
        public bool TrySetShowSleep(bool value, out string message) =>
            TrySetControl(value, 1, ShowSleep, v => ShowSleep = v, ApplySleepVisibility, out message);
        public bool TrySetShowShutdown(bool value, out string message) =>
            TrySetControl(value, 1, ShowShutdown, v => ShowShutdown = v, ApplyShutdownVisibility, out message);
        public bool TrySetShowRestart(bool value, out string message) =>
            TrySetControl(value, 1, ShowRestart, v => ShowRestart = v, ApplyRestartVisibility, out message);
        public bool TrySetShowObsRecord(bool value, out string message) =>
            TrySetControl(value, 1, ShowObsRecord, v => ShowObsRecord = v, ApplyObsRecordVisibility, out message);
        public bool TrySetShowObsBuffer(bool value, out string message) =>
            TrySetControl(value, 1, ShowObsBuffer, v => ShowObsBuffer = v, ApplyObsBufferVisibility, out message);

        private bool TrySetControl(bool value, int slotCount, bool currentValue,
            Action<bool> setValue, Action applyVisibility, out string message)
        {
            message = string.Empty;
            if (value == currentValue)
            {
                if (_settingsLoadCompleted && NormalizeShortcutSlotCount()) SaveShortcuts();
                return true;
            }

            setValue(value);
            applyVisibility();
            if(_settingsLoadCompleted&&value&&_layoutItems.Sum(GetLayoutItemUnitWidth)-NormalBackingUnitCapacity>_slots.Count(s=>s.IsEmpty))
            {
                setValue(currentValue);applyVisibility();RebuildTopBarLayout();
                message=$"Free {slotCount} shortcut cells first.";return false;
            }
            if (_settingsLoadCompleted)
                NormalizeShortcutSlotCount();
            RebuildTopBarLayout();
            SaveShortcuts();
            return true;
        }

        private void ApplyClockVisibility()
        {
            clockButton.Visibility = ShowClock ? Visibility.Visible : Visibility.Collapsed;
            RebuildTopBarLayout();
        }

        private void ApplySleepVisibility()
        {
            monitorSleep.Visibility = ShowSleep ? Visibility.Visible : Visibility.Collapsed;
            RebuildTopBarLayout();
        }

        private void ApplyShutdownVisibility()
        {
            shutdownButton.Visibility = ShowShutdown ? Visibility.Visible : Visibility.Collapsed;
            RebuildTopBarLayout();
        }
        private void ApplyRestartVisibility()
        {
            restartButton.Visibility = ShowRestart ? Visibility.Visible : Visibility.Collapsed;
            RebuildTopBarLayout();
        }
        private void ApplyObsRecordVisibility() { obsRecordButton.Visibility = ShowObsRecord ? Visibility.Visible : Visibility.Collapsed; UpdateObsControllerActivity(); RebuildTopBarLayout(); }
        private void ApplyObsBufferVisibility() { obsBufferButton.Visibility = ShowObsBuffer ? Visibility.Visible : Visibility.Collapsed; UpdateObsControllerActivity(); RebuildTopBarLayout(); }

        public bool ShowShortcutNames
        {
            set
            {
                _showShortcutNames = value;
                foreach (QuickLaunchSlot slot in _slots)
                    slot.ShowName = value;
            }
        }

        public QuickLaunchBar() : this(Path.Combine(
            DiagnosticState.LocalApplicationData,
            "Ekrano_valdymas"))
        {
        }

        internal QuickLaunchBar(string settingsFolder)
        {
            InitializeComponent();
            AttachConfirmationBlur();
            Loaded+=(_,_)=>RefreshMilitarySticker();

            _settingsFile = Path.Combine(settingsFolder, "quick-launch.json");
            _layoutFile = Path.Combine(Path.GetDirectoryName(_settingsFile)!, "top-bar-layout.json");
            _activeTabFile = Path.Combine(Path.GetDirectoryName(_settingsFile)!, "top-bar-active-tab.json");

            LoadConfigurableTabs(settingsFolder);
            InitializeEditableTabs(settingsFolder);
            RestoreNormalPoolClockSpan();
            LoadActiveTab();
            LoadPersistedExternalControls();
            LoadShortcuts();
            LoadTopBarLayout();
            slotsControl.ItemsSource = _slots;
            slotsControl.Visibility = Visibility.Collapsed;
            _slots.CollectionChanged += (_, _) => RebuildTopBarLayout();
            RebuildTopBarLayout();
            _shutdownConfirmationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _shutdownConfirmationTimer.Tick += ShutdownConfirmationTimer_Tick;
            _sleepConfirmationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _sleepConfirmationTimer.Tick += SleepConfirmationTimer_Tick;
            _restartConfirmationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _restartConfirmationTimer.Tick += RestartConfirmationTimer_Tick;
            _clockTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _clockTimer.Tick += (_, _) => UpdateClock();
            UpdateClock();
            _clockTimer.Start();
            _obsDisplayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _obsDisplayTimer.Tick += (_, _) => RefreshObsControlState();
            _obsDisplayTimer.Start();
            LanguageManager.Changed += LanguageManager_Changed;
            void RefreshMacroIcons(object? sender,EventArgs e){foreach(var slot in _slots)if(MacroLibrary.IsReference(slot.Target))slot.RefreshLanguage();}
            Loaded+=(_,_)=>{UpdateClock();_clockTimer.Start();_obsDisplayTimer.Start();MacroLibrary.Current.Changed+=RefreshMacroIcons;RefreshMacroIcons(this,EventArgs.Empty);};
            Unloaded += (_, _) =>
            {
                MacroLibrary.Current.Changed-=RefreshMacroIcons;
                _clockTimer.Stop();
                _obsDisplayTimer.Stop();
                _shutdownConfirmationTimer.Stop();
                _sleepConfirmationTimer.Stop();
                _restartConfirmationTimer.Stop();
                LanguageManager.Changed -= LanguageManager_Changed;
            };
        }

        private void RebuildTopBarLayout()
        {
            if (_suspendTopBarRebuild || topBarGrid == null || slotsControl == null)
                return;

            foreach (ContentControl presenter in _slotPresenters)
                topBarGrid.Children.Remove(presenter);
            _slotPresenters.Clear();
            topBarGrid.ColumnDefinitions.Clear();
            clockButton.Visibility = Visibility.Collapsed;
            monitorSleep.Visibility = Visibility.Collapsed;
            shutdownButton.Visibility = Visibility.Collapsed;
            restartButton.Visibility = Visibility.Collapsed;
            obsRecordButton.Visibility = Visibility.Collapsed;
            obsBufferButton.Visibility = Visibility.Collapsed;

            ReconcileLayoutItems();
            NormalizeAirthingsMiniSpans();
            int units = TabRailUnits + Math.Max(0, _layoutItems.Sum(GetLayoutItemUnitWidth) - HiddenNormalUnits) + 1;
            compactToggle.Margin=new Thickness(3,0,5,0);
            // Shortcut cells use the same reference pitch as the twelve-column wide grid.
            double pitch=(1900-43)/12;
            for (int i = 0; i < units; i++)
                topBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width =
                    i<TabRailUnits?new GridLength(1,GridUnitType.Star):
                    new GridLength(pitch) });

            Grid.SetColumn(tabRail, 0);
            Grid.SetColumnSpan(tabRail, TabRailUnits);
            Grid.SetColumnSpan(catalogBase,units);
            UpdateTabVisuals();
            int column = TabRailUnits;
            int normalUnit = 0;
            foreach (object item in _layoutItems)
            {
                int requestedSpan = GetLayoutItemUnitWidth(item);
                int effectiveSpan = Math.Max(0, normalUnit + requestedSpan - Math.Max(normalUnit, HiddenNormalUnits));
                normalUnit += requestedSpan;
                if (effectiveSpan == 0) continue;
                if (item is QuickLaunchSlot slot)
                {
                    if(slot.IsAirthingsReserved)continue;
                    ContentControl presenter = new() { Content = slot, ContentTemplate = slotsControl.ItemTemplate };
                    Grid.SetColumn(presenter, column);Grid.SetColumnSpan(presenter,effectiveSpan);column+=effectiveSpan;
                    topBarGrid.Children.Add(presenter);
                    _slotPresenters.Add(presenter);
                }
                else if (item is string key)
                {
                    if (key == "clock") PlaceTopBarControl(clockButton, ShowClock, ref column, effectiveSpan);
                    else if (key == "sleep") PlaceTopBarControl(monitorSleep, ShowSleep, ref column, 1);
                    else if (key == "shutdown") PlaceTopBarControl(shutdownButton, ShowShutdown, ref column, 1);
                    else if (key == "restart") PlaceTopBarControl(restartButton, ShowRestart, ref column, 1);
                    else if (key == "obs-record") PlaceTopBarControl(obsRecordButton, ShowObsRecord, ref column, 1);
                    else if (key == "obs-buffer") PlaceTopBarControl(obsBufferButton, ShowObsBuffer, ref column, 1);
                }
            }
            PlaceTopBarControl(compactToggle, true, ref column, 1);
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(ApplyBarMaterial));
            if (_settingsLoadCompleted)
                SaveTopBarLayout();
        }

        private void ReconcileLayoutItems()
        {
            _layoutItems.RemoveAll(item => item is QuickLaunchSlot slot && !_slots.Contains(slot));
            foreach (QuickLaunchSlot slot in _slots)
                if (!_layoutItems.Contains(slot)) _layoutItems.Add(slot);
            ReconcileControl("clock", ShowClock);
            ReconcileControl("sleep", ShowSleep);
            ReconcileControl("shutdown", ShowShutdown);
            ReconcileControl("restart", ShowRestart);
            ReconcileControl("obs-record", ShowObsRecord);
            ReconcileControl("obs-buffer", ShowObsBuffer);
        }

        private void ReconcileControl(string key, bool visible)
        {
            bool hostedOutsideTopBar = _externalControlKeys.Contains(key) ||
                                       _persistedExternalControlKeys.Contains(key);
            if (visible && !hostedOutsideTopBar && !_layoutItems.Contains(key)) _layoutItems.Add(key);
            if (_settingsLoadCompleted && !visible) _layoutItems.Remove(key);
        }

        private void LoadPersistedExternalControls()
        {
            string folder = Path.GetDirectoryName(_settingsFile)!;
            var storageFiles = Enumerable.Range(0, MaximumLayoutTabs)
                .SelectMany(tab => TabShortcutStorage.Kinds
                    .Select(kind => Path.Combine(folder, TabShortcutStorage.Key(tab, kind) + ".json"))).ToList();
            // Once migration has completed, legacy families are recovery copies,
            // not owners. Include hidden tabs before any widget is instantiated.
            if (!File.Exists(Path.Combine(folder, "tab-shortcuts-migration-v2.json")))
                storageFiles.AddRange(new[] { "module-shortcuts.json", "module-shortcuts-2.json", "module-shortcuts-wide.json" }
                    .Select(name => Path.Combine(folder, name)));

            foreach (string storageFile in storageFiles)
            {
                try
                {
                    if (!File.Exists(storageFile)) continue;
                    string?[] entries = JsonSerializer.Deserialize<string?[]>(
                        File.ReadAllText(storageFile)) ?? Array.Empty<string?>();
                    foreach (string entry in entries.OfType<string>())
                    {
                        if (!entry.StartsWith("control:", StringComparison.Ordinal)) continue;
                        string key = entry[8..];
                        if(key=="clock"&&(_loadedNormalPool?.Order.Contains("clock") ??
                            (File.Exists(_layoutFile)&&(JsonSerializer.Deserialize<string[]>(File.ReadAllText(_layoutFile))?.Contains("clock")??false))))continue;
                        if (key is "clock" or "sleep" or "shutdown" or "restart" or
                            "obs-record" or "obs-buffer")
                            _persistedExternalControlKeys.Add(key);
                    }
                }
                catch
                {
                    // Vienas sugadintas modulio failas neturi trukdyti atkurti kitÅ³ valdikliÅ³.
                }
            }
        }

        internal void CompleteSettingsLoad()
        {
            if (_settingsLoadCompleted) return;
            foreach (string key in _persistedExternalControlKeys)
                _externalControlKeys.Add(key);
            _settingsLoadCompleted = true;
            ReconcileLayoutItems();
            if (_loadedNormalPool?.FactoryLayout == true)
            {
                _layoutItems.Remove("clock");
                _layoutItems.Add("clock");
                clockAppearance.Cells = 2;
                clockAppearance.Apply(clockText);
            }
            NormalizeShortcutSlotCount();
            ExpandNormalPool();
            RebuildTopBarLayout();
            SaveShortcuts();
        }

        internal void FreezeDynamicContentForGeometryProbe()
        {
            _clockTimer.Stop();
            _obsDisplayTimer.Stop();
            clockText.Text = "00:00";
        }

        private void LoadTopBarLayout()
        {
            if (LoadNormalPoolOrder()) return;
            try
            {
                if (!File.Exists(_layoutFile)) return;
                string[]? tokens = JsonSerializer.Deserialize<string[]>(File.ReadAllText(_layoutFile));
                if (tokens == null) return;
                foreach (string token in tokens)
                {
                    if (token.StartsWith("slot:") && int.TryParse(token[5..], out int index))
                    {
                        int resolvedIndex = _loadedTabUsesLegacyEightSlots ? index - LegacyTabOffset : index;
                        if (resolvedIndex >= 0 && resolvedIndex < _slots.Count)
                            _layoutItems.Add(_slots[resolvedIndex]);
                    }
                    else if (token is "clock" or "sleep" or "shutdown" or "restart" or "obs-record" or "obs-buffer") _layoutItems.Add(token);
                }
            }
            catch { _layoutItems.Clear(); }
        }

        private void SaveTopBarLayout()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_layoutFile)!);
                string[] tokens = _layoutItems.Select(item => item is QuickLaunchSlot slot
                    ? $"slot:{_slots.IndexOf(slot)}" : (string)item).ToArray();
                File.WriteAllText(_layoutFile, JsonSerializer.Serialize(tokens));
                SaveNormalPool();
            }
            catch { }
        }

        internal void SavePersistentLayoutState()
        {
            _pendingActiveTabSave?.Abort();
            _pendingActiveTabSave = null;
            SaveActiveTab();
            SaveTopBarLayout();
            SaveShortcuts();
        }

        private void SaveActiveTab()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_activeTabFile)!);
                File.WriteAllText(_activeTabFile, _activeTab.ToString());
            }
            catch { }
        }

        internal static bool VerifyLayoutPersistence()
        {
            string folder = Path.Combine(Path.GetTempPath(), $"BlakiDeck-top-bar-{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "quick-launch.json"),
                    "[null,null,null,null,null]");
                File.WriteAllText(Path.Combine(folder, "quick-launch.json.three-tabs-migrated"), "1");
                string[] initial =
                {
                    "clock", "slot:0", "slot:1", "sleep", "slot:2",
                    "shutdown", "slot:3", "slot:4"
                };
                File.WriteAllText(Path.Combine(folder, "top-bar-layout.json"),
                    JsonSerializer.Serialize(initial));

                QuickLaunchBar first = new(folder); first.TrySetShowSleep(false,out _);first.TrySetShowShutdown(false,out _);
                first.CompleteSettingsLoad();
                first.MoveLayoutItemForProbe("clock", 6);
                first.SavePersistentLayoutState();
                string saved = File.ReadAllText(Path.Combine(folder, "top-bar-layout.json"));

                QuickLaunchBar second = new(folder); second.TrySetShowSleep(false,out _);second.TrySetShowShutdown(false,out _);
                second.CompleteSettingsLoad();
                second.SavePersistentLayoutState();
                string reloaded = File.ReadAllText(Path.Combine(folder, "top-bar-layout.json"));
                string[] actual = JsonSerializer.Deserialize<string[]>(reloaded) ?? Array.Empty<string>();
                string[] expected =
                {
                    "slot:0", "slot:1", "sleep", "slot:2", "shutdown",
                    "slot:3", "clock", "slot:4"
                };
                expected=new[]{"slot:0","slot:1","slot:2","slot:3","clock"}; bool baseResult = saved == reloaded && actual.SequenceEqual(expected);

                string obsFolder = Path.Combine(folder, "obs-scenario");
                Directory.CreateDirectory(obsFolder);
                File.WriteAllText(Path.Combine(obsFolder, "quick-launch.json"),
                    "[null,null,null,null,null]");
                File.WriteAllText(Path.Combine(obsFolder, "quick-launch.json.three-tabs-migrated"), "1");
                string?[] wideControls = new string?[12];
                wideControls[6] = "control:shutdown";
                wideControls[7] = "control:sleep";
                File.WriteAllText(Path.Combine(obsFolder, "module-shortcuts-wide.json"),
                    JsonSerializer.Serialize(wideControls));
                string[] obsExpected =
                {
                    "slot:0", "clock", "obs-record", "obs-buffer",
                    "slot:3", "slot:4", "slot:1", "slot:2"
                };
                File.WriteAllText(Path.Combine(obsFolder, "top-bar-layout.json"),
                    JsonSerializer.Serialize(obsExpected));

                QuickLaunchBar obsFirst = new(obsFolder);
                obsFirst.TrySetShowObsRecord(true, out _);
                obsFirst.TrySetShowObsBuffer(true, out _);
                obsFirst.CompleteSettingsLoad();
                obsFirst.SavePersistentLayoutState();
                string obsSaved = File.ReadAllText(Path.Combine(obsFolder, "top-bar-layout.json"));

                QuickLaunchBar obsSecond = new(obsFolder);
                obsSecond.TrySetShowObsRecord(true, out _);
                obsSecond.TrySetShowObsBuffer(true, out _);
                obsSecond.CompleteSettingsLoad();
                obsSecond.SavePersistentLayoutState();
                string obsReloaded = File.ReadAllText(Path.Combine(obsFolder, "top-bar-layout.json"));
                string[] obsActual = JsonSerializer.Deserialize<string[]>(obsReloaded) ?? Array.Empty<string>();
                bool obsResult = obsSaved == obsReloaded && obsActual.SequenceEqual(new[]{"slot:0","clock","obs-record","obs-buffer","slot:1"});
                string legacyFolder=Path.Combine(folder,"legacy-eight-with-overflow");Directory.CreateDirectory(legacyFolder);
                File.WriteAllText(Path.Combine(legacyFolder,"quick-launch.json"),JsonSerializer.Serialize(
                    Enumerable.Range(0,10).Select(i=>new QuickLaunchEntry($"C:/BlakiDeck-probe/legacy-{i}.txt",$"legacy-{i}"))));
                File.WriteAllText(Path.Combine(legacyFolder,"top-bar-layout.json"),JsonSerializer.Serialize(new[]{"slot:3","slot:4","clock","slot:5","slot:6","slot:7"}));
                var legacy=new QuickLaunchBar(legacyFolder);legacy.CompleteSettingsLoad();legacy.SwitchTopBarTab(4);legacy.SavePersistentLayoutState();
                var migrated=new QuickLaunchBar(legacyFolder);migrated.CompleteSettingsLoad();
                bool migrationResult=migrated.ActiveLayoutTab==4&&migrated.editableTabBorders.Length==5&&
                    migrated._slots.Select(s=>s.DisplayName).Concat(migrated._savedOverflow.Select(s=>s?.DisplayName)).SequenceEqual(Enumerable.Range(3,7).Select(i=>$"legacy-{i}"))&&
                    migrated._layoutItems.Sum(migrated.GetLayoutItemUnitWidth)==SlotCount;
                bool result = baseResult && obsResult && migrationResult;
                LayoutPersistenceProbeDetails =
                    $"baseSaved={saved}; baseReloaded={reloaded}; expected={JsonSerializer.Serialize(expected)}; " +
                    $"obsSaved={obsSaved}; obsReloaded={obsReloaded}; legacyMigration={migrationResult}";
                return result;
            }
            finally
            {
                try { Directory.Delete(folder, true); } catch { }
            }
        }

        private void MoveLayoutItemForProbe(string key, int insertionIndex)
        {
            int sourceIndex = _layoutItems.IndexOf(key);
            if (sourceIndex < 0) throw new InvalidOperationException($"Missing top-bar item: {key}");
            _layoutItems.RemoveAt(sourceIndex);
            _layoutItems.Insert(Math.Clamp(insertionIndex, 0, _layoutItems.Count), key);
            RebuildTopBarLayout();
            SaveTopBarLayout();
        }

        private static void PlaceTopBarControl(FrameworkElement control, bool visible, ref int column, int span)
        {
            control.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (!visible) return;
            Grid.SetColumn(control, column);
            Grid.SetColumnSpan(control, span);
            column += span;
        }

        private string? GetControlKey(object sender) => sender is FrameworkElement { Tag: string taggedKey }
            ? taggedKey
            : sender == clockButton ? "clock" :
            sender == monitorSleep ? "sleep" : sender == shutdownButton ? "shutdown" :
            sender == restartButton ? "restart" : sender == obsRecordButton ? "obs-record" :
            sender == obsBufferButton ? "obs-buffer" : null;
        

        private void BeginTopBarControlDrag(object sender, Point point)
        {
            _topBarControlDragKey = GetControlKey(sender);
            _topBarControlDragSource = sender as Border;
            _topBarControlDragStart = point;
            _topBarControlDragMoved = false;
            // Retain requested backing coordinates; projection changes only rendering.
            _topBarDragInitialUnit = _layoutItems.TakeWhile(item=>!Equals(item,_topBarControlDragKey)).Sum(GetLayoutItemUnitWidth);
            _pendingTopBarDropUnit = null;
            _pendingTopBarDropIndex = null;
            _pendingTopBarFixedSwap = false;
            ClearTopBarLayoutDropVisual();
        }

        private Point lastControlDragPoint;
        private void UpdateTopBarControlDrag(Point point)
        {
            lastControlDragPoint=point;
            if (_topBarControlDragKey == null) return;
            if (!_topBarControlDragMoved && Math.Abs(point.X - _topBarControlDragStart.X) < 8 && Math.Abs(point.Y - _topBarControlDragStart.Y) < 8) return;
            if (_topBarDragInitialUnit < HiddenNormalUnits)
            {
                // A tap still activates a partial control; a drag must not move its hidden cells.
                _topBarControlDragMoved = true;
                _pendingTopBarDropUnit = null;
                _pendingTopBarDropIndex = null;
                _pendingTopBarFixedSwap = false;
                ClearTopBarLayoutDropVisual();
                return;
            }
            if (!_topBarControlDragMoved)
            {
                _topBarControlDragMoved = true;
                if (_topBarControlDragSource != null)
                {
                    ShowDragPreview(_topBarControlDragSource, point);
                    ApplyDragSourceVisual(_topBarControlDragSource, true);
                }
            }
            UpdateDragPreviewPosition(point);
            _controlDropShortcutsOwner?.ClearExternalControlDropTarget();
            _controlDropShortcutsOwner = null;
            _controlDropShortcutsSlot = null;
            TouchDropTarget? external = FindQuickLaunchSlotAt(point);
            if (external?.ShortcutsOwner != null && external.ShortcutsSlot is { } externalSlot &&
                external.ShortcutsOwner.TryResolveControlDropTarget(
                    external.Border, externalSlot, _topBarControlDragKey,
                    out Border resolvedBorder, out ShortcutsWidget.Slot resolvedSlot, ControlSlotCount(_topBarControlDragKey)) &&
                external.ShortcutsOwner.CanAcceptControl(resolvedSlot, _topBarControlDragKey,ControlSlotCount(_topBarControlDragKey)))
            {
                ClearTopBarLayoutDropVisual();
                _controlDropShortcutsOwner = external.ShortcutsOwner;
                _controlDropShortcutsSlot = resolvedSlot;
                _pendingTopBarDropUnit = null;
                _pendingTopBarDropIndex = null;
                _pendingTopBarFixedSwap = false;
                external.ShortcutsOwner.ShowExternalControlDropTarget(
                    resolvedBorder,
                    resolvedSlot,
                    _topBarControlDragKey,ControlSlotCount(_topBarControlDragKey));
                return;
            }
            ClearTopBarLayoutDropVisual();
            _controlDropShortcutsOwner = null;
            _controlDropShortcutsSlot = null;
            double localCellWidth = GetTopBarCellWidth();
            if (localCellWidth <= 0) return;

            int deltaUnits = TopBarLayoutPlanner.ResolveUnitDelta(
                point.X - _topBarControlDragStart.X,
                localCellWidth);
            int draggedSpan = ControlSlotCount(_topBarControlDragKey);
            int maxStartUnit = Math.Min(NormalBackingUnitCapacity - draggedSpan,
                _layoutItems.Where(item => !Equals(item, _topBarControlDragKey)).Sum(GetLayoutItemUnitWidth));
            if (maxStartUnit < HiddenNormalUnits)
            {
                _pendingTopBarDropUnit = null;
                _pendingTopBarDropIndex = null;
                _pendingTopBarFixedSwap = false;
                return;
            }
            _pendingTopBarDropUnit = Math.Clamp(
                _topBarDragInitialUnit + deltaUnits,
                HiddenNormalUnits,
                maxStartUnit);

            _pendingTopBarFixedSwap = TopBarLayoutPlanner.CanMoveFixedBlock(
                AtomicMiniLayout(),
                GetLayoutItemUnitWidth,
                _topBarControlDragKey,
                _pendingTopBarDropUnit.Value);
            if (_pendingTopBarFixedSwap)
            {
                _pendingTopBarDropIndex = null;
                Grid.SetColumn(topBarDropOverlay, _pendingTopBarDropUnit.Value - HiddenNormalUnits + TabRailUnits);
                Grid.SetColumnSpan(topBarDropOverlay, draggedSpan);
                topBarDropOverlay.Visibility = Visibility.Visible;
                return;
            }

            List<int> remainingWidths = AtomicMiniLayout()
                .Where(item => !Equals(item, _topBarControlDragKey))
                .Select(GetLayoutItemUnitWidth)
                .ToList();
            int insertionIndex = TopBarLayoutPlanner.ResolveInsertionIndexForUnit(
                remainingWidths,
                _pendingTopBarDropUnit.Value);
            int snappedStartUnit = TopBarLayoutPlanner.GetStartUnitForInsertionIndex(
                remainingWidths,
                insertionIndex);
            while (snappedStartUnit < HiddenNormalUnits && insertionIndex < remainingWidths.Count)
            {
                snappedStartUnit += remainingWidths[insertionIndex];
                insertionIndex++;
            }
            if (snappedStartUnit < HiddenNormalUnits || snappedStartUnit > maxStartUnit)
            {
                _pendingTopBarDropUnit = null;
                _pendingTopBarDropIndex = null;
                return;
            }
            _pendingTopBarDropIndex = insertionIndex;
            _pendingTopBarDropUnit = snappedStartUnit;
            Grid.SetColumn(topBarDropOverlay, snappedStartUnit - HiddenNormalUnits + TabRailUnits);
            Grid.SetColumnSpan(topBarDropOverlay, draggedSpan);
            topBarDropOverlay.Visibility = Visibility.Visible;
        }

        private void ClearTopBarLayoutDropVisual()
        {
            _controlDropShortcutsOwner?.ClearExternalControlDropTarget();
            if (_topBarLayoutDropBorder != null)
            {
                _topBarLayoutDropBorder.BorderBrush = DefaultBorder;
                _topBarLayoutDropBorder.SetResourceReference(Border.BackgroundProperty, "PanelMetalBrush");
            }
            _topBarLayoutDropBorder = null;
            topBarDropOverlay.Visibility = Visibility.Collapsed;
        }

        private FrameworkElement? GetLayoutElement(object item) => item is QuickLaunchSlot slot
            ? _slotPresenters.FirstOrDefault(p => ReferenceEquals(p.Content, slot))
            : (item as string) switch { "clock" => clockButton, "sleep" => monitorSleep, "shutdown" => shutdownButton, "restart" => restartButton, "obs-record" => obsRecordButton, "obs-buffer" => obsBufferButton, _ => null };

        private static T? FindVisualChild<T>(DependencyObject? root) where T : DependencyObject
        {
            if (root == null) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) return match;
                T? nested = FindVisualChild<T>(child);
                if (nested != null) return nested;
            }
            return null;
        }

        private void TopBarControl_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            InteractionDiagnostics.Log(
                $"TopBar mouse-down key={GetControlKey(sender)} stylus={e.StylusDevice != null} " +
                $"source={e.OriginalSource?.GetType().Name}");
            if (e.StylusDevice != null) return;
            SuppressWindowLayoutChanges();
            BeginTopBarControlDrag(sender, e.GetPosition(topBarGrid));
            ((UIElement)sender).CaptureMouse(); e.Handled = true;
        }

        private void QuickLaunchBar_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.StylusDevice != null || _topBarControlTouchDevice != null)
                e.Handled = true;
        }
        private void TopBarControl_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || e.StylusDevice != null) return;
            UpdateTopBarControlDrag(e.GetPosition(topBarGrid)); e.Handled = true;
        }
        private void TopBarControl_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.StylusDevice != null) return;
            ((UIElement)sender).ReleaseMouseCapture();
            string? key = _topBarControlDragKey; bool moved = _topBarControlDragMoved;
            InteractionDiagnostics.Log($"TopBar mouse-up key={key} moved={moved}");
            CommitTopBarControlDrop(key, moved);
            ResetTopBarControlDragVisuals(); _topBarControlDragKey = null; e.Handled = true;
            if (!moved) ActivateTopBarControl(key, sender as FrameworkElement);
        }
        private void TopBarControl_PreviewTouchDown(object sender, TouchEventArgs e)
        {
            InteractionDiagnostics.Log(
                $"TopBar touch-down key={GetControlKey(sender)} device={e.TouchDevice.Id}");
            SuppressWindowLayoutChanges();
            BeginTopBarControlDrag(sender, e.GetTouchPoint(topBarGrid).Position);
            _topBarControlTouchDevice = e.TouchDevice; ((UIElement)sender).CaptureTouch(e.TouchDevice); e.Handled = true;
        }
        private void TopBarControl_PreviewTouchMove(object sender, TouchEventArgs e)
        {
            if (!ReferenceEquals(_topBarControlTouchDevice, e.TouchDevice)) return;
            UpdateTopBarControlDrag(e.GetTouchPoint(topBarGrid).Position); e.Handled = true;
        }
        private void TopBarControl_PreviewTouchUp(object sender, TouchEventArgs e)
        {
            if (!ReferenceEquals(_topBarControlTouchDevice, e.TouchDevice)) return;
            ((UIElement)sender).ReleaseTouchCapture(e.TouchDevice);
            string? key = _topBarControlDragKey; bool moved = _topBarControlDragMoved;
            InteractionDiagnostics.Log(
                $"TopBar touch-up key={key} moved={moved} device={e.TouchDevice.Id}");
            CommitTopBarControlDrop(key, moved);
            ResetTopBarControlDragVisuals(); _topBarControlTouchDevice = null; _topBarControlDragKey = null; e.Handled = true;
            if (!moved) ActivateTopBarControl(key, sender as FrameworkElement);
        }

        private void ResetTopBarControlDragVisuals()
        {
            if (_topBarControlDragSource != null) ApplyDragSourceVisual(_topBarControlDragSource, false);
            _topBarControlDragSource = null;
            ClearTopBarLayoutDropVisual();
            CloseDragPreview();
            _controlDropShortcutsOwner = null;
            _controlDropShortcutsSlot = null;
            _pendingTopBarDropUnit = null;
            _pendingTopBarDropIndex = null;
            _pendingTopBarFixedSwap = false;
        }

        private void CommitTopBarControlDrop(string? key, bool moved)
        {
            if (!moved || key == null) return;
            if(key=="clock"&&Window.GetWindow(this) is {} window&&SystemMonitorWidget.DropClock(window,topBarGrid.TranslatePoint(lastControlDragPoint,window),clockAppearance))
            {
                _externalControlKeys.Add(key);_layoutItems.Remove(key);NormalizeShortcutSlotCount();RebuildTopBarLayout();SaveTopBarLayout();SaveShortcuts();return;
            }
            if (!_layoutItems.Contains(key) ||
                _layoutItems.TakeWhile(item => !Equals(item, key)).Sum(GetLayoutItemUnitWidth) < HiddenNormalUnits)
                return;
            if (_controlDropShortcutsOwner != null && _controlDropShortcutsSlot != null)
            {
                CommitExternalControlDrop(key, true);
                return;
            }
            if (_pendingTopBarDropUnit == null) return;
            if (_pendingTopBarDropUnit.Value < HiddenNormalUnits ||
                _pendingTopBarDropUnit.Value + ControlSlotCount(key) > NormalBackingUnitCapacity)
                return;

            if (_pendingTopBarFixedSwap)
            {
                List<object> swappedLayout = TopBarLayoutPlanner.MoveFixedBlock(
                    AtomicMiniLayout(),
                    GetLayoutItemUnitWidth,
                    key,
                    _pendingTopBarDropUnit.Value);
                ApplyAtomicMiniLayout(swappedLayout);
                RebuildTopBarLayout();
                SaveTopBarLayout();
                return;
            }

            if (_pendingTopBarDropIndex == null) return;

            var atomicLayout=AtomicMiniLayout();int sourceIndex = atomicLayout.IndexOf(key);
            if (sourceIndex < 0) return;
            atomicLayout.RemoveAt(sourceIndex);
            int insertionIndex = Math.Clamp(_pendingTopBarDropIndex.Value, 0, atomicLayout.Count);
            int insertionUnit = atomicLayout.Take(insertionIndex).Sum(GetLayoutItemUnitWidth);
            if (insertionUnit < HiddenNormalUnits || insertionUnit + ControlSlotCount(key) > NormalBackingUnitCapacity)
                return;
            atomicLayout.Insert(insertionIndex, key);ApplyAtomicMiniLayout(atomicLayout);
            RebuildTopBarLayout();
            SaveTopBarLayout();
        }

        private double GetTopBarCellWidth() => topBarGrid.ColumnDefinitions.Count > TabRailUnits+1
            ? topBarGrid.ColumnDefinitions[TabRailUnits].ActualWidth : 0;

        private int FindInsertionIndexForUnit(int desiredUnit)
        {
            return TopBarLayoutPlanner.ResolveInsertionIndexForUnit(
                _layoutItems.Select(GetLayoutItemUnitWidth).ToList(),
                desiredUnit);
        }

        private int GetLayoutItemUnitWidth(object item) =>
            item is QuickLaunchSlot slot?slot.IsAirthingsReserved?0:AirthingsMini.Span(slot.Target):item is string key && key == "clock" ? clockAppearance.Span : 1;

        private void CommitExternalControlDrop(string? key, bool moved)
        {
            if (!moved || key == null || _controlDropShortcutsOwner == null || _controlDropShortcutsSlot == null) return;
            if (!_controlDropShortcutsOwner.SetControl(_controlDropShortcutsSlot, key,key=="clock"?clockAppearance:null)) return;
            _externalControlKeys.Add(key);
            _layoutItems.Remove(key);
            NormalizeShortcutSlotCount();
            RebuildTopBarLayout();
            SaveTopBarLayout();
            SaveShortcuts();
        }

        internal void RegisterExternalControls(IEnumerable<string> keys)
        {
        using var callbackTiming = new UiCallbackTiming("RegisterExternalControls");
            bool changed=false;
            foreach (string key in keys)
            {
                if(key=="clock"&&_layoutItems.Contains("clock"))continue;
                changed|=_externalControlKeys.Add(key);
                changed|=_layoutItems.Remove(key);
            }
            // Loading a tab's shortcut widget can report an empty/already known
            // control set. Preserve the shared presenters in that case.
            if(!changed)return;
            if(!_settingsLoadCompleted){RebuildTopBarLayout();return;}
            NormalizeShortcutSlotCount();
            RebuildTopBarLayout();
            SaveTopBarLayout();
            SaveShortcuts();
        }

        internal bool RestoreExternalControl(string key, double? dropX = null,ClockAppearance? appearance=null)
        {
            if(key=="clock")
            {
                var destination=dropX.HasValue?GetEmptySlotAt(dropX.Value):_slots.FirstOrDefault(s=>s.IsEmpty);
                return destination!=null&&AddLibraryClock(destination,appearance);
            }
            int insertionIndex = dropX.HasValue
                ? GetLayoutInsertionIndex(dropX.Value)
                : _layoutItems.Count;
            _externalControlKeys.Remove(key);
            _layoutItems.Remove(key);
            insertionIndex = Math.Clamp(insertionIndex, 0, _layoutItems.Count);
            _layoutItems.Insert(insertionIndex, key);
            NormalizeShortcutSlotCount();
            RebuildTopBarLayout();
            SaveShortcuts();
            return true;
        }

        private int GetLayoutInsertionIndex(double x)
        {
            for (int index = 0; index < _layoutItems.Count; index++)
            {
                FrameworkElement? element = GetLayoutElement(_layoutItems[index]);
                if (element?.IsVisible != true) continue;
                Point origin = element.TranslatePoint(new Point(), topBarGrid);
                if (x < origin.X + element.ActualWidth / 2) return index;
            }
            return _layoutItems.Count;
        }

        private QuickLaunchSlot? GetEmptySlotAt(double x)
        {
            foreach (object item in _layoutItems)
            {
                if (item is not QuickLaunchSlot { IsEmpty: true } slot) continue;
                FrameworkElement? element = GetLayoutElement(slot);
                if (element?.IsVisible != true) continue;
                Point origin = element.TranslatePoint(new Point(), topBarGrid);
                if (x >= origin.X && x <= origin.X + element.ActualWidth) return slot;
            }
            return null;
        }

        private int ControlSlotCount(string key) => key == "clock" ? clockAppearance.Span : 1;

        private void AddShortcutSlots(int count)
        {
            for (int added = 0; added < count; added++)
            {
                QuickLaunchEntry? entry = _savedOverflow.Count > 0 ? _savedOverflow.Dequeue() : null;
                QuickLaunchSlot slot = new(_slots.Count, entry?.Target, entry?.DisplayName)
                {
                    ShowName = _showShortcutNames, IconPath=entry?.IconPath, Presentation=entry?.Presentation
                };
                _slots.Add(slot);
            }
        }

        private bool NormalizeShortcutSlotCount()
        {
            int expectedCount = Math.Max(0,NormalBackingUnitCapacity-_layoutItems.OfType<string>().Sum(ControlSlotCount));

            bool changed = false;
            while (_slots.Count > expectedCount)
            {
                QuickLaunchSlot? extraEmptySlot = _slots.LastOrDefault(slot => slot.IsEmpty);
                if (extraEmptySlot == null)
                {
                    extraEmptySlot=_slots.Last();
                    var retained=_savedOverflow.ToArray();_savedOverflow.Clear();_savedOverflow.Enqueue(new QuickLaunchEntry(extraEmptySlot.Target!,extraEmptySlot.DisplayName,extraEmptySlot.Presentation,extraEmptySlot.IconPath));foreach(var entry in retained)_savedOverflow.Enqueue(entry);
                }
                _layoutItems.Remove(extraEmptySlot);
                _slots.Remove(extraEmptySlot);
                changed = true;
            }
            while (_slots.Count < expectedCount)
            {
                AddShortcutSlots(1);
                changed = true;
            }
            return changed;
        }

        private static void ApplyDragSourceVisual(Border border, bool dragging)
        {
            border.Opacity = 1;
            border.BorderBrush = dragging ? DashboardAccent : DefaultBorder;
        }

        private void ShowDragPreview(Border source, Point point)
        {
            CloseDragPreview();var owner=Window.GetWindow(this);
            _dragPreviewPopup=ShortcutDragSupport.CreateAnchoredPreview(source,topBarGrid.TranslatePoint(point,owner));
            UpdateDragPreviewPosition(point);_dragPreviewPopup.IsOpen=true;
        }
        private void UpdateDragPreviewPosition(Point point)
        {
            if(_dragPreviewPopup!=null)ShortcutDragSupport.MoveAnchoredPreview(_dragPreviewPopup,topBarGrid.TranslatePoint(point,Window.GetWindow(this)));
        }
        private void CloseDragPreview()
        {
            if (_dragPreviewPopup != null) _dragPreviewPopup.IsOpen = false;
            _dragPreviewPopup = null;
        }

        private void ActivateTopBarControl(string? key, FrameworkElement? activationSource = null)
        {
            InteractionDiagnostics.Log(
                $"ActivateTopBarControl key={key} source={activationSource?.Name ?? activationSource?.GetType().Name ?? "null"}");
            if (key == "obs-record")
            {
                if (_obsWidget != null) _ = _obsWidget.ToggleRecordingAsync();
                return;
            }
            if (key == "obs-buffer")
            {
                if (_obsWidget != null) _ = _obsWidget.SaveReplayBufferAsync();
                return;
            }
            if (key is not ("sleep" or "shutdown" or "restart")) return;
            ResetAllConfirmations();

            if (key == "sleep")
            {
                _sleepConfirmationSeconds = 6;
                sleepYesCountdownText.Text = sleepNoCountdownText.Text = "6 s";
                SetConfirmationPlacement(sleepConfirmationPopup, activationSource); sleepConfirmationPopup.IsOpen = true;
                _sleepConfirmationTimer.Stop(); _sleepConfirmationTimer.Start();
            }
            else if (key == "shutdown")
            {
                _shutdownConfirmationSeconds = 6;
                shutdownCountdownText.Text = shutdownNoCountdownText.Text = "6 s";
                SetConfirmationPlacement(shutdownConfirmationPopup, activationSource); shutdownConfirmationPopup.IsOpen = true;
                shutdownButton.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 74, 80));
                _shutdownConfirmationTimer.Stop(); _shutdownConfirmationTimer.Start();
            }
            else if (key == "restart")
            {
                _restartConfirmationSeconds = 6;
                restartYesCountdownText.Text = restartNoCountdownText.Text = "6 s";
                SetConfirmationPlacement(restartConfirmationPopup, activationSource); restartConfirmationPopup.IsOpen = true;
                _restartConfirmationTimer.Stop(); _restartConfirmationTimer.Start();
            }
        }

        internal void PrepareExternalControlActivation() => SuppressWindowLayoutChanges();

        internal void ActivateExternalControl(string key, FrameworkElement activationSource)
        {
            SuppressWindowLayoutChanges();
            ActivateTopBarControl(key, activationSource);
        }

        internal void AttachObsWidget(OBSWidget widget)
        {
            if (_obsWidget != null)
            {
                _obsWidget.ControlStateChanged -= ObsWidget_ControlStateChanged;
                _obsWidget.SetExternalControlsActive(false);
            }
            _obsWidget = widget;
            _obsWidget.ControlStateChanged += ObsWidget_ControlStateChanged;
            UpdateObsControllerActivity();
            RefreshObsControlState();
        }

        private void UpdateObsControllerActivity() =>
            _obsWidget?.SetExternalControlsActive(ShowObsRecord || ShowObsBuffer);

        private void ObsWidget_ControlStateChanged(object? sender, EventArgs e) => RefreshObsControlState();

        internal (bool ObsRunning, bool Recording, bool BufferActive, string ElapsedText) GetObsControlState()
        {
            bool recording = _obsWidget?.IsRecording == true;
            TimeSpan elapsed = recording ? _obsWidget!.RecordingElapsed : TimeSpan.Zero;
            return (_obsWidget?.IsObsApplicationRunning == true, recording,
                _obsWidget?.IsReplayBufferActive == true, elapsed.ToString(@"hh\:mm\:ss"));
        }

        private void RefreshObsControlState()
        {
            if (obsRecordContent == null || obsBufferContent == null) return;
            foreach (var slot in _slots) if (MacroLibrary.IsReference(slot.Target)) slot.RefreshLanguage();
            var state = GetObsControlState();
            obsRecordContent.Recording = state.Recording;
            obsRecordContent.StatusActive = state.ObsRunning;
            obsRecordContent.ElapsedText = state.ElapsedText;
            obsBufferContent.StatusActive = state.BufferActive;
        }

        private void SuppressWindowLayoutChanges()
        {
            _compactModeSuppressedUntil = Environment.TickCount64 + 1500;
            (Window.GetWindow(this) as MainWindow)?.SuppressResizeAfterInteractiveControl();
        }

        private void UpdateClock()
        {
            string next=DateTime.Now.ToString("t",CultureInfo.CurrentCulture);
            if(clockText.Text!=next)clockText.Text=next;
            if(gameModeIndicator&&gameElapsed.HasValue)RefreshGameElapsed();
        }

        internal void RefreshClockColorResource()
        {
            clockText.TextBrush=clockAppearance.Brush;
            clockText.InvalidateVisual();
        }

        private void LanguageManager_Changed(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                foreach (QuickLaunchSlot slot in _slots)
                    slot.RefreshLanguage();
            });
        }

        private void EditMacro_Click(object sender, RoutedEventArgs e) { if(sender is MenuItem {CommandParameter:QuickLaunchSlot slot} && MacroLibrary.Current.Find(slot.Target) is {} macro) MacroEditor.Show(Window.GetWindow(this),macro); e.Handled=true; }

        private void SensorContextMenu_Opening(object sender,ContextMenuEventArgs e)
        {
            if(sender is Border {DataContext:QuickLaunchSlot {IsSensor:true}} border&&FindSensorTile(border) is {} tile){e.Handled=true;Dispatcher.BeginInvoke(new Action(()=>tile.OpenOptions()));}
        }

        private void ShortcutContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu { DataContext: QuickLaunchSlot slot } menu) return;
            foreach (MenuItem item in menu.Items.OfType<MenuItem>())
                { item.Visibility = (item.Header?.ToString()=="Edit macro" && !MacroLibrary.IsReference(slot.Target) || item.Header?.ToString()=="Edit" && MacroLibrary.IsReference(slot.Target)) ? Visibility.Collapsed : Visibility.Visible; item.IsEnabled = !slot.IsEmpty && (item.Header?.ToString() != "Properties" || ShortcutShellActions.IsFileTarget(slot.Target)); }
        }

        internal void RefreshMilitarySticker()
        {
            militaryRadiationSticker.Visibility=!gameModeIndicator&&!CommonMetal.NoSkin&&CommonMetal.FinishName=="militarity"?Visibility.Visible:Visibility.Collapsed;
        }

        private void CompactToggle_PreviewMouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            e.Handled = true;
            RequestCompactToggle();
        }

        private void CompactToggle_PreviewStylusDown(object sender, StylusDownEventArgs e)
        {
            e.Handled = true;
            RequestCompactToggle();
        }

        private void CompactToggle_PreviewTouchDown(object sender, TouchEventArgs e)
        {
            e.Handled = true;
            RequestCompactToggle();
        }

        private void RequestCompactToggle()
        {
            long now = Environment.TickCount64;
            if (now - _lastCompactToggleTick < 500) return;
            _lastCompactToggleTick = now;
            CompactModeRequested?.Invoke(this, EventArgs.Empty);
        }

        private void CompactToggle_PreviewTouchUp(object sender, TouchEventArgs e)
        {
            e.Handled = true;
        }

        private void MonitorSleep_PreviewMouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            e.Handled = true;
            ResetAllConfirmations();
            _sleepConfirmationSeconds = 6;
            sleepYesCountdownText.Text = "6 s";
            sleepNoCountdownText.Text = "6 s";
            SetConfirmationPlacement(sleepConfirmationPopup); sleepConfirmationPopup.IsOpen = true;
            _sleepConfirmationTimer.Stop();
            _sleepConfirmationTimer.Start();
        }

        private void SleepYes_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            ResetSleepConfirmation();
            if (SleepComputer)
                SetSuspendState(false, false, false);
            else
                SendMessage(new IntPtr(HwndBroadcast), WmSysCommand,
                    new IntPtr(ScMonitorPower), new IntPtr(MonitorPowerOff));
        }

        private void SleepNo_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            ResetSleepConfirmation();
        }

        private void SleepConfirmationTimer_Tick(object? sender, EventArgs e)
        {
            _sleepConfirmationSeconds--;
            if (_sleepConfirmationSeconds <= 0) { ResetSleepConfirmation(); return; }
            sleepYesCountdownText.Text = $"{_sleepConfirmationSeconds} s";
            sleepNoCountdownText.Text = $"{_sleepConfirmationSeconds} s";
        }

        private void ResetSleepConfirmation()
        {
            _sleepConfirmationTimer.Stop();
            sleepConfirmationPopup.IsOpen = false;
        }

        private void ShutdownButton_PreviewMouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            e.Handled = true;
            ResetAllConfirmations();
            _shutdownConfirmationSeconds = 6;
            shutdownCountdownText.Text = "6 s";
            shutdownNoCountdownText.Text = "6 s";
            SetConfirmationPlacement(shutdownConfirmationPopup); shutdownConfirmationPopup.IsOpen = true;
            shutdownButton.BorderBrush =
                new SolidColorBrush(Color.FromRgb(255, 74, 80));
            _shutdownConfirmationTimer.Start();
        }

        private void ShutdownNo_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            ResetShutdownConfirmation();
        }

        private void ShutdownConfirmation_PreviewMouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            e.Handled = true;
            _shutdownConfirmationTimer.Stop();
            Process.Start(new ProcessStartInfo
            {
                FileName = "shutdown.exe",
                Arguments = "/s /t 0",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }

        private void ShutdownConfirmationTimer_Tick(
            object? sender,
            EventArgs e)
        {
            _shutdownConfirmationSeconds--;

            if (_shutdownConfirmationSeconds <= 0)
            {
                ResetShutdownConfirmation();
                return;
            }

            shutdownCountdownText.Text = $"{_shutdownConfirmationSeconds} s";
            shutdownNoCountdownText.Text = $"{_shutdownConfirmationSeconds} s";
        }

        private void ResetShutdownConfirmation()
        {
            _shutdownConfirmationTimer.Stop();
            shutdownConfirmationPopup.IsOpen = false;
            shutdownButton.BorderBrush = DefaultBorder;
        }
        private void RestartButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true; ResetAllConfirmations(); _restartConfirmationSeconds = 6;
            restartYesCountdownText.Text = restartNoCountdownText.Text = "6 s";
            SetConfirmationPlacement(restartConfirmationPopup); restartConfirmationPopup.IsOpen = true;
            _restartConfirmationTimer.Stop(); _restartConfirmationTimer.Start();
        }
        private void RestartYes_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true; ResetRestartConfirmation();
            Process.Start(new ProcessStartInfo { FileName = "shutdown.exe", Arguments = "/r /t 0", UseShellExecute = false, CreateNoWindow = true });
        }
        private void RestartNo_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) { e.Handled = true; ResetRestartConfirmation(); }
        private void RestartConfirmationTimer_Tick(object? sender, EventArgs e)
        {
            if (--_restartConfirmationSeconds <= 0) { ResetRestartConfirmation(); return; }
            restartYesCountdownText.Text = restartNoCountdownText.Text = $"{_restartConfirmationSeconds} s";
        }
        private void ResetRestartConfirmation() { _restartConfirmationTimer.Stop(); restartConfirmationPopup.IsOpen = false; }
        private void ResetAllConfirmations()
        {
            ResetSleepConfirmation();
            ResetShutdownConfirmation();
            ResetRestartConfirmation();
        }

        private void SetConfirmationPlacement(Popup popup, FrameworkElement? activationSource = null)
        {
            FrameworkElement anchor = activationSource ?? (popup == sleepConfirmationPopup ? monitorSleep :
                popup == shutdownConfirmationPopup ? shutdownButton : restartButton);
            popup.PlacementTarget = anchor;
            Window? window = Window.GetWindow(this);
            bool up = window is MainWindow mainWindow
                ? mainWindow.ShouldOpenTopBarPopupUpward()
                : false;
            popup.Placement = up ? PlacementMode.Top : PlacementMode.Bottom;
            popup.VerticalOffset = up ? -8 : 8;
            double popupWidth = (popup.Child as FrameworkElement)?.Width ?? 0;
            popup.HorizontalOffset = popupWidth > 0 ? (anchor.ActualWidth - popupWidth) / 2 : 0;
            InteractionDiagnostics.Log(
                $"Confirmation popup={popup.Name} anchor={anchor.Name} " +
                $"anchorSize={anchor.ActualWidth:0.###},{anchor.ActualHeight:0.###} " +
                $"placement={popup.Placement} offsets={popup.HorizontalOffset:0.###},{popup.VerticalOffset:0.###}");
        }

        public void SetCompactMode(bool compact)
        {
            compactIconState=compact;
            RefreshResourceModeIcon();
            compactToggle.ToolTip = compact ? "Restore" : "Minimize";
        }

        private void LoadActiveTab()
        {
            try
            {
                if (File.Exists(_activeTabFile) &&
                    int.TryParse(File.ReadAllText(_activeTabFile), out int savedTab))
                    _activeTab = Math.Clamp(savedTab, 0, TabCount - 1);
            }
            catch { _activeTab = 0; }
        }

        private void TopBarTab_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.StylusDevice != null) return;
            _pressedTab = sender as Border;
            _pressedTabPoint = e.GetPosition(Window.GetWindow(this));
            _tabDragStarted = false;
            _pressedTab?.CaptureMouse();
            e.Handled = true;
        }

        private void TopBarTab_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_pressedTab == null || e.LeftButton != MouseButtonState.Pressed || _tabDragStarted)
                return;
            if (Window.GetWindow(this) is MainWindow { AreTopBarTabsDragBlocked: true })
            {
                e.Handled = true;
                return;
            }
            Point current = e.GetPosition(Window.GetWindow(this));
            if (Math.Abs(current.X - _pressedTabPoint.X) < 6 &&
                Math.Abs(current.Y - _pressedTabPoint.Y) < 6)
                return;

            _tabDragStarted = true;
            _pressedTab.ReleaseMouseCapture();
            try
            {
                SuppressWindowLayoutChanges();
                Window? window = Window.GetWindow(this);
                (window as MainWindow)?.BeginLinkedWindowMove();
                window?.DragMove();
                (window as MainWindow)?.CompleteLinkedWindowMove();
            }
            catch (InvalidOperationException) { }
            finally { _pressedTab = null; }
            e.Handled = true;
        }

        private void TopBarTab_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            Border? tabBorder = _pressedTab;
            tabBorder?.ReleaseMouseCapture();
            _pressedTab = null;
            if (!_tabDragStarted && tabBorder?.Tag is string value && int.TryParse(value, out int tab))
                SwitchTopBarTab(tab);
            else if (_tabDragStarted)
                (Window.GetWindow(this) as MainWindow)?.CompleteLinkedWindowMove();
            _tabDragStarted = false;
            e.Handled = true;
        }

        private void TopBarTab_PreviewTouchDown(object sender, TouchEventArgs e)
        {
            if (_tabTouchDevice != null) return;
            _pressedTab = sender as Border;
            _tabTouchDevice = e.TouchDevice;
            Window? window = Window.GetWindow(this);
            _pressedTabPoint = GetTouchScreenDip(e);
            _pressedWindowPoint = window == null ? default : new Point(window.Left, window.Top);
            _tabDragStarted = false;
            if (window is not MainWindow { AreTopBarTabsDragBlocked: true })
                (window as MainWindow)?.BeginLinkedWindowMove();
            e.TouchDevice.Capture(_pressedTab);
            e.Handled = true;
        }

        private void TopBarTab_PreviewTouchMove(object sender, TouchEventArgs e)
        {
            if (!ReferenceEquals(_tabTouchDevice, e.TouchDevice)) return;
            Window? window = Window.GetWindow(this);
            if (window == null) return;
            if (window is MainWindow { AreTopBarTabsDragBlocked: true })
            {
                e.Handled = true;
                return;
            }
            Point current = GetTouchScreenDip(e);
            double deltaX = current.X - _pressedTabPoint.X;
            double deltaY = current.Y - _pressedTabPoint.Y;
            if (!_tabDragStarted && Math.Abs(deltaX) < 8 && Math.Abs(deltaY) < 8)
            {
                e.Handled = true;
                return;
            }
            _tabDragStarted = true;
            SuppressWindowLayoutChanges();
            window.Left = _pressedWindowPoint.X + deltaX;
            window.Top = _pressedWindowPoint.Y + deltaY;
            e.Handled = true;
        }

        private Point GetTouchScreenDip(TouchEventArgs e)
        {
            Point screenPixels = PointToScreen(e.GetTouchPoint(this).Position);
            Matrix fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
                ?? Matrix.Identity;
            return fromDevice.Transform(screenPixels);
        }

        private void TopBarTab_PreviewTouchUp(object sender, TouchEventArgs e)
        {
            if (!ReferenceEquals(_tabTouchDevice, e.TouchDevice)) return;
            e.TouchDevice.Capture(null);
            Border? tabBorder = _pressedTab;
            _pressedTab = null;
            _tabTouchDevice = null;
            if (!_tabDragStarted && tabBorder?.Tag is string value && int.TryParse(value, out int tab))
                SwitchTopBarTab(tab);
            else if (_tabDragStarted)
                (Window.GetWindow(this) as MainWindow)?.CompleteLinkedWindowMove();
            _tabDragStarted = false;
            e.Handled = true;
        }

        private void SwitchTopBarTab(int requestedTab)
        {
            Stopwatch timing = Stopwatch.StartNew();
            requestedTab = Math.Clamp(requestedTab, 0, TabCount - 1);
            if (requestedTab == _activeTab)
            {
                CompactModeRequested?.Invoke(this, EventArgs.Empty);
                return;
            }
            SuppressWindowLayoutChanges();
            _activeTab = requestedTab;
            UpdateTabVisuals();
            InteractionDiagnostics.Log(
                $"TabSwitch request tab={requestedTab + 1} localMs={timing.Elapsed.TotalMilliseconds:0.###}");
            LayoutTabChanged?.Invoke(_activeTab);
            InteractionDiagnostics.Log(
                $"TabSwitch returned tab={requestedTab + 1} totalMs={timing.Elapsed.TotalMilliseconds:0.###}");
            _pendingActiveTabSave?.Abort();
            _pendingActiveTabSave = Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() =>
                {
                    _pendingActiveTabSave = null;
                    SaveActiveTab();
                }));
        }

        private void UpdateTabVisuals()
        {
            Border[] tabs = editableTabBorders;
            OpticallyCenteredText[] labels = editableTabLabels;
            Color surface=(TryFindResource("ModuleCardSurfaceBrush") as SolidColorBrush)?.Color??Color.FromRgb(27,23,20);
            catalogBase.Background=new SolidColorBrush(CatalogRailFace.Lighten(surface,.17));
            catalogFace.Surface=surface;catalogFace.TabCount=TabCount;catalogFace.Selected=_activeTab;ApplyBarMaterial();
            for (int index = 0; index < tabs.Length; index++)
            {
                tabs[index].Visibility = index < TabCount ? Visibility.Visible : Visibility.Collapsed;
                tabRailGrid.ColumnDefinitions[index].Width = index < TabCount ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
                if (index >= TabCount) continue;
                bool selected = index == _activeTab;
                tabs[index].Background=Brushes.Transparent;
                tabs[index].BorderBrush=Brushes.Transparent;
                tabs[index].BorderThickness=selected?new Thickness(2):new Thickness(1.5);
                tabs[index].Opacity=MusicFinish.IsLight||selected?1:.52;
                labels[index].TextBrush=Brushes.White;
                RenderEditableTab(index);
            }
        }

        internal void RefreshTabVisuals() => UpdateTabVisuals();

        private void LoadShortcuts()
        {
            if (LoadNormalPoolEntries()) return;
            QuickLaunchEntry?[] savedEntries = Array.Empty<QuickLaunchEntry?>();
            _loadedTabUsesLegacyEightSlots = false;
            string? migrationMarker = null;

            try
            {
                string settingsFile = _settingsFile;
                if (File.Exists(settingsFile))
                {
                    savedEntries =
                        JsonSerializer.Deserialize<QuickLaunchEntry?[]>(
                            File.ReadAllText(settingsFile)) ??
                        Array.Empty<QuickLaunchEntry?>();
                    migrationMarker = settingsFile + ".three-tabs-migrated";
                    if (!File.Exists(migrationMarker) && savedEntries.Length >= LegacyTabOffset)
                    {
                        savedEntries = savedEntries.Skip(LegacyTabOffset).ToArray();
                        _loadedTabUsesLegacyEightSlots = true;
                    }
                }
            }
            catch
            {
                // Sugadintas nustatymÅ³ failas neturi trukdyti paleisti dashboard.
            }

            int loadedCount=File.Exists(Path.Combine(Path.GetDirectoryName(_settingsFile)!,"power-macros-v1.json"))?Math.Max(SlotCount,savedEntries.Length):SlotCount;
            for (int index = 0; index < loadedCount; index++)
            {
                QuickLaunchEntry? entry = index < savedEntries.Length
                    ? savedEntries[index]
                    : null;

                _slots.Add(new QuickLaunchSlot(index, entry?.Target, entry?.DisplayName) { IconPath=entry?.IconPath, Presentation=entry?.Presentation });
            }

            for (int index = loadedCount; index < savedEntries.Length; index++)
                _savedOverflow.Enqueue(savedEntries[index]);

            if (_loadedTabUsesLegacyEightSlots && migrationMarker != null)
            {
                SaveShortcuts();
                File.WriteAllText(migrationMarker, "1");
            }
        }

        private void SaveShortcuts(bool throwOnError = false)
        {
            if(NormalizeAirthingsMiniSpans())RebuildTopBarLayout();
            try
            {
                string settingsFile = _settingsFile;
                string? folder = Path.GetDirectoryName(settingsFile);
                if (folder != null)
                    Directory.CreateDirectory(folder);

                QuickLaunchEntry?[] entries = _slots
                    .Select(slot => slot.IsEmpty
                        ? null
                        : new QuickLaunchEntry(slot.Target!, slot.SavedDisplayName, slot.Presentation,slot.IconPath))
                    .Concat(_savedOverflow)
                    .ToArray();

                File.WriteAllText(settingsFile, JsonSerializer.Serialize(entries));
                SaveNormalPool();
            }
            catch(Exception ex)
            {
                if(throwOnError)throw;
                InteractionDiagnostics.RecordFailure("Save shortcuts",ex);
                // Nuoroda veiks Å¡io seanso metu net jei nepavyko jos iÅ¡saugoti.
            }
        }

        internal void SaveTouchShortcuts() => SaveShortcuts();

        internal bool AddLibraryClock(QuickLaunchSlot destination,ClockAppearance? appearance=null)
        {
            if(_layoutItems.Contains("clock"))return false;
            var nextAppearance=appearance??clockAppearance;int span=nextAppearance.Span;
            int index=_layoutItems.IndexOf(destination);
            if(index<0)return false;
            if(span==2&&index==_layoutItems.Count-1)index--;
            if(index<0||_layoutItems[index] is not QuickLaunchSlot {IsEmpty:true} first)return false;
            if(span==2){if(_layoutItems[index+1] is not QuickLaunchSlot {IsEmpty:true} second)return false;_layoutItems.RemoveAt(index+1);_slots.Remove(second);}
            _layoutItems.RemoveAt(index);_slots.Remove(first);
            _layoutItems.Insert(index,"clock");_externalControlKeys.Remove("clock");_persistedExternalControlKeys.Remove("clock");ShowClock=true;
            clockAppearance=nextAppearance.Clone();clockAppearance.Apply(clockText);AtomicJson.Write(clockAppearanceFile,clockAppearance);
            RebuildTopBarLayout();SaveShortcuts();return true;
        }

        private static SensorTile? FindSensorTile(DependencyObject node)
        {
            if(node is SensorTile tile)return tile;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)if(FindSensorTile(VisualTreeHelper.GetChild(node,i)) is {} found)return found;
            return null;
        }

        private void Slot_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border { DataContext: QuickLaunchSlot slot })
                return;

            if(slot.IsSensor&&e.ClickCount==2){FindSensorTile((DependencyObject)sender)?.ResetZoom();e.Handled=true;return;}
            _pressedShortcut = slot;
            _shortcutDragStart = e.GetPosition(this);
            _shortcutDragStarted = false;
            ((UIElement)sender).CaptureMouse();
            e.Handled = true;
        }

        private void Slot_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_pressedShortcut is null || _shortcutDragStarted || e.LeftButton != MouseButtonState.Pressed)
                return;

            if (_pressedShortcut.IsEmpty)
            {
                e.Handled = true;
                return;
            }

            Point current = e.GetPosition(this);
            if (Math.Abs(current.X - _shortcutDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(current.Y - _shortcutDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            _shortcutDragStarted = true;
            QuickLaunchSlot source = _pressedShortcut;
            _pressedShortcut = null;
            ShortcutTransfer transfer = CreateShortcutTransfer(source);
            ShortcutDragSupport.BeginDrag(
                (FrameworkElement)sender,
                source.Icon,
                source.DisplayName,
                transfer);
            _pressedShortcut = null;
            e.Handled = true;
        }

        internal ShortcutTransfer CreateShortcutTransfer(QuickLaunchSlot source)
        {
            string target=source.Target!;string? presentation=source.Presentation,icon=source.IconPath;
            return new ShortcutTransfer(
                target,source.DisplayName,presentation,icon,
                (replacementTarget, replacementName, replacementPresentation,replacementIcon) =>
                {
                    source.SetTarget(replacementTarget, replacementName);
                    source.Presentation=replacementPresentation;source.IconPath=replacementIcon;
                    SaveShortcuts();
                }){SourceLocation="top:"+source.Index,SourceOwner=this,SourceSlot=source,
                    SourceIsCurrent=()=>_slots.Contains(source)&&source.Target==target&&source.Presentation==presentation&&source.IconPath==icon};
        }

        private void Slot_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            QuickLaunchSlot? slot = _pressedShortcut;
            _pressedShortcut = null;

            if(sender is UIElement element && element.IsMouseCaptured)element.ReleaseMouseCapture();

            if (slot is null || _shortcutDragStarted)
                return;

            e.Handled = true;
            if (slot.IsEmpty)
            {
                ChooseShortcut(slot);
                return;
            }

            if (slot.IsSensor) return;
            if(UsageMini.IsTarget(slot.Target)||AirthingsMini.TryActivate(slot.Target,Window.GetWindow(this)))return;
                if (MacroRunner.TryActivate(slot.Target, Window.GetWindow(this))) return;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = ShellTargetPath.Resolve(slot.Target!),
                    UseShellExecute = true
                });
            }
            catch
            {
                slot.MarkUnavailable();
            }
        }

        private void Slot_PreviewTouchDown(object sender, TouchEventArgs e)
        {
            if (sender is not Border { DataContext: QuickLaunchSlot slot } border)
                return;

            _pressedShortcut = slot;
            _shortcutDragStart = e.GetTouchPoint(this).Position;
            _shortcutDragStarted = false;
            _touchDragSourceBorder = border;
            ClearTouchDropTarget();
            border.CaptureTouch(e.TouchDevice);
            e.Handled = true;
        }

        private void Slot_PreviewTouchMove(object sender, TouchEventArgs e)
        {
            if (_pressedShortcut is null || sender is not Border border)
                return;

            if (_pressedShortcut.IsEmpty)
            {
                e.Handled = true;
                return;
            }

            Point current = e.GetTouchPoint(this).Position;
            if (!_shortcutDragStarted &&
                Math.Abs(current.X - _shortcutDragStart.X) < 12 &&
                Math.Abs(current.Y - _shortcutDragStart.Y) < 12)
            {
                e.Handled = true;
                return;
            }

            if (!_shortcutDragStarted)
            {
                var source=_pressedShortcut;_pressedShortcut=null;_shortcutDragStarted=true;
                if(ShortcutDragSupport.BeginTouchDrag(border,e.TouchDevice,e.GetTouchPoint(Window.GetWindow(this)).Position,CreateShortcutTransfer(source),ResetTouchDragVisuals))
                {e.Handled=true;return;}
                _pressedShortcut=source;_shortcutDragStarted=false;
                _shortcutDragStarted = true;
                ShowDragPreview(border, current);
                ApplyDragSourceVisual(border, true);
            }
            UpdateDragPreviewPosition(current);

            SetTouchDropTarget(FindQuickLaunchSlotAt(current));
            e.Handled = true;
        }

        private void Slot_PreviewTouchUp(object sender, TouchEventArgs e)
        {
            QuickLaunchSlot? slot = _pressedShortcut;
            _pressedShortcut = null;
            if (sender is Border border)
                border.ReleaseTouchCapture(e.TouchDevice);

            e.Handled = true;
            if (slot is null)
            {
                ResetTouchDragVisuals();
                return;
            }

            if (_shortcutDragStarted)
            {
                if(slot.IsAirthingsMini)
                {
                    if(_touchDropShortcutsOwner!=null&&_touchDropShortcutsSlot!=null)MiniPlacement.Place(_touchDropShortcutsOwner,_touchDropShortcutsSlot,AirthingsMini.Decode(slot.Target),this,slot);else if(_touchDropSlot!=null)MiniPlacement.Place(this,_touchDropSlot,AirthingsMini.Decode(slot.Target),this,slot);ResetTouchDragVisuals();return;
                }
                if(_touchDropSlot?.IsAirthingsMini==true||_touchDropSlot?.IsAirthingsReserved==true||_touchDropShortcutsSlot?.IsAirthingsMini==true||_touchDropShortcutsSlot?.IsReserved==true){ResetTouchDragVisuals();return;}
                if (_touchDropShortcutsOwner != null && _touchDropShortcutsSlot != null)
                {
                    string? destinationTarget = _touchDropShortcutsSlot.Target;
                    string destinationName = _touchDropShortcutsSlot.Name;
                    string? destinationPresentation=_touchDropShortcutsSlot.Presentation;string? destinationIconPath=_touchDropShortcutsSlot.IconPath;
                    string? sourcePresentation=slot.Presentation;string? sourceIconPath=slot.IconPath;
                    SensorCellPreferences.Exchange("top:"+slot.Index,slot.Target,_touchDropShortcutsOwner.SensorLocation(_touchDropShortcutsSlot),destinationTarget);
                    _touchDropShortcutsSlot.SetTarget(slot.Target, slot.DisplayName);
                    _touchDropShortcutsSlot.Presentation=sourcePresentation;_touchDropShortcutsSlot.IconPath=sourceIconPath;
                    slot.SetTarget(destinationTarget, destinationName);
                    slot.Presentation=destinationPresentation;slot.IconPath=destinationIconPath;
                    SensorCellPreferences.NotifyPlacement();
                    _touchDropShortcutsOwner.SaveTouchShortcuts();
                    SaveShortcuts();
                }
                else if (_touchDropSlot != null && !ReferenceEquals(slot, _touchDropSlot))
                {
                    string? destinationTarget = _touchDropSlot.Target;
                    string destinationName = _touchDropSlot.DisplayName;
                    string? destinationPresentation=_touchDropSlot.Presentation;string? destinationIconPath=_touchDropSlot.IconPath;
                    string? sourcePresentation=slot.Presentation;string? sourceIconPath=slot.IconPath;
                    SensorCellPreferences.Exchange("top:"+slot.Index,slot.Target,"top:"+_touchDropSlot.Index,destinationTarget);
                    _touchDropSlot.SetTarget(slot.Target, slot.DisplayName);
                    _touchDropSlot.Presentation=sourcePresentation;_touchDropSlot.IconPath=sourceIconPath;
                    slot.SetTarget(destinationTarget, destinationName);
                    slot.Presentation=destinationPresentation;slot.IconPath=destinationIconPath;
                    SensorCellPreferences.NotifyPlacement();
                    SaveShortcuts();
                }

                ResetTouchDragVisuals();
                return;
            }

            ResetTouchDragVisuals();

            if (slot.IsEmpty)
            {
                ChooseShortcut(slot);
                return;
            }

            try
            {
                if (slot.IsSensor) return;
                if(UsageMini.IsTarget(slot.Target)||AirthingsMini.TryActivate(slot.Target,Window.GetWindow(this)))return;
                if (MacroRunner.TryActivate(slot.Target, Window.GetWindow(this))) return;
                Process.Start(new ProcessStartInfo { FileName = ShellTargetPath.Resolve(slot.Target!), UseShellExecute = true });
            }
            catch { slot.MarkUnavailable(); }
        }

        private void ChooseShortcut(QuickLaunchSlot slot)
        {
            OpenFileDialog dialog = new()
            {
                Title = "Choose shortcut",
                Filter = "Programs and files|*.exe;*.lnk;*.bat;*.cmd;*.*|All files|*.*"
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            slot.SetTarget(dialog.FileName, Path.GetFileNameWithoutExtension(dialog.FileName));
            SaveShortcuts();
        }

        private TouchDropTarget? FindQuickLaunchSlotAt(Point point)
        {
            Window? window = Window.GetWindow(this);
            if (window == null) return null;

            Point windowPoint = TranslatePoint(point, window);
            for (DependencyObject? current = window.InputHitTest(windowPoint) as DependencyObject;
                 current != null;
                 current = VisualTreeHelper.GetParent(current))
            {
                if (current is Border { DataContext: QuickLaunchSlot slot } target)
                    return new TouchDropTarget(target, slot, null, null);
                if (current is Border { DataContext: ShortcutsWidget.Slot shortcutSlot } shortcutTarget)
                {
                    ShortcutsWidget? owner = FindVisualAncestor<ShortcutsWidget>(shortcutTarget);
                    if (owner != null)
                        return new TouchDropTarget(shortcutTarget, null, owner, shortcutSlot);
                }
            }

            return null;
        }

        private void SetTouchDropTarget(TouchDropTarget? target)
        {
            if (ReferenceEquals(_touchDropBorder, target?.Border)) return;
            ClearTouchDropTarget();
            if (target == null) return;

            _touchDropBorder = target.Border;
            _touchDropSlot = target.QuickSlot;
            _touchDropShortcutsOwner = target.ShortcutsOwner;
            _touchDropShortcutsSlot = target.ShortcutsSlot;
            _touchDropBorder.BorderBrush = DashboardAccent;
            _touchDropBorder.Background = DashboardAccentBackground;
        }

        private void ClearTouchDropTarget()
        {
            if (_touchDropBorder != null)
            {
                _touchDropBorder.BorderBrush = DefaultBorder;
                if (_touchDropShortcutsSlot != null)
                    _touchDropBorder.Background = Brushes.Transparent;
                else
                    _touchDropBorder.SetResourceReference(
                        Border.BackgroundProperty,
                        "PanelMetalBrush");
            }

            _touchDropBorder = null;
            _touchDropSlot = null;
            _touchDropShortcutsOwner = null;
            _touchDropShortcutsSlot = null;
        }

        private void ResetTouchDragVisuals()
        {
            if (_touchDragSourceBorder != null)
                ApplyDragSourceVisual(_touchDragSourceBorder, false);
            _touchDragSourceBorder = null;
            ClearTouchDropTarget();
            CloseDragPreview();
            _shortcutDragStarted = false;
        }

        private static T? FindVisualAncestor<T>(DependencyObject start) where T : DependencyObject
        {
            for (DependencyObject? current = start; current != null; current = VisualTreeHelper.GetParent(current))
                if (current is T match) return match;
            return null;
        }

        private sealed record TouchDropTarget(
            Border Border,
            QuickLaunchSlot? QuickSlot,
            ShortcutsWidget? ShortcutsOwner,
            ShortcutsWidget.Slot? ShortcutsSlot);

        private void Slot_DragEnter(object sender, DragEventArgs e)
        {
            bool internalMove = e.Data.GetDataPresent(ShortcutDragSupport.Format);
            if (sender is not Border { DataContext: QuickLaunchSlot slot } border) return;
            var transfer=internalMove?ShortcutDragSupport.ResolveTransfer(e.Data):null;
            bool protectedCell=slot.IsAirthingsMini||slot.IsAirthingsReserved;
            bool supported=internalMove
                ? transfer?.CanMoveTo(slot.Target)==true&&transfer.FitsTarget(this,slot)&&
                  (transfer.Target==MacroLibraryCard.ClockTarget||AirthingsMini.IsTarget(transfer.Target)||!protectedCell)
                : !protectedCell&&TryGetDroppedTarget(e.Data,out _,out _);
            if(!supported)
            {
                Slot_DragLeave(sender,e);e.Effects=DragDropEffects.None;e.Handled=true;return;
            }
            border.BorderBrush = DashboardAccent;
            border.Background = DashboardAccentBackground;
            e.Effects = internalMove ? DragDropEffects.Move : DragDropEffects.Link;
            e.Handled = true;
        }

        private void Slot_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is Border border)
            {
                border.BorderBrush = DefaultBorder;
                border.SetResourceReference(
                    Border.BackgroundProperty,
                    "PanelMetalBrush");
            }
        }

        private void Slot_Drop(object sender, DragEventArgs e)
        {
            if (sender is not Border { DataContext: QuickLaunchSlot slot } border)
                return;

            border.BorderBrush = DefaultBorder;
            border.SetResourceReference(
                Border.BackgroundProperty,
                "PanelMetalBrush");

            if (e.Data.GetDataPresent(ShortcutDragSupport.Format) &&
                ShortcutDragSupport.ResolveTransfer(e.Data) is ShortcutTransfer transfer)
            {                
                e.Effects = AcceptShortcutTransfer(slot,transfer)?DragDropEffects.Move:DragDropEffects.None;
                e.Handled = true;
                return;
            }

            if(e.Data.GetDataPresent(ShortcutDragSupport.Format))
            { e.Effects=DragDropEffects.None;e.Handled=true;return; }
            if (!TryGetDroppedTarget(e.Data, out string? target, out string? displayName))
                return;
            if(slot.IsAirthingsMini||slot.IsAirthingsReserved){e.Effects=DragDropEffects.None;e.Handled=true;return;}

            slot.SetTarget(target!, displayName!);
            SaveShortcuts();
            e.Handled = true;
        }

        internal bool AcceptShortcutTransfer(QuickLaunchSlot slot,ShortcutTransfer transfer)
        {
            if(!_slots.Contains(slot)||!transfer.CanMoveTo(slot.Target)||!transfer.FitsTarget(this,slot))return false;
            if(transfer.Target==MacroLibraryCard.ClockTarget)
            {
                if(!slot.IsEmpty||!AddLibraryClock(slot,MonitorClockContent.Decode(transfer.Presentation)))return false;
                transfer.CompleteWithPresentation(null,null,null);return true;
            }
            if(AirthingsMini.IsTarget(transfer.Target)){bool accepted=MiniPlacement.Place(this,slot,AirthingsMini.Decode(transfer.Target),transfer.SourceOwner,transfer.SourceSlot);if(accepted)transfer.AcceptWithoutReplacement();return accepted;}
            if(slot.IsAirthingsMini||slot.IsAirthingsReserved)return false;
            string? target=slot.Target,presentation=slot.Presentation,icon=slot.IconPath;string name=slot.DisplayName;
            transfer.ExchangeSensorPreferences("top:"+slot.Index,target);
            slot.SetTarget(transfer.Target,transfer.DisplayName);slot.Presentation=transfer.Presentation;slot.IconPath=transfer.IconPath;
            transfer.CompleteWithPresentation(target,name,presentation,icon);
            SaveShortcuts();SensorCellPreferences.NotifyPlacement();return true;
        }

        private void RemoveShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem { CommandParameter: QuickLaunchSlot slot })
                return;

            slot.Clear();
            SaveShortcuts();
        }

        private void EditShortcut_Click(object sender, RoutedEventArgs e)
        {
            if(sender is not MenuItem { CommandParameter:QuickLaunchSlot slot } || slot.IsEmpty)return;
            if(slot.IsAirthingsMini){EditAirthingsMini(slot);e.Handled=true;return;}
            if(slot.IsUsageMini){CreateUsageMiniEditor(slot).ShowDialog();e.Handled=true;return;}
            CreateShortcutEditor(slot).ShowDialog();
            e.Handled=true;
        }

        private void RenameShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem { CommandParameter: QuickLaunchSlot slot } || slot.IsEmpty) return;
            ShortcutRenameDialog dialog = new(slot.DisplayName) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() != true) return;
            slot.SetTarget(slot.Target, dialog.ShortcutName);
            SaveShortcuts();
        }

        private void ShortcutProperties_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { CommandParameter: QuickLaunchSlot slot })
                ShortcutShellActions.ShowProperties(Window.GetWindow(this), slot.Target);
        }

        private static bool TryGetDroppedTarget(
            IDataObject data,
            out string? target,
            out string? displayName)
        {
            target = null;
            displayName = null;

            if (data.GetDataPresent(DataFormats.FileDrop) &&
                data.GetData(DataFormats.FileDrop) is string[] files &&
                files.Length > 0)
            {
                target = files[0];
                displayName = Path.GetFileNameWithoutExtension(target);
                return true;
            }

            if (data.GetDataPresent(DataFormats.Text) &&
                data.GetData(DataFormats.Text) is string text &&
                Uri.TryCreate(text.Trim(), UriKind.Absolute, out Uri? uri))
            {
                target = uri.AbsoluteUri;
                displayName = uri.Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase);
                return true;
            }

            return false;
        }

        private sealed record QuickLaunchEntry(string Target, string DisplayName, string? Presentation = null,string? IconPath=null);

        internal sealed class QuickLaunchSlot : INotifyPropertyChanged
        {
            private Func<TabPresentation>? textAppearance;
            private bool textMetal,textLight;
            internal void ConfigureText(Func<TabPresentation> appearance,bool metal,bool light){textAppearance=appearance;textMetal=metal;textLight=light;NotifyAll();}
            private TabPresentation TextStyle=>textAppearance?.Invoke()??DefaultShortcutText;
            private LabelTypography? MacroTypography=>MacroLibrary.Current.TypographyFor(Target);
            public double TextSize=>MacroTypography?.SafeSize??(double.IsFinite(TextStyle.TextSize)?Math.Clamp(TextStyle.TextSize,10,96):28);
            public double TextRowHeight=>Math.Min(48,TextSize*1.15+4);
            public FontFamily TextFamily=>MacroTypography?.Face??FontEditor.Family(TextStyle.FontFamily);
            public FontWeight TextWeight=>MacroTypography?.Weight??(TextStyle.Bold?FontWeights.Bold:FontWeights.Normal);
            public FontStyle TextStyleMode=>MacroTypography?.Style??(TextStyle.Italic?FontStyles.Italic:FontStyles.Normal);
            public LabelTypography RenderTextEffect=>MacroTypography??new LabelTypography{LightEffectStrength=TextStyle.EffectFor(true),DarkEffectStrength=TextStyle.EffectFor(false),LightEffectMode=TextStyle.ModeFor(true),DarkEffectMode=TextStyle.ModeFor(false)};
            public bool TextMetal=>textMetal;
            public bool TextDark=>!textLight;
            public double TextEffect=>MacroTypography?.EffectFor(textLight,TextStyle.EffectFor(textLight))??TextStyle.EffectFor(textLight);
            private string? _target;
            private string _displayName = "";
            private ImageSource? _icon;
            private bool _showName = true;

            public QuickLaunchSlot(int index, string? target, string? displayName)
            {
                Index = index;
                SetTarget(target, displayName);
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            public int Index { get; }
            public string? Target => _target;
            public string SavedDisplayName => _displayName;
            private string? previewName,previewPresentation;
            private string? iconPath,previewIconPath;
            public string? IconPath {get=>iconPath;set{iconPath=value;NotifyAll();}}
            internal void PreviewIcon(string? path){previewIconPath=path??"";NotifyAll();}

            public string DisplayName => previewName??SavedDisplayName;
            internal void PreviewAppearance(string name,string target,string presentation){previewName=name;previewPresentation=presentation;NotifyAll();}
            internal void ClearAppearancePreview(){previewIconPath=null;previewName=null;previewPresentation=null;NotifyAll();}
            public bool IsSensor => _target?.StartsWith("sensor:")==true&&Enum.TryParse<SystemMonitorWidget.MetricKind>(_target[7..],out _);
            public bool IsUsageMini=>UsageMini.IsTarget(Target);
            public bool IsAirthingsMini=>AirthingsMini.IsTarget(_target);
            internal bool IsAirthingsReserved=>_target==AirthingsMini.ReservedTarget;
            public bool IsEmpty => string.IsNullOrWhiteSpace(_target);
            public string Symbol => IsEmpty ? "+" : DisplayName.Length==0?"â—†":DisplayName[..1].ToUpperInvariant();
            public ImageSource? Icon => IsSensor||IsUsageMini||IsAirthingsMini||IsAirthingsReserved ? null : MacroLibrary.IsReference(_target) ? MacroLibrary.Current.IconFor(_target) : ShortcutIcon.Load(previewIconPath??IconPath)??_icon;
            private string? presentation;
            public string? Presentation { get=>presentation; set { presentation=ShortcutAppearanceEditor.Normalize(value);NotifyAll(); } }
            public bool ShowIcon => (previewPresentation??MacroLibrary.Current.PresentationFor(_target,Presentation))!="Text";
            public bool ShowName
            {
                get => (previewPresentation??MacroLibrary.Current.PresentationFor(_target,Presentation)) is string mode?mode!="Icon":_showName;
                set
                {
                    if (_showName == value) return;
                    _showName = value;
                    OnPropertyChanged();
                }
            }
            public string ToolTip => IsEmpty
                ? LanguageManager.Text(
                    "Drag an application or web shortcut here",
                    "Nutempkite programos arba interneto nuorodÄ…")
                : IsUsageMini?UsageMini.Description(Target):IsAirthingsMini?AirthingsMini.Description(_target):IsSensor ? $"{SystemMonitorWidget.GetMetricLabel(Enum.Parse<SystemMonitorWidget.MetricKind>(_target![7..]))}\nRight-click: sensor options · graphs: wheel to zoom, double-click to reset" : MacroLibrary.IsReference(_target) ? MacroLibrary.Current.Description(_target) : $"{DisplayName}\n{_target}";

            public void SetTarget(string? target, string? displayName)
            {
                if(!string.Equals(_target,target,StringComparison.Ordinal)){presentation=null;iconPath=null;}iconPath=null;
                _target = string.IsNullOrWhiteSpace(target)||LegacyAirthingsStatusTarget.IsTarget(target) ? null : target;
                _displayName = string.IsNullOrWhiteSpace(displayName)
                    ? LanguageManager.Text("Shortcut", "Nuoroda")
                    : displayName;
                _icon = _target == null || IsSensor||IsUsageMini||IsAirthingsMini||IsAirthingsReserved ? null : GetShellIcon(_target);
                NotifyAll();
            }

            public void Clear()
            {
                presentation=null;iconPath=null;
                _target = null;
                _displayName = "";
                _icon = null;
                NotifyAll();
            }

            public void MarkUnavailable()
            {
                _displayName = $"{_displayName} " +
                    LanguageManager.Text("(not found)", "(nerasta)");
                NotifyAll();
            }

            public void RefreshLanguage()
            {
                NotifyAll();
            }

            private void NotifyAll()
            {
                foreach(string property in new[]{nameof(TextSize),nameof(TextRowHeight),nameof(TextFamily),nameof(TextWeight),nameof(TextStyleMode),nameof(TextMetal),nameof(TextDark),nameof(TextEffect),nameof(RenderTextEffect)})OnPropertyChanged(property);
                OnPropertyChanged(nameof(Presentation));
                OnPropertyChanged(nameof(ShowIcon));
                OnPropertyChanged(nameof(ShowName));
                OnPropertyChanged(nameof(Target));
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(IsSensor));
                OnPropertyChanged(nameof(IsUsageMini));
                OnPropertyChanged(nameof(IsAirthingsMini));
                OnPropertyChanged(nameof(Symbol));
                OnPropertyChanged(nameof(Icon));
                OnPropertyChanged(nameof(ToolTip));
            }

            private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        internal static ImageSource? GetShellIcon(string target)
        {
            var timer=Stopwatch.StartNew();try{return GetShellIconCore(target);}finally{if(timer.ElapsedMilliseconds>10)InteractionDiagnostics.Log($"ShellIcon file={Path.GetFileName(target)} ms={timer.Elapsed.TotalMilliseconds:0.###}");}
        }
        private static ImageSource? GetShellIconCore(string target)
        {
            target = ShellTargetPath.Resolve(target);
            if (MacroLibrary.IsReference(target)) return MacroLibrary.Current.IconFor(target);
            if (string.Equals(Path.GetExtension(target), ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                ImageSource? shortcutIcon = GetShortcutCustomIcon(target);
                if (shortcutIcon != null)
                    return shortcutIcon;
            }

            if (string.Equals(Path.GetExtension(target), ".url", StringComparison.OrdinalIgnoreCase))
            {
                ImageSource? internetShortcutIcon = GetInternetShortcutIcon(target);
                if (internetShortcutIcon != null)
                    return internetShortcutIcon;
            }

            string iconTarget = ResolveShortcutTarget(target);
            ImageSource? imageThumbnail = GetImageThumbnail(iconTarget);
            if (imageThumbnail != null)
                return imageThumbnail;

            return GetShellFileIcon(iconTarget);
        }

        private static ImageSource? GetShortcutCustomIcon(string shortcutPath)
        {
            object? shell = null;
            object? shortcut = null;
            try
            {
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return null;
                shell = Activator.CreateInstance(shellType);
                if (shell == null) return null;
                dynamic dynamicShell = shell;
                shortcut = dynamicShell.CreateShortcut(shortcutPath);
                dynamic dynamicShortcut = shortcut;
                string? iconLocation = dynamicShortcut.IconLocation as string;
                if (string.IsNullOrWhiteSpace(iconLocation)) return null;

                int iconIndex = 0;
                string iconPath = iconLocation;
                int separator = iconLocation.LastIndexOf(',');
                if (separator > 0)
                {
                    iconPath = iconLocation[..separator];
                    int.TryParse(iconLocation[(separator + 1)..], out iconIndex);
                }

                return ExtractIconFromFile(iconPath, iconIndex);
            }
            catch
            {
                return null;
            }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
            }
        }

        private static ImageSource? GetInternetShortcutIcon(string shortcutPath)
        {
            try
            {
                string? iconPath = null;
                int iconIndex = 0;
                foreach (string line in File.ReadLines(shortcutPath))
                {
                    if (line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase))
                        iconPath = line[9..].Trim();
                    else if (line.StartsWith("IconIndex=", StringComparison.OrdinalIgnoreCase))
                        int.TryParse(line[10..].Trim(), out iconIndex);
                }

                return string.IsNullOrWhiteSpace(iconPath)
                    ? null
                    : ExtractIconFromFile(iconPath, iconIndex);
            }
            catch
            {
                return null;
            }
        }

        private static ImageSource? ExtractIconFromFile(string iconPath, int iconIndex)
        {
            iconPath = Environment.ExpandEnvironmentVariables(iconPath.Trim().Trim('"'));
            iconPath = ShellTargetPath.Resolve(iconPath);
            iconPath=PackagedIconPath.Resolve(iconPath)!;
            if (!File.Exists(iconPath)) return null;

            if (string.Equals(Path.GetExtension(iconPath), ".ico", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    BitmapDecoder decoder = BitmapDecoder.Create(
                        new Uri(iconPath, UriKind.Absolute),
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    const int preferredIconSize = 256;
                    BitmapFrame? bestFrame = decoder.Frames
                        .OrderBy(frame => Math.Abs(
                            Math.Max(frame.PixelWidth, frame.PixelHeight) - preferredIconSize))
                        .ThenByDescending(frame => frame.PixelWidth * frame.PixelHeight)
                        .FirstOrDefault();
                    bestFrame?.Freeze();
                    return bestFrame;
                }
                catch
                {
                    return null;
                }
            }

            string extension = Path.GetExtension(iconPath);
            if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".cpl", StringComparison.OrdinalIgnoreCase))
            {
                ImageSource? embeddedIcon = ExtractEmbeddedIcon(iconPath, iconIndex);
                if (embeddedIcon != null) return embeddedIcon;
            }

            IntPtr[] largeIcons = new IntPtr[1];
            if (ExtractIconEx(iconPath, iconIndex, largeIcons, null, 1) == 0 ||
                largeIcons[0] == IntPtr.Zero) return null;
            try
            {
                BitmapSource icon = Imaging.CreateBitmapSourceFromHIcon(
                    largeIcons[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                icon.Freeze();
                return icon;
            }
            finally
            {
                DestroyIcon(largeIcons[0]);
            }
        }

        private static ImageSource? ExtractEmbeddedIcon(string filePath, int iconIndex)
        {
            if (!File.Exists(filePath)) return null;

            IntPtr[] icons = new IntPtr[1];
            uint[] iconIds = new uint[1];
            uint extracted = PrivateExtractIcons(
                filePath, iconIndex, 256, 256, icons, iconIds, 1, 0);
            if (extracted == 0 || extracted == uint.MaxValue || icons[0] == IntPtr.Zero)
                return null;

            try
            {
                BitmapSource icon = Imaging.CreateBitmapSourceFromHIcon(
                    icons[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                icon.Freeze();
                return icon;
            }
            finally
            {
                DestroyIcon(icons[0]);
            }
        }

        private static ImageSource? GetShellFileIcon(string iconTarget)
        {
            string extension = Path.GetExtension(iconTarget);
            if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".cpl", StringComparison.OrdinalIgnoreCase))
            {
                ImageSource? embeddedIcon = ExtractEmbeddedIcon(iconTarget, 0);
                if (embeddedIcon != null) return embeddedIcon;
            }

            ImageSource? highResolutionIcon = GetHighResolutionShellIcon(iconTarget);
            if (highResolutionIcon != null)
                return highResolutionIcon;

            ShellFileInfo info = new();
            uint flags = ShgfiIcon | ShgfiLargeIcon;
            uint attributes = 0;

            if (!File.Exists(iconTarget) && !Directory.Exists(iconTarget))
            {
                flags |= ShgfiUseFileAttributes;
                attributes = FileAttributeNormal;
            }

            IntPtr result = SHGetFileInfo(
                iconTarget,
                attributes,
                out info,
                (uint)Marshal.SizeOf<ShellFileInfo>(),
                flags);

            if (result == IntPtr.Zero || info.IconHandle == IntPtr.Zero)
                return null;

            try
            {
                BitmapSource icon = Imaging.CreateBitmapSourceFromHIcon(
                    info.IconHandle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                icon.Freeze();
                return icon;
            }
            finally
            {
                DestroyIcon(info.IconHandle);
            }
        }

        private static ImageSource? GetHighResolutionShellIcon(string target)
        {
            if (!File.Exists(target) && !Directory.Exists(target)) return null;

            IShellItemImageFactory? factory = null;
            try
            {
                Guid interfaceId = typeof(IShellItemImageFactory).GUID;
                int result = SHCreateItemFromParsingName(
                    target, IntPtr.Zero, ref interfaceId, out factory);
                if (result < 0 || factory == null) return null;

                const uint iconOnly = 0x00000004;
                const uint biggerSizeOk = 0x00000001;
                result = factory.GetImage(
                    new NativeSize(256, 256),
                    iconOnly | biggerSizeOk,
                    out IntPtr bitmapHandle);
                if (result < 0 || bitmapHandle == IntPtr.Zero) return null;

                try
                {
                    BitmapSource bitmap = Imaging.CreateBitmapSourceFromHBitmap(
                        bitmapHandle,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    bitmap.Freeze();
                    return bitmap;
                }
                finally
                {
                    DeleteObject(bitmapHandle);
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                if (factory != null && Marshal.IsComObject(factory))
                    Marshal.FinalReleaseComObject(factory);
            }
        }

        private static string ResolveShortcutTarget(string target)
        {
            if (!string.Equals(
                    Path.GetExtension(target),
                    ".lnk",
                    StringComparison.OrdinalIgnoreCase))
            {
                return target;
            }

            object? shell = null;
            object? shortcut = null;

            try
            {
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                    return target;

                shell = Activator.CreateInstance(shellType);
                if (shell == null)
                    return target;

                dynamic dynamicShell = shell;
                shortcut = dynamicShell.CreateShortcut(target);
                dynamic dynamicShortcut = shortcut;
                string? resolvedTarget = dynamicShortcut.TargetPath as string;
                if(resolvedTarget!=null)resolvedTarget=ShellTargetPath.Resolve(resolvedTarget);

                return !string.IsNullOrWhiteSpace(resolvedTarget) &&
                       (File.Exists(resolvedTarget) ||
                        Directory.Exists(resolvedTarget))
                    ? resolvedTarget
                    : target;
            }
            catch
            {
                return target;
            }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut))
                    Marshal.FinalReleaseComObject(shortcut);
                if (shell != null && Marshal.IsComObject(shell))
                    Marshal.FinalReleaseComObject(shell);
            }
        }

        private static ImageSource? GetImageThumbnail(string target)
        {
            if (!File.Exists(target))
                return null;

            string extension = Path.GetExtension(target).ToLowerInvariant();
            string[] supportedImageExtensions =
            {
                ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff"
            };

            if (!supportedImageExtensions.Contains(extension))
                return null;

            try
            {
                BitmapImage thumbnail = new();
                thumbnail.BeginInit();
                thumbnail.UriSource = new Uri(target, UriKind.Absolute);
                thumbnail.CacheOption = BitmapCacheOption.OnLoad;
                thumbnail.DecodePixelWidth = 128;
                thumbnail.DecodePixelHeight = 128;
                thumbnail.EndInit();
                thumbnail.Freeze();
                return thumbnail;
            }
            catch
            {
                return null;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ShellFileInfo
        {
            public IntPtr IconHandle;
            public int IconIndex;
            public uint Attributes;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string DisplayName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string TypeName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct NativeSize
        {
            public NativeSize(int width, int height)
            {
                Width = width;
                Height = height;
            }

            public readonly int Width;
            public readonly int Height;
        }

        [ComImport]
        [Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(NativeSize size, uint flags, out IntPtr bitmapHandle);
        }
    }
}










