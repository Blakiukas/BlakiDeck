using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Ekrano_valdymas
{
    public partial class OBSWidget : UserControl
    {
        private Brush NeutralTextBrush =>
            Application.Current.TryFindResource("PrimaryTextBrush") as Brush ?? Brushes.White;
        private static readonly SolidColorBrush ActiveRed = new(Color.FromRgb(255, 74, 80));
        private static readonly Brush ActiveRedBackground = new SolidColorBrush(Color.FromArgb(255, 40, 25, 28));
        private static readonly Brush DefaultBackground = new SolidColorBrush(Color.FromArgb(255, 27, 23, 20));
        private static readonly Brush DefaultBorder = new SolidColorBrush(Color.FromArgb(39, 255, 255, 255));
        private static readonly SolidColorBrush ActiveGreen = new(Color.FromRgb(30, 215, 96));

        private readonly ObsWebSocketClient _obsClient = new();
        private readonly DispatcherTimer _uiTimer;
        private readonly bool _diagnosticMode;
        private bool _externalControlsActive;
        private bool _recording;
        private bool _replayBufferActive;
        private bool _recordPulseRunning;
        private bool _connectionAttemptRunning;
        private bool _connectionActionRunning;
        private bool _saveAnimationRunning;
        private int _recordCommandRunning;
        private int _replayBufferCommandRunning;
        private DateTime? _recordingStartedAt;
        private DateTime _nextReconnectAttempt = DateTime.MinValue;
        private DateTime _nextHotkeyCheck = DateTime.MinValue;
        private string _hotkeyFileState = "";
        private string _startRecordHotkey = "NO HOTKEY";
        private string _stopRecordHotkey = "NO HOTKEY";
        private string _startReplayHotkey = "NO HOTKEY";
        private string _stopReplayHotkey = "NO HOTKEY";
        private string _saveReplayHotkey = "NO HOTKEY";

        public bool IsRecording => _recording;
        public bool IsReplayBufferActive => _replayBufferActive;
        public bool IsObsApplicationRunning => IsObsRunning();
        public TimeSpan RecordingElapsed => _recordingStartedAt is DateTime startedAt
            ? DateTime.UtcNow - startedAt
            : TimeSpan.Zero;
        public event EventHandler? ControlStateChanged;

        public OBSWidget() : this(false)
        {
        }

        internal OBSWidget(bool diagnosticMode)
        {
            InitializeComponent();

            _diagnosticMode = diagnosticMode;
            _obsClient.StateChanged += ObsClient_StateChanged;

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _uiTimer.Tick += UiTimer_Tick;

            UpdateConnectionButton();
            ApplyLanguage();
            Loaded += ObsWidget_Loaded;
            Unloaded += ObsWidget_Unloaded;
        }

        private void ObsWidget_Loaded(object sender, RoutedEventArgs e)
        {
            LanguageManager.Changed -= LanguageManager_Changed;
            LanguageManager.Changed += LanguageManager_Changed;
            if (!_diagnosticMode)
            {
                ApplyLanguage();
                UpdateUi();
            }
            UpdateObsActivity();
        }

        private void ObsWidget_Unloaded(object sender, RoutedEventArgs e)
        {
            LanguageManager.Changed -= LanguageManager_Changed;
            SetRecordingPulse(false);
            UpdateObsActivity();
        }

        // Legacy top-bar recording/replay controls share this controller even
        // while its dashboard module is absent.
        internal void SetExternalControlsActive(bool active)
        {
            if (_externalControlsActive == active) return;
            _externalControlsActive = active;
            UpdateObsActivity();
        }

        private void UpdateObsActivity()
        {
            bool active = IsLoaded || _externalControlsActive;
            if (!_diagnosticMode && active)
            {
                bool wasActive = _uiTimer.IsEnabled;
                _uiTimer.Start();
                if (!wasActive) _ = ConnectAsync();
            }
            else
            {
                _uiTimer.Stop();
                if (!active) _obsClient.Dispose();
            }
        }

        private async Task ConnectAsync()
        {
            if (_connectionAttemptRunning)
                return;

            _connectionAttemptRunning = true;
            SetStatus("Connecting...", "Jungiamasi...", Brushes.White);

            try
            {
                await _obsClient.ConnectAsync();
                await RefreshStateAsync();
            }
            catch (ObsWebSocketDisabledException)
            {
                _obsClient.Dispose();
                SetStatus(
                    "Enable WebSocket in OBS → Tools → WebSocket Server Settings",
                    "Įjunkite WebSocket: OBS → Tools → WebSocket Server Settings",
                    Brushes.Orange);
                _nextReconnectAttempt = DateTime.UtcNow.AddSeconds(15);
            }
            catch (ObsSetupException ex)
            {
                _obsClient.Dispose();
                SetStatus(ex.Message, ex.Lithuanian, Brushes.Orange);
                _nextReconnectAttempt = DateTime.UtcNow.AddSeconds(15);
            }
            catch
            {
                // OBS paleidimo metu WebSocket gali jau priimti ryšį, nors pirmosios
                // būsenos užklausos dar neparuoštos. Uždarius nepilną ryšį laikmatis
                // po kelių sekundžių galės prisijungti iš naujo.
                _obsClient.Dispose();
                SetStatus(
                    IsObsRunning() ? "Cannot connect — check OBS WebSocket Server Settings" : "OBS is not running",
                    IsObsRunning() ? "Nepavyko prisijungti — patikrinkite OBS WebSocket Server Settings" : "OBS nepaleistas",
                    Brushes.Gray);
                _nextReconnectAttempt = DateTime.UtcNow.AddSeconds(3);
            }
            finally
            {
                _connectionAttemptRunning = false;
                UpdateConnectionButton();
            }
        }

        private async Task RefreshStateAsync()
        {
            JsonElement recordStatus = await _obsClient.SendRequestAsync("GetRecordStatus");
            JsonElement replayStatus = await _obsClient.SendRequestAsync("GetReplayBufferStatus");

            _recording = recordStatus.TryGetProperty("outputActive", out JsonElement recording) &&
                         recording.GetBoolean();
            _replayBufferActive = replayStatus.TryGetProperty("outputActive", out JsonElement replay) &&
                                  replay.GetBoolean();

            if (_recording &&
                recordStatus.TryGetProperty("outputTimecode", out JsonElement timecode) &&
                TimeSpan.TryParse(timecode.GetString(), out TimeSpan elapsed))
            {
                _recordingStartedAt = DateTime.UtcNow - elapsed;
            }
            else if (!_recording)
            {
                _recordingStartedAt = null;
            }

            UpdateUi();
        }

        private void ObsClient_StateChanged(object? sender, ObsStateChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (e.RecordActive.HasValue)
                {
                    _recording = e.RecordActive.Value;
                    _recordingStartedAt = _recording ? DateTime.UtcNow : null;
                }

                if (e.ReplayBufferActive.HasValue)
                    _replayBufferActive = e.ReplayBufferActive.Value;

                if (e.Connected == false)
                {
                    SetStatus(
                        "Restoring OBS connection...",
                        "Atkuriamas OBS ryšys...",
                        Brushes.Orange);
                    _nextReconnectAttempt = DateTime.UtcNow;
                }

                UpdateUi();
                UpdateConnectionButton();
            });
        }

        private void UiTimer_Tick(object? sender, EventArgs e)
        {
            bool obsRunning = IsObsRunning();

            if (!obsRunning)
            {
                if (_obsClient.IsConnected || _recording || _replayBufferActive)
                {
                    _obsClient.Dispose();
                    _recording = false;
                    _replayBufferActive = false;
                    _recordingStartedAt = null;
                }

                UpdateUi();
                UpdateConnectionButton();
                return;
            }

            if (_recording)
            {
                txtRecordTime.Visibility = System.Windows.Visibility.Visible;
                txtRecordTime.Foreground = ActiveRed;

                if (_recordingStartedAt is DateTime startedAt)
                    txtRecordTime.Text = (DateTime.UtcNow - startedAt).ToString(@"hh\:mm\:ss");
            }
            else
            {
                txtRecordTime.Visibility = System.Windows.Visibility.Collapsed;
                txtRecordTime.Text = "00:00:00";
            }

            UpdateConnectionButton();

            if (DateTime.UtcNow >= _nextHotkeyCheck)
            {
                _nextHotkeyCheck = DateTime.UtcNow.AddSeconds(2);
                UpdateObsHotkeys();
            }

            if (IsObsRunning() &&
                !_obsClient.IsConnected &&
                !_connectionAttemptRunning &&
                DateTime.UtcNow >= _nextReconnectAttempt)
            {
                _nextReconnectAttempt = DateTime.UtcNow.AddSeconds(3);
                _ = ConnectAsync();
            }
        }

        private void UpdateUi()
        {
            txtRecordAction.Text = _recording
                ? LanguageManager.Text("STOP REC", "STABDYTI ĮRAŠĄ")
                : LanguageManager.Text("START REC", "PRADĖTI ĮRAŠĄ");
            txtRecordHotkey.Text = _recording ? _stopRecordHotkey : _startRecordHotkey;
            txtRecordAction.Foreground = _recording ? ActiveRed : NeutralTextBrush;
            if(_recording)btnRecord.Background=ActiveRedBackground;else btnRecord.SetResourceReference(Border.BackgroundProperty,"ModuleCardSurfaceBrush");
            SetRecordingPulse(_recording);

            txtReplayAction.Text = _replayBufferActive
                ? "STOP\nREPLAY\nBUFFER"
                : LanguageManager.Text("REPLAY BUFFER", "REPLAY BUFFER");
            txtReplayBufferHotkey.Text = _replayBufferActive
                ? _stopReplayHotkey
                : _startReplayHotkey;
            txtSaveReplayHotkey.Text = _saveReplayHotkey;
            btnReplayBuffer.BorderBrush = _replayBufferActive ? ActiveGreen : DefaultBorder;

            if (!_obsClient.IsConnected)
            {
                SetStatus(
                    IsObsRunning()
                        ? "Waiting for OBS connection..."
                        : "OBS is not running",
                    IsObsRunning()
                        ? "Laukiama OBS ryšio..."
                        : "OBS nepaleistas",
                    Brushes.Gray);
            }
            else
            {
                SetStatus(
                    _recording ? "Recording" : "Ready",
                    _recording ? "Įrašoma" : "Pasiruošęs",
                    _recording ? ActiveRed : ActiveGreen);
            }
            ControlStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SetRecordingPulse(bool active)
        {
            if (active)
            {
                if (_recordPulseRunning)
                    return;

                _recordPulseRunning = true;
                SolidColorBrush border = new(ActiveRed.Color);
                btnRecord.BorderBrush = border;
                DoubleAnimation pulse = new(1, 0.12, TimeSpan.FromMilliseconds(900))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                border.BeginAnimation(Brush.OpacityProperty, pulse);
                txtRecordAction.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(1, 0.12, TimeSpan.FromMilliseconds(900))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                    });
                return;
            }

            if (_recordPulseRunning)
                txtRecordAction.BeginAnimation(UIElement.OpacityProperty, null);
            _recordPulseRunning = false;
            txtRecordAction.Opacity = 1;
            btnRecord.BorderBrush = DefaultBorder;
        }

        public async Task<bool> ToggleRecordingAsync()
        {
            if (Interlocked.Exchange(ref _recordCommandRunning, 1) != 0) return false;
            try
            {
                bool targetState = !_recording;
                if (!await ExecuteAsync(targetState ? "StartRecord" : "StopRecord", refreshState: false))
                    return false;
                _recording = targetState;
                _recordingStartedAt = targetState ? DateTime.UtcNow : null;
                UpdateUi();
                return true;
            }
            finally { Interlocked.Exchange(ref _recordCommandRunning, 0); }
        }

        public async Task<bool> ToggleReplayBufferAsync()
        {
            if (Interlocked.Exchange(ref _replayBufferCommandRunning, 1) != 0) return false;
            try
            {
                bool targetState = !_replayBufferActive;
                if (!await ExecuteAsync(targetState ? "StartReplayBuffer" : "StopReplayBuffer", refreshState: false))
                    return false;
                _replayBufferActive = targetState;
                UpdateUi();
                return true;
            }
            finally { Interlocked.Exchange(ref _replayBufferCommandRunning, 0); }
        }

        public async Task<bool> SaveReplayBufferAsync()
        {
            if (!_replayBufferActive)
            {
                SetStatus("Replay Buffer is not active", "Replay Buffer neįjungtas", ActiveRed);
                return false;
            }
            if (!await ExecuteAsync("SaveReplayBuffer")) return false;
            await AnimateReplaySavedAsync();
            return true;
        }

        private async void BtnRecord_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            await ToggleRecordingAsync();
        }

        private async void BtnReplayBuffer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            await ToggleReplayBufferAsync();
        }

        private async void BtnSaveReplay_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;

            await SaveReplayBufferAsync();
        }

        private async void BtnReconnect_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;

            if (_connectionActionRunning)
                return;

            _connectionActionRunning = true;
            btnReconnect.IsHitTestVisible = false;
            txtConnectionAction.Text = "CONNECTING";

            try
            {
                if (!IsObsRunning())
                {
                    if (!await StartObsAsync())
                    {
                        SetStatus(
                            "OBS was not selected. Use START OBS to locate it.",
                            "OBS nepasirinktas. Paspauskite START OBS ir nurodykite jo vietą.",
                            ActiveRed);
                        return;
                    }

                    SetStatus("Starting OBS...", "Paleidžiama OBS...", Brushes.White);

                    for (int attempt = 0; attempt < 15; attempt++)
                    {
                        await Task.Delay(1000);

                        if (!IsObsRunning())
                            continue;

                        await ConnectAsync();
                        if (_obsClient.IsConnected)
                            return;
                    }

                    return;
                }

                _obsClient.Dispose();
                await ConnectAsync();
            }
            catch (Exception ex)
            {
                SetStatus(GetFriendlyError(ex, false), GetFriendlyError(ex, true), ActiveRed);
            }
            finally
            {
                _connectionActionRunning = false;
                btnReconnect.IsHitTestVisible = true;
                UpdateConnectionButton();
            }
        }

        private async Task<bool> ExecuteAsync(string requestType, bool refreshState = true)
        {
            try
            {
                if (!IsObsRunning())
                {
                    if (!await StartObsAsync())
                    {
                        SetStatus("OBS was not selected. Use START OBS to locate it.", "OBS nepasirinktas. Paspauskite START OBS ir nurodykite jo vietą.", ActiveRed);
                        return false;
                    }

                    SetStatus("Starting OBS...", "Paleidžiama OBS...", Brushes.White);
                    for (int attempt = 0; attempt < 15 && !IsObsRunning(); attempt++)
                        await Task.Delay(1000);
                }

                if (!_obsClient.IsConnected)
                    await ConnectAsync();

                if (!_obsClient.IsConnected)
                    return false;

                await _obsClient.SendRequestAsync(requestType);
                if (refreshState)
                    await RefreshStateAsync();
                return true;
            }
            catch (Exception ex)
            {
                SetStatus(
                    GetFriendlyError(ex, false),
                    GetFriendlyError(ex, true),
                    ActiveRed);
                return false;
            }
        }

        private async Task AnimateReplaySavedAsync()
        {
            if (_saveAnimationRunning)
                return;

            _saveAnimationRunning = true;

            try
            {
                SetStatus("Replay saved", "Replay išsaugotas", ActiveGreen);

                for (int flash = 0; flash < 8; flash++)
                {
                    bool highlighted = flash % 2 == 0;
                    saveReplayIcon.Fill = highlighted ? ActiveGreen : NeutralTextBrush;
                    saveReplayIcon.Opacity = highlighted ? 1 : 0.35;
                    btnSaveReplay.BorderBrush = highlighted ? ActiveGreen : DefaultBorder;
                    await Task.Delay(400);
                }
            }
            finally
            {
                saveReplayIcon.Fill = NeutralTextBrush;
                saveReplayIcon.Opacity = 1;
                btnSaveReplay.BorderBrush = DefaultBorder;
                _saveAnimationRunning = false;
            }
        }

        private void SetStatus(string english, string lithuanian, Brush color)
        {
            Dispatcher.Invoke(() =>
            {
                txtStatus.Text = LanguageManager.Text(english, lithuanian);
                if(color is SolidColorBrush neutral && neutral.Color.R==neutral.Color.G && neutral.Color.G==neutral.Color.B)txtStatus.SetResourceReference(TextBlock.ForegroundProperty,"PanelTextBrush");else txtStatus.Foreground = color;
            });
        }

        private void LanguageManager_Changed(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                ApplyLanguage();

                if (_obsClient.IsConnected)
                    UpdateUi();
                else
                    SetStatus(
                        IsObsRunning() ? "Waiting for OBS connection..." : "OBS is not running",
                        IsObsRunning() ? "Laukiama OBS ryšio..." : "OBS nepaleistas",
                        Brushes.Gray);
            });
        }

        private void ApplyLanguage()
        {
            txtObsHeading.Text = LanguageManager.Text("OBS CONTROLS", "OBS VALDYMAS");
            txtSaveReplayAction.Text = LanguageManager.Text(
                "SAVE REPLAY",
                "IŠSAUGOTI REPLAY");
            txtStatusLabel.Text = LanguageManager.Text("Status:", "Būsena:");
            UpdateConnectionButton();
        }

        private void UpdateConnectionButton()
        {
            if (_connectionActionRunning)
            {
                txtConnectionAction.Text = "CONNECTING";
                btnReconnect.BorderBrush = DefaultBorder;
                return;
            }

            if (!IsObsRunning())
            {
                txtConnectionAction.Text =
                    LanguageManager.Text("START OBS", "PALEISTI OBS");
                btnReconnect.BorderBrush = DefaultBorder;
                return;
            }

            txtConnectionAction.Text = _obsClient.IsConnected
                ? "OBS\nONLINE"
                : LanguageManager.Text("RECONNECT", "PRISIJUNGTI");
            btnReconnect.BorderBrush = _obsClient.IsConnected ? ActiveGreen : DefaultBorder;
        }

        internal void ApplyDiagnosticVisualState(bool connected, bool recording, bool replayBufferActive)
        {
            _recording = recording;
            _replayBufferActive = replayBufferActive;
            _recordingStartedAt = recording ? DateTime.UtcNow - TimeSpan.FromSeconds(18) : null;
            UpdateUi();
            txtConnectionAction.Text = connected ? "OBS\nONLINE" : "RECONNECT";
            btnReconnect.BorderBrush = connected ? ActiveGreen : DefaultBorder;
            txtRecordTime.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
            txtRecordTime.Text = recording ? "00:00:18" : "00:00:00";
            SetStatus(recording ? "Recording" : "Ready", recording ? "Įrašoma" : "Pasiruošęs",
                recording ? ActiveRed : ActiveGreen);
        }

        private static bool IsObsRunning()
        {
            Process[] processes = Process.GetProcessesByName("obs64");

            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (Process process in processes)
                    process.Dispose();
            }
        }

        private async Task<bool> StartObsAsync()
        {
            var installation = await Task.Run(ObsInstallationLocator.Resolve);
            if (installation == null)
            {
                if (AppMessage.Show(Window.GetWindow(this), LanguageManager.Text(
                    "OBS Studio was not found in its registered locations. If it is installed in a custom or portable folder, locate obs64.exe now.\n\nChoose the OBS application?",
                    "OBS Studio registruotose vietose nerastas. Jei naudojate kitą arba nešiojamą aplanką, nurodykite obs64.exe.\n\nPasirinkti OBS programą?"), "OBS Studio", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return false;
                var picker = new Microsoft.Win32.OpenFileDialog { Title = "Select OBS Studio", Filter = "OBS Studio|obs64.exe", CheckFileExists = true, AddToRecent = false };
                if (picker.ShowDialog(Window.GetWindow(this)) != true) return false;
                installation = ObsInstallationLocator.FromExecutable(picker.FileName);
                if (installation == null) throw new ObsSetupException("Select the OBS Studio application (obs64.exe).", "Pasirinkite OBS Studio programą (obs64.exe).");
                ObsInstallationLocator.RememberSelection(installation);
            }
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = installation.Executable,
                Arguments = installation.Portable ? "--portable" : "",
                WorkingDirectory = Path.GetDirectoryName(installation.Executable)!,
                UseShellExecute = true
            });
            return process != null;
        }

        private void UpdateObsHotkeys()
        {
            try
            {
                var installation = ObsInstallationLocator.Current;
                if (installation == null)
                {
                    ClearObsHotkeys();
                    return;
                }
                string obsRoot = installation.ConfigDirectory;
                string userSettings = Path.Combine(obsRoot, "user.ini");
                string profileDirectory = ReadIniValue(userSettings, "Basic", "ProfileDir")
                    ?? "Untitled";
                string hotkeyFile = Path.Combine(
                    obsRoot,
                    "basic",
                    "profiles",
                    profileDirectory,
                    "basic.ini");

                if (!File.Exists(hotkeyFile))
                {
                    ClearObsHotkeys();
                    return;
                }

                string fileState =
                    $"{obsRoot}|{profileDirectory}|" +
                    $"{File.GetLastWriteTimeUtc(userSettings).Ticks}|" +
                    $"{File.GetLastWriteTimeUtc(hotkeyFile).Ticks}";

                if (fileState == _hotkeyFileState)
                    return;

                string[] lines = File.ReadAllLines(hotkeyFile);

                _startRecordHotkey = ReadHotkey(lines, "OBSBasic.StartRecording");
                _stopRecordHotkey = ReadHotkey(lines, "OBSBasic.StopRecording");
                _startReplayHotkey = ReadHotkey(lines, "OBSBasic.StartReplayBuffer");
                _stopReplayHotkey = ReadHotkey(lines, "OBSBasic.StopReplayBuffer");
                _saveReplayHotkey = ReadHotkey(
                    lines,
                    "ReplayBuffer",
                    "ReplayBuffer.Save");
                _hotkeyFileState = fileState;

                UpdateUi();
            }
            catch
            {
                // OBS gali trumpam perrašyti failą; kitas patikrinimas bandys dar kartą.
                ClearObsHotkeys();
            }
        }

        private void ClearObsHotkeys()
        {
            bool changed = _startRecordHotkey != "NO HOTKEY" || _stopRecordHotkey != "NO HOTKEY" ||
                _startReplayHotkey != "NO HOTKEY" || _stopReplayHotkey != "NO HOTKEY" || _saveReplayHotkey != "NO HOTKEY";
            _hotkeyFileState = "";
            _startRecordHotkey = _stopRecordHotkey = _startReplayHotkey = _stopReplayHotkey = _saveReplayHotkey = "NO HOTKEY";
            if (changed) UpdateUi();
        }

        private static string? ReadIniValue(
            string filePath,
            string section,
            string key)
        {
            if (!File.Exists(filePath))
                return null;

            bool inSection = false;

            foreach (string rawLine in File.ReadLines(filePath))
            {
                string line = rawLine.Trim();

                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    inSection = string.Equals(
                        line[1..^1],
                        section,
                        StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inSection || !line.StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase))
                    continue;

                return line[(line.IndexOf('=') + 1)..].Trim();
            }

            return null;
        }

        private static string ReadHotkey(
            string[] lines,
            string settingName,
            string? nestedName = null)
        {
            string prefix = $"{settingName}=";
            string? line = lines.FirstOrDefault(item =>
                item.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

            if (line == null)
                return "NO HOTKEY";

            try
            {
                using JsonDocument document = JsonDocument.Parse(line[prefix.Length..]);
                JsonElement root = document.RootElement;
                JsonElement bindings;

                if (nestedName != null)
                {
                    if (!root.TryGetProperty(nestedName, out bindings))
                        return "NO HOTKEY";
                }
                else if (!root.TryGetProperty("bindings", out bindings))
                {
                    return "NO HOTKEY";
                }

                if (bindings.ValueKind != JsonValueKind.Array ||
                    bindings.GetArrayLength() == 0)
                {
                    return "NO HOTKEY";
                }

                return FormatHotkey(bindings[0]);
            }
            catch
            {
                return "NO HOTKEY";
            }
        }

        private static string FormatHotkey(JsonElement binding)
        {
            string key = binding.TryGetProperty("key", out JsonElement keyElement)
                ? keyElement.GetString() ?? ""
                : "";

            if (string.IsNullOrWhiteSpace(key) || key == "OBS_KEY_NONE")
                return "NO HOTKEY";

            string result = "";

            if (IsModifierEnabled(binding, "control"))
                result += "CTRL+";
            if (IsModifierEnabled(binding, "alt"))
                result += "ALT+";
            if (IsModifierEnabled(binding, "shift"))
                result += "SHIFT+";
            if (IsModifierEnabled(binding, "command"))
                result += "WIN+";

            result += key.Replace("OBS_KEY_", "", StringComparison.OrdinalIgnoreCase);
            return result;
        }

        private static bool IsModifierEnabled(JsonElement binding, string modifier)
        {
            return binding.TryGetProperty(modifier, out JsonElement value) &&
                   value.ValueKind == JsonValueKind.True;
        }

        private static string GetFriendlyError(Exception exception, bool lithuanian)
        {
            if (exception is ObsSetupException setup) return lithuanian ? setup.Lithuanian : setup.Message;
            string message = exception.Message;

            if (message.Contains("not active", StringComparison.OrdinalIgnoreCase))
                return lithuanian ? "Replay Buffer neįjungtas" : "Replay Buffer is not active";
            if (message.Contains("already active", StringComparison.OrdinalIgnoreCase))
                return lithuanian ? "Jau paleista" : "Already active";

            return string.IsNullOrWhiteSpace(message)
                ? (lithuanian ? "OBS komandos klaida" : "OBS command error")
                : message;
        }
    }
}
