using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Ekrano_valdymas;
public partial class MainWindow
{
    private readonly Dictionary<int, MediaWidget> mediaWidgets = new();
    private MediaWidget GetMediaWidget()
    {
        if (!mediaWidgets.TryGetValue(activeDashboardLayoutTab, out var widget)) mediaWidgets[activeDashboardLayoutTab] = widget = new MediaWidget(activeDashboardLayoutTab);
        return widget;
    }
    private void InitializeMediaFeatures()
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromHours(24) };
        timer.Tick += async (_, _) => await ApplicationUpdates.CheckAsync(this, false);
        Loaded += async (_, _) => { timer.Start(); await ApplicationUpdates.CheckAsync(this, false); };
        Closed += (_, _) => timer.Stop();
    }
    internal void AddMediaAppearanceControls(Panel panel)
    {
        MediaWidget.AddSlider(panel, "Background transparency (shared)", 0, 100, backgroundTransparency, value =>
        {
            backgroundTransparency = value; backgroundTransparencySlider.Value = value; backgroundTransparencyValue.Text = $"{value:0}%"; ApplyBaseColor(baseColor); SaveWindowSettings();
        });
        MediaWidget.AddSlider(panel, "UI opacity (shared)", 60, 100, uiOpacity * 100, value =>
        {
            uiOpacity = value / 100; opacitySlider.Value = value; txtOpacityValue.Text = $"{value:0}%"; ApplyUiOpacityImmediate(); SaveWindowSettings();
        });
    }
}
