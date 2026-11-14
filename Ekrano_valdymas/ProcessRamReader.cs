using System.Runtime.InteropServices;

namespace Ekrano_valdymas;

// Windows Process counters include elevated helpers without opening their heaps.
// Sample PID and private resident bytes in the same query; instance suffixes
// are not stable process identities. Never substitute private committed bytes.
internal sealed class ProcessRamReader : IDisposable
{
    private IntPtr query, ids, memory;
    private readonly object sync=new();
    private DateTime retry;
    internal Dictionary<int,long> Read()
    {lock(sync)return ReadCore();}
    private Dictionary<int,long> ReadCore()
    {
        var result=new Dictionary<int,long>();
        if(DateTime.UtcNow<retry)return result;
        try
        {
            if(query==IntPtr.Zero)
            {
                string category=OperatingSystem.IsWindowsVersionAtLeast(10,0,22000)?"Process V2":"Process";
                string processId=category=="Process V2"?"Process ID":"ID Process";
                if(PdhOpenQueryW(null,UIntPtr.Zero,out query)!=0 ||
                   PdhAddEnglishCounterW(query,$@"\{category}(*)\{processId}",UIntPtr.Zero,out ids)!=0 ||
                   PdhAddEnglishCounterW(query,$@"\{category}(*)\Working Set - Private",UIntPtr.Zero,out memory)!=0)
                    throw new InvalidOperationException("Private working set counters unavailable");
            }
            if(PdhCollectQueryData(query)!=0)throw new InvalidOperationException("Memory sample unavailable");
            var processes=Values(ids);var bytes=Values(memory);
            foreach(var pair in processes)
                if(pair.Value>0&&pair.Value<=int.MaxValue&&bytes.TryGetValue(pair.Key,out long value))
                    result[(int)pair.Value]=Math.Max(0,value);
        }
        catch{Dispose();retry=DateTime.UtcNow.AddSeconds(10);}
        return result;
    }
    private static Dictionary<string,long> Values(IntPtr counter)
    {
        uint size=0,count;
        uint status=PdhGetFormattedCounterArrayW(counter,0x400,ref size,out count,IntPtr.Zero);
        if(status!=0x800007D2||size==0||size>16*1024*1024)throw new InvalidOperationException("Invalid memory counter buffer");
        IntPtr buffer=Marshal.AllocHGlobal((int)size);
        try
        {
            if(PdhGetFormattedCounterArrayW(counter,0x400,ref size,out count,buffer)!=0)throw new InvalidOperationException("Memory counters changed during sample");
            var values=new Dictionary<string,long>(StringComparer.Ordinal);
            var occurrences=new Dictionary<string,int>(StringComparer.Ordinal);
            int stride=Marshal.SizeOf<Item>();
            for(int i=0;i<count;i++)
            {
                var item=Marshal.PtrToStructure<Item>(IntPtr.Add(buffer,i*stride));
                if(Marshal.PtrToStringUni(item.Name) is not {} name)continue;
                int ordinal=occurrences.GetValueOrDefault(name);occurrences[name]=ordinal+1;
                // Windows 10's legacy set repeats executable names. Match only
                // within this single collection, never across different samples.
                if(item.Value.Status<=1)values[name+"\0"+ordinal]=item.Value.Bytes;
            }
            return values;
        }
        finally{Marshal.FreeHGlobal(buffer);}
    }
    public void Dispose(){lock(sync){if(query!=IntPtr.Zero)PdhCloseQuery(query);query=ids=memory=IntPtr.Zero;}}
    [StructLayout(LayoutKind.Explicit,Size=16)] private struct Value
    { [FieldOffset(0)] internal uint Status;[FieldOffset(8)] internal long Bytes; }
    [StructLayout(LayoutKind.Sequential)] private struct Item
    { internal IntPtr Name;internal Value Value; }
    [DllImport("pdh.dll",CharSet=CharSet.Unicode)] private static extern uint PdhOpenQueryW(string? source,UIntPtr user,out IntPtr query);
    [DllImport("pdh.dll",CharSet=CharSet.Unicode)] private static extern uint PdhAddEnglishCounterW(IntPtr query,string path,UIntPtr user,out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll",CharSet=CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter,uint format,ref uint size,out uint count,IntPtr buffer);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
}
