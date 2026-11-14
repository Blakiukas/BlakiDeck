using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Windows.Media.Control;

namespace Ekrano_valdymas
{
    public enum MusicService
    {
        Spotify,
        Tidal,
        Deezer,
        AppleMusic,
        AmazonMusic,
        MusicBee,
        Local
    }

    public partial class SpotifyWidget : UserControl
    {
        public static event EventHandler<ImageSource?>? AlbumArtChanged;
        public static event EventHandler? PlaybackStateChanged;
        public static ImageSource? CurrentAlbumArt { get; private set; }
        private DispatcherTimer _updateTimer;
        private DispatcherTimer _progressTimer;

        private bool _isPlaying = false;
        private bool _isLiked = false;
        private string _lastTrackName = "";

        private int _currentSeconds = 0;
        private int _totalSeconds = 210;
        private bool _isTimelineSeeking;
        private int _coverAttempts;
        private MusicService _selectedService = MusicService.Spotify;
        public string[] SelectedProcessNames => GetServiceInfo(_selectedService).ProcessNames;
        private bool _isUpdatingMediaInfo;
        private bool _isUpdatingApplicationVolume;
        private GlobalSystemMediaTransportControlsSessionManager?
            _sessionManager;
        private string _sessionManagerFailure="";
        private Task<GlobalSystemMediaTransportControlsSessionMediaProperties>?
            _pendingMediaPropertiesTask;
        private string? _pendingMediaPropertiesSource;
        private static readonly TimeSpan MediaRequestTimeout =
            TimeSpan.FromSeconds(2);
        private int _coverTransitionVersion;

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SwMinimize = 6;

        public SpotifyWidget()
        {
            InitializeComponent();
            LocalPlayback.Instance.Changed += RefreshLocal;
            LanguageManager.Changed += LanguageManager_Changed;
            Unloaded += SpotifyWidget_Unloaded;

_updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _updateTimer.Tick += UpdateTimer_Tick;
            _updateTimer.Start();

            _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _progressTimer.Tick += ProgressTimer_Tick;
            _progressTimer.Start();
        }

        public MusicService SelectedService => _selectedService;
        internal bool LocalAvailable {get;set;}
        internal string AudioFormat => SelectedService==MusicService.Local?LocalPlayback.Instance.Format:"";
        internal string AudioBitrate => SelectedService==MusicService.Local?LocalPlayback.Instance.Bitrate:"";
        internal string AudioSampleRate => SelectedService==MusicService.Local?LocalPlayback.Instance.SampleRate:"";
        internal string AudioDetails => string.Join("  ·  ",new[]{AudioFormat,AudioBitrate,AudioSampleRate}.Where(s=>s.Length>0));
        private void RefreshLocal()
        {
            if(SelectedService!=MusicService.Local)return;
            var local=LocalPlayback.Instance;
            txtTitle.Text=local.Current?.Title??"Local player";
            txtArtist.Text=local.Error.Length>0?local.Error:local.Current?.Artist??(local.Tracks.Count>0?"Select a track":"Add audio files to Playlist");
            _currentSeconds=(int)local.Position;_totalSeconds=(int)local.Duration;
            SetIsPlaying(local.Playing);SetPlayPauseIcon(local.Playing);
            ShuffleActive=local.Shuffle;RepeatActive=local.Repeat;
            if(CurrentAlbumArt!=local.Artwork){_coverTransitionVersion++;CurrentAlbumArt=local.Artwork;imgCover.Source=local.Artwork;AlbumArtChanged?.Invoke(this,local.Artwork);}
            UpdateProgressUI();
        }
        public bool IsPlaying => _isPlaying;
        internal string ConsoleTitle => txtTitle.Text;
        internal string ConsoleArtist => txtArtist.Text;
        internal string ConsoleTime => txtTimeCurrent.Text + "  /  " + txtTimeTotal.Text;
        internal double ConsolePosition => _currentSeconds;
        internal double ConsoleDuration => _totalSeconds;
        internal async Task ConsoleSeekAsync(double seconds)
        {
            if(SelectedService==MusicService.Local){LocalPlayback.Instance.Seek(seconds);return;}
            var session = await GetSelectedSessionAsync(startServiceIfMissing: false);
            if (session == null || _totalSeconds <= 0) return;
            double position = Math.Clamp(seconds, 0, _totalSeconds);
            if (await session.TryChangePlaybackPositionAsync(TimeSpan.FromSeconds(position).Ticks))
            { _currentSeconds = (int)position; UpdateProgressUI(); }
        }
        internal bool ConsoleActive { get; set; }
        private readonly HashSet<object> cardConsumers=new();
        internal void SetCardActive(object card,bool active){if(active)cardConsumers.Add(card);else cardConsumers.Remove(card);}
        internal bool? ShuffleActive {get;private set;}
        internal bool? RepeatActive {get;private set;}
        private MusicService? modeCapabilityService;
        private bool shuffleSupported,repeatSupported;
        internal event Action? PlaybackModesChanged;
        private GlobalSystemMediaTransportControlsSession? modeSession;
        private MusicService modeSessionService;
        private void ObserveModeSession(GlobalSystemMediaTransportControlsSession? session)
        {
            if(ReferenceEquals(modeSession,session)&&modeSessionService==SelectedService)return;
            if(modeSession!=null)modeSession.PlaybackInfoChanged-=ModeSessionChanged;
            modeSession=session;modeSessionService=SelectedService;
            if(session!=null)session.PlaybackInfoChanged+=ModeSessionChanged;
        }
        private void ModeSessionChanged(GlobalSystemMediaTransportControlsSession sender,PlaybackInfoChangedEventArgs args)
        {
            // Mode acknowledgements must not wait for the metadata polling timer
            // or an album/track request. Only the selected session may update UI.
            _=Dispatcher.InvokeAsync(()=>
            {
                if(!ReferenceEquals(sender,modeSession)||SelectedService!=modeSessionService)return;
                try{UpdateSessionModes(modeSessionService,sender.GetPlaybackInfo());}
                catch(Exception error){InteractionDiagnostics.RecordFailure("Playback modes",error);}
            },DispatcherPriority.Normal);
        }
        internal bool CanShuffle=>SelectedService==MusicService.Local||modeCapabilityService==SelectedService&&shuffleSupported;
        internal bool CanRepeat=>SelectedService==MusicService.Local||modeCapabilityService==SelectedService&&repeatSupported;
        private void UpdateSessionModes(MusicService service,GlobalSystemMediaTransportControlsSessionPlaybackInfo playback)
        {
            if(SelectedService!=service)return;
            var before=(ShuffleActive,RepeatActive,modeCapabilityService,shuffleSupported,repeatSupported);
            ShuffleActive=playback.IsShuffleActive;
            RepeatActive=playback.AutoRepeatMode.HasValue?playback.AutoRepeatMode!=Windows.Media.MediaPlaybackAutoRepeatMode.None:null;
            modeCapabilityService=service;
            shuffleSupported=playback.Controls.IsShuffleEnabled;
            repeatSupported=playback.Controls.IsRepeatEnabled;
            if(before!=(ShuffleActive,RepeatActive,modeCapabilityService,shuffleSupported,repeatSupported))
            {
                InteractionDiagnostics.Log($"playback-modes service={service} shuffle={ShuffleActive} repeat={RepeatActive}");
                PlaybackModesChanged?.Invoke();
            }
        }
        private bool modeCommandPending;
        internal async Task ConsoleModeAsync(bool shuffle)
        {
            if(SelectedService==MusicService.Local){LocalPlayback.Instance.SetMode(shuffle);return;}
            if(modeCommandPending||!(shuffle?CanShuffle:CanRepeat))return;
            var requestedService=SelectedService;
            modeCommandPending=true;
            long started=Environment.TickCount64;
            InteractionDiagnostics.Log($"playback-mode-command start service={requestedService} shuffleCommand={shuffle}");
            try
            {
                var session=await GetSelectedSessionAsync(startServiceIfMissing:false);
                if(SelectedService!=requestedService)return;
                if(session==null)throw new InvalidOperationException("Pasirinktame muzikos šaltinyje nėra aktyvios dainos.");
                var info=session.GetPlaybackInfo();bool accepted=false;
                UpdateSessionModes(requestedService,info);
                if(!(shuffle?info.Controls.IsShuffleEnabled:info.Controls.IsRepeatEnabled))
                    throw new InvalidOperationException($"{requestedService} šiuo metu neleidžia {(shuffle?"maišymo":"kartojimo")}. Ši funkcija turi būti prieinama pačiame muzikos šaltinyje.");
                if(shuffle)
                {
                    bool next=info.IsShuffleActive!=true;
                    accepted=await session.TryChangeShuffleActiveAsync(next);
                }
                else
                {
                    bool next=!info.AutoRepeatMode.HasValue||info.AutoRepeatMode==Windows.Media.MediaPlaybackAutoRepeatMode.None;
                    accepted=await session.TryChangeAutoRepeatModeAsync(next?Windows.Media.MediaPlaybackAutoRepeatMode.List:Windows.Media.MediaPlaybackAutoRepeatMode.None);
                }
                if(SelectedService!=requestedService)return;
                // Acceptance does not substitute for the source's actual mode.
                UpdateSessionModes(requestedService,session.GetPlaybackInfo());
                if(!accepted)throw new InvalidOperationException($"{requestedService} šiuo metu neleidžia {(shuffle?"maišymo":"kartojimo")}. Ši funkcija turi būti prieinama pačiame muzikos šaltinyje.");
            }
            finally
            {
                modeCommandPending=false;
                InteractionDiagnostics.Log($"playback-mode-command end service={requestedService} elapsedMs={Environment.TickCount64-started}");
            }
        }
        internal double ConsoleVolume
        {
            get => SelectedService==MusicService.Local?LocalPlayback.Instance.Volume:applicationVolumeSlider.Value;
            set
            {
                if(SelectedService==MusicService.Local){LocalPlayback.Instance.Volume=value;return;}
                _isUpdatingApplicationVolume = true;
                try { applicationVolumeSlider.Value = Math.Clamp(value, 0, 100); }
                finally { _isUpdatingApplicationVolume = false; }
                ApplicationVolumeController.SetVolume(SelectedProcessNames, (float)(applicationVolumeSlider.Value / 100));
            }
        }
        internal async Task<bool> ConsoleCommandAsync(string command)
        {
            try {
            if(SelectedService==MusicService.Local){if(!LocalAvailable&&command!="pause")throw new InvalidOperationException("Local player requires Playlist on the same tab.");return await LocalPlayback.Instance.CommandAsync(command);}
            var requestedService=SelectedService;
            return await PlaybackCoordinator.RunAsync(async()=>{
            var session = await GetSelectedSessionAsync(startServiceIfMissing: false);
            if(session==null&&command!="pause"){
                await PlaybackCoordinator.PauseOthersAsync(null);
                if(SelectedService!=requestedService)return false;
                session=await GetSelectedSessionAsync(startServiceIfMissing:true);
            }
            if(SelectedService!=requestedService)return false;
            if (session == null)
            {
                if(command=="pause")return false;
                throw new InvalidOperationException(_sessionManagerFailure.Length>0?_sessionManagerFailure:
                    MediaSupport.MissingPlayerMessage(GetServiceInfo(requestedService).DisplayName,IsSelectedServiceRunning()));
            }
            bool accepted;
            bool pausing=command=="pause"||(command=="play"&&session.GetPlaybackInfo().PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
            if(!pausing)await PlaybackCoordinator.PauseOthersAsync(session);
            if (command == "pause") accepted=await session.TryPauseAsync();
            else if (command == "previous") accepted=await session.TrySkipPreviousAsync();
            else if (command == "next") accepted=await session.TrySkipNextAsync();
            else if (session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                accepted=await session.TryPauseAsync();
            else accepted=await session.TryPlayAsync();
            await UpdateSelectedServiceInfoAsync();
            if(!accepted&&command!="pause")throw new InvalidOperationException(LanguageManager.Text(
                "The selected player did not accept this media command. Start playback in the player and try again; available controls depend on that player.",
                "Pasirinktas grotuvas nepriėmė šios komandos. Paleiskite muziką pačiame grotuve ir bandykite dar kartą; galimas valdymas priklauso nuo grotuvo."));
            return accepted;
            });
            }catch(Exception error){InteractionDiagnostics.RecordFailure("Playback arbitration",error);if(DiagnosticState.Root==null)AppMessage.Show(error.Message,"Playback",MessageBoxButton.OK,MessageBoxImage.Warning);return false;}
        }


        private void SetIsPlaying(bool value)
        {
            if (_isPlaying == value) return;
            _isPlaying = value;
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetMusicService(MusicService service)
        {
            if(service!=SelectedService)ObserveModeSession(null);
            _coverTransitionVersion++;
            if(service==MusicService.Local){_selectedService=service;ResetPendingMediaProperties();UpdateServiceHeading();RefreshLocal();return;}
            if (_selectedService == service)
            {
                UpdateServiceHeading();
                SetIdleServiceInfo(isRunning: IsSelectedServiceRunning());
                return;
            }

            CurrentAlbumArt=null;AlbumArtChanged?.Invoke(this,null);
            _selectedService = service;
            ResetPendingMediaProperties();
            _lastTrackName = "";
            _currentSeconds = 0;
            _totalSeconds = 0;
            imgCover.Source = null;
            txtNoCover.Visibility = Visibility.Visible;
            UpdateServiceHeading();
            SetIdleServiceInfo(isRunning: IsSelectedServiceRunning());
            UpdateProgressUI();
            _ = UpdateSelectedServiceInfoAsync();
        }

        private async void LanguageManager_Changed(object? sender, EventArgs e)
        {
            await Dispatcher.InvokeAsync(UpdateServiceHeading);
            await UpdateSelectedServiceInfoAsync();
        }

        private void ProgressTimer_Tick(object? sender, EventArgs e)
        {
            if(SelectedService==MusicService.Local){RefreshLocal();return;}
            if (!_isTimelineSeeking && _isPlaying && _currentSeconds < _totalSeconds)
            {
                _currentSeconds++;
                UpdateProgressUI();
            }
        }

        private void UpdateTimelinePreview(Point position)
        {
            if (_totalSeconds <= 0 || timelineTouchArea.ActualWidth <= 0) return;
            double ratio = Math.Clamp(position.X / timelineTouchArea.ActualWidth, 0, 1);
            _currentSeconds = (int)Math.Round(_totalSeconds * ratio);
            UpdateProgressUI();
        }

        private async Task CommitTimelineSeekAsync()
        {
            if(SelectedService==MusicService.Local){LocalPlayback.Instance.Seek(_currentSeconds);_isTimelineSeeking=false;return;}
            try
            {
                GlobalSystemMediaTransportControlsSession? session =
                    await GetSelectedSessionAsync(startServiceIfMissing: false);
                if (session != null)
                    await session.TryChangePlaybackPositionAsync(
                        TimeSpan.FromSeconds(_currentSeconds).Ticks);
            }
            finally
            {
                _isTimelineSeeking = false;
                await UpdateSelectedServiceInfoAsync();
            }
        }

        private void Timeline_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isTimelineSeeking = true;
            timelineTouchArea.CaptureMouse();
            UpdateTimelinePreview(e.GetPosition(timelineTouchArea));
            e.Handled = true;
        }

        private void Timeline_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isTimelineSeeking || e.LeftButton != MouseButtonState.Pressed) return;
            UpdateTimelinePreview(e.GetPosition(timelineTouchArea));
            e.Handled = true;
        }

        private async void Timeline_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isTimelineSeeking) return;
            UpdateTimelinePreview(e.GetPosition(timelineTouchArea));
            timelineTouchArea.ReleaseMouseCapture();
            e.Handled = true;
            await CommitTimelineSeekAsync();
        }

        private void Timeline_PreviewTouchDown(object sender, TouchEventArgs e)
        {
            _isTimelineSeeking = true;
            timelineTouchArea.CaptureTouch(e.TouchDevice);
            UpdateTimelinePreview(e.GetTouchPoint(timelineTouchArea).Position);
            e.Handled = true;
        }

        private void Timeline_PreviewTouchMove(object sender, TouchEventArgs e)
        {
            if (!_isTimelineSeeking) return;
            UpdateTimelinePreview(e.GetTouchPoint(timelineTouchArea).Position);
            e.Handled = true;
        }

        private async void Timeline_PreviewTouchUp(object sender, TouchEventArgs e)
        {
            if (!_isTimelineSeeking) return;
            UpdateTimelinePreview(e.GetTouchPoint(timelineTouchArea).Position);
            timelineTouchArea.ReleaseTouchCapture(e.TouchDevice);
            e.Handled = true;
            await CommitTimelineSeekAsync();
        }

        private void UpdateProgressUI()
        {
            TimeSpan current = TimeSpan.FromSeconds(_currentSeconds);
            TimeSpan total = TimeSpan.FromSeconds(_totalSeconds);

            txtTimeCurrent.Text = $"{current.Minutes}:{current.Seconds:D2}";
            txtTimeTotal.Text = $"{total.Minutes}:{total.Seconds:D2}";

            if (_totalSeconds > 0)
            {
                progressSong.Value = ((double)_currentSeconds / _totalSeconds) * 100;
            }
        }

        private long lastPassiveMediaUpdate;
        private async void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            long now=Environment.TickCount64;
            if(ResourcePolicy.IsSaving&&now-lastPassiveMediaUpdate<ResourcePolicy.PassiveInterval.TotalMilliseconds)return;
            lastPassiveMediaUpdate=now;
            try{await PlaybackCoordinator.ObserveExternalPlaybackAsync();}catch(Exception error){InteractionDiagnostics.RecordFailure("External playback",error);}
            await UpdateSelectedServiceInfoAsync();
        }

        private async Task UpdateSelectedServiceInfoAsync()
        {
            if(SelectedService==MusicService.Local){RefreshLocal();return;}
            var requestedService=SelectedService;
            if (_isUpdatingMediaInfo)
                return;

            _isUpdatingMediaInfo = true;

            try
            {
                GlobalSystemMediaTransportControlsSession? session =
                    await GetSelectedSessionAsync(startServiceIfMissing: false);

                if(SelectedService!=requestedService)return;

                if (session == null)
                {
                    SetIdleServiceInfo(isRunning: IsSelectedServiceRunning());
                    return;
                }

                GlobalSystemMediaTransportControlsSessionPlaybackInfo playback =
                    session.GetPlaybackInfo();
                UpdateSessionModes(requestedService,playback);
                GlobalSystemMediaTransportControlsSessionTimelineProperties timeline =
                    session.GetTimelineProperties();

                // PlaybackStatus ir laiko juosta yra svarbesni už lėčiau
                // gaunamus dainos metaduomenis, todėl juos atnaujiname pirmiau.
                SetIsPlaying(playback.PlaybackStatus ==
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
                SetPlayPauseIcon(_isPlaying);

                int reportedSeconds = Math.Max(
                    0,
                    (int)Math.Floor(timeline.Position.TotalSeconds));
                if (_isPlaying)
                {
                    double ageSeconds =
                        (DateTimeOffset.UtcNow - timeline.LastUpdatedTime)
                        .TotalSeconds;
                    if (ageSeconds > 0 && ageSeconds < 10)
                        reportedSeconds += (int)Math.Floor(ageSeconds);
                }

                _totalSeconds = Math.Max(
                    0,
                    (int)Math.Floor(timeline.EndTime.TotalSeconds));

                // Grojant UI juda tolygiai kas sekundę. GSMTC rodmenį
                // pritaikome tik aptikę tikrą, reikšmingą persukimą.
                int difference = reportedSeconds - _currentSeconds;
                if (!_isTimelineSeeking && (!_isPlaying || Math.Abs(difference) >= 5))
                    _currentSeconds = reportedSeconds;

                UpdateProgressUI();
                UpdateApplicationVolume();

                GlobalSystemMediaTransportControlsSessionMediaProperties?
                    media = await TryGetMediaPropertiesWithTimeoutAsync(session);

                if (media == null || SelectedService!=requestedService)
                    return;

                string title = string.IsNullOrWhiteSpace(media.Title)
                    ? LanguageManager.Text("Paused", "Pristabdyta")
                    : media.Title.Trim();
                string artist = string.IsNullOrWhiteSpace(media.Artist)
                    ? GetServiceInfo(_selectedService).DisplayName
                    : media.Artist.Trim();

                txtTitle.Text = title;
                txtArtist.Text = artist;

                string trackKey = $"{session.SourceAppUserModelId}\n{artist}\n{title}\n{media.AlbumTitle}";
                if (!string.Equals(
                        _lastTrackName,
                        trackKey,
                        StringComparison.Ordinal))
                {
                    _lastTrackName = trackKey;
                    _coverAttempts = 0;
                    BeginCoverTransition(null);
                }
                if (_coverAttempts < 3 && (_coverAttempts == 0 || CurrentAlbumArt == null))
                {
                    _coverAttempts++;
                    await FetchAlbumCoverAsync(media.Thumbnail);
                }
            }
            catch
            {
                if(SelectedService==requestedService)SetIdleServiceInfo(isRunning: IsSelectedServiceRunning());
            }
            finally
            {
                _isUpdatingMediaInfo = false;
            }
        }

        private async void UpdateApplicationVolume()
        {
            if(SelectedService==MusicService.Local)return;
            if (_isUpdatingApplicationVolume) return;
            _isUpdatingApplicationVolume = true;
            try
            {
                string[] processNames = GetServiceInfo(_selectedService).ProcessNames;
                float? volume = await Task.Run(() =>
                    ApplicationVolumeController.GetVolume(processNames));
                if (volume == null || (!IsLoaded && !ConsoleActive && cardConsumers.Count==0)) return;
                applicationVolumeSlider.Value = Math.Round(volume.Value * 100);
                applicationVolumeText.Text = $"{applicationVolumeSlider.Value:0}%";
            }
            finally
            {
                _isUpdatingApplicationVolume = false;
            }
        }

        private void ApplicationVolumeSlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (applicationVolumeText == null) return;
            applicationVolumeText.Text = $"{e.NewValue:0}%";
            if (_isUpdatingApplicationVolume || !IsLoaded) return;
            if(SelectedService==MusicService.Local){LocalPlayback.Instance.Volume=e.NewValue;return;}
            ApplicationVolumeController.SetVolume(
                GetServiceInfo(_selectedService).ProcessNames,
                (float)(e.NewValue / 100));
        }

        private async Task<GlobalSystemMediaTransportControlsSessionMediaProperties?>
            TryGetMediaPropertiesWithTimeoutAsync(
                GlobalSystemMediaTransportControlsSession session)
        {
            string source = session.SourceAppUserModelId;

            if (!string.Equals(
                    _pendingMediaPropertiesSource,
                    source,
                    StringComparison.OrdinalIgnoreCase))
            {
                ResetPendingMediaProperties();
                _pendingMediaPropertiesSource = source;
            }

            _pendingMediaPropertiesTask ??=
                session.TryGetMediaPropertiesAsync().AsTask();

            Task completedTask = await Task.WhenAny(
                _pendingMediaPropertiesTask,
                Task.Delay(MediaRequestTimeout));

            if (completedTask != _pendingMediaPropertiesTask)
                return null;

            try
            {
                return await _pendingMediaPropertiesTask;
            }
            finally
            {
                _pendingMediaPropertiesTask = null;
            }
        }

        private void ResetPendingMediaProperties()
        {
            if (_pendingMediaPropertiesTask?.IsFaulted == true)
                _ = _pendingMediaPropertiesTask.Exception;

            _pendingMediaPropertiesTask = null;
            _pendingMediaPropertiesSource = null;
        }

        private void SetPlayPauseIcon(bool playing)
        {
            iconPlay.Visibility =
                playing ? Visibility.Collapsed : Visibility.Visible;
            iconPause.Visibility =
                playing ? Visibility.Visible : Visibility.Collapsed;
        }

        private async Task FetchAlbumCoverAsync(Windows.Storage.Streams.IRandomAccessStreamReference? thumbnail)
        {
            var source = SelectedService;
            string track = _lastTrackName;
            int transition = _coverTransitionVersion;
            ImageSource? cover = null;
            try
            {
                cover = await AlbumArtwork.ReadSessionThumbnailAsync(thumbnail);
            }
            catch { /* A missing or unavailable provider image uses the existing no-cover state. */ }
            if (SelectedService == source && _lastTrackName == track && _coverTransitionVersion == transition)
                BeginCoverTransition(cover);
        }
        private void BeginCoverTransition(ImageSource? newCover)
        {
            int version = ++_coverTransitionVersion;
            DoubleAnimation fadeOut = new(imgCover.Opacity, 0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (_, _) =>
            {
                if (version != _coverTransitionVersion) return;
                imgCover.Source = newCover;
                CurrentAlbumArt = newCover;
                AlbumArtChanged?.Invoke(this, newCover);
                txtNoCover.Visibility = newCover == null ? Visibility.Visible : Visibility.Collapsed;
                imgCover.Opacity = 0;
                DoubleAnimation fadeIn = new(0, 1, TimeSpan.FromMilliseconds(340))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                imgCover.BeginAnimation(OpacityProperty, fadeIn);
            };
            imgCover.BeginAnimation(OpacityProperty, fadeOut);
        }

        private void BtnHeart_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isLiked = !_isLiked;

            btnHeart.Text = _isLiked ? "♥" : "♡";
            btnHeart.Foreground = _isLiked
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1DB954"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#666666"));

            e.Handled = true;
        }

        private async void BtnPlayPause_PreviewMouseDown(object sender, MouseButtonEventArgs e) { e.Handled=true;await ConsoleCommandAsync("play"); }
        private async void BtnPrev_PreviewMouseDown(object sender, MouseButtonEventArgs e) { e.Handled=true;await ConsoleCommandAsync("previous"); }
        private async void BtnNext_PreviewMouseDown(object sender, MouseButtonEventArgs e) { e.Handled=true;await ConsoleCommandAsync("next"); }
        private async Task<GlobalSystemMediaTransportControlsSession?>
            GetSelectedSessionAsync(bool startServiceIfMissing)
        {
            GlobalSystemMediaTransportControlsSessionManager? manager =
                await GetSessionManagerAsync();

            if (manager == null)
                return null;

            GlobalSystemMediaTransportControlsSession? session =
                FindSelectedServiceSession(manager);

            ObserveModeSession(session);

            if (session != null || !startServiceIfMissing)
                return session;

            if(!await StartSelectedServiceMinimizedAsync())return null;

            for (int attempt = 0; attempt < 20; attempt++)
            {
                await Task.Delay(500);
                session = FindSelectedServiceSession(manager);

                if (session != null)
                {
                    ObserveModeSession(session);
                    MinimizeSelectedServiceWindow();
                    return session;
                }
            }

            return null;
        }

        private async Task<GlobalSystemMediaTransportControlsSessionManager?>
            GetSessionManagerAsync()
        {
            if (_sessionManager != null)
                return _sessionManager;

            using CancellationTokenSource timeout =
                new(MediaRequestTimeout);

            try
            {
                _sessionManager =
                    await GlobalSystemMediaTransportControlsSessionManager
                        .RequestAsync()
                        .AsTask(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                _sessionManagerFailure=LanguageManager.Text(
                    "Windows media controls did not respond. Open the selected player, start a track and try again.",
                    "Windows medijos valdymas neatsakė. Atidarykite pasirinktą grotuvą, paleiskite kūrinį ir bandykite dar kartą.");
                return null;
            }
            catch(Exception error)
            {
                if(_sessionManagerFailure.Length==0)InteractionDiagnostics.RecordFailure("Windows media controls",error);
                _sessionManagerFailure=LanguageManager.Text(
                    "Windows media controls are unavailable. Check Windows media features and the selected player. On Windows N, install Media Feature Pack in Optional features and restart Windows.",
                    "Windows medijos valdymas neprieinamas. Patikrinkite Windows medijos funkcijas ir pasirinktą grotuvą. Windows N sistemoje įdiekite Media Feature Pack per Pasirinktines funkcijas ir paleiskite Windows iš naujo.");
                return null;
            }

            _sessionManagerFailure="";
            _sessionManager.SessionsChanged += SessionManager_SessionsChanged;
            return _sessionManager;
        }

        private void SessionManager_SessionsChanged(
            GlobalSystemMediaTransportControlsSessionManager sender,
            SessionsChangedEventArgs args)
        {
            _ = Dispatcher.InvokeAsync(
                async () => await UpdateSelectedServiceInfoAsync());
        }

        private void SpotifyWidget_Unloaded(
            object sender,
            RoutedEventArgs e)
        {
            ObserveModeSession(null);
            LanguageManager.Changed -= LanguageManager_Changed;

            if (_sessionManager != null)
                _sessionManager.SessionsChanged -= SessionManager_SessionsChanged;
        }

        private GlobalSystemMediaTransportControlsSession?
            FindSelectedServiceSession(
            GlobalSystemMediaTransportControlsSessionManager manager)
        {
            MusicServiceInfo info = GetServiceInfo(_selectedService);
            GlobalSystemMediaTransportControlsSession[] sessions =
                manager.GetSessions().ToArray();

            return sessions.FirstOrDefault(session =>
                info.SessionTokens.Any(token =>
                    session.SourceAppUserModelId.Contains(
                        token,
                        StringComparison.OrdinalIgnoreCase)));
        }

        private async Task<bool> StartSelectedServiceMinimizedAsync()
        {
            MusicServiceInfo info = GetServiceInfo(_selectedService);

            if (!IsSelectedServiceRunning())
            {
                bool started =
                    TryStartKnownExecutable(info) ||
                    TryStartRegisteredProtocol(info);

                if (!started)started=await TryStartWindowsAppAsync(info);
                if(!started)return false;
            }

            await Task.Delay(700);
            MinimizeSelectedServiceWindow();
            return true;
        }

        private bool IsSelectedServiceRunning()
        {
            MusicServiceInfo info = GetServiceInfo(_selectedService);

            foreach (string processName in info.ProcessNames)
            {
                Process[] processes = Process.GetProcessesByName(processName);
                bool found = processes.Length > 0;

                foreach (Process process in processes)
                {
                    process.Dispose();
                }

                if (found)
                    return true;
            }

            return false;
        }

        private void MinimizeSelectedServiceWindow()
        {
            MusicServiceInfo info = GetServiceInfo(_selectedService);

            foreach (string processName in info.ProcessNames)
            {
                foreach (Process process in Process.GetProcessesByName(processName))
                {
                    try
                    {
                        if (process.MainWindowHandle != IntPtr.Zero)
                            ShowWindow(process.MainWindowHandle, SwMinimize);
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
        }

        private static bool TryStartKnownExecutable(MusicServiceInfo info)
        {
            foreach (string candidate in info.ExecutableCandidates)
            {
                string executable = Environment.ExpandEnvironmentVariables(candidate);
                if (!File.Exists(executable))
                    continue;

                Process.Start(new ProcessStartInfo
                {
                    FileName = executable,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Minimized
                });
                return true;
            }

            return false;
        }

        private static bool TryStartRegisteredProtocol(MusicServiceInfo info)
        {
            foreach (string protocol in info.Protocols)
            {
                string scheme = protocol.TrimEnd(':');
                using RegistryKey? protocolKey =
                    Registry.ClassesRoot.OpenSubKey(scheme);

                if (protocolKey == null)
                    continue;

                Process.Start(new ProcessStartInfo
                {
                    FileName = protocol,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Minimized
                });
                return true;
            }

            return false;
        }

        private static async Task<bool> TryStartWindowsAppAsync(
            MusicServiceInfo info)
        {
            string escapedName = info.StartAppName.Replace("'", "''");
            string command =
                "$app = Get-StartApps | Where-Object { $_.Name -like '*" +
                escapedName +
                "*' } | Select-Object -First 1; " +
                "if ($app) { Start-Process ('shell:AppsFolder\\' + $app.AppID); exit 0 }; exit 1";

            ProcessStartInfo startInfo = new()
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-WindowStyle");
            startInfo.ArgumentList.Add("Hidden");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(command);

            using Process process = Process.Start(startInfo)!;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }

        private void UpdateServiceHeading()
        {
            txtServiceHeading.Text =
                GetServiceInfo(_selectedService).DisplayName.ToUpperInvariant();
        }

        private void SetIdleServiceInfo(bool isRunning)
        {
            ++_coverTransitionVersion;
            if (CurrentAlbumArt != null) { CurrentAlbumArt = null; AlbumArtChanged?.Invoke(this, null); }
            shuffleSupported=repeatSupported=false;modeCapabilityService=null;
            MusicServiceInfo info = GetServiceInfo(_selectedService);
            if (!isRunning)
                ResetPendingMediaProperties();

            SetIsPlaying(false);
            _currentSeconds = 0;
            _totalSeconds = 0;
            _lastTrackName = "";
            txtTitle.Text = isRunning
                ? LanguageManager.Text("Ready", "Paruošta")
                : LanguageManager.Text(
                    $"{info.DisplayName} is not running",
                    $"{info.DisplayName} išjungta");
            txtArtist.Text = isRunning
                ? LanguageManager.Text("Start a track in the player to connect", "Paleiskite kūrinį grotuve, kad prisijungtų")
                : LanguageManager.Text(
                    "Press Play to start",
                    "Paspauskite Play paleidimui");
            imgCover.Source = null;
            txtNoCover.Visibility = Visibility.Visible;
            SetPlayPauseIcon(false);
            UpdateProgressUI();
        }

        private static MusicServiceInfo GetServiceInfo(MusicService service)
        {
            return service switch
            {
                MusicService.Local => new("Local player",Array.Empty<string>(),Array.Empty<string>(),Array.Empty<string>(),Array.Empty<string>(),""),
                MusicService.Tidal => new(
                    "TIDAL",
                    new[] { "TIDAL" },
                    new[] { "TIDAL" },
                    new[] { "tidal:" },
                    new[]
                    {
                        @"%LOCALAPPDATA%\TIDAL\TIDAL.exe",
                        @"%APPDATA%\TIDAL\TIDAL.exe"
                    },
                    "TIDAL"),
                MusicService.Deezer => new(
                    "Deezer",
                    new[] { "Deezer" },
                    new[] { "Deezer" },
                    new[] { "deezer:" },
                    new[]
                    {
                        @"%LOCALAPPDATA%\Programs\deezer-desktop\Deezer.exe",
                        @"%APPDATA%\Deezer\Deezer.exe"
                    },
                    "Deezer"),
                MusicService.AppleMusic => new(
                    "Apple Music",
                    new[] { "AppleMusic", "Apple Music" },
                    new[] { "AppleMusic" },
                    new[] { "music:" },
                    Array.Empty<string>(),
                    "Apple Music"),
                MusicService.AmazonMusic => new(
                    "Amazon Music",
                    new[] { "AmazonMusic", "Amazon Music" },
                    new[] { "Amazon Music", "AmazonMusic" },
                    new[] { "amazonmusic:" },
                    new[]
                    {
                        @"%LOCALAPPDATA%\Amazon Music\Amazon Music.exe",
                        @"%APPDATA%\Amazon Music\Amazon Music.exe"
                    },
                    "Amazon Music"),
                MusicService.MusicBee => new(
                    "MusicBee",
                    new[] { "MusicBee" },
                    new[] { "MusicBee" },
                    Array.Empty<string>(),
                    new[]
                    {
                        @"%PROGRAMFILES(X86)%\MusicBee\MusicBee.exe",
                        @"%PROGRAMFILES%\MusicBee\MusicBee.exe",
                        @"%APPDATA%\MusicBee\MusicBee.exe"
                    },
                    "MusicBee"),
                _ => new(
                    "Spotify",
                    new[] { "Spotify" },
                    new[] { "Spotify" },
                    new[] { "spotify:" },
                    new[]
                    {
                        @"%APPDATA%\Spotify\Spotify.exe",
                        @"%LOCALAPPDATA%\Microsoft\WindowsApps\Spotify.exe"
                    },
                    "Spotify")
            };
        }

        private sealed record MusicServiceInfo(
            string DisplayName,
            string[] SessionTokens,
            string[] ProcessNames,
            string[] Protocols,
            string[] ExecutableCandidates,
            string StartAppName);

    }

    internal static class AudioNative
    {
        public static void SetMasterVolume(float level)
        {
            var device = GetMasterDevice();
            if (device != null)
            {
                Guid dummy = Guid.Empty;
                device.SetMasterVolumeLevelScalar(level, ref dummy);
            }
        }

        public static float GetMasterVolume()
        {
            var device = GetMasterDevice();
            if (device != null)
            {
                device.GetMasterVolumeLevelScalar(out float level);
                return level;
            }
            return 0.7f;
        }

        private static IAudioEndpointVolume? GetMasterDevice()
        {
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                enumerator.GetDefaultAudioEndpoint(0, 0, out IMMDevice device);
                Guid IID_IAudioEndpointVolume = typeof(IAudioEndpointVolume).GUID;
                const int clsctxAll = 23;
                device.Activate(ref IID_IAudioEndpointVolume, clsctxAll, IntPtr.Zero, out object endpoint);
                return (IAudioEndpointVolume)endpoint;
            }
            catch
            {
                return null;
            }
        }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumerator { }

        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int NotUsed();
            [PreserveSig]
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
        }

        [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig]
            int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        }

        [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            int NotUsed1();
            int NotUsed2();
            int NotUsed3();
            int NotUsed4();
            [PreserveSig]
            int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
            int NotUsed5();
            [PreserveSig]
            int GetMasterVolumeLevelScalar(out float pfLevel);
        }
    }
}



