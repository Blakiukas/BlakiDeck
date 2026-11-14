using System.Windows;

namespace Ekrano_valdymas;

public partial class SystemMonitorWidget
{
    internal string? SensorAccessProblem => advancedSensors?.Problem;
    internal string SensorSupportSummary => advancedSensors?.SupportSummary ??
        (HardwareAccessStatus.Check().Problem ?? "The sensor driver is accessible. Advanced readings start when a sensor that needs them is displayed.");
    internal void OpenSensorSupport()
    {
        var service=Shared??this;
        SensorSupportWindow.Create(Window.GetWindow(this),()=>service.SensorSupportSummary+service.NvidiaSupportNote,()=>
        {
            service.advancedSensors?.Dispose();service.advancedSensors=null;
            service.nvidia?.Dispose();service.nvidia=null;
            service.UpdateSensorActivity();
        }).ShowDialog();
    }
    private string NvidiaSupportNote => GpuTelemetrySelection.Current?.Vendor=="GpuNvidia"&&nvidia?.Problem is {} problem?"\n\n"+problem:"";
    private void PcInformation_Click(object sender,RoutedEventArgs e)
    {
        e.Handled=true;
        PcSpecificationsWindow.Open(Window.GetWindow(this));
    }
}
