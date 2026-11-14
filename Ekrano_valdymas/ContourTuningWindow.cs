using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ekrano_valdymas;

internal static class ContourTuningWindow
{
    internal static Window Open(PulseContourControl control)
    {
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = "Changes apply live to this visualizer.\nRight-click its shape to reopen these controls.", Margin = new Thickness(0, 0, 0, 14) });
        (string Name, string Label, double Min, double Max)[] entries =
        [
            ("LowGain", "Bass strength", 0, 4), ("MidGain", "Mid strength", 0, 4), ("HighGain", "Fine detail strength", 0, 4),
            ("MasterDisplacement", "Overall deformation", 0, 2.5),
            ("LowAttack", "Bass attack (ms)", 1, 150), ("LowRelease", "Bass release (ms)", 20, 800),
            ("MidAttack", "Mid attack (ms)", 1, 150), ("MidRelease", "Mid release (ms)", 20, 800),
            ("HighAttack", "Detail attack (ms)", 1, 100), ("HighRelease", "Detail release (ms)", 10, 500),
            ("LowSmoothing", "Bass contour smoothing", 10, 18), ("MidSmoothing", "Mid contour smoothing", 4, 8),
            ("HighSmoothing", "Detail smoothing", 1, 3), ("NoiseGate", "Silence threshold", 0, .01),
            ("NormalizationSeconds", "Level adaptation (seconds)", .25, 10), ("BaseScale", "Body size", .16, .36),
            ("MarkerLength", "Edge mark length", 0, 9), ("MarkerDensity", "Edge mark count", 0, 192)
        ];
        foreach (var entry in entries)
        {
            PropertyInfo property = typeof(ContourTuning).GetProperty(entry.Name)!;
            var label = new TextBlock();
            var slider = new Slider { Minimum = entry.Min, Maximum = entry.Max, Value = (double)property.GetValue(control.Tuning)!, Margin = new Thickness(0, 4, 0, 12) };
            void Update() { property.SetValue(control.Tuning, slider.Value); label.Text = $"{entry.Label}: {slider.Value:0.####}"; control.InvalidateVisual(); }
            slider.ValueChanged += (_, _) => Update(); Update();
            System.Windows.Automation.AutomationProperties.SetName(slider, entry.Label);
            panel.Children.Add(label); panel.Children.Add(slider);
        }
        var window = new SettingsWindow
        {
            Title = "Pulse contour — tuning", Width = 420, Height = Math.Min(740, SystemParameters.WorkArea.Height - 60),
            Owner = Window.GetWindow(control), ShowInTaskbar = false, WindowStyle = WindowStyle.ToolWindow,
            Background = new SolidColorBrush(Color.FromRgb(34, 34, 38)), Foreground = Brushes.White,
            Content = MacroUi.Scroll(panel)
        };
        window.Show();
        return window;
    }
}
