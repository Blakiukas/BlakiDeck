using System;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Ekrano_valdymas
{
    public partial class App : Application
    {
        public App()
        {
            TooltipPolicy.Initialize();
            SliderDefaults.InitializeWheel();
            AppDomain.CurrentDomain.UnhandledException+=(_,e)=>InteractionDiagnostics.RecordFailure("Unhandled terminating="+e.IsTerminating,e.ExceptionObject);
            DispatcherUnhandledException+=(_,e)=>InteractionDiagnostics.RecordFailure("DispatcherUnhandledException",e.Exception);
            TaskScheduler.UnobservedTaskException+=(_,e)=>InteractionDiagnostics.RecordFailure("UnobservedTaskException",e.Exception);
        }

        private const int SwRestore = 9;
        private Mutex? singleInstanceMutex;
        private bool ownsSingleInstanceMutex;

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr windowHandle, int command);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr windowHandle);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr windowHandle);

        protected override void OnStartup(StartupEventArgs e)
        {
#if PRODUCT_DIAGNOSTICS
            if(e.Args.Length>=2&&e.Args[0]=="--sensor-shutdown-probe") {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;
                string output=Path.GetFullPath(e.Args[1]);Directory.CreateDirectory(output);
                DiagnosticState.Root=Path.Combine(output,"isolated-state");DiagnosticState.SuppressAudioCapture=true;
                Dispatcher.BeginInvoke(new Action(async()=>{
                    try {
                        if(e.Args.Contains("--nvidia-owner"))await SensorShutdownProbe.RunNvidiaOwner(output);
                        else await SensorShutdownProbe.Run(output);
                        Shutdown(0);
                    }
                    catch(Exception error){File.WriteAllText(Path.Combine(output,"sensor-shutdown-error.txt"),error.ToString());Shutdown(1);}
                }));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--air-quality-probe") {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");DiagnosticState.SuppressAudioCapture=true;
                // Probe awaits include their own window cleanup and evidence writes.
                // Exit explicitly, as other long-running visual probes do, so WPF's
                // residual popup dispatcher work cannot keep the headless host alive.
                Dispatcher.BeginInvoke(new Action(async()=>Environment.Exit(await Ekrano_valdymas.MainWindow.RunAirQualityProbe(Path.GetFullPath(e.Args[1]),e.Args.Contains("--native-dpi")))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--settings-followup-probe") {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");DiagnosticState.SuppressAudioCapture=true;
                Dispatcher.BeginInvoke(new Action(async()=>Environment.Exit(await Ekrano_valdymas.MainWindow.RunSettingsFollowupProbe(Path.GetFullPath(e.Args[1]),e.Args.Contains("--native-dpi")))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--full-audit-probe") {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await Ekrano_valdymas.MainWindow.RunFullAuditProbe(Path.GetFullPath(e.Args[1]),e.Args.Contains("--native-dpi")))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--playlist-probe") {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Environment.Exit(await Ekrano_valdymas.MainWindow.RunPlaylistProbe(Path.GetFullPath(e.Args[1])))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--hud-visualizer-probe") {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Environment.Exit(await Ekrano_valdymas.MainWindow.RunHudVisualizerProbe(Path.GetFullPath(e.Args[1])))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--appearance-lab") {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"output","appearance-lab","isolated-state");
                new AppearanceLab(Path.GetFullPath(e.Args[1])).Show();return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--material-preview") {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await MusicConsoleControl.RenderMaterialPreviews(Path.GetFullPath(e.Args[1])))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--icon-style-probe") {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Environment.Exit(await Ekrano_valdymas.MainWindow.RunIconStyleProbe(Path.GetFullPath(e.Args[1])))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--classic-vu-startup-probe") {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Environment.Exit(await ClassicVuStartupProbe.Run(Path.GetFullPath(e.Args[1])))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--macro-states-probe") {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Environment.Exit(await MacroStatesProbe.Run(Path.GetFullPath(e.Args[1])))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--obs-macro-probe") {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Environment.Exit(await ObsMacroProbe.Run(Path.GetFullPath(e.Args[1])))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--menu-polish-probe") {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Environment.Exit(await Ekrano_valdymas.MainWindow.RunMenuPolishProbe(Path.GetFullPath(e.Args[1])))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--tab-transition-probe") {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Environment.Exit(await Ekrano_valdymas.MainWindow.RunTabTransitionProbe(Path.GetFullPath(e.Args[1])))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--launch-skin-probe") {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Environment.Exit(await Ekrano_valdymas.MainWindow.RunLaunchSkinProbe(Path.GetFullPath(e.Args[1])))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--tooltip-marker-probe") {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Environment.Exit(await Ekrano_valdymas.MainWindow.RunTooltipMarkerProbe(Path.GetFullPath(e.Args[1])))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--light-skin-probe") {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Environment.Exit(await Ekrano_valdymas.MainWindow.RunLightSkinProbe(Path.GetFullPath(e.Args[1]),e.Args.Contains("--hold"),e.Args.Contains("--layers"),e.Args.Contains("--ink-fixtures"),e.Args.Contains("--minimize-layers")))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--list-refinement-probe") {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Environment.Exit(await Ekrano_valdymas.MainWindow.RunListRefinementProbe(Path.GetFullPath(e.Args[1])))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--weather-locations-probe") {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                // Isolated probe has already closed its window and flushed evidence.
                // Avoid Wisp tablet discovery during diagnostic dispatcher teardown.
                Dispatcher.BeginInvoke(new Action(async()=>Environment.Exit(await Ekrano_valdymas.MainWindow.RunWeatherLocationsProbe(Path.GetFullPath(e.Args[1]),e.Args.Contains("--origin-only")))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--player-global-dim-probe") {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await Ekrano_valdymas.MainWindow.RunPlayerGlobalDimProbe(Path.GetFullPath(e.Args[1])))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--web-interaction-probe") {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await Ekrano_valdymas.MainWindow.RunWebInteractionProbe(Path.GetFullPath(e.Args[1]),e.Args.Contains("--verify")))));return;
            }
            if(e.Args.Length==2&&(e.Args[0]=="--dim-bounds-probe"||e.Args[0]=="--clock-library-probe")) {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await Ekrano_valdymas.MainWindow.RunDimBoundsProbe(Path.GetFullPath(e.Args[1]),e.Args[0]=="--clock-library-probe"))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--five-fixes-probe") {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await Ekrano_valdymas.MainWindow.RunFiveFixesProbe(Path.GetFullPath(e.Args[1]),e.Args.Contains("--web")))));return;
            }
            if(e.Args.Length==2&&e.Args[0]=="--youtube-display-live-probe") {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await YouTubeDisplayLiveProbe.Run(Path.GetFullPath(e.Args[1])))));return;
            }
            if(e.Args.Length==2&&e.Args[0]=="--youtube-display-probe") {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await YouTubeDisplayProbe.Run(Path.GetFullPath(e.Args[1])))));return;
            }
            if(e.Args.Length==2&&e.Args[0]=="--hue-web-probe") {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await Ekrano_valdymas.MainWindow.RunHueWebProbe(Path.GetFullPath(e.Args[1])))));return;
            }
            if(e.Args.Length==2&&e.Args[0]=="--tab-refinement-probe") {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await Ekrano_valdymas.MainWindow.RunTabRefinementProbe(Path.GetFullPath(e.Args[1])))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--surface-refinement-probe")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await Ekrano_valdymas.MainWindow.RunSurfaceRefinementProbe(Path.GetFullPath(e.Args[1]),e.Args.Contains("--verify")))));return;
            }
            if(e.Args.Length==2&&e.Args[0]=="--ram-probe")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await Ekrano_valdymas.MainWindow.RunRamProbe(Path.GetFullPath(e.Args[1])))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--skin-continuity-probe") { ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await Ekrano_valdymas.MainWindow.RunSkinContinuityProbe(Path.GetFullPath(e.Args[1])))));return; }
            if(e.Args.Length>=2&&e.Args[0]=="--panel-colors-probe") { ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await Ekrano_valdymas.MainWindow.RunPanelColorsProbe(Path.GetFullPath(e.Args[1])))));return; }
            if(e.Args.Length>=2&&e.Args[0]=="--panel-metal-probe")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await Ekrano_valdymas.MainWindow.RunPanelMetalProbe(Path.GetFullPath(e.Args[1]),e.Args.Contains("--surfaces-only")))));return;
            }
            if(e.Args.Length==2&&e.Args[0]=="--camera-readonly-probe")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.GetFullPath(e.Args[1]);
                Dispatcher.BeginInvoke(new Action(async()=>{Directory.CreateDirectory(DiagnosticState.Root);using var activity=new CameraActivity();await Task.Delay(3000);var rows=CameraControl.Devices().Select(d=>new{d.Id,d.Name,enabled=CameraControl.Enabled(d.Id),streaming=activity.Streaming(d.Id)}).ToArray();File.WriteAllText(Path.Combine(DiagnosticState.Root,"camera-readonly.json"),System.Text.Json.JsonSerializer.Serialize(new{activity.Running,activity.Error,activity.Diagnostic,devices=rows},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));Shutdown();}));return;
            }
            if(e.Args.Length==2&&e.Args[0]=="--theme-vu-probe")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await Ekrano_valdymas.MainWindow.RunThemeVuProbe(e.Args[1]))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--tab-player-probe")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.GetFullPath(e.Args[1]);Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await Ekrano_valdymas.MainWindow.RunTabPlayerProbe(DiagnosticState.Root))));return;
            }
            if(e.Args.Length==3&&e.Args[0]=="--camera-device-action")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;
                try{if(e.Args[1] is not ("On" or "Off"))throw new ArgumentException();Shutdown(CameraControl.SetEnabled(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(e.Args[2])),e.Args[1]=="On"));}catch{Shutdown(13);}return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--idle-work-probe")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;
                DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await IdleWorkProbe.Run(Path.GetFullPath(e.Args[1])))));return;
            }
            if(e.Args.Length>=2 && e.Args[0]=="--vu-monitor-probe")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;
                DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await VuMonitorProbe.Run(Path.GetFullPath(e.Args[1]),e.Args.Contains("--lifecycle-only")))));
                return;
            }

            if(e.Args.Length >= 2 && e.Args[0] == "--classic-vu-probe")
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                DiagnosticState.Root = Path.Combine(Path.GetFullPath(e.Args[1]), "isolated-state");
                Dispatcher.BeginInvoke(new Action(() => Shutdown(ClassicVuProbe.Run(Path.GetFullPath(e.Args[1])))));
                return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--needle-display-compare")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;
                DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(()=> {
                    if(e.Args.Contains("--native")) Ekrano_valdymas.MainWindow.OpenNativeNeedleComparison(e.Args[1]);
                    else Ekrano_valdymas.MainWindow.OpenNeedleDisplayComparison(e.Args[1]);
                }));return;
            }
            if (e.Args.Contains("--pulse-contour-preview"))
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                DiagnosticState.Root = Path.Combine(Path.GetTempPath(), "BlakiDeck-contour-preview");
                Dispatcher.BeginInvoke(new Action(ContourPreview.Open)); return;
            }
            if (e.Args.Length >= 2 && e.Args[0] == "--contour-audio-probe")
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                DiagnosticState.Root = Path.Combine(Path.GetFullPath(e.Args[1]), "isolated-state");
                Dispatcher.BeginInvoke(new Action(async () => Shutdown(await ContourPreview.ProbeAudio(e.Args[1])))); return;
            }
            if (e.Args.Length >= 3 && e.Args[0] == "--pulse-contour-probe")
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                DiagnosticState.Root = Path.Combine(Path.GetFullPath(e.Args[1]), "isolated-state");
                Dispatcher.BeginInvoke(new Action(async () => Shutdown(await PulseContourProbe.Run(Path.GetFullPath(e.Args[1]), e.Args[2]))));
                return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--mechanical-audit-probe")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await MechanicalAuditProbe.Run(Path.GetFullPath(e.Args[1]),e.Args.Contains("--live"),e.Args.Contains("--preview")))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--player-controls-probe")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await PlayerControlsProbe.Run(Path.GetFullPath(e.Args[1])))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--refinement3-probe")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await Ekrano_valdymas.MainWindow.RunRefinement3Probe(Path.GetFullPath(e.Args[1]),e.Args.Contains("--verify")))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--tab-stability-probe")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;
                DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await QuickLaunchBar.RunTabStabilityProbe(e.Args[1]))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--appearance-live-probe")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await Ekrano_valdymas.MainWindow.RunAppearanceLiveProbe(Path.GetFullPath(e.Args[1]),e.Args.Contains("--verify"),e.Args.Contains("--minimum-only")))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--appearance-probe")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await Ekrano_valdymas.MainWindow.RunAppearanceProbe(Path.GetFullPath(e.Args[1]),e.Args.Contains("--interactive")))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--catalog-probe")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await Ekrano_valdymas.MainWindow.RunCatalogProbe(Path.GetFullPath(e.Args[1])))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--shortcut-interaction-probe")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;
                DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await Ekrano_valdymas.MainWindow.RunShortcutInteractionProbe(e.Args[1],e.Args.Contains("--interactive")))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--macro-probe")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.GetFullPath(e.Args[1]);Directory.CreateDirectory(DiagnosticState.Root);
                Dispatcher.BeginInvoke(new Action(async()=>{try{await MacroProbe.Run(DiagnosticState.Root);Shutdown(0);}catch(Exception ex){File.WriteAllText(Path.Combine(DiagnosticState.Root,"error.txt"),ex.ToString());Shutdown(1);}}));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--macro-bar-probe")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await Ekrano_valdymas.MainWindow.RunMacroBarProbeAsync(e.Args[1]))));return;
            }            if(e.Args.Length>=2&&e.Args[0]=="--completion-probe")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;
                DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await Ekrano_valdymas.MainWindow.RunCompletionProbeAsync(e.Args[1]))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--independent-cards-probe")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;
                DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await Ekrano_valdymas.MainWindow.RunIndependentCardsProbeAsync(e.Args[1]))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--single-module-preview")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;
                DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await Ekrano_valdymas.MainWindow.RunSingleModulePreviewAsync(e.Args[1],e.Args.Contains("--standby-only"),e.Args.Contains("--language-only"),e.Args.Contains("--adjustment-only")))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--tab-size-probe")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;
                DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await Ekrano_valdymas.MainWindow.RunTabSizeProbeAsync(e.Args[1]))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--web-refresh-probe")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;
                DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await Ekrano_valdymas.MainWindow.RunWebRefreshProbe(e.Args[1]))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--web-images-probe")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;
                DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await Ekrano_valdymas.MainWindow.RunWebImagesProbeAsync(e.Args[1]))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--rotary-transition-probe")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;
                DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await Ekrano_valdymas.MainWindow.RunRotaryTransitionProbeAsync(e.Args[1],e.Args.Contains("--native"),e.Args.Contains("--native96"),e.Args.Contains("--motion"),e.Args.Contains("--full")))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--grouped-menu-probe")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;
                DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await Ekrano_valdymas.MainWindow.RunGroupedMenuProbeAsync(e.Args[1]))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--linked-web-regression-probe")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;DiagnosticState.Root=Path.Combine(Path.GetFullPath(e.Args[1]),"isolated-state");
                Dispatcher.BeginInvoke(new Action(async()=>Shutdown(await Ekrano_valdymas.MainWindow.RunLinkedWebRegressionProbe(e.Args[1],e.Args.Contains("--verify"),e.Args.Contains("--music")))));return;
            }
            if(e.Args.Length>=2&&e.Args[0]=="--music-live-meter-probe")
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;
                Dispatcher.BeginInvoke(new Action(async()=>
                {
                    using var meter=new MusicSourceMeter();var samples=new System.Collections.Generic.List<object>();
                    int count=e.Args.Length>2&&int.TryParse(e.Args[2],out int requested)?Math.Clamp(requested,200,12000):200;
                    for(int i=0;i<count;i++){meter.Read(new[]{"Spotify"},out double l,out double r);samples.Add(new{t=i*.05,l,r});await Task.Delay(50);}
                    File.WriteAllText(e.Args[1],System.Text.Json.JsonSerializer.Serialize(samples));Shutdown();
                }));return;
            }
            if (e.Args.Length >= 2 && e.Args[0] == "--music-audio-volume-probe")
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Dispatcher.BeginInvoke(new Action(async () => Shutdown(await MusicAudioVolumeProbe.RunAsync(e.Args[1]))));return;
            }
            if (e.Args.Length >= 2 && e.Args[0] == "--music-console-probe")
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                DiagnosticState.Root = Path.Combine(Path.GetFullPath(e.Args[1]), "isolated-state");
                Directory.CreateDirectory(DiagnosticState.Root);
                Dispatcher.BeginInvoke(new Action(async () => Shutdown(await Ekrano_valdymas.MainWindow.RunMusicConsoleProbeAsync(e.Args[1]))));
                return;
            }
            if (e.Args.Length >= 2 && e.Args[0] is "--window-transition-probe" or "--web-clarity-probe" or "--startup-state-probe")
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                DiagnosticState.Root = Path.Combine(Path.GetFullPath(e.Args[1]), "isolated-state");
                DiagnosticState.HideOtherWindows = e.Args.Contains("--hide-other-windows");
                Directory.CreateDirectory(DiagnosticState.Root);
                Dispatcher.BeginInvoke(new Action(async () =>
                    Shutdown(e.Args[0] == "--startup-state-probe"
                        ? await Ekrano_valdymas.MainWindow.RunStartupStateProbeAsync(e.Args[1])
                        : e.Args[0] == "--web-clarity-probe"
                        ? await Ekrano_valdymas.MainWindow.RunWebClarityProbeAsync(e.Args[1])
                        : await Ekrano_valdymas.MainWindow.RunTransitionProbeAsync(e.Args[1]))));
                return;
            }
            int spotifyConsoleVisualArgument = Array.FindIndex(e.Args, argument =>
                argument.Equals("--spotify-console-visual-probe", StringComparison.OrdinalIgnoreCase));
            if (spotifyConsoleVisualArgument >= 0)
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                base.OnStartup(e);
                string outputDirectory = spotifyConsoleVisualArgument + 1 < e.Args.Length
                    ? e.Args[spotifyConsoleVisualArgument + 1]
                    : Path.Combine(Path.GetTempPath(), "BlakiDeck-spotify-console-visual-probe");
                _ = RunSpotifyConsoleVisualProbeAsync(outputDirectory);
                return;
            }
            int audioLevelArgument = Array.FindIndex(e.Args, argument =>
                argument.Equals("--audio-level-probe", StringComparison.OrdinalIgnoreCase));
            if (audioLevelArgument >= 0)
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                base.OnStartup(e);
                string outputDirectory = audioLevelArgument + 1 < e.Args.Length
                    ? e.Args[audioLevelArgument + 1]
                    : Path.Combine(Path.GetTempPath(), "BlakiDeck-audio-level-probe");
                _ = RunAudioLevelProbeAsync(outputDirectory);
                return;
            }
            int analogVuVisualArgument = Array.FindIndex(e.Args, argument =>
                argument.Equals("--analog-vu-visual-probe", StringComparison.OrdinalIgnoreCase));
            if (analogVuVisualArgument >= 0)
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                base.OnStartup(e);
                string outputDirectory = analogVuVisualArgument + 1 < e.Args.Length
                    ? e.Args[analogVuVisualArgument + 1]
                    : Path.Combine(Path.GetTempPath(), "BlakiDeck-analog-vu-visual-probe");
                _ = RunAnalogVuVisualProbeAsync(outputDirectory);
                return;
            }
            int geometryProbeArgument = Array.FindIndex(e.Args, argument =>
                argument.Equals("--ui-geometry-probe", StringComparison.OrdinalIgnoreCase));
            if (geometryProbeArgument >= 0)
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                base.OnStartup(e);
                string outputDirectory = geometryProbeArgument + 1 < e.Args.Length
                    ? e.Args[geometryProbeArgument + 1]
                    : Path.Combine(Path.GetTempPath(), "BlakiDeck-ui-geometry-probe");
                int activeTab = geometryProbeArgument + 2 < e.Args.Length &&
                                int.TryParse(e.Args[geometryProbeArgument + 2], out int parsedTab)
                    ? parsedTab : 1;
                _ = RunUiGeometryProbeAsync(outputDirectory, activeTab);
                return;
            }
            int webDashboardGeometryArgument = Array.FindIndex(e.Args, argument =>
                argument.Equals("--web-dashboard-geometry-probe", StringComparison.OrdinalIgnoreCase));
            if (webDashboardGeometryArgument >= 0)
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                base.OnStartup(e);
                string outputDirectory = webDashboardGeometryArgument + 1 < e.Args.Length
                    ? e.Args[webDashboardGeometryArgument + 1]
                    : Path.Combine(Path.GetTempPath(), "BlakiDeck-web-dashboard-geometry-probe");
                _ = RunWebDashboardGeometryProbeAsync(outputDirectory);
                return;
            }
            int obsVisualArgument = Array.FindIndex(e.Args, argument =>
                argument.Equals("--obs-visual-probe", StringComparison.OrdinalIgnoreCase));
            if (obsVisualArgument >= 0)
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                base.OnStartup(e);
                string outputDirectory = obsVisualArgument + 1 < e.Args.Length
                    ? e.Args[obsVisualArgument + 1]
                    : Path.Combine(Path.GetTempPath(), "BlakiDeck-obs-visual-probe");
                _ = RunObsVisualProbeAsync(outputDirectory);
                return;
            }
            if (e.Args.Any(argument => argument.Equals("--color-dialog-probe", StringComparison.OrdinalIgnoreCase)))
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                base.OnStartup(e);
                int exitCode = 1;
                try
                {
                    AccentColorDialog dialog = new(
                        Color.FromRgb(30, 215, 96), Colors.White, Color.FromRgb(30, 215, 96),
                        Color.FromRgb(44, 44, 44), Colors.White, 70, 95, 80, 100, 69);
                    if (dialog.DashboardColor != Color.FromRgb(30, 215, 96) ||
                        dialog.SelectedColor != Color.FromRgb(30, 215, 96) ||
                        dialog.DashboardIntensity != 70)
                        throw new InvalidOperationException("Color dialog changed the supplied settings during construction.");
                    dialog.Close();
                    ScreenColorSamplerWindow sampler = new();
                    sampler.Close();
                    exitCode = 0;
                }
                catch (Exception exception)
                {
                    File.WriteAllText(Path.Combine(AppContext.BaseDirectory,
                        "color-dialog-probe-error.txt"), exception.ToString());
                }
                Shutdown(exitCode);
                return;
            }
            int colorGeometryArgument = Array.FindIndex(e.Args, argument =>
                argument.Equals("--color-dialog-geometry-probe", StringComparison.OrdinalIgnoreCase));
            if (colorGeometryArgument >= 0)
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                base.OnStartup(e);
                string outputDirectory = colorGeometryArgument + 1 < e.Args.Length
                    ? e.Args[colorGeometryArgument + 1]
                    : Path.Combine(Path.GetTempPath(), "BlakiDeck-color-dialog-geometry-probe");
                _ = RunColorDialogGeometryProbeAsync(outputDirectory);
                return;
            }
            if (e.Args.Any(argument => argument.Equals("--module-dialog-probe", StringComparison.OrdinalIgnoreCase)))
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                base.OnStartup(e);
                int exitCode = 1;
                try
                {
                    ModuleSelectionDialog dialog = new(new[] { "Music", "Hue", "Weather" }, 0);
                    dialog.Close();
                    exitCode = 0;
                }
                catch (Exception exception)
                {
                    File.WriteAllText(Path.Combine(AppContext.BaseDirectory,
                        "module-dialog-probe-error.txt"), exception.ToString());
                }
                Shutdown(exitCode);
                return;
            }
            if (e.Args.Any(argument => argument.Equals("--top-bar-layout-probe", StringComparison.OrdinalIgnoreCase)))
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                base.OnStartup(e);
                bool persistence = QuickLaunchBar.VerifyLayoutPersistence();
                if (!persistence)
                    File.WriteAllText(Path.Combine(AppContext.BaseDirectory,
                        "top-bar-layout-probe-error.txt"), QuickLaunchBar.LayoutPersistenceProbeDetails);
                Shutdown(TopBarLayoutPlanner.Verify() && persistence && ShortcutsGridPlanner.Verify() &&
                    ModuleLayoutPlanner.Verify() && DoubleTapDetector.Verify() &&
                    UsageOverlayWindow.VerifyGeometry() &&
                    UsageTelemetryView.VerifyStableGeometry() ? 0 : 1);
                return;
            }
            if (e.Args.Any(argument => argument.Equals("--renderer-probe", StringComparison.OrdinalIgnoreCase)))
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                base.OnStartup(e);
                _ = RunRendererProbeAsync();
                return;
            }
            if (e.Args.Any(argument => argument.Equals("--webview-probe", StringComparison.OrdinalIgnoreCase)))
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                base.OnStartup(e);
                _ = RunWebViewProbeAsync();
                return;
            }
            if (e.Args.Any(argument => argument.Equals("--web-shortcuts-probe", StringComparison.OrdinalIgnoreCase)))
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                base.OnStartup(e);
                int exitCode = 1;
                try
                {
                    WebShortcutsWidget.WebShortcut stateProbe = new("Example", "https://example.com/")
                    {
                        LastUrl = "https://example.com/continued"
                    };
                    string stateJson = System.Text.Json.JsonSerializer.Serialize(stateProbe);
                    WebShortcutsWidget.WebShortcut? restoredState =
                        System.Text.Json.JsonSerializer.Deserialize<WebShortcutsWidget.WebShortcut>(stateJson);
                    if (restoredState?.Url != stateProbe.Url || restoredState.LastUrl != stateProbe.LastUrl)
                        throw new InvalidOperationException("The last Web URL was not persisted independently.");
                    var sixLinks = Enumerable.Range(1, 6)
                        .Select(index => new WebShortcutsWidget.WebShortcut(
                            $"Link {index}", $"https://example.com/{index}"));
                    var sevenTiles = WebShortcutsWidget.BuildVisibleTiles(sixLinks);
                    if (sevenTiles.Count != 7 || sevenTiles.Take(6).Any(tile => tile.IsAdd) || !sevenTiles[^1].IsAdd)
                        throw new InvalidOperationException("Unlimited Web links must be followed by one final add tile.");
                    WebShortcutsWidget widget = new();
                    if (!widget.ExistingPreviewsDecoded)
                        throw new InvalidOperationException("A saved web preview could not be decoded.");
                    Window owner = new() { Content = widget, ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
                    owner.Show();
                    WebShortcutDialog editor = new("Example", "https://example.com/") { Owner = owner };
                    editor.Close();
                    FullscreenBrowserWindow browser = new("https://example.com/", owner);
                    browser.Close(); owner.Close();
                    exitCode = 0;
                }
                catch (Exception exception)
                {
                    File.WriteAllText(Path.Combine(AppContext.BaseDirectory,
                        "web-shortcuts-probe-error.txt"), exception.ToString());
                }
                Shutdown(exitCode);
                return;
            }

#endif
            int rendererArgument = Array.FindIndex(e.Args, argument =>
                argument.Equals("--visual-renderer", StringComparison.OrdinalIgnoreCase));
            if (rendererArgument >= 0)
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                base.OnStartup(e);
                int parentProcessId = rendererArgument + 1 < e.Args.Length &&
                                      int.TryParse(e.Args[rendererArgument + 1], out int parsedParent)
                    ? parsedParent : 0;
                string? pipeName = rendererArgument + 2 < e.Args.Length
                    ? e.Args[rendererArgument + 2] : null;
                _ = RunRendererWorkerAsync(parentProcessId, pipeName);
                return;
            }

            if (e.Args.Any(argument => argument.Equals("--sensor-helper", StringComparison.OrdinalIgnoreCase)))
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                base.OnStartup(e);
                int helperIndex=Array.FindIndex(e.Args,a=>a.Equals("--sensor-helper",StringComparison.OrdinalIgnoreCase));
                _ = RunSensorHelperAsync(helperIndex+1<e.Args.Length?e.Args[helperIndex+1]:null);
                return;
            }

            string? resetToken = e.Args.Length == 2 && e.Args[0] == "--apply-profile-reset" ? e.Args[1] : null;
            if(resetToken!=null)
            {
                try { ApplicationProfileReset.WaitForPreviousProcess(resetToken); }
                catch(Exception error) { MessageBox.Show(error.Message,"Reset could not finish",MessageBoxButton.OK,MessageBoxImage.Error);Shutdown(1);return; }
            }
            singleInstanceMutex = new Mutex(
                initiallyOwned: true,
                name: ProductProfile.InstanceMutex,
                createdNew: out bool createdNew);
            ownsSingleInstanceMutex = createdNew;

            if (!createdNew)
            {
                ActivateExistingInstance();
                Shutdown();
                return;
            }

            if(resetToken!=null)
            {
                try { ApplicationProfileReset.ApplyPending(resetToken); }
                catch(Exception error) { MessageBox.Show(error.Message,"Reset could not finish",MessageBoxButton.OK,MessageBoxImage.Error);Shutdown(1);return; }
            }

            base.OnStartup(e);

            int startupDiagnosticArgument = Array.FindIndex(e.Args, argument =>
                argument.Equals("--startup-diagnostic", StringComparison.OrdinalIgnoreCase));
            if(startupDiagnosticArgument>=0&&startupDiagnosticArgument+1<e.Args.Length)
                InteractionDiagnostics.Enable(e.Args[startupDiagnosticArgument+1]);

            MainWindow window = new();
            window.Title = "BlakiDeck " + typeof(App).Assembly.GetName().Version?.ToString(3);
            MainWindow = window;
            if(InteractionDiagnostics.Enabled)
            {
                void Log(string message) => InteractionDiagnostics.Log(message);
                AppDomain.CurrentDomain.ProcessExit+=(_,_)=>Log("ProcessExit PID="+Environment.ProcessId);
                Log("MainWindow constructed");
                window.SourceInitialized += (_, _) => Log($"SourceInitialized hwnd={new System.Windows.Interop.WindowInteropHelper(window).Handle}");
                window.Loaded += (_, _) => Log($"Loaded visible={window.IsVisible} state={window.WindowState} bounds={window.Left},{window.Top},{window.ActualWidth},{window.ActualHeight}");
                window.IsVisibleChanged += (_, _) => Log($"IsVisibleChanged visible={window.IsVisible} state={window.WindowState}");
                window.StateChanged += (_, _) => Log($"StateChanged visible={window.IsVisible} state={window.WindowState}");
                window.Closed += (_, _) => Log("Closed");
                window.Unloaded += (_, _) => Log("Unloaded");
                DispatcherUnhandledException += (_, args) => Log($"DispatcherUnhandledException {args.Exception}");
                DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
                int ticks = 0;
                timer.Tick += (_, _) =>
                {
                    Log($"Tick {++ticks} visible={window.IsVisible} state={window.WindowState} hwnd={new System.Windows.Interop.WindowInteropHelper(window).Handle} bounds={window.Left},{window.Top},{window.ActualWidth},{window.ActualHeight}");
                    if (ticks >= 12) timer.Stop();
                };
                timer.Start();
            }
            window.Show();
        }

        private static void ActivateExistingInstance()
        {
            int currentProcessId = Environment.ProcessId;
            foreach (Process process in Process.GetProcessesByName("BlakiDeck"))
            {
                using (process)
                {
                    if (process.Id == currentProcessId)
                        continue;

                    // A development copy can be running concurrently. Activate
                    // only another instance of this installed executable.
                    try
                    {
                        if (!string.Equals(process.MainModule?.FileName, Environment.ProcessPath,
                            StringComparison.OrdinalIgnoreCase)) continue;
                    }
                    catch (System.ComponentModel.Win32Exception) { continue; }
                    catch (InvalidOperationException) { continue; }

                    process.Refresh();
                    IntPtr windowHandle = process.MainWindowHandle;
                    if (windowHandle == IntPtr.Zero)
                        continue;

                    ShowWindowAsync(windowHandle, SwRestore);
                    BringWindowToTop(windowHandle);
                    SetForegroundWindow(windowHandle);
                    return;
                }
            }
        }

#if PRODUCT_DIAGNOSTICS
        private async Task RunUiGeometryProbeAsync(string outputDirectory, int activeTab)
        {
            int exitCode = await UiGeometryProbe.RunAsync(outputDirectory, activeTab);
            Shutdown(exitCode);
        }

        private async Task RunAnalogVuVisualProbeAsync(string outputDirectory)
        {
            int exitCode = await AnalogVuVisualProbe.RunAsync(outputDirectory);
            Shutdown(exitCode);
        }

        private async Task RunAudioLevelProbeAsync(string outputDirectory)
        {
            int exitCode = await AudioLevelProbe.RunAsync(outputDirectory);
            Shutdown(exitCode);
        }

        private async Task RunSpotifyConsoleVisualProbeAsync(string outputDirectory)
        {
            int exitCode = await SpotifyConsoleVisualProbe.RunAsync(outputDirectory);
            Shutdown(exitCode);
        }



        private async Task RunColorDialogGeometryProbeAsync(string outputDirectory)
        {
            int exitCode = await ColorDialogGeometryProbe.RunAsync(outputDirectory);
            Shutdown(exitCode);
        }

        private async Task RunWebDashboardGeometryProbeAsync(string outputDirectory)
        {
            int exitCode = await WebDashboardGeometryProbe.RunAsync(outputDirectory);
            Shutdown(exitCode);
        }

        private async Task RunObsVisualProbeAsync(string outputDirectory)
        {
            int exitCode = await ObsVisualProbe.RunAsync(outputDirectory);
            Shutdown(exitCode);
        }

        private async Task RunWebViewProbeAsync()
        {
            int exitCode = 1;
            try
            {
                WebViewProbeWindow probe = new();
                exitCode = await probe.RunAsync() ? 0 : 2;
                if (exitCode != 0)
                    File.WriteAllText(Path.Combine(AppContext.BaseDirectory,
                        "webview-probe-error.txt"), WebViewProbeWindow.LastResult);
                probe.Close();
            }
            catch (Exception exception)
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory,
                    "webview-probe-error.txt"), exception.ToString());
            }
            Shutdown(exitCode);
        }

#endif
        private async Task RunRendererWorkerAsync(int parentProcessId, string? pipeName)
        {
            try
            {
                await IsolatedRendererWorker.RunAsync(parentProcessId, pipeName);
            }
            finally
            {
                Shutdown();
            }
        }

#if PRODUCT_DIAGNOSTICS
        private async Task RunRendererProbeAsync()
        {
            int exitCode = 1;
            try
            {
                await using RendererProcessHost host = await RendererProcessHost.StartAsync();
                exitCode = await host.PingAsync() ? 0 : 2;
            }
            catch { }
            finally { Shutdown(exitCode); }
        }

#endif
        private async Task RunSensorHelperAsync(string? pipeName)
        {
            try
            {
                await AdvancedSensorServer.RunAsync(pipeName);
            }
            finally
            {
                Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            MacroRunner.Current.Stop();
            if (ownsSingleInstanceMutex)
            {
                singleInstanceMutex?.ReleaseMutex();
            }

            singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}









