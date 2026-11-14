using System.Text.RegularExpressions;
using Microsoft.Win32;
namespace Ekrano_valdymas;

internal static class HardwareModelName
{
    private static readonly Lazy<string> cpu=new(()=>
    {
        try{using var key=Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");return key?.GetValue("ProcessorNameString")?.ToString()?.Trim()??"CPU unavailable";}
        catch{return "CPU unavailable";}
    });
    internal static string Full(bool gpu)=>gpu?GpuTelemetrySelection.Current?.Name??"GPU unavailable":cpu.Value;
    internal static string Short(bool gpu)
    {
        string name=Full(gpu).Replace("(R)","",StringComparison.OrdinalIgnoreCase).Replace("(TM)","",StringComparison.OrdinalIgnoreCase);
        name=Regex.Replace(name,@"\b(?:AMD|NVIDIA|Intel|GeForce|Processor)\b|\b\d+-Core\b|\s+@.*$","",RegexOptions.IgnoreCase);
        return Regex.Replace(name,@"\s+"," ").Trim();
    }
}
