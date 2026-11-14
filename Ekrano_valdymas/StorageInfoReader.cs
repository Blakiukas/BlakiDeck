using System.Management;

namespace Ekrano_valdymas;

internal sealed record StorageInfo(string Id,string Name,string Manufacturer,double Capacity,
    double? Temperature,double? Wear,string Health,DateTime SampledAt,string? StableId=null)
{
    internal string SelectionId=>string.IsNullOrWhiteSpace(StableId)?Id:"device:"+StableId;
    internal string Description => $"{(string.IsNullOrWhiteSpace(Manufacturer)||Name.Contains(Manufacturer,StringComparison.OrdinalIgnoreCase)?Name:Manufacturer+" "+Name)} · {(Capacity>=1099511627776d?$"{Capacity/1099511627776d:0.##} TB":$"{Capacity/1073741824d:0.##} GB")} · Disk {Id}";
}

internal static class StorageInfoReader
{
    // Invoked by the existing elevated helper, at most once every 30 seconds.
    internal static StorageInfo[] Read()
    {
        var result=new List<StorageInfo>();
        try
        {
            using var query=new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage","SELECT * FROM MSFT_PhysicalDisk");
            query.Options.Timeout=TimeSpan.FromSeconds(5);
            using var disks=query.Get();
            foreach(ManagementObject disk in disks)
            using(disk)
            {
                string id=Convert.ToString(disk["DeviceId"])??"",name=Convert.ToString(disk["FriendlyName"])??"Disk "+id;
                double? temperature=null,wear=null;
                try
                {
                    using var counters=disk.GetRelated("MSFT_StorageReliabilityCounter",null,null,null,null,null,false,new EnumerationOptions{Timeout=TimeSpan.FromSeconds(5)});
                    foreach(ManagementObject counter in counters)
                    using(counter)
                    {
                        if(counter["Temperature"] is {} t && Convert.ToDouble(t)>0)temperature=Convert.ToDouble(t);
                        if(Convert.ToInt32(disk["MediaType"]??0)==4 && counter["Wear"] is {} w)wear=Convert.ToDouble(w);if(wear<0||!double.IsFinite(wear??double.NaN))wear=null;
                    }
                }
                catch { /* USB bridges and some device drivers do not expose SMART. */ }
                string health=disk["HealthStatus"] is {} h?Convert.ToInt32(h) switch{0=>"Healthy",1=>"Warning",2=>"Unhealthy",_=>"Unknown"}:"Unknown";
                result.Add(new(id,name,Convert.ToString(disk["Manufacturer"])??"",Convert.ToDouble(disk["Size"]??0),temperature,wear,health,DateTime.UtcNow,Convert.ToString(disk["UniqueId"])));
            }
        }
        catch { }
        return result.ToArray();
    }
}
