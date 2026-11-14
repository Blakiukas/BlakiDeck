using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ShapePath = System.Windows.Shapes.Path;

namespace Ekrano_valdymas
{
    internal sealed partial class FullscreenBrowserWindow : Window
    {
        internal const double ToolbarHeight = 64;
        private double toolbarLayoutHeight=ToolbarHeight;
        private Action? reflowTelemetry;
        private readonly WebView2 webView = new();
        private readonly Button back = IconButton(BrowserIcon.Back, "Back"), forward = IconButton(BrowserIcon.Forward, "Forward");
        private readonly UsageTelemetryView telemetryGrid = new();
        private readonly TextBox address = new() { FontSize=18, VerticalContentAlignment=VerticalAlignment.Center, Height=34, Margin=new Thickness(8,0,8,18), ToolTip="Web address — press Enter to navigate" };
        internal event Action<string,string>? SavePageRequested;
        internal event Action<bool>? TelemetryChanged;
        private bool telemetryEnabled;
        private MenuItem? telemetryMenuItem;
        private readonly Grid addressArea=new();
        private readonly Image siteIcon=new() { Width=22,Height=22,Margin=new Thickness(8,0,6,0),Stretch=Stretch.Uniform };

        private readonly Button zoomOutButton = TextButton("−", "Decrease Web zoom");
        private readonly Button zoomInButton = TextButton("+", "Increase Web zoom");
        private readonly Button zoomSettingsButton = TextButton("⚙", "Web zoom settings");
        private readonly OpticallyCenteredText zoomValueText = ToolbarText("100%", accent: false);
        private readonly Button[] toolbarButtons = new Button[5];
        private readonly Border toolbarBar;
        private readonly Border toolbarDimOverlay;
        private readonly Grid toolbarLayers;
        private readonly string initialUrl;
        private readonly Exception? profileInitializationError;
        private bool browserInitializationStarted, browserClosed;
        private readonly string webZoomSettingsFile = Path.Combine(
            DiagnosticState.LocalApplicationData,
            "Ekrano_valdymas", "web-zoom-settings.json");
        private readonly Window dashboardWindow;
        private readonly Action<string>? currentUrlSaved;
        private readonly Action? linkedMoveStarted;
        private readonly Action<double, double>? linkedMoveRequested;
        private readonly Action? linkedMoveCompleted;
        private readonly Action<Point>? menuRequested;
        private long networkBytesReceived;
        private System.Windows.Rect? dashboardWorkArea;
        private bool toolbarMouseCaptured;
        private Point toolbarMouseStart;
        private TouchDevice? toolbarTouchDevice;
        private Point toolbarTouchStart;
        private Point toolbarTouchDownPoint;
        private long lastToolbarTapTick;
        private Point lastToolbarTapPoint;
        private static readonly double[] WebZoomSteps = { 0.80, 0.90, 1.00, 1.10, 1.25, 1.50, 1.75, 2.00 };
        private double webZoomFactor = 1.0;
        private bool webZoomEnabled = true;
        private bool webZoomAllSites = true;
        private bool autoDashChromeHidden;
        private readonly Dictionary<string, double> webSiteZoomFactors = new(StringComparer.OrdinalIgnoreCase);

        private static readonly DependencyProperty ToolbarVisibleHeightProperty =
            DependencyProperty.Register(nameof(ToolbarVisibleHeight), typeof(double),
                typeof(FullscreenBrowserWindow), new PropertyMetadata(ToolbarHeight, OnToolbarVisibleHeightChanged));

        private double ToolbarVisibleHeight
        {
            get => (double)GetValue(ToolbarVisibleHeightProperty);
            set => SetValue(ToolbarVisibleHeightProperty, value);
        }

        public FullscreenBrowserWindow(string url, Window owner, string? previewPath = null,
            Action? previewSaved = null, Action<string>? currentUrlSaved = null,
            Action? localActivity = null, Action? linkedMoveStarted = null,
            Action<double, double>? linkedMoveRequested = null,
            Action? linkedMoveCompleted = null, Action<Point>? menuRequested = null)
        {
            initialUrl = url; dashboardWindow = owner;
            pageActivity = localActivity;
            try { webView.CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = WebProfileLocation.Resolve() }; }
            catch(Exception ex) { profileInitializationError = ex; }
            this.currentUrlSaved = currentUrlSaved;
            // Preserve the existing optional constructor contract for probes.
            // Normal saved entries install a transactional callback via ConfigurePreviewCapture.
            if(!string.IsNullOrWhiteSpace(previewPath))ConfigurePreviewCapture(()=>!browserClosed,()=>previewPath,
                _=>{previewSaved?.Invoke();return true;},()=>File.Exists(previewPath));
            this.linkedMoveStarted = linkedMoveStarted;
            this.linkedMoveRequested = linkedMoveRequested;
            this.linkedMoveCompleted = linkedMoveCompleted;
            this.menuRequested = menuRequested;
            LoadWebZoomSettings();
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            Background = Brushes.Black; Topmost = false; ShowInTaskbar = false;
            // ShowInTaskbar alone does not define Alt+Tab eligibility. Keep the
            // dashboard as the sole shell entry without changing linked Z-order.
            SourceInitialized += (_,_) =>
            {
                IntPtr handle=GetNativeHandle();
                long style=GetWindowLongPtr(handle,-20).ToInt64();
                SetWindowLongPtr(handle,-20,new IntPtr((style|0x80L)&~0x40000L));
            };
            Grid root = new() { ClipToBounds = true };
            toolbarBar = new Border { Background = new SolidColorBrush(Color.FromRgb(44,44,44)), Padding = new Thickness(10,4,10,4),
                Height = ToolbarHeight, VerticalAlignment = VerticalAlignment.Bottom };
            toolbarBar.SetResourceReference(Border.BackgroundProperty, "PanelMetalBrush");
            CommonMetal.SetSurface(toolbarBar, true);
            Grid toolbar = new();
            toolbar.RowDefinitions.Add(new RowDefinition{Height=new GridLength(ToolbarHeight-8)});
            toolbar.RowDefinitions.Add(new RowDefinition{Height=new GridLength(0)});
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition());
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel actions = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
            back.Click += (_,_) => { if (webView.CanGoBack) webView.GoBack(); }; forward.Click += (_,_) => { if (webView.CanGoForward) webView.GoForward(); };
            Button refresh = IconButton(BrowserIcon.Refresh, "Refresh"), external = IconButton(BrowserIcon.External, "Open in browser"), close = TextButton("CLOSE WEB", "Close web");
            close.Margin = new Thickness(10, 0, 4, 0);
            toolbarButtons[0] = back; toolbarButtons[1] = forward; toolbarButtons[2] = refresh;
            toolbarButtons[3] = external; toolbarButtons[4] = close;
            refresh.Click += (_,_) => { if (webView.CoreWebView2 != null) webView.Reload(); };
            external.Click += (_,_) => { if (webView.Source is Uri current) WebRuntimeSupport.OpenExternal(this, current); };
            close.Click += (_,_) => Close();
            foreach (Button button in new[] { back, forward, refresh, external }) actions.Children.Add(button);
            Grid.SetColumn(actions, 0); toolbar.Children.Add(actions);
            StackPanel zoomPanel = new()
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            zoomOutButton.Width = 42;
            zoomInButton.Width = 42;
            zoomSettingsButton.Width = 42;
            zoomOutButton.Content = ToolbarText("−", accent: false);
            zoomInButton.Content = ToolbarText("+", accent: false);
            var settingsGlyph = new OpticallyCenteredText { Width=24, Height=24, Text="\uE713", TextFontFamily=new FontFamily("Segoe MDL2 Assets"), TextFontSize=24, IsHitTestVisible=false };
            settingsGlyph.SetResourceReference(OpticallyCenteredText.TextBrushProperty, "PanelTextBrush");
            zoomSettingsButton.Content = settingsGlyph;
            zoomSettingsButton.Margin = new Thickness(20,0,4,0); // 4 + 20 = three original 8-DIP gaps.
            zoomValueText.Width = 72;
            zoomValueText.HorizontalAlignment = HorizontalAlignment.Center;
            zoomOutButton.Click += (_, _) => ChangeWebZoom(-1);
            zoomInButton.Click += (_, _) => ChangeWebZoom(1);
            zoomSettingsButton.Click += (_, _) => OpenWebZoomSettings();
            zoomPanel.Children.Add(zoomOutButton);
            zoomPanel.Children.Add(zoomValueText);
            zoomPanel.Children.Add(zoomInButton);
            zoomPanel.Children.Add(zoomSettingsButton);
            actions.Children.Add(new Border { Width = 38, IsHitTestVisible = false });
            actions.Children.Add(zoomPanel);
            address.SetResourceReference(Control.ForegroundProperty,"PanelTextBrush");
            address.Background=new SolidColorBrush(Color.FromArgb(70,0,0,0));
            address.Text=url;
            address.ContextMenuOpening+=TextEditMenu.Opening;
            address.KeyDown+=(_,e)=>{
                if(e.Key!=Key.Enter)return;
                string value=address.Text.Trim();if(!value.Contains("://"))value="https://"+value;
                if(Uri.TryCreate(value,UriKind.Absolute,out var uri)&&uri.Scheme is "http" or "https")webView.Source=uri;
                e.Handled=true;
            };
            webView.SourceChanged+=(_,_)=>{if(!address.IsKeyboardFocusWithin)address.Text=webView.Source?.AbsoluteUri??"";};
            address.LostKeyboardFocus+=(_,_)=>address.Text=webView.Source?.AbsoluteUri??address.Text;
            address.Margin=new Thickness(2,0,8,0);
            addressArea.Margin=new Thickness(0,0,0,18);
            addressArea.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            addressArea.ColumnDefinitions.Add(new ColumnDefinition());
            addressArea.Children.Add(siteIcon);Grid.SetColumn(address,1);addressArea.Children.Add(address);
            Grid.SetColumn(addressArea,1);toolbar.Children.Add(addressArea);
            Grid.SetColumn(telemetryGrid,1);
            telemetryGrid.Margin=new Thickness(8,0,12,0);
            telemetryGrid.HorizontalAlignment=HorizontalAlignment.Right;
            telemetryGrid.VerticalAlignment=VerticalAlignment.Bottom;
            telemetryGrid.IsHitTestVisible=false;
            toolbar.Children.Add(telemetryGrid);
            var savePage=TextButton("↓","Save current address to Web shortcuts");
            savePage.Width=44;
            var saveArrow=new ShapePath { Width=18,Height=24,Stretch=Stretch.Uniform,StrokeThickness=3.2,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Data=Geometry.Parse("M9,1 L9,22 M2,15 L9,22 L16,15") };
            saveArrow.SetResourceReference(ShapePath.StrokeProperty,"PanelTextBrush");savePage.Content=saveArrow;
            savePage.Click+=(_,_)=>{
                if(webView.Source is not Uri {Scheme:"http" or "https"} current)return;
                try {SavePageRequested?.Invoke(current.AbsoluteUri,webView.CoreWebView2?.DocumentTitle??current.Host);savePage.ToolTip="Saved to Web shortcuts";}
                catch(Exception ex){AppMessage.Show(this,ex.Message,"Could not save Web shortcut");}
            };
            Grid.SetColumn(savePage,2);toolbar.Children.Add(savePage);
            Grid.SetColumn(close, 3); toolbar.Children.Add(close);
            toolbarDimOverlay = new Border { Background = Brushes.Black, Opacity = 0, IsHitTestVisible = false };
            // The dim layer covers the whole chrome, including its metal padding.
            toolbarBar.Child = toolbar;
            toolbarLayers = new() { ClipToBounds = true, Height = ToolbarHeight, VerticalAlignment = VerticalAlignment.Top };
            toolbarLayers.Children.Add(toolbarBar); toolbarLayers.Children.Add(toolbarDimOverlay);
            reflowTelemetry=()=>{
                if(toolbar.ActualWidth<=0)return;
                telemetryGrid.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
                bool separate=telemetryEnabled&&addressArea.ActualWidth<telemetryGrid.NaturalWidth+20;
                Grid.SetRow(telemetryGrid,separate?1:0);Grid.SetColumn(telemetryGrid,separate?0:1);Grid.SetColumnSpan(telemetryGrid,separate?4:1);
                toolbar.RowDefinitions[1].Height=new GridLength(separate?24:0);
                addressArea.Margin=new Thickness(0,0,0,telemetryEnabled&&!separate?26:0);
                double height=ToolbarHeight+(separate?24:0);
                if(Math.Abs(toolbarLayoutHeight-height)<.01)return;
                toolbarLayoutHeight=height;toolbarBar.Height=height;BeginAnimation(ToolbarVisibleHeightProperty,null);ToolbarVisibleHeight=autoDashChromeHidden?0:height;UpdatePageInteractionBounds();
            };
            toolbar.SizeChanged+=(_,_)=>reflowTelemetry();addressArea.SizeChanged+=(_,_)=>reflowTelemetry();
            root.Children.Add(webView); root.Children.Add(toolbarLayers); Content = root;
            SizeChanged += (_, _) => UpdatePageInteractionBounds();
            toolbarBar.PreviewMouseLeftButtonDown += Toolbar_PreviewMouseLeftButtonDown;
            toolbarBar.PreviewMouseMove += Toolbar_PreviewMouseMove;
            toolbarBar.PreviewMouseLeftButtonUp += Toolbar_PreviewMouseLeftButtonUp;
            toolbarBar.PreviewMouseRightButtonDown += Toolbar_PreviewMouseRightButtonDown;
            toolbarBar.PreviewTouchDown += Toolbar_PreviewTouchDown;
            toolbarBar.PreviewTouchMove += Toolbar_PreviewTouchMove;
            toolbarBar.PreviewTouchUp += Toolbar_PreviewTouchUp;
            Loaded += Browser_Loaded; webView.NavigationStarting+=PreviewNavigationStarting;webView.NavigationCompleted += WebView_NavigationCompleted; KeyDown += (_,e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
            PreviewMouseMove += (_, _) => localActivity?.Invoke();
            PreviewMouseDown += (_, _) => localActivity?.Invoke();
            PreviewTouchDown += (_, _) => localActivity?.Invoke();
            PreviewKeyDown += (_, _) => localActivity?.Invoke();
            Closing += (_, _) => { if(webSettingsMenu is {} menu)menu.IsOpen=false;ClosePreviewCapture();SaveCurrentUrl(); SaveWebZoomSettings(); };
            IsVisibleChanged += (_, _) => RequestResourcePolicy();
            StateChanged += ResourceVisibilityChanged;
            Closed += (_, _) => { browserClosed=true;CloseResourcePolicy();displayRestore?.Cancel();displayRestore?.Dispose();webView.Dispose(); };
        }

        private static void OnToolbarVisibleHeightChanged(
            DependencyObject dependencyObject,
            DependencyPropertyChangedEventArgs e)
        {
            FullscreenBrowserWindow window = (FullscreenBrowserWindow)dependencyObject;
            double height = Math.Clamp((double)e.NewValue, 0, window.toolbarLayoutHeight);
            window.toolbarLayers.Height = height;
            // The full-height, bottom-aligned child slides above this shrinking
            // viewport. Its own layout stays fixed, so controls leave together.
        }

        internal void SetAutoDashChromeHidden(bool hidden, TimeSpan duration)
        {
            if (autoDashChromeHidden == hidden) return;
            autoDashChromeHidden = hidden;
            UpdatePageInteractionBounds(duration > TimeSpan.Zero);
            if (!hidden)
            {
                toolbarBar.Visibility = Visibility.Visible;
                toolbarBar.IsHitTestVisible = true;
            }
            double current = toolbarLayers.ActualHeight;
            BeginAnimation(ToolbarVisibleHeightProperty, null);
            ToolbarVisibleHeight = current;
            DoubleAnimation animation = new(hidden ? 0d : toolbarLayoutHeight, duration)
            {
                EasingFunction = CreateInteractionEasing(),
                FillBehavior = FillBehavior.HoldEnd
            };
            animation.Completed += (_, _) =>
            {
                if (autoDashChromeHidden == hidden)
                {
                    toolbarBar.IsHitTestVisible = !hidden;
                    if (hidden) toolbarBar.Visibility = Visibility.Collapsed;
                }
            };
            BeginAnimation(ToolbarVisibleHeightProperty, animation,
                HandoffBehavior.SnapshotAndReplace);
        }

        private bool IsToolbarControl(DependencyObject? source)
        {
            for (DependencyObject? current = source; current != null; current = VisualTreeHelper.GetParent(current))
            {
                if (current is ButtonBase or TextBox)
                    return true;
                if (ReferenceEquals(current, toolbarBar))
                    break;
            }
            return false;
        }

        private void Toolbar_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.StylusDevice != null || IsToolbarControl(e.OriginalSource as DependencyObject))
                return;
            toolbarMouseCaptured = true;
            toolbarMouseStart = PointToScreen(e.GetPosition(this));
            toolbarBar.CaptureMouse();
            linkedMoveStarted?.Invoke();
            e.Handled = true;
        }

        private void Toolbar_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!toolbarMouseCaptured || e.LeftButton != MouseButtonState.Pressed)
                return;
            Point current = PointToScreen(e.GetPosition(this));
            linkedMoveRequested?.Invoke(current.X - toolbarMouseStart.X, current.Y - toolbarMouseStart.Y);
            toolbarMouseStart = current;
            e.Handled = true;
        }

        private void Toolbar_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!toolbarMouseCaptured) return;
            toolbarMouseCaptured = false;
            toolbarBar.ReleaseMouseCapture();
            linkedMoveCompleted?.Invoke();
            e.Handled = true;
        }

        private void Toolbar_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsToolbarControl(e.OriginalSource as DependencyObject)) return;
            menuRequested?.Invoke(PointToScreen(e.GetPosition(this)));
            e.Handled = true;
        }

        private void Toolbar_PreviewTouchDown(object? sender, TouchEventArgs e)
        {
            if (toolbarTouchDevice != null || IsToolbarControl(e.OriginalSource as DependencyObject))
                return;
            toolbarTouchDevice = e.TouchDevice;
            toolbarTouchStart = PointToScreen(e.GetTouchPoint(this).Position);
            toolbarTouchDownPoint = toolbarTouchStart;
            e.TouchDevice.Capture(toolbarBar);
            linkedMoveStarted?.Invoke();
            e.Handled = true;
        }

        private void Toolbar_PreviewTouchMove(object? sender, TouchEventArgs e)
        {
            if (!ReferenceEquals(toolbarTouchDevice, e.TouchDevice)) return;
            Point current = PointToScreen(e.GetTouchPoint(this).Position);
            linkedMoveRequested?.Invoke(current.X - toolbarTouchStart.X, current.Y - toolbarTouchStart.Y);
            toolbarTouchStart = current;
            e.Handled = true;
        }

        private void Toolbar_PreviewTouchUp(object? sender, TouchEventArgs e)
        {
            if (!ReferenceEquals(toolbarTouchDevice, e.TouchDevice)) return;
            Point localPoint = e.GetTouchPoint(this).Position;
            Point point = PointToScreen(localPoint);
            e.TouchDevice.Capture(null);
            toolbarTouchDevice = null;
            linkedMoveCompleted?.Invoke();
            long now = Environment.TickCount64;
            bool stayedInTapRange = Math.Abs(point.X - toolbarTouchDownPoint.X) <= 8 &&
                Math.Abs(point.Y - toolbarTouchDownPoint.Y) <= 8;
            if (stayedInTapRange && now - lastToolbarTapTick <= 500 &&
                Math.Abs(point.X - lastToolbarTapPoint.X) <= 24 &&
                Math.Abs(point.Y - lastToolbarTapPoint.Y) <= 24)
            {
                lastToolbarTapTick = 0;
                menuRequested?.Invoke(PointToScreen(localPoint));
            }
            else if (stayedInTapRange)
            {
                lastToolbarTapTick = now;
                lastToolbarTapPoint = point;
            }
            else
                lastToolbarTapTick = 0;
            e.Handled = true;
        }

        private Point GetScreenDip(Point localPoint)
        {
            Point screenPixels = PointToScreen(localPoint);
            Matrix fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
                ?? Matrix.Identity;
            return fromDevice.Transform(screenPixels);
        }

        private void SaveCurrentUrl()
        {
            try
            {
                if (webView.Source is Uri { Scheme: "http" or "https" } current)
                    currentUrlSaved?.Invoke(current.AbsoluteUri);
            }
            catch { }
        }

        public void SetTelemetry(string text, bool visible)
        {
            telemetryGrid.SetCanonicalText(text);
            telemetryGrid.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            telemetryEnabled=visible;
            reflowTelemetry?.Invoke();
            if(telemetryMenuItem!=null)telemetryMenuItem.IsChecked=visible;
        }

        public void SetDashboardAppearance(Color baseColor, double opacity)
        {
            toolbarBar.SetResourceReference(Border.BackgroundProperty, "PanelMetalBrush");
            CommonMetal.SetSurface(toolbarBar, true);
        }

        public void SetAutoDimmed(bool dimmed) => SetAutoDimOpacity(dimmed ? 0.70 : 1.0);

        public void SetAutoDimOpacity(double opacity)
            => SetAutoDimAmount(1d - Math.Clamp(opacity, 0, 1));

        public void SetUiOpacity(double opacity)
        {
            toolbarLayers.BeginAnimation(UIElement.OpacityProperty, null);
            toolbarLayers.Opacity = Math.Clamp(opacity, 0.60, 1.0);
        }

        public void SetAutoDimAmount(double amount)
        {
            DoubleAnimation animation = new(Math.Clamp(amount, 0, 0.80),
                TimeSpan.FromMilliseconds(650))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.HoldEnd
            };
            toolbarDimOverlay.BeginAnimation(UIElement.OpacityProperty, animation,
                HandoffBehavior.SnapshotAndReplace);
        }

        public void SetTextOpacity(double opacity)
        {
            double safeOpacity = Math.Clamp(opacity, 0.35, 1.0);
            telemetryGrid.Opacity = safeOpacity;
            foreach (Button button in toolbarButtons)
                if (button.Content is UIElement content) content.Opacity = safeOpacity;
            zoomValueText.Opacity = safeOpacity;
            foreach (Button button in new[] { zoomOutButton, zoomInButton, zoomSettingsButton })
                if (button.Content is UIElement content) content.Opacity = safeOpacity;
        }

        public void SetAlwaysOnTop(bool enabled) => Topmost = false;

        public void SetDashboardExpanded(bool expanded)
        {
            dashboardExpanded = expanded;
            RequestResourcePolicy();
        }

        public void SetDashboardWorkArea(System.Windows.Rect workArea) =>
            dashboardWorkArea = workArea;

        public System.Windows.Rect GetCurrentBounds() => new(
            Left,
            Top,
            ActualWidth > 0 ? ActualWidth : Width,
            ActualHeight > 0 ? ActualHeight : Height);

        public IntPtr GetNativeHandle() => new WindowInteropHelper(this).Handle;

        internal FrameworkElement GeometryRoot => (FrameworkElement)Content;
        internal Border GeometryToolbar => toolbarBar;
        internal UsageTelemetryView GeometryTelemetry => telemetryGrid;
        internal IReadOnlyList<Button> GeometryToolbarButtons => toolbarButtons;
        internal WebView2 GeometryWebView => webView;
        public long NetworkBytesReceived => Interlocked.Read(ref networkBytesReceived);

        public void ClearDashboardWorkArea() => dashboardWorkArea = null;

        public void SetLinkedBounds(System.Windows.Rect bounds)
        {
            dashboardWorkArea = null;
            WindowState = WindowState.Normal;
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
        }

        public void FitToWorkArea(System.Windows.Rect workArea)
        {
            dashboardWorkArea = workArea;
            WindowState = WindowState.Normal;
            Left = workArea.Left;
            Top = workArea.Top;
            Width = workArea.Width;
            Height = workArea.Height;
        }

        public void MoveWithDashboard(double deltaX, double deltaY)
        {
            if (!IsLoaded || (Math.Abs(deltaX) < 0.01 && Math.Abs(deltaY) < 0.01))
                return;
            Left += deltaX;
            Top += deltaY;
        }

        public void SynchronizeWithDashboard()
        {
            if (!IsLoaded || dashboardWindow.WindowState == WindowState.Minimized)
                return;

            if (dashboardWorkArea is System.Windows.Rect workArea)
            {
                Left = workArea.Left;
                Top = workArea.Top;
                Width = workArea.Width;
                Height = workArea.Height;
                return;
            }
            FitOwnerMonitor();
        }

        public Int32Rect? InitialPhysicalBounds {get;set;}

        private async void Browser_Loaded(object sender, RoutedEventArgs e)
        {
            if(browserInitializationStarted||browserClosed)return;
            browserInitializationStarted=true;
            string stage="profile";
            if(InitialPhysicalBounds is Int32Rect bounds)
                SetWindowPos(GetNativeHandle(),IntPtr.Zero,bounds.X,bounds.Y,bounds.Width,bounds.Height,0x0010);
            else FitOwnerMonitor();
            try
            {
                if(profileInitializationError != null) throw profileInitializationError;
                stage="WebView2 Runtime";
                WebRuntimeSupport.CheckAvailable();
                stage="WebView initialization";
                await webView.EnsureCoreWebView2Async();
                if(browserClosed)return;
                webView.CoreWebView2.NewWindowRequested += (_,request) =>
                {
                    InteractionDiagnostics.Log("Web new-window request handled in existing view");
                    request.Handled=true;
                    if(Uri.TryCreate(request.Uri,UriKind.Absolute,out var target))
                        webView.CoreWebView2.Navigate(target.AbsoluteUri);
                };
                var core=webView.CoreWebView2;
                InitializeResourcePolicy(core);
                core.FaviconChanged+=async(_,_)=>{
                    try { var page=webView.Source;using var icon=await core.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);var decoded=WebsiteIcon.Decode(icon);if(!browserClosed && webView.Source==page && decoded!=null)siteIcon.Source=decoded; }catch { }
                };
                core.NavigationStarting+=(_,_)=>siteIcon.Source=null;
                core.Settings.IsZoomControlEnabled = false;
                stage="page activity";
                await InitializePageActivity(core);
                // Optional quality/usage integrations cannot invalidate a working page.
                stage="YouTube quality integration";
                try { await core.AddScriptToExecuteOnDocumentCreatedAsync(YouTubeQualityScript); }
                catch(Exception ex) { if(!browserClosed)InteractionDiagnostics.RecordFailure("Web optional "+stage,ex); }
                try { await core.AddScriptToExecuteOnDocumentCreatedAsync(YouTubeDisplayScript);core.SourceChanged+=(_,_)=>RestoreYouTubeDisplay(); }
                catch(Exception ex) { if(!browserClosed)InteractionDiagnostics.RecordFailure("Web optional YouTube display memory",ex); }
                if(browserClosed)return;
                stage="navigation";
                webView.Source = new Uri(initialUrl);
                stage="network usage integration";
                try {
                await core.CallDevToolsProtocolMethodAsync("Network.enable", "{}");
                if(browserClosed)return;
                webView.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.dataReceived")
                    .DevToolsProtocolEventReceived += (_, args) =>
                    {
                        try
                        {
                            using JsonDocument document = JsonDocument.Parse(args.ParameterObjectAsJson);
                            if (document.RootElement.TryGetProperty("encodedDataLength", out JsonElement length))
                                Interlocked.Add(ref networkBytesReceived, (long)Math.Max(0, length.GetDouble()));
                        }
                        catch { }
                    };
                } catch(Exception ex) { if(!browserClosed)InteractionDiagnostics.RecordFailure("Web optional "+stage,ex); }
            }
            catch (Exception ex)
            {
                if(browserClosed)return; // Closing disposes the pending COM operation.
                InteractionDiagnostics.RecordFailure("Web initialization stage="+stage,ex);
                WebRuntimeSupport.ShowInitializationFailure(this,ex,stage);Close();
            }
        }
        private void UpdateButtons() { back.IsEnabled = webView.CanGoBack; forward.IsEnabled = webView.CanGoForward; }
        // YouTube may keep the small-window stream after the host grows. Ask
        // its player for a suitable source on enlargement; never reload or seek.
        internal const string YouTubeQualityScript = """
            (() => {
              if (!/(^|\.)youtube\.com$/.test(location.hostname)) return;
              const heights = {tiny:144,small:240,medium:360,large:480,hd720:720,hd1080:1080,hd1440:1440,hd2160:2160,highres:4320};
              let largest = 0, timer;
              function update() {
                const player = document.getElementById('movie_player');
                const video = player?.querySelector('video');
                if (!video || !video.videoHeight || typeof player.setPlaybackQualityRange !== 'function') return;
                const r = video.getBoundingClientRect();
                const wanted = Math.min(r.height, r.width * video.videoHeight / video.videoWidth) * devicePixelRatio;
                if (wanted <= largest + 2) return;
                const levels = (player.getAvailableQualityLevels?.() || []).filter(q => heights[q]).sort((a,b) => heights[a]-heights[b]);
                if (!levels.length) return;
                const chosen = levels.find(q => heights[q] >= wanted - 2) || levels[levels.length-1];
                largest = wanted;
                if (video.videoHeight >= heights[chosen]) return;
                try {
                  player.setPlaybackQualityRange(chosen, chosen);
                  player.setPlaybackQuality?.(chosen);
                  window.__blakiDeckVideoQuality = { requested:chosen, viewportHeight:wanted };
                } catch (_) { largest = 0; }
              }
              function schedule() { clearTimeout(timer); timer = setTimeout(update, 500); }
              addEventListener('resize', schedule);
              document.addEventListener('fullscreenchange', schedule);
              document.addEventListener('loadeddata', schedule, true);
              document.addEventListener('yt-navigate-finish', () => { largest = 0; schedule(); });
              setTimeout(schedule, 1500);
            })();
            """;
        private async void WebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            // The five-second capture delay begins at navigation completion,
            // independently of favicon fetching or other optional integrations.
            PreviewNavigationCompleted(e);
            if(e.IsSuccess)RestoreYouTubeDisplay();
            if(e.IsSuccess && siteIcon.Source==null && webView.Source is Uri iconPage)
            {
                var fallback=await WebsiteIcon.LoadAsync(iconPage.AbsoluteUri);
                if(!browserClosed && webView.Source==iconPage && siteIcon.Source==null)siteIcon.Source=fallback;
            }
            if(browserClosed)return;
            UpdateButtons();
            ApplyWebZoomForCurrentSite();
        }

        private void ChangeWebZoom(int direction)
        {
            double effectiveFactor = GetEffectiveWebZoomFactor();
            int current = Array.FindIndex(WebZoomSteps,
                step => Math.Abs(step - effectiveFactor) < 0.001);
            int next = Math.Clamp((current < 0 ? 2 : current) + direction, 0, WebZoomSteps.Length - 1);
            if (!webZoomAllSites && webView.Source?.Host is string host && !string.IsNullOrWhiteSpace(host))
                webSiteZoomFactors[host] = WebZoomSteps[next];
            else
                webZoomFactor = WebZoomSteps[next];
            ApplyWebZoomForCurrentSite();
            SaveWebZoomSettings();
        }

        private double GetEffectiveWebZoomFactor()
        {
            string? host = webView.Source?.Host;
            return webZoomAllSites || string.IsNullOrWhiteSpace(host)
                ? webZoomFactor
                : webSiteZoomFactors.GetValueOrDefault(host, webZoomFactor);
        }

        private void ApplyWebZoomForCurrentSite()
        {
            double factor = GetEffectiveWebZoomFactor();
            try { webView.ZoomFactor = webZoomEnabled ? factor : 1.0; }
            catch { }
            zoomValueText.Text = webZoomEnabled ? $"{factor * 100:0}%" : "OFF";
            zoomOutButton.IsEnabled = webZoomEnabled;
            zoomInButton.IsEnabled = webZoomEnabled;
        }

        private void OpenWebZoomSettings(FrameworkElement? anchor=null)
        {
            // IsOpen becomes false before the popup's Closed event is delivered.
            // Retain the instance until Closed and consume a second anchor click
            // in either state, rather than opening a replacement during dismissal.
            if (webSettingsMenu is { } current)
            {
                webSettingsMenu = null;
                current.IsOpen = false;
                return;
            }
            ContextMenu menu = ListAppearanceUi.Create(this);
            var target = anchor ?? zoomSettingsButton;
            webSettingsMenu = menu;
            menu.Closed += (_, _) =>
            {
                if (ReferenceEquals(webSettingsMenu, menu)) webSettingsMenu = null;
                pageActivity?.Invoke();
            };
            MenuItem enabled = new() { Header = "Zoom enabled", StaysOpenOnClick = true };
            MenuItem allSites = new() { Header = "All websites", IsCheckable = true, IsChecked = webZoomAllSites, StaysOpenOnClick = true };
            MenuItem thisSite = new() { Header = "This website only", IsCheckable = true, IsChecked = !webZoomAllSites, StaysOpenOnClick = true };
            void UpdateHeaders()
            {
                var toggle=new CheckBox{IsChecked=webZoomEnabled,Foreground=Brushes.WhiteSmoke,ToolTip="Enable website zoom; open the arrow for its scope."};
                toggle.SetResourceReference(StyleProperty,"DashboardCheckBox");
                toggle.Checked+=(_,_)=>{webZoomEnabled=true;enabled.IsChecked=true;ApplyWebZoomForCurrentSite();SaveWebZoomSettings();};
                toggle.Unchecked+=(_,_)=>{webZoomEnabled=false;enabled.IsChecked=false;ApplyWebZoomForCurrentSite();SaveWebZoomSettings();};
                enabled.Icon=toggle;
            }
            enabled.Click += (_, e) =>
            {
                if(!ReferenceEquals(e.OriginalSource,enabled))return;
                enabled.IsSubmenuOpen=true;
            };
            // A checkable MenuItem executes a command instead of behaving as a
            // submenu header. The checkbox owns enablement; the row/arrow owns scope.
            enabled.MouseEnter+=(_,_)=>enabled.IsSubmenuOpen=true;
            enabled.PreviewMouseLeftButtonUp+=(_,e)=>{
                for(var node=e.OriginalSource as DependencyObject;node!=null&&node!=enabled;node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node))
                    if(node is CheckBox||node is MenuItem child&&!ReferenceEquals(child,enabled))return;
                enabled.IsSubmenuOpen=true;e.Handled=true;
            };
            enabled.PreviewKeyDown+=(_,e)=>{if(e.OriginalSource is MenuItem child&&!ReferenceEquals(child,enabled))return;if(e.Key is Key.Right or Key.Enter){enabled.IsSubmenuOpen=true;e.Handled=true;}};
            allSites.Click += (_, _) =>
            {
                webZoomAllSites = true; allSites.IsChecked = true; thisSite.IsChecked = false;
                UpdateHeaders(); ApplyWebZoomForCurrentSite(); SaveWebZoomSettings();
            };
            thisSite.Click += (_, _) =>
            {
                webZoomAllSites = false; allSites.IsChecked = false; thisSite.IsChecked = true;
                if (webView.Source?.Host is string host && !string.IsNullOrWhiteSpace(host))
                    webSiteZoomFactors[host] = GetEffectiveWebZoomFactor();
                UpdateHeaders(); ApplyWebZoomForCurrentSite(); SaveWebZoomSettings();
            };
            UpdateHeaders();
            telemetryMenuItem=new MenuItem { ToolTip="Show CPU, GPU, RAM and network usage in this upper toolbar.", Header="Show telemetry",IsCheckable=true,IsChecked=telemetryEnabled,StaysOpenOnClick=true };
            telemetryMenuItem.Click+=(_,_)=>{TelemetryChanged?.Invoke(telemetryMenuItem.IsChecked);};
            menu.Items.Add(telemetryMenuItem);menu.Items.Add(new Separator());
            menu.Items.Add(enabled);
            enabled.Items.Add(allSites);
            enabled.Items.Add(thisSite);
            menu.Items.Add(new Separator());
            MenuItem hideBars = new() { Header = "Auto-hide all bars",
                IsCheckable = true, IsChecked = AutoHideBarsEnabled, StaysOpenOnClick = true,
                ToolTip = "Hide both bars after 3 seconds. Tap the page to show them." };
            MenuItem hideTop = new() { Header = "Hide top bar",
                IsCheckable = true, IsChecked = HideTopBarEnabled, StaysOpenOnClick = true,
                ToolTip = "Hide only the Web toolbar after 3 seconds. Tap the page to show it." };
            void RefreshHideChoices()
            {
                hideBars.IsChecked = AutoHideBarsEnabled;
                hideTop.IsChecked = HideTopBarEnabled;
            }
            hideBars.Click += (_, _) => {
                AutoHideBarsEnabled = hideBars.IsChecked;
                RefreshHideChoices();
                AutoHideBarsChanged?.Invoke(AutoHideBarsEnabled);
                pageActivity?.Invoke();
            };
            hideTop.Click += (_, _) => {
                HideTopBarEnabled = hideTop.IsChecked;
                RefreshHideChoices();
                SaveWebZoomSettings();
                pageActivity?.Invoke();
            };
            menu.Items.Add(hideTop);
            menu.Items.Add(hideBars);
            menu.Items.Add(new Separator());
            menu.Items.Add(WebCacheMaintenance.CreateCommand(this, ClearWebCacheAsync));
            void SizeRows(ItemsControl owner)
            {
                foreach(var item in owner.Items.OfType<MenuItem>())
                {
                    item.SetResourceReference(HeightProperty,"UnifiedMenuRowHeight");
                    SizeRows(item);
                }
            }
            SizeRows(menu);
            menu.PlacementTarget = anchor??zoomSettingsButton;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void LoadWebZoomSettings()
        {
            try
            {
                if (!File.Exists(webZoomSettingsFile)) return;
                WebZoomSettings? settings = JsonSerializer.Deserialize<WebZoomSettings>(
                    File.ReadAllText(webZoomSettingsFile));
                if (settings == null) return;
                webZoomEnabled = settings.Enabled;
                HideTopBarEnabled = settings.HideTopBar;
                webZoomAllSites = settings.AllSites;
                if (double.IsFinite(settings.GlobalFactor) && settings.GlobalFactor is >= 0.8 and <= 2.0)
                    webZoomFactor = settings.GlobalFactor;
                webSiteZoomFactors.Clear();
                foreach ((string host, double factor) in settings.SiteFactors)
                    if (!string.IsNullOrWhiteSpace(host) && double.IsFinite(factor) && factor is >= 0.8 and <= 2.0)
                        webSiteZoomFactors[host] = factor;
            }
            catch { }
        }

        private void SaveWebZoomSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(webZoomSettingsFile)!);
                File.WriteAllText(webZoomSettingsFile, JsonSerializer.Serialize(new WebZoomSettings
                {
                    Enabled = webZoomEnabled,
                    HideTopBar = HideTopBarEnabled,
                    AllSites = webZoomAllSites,
                    GlobalFactor = webZoomFactor,
                    SiteFactors = new Dictionary<string, double>(webSiteZoomFactors, StringComparer.OrdinalIgnoreCase)
                }));
            }
            catch { }
        }

        private sealed class WebZoomSettings
        {
            public bool HideTopBar { get; set; }
            public bool Enabled { get; set; } = true;
            public bool AllSites { get; set; } = true;
            public double GlobalFactor { get; set; } = 1.0;
            public Dictionary<string, double> SiteFactors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }
        private enum BrowserIcon { Back, Forward, Refresh, External }

        private static Button IconButton(BrowserIcon icon, string tooltip)
        {
            bool navigation=icon is BrowserIcon.Back or BrowserIcon.Forward;
            Button button = BaseButton(tooltip, navigation?84:62);
            button.Content = navigation ? new Viewbox {Width=36,Height=36,Child=CreateIcon(icon)} : CreateIcon(icon);
            return button;
        }

        private static Button TextButton(string text, string tooltip)
        {
            Button button = BaseButton(tooltip, 160);
            button.Content = ToolbarText(text, accent: false);
            return button;
        }

        private static OpticallyCenteredText ToolbarText(string text, bool accent)
        {
            var label = new OpticallyCenteredText {
            Text = text,
            TextBrush = accent
                ? Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.White
                : Brushes.White,
            TextFontFamily = Application.Current.TryFindResource("TekoFont") as FontFamily ?? new FontFamily("Segoe UI Semibold"),
            TextFontSize = 23,
            TextFontWeight = FontWeights.ExtraBold,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
            };
            label.SetResourceReference(OpticallyCenteredText.TextBrushProperty, accent ? "AccentBrush" : "PanelTextBrush");
            return label;
        }

        private static Button BaseButton(string tooltip, double width)
        {
            Button button = new()
            {
                ToolTip = tooltip, Height = 56,
                Width = width, Margin = new Thickness(4,0,4,0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(48,48,48)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(75,255,255,255))
            };
            if (Application.Current.TryFindResource("DashboardDialogButton") is Style style)
                button.Style = style;
            button.SetResourceReference(Control.BackgroundProperty, "ModuleCardSurfaceBrush");
            button.SetResourceReference(Control.ForegroundProperty, "PanelTextBrush");
            button.SetResourceReference(Control.BorderBrushProperty, "PanelDividerBrush");
            return button;
        }

        private static Grid CreateIcon(BrowserIcon icon)
        {
            Grid surface = new() { Width = 28, Height = 28 };
            void Add(string data)
            {
                var path = new ShapePath
                {
                    Data = Geometry.Parse(data), Stroke = Brushes.White, StrokeThickness = 2.5,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round
                };
                path.SetResourceReference(ShapePath.StrokeProperty, "PanelTextBrush");surface.Children.Add(path);
            }

            switch (icon)
            {
                case BrowserIcon.Back:
                    Add("M16,5 L6,14 L16,23 M6,14 H22");
                    break;
                case BrowserIcon.Forward:
                    Add("M12,5 L22,14 L12,23 M6,14 H22");
                    break;
                case BrowserIcon.Refresh:
                    Add("M22,9 A9,9 0 1 0 22,19 M22,9 V3 M22,9 H16");
                    break;
                case BrowserIcon.External:
                    Add("M12,5 H5 V23 H23 V16 M14,5 H23 V14 M23,5 L11,17");
                    break;
            }
            return surface;
        }

        private void FitOwnerMonitor()
        {
            if (dashboardWorkArea is System.Windows.Rect workArea)
            {
                Left = workArea.Left;
                Top = workArea.Top;
                Width = workArea.Width;
                Height = workArea.Height;
                return;
            }

            IntPtr owner = new WindowInteropHelper(dashboardWindow).Handle;
            IntPtr monitor = MonitorFromWindow(owner, 2);
            MonitorInfo info = new() { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info)) return;
            dashboardWorkArea = null;
            SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero,
                info.Work.Left, info.Work.Top,
                info.Work.Right - info.Work.Left,
                info.Work.Bottom - info.Work.Top, 0x0010);
        }

        [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
        [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet=CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll", SetLastError=true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Auto)] private struct MonitorInfo { public int Size; public Rect Monitor; public Rect Work; public uint Flags; }
    }
}

