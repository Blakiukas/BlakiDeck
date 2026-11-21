using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Ekrano_valdymas;

internal sealed class MediaWidget : UserControl
{
    internal sealed class Preferences
    {
        public string? File { get; set; }
        public bool Loop { get; set; } = true;
        public bool Sound { get; set; }
        public bool Fill { get; set; }
        public double BackgroundTransparency { get; set; }
    }
    private readonly string settingsPath;
    internal Preferences Settings { get; private set; } = new();
    private readonly Border backdrop = new() { CornerRadius = new CornerRadius(18) };
    private readonly Image picture = new();
    private readonly MediaElement video = new() { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual };
    private readonly TextBlock message = new() { Text = "Choose an image or video", Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24), FontSize = 20 };
    private readonly Button options = new() { Width = 42, Height = 40, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12), Content = "⚙", ToolTip = "Media settings", Visibility = Visibility.Hidden, Tag = "NoWindowDrag" };
    private readonly DispatcherTimer hide = new() { Interval = TimeSpan.FromMilliseconds(InteractionFeedback.ControlsIdleMilliseconds) };
    private bool isVideo, ended, dialogOpen;
    internal MediaWidget(int tab)
    {
        AllowDrop = true;
        DragEnter += MediaDragOver;
        DragOver += MediaDragOver;
        Drop += MediaDrop;
        options.SetResourceReference(StyleProperty, "ModuleOptionsButtonStyle");
        var gear = new OpticallyCenteredText { Width = 20, Height = 20, Text = "\uE713", TextFontFamily = new FontFamily("Segoe MDL2 Assets"), TextFontSize = 20, IsHitTestVisible = false };
        gear.SetResourceReference(OpticallyCenteredText.TextBrushProperty, "PanelTextBrush"); options.Content = gear;
        settingsPath = Path.Combine(DiagnosticState.LocalApplicationData, "Ekrano_valdymas", $"dashboard-tab-{tab + 1}-media.json");
        try { if (File.Exists(settingsPath)) Settings = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(settingsPath)) ?? new(); } catch { }
        var grid = new Grid { Background = Brushes.Transparent, ClipToBounds = true };
        backdrop.SetResourceReference(Border.BackgroundProperty, "PanelMetalBrush");
        grid.Children.Add(backdrop); grid.Children.Add(picture); grid.Children.Add(video); grid.Children.Add(message); grid.Children.Add(options); Content = grid;
        SizeChanged += (_, _) => grid.Clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), 18, 18);
        MouseMove += (_, _) => Reveal();
        AddHandler(TouchDownEvent, new EventHandler<System.Windows.Input.TouchEventArgs>((_, _) => Reveal()), true);
        hide.Tick += (_, _) => { if (dialogOpen || options.IsMouseOver || options.IsMouseCaptured || options.AreAnyTouchesCaptured) return; hide.Stop(); options.Visibility = Visibility.Hidden; };
        options.Click += (_, e) => { e.Handled = true; ShowSettings(); };
        video.MediaEnded += (_, _) => { ended = true; if (Settings.Loop && IsVisible) { ended = false; video.Position = TimeSpan.Zero; video.Play(); } };
        video.MediaFailed += (_, e) => { message.Text = "Cannot play this video. Try MP4 (H.264)."; message.Visibility = Visibility.Visible; };
        IsVisibleChanged += (_, _) => UpdatePlayback(); Loaded += (_, _) => UpdatePlayback();
        Unloaded += (_, _) => { video.Pause(); hide.Stop(); options.Visibility = Visibility.Hidden; };
        LoadMedia();
    }
    private void Reveal() { options.Visibility = Visibility.Visible; hide.Stop(); hide.Start(); }
    internal static string? DroppedFile(IDataObject data)
    {
        try
        {
            if (!data.GetDataPresent(DataFormats.FileDrop) || data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } files) return null;
            string path = files[0];
            return File.Exists(path) && new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".mp4", ".m4v", ".wmv", ".avi", ".mov" }.Contains(Path.GetExtension(path).ToLowerInvariant()) ? Path.GetFullPath(path) : null;
        }
        catch { return null; }
    }
    private void MediaDragOver(object sender, DragEventArgs e)
    {
        e.Effects = (e.AllowedEffects & DragDropEffects.Copy) != 0 && DroppedFile(e.Data) != null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private void MediaDrop(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None; e.Handled = true;
        if ((e.AllowedEffects & DragDropEffects.Copy) == 0 || DroppedFile(e.Data) is not { } path) return;
        Settings.File = path; Save(); LoadMedia(); Reveal(); e.Effects = DragDropEffects.Copy;
    }
    private void Save() => AtomicJson.Write(settingsPath, Settings);
    private void UpdatePlayback() { if (!isVideo) return; if (IsVisible && IsLoaded && !ended) video.Play(); else video.Pause(); }
    internal void LoadMedia()
    {
        video.Close(); video.Source = null; picture.Source = null; ended = false;
        backdrop.Opacity = 1 - Math.Clamp(Settings.BackgroundTransparency, 0, 100) / 100;
        picture.Stretch = video.Stretch = Settings.Fill ? Stretch.UniformToFill : Stretch.Uniform;
        video.IsMuted = !Settings.Sound;
        isVideo = !string.IsNullOrWhiteSpace(Settings.File) && new[] { ".mp4", ".m4v", ".wmv", ".avi", ".mov" }.Contains(Path.GetExtension(Settings.File).ToLowerInvariant());
        picture.Visibility = isVideo ? Visibility.Collapsed : Visibility.Visible; video.Visibility = isVideo ? Visibility.Visible : Visibility.Collapsed;
        message.Text = "Choose or drop an image or video"; message.Visibility = Visibility.Visible;
        if (string.IsNullOrWhiteSpace(Settings.File)) return;
        try
        {
            if (!File.Exists(Settings.File)) throw new FileNotFoundException();
            if (isVideo) video.Source = new Uri(Settings.File, UriKind.Absolute);
            else { var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(Settings.File, UriKind.Absolute); bitmap.EndInit(); bitmap.Freeze(); picture.Source = bitmap; }
            message.Visibility = Visibility.Collapsed; UpdatePlayback();
        }
        catch { message.Text = "Media file unavailable. Choose another file."; }
    }
    internal SettingsWindow CreateSettings()
    {
        var owner = Window.GetWindow(this); var dialog = AppearanceEditorUi.Window("Media", owner, 610);
        var panel = new StackPanel { Margin = new Thickness(20) };
        var filename = new TextBlock { Text = Settings.File ?? "No file selected", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) }; panel.Children.Add(filename);
        var files = new StackPanel { Orientation = Orientation.Horizontal }; panel.Children.Add(files);
        files.Children.Add(MacroUi.Button("Choose file…", (_, _) => { var picker = new OpenFileDialog { Filter = "Images and videos|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.mp4;*.m4v;*.wmv;*.avi;*.mov" }; if (picker.ShowDialog(dialog) == true) { Settings.File = picker.FileName; filename.Text = picker.FileName; Save(); LoadMedia(); } }));
        files.Children.Add(MacroUi.Button("Remove", (_, _) => { Settings.File = null; filename.Text = "No file selected"; Save(); LoadMedia(); }));
        void Check(string label, bool value, Action<bool> apply) { var box = new CheckBox { Content = label, Foreground = Brushes.White, IsChecked = value, Margin = new Thickness(0, 10, 0, 4) }; box.Click += (_, _) => { apply(box.IsChecked == true); Save(); LoadMedia(); }; panel.Children.Add(box); }
        Check("Loop video", Settings.Loop, value => Settings.Loop = value); Check("Video sound", Settings.Sound, value => Settings.Sound = value); Check("Fill module (crop edges)", Settings.Fill, value => Settings.Fill = value);
        if (owner is MainWindow main) main.AddMediaAppearanceControls(panel);
        AddSlider(panel, "This module background transparency", 0, 100, Settings.BackgroundTransparency, value => { Settings.BackgroundTransparency = value; backdrop.Opacity = 1 - value / 100; Save(); });
        panel.Children.Add(MacroUi.Button("Close", (_, _) => dialog.Close())); dialog.Content = panel; AppearanceEditorUi.Frame(dialog, 500, 420); return dialog;
    }
    internal static void AddSlider(Panel panel, string label, double min, double max, double value, Action<double> changed)
    {
        var text = new TextBlock { Text = $"{label}: {value:0}%", Margin = new Thickness(0, 16, 0, 2), TextWrapping = TextWrapping.Wrap }; panel.Children.Add(text);
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, Height = 36, IsMoveToPointEnabled = true }; slider.SetResourceReference(StyleProperty, "DashboardSlider");
        slider.ValueChanged += (_, e) => { text.Text = $"{label}: {e.NewValue:0}%"; changed(e.NewValue); }; panel.Children.Add(slider);
    }
    private void ShowSettings() { dialogOpen = true; hide.Stop(); try { CreateSettings().ShowDialog(); } finally { dialogOpen = false; Reveal(); } }
}
