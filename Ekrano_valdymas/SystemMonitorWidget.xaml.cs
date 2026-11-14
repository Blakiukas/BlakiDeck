using LibreHardwareMonitor.Hardware;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Ekrano_valdymas
{
    public partial class SystemMonitorWidget : UserControl, IDisposable
    {
        
        private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly DispatcherTimer coreTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
        private HardwareReader? hardware;
        private readonly object hardwareState=new();
        private bool hardwareRequested;
        private long helperStartedAt;
        private string hardwareReaderSource="Inactive";
        internal string HardwareReaderSourceForAudit
        {
            get=>hardwareReaderSource;
            private set{if(hardwareReaderSource==value)return;hardwareReaderSource=value;ResourceModeDiagnostics.RecordHardwareSource(value);}
        }
        private NvidiaReader? nvidia;
        private Window? sensorWindow;
        private bool sensorsActive;
        private bool sessionStarted;
        internal static SystemMonitorWidget? Shared {get;private set;}
        internal readonly MonitorHistory History = new();
        internal event Action? ReadingsChanged;
        internal event Action? CoreReadingsChanged;
        private readonly HashSet<SensorTile> presentationsInView = new();
        internal void TrackPresentation(SensorTile tile,bool present)
        {
            if(present)presentationsInView.Add(tile);else presentationsInView.Remove(tile);
            UpdateSensorActivity();
        }
        private bool HasCoreDemand => presentationsInView.Any(t=>t.NeedsCoreUpdates);
        internal bool CoreTimerRunningForAudit=>coreTimer.IsEnabled;
        internal long CoreTicksForAudit {get;private set;}
        internal IReadOnlyDictionary<MetricKind,double?> Latest {get;private set;} = new Dictionary<MetricKind,double?>();
        internal double TotalRam {get;private set;}
        internal IReadOnlyList<double> LatestCoreLoads {get;private set;}=[];
        private CodexUsageSnapshot? codexUsage;
        internal CodexUsageSnapshot? UsageSnapshot=>codexUsage;
        private bool codexPending;
        private readonly CancellationTokenSource codexCancellation=new();
        private double nextCodexRead;
        private double lastSampleTime;
        private void ActivateHistory(MetricKind kind)
        {
            if(kind is MetricKind.CodexFiveHour or MetricKind.CodexWeekly){History.Activate(MetricKind.CodexFiveHour);History.Activate(MetricKind.CodexWeekly);}
            if(kind==MetricKind.CpuStatus)foreach(var metric in new[]{MetricKind.CpuLoad,MetricKind.CpuTemperature,MetricKind.CpuPower,MetricKind.CpuVoltage,MetricKind.CpuFan,MetricKind.CpuPump})History.Activate(metric);
            if(kind==MetricKind.DiskStatus)foreach(var metric in new[]{MetricKind.DiskRead,MetricKind.DiskWrite,MetricKind.DiskTemperature,MetricKind.DiskWear,MetricKind.DiskHealth})History.Activate(metric);
            History.Activate(kind);
            if(NormalizeMetric(kind)==MetricKind.Network)
            {
                History.Activate(MetricKind.Network);History.Activate(MetricKind.NetworkDownload);History.Activate(MetricKind.NetworkUpload);
            }
        }
        internal void ActivateMetric(MetricKind kind) { ActivateHistory(kind); sessionStarted=true; UpdateSensorActivity(); }
        internal MetricPresentation GetPresentation(MetricKind kind)
        {
            var value=presentations.GetValueOrDefault(kind);
            if(value==null)
            {
                value=presentations.GetValueOrDefault(NormalizeMetric(kind))??SensorFactoryDefaults.ForMetric(NormalizeMetric(kind));
                // A saved legacy source keeps its original direction even when
                // the newer NETWORK base preference has direction overrides.
                if(kind!=NormalizeMetric(kind))value=value with{NetworkDown=null,NetworkUp=null};
            }
            return NormalizeNetworkPresentation(kind,value);
        }
        private bool suspended;
        internal void SetSuspended(bool value){suspended=value;UpdateSensorActivity();}
        private readonly CpuReader cpu = new();
        private AdvancedSensorClient? advancedSensors;
        private readonly string settingsFile = Path.Combine(
            DiagnosticState.LocalApplicationData,
            "Ekrano_valdymas", "system-monitor.json");
        private readonly string intensitySettingsFile = Path.Combine(
            DiagnosticState.LocalApplicationData,
            "Ekrano_valdymas", "system-monitor-intensity.json");
        private Point dragStart;
        private MonitorSlot? pressedSlot;
        private readonly PhysicalNetworkRateReader networkReader=new();
        private double colorIntensity = 45;
        private sealed record HardwareGpuSample(string? Id,string Scope,IReadOnlyDictionary<MetricKind,double?> Values,DateTime SampledAt=default);
        private HardwareGpuSample hardwareGpu=new(null,"GPU",new Dictionary<MetricKind,double?>());
        private string? historyGpuId;
        private string gpuPowerScope="GPU";
        private IReadOnlyDictionary<MetricKind, double?> hardwareValues =
            new Dictionary<MetricKind, double?>();
        private int hardwareRefreshPending;
        private bool isDisposed;

        public static readonly DependencyProperty WideLayoutProperty=DependencyProperty.Register(nameof(WideLayout),typeof(bool),typeof(SystemMonitorWidget),new PropertyMetadata(false));
        public bool WideLayout {get=>(bool)GetValue(WideLayoutProperty);private set=>SetValue(WideLayoutProperty,value);}
        internal void SetWideLayout(bool wide){bool changed=WideLayout!=wide;WideLayout=wide;if(changed)RebuildActiveSlots();foreach(var slot in Slots)slot.SetLargeGraph(IsLargeGraph(ActiveSlots.IndexOf(slot)));}
        public ObservableCollection<MonitorSlot> Slots { get; } = new();
        public ObservableCollection<MonitorSlot> ActiveSlots { get; } = new();
        public int ActiveColumns => ActiveSlots.Count switch { <= 1 => 1, <= 2 => 2, 4 => 2, _ => 3 };

        public SystemMonitorWidget() : this(null,null) { }
        internal SystemMonitorWidget(SystemMonitorWidget? source,string? key)
        {
            readingSource=source;
            ViewKey=key;
            if(key!=null)settingsFile=Path.Combine(DiagnosticState.LocalApplicationData,"Ekrano_valdymas",$"system-monitor-{key}.json");
            InitializeComponent();
            if(source==null)Shared=this;
            SeedIndependentLayout();
            LoadSlots();
            LoadPresentations();
            LoadIntensity();
            LoadSectionLayout();
            LoadGridLayout();
            InitializePresets();
            MigrateGpuStatus();
            SizeChanged+=(_,_)=>monitorDragHint.Visibility=ActualWidth>=800?Visibility.Visible:Visibility.Collapsed;
            if(source!=null&&!File.Exists(settingsFile))SaveSlots();
            timer.Tick += (_, _) =>
            {
                QueueHardwareRefresh();
                QueueDiskRefresh();
                RefreshValues();
            };
            coreTimer.Tick += (_, _) => RefreshCoreValues();
            ResourcePolicy.Changed+=UpdateCoreInterval;
            UpdateCoreInterval();
            Loaded += (_, _) =>
            {
                sensorWindow=Window.GetWindow(this);
                if(sensorWindow!=null)sensorWindow.StateChanged+=SensorWindowChanged;
                UpdateSensorActivity();
            };
            IsVisibleChanged+=(_,_)=>UpdateSensorActivity();
            Unloaded += (_, _) => { if(sensorWindow!=null)sensorWindow.StateChanged-=SensorWindowChanged;sensorWindow=null;UpdateSensorActivity(); };
        }

        private void SensorWindowChanged(object? sender,EventArgs e)=>UpdateSensorActivity();
        private void UpdateCoreInterval()
        {
            // Only the live per-core display lane is reduced. The one-second
            // sensor/history sampler remains unchanged during a game.
            coreTimer.Interval=TimeSpan.FromMilliseconds(ResourcePolicy.IsGame?500:100);
        }
        private void UpdateSensorActivity()
        {
            if(readingSource!=null)return;
            if(isDisposed){StopSensors();return;}
            bool visible=!suspended&&IsLoaded&&IsVisible&&sensorWindow?.WindowState!=WindowState.Minimized;
            if(visible){sessionStarted=true;foreach(var slot in ActiveSlots.Where(s=>s.IsActive&&!s.IsShortcut))ActivateHistory(slot.Kind);}
            if(!sessionStarted){StopSensors();return;}
            var activated=History.Activated;
            sensorsActive=true;
            bool gpu=activated.Any(k=>k is >=MetricKind.GpuLoad and <=MetricKind.VramUsed or >=MetricKind.GpuPowerLimit and <=MetricKind.GpuStatus or MetricKind.GpuPump);
            bool advanced=activated.Any(k=>k is MetricKind.HardwareSensor or MetricKind.CpuPump or MetricKind.GpuPump or >=MetricKind.CpuTemperature and <=MetricKind.CpuFan or MetricKind.GpuTemperature or MetricKind.GpuHotspot or MetricKind.GpuPower or MetricKind.GpuVoltage or MetricKind.GpuFan or MetricKind.DiskRead or MetricKind.DiskWrite or MetricKind.DiskTemperature or MetricKind.DiskWear or MetricKind.DiskHealth);
            if(gpu&&nvidia==null){nvidia=new();if(DiagnosticState.Root==null)nvidia.Start();}
            if(!gpu){nvidia?.Dispose();nvidia=null;}
            if(advanced&&advancedSensors==null&&DiagnosticState.Root==null){helperStartedAt=Environment.TickCount64;advancedSensors=new();}
            if(!advanced){advancedSensors?.Dispose();advancedSensors=null;}
            if(advancedSensors!=null)advancedSensors.IncludeStorage=activated.Any(IsDisk);
            hardwareRequested=gpu||advanced;
            if(!hardwareRequested)ReleaseLocalHardware();
            if(HasCoreDemand)coreTimer.Start();else coreTimer.Stop();
            if(!timer.IsEnabled){networkReader.Sample(MonitorHistory.Now,null);timer.Start();QueueHardwareRefresh();RefreshValues();}
        }
        private void StopSensors()
        {
            sensorsActive=false;timer.Stop();coreTimer.Stop();
            nvidia?.Dispose();nvidia=null;advancedSensors?.Dispose();advancedSensors=null;
            hardwareRequested=false;ReleaseLocalHardware();HardwareReaderSourceForAudit="Inactive";
        }
        private void ReleaseLocalHardware()
        {
            HardwareReader? old;
            lock(hardwareState){old=hardware;hardware=null;if(old!=null)HardwareTelemetry.Publish([],false);}
            if(old!=null)_=Task.Run(old.Dispose);
        }
        internal bool SensorsActive=>sensorsActive;
        internal bool SensorTimersRunning=>timer.IsEnabled||coreTimer.IsEnabled;
        internal void RefreshPanelPalette() { foreach(var slot in Slots)slot.RefreshNeutralBrush(); }
        internal bool SensorReadersAllocated=>hardware!=null||nvidia!=null||advancedSensors!=null;

        public double ColorIntensity
        {
            get => readingSource?.ColorIntensity??colorIntensity;
            set
            {
                colorIntensity = Math.Clamp(value, 0, 100);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(intensitySettingsFile)!);
                    File.WriteAllText(intensitySettingsFile, colorIntensity.ToString(CultureInfo.InvariantCulture));
                }
                catch { }
                RefreshValues();
            }
        }

        public MetricKind GetSlotMetric(int index) => Slots[index].Kind;
        public bool IsSlotActive(int index) => index >= 0 && index < Slots.Count && Slots[index].IsActive;

        public static string GetMetricLabel(MetricKind kind) => MetricInfo.Label(kind);

        public void SetSlotMetric(int index, MetricKind kind)
        {
            if (index < 0 || index >= Slots.Count) return;
            Slots[index].SetContent("sensor:"+kind);
            UpdateSensorActivity();
            RefreshValues();
            SaveSlots();
        }
        public void SetSlotActive(int index, bool active)
        {
            if (index < 0 || index >= Slots.Count) return;
            if (!active && ActiveSlots.Count <= 1) return;
            Slots[index].IsActive = active;
            RebuildActiveSlots(); SaveSlots();
        }

        internal static void ActivateShortcut(ShortcutsWidget.Slot slot)
        {
            if(string.IsNullOrWhiteSpace(slot.Target)||UsageMini.IsTarget(slot.Target)||slot.Target==MacroLibraryCard.ClockTarget)return;
            if(MacroRunner.TryActivate(slot.Target,Application.Current.MainWindow))return;
            try {Process.Start(new ProcessStartInfo(slot.Target){UseShellExecute=true});}
            catch(Exception ex){InteractionDiagnostics.RecordFailure("Monitor shortcut",ex);}
        }
        public void ResetLayout()
        {
            RestorePreset(FactoryPreset(selectedPreset));
        }

        private void LoadIntensity()
        {
            try
            {
                if (File.Exists(intensitySettingsFile) &&
                    double.TryParse(File.ReadAllText(intensitySettingsFile), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double saved))
                    colorIntensity = Math.Clamp(saved, 0, 100);
            }
            catch { }
        }

        private void LoadSlots()
        {
            MetricKind[] defaults = SensorFactoryDefaults.DefaultKinds;
            try
            {
                if (File.Exists(settingsFile))
                {
                    string[]? saved = JsonSerializer.Deserialize<string[]>(File.ReadAllText(settingsFile));
                    if (saved is { Length: > 0 })
                    {
                        for (int i = 0; i < Math.Min(defaults.Length, saved.Length); i++)
                            if (Enum.TryParse(saved[i], true, out MetricKind parsed)) defaults[i] = parsed;
                    }
                }
            }
            catch { }
            foreach (MetricKind kind in defaults) Slots.Add(new MonitorSlot(kind));
            // Enough backing entries for every minimum-size (1 x 2) grid cell.
            while(Slots.Count<GridColumns*6){var empty=new MonitorSlot(MetricKind.CpuLoad);empty.SetContent(null);Slots.Add(empty);}
            try {if(File.Exists(settingsFile+".mixed.json")&&JsonSerializer.Deserialize<MixedCell[]>(File.ReadAllText(settingsFile+".mixed.json")) is {} mixed)
                for(int i=0;i<Math.Min(mixed.Length,Slots.Count);i++)if(mixed[i] is {} cell)Slots[i].SetContent(cell.Target,cell.Name,cell.Appearance,cell.Icon);
            }catch(Exception ex){InteractionDiagnostics.RecordFailure("Monitor mixed layout",ex);}
            try
            {
                string activeFile = Path.ChangeExtension(settingsFile, ".active.json");
                if (File.Exists(activeFile) && JsonSerializer.Deserialize<bool[]>(File.ReadAllText(activeFile)) is { } active)
                    for (int i = 0; i < Math.Min(active.Length, Slots.Count); i++) Slots[i].IsActive = active[i];
            }
            catch { }
            if (!Slots.Any(slot => slot.IsActive)) Slots[0].IsActive = true;
            RebuildActiveSlots();
        }
        private void RebuildActiveSlots()
        {
            ActiveSlots.Clear();
            foreach(var slot in GridLayout is {} grid?grid.Cells.Select(c=>Slots[c.Slot]):Slots.Where((slot,index) => SectionLayout.Length>0?index<SlotCapacity:WideLayout?index<11:index<9&&slot.IsActive))ActiveSlots.Add(slot);
            OnPropertyChanged(nameof(ActiveColumns));
            SetWideLayout(WideLayout);
            UpdateSensorActivity();
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private sealed record MixedCell(string? Target,string? Name,string? Appearance,string? Icon);
        private void SaveSlots()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsFile)!);
                AtomicJson.Write(settingsFile+".mixed.json",Slots.Select(s=>new MixedCell(s.TransferTarget,s.Shortcut?.CustomName,s.Shortcut?.Presentation,s.Shortcut?.IconPath)).ToArray());
                File.WriteAllText(settingsFile,
                    JsonSerializer.Serialize(Slots.Select(slot => slot.Kind.ToString()).ToArray()));
                File.WriteAllText(Path.ChangeExtension(settingsFile, ".active.json"),
                    JsonSerializer.Serialize(Slots.Select(slot => slot.IsActive).ToArray()));
            }
            catch { }
        }

        private void RefreshValues()
        {
            if(readingSource!=null)return;
            if(!sensorsActive)return;
            if(MonitorHistory.Now-lastSampleTime<.9){PublishReadings();return;}
            lastSampleTime=MonitorHistory.Now;
            var hardwareSample=Volatile.Read(ref hardwareGpu);
            IReadOnlyDictionary<MetricKind, double?> values = DiagnosticState.Root==null?hardwareSample.Values:Volatile.Read(ref hardwareValues);
            if(DiagnosticState.Root==null&&DateTime.UtcNow-hardwareSample.SampledAt>TimeSpan.FromSeconds(5))values=new Dictionary<MetricKind,double?>();
            NvidiaSnapshot nv = nvidia?.ReadFor(GpuTelemetrySelection.Current)??new(null,null,null,null,null);
            AdvancedSensorSnapshot advanced = advancedSensors?.Read() ??
                new(null, null, null, null, null, null, null, null, null);
            var currentGpu=GpuTelemetrySelection.Current?.Id;
            if(historyGpuId!=currentGpu){History.ClearGpu();historyGpuId=currentGpu;}
            if(hardwareSample.Id!=currentGpu||currentGpu==null)values=values.ToDictionary(p=>p.Key,p=>MetricGroup(p.Key)=="GPU"?(double?)null:p.Value);
            bool matchingGpu=advanced.GpuId!=null&&advanced.GpuId==GpuTelemetrySelection.Current?.Id;
            double cpuLoad = values.GetValueOrDefault(MetricKind.CpuLoad) ?? cpu.Read();
            IReadOnlyList<double> coreLoads = History.Activated.Contains(MetricKind.CpuCoreLoad)?(coreTimer.IsEnabled?LatestCoreLoads:cpu.ReadCoreLoads()):Array.Empty<double>();
            (ulong total, ulong available) = ReadMemory();
            double usedGb = (total - available) / 1073741824d;
            double totalGb = total / 1073741824d;
            double memoryLoad = total == 0 ? 0 : (total - available) * 100d / total;
            var networkCounters=PhysicalNetworkRateReader.Capture();
            double networkNow=MonitorHistory.Now;
            var networkReading=networkReader.Sample(networkNow,networkCounters);
            double? downloadRate=networkReading.Download,uploadRate=networkReading.Upload;
            double? networkRate=downloadRate+uploadRate;

            Dictionary<MetricKind, double?> merged = new(values)
            {
                [MetricKind.CpuLoad] = cpuLoad,
                [MetricKind.CpuTemperature] = advanced.CpuTemperature ?? values.GetValueOrDefault(MetricKind.CpuTemperature),
                [MetricKind.CpuEffectiveClock] = HardwareTelemetry.CpuEffectiveClock(HardwareTelemetry.Readings),
                [MetricKind.CpuPower] = advanced.CpuPower ?? values.GetValueOrDefault(MetricKind.CpuPower),
                [MetricKind.CpuVoltage] = advanced.CpuVoltage ?? values.GetValueOrDefault(MetricKind.CpuVoltage),
                [MetricKind.CpuPump] = advanced.CpuPump ?? values.GetValueOrDefault(MetricKind.CpuPump),
                [MetricKind.GpuPump] = (matchingGpu?advanced.GpuPump:null) ?? values.GetValueOrDefault(MetricKind.GpuPump),
                [MetricKind.CpuFan] = advanced.CpuFan ?? values.GetValueOrDefault(MetricKind.CpuFan),
                [MetricKind.GpuLoad] = nv.Load ?? values.GetValueOrDefault(MetricKind.GpuLoad),
                [MetricKind.GpuTemperature] = (matchingGpu?advanced.GpuTemperature:null) ?? nv.Temperature ?? values.GetValueOrDefault(MetricKind.GpuTemperature),
                [MetricKind.GpuHotspot] = (matchingGpu?advanced.GpuHotspot:null) ?? values.GetValueOrDefault(MetricKind.GpuHotspot),
                [MetricKind.GpuPower] = nv.Power ?? (matchingGpu?advanced.GpuPower:null) ?? values.GetValueOrDefault(MetricKind.GpuPower),
                [MetricKind.GpuClock] = nv.Clock ?? values.GetValueOrDefault(MetricKind.GpuClock),
                [MetricKind.GpuFan] = (matchingGpu?advanced.GpuFan:null) ?? values.GetValueOrDefault(MetricKind.GpuFan),
                [MetricKind.GpuVoltage] = (matchingGpu?advanced.GpuVoltage:null) ?? values.GetValueOrDefault(MetricKind.GpuVoltage),
                [MetricKind.VramUsed] = nv.VramUsedGb ?? values.GetValueOrDefault(MetricKind.VramUsed),
                [MetricKind.MemoryUsed] = usedGb,
                [MetricKind.MemoryLoad] = memoryLoad,
                [MetricKind.CpuCoreLoad] = coreLoads.Count>0?coreLoads.Average():null,
                [MetricKind.CodexFiveHour] = codexUsage?.FiveHourRemainingPercent,
                [MetricKind.CodexWeekly] = codexUsage?.WeeklyRemainingPercent,
                [MetricKind.Network] = networkRate,
                [MetricKind.NetworkDownload] = downloadRate,
                [MetricKind.NetworkUpload] = uploadRate,
                [MetricKind.DiskRead] = DiskValue(null),
                [MetricKind.DiskWrite] = DiskValue(null,MetricKind.DiskWrite),
                [MetricKind.GpuPowerLimit] = nv.EnforcedLimit,
                [MetricKind.GpuPowerLimitActive] = nv.Flag(4),
                [MetricKind.GpuThermalLimitActive] = nv.Flag(0x60)
            };

            gpuPowerScope=nv.Power.HasValue?"BOARD":matchingGpu&&advanced.GpuPower.HasValue?advanced.GpuPowerScope:hardwareSample.Scope;
            storageSnapshot=advanced.Storage??[];
            gpuSnapshot=nv;
            RecordDiskHistory(networkNow);
            LatestCoreLoads=coreLoads;TotalRam=totalGb;Latest=merged;History.Record(networkNow,merged,networkReading.IntervalStart);
            PublishReadings();
            if(DiagnosticState.Root==null&&!codexPending&&MonitorHistory.Now>=nextCodexRead&&History.Activated.Any(k=>k is MetricKind.CodexFiveHour or MetricKind.CodexWeekly))
            {
                codexPending=true;nextCodexRead=MonitorHistory.Now+60;
                _=ReadCodex();
            }
        }

        private void PublishReadings()
        {
            if(!suspended&&IsLoaded&&IsVisible&&sensorWindow?.WindowState!=WindowState.Minimized)
                foreach(var slot in ActiveSlots.Where(s=>s.IsActive&&!s.IsShortcut))slot.Update(Latest.GetValueOrDefault(slot.Kind),TotalRam,colorIntensity,LatestCoreLoads);
            ReadingsChanged?.Invoke();
            if(HasCoreDemand)coreTimer.Start();else coreTimer.Stop();
        }
        private async Task ReadCodex()
        {
            try { var result=await new CodexUsageService().ReadAsync(codexCancellation.Token); if(!isDisposed&&result!=null)codexUsage=result; }
            finally {codexPending=false;}
        }
        private void QueueHardwareRefresh()
        {
            if(!sensorsActive||!hardwareRequested||DiagnosticState.Root!=null||isDisposed)return;
            DateTime sampledAt=default;
            var helper=advancedSensors?.Read(out sampledAt);
            var reader=SelectHardwareReader(helper,sampledAt,advancedSensors!=null&&Environment.TickCount64-helperStartedAt<5000);
            if(reader==null||Interlocked.Exchange(ref hardwareRefreshPending,1)!=0)return;
            _ = Task.Run(() =>
            {
                try
                {
                    IReadOnlyDictionary<MetricKind, double?> values = reader.Read();
                    lock(hardwareState)if (!isDisposed&&ReferenceEquals(reader,hardware)){HardwareTelemetry.Publish(reader.Readings);Volatile.Write(ref hardwareGpu,new(reader.GpuId,reader.PowerScope,values,DateTime.UtcNow));Volatile.Write(ref hardwareValues, values);}
                }
                catch { }
                finally { Interlocked.Exchange(ref hardwareRefreshPending, 0); }
            });
        }
        private HardwareReader? SelectHardwareReader(AdvancedSensorSnapshot? helper,DateTime sampledAt,bool helperStarting)
        {
            if(helper?.HasHardwareSnapshot==true)
            {
                ReleaseLocalHardware();HardwareReaderSourceForAudit="Helper";
                Volatile.Write(ref hardwareGpu,new(helper.GpuId,helper.GpuPowerScope,helper.Metrics!,sampledAt));
                Volatile.Write(ref hardwareValues,helper.Metrics!);
                return null;
            }
            // Give the helper its initial connection window before opening a
            // duplicate hardware library. Refusal, disconnect, stale data and
            // older helper protocols all retain the ordinary local fallback.
            if(helperStarting&&hardware==null){HardwareReaderSourceForAudit="Waiting for helper";return null;}
            var reader=hardware??=new();HardwareReaderSourceForAudit="Local fallback";
            return reader;
        }

        private void RefreshCoreValues()
        {
            if (!sensorsActive||!HasCoreDemand){coreTimer.Stop();return;}
            IReadOnlyList<double> loads = cpu.ReadCoreLoads();
            double? average = loads.Count == 0 ? null : loads.Average();
            LatestCoreLoads=loads;
            Latest=new Dictionary<MetricKind,double?>(Latest){[MetricKind.CpuCoreLoad]=average};
            foreach (MonitorSlot slot in ActiveSlots.Where(slot => !suspended&&IsVisible&&slot.IsActive&&!slot.IsShortcut&&slot.Kind == MetricKind.CpuCoreLoad))
                slot.Update(average, 0, colorIntensity, loads);
            CoreTicksForAudit++;
            CoreReadingsChanged?.Invoke();
        }

        public void Dispose()
        {
            isDisposed = true;
            if(Interlocked.CompareExchange(ref diskPending,1,0)==0){diskReader?.Dispose();diskReader=null;diskWriteReader?.Dispose();diskWriteReader=null;Interlocked.Exchange(ref diskPending,0);}
            ResourcePolicy.Changed-=UpdateCoreInterval;
            if(ReferenceEquals(Shared,this))Shared=null;
            codexCancellation.Cancel();
            StopSensors();
            if(sensorWindow!=null)sensorWindow.StateChanged-=SensorWindowChanged;
            sensorWindow=null;
        }

        private static SensorTile? FindSensorTile(DependencyObject root){if(root is SensorTile tile)return tile;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var match=FindSensorTile(VisualTreeHelper.GetChild(root,i));if(match!=null)return match;}return null;}
        private void Metric_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border { DataContext: MonitorSlot slot })
            {
                metricClick?.Cancel();if(e.ClickCount==2&&(slot.Shortcut==null||slot.Shortcut.IsEmpty||UsageMini.IsTarget(slot.Shortcut.Target)||slot.Shortcut.Target==MacroLibraryCard.ClockTarget)){pressedSlot=null;liveGridStart=null;liveGridGesture=false;e.Handled=true;FindSensorTile((DependencyObject)sender)?.EditCell();return;}
                pressedSlot = slot;
                dragStart = e.GetPosition(this);
                cellGrab=e.GetPosition((IInputElement)sender);
                liveGridStart=CaptureGridLayout().Cells.FirstOrDefault(c=>c.Slot==Slots.IndexOf(slot));liveGridChanged=false;liveGridGesture=false;
                ((UIElement)sender).CaptureMouse();
                e.Handled = true;
            }
        }

        private void Metric_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (pressedSlot is null || !pressedSlot.IsActive || e.LeftButton != MouseButtonState.Pressed) return;
            Point current = e.GetPosition(this);
            if (Math.Abs(current.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(current.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            var local=e.GetPosition(monitorCells);
            liveGridGesture=true;
            if(new Rect(monitorCells.RenderSize).Contains(local))
            {MoveLiveCell(current);e.Handled=true;return;}
            // Inside the monitor move its grid cell; outside use shared transfer.
            var source=pressedSlot;
            if(string.IsNullOrEmpty(source.TransferTarget)){e.Handled=true;return;}
            var transfer=CreateCellTransfer(source);
            transfer.SourceGrab=cellGrab;
            pressedSlot=null;liveGridStart=null;
            if(liveGridChanged){liveGridChanged=false;AtomicJson.Write(GridFile,CaptureGridLayout());SaveSlots();}
            if(sender is FrameworkElement element)
            {
                SetTransferSize(element,source,transfer);
                ShortcutDragSupport.BeginDrag(element,null,source.Label,transfer);
            }
            e.Handled = true;
        }

        private static string? ExternalShortcut(IDataObject data)
        {
            if(data.GetDataPresent(ShortcutDragSupport.Format))return null;
            if(data.GetDataPresent(DataFormats.FileDrop)&&data.GetData(DataFormats.FileDrop) is string[] files&&files.Length==1)return files[0];
            if(data.GetDataPresent(DataFormats.UnicodeText)&&data.GetData(DataFormats.UnicodeText) is string text&&Uri.TryCreate(text.Trim(),UriKind.Absolute,out var uri)&&uri.Scheme is "http" or "https")return uri.AbsoluteUri;
            return null;
        }
        private static bool SupportedMonitorTransfer(ShortcutTransfer transfer)=>!AirthingsMini.IsTarget(transfer.Target)&&!LegacyAirthingsStatusTarget.IsTarget(transfer.Target);
        private void Metric_DragEnter(object sender, DragEventArgs e)
        {
            if(sender is not Border {DataContext:MonitorSlot destination} border)return;
            var transfer=ShortcutDragSupport.ResolveTransfer(e.Data);
            if(transfer!=null&&ReferenceEquals(transfer.SourceOwner,this)){e.Effects=MovedCellAt(e.GetPosition(monitorCells),monitorCells.RenderSize,transfer)!=null?DragDropEffects.Move:DragDropEffects.None;e.Handled=true;return;}
            bool empty=!destination.IsActive||destination.Shortcut?.IsEmpty==true;
            bool allowed=transfer!=null?SupportedMonitorTransfer(transfer)&&transfer.FitsTarget(this,destination)&&transfer.CanMoveTo(empty?null:destination.TransferTarget):empty&&ExternalShortcut(e.Data)!=null;
            if(!allowed){ResetBorder(sender);e.Effects=DragDropEffects.None;e.Handled=true;return;}
            border.BorderBrush=(Brush)Application.Current.Resources["AccentBrush"];
            border.Background=new SolidColorBrush(Color.FromArgb(45,30,215,96));
            e.Effects=transfer!=null?DragDropEffects.Move:DragDropEffects.Link;e.Handled=true;
        }
        private void Metric_DragLeave(object sender,DragEventArgs e)=>ResetBorder(sender);
        internal bool AcceptShortcutTransfer(MonitorSlot destination,ShortcutTransfer transfer)
        {
            if(!Slots.Contains(destination)||!SupportedMonitorTransfer(transfer)||!transfer.FitsTarget(this,destination))return false;
            bool empty=!destination.IsActive||destination.Shortcut?.IsEmpty==true;
            var target=empty?null:destination.TransferTarget;var name=empty?null:destination.Label;
            if(!transfer.CanMoveTo(target))return false;
            var appearance=empty?null:destination.Shortcut?.Presentation;var icon=empty?null:destination.Shortcut?.IconPath;
            transfer.ExchangeSensorPreferences(CellLocation(destination),target);
            destination.SetContent(transfer.Target,transfer.DisplayName,transfer.Presentation,transfer.IconPath);destination.IsActive=true;
            transfer.CompleteWithPresentation(target,name,appearance,icon);
            SensorCellPreferences.NotifyPlacement();UpdateSensorActivity();RefreshValues();SaveSlots();return true;
        }
        private void Metric_Drop(object sender,DragEventArgs e)
        {
            ResetBorder(sender);e.Effects=DragDropEffects.None;e.Handled=true;
            if(sender is not Border {DataContext:MonitorSlot destination})return;
            if(ShortcutDragSupport.ResolveTransfer(e.Data) is {} transfer)
            {if(ReferenceEquals(transfer.SourceOwner,this)?MoveCellAt(e.GetPosition(monitorCells),monitorCells.RenderSize,transfer):AcceptShortcutTransfer(destination,transfer))e.Effects=DragDropEffects.Move;return;}
            if((!destination.IsActive||destination.Shortcut?.IsEmpty==true)&&ExternalShortcut(e.Data) is {} target)
            {destination.SetContent(target);destination.IsActive=true;UpdateSensorActivity();RefreshValues();SaveSlots();e.Effects=DragDropEffects.Link;}
        }

        private static void ResetBorder(object sender)
        {
            if (sender is Border border)
            {
                border.BorderBrush = new SolidColorBrush(Color.FromArgb(0x28, 255, 255, 255));
                border.SetResourceReference(Border.BackgroundProperty, "ModuleCardSurfaceBrush");
            }
        }

        private static void ResetMetricDragVisuals(DependencyObject root)
        {
            if(root is Border { DataContext: MonitorSlot } border) ResetBorder(border);
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) ResetMetricDragVisuals(VisualTreeHelper.GetChild(root,i));
        }

        private void Metric_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border { DataContext: MonitorSlot slot } border) return;
            OpenMetricOptions(border,slot);
            e.Handled = true;
        }

        public enum MetricKind
        {
            CpuLoad, CpuCoreLoad, CpuTemperature, CpuPower, CpuVoltage, CpuFan,
            GpuLoad, GpuTemperature, GpuHotspot, GpuPower, GpuVoltage, GpuFan, VramUsed,
            MemoryUsed, MemoryLoad, Network, CodexFiveHour, CodexWeekly, NetworkDownload, NetworkUpload, DiskRead, DiskWrite, DiskTemperature, DiskWear, DiskHealth, GpuPowerLimit, GpuPowerLimitActive, GpuThermalLimitActive, GpuLimitReason, GpuClock, GpuStatus, CpuPump, GpuPump, CpuStatus, DiskStatus, HardwareSensor, CpuEffectiveClock
        }

        public sealed partial class MonitorSlot : INotifyPropertyChanged
        {
            private MetricKind kind;
            private string displayValue = "--";
            private Brush valueBrush = Brushes.White;
            private bool isActive = true;
            private double[] smoothedCoreLoads = Array.Empty<double>();
            public MonitorSlot(MetricKind value) => kind = value;
            public MetricKind Kind
            {
                get => kind;
                set { kind = value; Notify(nameof(Kind)); Notify(nameof(Label)); Notify(nameof(IsCoreLoad)); Notify(nameof(IsMemoryUsed)); Notify(nameof(ShowCoreBars)); }
            }
            public ShortcutsWidget.Slot? Shortcut {get;private set;}
            public bool IsShortcut=>Shortcut!=null;
            public string TransferTarget=>IsShortcut?Shortcut!.Target!:"sensor:"+Kind;
            internal void SetContent(string? target,string? name=null,string? appearance=null,string? icon=null)
            {
                IsActive=true;
                if(target?.StartsWith("sensor:")==true&&Enum.TryParse<MetricKind>(target[7..],out var metric)){Shortcut=null;Kind=metric;}
                else {Shortcut=new(target,name){Presentation=appearance,IconPath=icon};}
                Notify(nameof(Shortcut));Notify(nameof(IsShortcut));Notify(nameof(Label));Notify(nameof(TransferTarget));
            }
            public string Label => Shortcut?.Name??MetricInfo.Label(Kind);
            public bool IsActive { get => isActive; set { isActive = value; Notify(); } }
            public bool IsCoreLoad => Kind == MetricKind.CpuCoreLoad;
            public bool IsMemoryUsed => Kind == MetricKind.MemoryUsed;
            public ObservableCollection<CoreBar> CoreBars { get; } = new();
            public string DisplayValue { get => displayValue; private set { displayValue = value; Notify(); } }
            public string ValueNumber => IsMemoryUsed
                ? SplitValue(DisplayValue).number.Split('/')[0]
                : SplitValue(DisplayValue).number;
            public string ValueSecondary => IsMemoryUsed && SplitValue(DisplayValue).number.Contains('/')
                ? $"  /  {SplitValue(DisplayValue).number.Split('/')[1]} {SplitValue(DisplayValue).unit}"
                : "";
            public string ValueUnit => SplitValue(DisplayValue).unit;
            public Brush ValueBrush { get => valueBrush; private set { valueBrush = value; Notify(); } }
            internal void RefreshNeutralBrush() { if(DisplayValue is "N/A" or "--")ValueBrush=(Brush)Application.Current.Resources["PanelTextBrush"]; }

            public void Update(
                double? value,
                double totalRam,
                double intensity,
                IReadOnlyList<double> coreLoads)
            {
                if (IsCoreLoad)
                {
                    if (smoothedCoreLoads.Length != coreLoads.Count)
                        smoothedCoreLoads = coreLoads.Select(load => Math.Clamp(load, 0, 100)).ToArray();
                    else
                    {
                        for (int index = 0; index < coreLoads.Count; index++)
                        {
                            double target = Math.Clamp(coreLoads[index], 0, 100);
                            double difference = target - smoothedCoreLoads[index];
                            if (Math.Abs(difference) < 2) continue;
                            smoothedCoreLoads[index] += difference * (difference > 0 ? 0.40 : 0.15);
                        }
                    }
                    value = smoothedCoreLoads.Length == 0 ? null : smoothedCoreLoads.Average();
                    while (CoreBars.Count > smoothedCoreLoads.Length)
                        CoreBars.RemoveAt(CoreBars.Count - 1);
                    while (CoreBars.Count < smoothedCoreLoads.Length)
                        CoreBars.Add(new CoreBar());
                    for (int index = 0; index < smoothedCoreLoads.Length; index++)
                        CoreBars[index].Height = smoothedCoreLoads[index] * 0.34 + 2;
                }
                DisplayValue = MetricInfo.Format(Kind, value, totalRam);
                Notify(nameof(ValueNumber)); Notify(nameof(ValueSecondary)); Notify(nameof(ValueUnit));
                ValueBrush = MetricInfo.BrushFor(Kind, value, intensity);
                UpdatePresentation(value,totalRam);Notify(nameof(ValueNumber));Notify(nameof(ValueSecondary));Notify(nameof(ValueUnit));
            }
            private void Notify([CallerMemberName] string? name = null) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            public event PropertyChangedEventHandler? PropertyChanged;
            private static (string number, string unit) SplitValue(string text)
            {
                int index = 0; while (index < text.Length && (char.IsDigit(text[index]) || text[index] is '.' or ',' or '-' or '/')) index++;
                return index == 0 ? (text, "") : (text[..index], text[index..].Trim());
            }
        }

        public sealed class CoreBar : INotifyPropertyChanged
        {
            private double height;
            public double Height
            {
                get => height;
                set
                {
                    if (Math.Abs(height - value) < 0.05) return;
                    height = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Height)));
                }
            }
            public event PropertyChangedEventHandler? PropertyChanged;
        }

        private static class MetricInfo
        {
            public static string Label(MetricKind kind) => kind switch
            {
                MetricKind.DiskRead => "DISK READ", MetricKind.DiskWrite=>"DISK WRITE", MetricKind.DiskTemperature=>"DISK TEMP", MetricKind.DiskWear=>"DISK WEAR", MetricKind.DiskHealth=>"DISK HEALTH",
                MetricKind.GpuPowerLimit=>"GPU POWER LIMIT", MetricKind.GpuPowerLimitActive=>"GPU POWER CAP", MetricKind.GpuThermalLimitActive=>"GPU THERMAL LIMIT", MetricKind.GpuLimitReason=>"GPU LIMIT REASON",
                MetricKind.CpuEffectiveClock => "CPU EFFECTIVE CLOCK", MetricKind.GpuClock => "GPU CLOCK", MetricKind.GpuStatus => "GPU STATUS",
                MetricKind.CpuLoad => "CPU LOAD", MetricKind.CpuCoreLoad => "CPU CORES",
                MetricKind.CpuTemperature => "CPU TEMP", MetricKind.CpuPower => "CPU POWER",
                MetricKind.HardwareSensor => "HARDWARE SENSOR", MetricKind.CpuStatus => "CPU STATUS", MetricKind.DiskStatus => "DISK STATUS",
                MetricKind.CpuPump => "CPU PUMP", MetricKind.GpuPump => "GPU PUMP",
                MetricKind.CpuVoltage => "CPU VOLTAGE", MetricKind.CpuFan => "CPU FAN",
                MetricKind.GpuLoad => "GPU LOAD", MetricKind.GpuTemperature => "GPU TEMP",
                MetricKind.GpuHotspot => "GPU HOTSPOT", MetricKind.GpuPower => "GPU POWER",
                MetricKind.GpuVoltage => "GPU VOLTAGE", MetricKind.GpuFan => "GPU FAN",
                MetricKind.VramUsed => "VRAM", MetricKind.MemoryUsed => "RAM USED",
                MetricKind.MemoryLoad => "RAM LOAD", MetricKind.Network => "NETWORK",
                MetricKind.NetworkDownload or MetricKind.NetworkUpload => "NETWORK",
                MetricKind.CodexFiveHour or MetricKind.CodexWeekly => "CODEX", _ => kind.ToString()
            };

            public static string Format(MetricKind kind, double? value, double totalRam)
            {
                if (!value.HasValue) return "N/A";
                return kind switch
                {
                    MetricKind.GpuClock or MetricKind.CpuEffectiveClock => $"{value:0} MHz",
                    MetricKind.CpuLoad or MetricKind.CpuCoreLoad or MetricKind.GpuLoad or MetricKind.MemoryLoad or MetricKind.CodexFiveHour or MetricKind.CodexWeekly => $"{value:0}%",
                    MetricKind.CpuTemperature or MetricKind.GpuTemperature or MetricKind.GpuHotspot => $"{value:0}\u00B0C",
                    MetricKind.CpuPower or MetricKind.GpuPower or MetricKind.GpuPowerLimit => $"{value:0.0} W",
                    MetricKind.CpuVoltage or MetricKind.GpuVoltage => $"{value:0.000} V",
                    MetricKind.CpuFan or MetricKind.GpuFan or MetricKind.CpuPump or MetricKind.GpuPump => $"{value:0} RPM",
                    MetricKind.VramUsed => $"{value*1073741824d/1048576d:0} MB",
                    MetricKind.MemoryUsed => $"{value*1073741824d/1048576d:0}/{totalRam*1073741824d/1048576d:0} MB",
                    MetricKind.DiskRead or MetricKind.DiskWrite => $"{value / 1048576d:0.00} MB/s",
                    MetricKind.Network or MetricKind.NetworkDownload or MetricKind.NetworkUpload => value >= 1048576
                        ? $"{value / 1048576d:0.0} MB/s" : $"{value / 1024d:0} KB/s",
                    _ => $"{value:0}"
                };
            }

            public static Brush BrushFor(MetricKind kind, double? value, double intensity)
            {
                if (!value.HasValue) return (Brush)Application.Current.Resources["PanelTextBrush"];
                Color target = Application.Current.Resources["SystemMonitorBrush"] is SolidColorBrush accent
                    ? accent.Color
                    : Color.FromRgb(30, 215, 96);
                return new SolidColorBrush(Color.FromArgb(
                    target.A, target.R, target.G, target.B));
            }
        }

        private sealed class HardwareReader
        {
            private readonly object sync = new();
            private readonly Computer computer = new()
            {
                IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true,
                IsMotherboardEnabled = true, IsControllerEnabled = true
            };
            private bool opened,disposed;
            internal HardwareReading[] Readings {get;private set;}=[];
            internal string? GpuId {get;private set;}
            internal string PowerScope {get;private set;}="GPU";

            public IReadOnlyDictionary<MetricKind, double?> Read()
            {
                lock (sync)
                {
                    if(disposed)return new Dictionary<MetricKind,double?>();
                    if(!opened){try{computer.Open();}catch{}opened=true;}
                    List<(HardwareType Hardware, ISensor Sensor)> sensors = new();
                    try { var selected=GpuTelemetrySelection.Pick(computer.Hardware); GpuId=selected?.Identifier.ToString(); foreach (IHardware item in computer.Hardware) ReadHardware(item, sensors); } catch { sensors.Clear(); }
                    Readings=HardwareTelemetry.Capture(sensors);
                    sensors.RemoveAll(s=>GpuTelemetrySelection.IsGpu(s.Item1)&&(GpuId==null||!s.Item2.Identifier.ToString().StartsWith(GpuId+"/",StringComparison.Ordinal)));
                    PowerScope=GpuTelemetrySelection.Power(sensors).Scope;
                    return HardwareMetricValues.Capture(sensors);
                }
            }
            private static void ReadHardware(IHardware hardware, ICollection<(HardwareType, ISensor)> sensors)
            {
                hardware.Update();
                foreach (ISensor sensor in hardware.Sensors)
                {
                    // Graphs use MonitorHistory; the library's separate day-long history is unused.
                    if(sensor.ValuesTimeWindow!=TimeSpan.Zero)sensor.ValuesTimeWindow=TimeSpan.Zero;
                    sensors.Add((hardware.HardwareType, sensor));
                }
                foreach (IHardware child in hardware.SubHardware) ReadHardware(child, sensors);
            }
            private static double? Find(IEnumerable<(HardwareType Hardware, ISensor Sensor)> all,
                Func<HardwareType, bool> filter, SensorType type, params string[] names)
            {
                return GpuTelemetrySelection.Find(all,filter,type,names);
            }
            private static bool IsCpu(HardwareType type) => type == HardwareType.Cpu;
            private static bool IsNvidia(HardwareType type) => type == HardwareType.GpuNvidia;
            private static bool IsGpu(HardwareType type) => type is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;
            public void Dispose() { lock (sync) { disposed=true;if(opened)try { computer.Close(); } catch { }opened=false; } }
        }

        internal sealed record NvidiaSnapshot(
            double? Load, double? Temperature, double? VramUsedGb, double? Power, double? Fan,
            double? RequestedLimit=null,double? EnforcedLimit=null,double? DefaultLimit=null,double? MaxLimit=null,ulong? Reasons=null,ulong? SupportedReasons=null,double? Clock=null,string? State=null,DateTime SampledAt=default,string? Id=null,string? Name=null)
        {
            internal double? Flag(ulong mask)=>Reasons is {} r&&SupportedReasons is {} s&&(s&mask)!=0?((r&mask)!=0?1:0):null;
        }
        internal sealed class NvidiaReader : IDisposable
        {
            private readonly object sync = new();
            private NvidiaSnapshot latest = new(null, null, null, null, null);
            private readonly Dictionary<string,NvidiaSnapshot> devices=new();
            private Process? ownedProcess;
            private ChildProcessLifetime? processLifetime;
            private bool disposed;
            private string? problem;
            internal string? Problem { get { lock(sync) return problem; } }
            internal int? ProcessId { get { lock(sync) return ownedProcess?.Id; } }
            internal bool IsLifetimeProtected { get { lock(sync) return ownedProcess is {} child && processLifetime?.Contains(child)==true; } }
            internal string? ProcessWorkingDirectory { get { lock(sync) return ownedProcess?.StartInfo.WorkingDirectory; } }
            public void Start()
            {
                lock(sync)
                {
                if(disposed || ownedProcess != null)return;
                Process? startingProcess=null;
                ChildProcessLifetime? startingLifetime=null;
                try
                {
                    startingLifetime=new ChildProcessLifetime();
                    Process? process = startingProcess = Process.Start(new ProcessStartInfo
                    {
                        FileName = NvidiaExecutable(),
                        Arguments = "--query-gpu=utilization.gpu,temperature.gpu,memory.used,power.draw,fan.speed,power.limit,enforced.power.limit,power.default_limit,power.max_limit,clocks_throttle_reasons.active,clocks_throttle_reasons.supported,clocks.gr,pstate,uuid,name --format=csv,noheader,nounits -lms 500",
                        UseShellExecute = false, RedirectStandardOutput = true,
                        RedirectStandardError = true, CreateNoWindow = true,
                        // Never inherit the installation directory: an orphaned
                        // driver utility previously kept it locked during setup.
                        WorkingDirectory = Environment.SystemDirectory
                    });
                    if (process != null)
                    {
                        // Fail closed if Windows cannot guarantee ownership.
                        startingLifetime.Attach(process);
                        processLifetime=startingLifetime;
                        startingLifetime=null;
                        ownedProcess=process;
                        startingProcess=null;
                        problem=null;
                        _ = Task.Run(async () => { try { await process.StandardError.ReadToEndAsync(); } catch { } });
                        _ = Task.Run(() => ReadLoop(process));
                    }
                }
                catch (System.ComponentModel.Win32Exception error) when(error.NativeErrorCode is 2 or 3)
                { problem="The NVIDIA sensor tool was not found. Install or repair the NVIDIA graphics driver for extended power, limit and clock readings. Other available readings remain enabled."; }
                catch { problem="The NVIDIA sensor tool could not start. Check the graphics driver and Windows security notifications."; }
                finally { startingLifetime?.Dispose();StopProcess(startingProcess); }
                }
            }
            private static string NvidiaExecutable()
            {
                foreach(string candidate in new[]{Path.Combine(Environment.SystemDirectory,"nvidia-smi.exe"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"NVIDIA Corporation","NVSMI","nvidia-smi.exe")})
                    if(File.Exists(candidate))return candidate;
                return "nvidia-smi.exe";
            }
            public void Dispose()
            {
                Process? process;
                ChildProcessLifetime? lifetime;
                lock(sync){if(disposed)return;disposed=true;process=ownedProcess;ownedProcess=null;lifetime=processLifetime;processLifetime=null;}
                lifetime?.Dispose();
                StopProcess(process);
            }
            private static void StopProcess(Process? process)
            {
                if(process==null)return;
                try { if(!process.HasExited)process.Kill();process.WaitForExit(1000); } catch { }
                process.Dispose();
            }
            public NvidiaSnapshot Read() { lock (sync) return devices.Count==1&&DateTime.UtcNow-latest.SampledAt<TimeSpan.FromSeconds(5)?latest:new(null,null,null,null,null); }
            internal NvidiaSnapshot ReadFor(GpuAdapterChoice? adapter)
            {
                lock(sync){
                    if(adapter?.Vendor!="GpuNvidia")return new(null,null,null,null,null);
                    string Key(string name)=>name.Replace("NVIDIA ","",StringComparison.OrdinalIgnoreCase).Trim();
                    var matches=devices.Values.Where(g=>g.Name!=null&&Key(g.Name).Equals(Key(adapter.Name),StringComparison.OrdinalIgnoreCase)&&DateTime.UtcNow-g.SampledAt<TimeSpan.FromSeconds(5)).ToArray();
                    return matches.Length==1&&GpuTelemetrySelection.Adapters.Count(a=>a.Vendor=="GpuNvidia"&&Key(a.Name).Equals(Key(adapter.Name),StringComparison.OrdinalIgnoreCase))==1?matches[0]:new(null,null,null,null,null);
                }
            }
            private static ulong? Hex(string[] parts,int index)=>index<parts.Length&&ulong.TryParse(parts[index].Replace("0x",""),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out ulong n)?n:null;
            private async Task ReadLoop(Process process)
            {
                try
                {
                    while (!process.HasExited)
                    {
                        string? line = await process.StandardOutput.ReadLineAsync();
                        if (line == null) break;
                        string[] parts = line.Split(',', StringSplitOptions.TrimEntries);
                        double? load = Parse(parts, 0), temp = Parse(parts, 1), memory = Parse(parts, 2);
                        lock (sync) { latest = new(load, temp,
                            memory.HasValue ? memory / 1024d : null,
                            Parse(parts, 3), Parse(parts, 4),Parse(parts,5),Parse(parts,6),Parse(parts,7),Parse(parts,8),Hex(parts,9),Hex(parts,10),Parse(parts,11),parts.ElementAtOrDefault(12),DateTime.UtcNow,parts.ElementAtOrDefault(13),parts.ElementAtOrDefault(14));
                            if(latest.Id is {} id)devices[id]=latest; }
                    }
                }
                catch { }
                finally
                {
                    bool owner;
                    ChildProcessLifetime? lifetime=null;
                    lock(sync){owner=ReferenceEquals(ownedProcess,process);if(owner){ownedProcess=null;lifetime=processLifetime;processLifetime=null;if(!disposed)problem="The NVIDIA sensor tool stopped. Some extended GPU readings are unavailable; check the graphics driver.";}}
                    if(owner){lifetime?.Dispose();StopProcess(process);}
                }
            }
            private static double? Parse(string[] parts, int index) =>
                index < parts.Length && double.TryParse(parts[index], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double value) && double.IsFinite(value) && value>=0 && (index!=0&&index!=4||value<=100) ? value : null;
        }

        private sealed class CpuReader
        {
            private ulong lastIdle, lastKernel, lastUser;
            private long[]? lastCoreIdle, lastCoreKernel, lastCoreUser;
            public double Read()
            {
                if (!GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user)) return 0;
                ulong total = kernel.Value - lastKernel + user.Value - lastUser;
                ulong idleDelta = idle.Value - lastIdle;
                lastIdle = idle.Value; lastKernel = kernel.Value; lastUser = user.Value;
                return total == 0 ? 0 : Math.Clamp((1 - idleDelta / (double)total) * 100, 0, 100);
            }

            public IReadOnlyList<double> ReadCoreLoads()
            {
                int itemSize = Marshal.SizeOf<ProcessorPerformanceInformation>();
                _ = NtQuerySystemInformation(8, IntPtr.Zero, 0, out int required);
                int bufferSize = Math.Max(Math.Max(1, Environment.ProcessorCount) * itemSize, required);
                IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
                try
                {
                    if (NtQuerySystemInformation(8, buffer, bufferSize, out int returned) != 0 || returned < itemSize)
                        return Array.Empty<double>();
                    int count = returned / itemSize;
                    long[] idle = new long[count], kernel = new long[count], user = new long[count];
                    double[] loads = new double[count];
                    for (int index = 0; index < count; index++)
                    {
                        ProcessorPerformanceInformation info = Marshal.PtrToStructure<ProcessorPerformanceInformation>(
                            IntPtr.Add(buffer, index * itemSize));
                        idle[index] = info.IdleTime; kernel[index] = info.KernelTime; user[index] = info.UserTime;
                        if (lastCoreIdle?.Length != count) continue;
                        long idleDelta = idle[index] - lastCoreIdle[index];
                        long totalDelta = kernel[index] - lastCoreKernel![index] + user[index] - lastCoreUser![index];
                        loads[index] = totalDelta <= 0 ? 0 : Math.Clamp(
                            100d * (totalDelta - idleDelta) / totalDelta, 0, 100);
                    }
                    lastCoreIdle = idle; lastCoreKernel = kernel; lastCoreUser = user;
                    return loads;
                }
                catch { return Array.Empty<double>(); }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }

        private static (ulong Total, ulong Available) ReadMemory()
        {
            MemoryStatus status = new() { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            GlobalMemoryStatusEx(ref status);
            return (status.TotalPhysical, status.AvailablePhysical);
        }
        [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
        [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
        [DllImport("ntdll.dll")] private static extern int NtQuerySystemInformation(
            int informationClass, IntPtr information, int informationLength, out int returnLength);
        [StructLayout(LayoutKind.Sequential)] private struct FileTime
        { public uint Low, High; public ulong Value => ((ulong)High << 32) | Low; }
        [StructLayout(LayoutKind.Sequential)] private struct ProcessorPerformanceInformation
        {
            public long IdleTime, KernelTime, UserTime, DpcTime, InterruptTime;
            public uint InterruptCount;
        }
        [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
        {
            public uint Length, MemoryLoad; public ulong TotalPhysical, AvailablePhysical,
                TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
        }
    }
}

