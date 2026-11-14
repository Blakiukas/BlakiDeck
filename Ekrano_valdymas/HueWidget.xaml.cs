using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace Ekrano_valdymas
{
    public partial class HueWidget : UserControl
    {
        private void Header_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            Window? owner = Window.GetWindow(this);
            if (owner?.ContextMenu is not ContextMenu menu)
                return;

            menu.PlacementTarget = sender as UIElement ?? this;
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
            e.Handled = true;
        }

        private void Header_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.StylusDevice != null || e.LeftButton != MouseButtonState.Pressed) return;
            // Let the shared module-header handler move the complete application.
        }
        private readonly HueSettingsStore settingsStore = new();
        private string? lastHueCommandError;
        private bool discoveryRunning;
        private bool pairingRunning;
        private HueBridgeInfo? currentBridge;
        private string? applicationKey;
        private IReadOnlyList<HueRoom> currentRooms = [];
        private readonly Dictionary<string, bool> roomPower = [];
        private readonly Dictionary<string, Color> roomColors = [];
        private readonly Dictionary<string, Color> selectedRoomColors = [];
        private readonly Dictionary<string, RoomControlVisual> roomControls = [];
        private readonly Dictionary<string, RoomVisualState> roomVisualStates = [];
        private readonly Dictionary<string, double> roomBrightness = [];
        private readonly Dictionary<string, HueBrightnessStream> brightnessUpdates = [];
        private readonly List<CheckBox> selectionCheckBoxes = [];
        private readonly DispatcherTimer stateRefreshTimer;
        private TouchDevice? scrollTouch;
        private Point scrollTouchStart;
        private double scrollOffsetStart;
        private bool touchScrolling;
        private readonly DispatcherTimer roomHoldTimer;
        private TouchDevice? roomTouch;
        private Button? activeRoomButton;
        private HueRoom? activeGestureRoom;
        private Point roomGestureStart;
        private double roomGestureStartBrightness;
        private double roomGestureScrollOffset;
        private RoomGesture roomGesture;
        private bool mouseRoomGesture;
        private long ignorePromotedMouseUntil;
        private bool stateRefreshRunning;
        private readonly HashSet<string> pendingRoomCommands = [];
        private readonly HashSet<string> selectedRoomIds = [];
        private bool selectionInitialized;
        private bool editingSelection;
        private HueBridgeClient? stateClient;
        private HueBridgeInfo? stateClientBridge;
        private Window? stateWindow;

        public HueWidget()
        {
            InitializeComponent();
            stateRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            stateRefreshTimer.Tick += StateRefreshTimer_Tick;
            roomHoldTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            roomHoldTimer.Tick += RoomHoldTimer_Tick;
            Unloaded += (_, _) =>
            {
                foreach (var update in brightnessUpdates.Values) update.Cancel();
                brightnessUpdates.Clear();
                stateRefreshTimer.Stop();
                if (stateWindow != null) stateWindow.StateChanged -= StateWindowChanged;
                stateWindow = null;
                DisposeStateClient();
            };
            Loaded += (_, _) =>
            {
                stateWindow = Window.GetWindow(this);
                if (stateWindow != null) stateWindow.StateChanged += StateWindowChanged;
                UpdateStatePolling();
            };
            IsVisibleChanged += (_, _) => UpdateStatePolling();
            _ = LoadBridgeAsync();
        }

        private void StateWindowChanged(object? sender, EventArgs e) => UpdateStatePolling();
        internal void RefreshResourceMode()=>UpdateStatePolling();

        private void UpdateStatePolling()
        {
            stateRefreshTimer.Interval=ResourcePolicy.PassiveInterval;
            stateRefreshTimer.IsEnabled = IsLoaded && IsVisible &&
                stateWindow?.WindowState != WindowState.Minimized &&
                currentBridge != null && applicationKey != null;
        }

        private void DisposeStateClient()
        {
            stateClient?.Dispose();
            stateClient = null;
            stateClientBridge = null;
        }

        private HueBridgeClient StateClient()
        {
            if (currentBridge == null) throw new InvalidOperationException("No Hue Bridge connected.");
            if (stateClient == null || stateClientBridge != currentBridge)
            {
                DisposeStateClient();
                stateClientBridge = currentBridge;
                stateClient = new HueBridgeClient(currentBridge);
            }
            return stateClient;
        }

        private async void DiscoverButton_Click(object sender, RoutedEventArgs e)
        {
            if (discoveryRunning) return;
            discoveryRunning = true;
            discoverButton.IsEnabled = false;
            statusText.Text = "Searching for Hue Bridge…";
            statusText.SetResourceReference(TextBlock.ForegroundProperty,"PanelTextBrush");
            try
            {
                IReadOnlyList<HueBridgeInfo> bridges = await HueBridgeDiscovery.DiscoverAsync();
                if (bridges.Count == 0)
                {
                    statusText.Text = "Hue Bridge not found";
                    statusText.Foreground = Brushes.Orange;
                    return;
                }
                if (bridges.Count > 1)
                {
                    bridgePicker.Items.Clear();
                    foreach (HueBridgeInfo candidate in bridges)
                        bridgePicker.Items.Add(new ComboBoxItem
                        {
                            Content = $"{candidate.Id}  •  {candidate.InternalIpAddress}",
                            Tag = candidate
                        });
                    statusText.Text = "Choose your Hue Bridge";
                    statusText.SetResourceReference(TextBlock.ForegroundProperty,"PanelTextBrush");
                    bridgePicker.Visibility = Visibility.Visible;
                    discoverButton.Visibility = Visibility.Collapsed;
                    return;
                }
                HueBridgeInfo bridge = bridges[0];
                settingsStore.Save(bridge);
                ShowDiscoveredBridge(bridge);
                await StartPairingAsync();
            }
            catch
            {
                statusText.Text = "Bridge discovery unavailable";
                statusText.Foreground = Brushes.Orange;
            }
            finally
            {
                discoveryRunning = false;
                discoverButton.IsEnabled = true;
            }
        }

        private async void BridgePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (bridgePicker.SelectedItem is not ComboBoxItem { Tag: HueBridgeInfo bridge }) return;
            settingsStore.Save(bridge);
            bridgePicker.Visibility = Visibility.Collapsed;
            ShowDiscoveredBridge(bridge);
            await StartPairingAsync();
        }

        private async Task LoadBridgeAsync()
        {
            try
            {
                HueSettings? settings = settingsStore.Load();
                if (string.IsNullOrWhiteSpace(settings?.BridgeId) ||
                    string.IsNullOrWhiteSpace(settings.BridgeIp)) return;
                HueBridgeInfo bridge = new(settings.BridgeId, settings.BridgeIp);
                selectionInitialized = settings.SelectedGroupedLightIds != null;
                selectedRoomIds.Clear();
                if (settings.SelectedGroupedLightIds != null)
                    selectedRoomIds.UnionWith(settings.SelectedGroupedLightIds);
                selectedRoomColors.Clear();
                if (settings.SelectedRoomColors != null)
                    foreach ((string id, string value) in settings.SelectedRoomColors)
                        if (TryParseColor(value, out Color parsed))
                            selectedRoomColors[id] = parsed;
                string? key = settingsStore.UnprotectKey(settings);
                if (key == null)
                {
                    ShowDiscoveredBridge(bridge);
                    await StartPairingAsync();
                    return;
                }
                using HueBridgeClient client = new(bridge);
                if (await client.ValidateApplicationKeyAsync(key))
                {
                    applicationKey = key;
                    ShowConnectedBridge(bridge);
                }
                else ShowDiscoveredBridge(bridge);
            }
            catch { }
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e) =>
            await StartPairingAsync();

        private async Task StartPairingAsync()
        {
            if (currentBridge == null || pairingRunning) return;
            pairingRunning = true;
            discoverButton.IsEnabled = connectButton.IsEnabled = false;
            discoverButton.Visibility = Visibility.Collapsed;
            connectButton.Visibility = Visibility.Collapsed;
            try
            {
                using HueBridgeClient client = new(currentBridge);
                for (int attempt = 0; attempt < 15; attempt++)
                {
                    int secondsLeft = 30 - attempt * 2;
                    statusText.Text = $"Press the round button on Hue Bridge\nWaiting {secondsLeft} s…";
                    statusText.Foreground = Brushes.Orange;
                    try
                    {
                        string key = await client.CreateApplicationKeyAsync();
                        settingsStore.Save(currentBridge, key);
                        applicationKey = key;
                        ShowConnectedBridge(currentBridge);
                        return;
                    }
                    catch (HueLinkButtonRequiredException)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2));
                    }
                }
                statusText.Text = "Pairing time expired";
                statusText.Foreground = Brushes.Orange;
                connectButton.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                statusText.Text = "Secure Bridge connection failed";
                statusText.Foreground = Brushes.Orange;
                bridgeAddressText.Text = $"{currentBridge.InternalIpAddress}  •  {ex.GetType().Name}";
                discoverButton.Visibility = Visibility.Visible;
            }
            finally
            {
                pairingRunning = false;
                discoverButton.IsEnabled = connectButton.IsEnabled = true;
            }
        }

        private void ShowDiscoveredBridge(HueBridgeInfo bridge)
        {
            DisposeStateClient();
            currentBridge = bridge;
            bridgePicker.Visibility = Visibility.Collapsed;
            statusText.Text = "Bridge found — press its button to pair";
            statusText.Foreground = Brushes.Orange;
            bridgeAddressText.Text = $"{bridge.InternalIpAddress}  •  {bridge.Id}";
            discoverButton.Content = "FIND AGAIN";
            connectButton.Visibility = Visibility.Collapsed;
        }

        private void ShowConnectedBridge(HueBridgeInfo bridge)
        {
            if (currentBridge != bridge) DisposeStateClient();
            currentBridge = bridge;
            bridgePicker.Visibility = Visibility.Collapsed;
            statusText.Text = "Hue Bridge connected";
            statusText.Foreground = new SolidColorBrush(Color.FromRgb(30, 215, 96));
            bridgeAddressText.Text = $"{bridge.InternalIpAddress}  •  {bridge.Id}";
            discoverButton.Content = "FIND AGAIN";
            discoverButton.Visibility = Visibility.Collapsed;
            connectButton.Visibility = Visibility.Collapsed;
            _ = LoadControlsAsync();
        }

        private async Task LoadControlsAsync()
        {
            if (currentBridge == null || applicationKey == null) return;
            statusText.Text = "Loading Hue rooms…";
            try
            {
                using HueBridgeClient client = new(currentBridge);
                currentRooms = await client.GetRoomsAsync(applicationKey);
                setupPanel.Visibility = Visibility.Collapsed;
                controlsPanel.Visibility = Visibility.Visible;

                if (!selectionInitialized) ShowRoomSelection();
                else BuildRoomControls();
            }
            catch
            {
                statusText.Text = "Connected, but rooms could not be loaded";
                statusText.Foreground = Brushes.Orange;
            }
        }

        private void BuildRoomControls()
        {
            editingSelection = false;
            selectionActions.Visibility = Visibility.Collapsed;

            allLightsOffButton.Visibility = Visibility.Visible;
            roomsPanel.Children.Clear();
            roomPower.Clear();
            roomColors.Clear();
            roomControls.Clear();
            roomVisualStates.Clear();
            roomBrightness.Clear();
            UpdateStatePolling();
            HueRoom[] visibleRooms = currentRooms
                .Where(room => selectedRoomIds.Contains(room.GroupedLightId)).ToArray();
            UpdateAllLightsAction();
            if (visibleRooms.Length == 0)
            {
                roomsPanel.Children.Add(new TextBlock
                {
                    Text = "No rooms selected", Foreground = Brushes.White,
                    FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center
                });
                return;
            }

            foreach (HueRoom room in visibleRooms)
            {
                roomPower[room.GroupedLightId] = room.IsOn;
                roomBrightness[room.GroupedLightId] = Math.Clamp(room.Brightness, 0.1, 100);
                roomColors[room.GroupedLightId] = RoomLiveColor(room);
                Button power = new()
                {
                    Tag = room, Margin = new Thickness(0, 5, 0, 5), Height = 68,
                    Padding = new Thickness(0), Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Stretch
                };
                power.SetResourceReference(StyleProperty, "HueRoomButton");
                Stylus.SetIsPressAndHoldEnabled(power, false);

                Grid surface = new() { ClipToBounds = true };
                Border baseLayer = new()
                {
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(Color.FromRgb(27,23,20)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255)),
                    BorderThickness = new Thickness(1)
                };
                baseLayer.SetResourceReference(Border.BackgroundProperty, "ModuleCardSurfaceBrush");
                Border fill = new()
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    CornerRadius = new CornerRadius(8),
                    Background = RoomBrush(room.IsOn, roomColors[room.GroupedLightId])
                };
                OpticallyCenteredText name = new()
                {
                    Text = room.Name, TextBrush = Brushes.White,
                    TextFontFamily = (FontFamily)FindResource("TekoFont"), TextFontSize = 25, TextFontWeight = FontWeights.Bold,
                    CenterCapToBaseline = true, CompactLineBox = true,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    IsHitTestVisible = false
                };
                OpticallyCenteredText value = new()
                {
                    TextBrush = Brushes.White, TextFontFamily = (FontFamily)FindResource("TekoFont"),
                    TextFontSize = 25, TextFontWeight = FontWeights.Bold, InkAlignment = HorizontalAlignment.Right,
                    CenterCapToBaseline = true, CompactLineBox = true,
                    VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(0, 0, 18, 0),
                    IsHitTestVisible = false
                };
                surface.Children.Add(baseLayer);
                surface.Children.Add(fill);
                name.SetResourceReference(OpticallyCenteredText.TextBrushProperty,"PanelTextBrush");
                value.SetResourceReference(OpticallyCenteredText.TextBrushProperty,"PanelTextBrush");
                surface.Children.Add(name);
                surface.Children.Add(value);
                var disconnected = new DisconnectedPowerIcon { Width=32, Height=32, HorizontalAlignment=HorizontalAlignment.Right, VerticalAlignment=VerticalAlignment.Center, Margin=new Thickness(0,0,18,0), IsHitTestVisible=false, Visibility=Visibility.Collapsed };
                surface.Children.Add(disconnected);
                power.Content = surface;
                // Separate light resources have independent power; gradient points do not.
                var lightActions=new StackPanel{Orientation=Orientation.Horizontal};
                foreach(var light in room.Lights.Where(l=>l.SupportsPower)) {
                var off = new Button {
                    Name="LightPowerButton", Tag=light.Id, Width=54, Height=54,
                    HorizontalAlignment=HorizontalAlignment.Right, VerticalAlignment=VerticalAlignment.Center,
                    Margin=new Thickness(3,0,0,0), Padding=new Thickness(12),
                    Background=Brushes.Transparent, BorderThickness=new Thickness(1),
                    ToolTip=light.Name
                };
                off.SetResourceReference(StyleProperty,"HueButton");
                off.SetResourceReference(Control.ForegroundProperty,"PanelTextBrush");
                var powerSymbol = new System.Windows.Shapes.Path {
                    Data=Geometry.Parse("M 12,2 L 12,12 M 6,5 A 9,9 0 1 0 18,5"),
                    StrokeThickness=2, StrokeStartLineCap=PenLineCap.Round, StrokeEndLineCap=PenLineCap.Round,
                    Stretch=Stretch.Uniform, Width=25, Height=27, IsHitTestVisible=false
                };
                powerSymbol.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,new System.Windows.Data.Binding("Foreground"){Source=off});
                off.Content=powerSymbol;
                off.Click+=async (_,e)=>{e.Handled=true;await ToggleLightAsync(room.GroupedLightId,light.Id);};
                lightActions.Children.Add(off);
                }
                var actionsHost=new Viewbox{Child=lightActions,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)};
                name.Ellipsis=true;
                var row=new Grid();row.Children.Add(power);row.Children.Add(actionsHost);
                RoomControlVisual visual = new(power, surface, baseLayer, fill, value, disconnected,lightActions);
                roomControls[room.GroupedLightId] = visual;
                surface.SizeChanged += (_, _) => {
                    double actionsWidth=Math.Min(lightActions.Children.Count*57,surface.ActualWidth*.45);
                    actionsHost.MaxWidth=actionsWidth;
                    value.Margin=new Thickness(0,0,actionsWidth+20,0);
                    disconnected.Margin=new Thickness(0,0,actionsWidth+20,0);
                    name.Margin=new Thickness(12,0,actionsWidth+86,0);
                    UpdateRoomControlVisual(room.GroupedLightId);
                };
                power.PreviewTouchDown += RoomControl_PreviewTouchDown;
                power.PreviewTouchMove += RoomControl_PreviewTouchMove;
                power.PreviewTouchUp += RoomControl_PreviewTouchUp;
                power.PreviewMouseLeftButtonDown += RoomControl_PreviewMouseLeftButtonDown;
                power.PreviewMouseMove += RoomControl_PreviewMouseMove;
                power.PreviewMouseLeftButtonUp += RoomControl_PreviewMouseLeftButtonUp;
                roomsPanel.Children.Add(row);
                UpdateRoomControlVisual(room.GroupedLightId);
            }
        }

        private bool lightOptionsEnabled = true;
        private readonly Dictionary<string, Color> selectionColorDraft = new();
        internal bool CanEditLights => lightOptionsEnabled && !editingSelection && controlsPanel.Visibility == Visibility.Visible;
        internal string LightOptionsLabel => "Edit lights…";
        internal void OpenLightOptions() { if(CanEditLights) ShowRoomSelection(); }
        private void CancelRoomSelection_Click(object sender, RoutedEventArgs e) => BuildRoomControls();
        private async void EditRoomsButton_Click(object sender, RoutedEventArgs e)
        {
            if (!editingSelection)
            {
                ShowRoomSelection();
                return;
            }

            selectionActions.IsEnabled = false;
            try
            {
                await SaveLightColorDraftAsync();
                if (selectionColorDraft.Count > 0)
                {
                    if (currentBridge == null || applicationKey == null) return;
                    using HueBridgeClient client = new(currentBridge);
                    foreach (var change in selectionColorDraft)
                    {
                        var xy = RgbToXy(change.Value);
                        await client.SetRoomColorAsync(applicationKey, change.Key, xy.X, xy.Y);
                        selectedRoomColors[change.Key] = change.Value;
                        settingsStore.SaveRoomColor(change.Key, $"#{change.Value.R:X2}{change.Value.G:X2}{change.Value.B:X2}");
                        currentRooms = currentRooms.Select(room => room.GroupedLightId == change.Key ? room with { IsOn = true, ColorX = xy.X, ColorY = xy.Y } : room).ToArray();
                    }
                }
            selectedRoomIds.Clear();
            foreach (CheckBox checkBox in selectionCheckBoxes)
                if (checkBox.IsChecked == true && checkBox.Tag is HueRoom room)
                    selectedRoomIds.Add(room.GroupedLightId);
            settingsStore.SaveSelection(selectedRoomIds);
            selectionInitialized = true;
            BuildRoomControls();
            }
            catch (Exception ex) { saveRoomSelectionButton.ToolTip = $"Could not save: {ex.Message}"; }
            finally { selectionActions.IsEnabled = true; }
        }

        private void ShowRoomSelection()
        {
            editingSelection = true;
            UpdateStatePolling();
            lightColorDraft.Clear();lightColorButtons.Clear();selectionColorDraft.Clear();saveRoomSelectionButton.ToolTip = "Save selected rooms and colors";

            allLightsOffButton.Visibility = Visibility.Collapsed;
            selectionActions.Visibility = Visibility.Visible;
            roomsPanel.Children.Clear();
            selectionCheckBoxes.Clear();
            foreach (HueRoom room in currentRooms)
            {
                Grid row = new() { Height = 58, Margin = new Thickness(12, 2, 6, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                CheckBox checkBox = new()
                {
                    Content = room.Name, Tag = room,
                    IsChecked = selectedRoomIds.Contains(room.GroupedLightId),
                    Foreground = Brushes.White, FontFamily = (FontFamily)FindResource("TekoFont"),
                    FontSize = 28, FontWeight = FontWeights.SemiBold,
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                checkBox.SetResourceReference(Control.ForegroundProperty, "PanelTextBrush");
                checkBox.SetResourceReference(StyleProperty,"DashboardCheckBox");
                var roomLabel=new OpticallyCenteredText {Text=room.Name,TextFontFamily=(FontFamily)FindResource("TekoFont"),TextFontSize=28,TextFontWeight=FontWeights.SemiBold,Height=42,InkAlignment=HorizontalAlignment.Left,Ellipsis=true};
                roomLabel.SetResourceReference(OpticallyCenteredText.TextBrushProperty,"PanelTextBrush");checkBox.Content=roomLabel;
                Color roomColor = RoomLiveColor(room);
                Button colorButton = new()
                {
                    Tag = room, Width = 46, Height = 42, Padding = new Thickness(0),
                    Background = room.SupportsColor
                        ? new SolidColorBrush(roomColor)
                        : new SolidColorBrush(Color.FromRgb(27,23,20)),
                    BorderBrush = Brushes.White,
                    IsEnabled = room.SupportsColor,
                    ToolTip = room.SupportsColor
                        ? $"Choose {room.Name} color"
                        : $"{room.Name} does not support color"
                };
                colorButton.SetResourceReference(StyleProperty, "HueButton");
                colorButton.Click += RoomColorButton_Click;
                Grid.SetColumn(colorButton, 1);
                row.Children.Add(checkBox);
                if(room.Lights.Length == 0) row.Children.Add(colorButton);
                selectionCheckBoxes.Add(checkBox);
                roomsPanel.Children.Add(row);
                AddLightColorRows(room,row);
            }
        }

        private async Task ToggleRoomAsync(HueRoom room, Button button, bool? requestedOn = null)
        {
            if (currentBridge == null || applicationKey == null || pendingRoomCommands.Contains(room.GroupedLightId) || currentRooms.FirstOrDefault(r=>r.GroupedLightId==room.GroupedLightId)?.IsReachable==false) return;
            bool next = requestedOn ?? !roomPower.GetValueOrDefault(room.GroupedLightId);
            if(!next && brightnessUpdates.Remove(room.GroupedLightId,out var pendingBrightness))pendingBrightness.Cancel();
            button.IsEnabled = false;
            pendingRoomCommands.Add(room.GroupedLightId);
            lastHueCommandError=null;
            try
            {
                using HueBridgeClient client = new(currentBridge);
                await client.SetRoomOnAsync(applicationKey, room.GroupedLightId, next);
                roomPower[room.GroupedLightId] = next;
                currentRooms = currentRooms.Select(item => item.GroupedLightId == room.GroupedLightId
                    ? item with { IsOn = next }
                    : item).ToArray();
                UpdateRoomControlVisual(room.GroupedLightId);
                await RefreshRoomStateAsync(room.GroupedLightId, next);
            }
            catch (Exception ex) { lastHueCommandError = "Hue command was not confirmed: " + ex.Message; }
            finally
            {
                pendingRoomCommands.Remove(room.GroupedLightId);
                button.IsEnabled = true;
                roomVisualStates.Remove(room.GroupedLightId);
                UpdateRoomControlVisual(room.GroupedLightId);
            }
        }

        private async Task ToggleLightAsync(string roomId,string lightId)
        {
            var room=currentRooms.FirstOrDefault(r=>r.GroupedLightId==roomId);
            var light=room?.Lights.FirstOrDefault(l=>l.Id==lightId);
            if(currentBridge==null||applicationKey==null||light==null||light.Reachable==false||!light.SupportsPower||pendingRoomCommands.Contains(roomId))return;
            bool next=!light.IsOn;
            if(brightnessUpdates.Remove(roomId,out var brightness))brightness.Cancel();
            pendingRoomCommands.Add(roomId);
            lastHueCommandError = null;
            UpdateRoomControlVisual(roomId);
            try {
                using var client=new HueBridgeClient(currentBridge);
                await client.SetLightOnAsync(applicationKey,lightId,next);
                var latest=currentRooms.FirstOrDefault(r=>r.GroupedLightId==roomId)??room!;
                var lights=latest.Lights.Select(l=>l.Id==lightId?l with{IsOn=next}:l).ToArray();
                bool anyOn=lights.Any(l=>l.IsOn&&l.Reachable!=false);
                currentRooms=currentRooms.Select(r=>r.GroupedLightId==roomId?r with{Lights=lights,IsOn=anyOn}:r).ToArray();
                roomPower[roomId]=anyOn;
                UpdateRoomControlVisual(roomId);
                await RefreshRoomStateAsync(roomId,anyOn);
            }
            catch (Exception ex) { lastHueCommandError = "Hue command was not confirmed: " + ex.Message; }
            finally {
                pendingRoomCommands.Remove(roomId);
                roomVisualStates.Remove(roomId);
                UpdateRoomControlVisual(roomId);
                UpdateAllLightsAction();
            }
        }

        private async void AllLightsOff_Click(object sender, RoutedEventArgs e)
        {
            if (currentBridge == null || applicationKey == null) return;
            var targets = currentRooms.Where(room => selectedRoomIds.Contains(room.GroupedLightId)&&room.IsReachable!=false).ToArray();
            bool next = !targets.Any(room => roomPower.GetValueOrDefault(room.GroupedLightId, room.IsOn));
            if (sender is Button button) button.IsEnabled = false;
            foreach(var room in targets) {
                if(brightnessUpdates.Remove(room.GroupedLightId,out var pendingBrightness))pendingBrightness.Cancel();
                pendingRoomCommands.Add(room.GroupedLightId);
            }
            lastHueCommandError=null;
            try
            {
                using HueBridgeClient client = new(currentBridge);
                foreach (HueRoom room in targets)
                {
                    await client.SetRoomOnAsync(applicationKey, room.GroupedLightId, next);
                    roomPower[room.GroupedLightId] = next;
                    currentRooms = currentRooms.Select(item => item.GroupedLightId == room.GroupedLightId ? item with { IsOn = next } : item).ToArray();
                    UpdateRoomControlVisual(room.GroupedLightId);
                    await Task.Delay(1100);
                }
            }
            catch (Exception ex) { lastHueCommandError = "Hue command was not confirmed: " + ex.Message; }
            finally {
                foreach(var room in targets) {
                    pendingRoomCommands.Remove(room.GroupedLightId);
                    roomVisualStates.Remove(room.GroupedLightId);
                    UpdateRoomControlVisual(room.GroupedLightId);
                }
                UpdateAllLightsAction();
            }
        }

        private void UpdateAllLightsAction()
        {
            var rooms = currentRooms.Where(room => selectedRoomIds.Contains(room.GroupedLightId)&&room.IsReachable!=false).ToArray();
            bool anyOn = rooms.Any(room => roomPower.GetValueOrDefault(room.GroupedLightId, room.IsOn));
            allLightsOffButton.Content = anyOn ? "All lights off" : "All lights on";
            allLightsOffButton.ToolTip = lastHueCommandError ?? (anyOn ? "Turn selected lights off" : "Turn selected lights on");
            allLightsOffButton.IsEnabled = rooms.Length > 0 && pendingRoomCommands.Count == 0;
        }

        private async void RoomColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: HueRoom room } button) return;
            room = currentRooms.FirstOrDefault(r=>r.GroupedLightId==room.GroupedLightId) ?? room;
            if(room.Lights.Length>0) { ShowRoomSelection(); return; }
            Color selected=RoomLiveColor(room);
            var dialog=CompactColorPicker.CreateOpaque(Window.GetWindow(this),selected.ToString(),s=>selected=CompactColorPicker.Parse(s,Colors.White));
            if(dialog.ShowDialog()==true)
            {
                if(editingSelection) { selectionColorDraft[room.GroupedLightId]=selected;button.Background=new SolidColorBrush(selected); }
                else await ApplyRoomColorAsync(room,selected,button);
            }
        }
        private async Task ApplyRoomColorAsync(HueRoom room, Color color, Button indicator)
        {
            if (currentBridge == null || applicationKey == null) return;
            (double x, double y) = RgbToXy(color);
            indicator.IsEnabled = false;
            lightOptionsEnabled = false;
            try
            {
                using HueBridgeClient client = new(currentBridge);
                await client.SetRoomColorAsync(applicationKey, room.GroupedLightId, x, y);
                roomColors[room.GroupedLightId] = color;
                selectedRoomColors[room.GroupedLightId] = color;
                settingsStore.SaveRoomColor(room.GroupedLightId,
                    $"#{color.R:X2}{color.G:X2}{color.B:X2}");
                currentRooms = currentRooms.Select(item => item.GroupedLightId == room.GroupedLightId
                    ? item with { IsOn = true, ColorX = x, ColorY = y }
                    : item).ToArray();
                roomPower[room.GroupedLightId] = true;
                UpdateRoomControlVisual(room.GroupedLightId);
                indicator.Background = new SolidColorBrush(color);
            }
            catch (Exception ex)
            {
                indicator.ToolTip = $"Color was not applied: {ex.Message}";
                indicator.BorderBrush = Brushes.Orange;
            }
            finally
            {
                indicator.IsEnabled = true;
                lightOptionsEnabled = true;
            }
        }

        private void RoomsScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            double scrollableHeight = roomsScrollViewer.ScrollableHeight;
            overflowIndicatorTop.Visibility = scrollableHeight > 1 && e.VerticalOffset > 1 ? Visibility.Visible : Visibility.Collapsed;
            overflowIndicator.Visibility = scrollableHeight > 1 &&
                                           e.VerticalOffset < scrollableHeight - 1
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void QueueBrightnessUpdate(HueRoom room, double brightness)
        {
            if (!IsLoaded || currentRooms.FirstOrDefault(r=>r.GroupedLightId==room.GroupedLightId)?.IsReachable==false) return;
            if (brightnessUpdates.TryGetValue(room.GroupedLightId, out var previous))
            {
                previous.Set(brightness);
                return;
            }
            var stream = new HueBrightnessStream(async (value, token) =>
            {
                if (currentBridge == null || applicationKey == null ||
                    currentRooms.FirstOrDefault(r=>r.GroupedLightId==room.GroupedLightId)?.IsReachable==false) return;
                using HueBridgeClient client = new(currentBridge);
                await client.SetRoomBrightnessAsync(applicationKey, room.GroupedLightId, value, token);
            });
            stream.Set(brightness);
            brightnessUpdates[room.GroupedLightId] = stream;
            _ = SendBrightnessUpdateAsync(room, stream);
        }

        private async Task SendBrightnessUpdateAsync(
            HueRoom room, HueBrightnessStream stream)
        {
            try
            {
                lastHueCommandError = null;
                await stream.RunAsync();
                roomPower[room.GroupedLightId] = true;
                UpdateRoomControlVisual(room.GroupedLightId);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { lastHueCommandError="Hue command was not confirmed: "+ex.Message; UpdateAllLightsAction(); }
            finally
            {
                if (brightnessUpdates.TryGetValue(room.GroupedLightId, out var active) &&
                    ReferenceEquals(active, stream))
                    brightnessUpdates.Remove(room.GroupedLightId);
            }
        }

        private async void StateRefreshTimer_Tick(object? sender, EventArgs e)
        {
            if (stateRefreshRunning || !selectionActions.IsEnabled || !IsLoaded || !IsVisible ||
                stateWindow?.WindowState == WindowState.Minimized ||
                currentBridge == null || applicationKey == null) return;
            stateRefreshRunning = true;
            try
            {
                // The bridge permits keep-alive. Recreating its handler each second
                // discarded all four resource requests' connection pools.
                HueBridgeClient client = StateClient();
                IReadOnlyList<HueRoom> refreshed = await client.GetRoomsAsync(applicationKey);
                if (!ReferenceEquals(client, stateClient) || !IsLoaded || !IsVisible ||
                    stateWindow?.WindowState == WindowState.Minimized) return;
                currentRooms = refreshed;
                if(editingSelection) { RefreshLightColorButtons(); return; }
                foreach (HueRoom room in refreshed)
                {
                    if (pendingRoomCommands.Contains(room.GroupedLightId) || brightnessUpdates.ContainsKey(room.GroupedLightId)) continue;
                    roomPower[room.GroupedLightId] = room.IsOn;
                    Color color = RoomLiveColor(room);
                    roomColors[room.GroupedLightId] = color;
                    if (activeGestureRoom?.GroupedLightId != room.GroupedLightId && !brightnessUpdates.ContainsKey(room.GroupedLightId))
                        roomBrightness[room.GroupedLightId] = Math.Clamp(room.Brightness, 0.1, 100);
                    UpdateRoomControlVisual(room.GroupedLightId);
                }
                UpdateAllLightsAction();
            }
            catch { }
            finally { stateRefreshRunning = false; }
        }

        private async Task RefreshRoomStateAsync(string roomId, bool expectedOn)
        {
            if (currentBridge == null || applicationKey == null) return;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (attempt > 0) await Task.Delay(180 * attempt);
                try
                {
                    using HueBridgeClient client = new(currentBridge);
                    IReadOnlyList<HueRoom> refreshed = await client.GetRoomsAsync(applicationKey);
                    HueRoom? room = refreshed.FirstOrDefault(item => item.GroupedLightId == roomId);
                    if (room == null) return;
                    currentRooms = refreshed;
                    roomPower[roomId] = room.IsOn;
                    if (room.Brightness > 0.5)
                        roomBrightness[roomId] = Math.Clamp(room.Brightness, 0.1, 100);
                    roomColors[roomId] = RoomLiveColor(room);
                    UpdateRoomControlVisual(roomId);
                    if (room.IsOn == expectedOn &&
                        (!room.IsOn || room.Brightness > 0.5))
                        return;
                }
                catch { }
            }
        }

        private void RoomsScrollViewer_PreviewTouchDown(object sender, TouchEventArgs e)
        {
            if (FindVisualParent<Button>(e.OriginalSource as DependencyObject)?.Tag is HueRoom) return;
            if (scrollTouch != null) return;
            scrollTouch = e.TouchDevice;
            scrollTouchStart = e.GetTouchPoint(roomsScrollViewer).Position;
            scrollOffsetStart = roomsScrollViewer.VerticalOffset;
            touchScrolling = false;
        }

        private void RoomControl_PreviewTouchDown(object? sender, TouchEventArgs e)
        {
            if (sender is not Button { Tag: HueRoom room } button || roomTouch != null) return;
            scrollTouch = null;
            touchScrolling = false;
            roomTouch = e.TouchDevice;
            ignorePromotedMouseUntil = long.MaxValue;
            BeginRoomGesture(button, room, e.GetTouchPoint(roomsScrollViewer).Position, false);
            button.CaptureTouch(e.TouchDevice);
            e.Handled = true;
        }

        private void RoomControl_PreviewTouchMove(object? sender, TouchEventArgs e)
        {
            if (!ReferenceEquals(roomTouch, e.TouchDevice)) return;
            ContinueRoomGesture(e.GetTouchPoint(roomsScrollViewer).Position, true);
            e.Handled = true;
        }

        private async void RoomControl_PreviewTouchUp(object? sender, TouchEventArgs e)
        {
            if (!ReferenceEquals(roomTouch, e.TouchDevice)) return;
            ContinueRoomGesture(e.GetTouchPoint(roomsScrollViewer).Position, true);
            bool toggle = roomGesture == RoomGesture.Pending;
            HueRoom? room = activeGestureRoom;
            Button? button = activeRoomButton;
            button?.ReleaseTouchCapture(e.TouchDevice);
            ignorePromotedMouseUntil = Environment.TickCount64 + 750;
            ResetRoomGesture();
            e.Handled = true;
            if (toggle && room != null && button != null)
                await ToggleRoomAsync(room, button);
        }

        private void BeginRoomGesture(Button button, HueRoom room, Point start, bool mouse)
        {
            activeRoomButton = button;
            activeGestureRoom = room;
            roomGestureStart = start;
            roomGestureStartBrightness = roomBrightness.GetValueOrDefault(room.GroupedLightId, 50);
            roomGestureScrollOffset = roomsScrollViewer.VerticalOffset;
            roomGesture = RoomGesture.Pending;
            mouseRoomGesture = mouse;
            roomHoldTimer.Stop();
            roomHoldTimer.Start();
        }

        private void RoomHoldTimer_Tick(object? sender, EventArgs e)
        {
            roomHoldTimer.Stop();
            if (roomGesture == RoomGesture.Pending)
                roomGesture = RoomGesture.Brightness;
        }

        private void ContinueRoomGesture(Point current, bool allowVerticalScroll)
        {
            if (activeRoomButton == null || activeGestureRoom == null) return;
            double deltaX = current.X - roomGestureStart.X;
            double deltaY = current.Y - roomGestureStart.Y;
            if (roomGesture == RoomGesture.Pending &&
                (Math.Abs(deltaX) >= 10 || Math.Abs(deltaY) >= 10))
            {
                roomHoldTimer.Stop();
                // Horizontal dragging adjusts brightness immediately; the hold timer
                // is an additional way to start, not a deadline that cancels a swipe.
                roomGesture = Math.Abs(deltaX) >= Math.Abs(deltaY)
                    ? RoomGesture.Brightness
                    : allowVerticalScroll ? RoomGesture.Scroll : RoomGesture.Cancelled;
            }
            if (roomGesture == RoomGesture.Scroll)
                roomsScrollViewer.ScrollToVerticalOffset(roomGestureScrollOffset - deltaY);
            else if (roomGesture == RoomGesture.Brightness)
            {
                // The visual remains full width, but the complete brightness range is
                // reached before the finger approaches either module edge.
                const double safeTouchInset = 44;
                double touchTravel = Math.Max(1, activeRoomButton.ActualWidth - safeTouchInset * 2);
                double brightness = Math.Clamp(
                    roomGestureStartBrightness + deltaX / touchTravel * 100, 0.1, 100);
                roomBrightness[activeGestureRoom.GroupedLightId] = brightness;
                roomPower[activeGestureRoom.GroupedLightId] = true;
                UpdateRoomControlVisual(activeGestureRoom.GroupedLightId);
                QueueBrightnessUpdate(activeGestureRoom, brightness);
            }
        }

        private void RoomControl_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (roomTouch != null || Environment.TickCount64 < ignorePromotedMouseUntil ||
                sender is not Button { Tag: HueRoom room } button) return;
            BeginRoomGesture(button, room, e.GetPosition(roomsScrollViewer), true);
            button.CaptureMouse();
            e.Handled = true;
        }

        private void RoomControl_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!mouseRoomGesture || !ReferenceEquals(sender, activeRoomButton) ||
                e.LeftButton != MouseButtonState.Pressed) return;
            ContinueRoomGesture(e.GetPosition(roomsScrollViewer), false);
            e.Handled = true;
        }

        private async void RoomControl_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!mouseRoomGesture || !ReferenceEquals(sender, activeRoomButton)) return;
            ContinueRoomGesture(e.GetPosition(roomsScrollViewer), false);
            bool toggle = roomGesture == RoomGesture.Pending;
            HueRoom? room = activeGestureRoom;
            Button? button = activeRoomButton;
            button?.ReleaseMouseCapture();
            ResetRoomGesture();
            e.Handled = true;
            if (toggle && room != null && button != null)
                await ToggleRoomAsync(room, button);
        }

        private void ResetRoomGesture()
        {
            roomHoldTimer.Stop();
            roomTouch = null;
            activeRoomButton = null;
            activeGestureRoom = null;
            roomGesture = RoomGesture.None;
            mouseRoomGesture = false;
        }

        private void UpdateRoomControlVisual(string roomId)
        {
            if (!roomControls.TryGetValue(roomId, out RoomControlVisual? visual)) return;
            double brightness = Math.Clamp(roomBrightness.GetValueOrDefault(roomId, 0.1), 0.1, 100);
            bool offline = currentRooms.FirstOrDefault(room=>room.GroupedLightId==roomId)?.IsReachable==false;
            bool on = !offline && roomPower.GetValueOrDefault(roomId);
            Color color = roomColors.GetValueOrDefault(roomId, Colors.White);
            var liveRoom=currentRooms.FirstOrDefault(r=>r.GroupedLightId==roomId);
            foreach(var action in visual.LightActions.Children.OfType<Button>()) {
                var light=liveRoom?.Lights.FirstOrDefault(l=>l.Id==(string)action.Tag);
                action.IsEnabled=light?.SupportsPower==true&&light.Reachable!=false&&!pendingRoomCommands.Contains(roomId);
                action.Opacity=light?.IsOn==true&&light.Reachable!=false?1:.45;
                action.ToolTip=light==null?"Light unavailable":light.Reachable==false?$"{light.Name}: Unavailable; last reported state is not current":$"{light.Name}: turn {(light.IsOn?"off":"on")}";
                action.Background=light?.IsOn==true&&light.Reachable!=false?RoomBrush(true,LightColor(light)):Brushes.Transparent;
                ToolTipService.SetShowOnDisabled(action,true);
            }
            var next = new RoomVisualState(visual.Surface.ActualWidth * brightness / 100, brightness, offline, on, color, liveRoom==null?"":string.Join(";",RoomLiveColors(liveRoom)));
            UpdateAllLightsAction();
            visual.Button.IsEnabled=!offline&&!pendingRoomCommands.Contains(roomId);
            ToolTipService.SetShowOnDisabled(visual.Button,true);
            visual.Button.ToolTip=lastHueCommandError ?? (offline?"Unavailable: Bridge cannot confirm the light state.":null);
            if (roomVisualStates.TryGetValue(roomId, out var previous) && previous == next) return;
            roomVisualStates[roomId] = next;
            visual.Fill.Width = next.Width;
            visual.Fill.Background = liveRoom==null?RoomBrush(on,color):RoomLiveBrush(liveRoom,on);
            visual.Base.BorderBrush = on
                ? new SolidColorBrush(Color.FromArgb(190, color.R, color.G, color.B))
                : new SolidColorBrush(Color.FromArgb(55, 255, 255, 255));
            visual.Base.BorderThickness = on ? new Thickness(2) : new Thickness(1);
            visual.Disconnected.Visibility=offline?Visibility.Visible:Visibility.Collapsed;
            visual.Disconnected.Margin=new Thickness(0,0,visual.Value.Margin.Right,0);
            visual.Value.Visibility=offline?Visibility.Collapsed:Visibility.Visible;
            visual.Value.Text = !on ? "0%" : brightness <= 0.5
                ? "ON"
                : $"{Math.Round(brightness):0}%";
            visual.Value.Opacity = on ? 1 : 0.55;
        }

        private static T? FindVisualParent<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match) return match;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private void RoomsScrollViewer_PreviewTouchMove(object sender, TouchEventArgs e)
        {
            if (!ReferenceEquals(scrollTouch, e.TouchDevice)) return;
            Point current = e.GetTouchPoint(roomsScrollViewer).Position;
            double delta = current.Y - scrollTouchStart.Y;
            if (!touchScrolling && Math.Abs(delta) < 10) return;
            if (!touchScrolling)
            {
                touchScrolling = true;
                roomsScrollViewer.CaptureTouch(e.TouchDevice);
            }
            roomsScrollViewer.ScrollToVerticalOffset(scrollOffsetStart - delta);
            e.Handled = true;
        }

        private void RoomsScrollViewer_PreviewTouchUp(object sender, TouchEventArgs e)
        {
            if (!ReferenceEquals(scrollTouch, e.TouchDevice)) return;
            if (touchScrolling)
            {
                roomsScrollViewer.ReleaseTouchCapture(e.TouchDevice);
                e.Handled = true;
            }
            scrollTouch = null;
            touchScrolling = false;
        }

        private static (double X, double Y) RgbToXy(Color color)
        {
            double Convert(byte value)
            {
                double channel = value / 255d;
                return channel > 0.04045 ? Math.Pow((channel + 0.055) / 1.055, 2.4) : channel / 12.92;
            }
            double r = Convert(color.R), g = Convert(color.G), b = Convert(color.B);
            double x = r * 0.664511 + g * 0.154324 + b * 0.162028;
            double y = r * 0.283881 + g * 0.668433 + b * 0.047685;
            double z = r * 0.000088 + g * 0.072310 + b * 0.986039;
            double total = x + y + z;
            return total <= 0.00001 ? (0.3127, 0.3290) : (x / total, y / total);
        }

        private static bool TryParseColor(string value, out Color color)
        {
            try
            {
                color = (Color)ColorConverter.ConvertFromString(value);
                return true;
            }
            catch
            {
                color = Colors.White;
                return false;
            }
        }

        private static Color XyToColor(double? xValue, double? yValue)
        {
            if (xValue is null || yValue is null || yValue <= 0.00001)
                return Colors.Gray;
            double x = xValue.Value, y = yValue.Value;
            double z = 1.0 - x - y;
            double luminance = 1.0;
            double r = 1.656492 * x / y - 0.354851 * luminance - 0.255038 * z / y;
            double g = -0.707196 * x / y + 1.655397 * luminance + 0.036152 * z / y;
            double b = 0.051713 * x / y - 0.121364 * luminance + 1.011530 * z / y;
            double Gamma(double value) => value <= 0.0031308
                ? 12.92 * value : 1.055 * Math.Pow(Math.Max(0, value), 1 / 2.4) - 0.055;
            r = Gamma(r); g = Gamma(g); b = Gamma(b);
            double maximum = Math.Max(r, Math.Max(g, b));
            if (maximum > 1) { r /= maximum; g /= maximum; b /= maximum; }
            return Color.FromRgb((byte)Math.Round(255 * Math.Clamp(r, 0, 1)),
                (byte)Math.Round(255 * Math.Clamp(g, 0, 1)),
                (byte)Math.Round(255 * Math.Clamp(b, 0, 1)));
        }

        private static Brush RoomBrush(bool on, Color color)
        {
            if (!on) return Brushes.Transparent;
            return new SolidColorBrush(Color.FromArgb(165, color.R, color.G, color.B));
        }

        private sealed record RoomControlVisual(
            Button Button, Grid Surface, Border Base, Border Fill, OpticallyCenteredText Value, DisconnectedPowerIcon Disconnected, StackPanel LightActions);
        private readonly record struct RoomVisualState(double Width, double Brightness, bool Offline, bool On, Color Color, string Colors);
        private enum RoomGesture { None, Pending, Brightness, Scroll, Cancelled }
    }
}

