using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Ekrano_valdymas;

internal sealed class ProcessGpuUsageReader : IDisposable
{
    private const uint MoreData = 0x800007D2;
    private const uint FormatDouble = 0x00000200;
    private const uint NoCap100 = 0x00008000;
    private const int MaximumBufferBytes = 16 * 1024 * 1024;
    private static readonly int ItemBytes = Marshal.SizeOf<FormattedCounterItem>();
    private readonly object sync = new();
    private readonly List<IntPtr> counters = new();
    private SafeQuery? query;
    private IntPtr buffer;
    private uint bufferBytes;
    private string processMarker = string.Empty;
    private DateTime refreshUtc;

    public double? Read(IEnumerable<int> processIds)
    {
        int[] ids = processIds.Where(id => id > 0).Distinct().Order().ToArray();
        string marker = string.Join('|', ids);
        lock (sync)
        {
            if (marker != processMarker) { ResetCore(); processMarker = marker; }
            if (ids.Length == 0 || DateTime.UtcNow < refreshUtc) return null;
            try
            {
                if (query == null)
                {
                    uint opened = PdhOpenQueryW(null, UIntPtr.Zero, out SafeQuery created);
                    query = created;
                    if (opened != 0 || created.IsInvalid) return Unavailable();
                    foreach (int id in ids)
                    {
                        // English paths are independent of Windows' display
                        // language. PDH expands this PID's live engine instances;
                        // no managed copy of every GPU counter is constructed.
                        uint added = PdhAddEnglishCounterW(created,
                            $@"\GPU Engine(pid_{id}_*)\Utilization Percentage",
                            UIntPtr.Zero, out IntPtr counter);
                        if (added != 0) return Unavailable();
                        counters.Add(counter);
                    }
                    // Rate counters need two samples. Do not invent a zero on
                    // their first sample or retain a previous family's reading.
                    return PdhCollectQueryData(created) == 0 ? null : Unavailable();
                }
                if (PdhCollectQueryData(query) != 0) return Unavailable();
                double total = 0;
                bool valid = false, instances = false;
                foreach (IntPtr counter in counters)
                {
                    uint size = bufferBytes, count;
                    uint status = PdhGetFormattedCounterArrayW(counter, FormatDouble | NoCap100,
                        ref size, out count, buffer);
                    if (status == MoreData)
                    {
                        // A too-small nonzero call need not return a usable size.
                        // Re-query with zero as required by the native contract.
                        size = 0;
                        status = PdhGetFormattedCounterArrayW(counter, FormatDouble | NoCap100,
                            ref size, out count, IntPtr.Zero);
                        if (status != MoreData || size == 0 || size > MaximumBufferBytes) continue;
                        if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                        buffer = IntPtr.Zero;bufferBytes = 0;
                        buffer = Marshal.AllocHGlobal(checked((int)size));
                        bufferBytes = size;
                        status = PdhGetFormattedCounterArrayW(counter, FormatDouble | NoCap100,
                            ref size, out count, buffer);
                    }
                    if (status != 0 || count == 0) continue;
                    if ((ulong)count * (uint)ItemBytes > bufferBytes) return Unavailable();
                    instances = true;
                    for (int i = 0; i < count; i++)
                    {
                        var item = Marshal.PtrToStructure<FormattedCounterItem>(IntPtr.Add(buffer, i * ItemBytes));
                        // API success and individual data validity are separate.
                        // Accept valid/new data, including a genuine zero.
                        if (item.Value.Status > 1 || !double.IsFinite(item.Value.Double)) continue;
                        total += Math.Max(0, item.Value.Double);
                        valid = true;
                    }
                }
                if (!instances) return Unavailable();
                return valid ? Math.Clamp(total, 0, 100) : null;
            }
            catch { return Unavailable(); }
        }
    }

    private double? Unavailable()
    {
        CloseQuery();refreshUtc = DateTime.UtcNow.AddSeconds(10);return null;
    }
    private void CloseQuery()
    {
        query?.Dispose();query = null;counters.Clear();
        if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
        buffer = IntPtr.Zero;bufferBytes = 0;
    }
    private void ResetCore() { CloseQuery();processMarker = string.Empty;refreshUtc = default; }
    internal void Reset() { lock (sync) ResetCore(); }
    public void Dispose() => Reset();

    // Native pdh.h definitions: the value's union is eight-byte aligned.
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct FormattedCounterValue
    {
        [FieldOffset(0)] internal uint Status;
        [FieldOffset(8)] internal double Double;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct FormattedCounterItem
    {
        internal IntPtr Name;
        internal FormattedCounterValue Value;
    }
    private sealed class SafeQuery : SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeQuery() : base(true) { }
        protected override bool ReleaseHandle() => PdhCloseQuery(handle) == 0;
    }
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhOpenQueryW(string? source, UIntPtr userData, out SafeQuery query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhAddEnglishCounterW(SafeQuery query, string path, UIntPtr userData, out IntPtr counter);
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCollectQueryData(SafeQuery query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferBytes, out uint itemCount, IntPtr buffer);
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCloseQuery(IntPtr query);
}
