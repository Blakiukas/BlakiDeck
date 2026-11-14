using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ekrano_valdymas;

public partial class ObsTopBarControl : UserControl
{
    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode), typeof(string), typeof(ObsTopBarControl),
        new PropertyMetadata("record", OnModeChanged));
    public static readonly DependencyProperty RecordingProperty = DependencyProperty.Register(
        nameof(Recording), typeof(bool), typeof(ObsTopBarControl),
        new PropertyMetadata(false, OnRecordingChanged));
    public static readonly DependencyProperty StatusActiveProperty = DependencyProperty.Register(
        nameof(StatusActive), typeof(bool), typeof(ObsTopBarControl));
    public static readonly DependencyProperty ElapsedTextProperty = DependencyProperty.Register(
        nameof(ElapsedText), typeof(string), typeof(ObsTopBarControl),
        new PropertyMetadata("00:00:00"));
    public static readonly DependencyProperty RecordBrushProperty = DependencyProperty.Register(
        nameof(RecordBrush), typeof(Brush), typeof(ObsTopBarControl),
        new PropertyMetadata(new SolidColorBrush(Color.FromRgb(105, 110, 112))));

    public string Mode { get => (string)GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
    public bool Recording { get => (bool)GetValue(RecordingProperty); set => SetValue(RecordingProperty, value); }
    public bool StatusActive { get => (bool)GetValue(StatusActiveProperty); set => SetValue(StatusActiveProperty, value); }
    public string ElapsedText { get => (string)GetValue(ElapsedTextProperty); set => SetValue(ElapsedTextProperty, value); }
    public Brush RecordBrush { get => (Brush)GetValue(RecordBrushProperty); private set => SetValue(RecordBrushProperty, value); }

    public ObsTopBarControl()
    {
        InitializeComponent();
        ApplyMode();
        ApplyRecording();
    }

    private static void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ObsTopBarControl)d).ApplyMode();

    private static void OnRecordingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ObsTopBarControl)d).ApplyRecording();

    private void ApplyMode()
    {
        if (recordContent == null || bufferContent == null) return;
        bool record = !string.Equals(Mode, "buffer", System.StringComparison.OrdinalIgnoreCase);
        headerText.Text = record ? "OBS" : "OBS BUFF.";
        recordContent.Visibility = record ? Visibility.Visible : Visibility.Collapsed;
        bufferContent.Visibility = record ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyRecording() => RecordBrush = Recording
        ? new SolidColorBrush(Color.FromRgb(255, 74, 80))
        : new SolidColorBrush(Color.FromRgb(105, 110, 112));
}
