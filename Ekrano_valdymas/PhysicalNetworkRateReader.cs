using System.Runtime.InteropServices;

namespace Ekrano_valdymas;

internal readonly record struct NetworkAdapterCounter(ulong Luid,ulong Received,ulong Sent,bool Up,bool Hardware,bool Filter,bool EndPoint=false);
internal readonly record struct NetworkRateReading(double? Download,double? Upload,double? IntervalStart);

// A UI-thread reader. Rates are bytes/second over the actual sample interval;
// the first sample or a discontinuity supplies a baseline, never a zero spike.
internal sealed class PhysicalNetworkRateReader
{
    private Dictionary<ulong,NetworkAdapterCounter>? previous;
    private double previousTime;

    internal NetworkRateReading Read(double now)=>Sample(now,Capture());

    internal NetworkRateReading Sample(double now,NetworkAdapterCounter[]? rows)
    {
        if(rows==null||!double.IsFinite(now))
        {
            previous=null;
            return default;
        }
        var current=new Dictionary<ulong,NetworkAdapterCounter>();
        foreach(var row in rows)
        {
            if(!row.Up||!row.Hardware||row.Filter||row.EndPoint)continue;
            if(current.TryGetValue(row.Luid,out var seen))
            {
                // Identical aliases count once. Conflicting observations have
                // no unambiguous baseline and cannot depend on row ordering.
                if(seen.Received!=row.Received||seen.Sent!=row.Sent)
                {
                    previous=null;
                    return default;
                }
            }
            else current.Add(row.Luid,row);
        }
        var baseline=previous;double start=previousTime;
        previous=current;previousTime=now;
        double elapsed=now-start;
        if(baseline==null||!double.IsFinite(elapsed)||elapsed<=0||current.Count!=baseline.Count)return default;
        UInt128 received=0,sent=0;
        foreach(var (luid,row) in current)
        {
            if(!baseline.TryGetValue(luid,out var old)||row.Received<old.Received||row.Sent<old.Sent)return default;
            // Subtract each unsigned counter before converting to floating
            // point. UInt128 also preserves sums exceeding one ulong.
            received+=row.Received-old.Received;
            sent+=row.Sent-old.Sent;
        }
        double download=(double)received/elapsed,upload=(double)sent/elapsed;
        return double.IsFinite(download)&&double.IsFinite(upload)?new(download,upload,start):default;
    }

    // GetIfTable2 owns the buffer until FreeMibTable. The SDK's MIB_IF_ROW2
    // uses 8-byte alignment: two 257-WCHAR names and fixed address arrays precede
    // flags at1152, OperStatus at1156 and octet counters at1208/1280; size1352.
    // A uint table count is padded to the first row at8. Keep the native stride,
    // not a reduced managed struct's size. No names, addresses or IPs are read
    // into managed fields or exported by this reader.
    // https://learn.microsoft.com/en-us/windows/win32/api/netioapi/ns-netioapi-mib_if_row2
    // https://learn.microsoft.com/en-us/windows/win32/api/netioapi/ns-netioapi-mib_if_table2
    // https://learn.microsoft.com/en-us/windows/win32/api/netioapi/nf-netioapi-getiftable2
    [StructLayout(LayoutKind.Explicit,Size=1352)]
    private struct MibIfRow2
    {
        [FieldOffset(0)] internal ulong InterfaceLuid;
        [FieldOffset(1152)] internal byte InterfaceAndOperStatusFlags;
        [FieldOffset(1156)] internal uint OperStatus;
        [FieldOffset(1208)] internal ulong InOctets;
        [FieldOffset(1280)] internal ulong OutOctets;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibIfTable2
    {
        internal uint NumEntries;
        internal MibIfRow2 FirstRow;
    }

    [DllImport("iphlpapi.dll",ExactSpelling=true)]
    private static extern uint GetIfTable2(out IntPtr table);
    [DllImport("iphlpapi.dll",ExactSpelling=true)]
    private static extern void FreeMibTable(IntPtr table);

    internal static NetworkAdapterCounter[]? Capture()
    {
        IntPtr table=IntPtr.Zero;
        try
        {
            int stride=Marshal.SizeOf<MibIfRow2>();
            int first=Marshal.OffsetOf<MibIfTable2>(nameof(MibIfTable2.FirstRow)).ToInt32();
            if(stride!=1352||first!=8||GetIfTable2(out table)!=0||table==IntPtr.Zero)return null;
            uint count=unchecked((uint)Marshal.ReadInt32(table));
            if(count>(int.MaxValue-first)/stride)return null;
            var rows=new NetworkAdapterCounter[(int)count];
            for(int i=0;i<rows.Length;i++)
            {
                var row=Marshal.PtrToStructure<MibIfRow2>(IntPtr.Add(table,first+i*stride));
                byte flags=row.InterfaceAndOperStatusFlags;
                rows[i]=new(row.InterfaceLuid,row.InOctets,row.OutOctets,row.OperStatus==1,(flags&1)!=0,(flags&2)!=0,(flags&128)!=0);
            }
            return rows;
        }
        catch(Exception error) when(error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return null;
        }
        finally
        {
            if(table!=IntPtr.Zero)FreeMibTable(table);
        }
    }
}
