using System.Runtime.InteropServices;

namespace Ekrano_valdymas;

// PhysicalDisk counts device reads, not cached application reads. English PDH
// paths also work on localized Windows installations. All values are bytes/s.
internal sealed class DiskReadRateReader : IDisposable
{
    private IntPtr query, counter;
    private readonly bool write;
    internal DiskReadRateReader():this(false) { }
    internal DiskReadRateReader(bool write) => this.write = write;
    private bool primed;
    private DateTime retry;
    internal Dictionary<string, double?> Read()
    {
        var result = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
        if (DateTime.UtcNow < retry) return result;
        try
        {
            if (query == IntPtr.Zero && (PdhOpenQueryW(null, UIntPtr.Zero, out query) != 0 ||
                PdhAddEnglishCounterW(query, write ? @"\PhysicalDisk(*)\Disk Write Bytes/sec" : @"\PhysicalDisk(*)\Disk Read Bytes/sec", UIntPtr.Zero, out counter) != 0))
                throw new InvalidOperationException("Disk counters unavailable");
            if (PdhCollectQueryData(query) != 0) throw new InvalidOperationException("Disk sample unavailable");
            if (!primed) { primed = true; return result; }
            uint size = 0;
            uint status = PdhGetFormattedCounterArrayW(counter, 0x200, ref size, out uint count, IntPtr.Zero);
            if (status != 0x800007D2 || size == 0 || size > 1024 * 1024)
                throw new InvalidOperationException("Invalid disk counter buffer");
            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArrayW(counter, 0x200, ref size, out count, buffer) != 0)
                    throw new InvalidOperationException("Disk instances changed during sample");
                int stride = Marshal.SizeOf<Item>();
                for (int i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<Item>(IntPtr.Add(buffer, i * stride));
                    if (Marshal.PtrToStringUni(item.Name) is { } name)
                        result[name] = item.Value.Status <= 1 && double.IsFinite(item.Value.Rate) && item.Value.Rate >= 0
                            ? item.Value.Rate : null;
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        catch { Dispose(); retry = DateTime.UtcNow.AddSeconds(10); }
        return result;
    }
    public void Dispose() { if (query != IntPtr.Zero) PdhCloseQuery(query); query = counter = IntPtr.Zero; primed = false; }
    [StructLayout(LayoutKind.Explicit, Size = 16)] private struct Value
    { [FieldOffset(0)] internal uint Status; [FieldOffset(8)] internal double Rate; }
    [StructLayout(LayoutKind.Sequential)] private struct Item
    { internal IntPtr Name; internal Value Value; }
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQueryW(string? source, UIntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, UIntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint size, out uint count, IntPtr buffer);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
}
