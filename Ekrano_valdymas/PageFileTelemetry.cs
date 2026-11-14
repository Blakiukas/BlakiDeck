using System.Management;

namespace Ekrano_valdymas;

internal static class PageFileTelemetry
{
    internal const string SensorId = "/windows/pagefile/used";
    private static HardwareReading reading = new(SensorId, "Windows page files", "Page file used", "MB", null, DateTime.MinValue, "Memory");
    private static long nextRead;
    private static int busy;
    internal static HardwareReading Current
    {
        get
        {
            if (DiagnosticState.Root == null && Environment.TickCount64 >= Interlocked.Read(ref nextRead) && Interlocked.CompareExchange(ref busy, 1, 0) == 0)
                _ = Task.Run(() =>
                {
                    try { Volatile.Write(ref reading, Read()); }
                    finally { Interlocked.Exchange(ref nextRead, Environment.TickCount64 + 10000); Volatile.Write(ref busy, 0); }
                });
            return Volatile.Read(ref reading);
        }
    }

    // WMI reports Windows MB (2^20 bytes). Keep the shared transport in decimal
    // MB; HardwareReading converts it to the user's selected display unit.
    internal static HardwareReading Read()
    {
        double? used = null;
        try
        {
            using var query = new ManagementObjectSearcher("SELECT CurrentUsage FROM Win32_PageFileUsage");
            query.Options.Timeout = TimeSpan.FromSeconds(5);
            using var files = query.Get();
            double total = 0;
            foreach (ManagementObject file in files)
                using (file)
                {
                    if (file["CurrentUsage"] is not uint value) throw new InvalidOperationException("Missing page-file usage");
                    total += value;
                }
            // Successful empty enumeration means no active page files, hence 0.
            used = total * 1048576d / 1_000_000d;
        }
        catch { /* Access/query failure is unavailable, never a fabricated zero. */ }
        return new(SensorId, "Windows page files", "Page file used", "MB", used, DateTime.UtcNow, "Memory");
    }
}
