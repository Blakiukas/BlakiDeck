using System.Globalization;
using System.Management;
using Microsoft.Win32;

namespace Ekrano_valdymas;

internal sealed record PcSpecificationDevice(string Name,string Text,string HardwareKind="",string HardwareName="");
internal sealed record PcSpecificationSection(string Title,string Text)
{
    internal PcSpecificationDevice[] Devices => (Title is "System" or "Board & BIOS" ? [Text] : Text.Split([Environment.NewLine+Environment.NewLine],StringSplitOptions.RemoveEmptyEntries))
        .Select((text,index)=>
        {
            var lines=text.Split(Environment.NewLine);
            string Field(string label)=>lines.FirstOrDefault(l=>l.StartsWith(label+": "))?[(label.Length+2)..]??"";
            string name=Title switch {"Memory"=>Field("Part")+" · "+Field("Capacity")+" · module "+(index+1),"Board & BIOS"=>Field("Board"),"System"=>Field("Windows"),_=>""};
            if(string.IsNullOrWhiteSpace(name))name=lines[0].Contains(": ")?lines[0][(lines[0].IndexOf(": ")+2)..]:lines[0];
            return new PcSpecificationDevice(name,text,Title=="CPU"?"Cpu":Title=="Graphics"?"Gpu":"",name);
        }).ToArray();
}

internal static class PcSpecifications
{
    internal static Task<PcSpecificationSection[]> ReadAsync()=>Task.Run(Read);
    private static string Value(object? value)=>value switch
    {
        null=>"Not reported",
        string s when string.IsNullOrWhiteSpace(s)=>"Not reported",
        Array a=>string.Join(", ",a.Cast<object>()),
        _=>Convert.ToString(value,CultureInfo.InvariantCulture)?.Trim()??"Not reported"
    };
    private static string Bytes(object? value)=>ulong.TryParse(Value(value),out var n)&&n>0
        ?n>=1099511627776UL?$"{n/1099511627776d:0.##} TB":$"{n/1073741824d:0.##} GB":"Not reported";
    private static string Frequency(object? value)=>uint.TryParse(Value(value),out var n)&&n>0?$"{n} MHz":"Not reported";
    private static string MemoryType(object? value)=>Value(value) switch{"26"=>"DDR4","34"=>"DDR5","24"=>"DDR3","30"=>"LPDDR4","35"=>"LPDDR5",_=>"Not reported"};
    private static string Date(object? value){try{return ManagementDateTimeConverter.ToDateTime(Value(value)).ToString("yyyy-MM-dd");}catch{return "Not reported";}}
    private static string ReadRows(string query,(string Property,string Label,Func<object?,string>? Format)[] fields,string scope=@"root\cimv2")
    {
        try
        {
            using var search=new ManagementObjectSearcher(scope,query);
            search.Options.Timeout=TimeSpan.FromSeconds(4);
            using var objects=search.Get();var devices=new List<string>();
            foreach(ManagementObject item in objects)
            using(item)
            {
                devices.Add(string.Join(Environment.NewLine,fields.Select(f=>
                {
                    object? value=null;try{value=item[f.Property];}catch{}
                    return f.Label+": "+(f.Format??Value)(value);
                })));
            }
            return devices.Count==0?"No devices reported by Windows.":string.Join(Environment.NewLine+Environment.NewLine,devices);
        }
        catch{return "Windows could not provide this information.";}
    }
    private static string GpuMemory(object? name)
    {
        // AdapterRAM is only 32-bit and truncates modern GPU capacities. Use the
        // driver's 64-bit dedicated-memory property instead; never guess 4 GB.
        try
        {
            using var root=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            foreach(string sub in root?.GetSubKeyNames()??[])
            {
                using var key=root!.OpenSubKey(sub);
                if(!string.Equals(Value(key?.GetValue("DriverDesc")),Value(name),StringComparison.OrdinalIgnoreCase))continue;
                if(key?.GetValue("HardwareInformation.qwMemorySize") is long bytes&&bytes>0)return Bytes(bytes);
                if(key?.GetValue("HardwareInformation.qwMemorySize") is byte[] raw&&raw.Length==8)return Bytes(BitConverter.ToUInt64(raw));
            }
        }
        catch{}
        return "Not reported (shared memory is not counted as dedicated VRAM)";
    }
    private static PcSpecificationSection[] Read()=>
    [
        new("System",ReadRows("SELECT Manufacturer,Model,SystemType,TotalPhysicalMemory FROM Win32_ComputerSystem",
            [("Manufacturer","Manufacturer",null),("Model","Model",null),("SystemType","Architecture",null),("TotalPhysicalMemory","RAM available to Windows",Bytes)])+Environment.NewLine+Environment.NewLine+
            ReadRows("SELECT Caption,Version,BuildNumber,OSArchitecture FROM Win32_OperatingSystem",
            [("Caption","Windows",null),("Version","Version",null),("BuildNumber","Build",null),("OSArchitecture","OS architecture",null)])),
        new("CPU",ReadRows("SELECT Name,Manufacturer,SocketDesignation,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed,L2CacheSize,L3CacheSize,AddressWidth FROM Win32_Processor",
            [("Name","Processor",null),("Manufacturer","Manufacturer",null),("SocketDesignation","Socket",null),("NumberOfCores","Cores",null),("NumberOfLogicalProcessors","Logical processors",null),("MaxClockSpeed","Reported base / maximum clock",Frequency),("L2CacheSize","L2 cache (KB)",null),("L3CacheSize","L3 cache (KB)",null),("AddressWidth","Address width (bits)",null)])),
        new("Memory",ReadRows("SELECT DeviceLocator,BankLabel,Manufacturer,PartNumber,Capacity,SMBIOSMemoryType,Speed,ConfiguredClockSpeed,DataWidth,ConfiguredVoltage FROM Win32_PhysicalMemory",
            [("DeviceLocator","Slot",null),("BankLabel","Bank",null),("Manufacturer","Manufacturer",null),("PartNumber","Part",null),("Capacity","Capacity",Bytes),("SMBIOSMemoryType","Type",MemoryType),("Speed","Reported speed (MT/s)",null),("ConfiguredClockSpeed","Configured speed (MT/s)",null),("DataWidth","Data width (bits)",null),("ConfiguredVoltage","Configured voltage",v=>uint.TryParse(Value(v),out var n)&&n>0?$"{n/1000d:0.###} V":"Not reported")])),
        new("Graphics",ReadRows("SELECT Name,VideoProcessor,AdapterCompatibility,DriverVersion,DriverDate,CurrentHorizontalResolution,CurrentVerticalResolution,CurrentRefreshRate FROM Win32_VideoController",
            [("Name","Graphics adapter",null),("VideoProcessor","Processor",null),("AdapterCompatibility","Manufacturer",null),("Name","Dedicated VRAM (driver reported)",GpuMemory),("DriverVersion","Driver",null),("DriverDate","Driver date",Date),("CurrentHorizontalResolution","Reported display width (px)",null),("CurrentVerticalResolution","Reported display height (px)",null),("CurrentRefreshRate","Reported refresh rate (Hz)",null)])),
        new("Storage",ReadRows("SELECT Model,Size,InterfaceType,MediaType,FirmwareRevision,Partitions,BytesPerSector FROM Win32_DiskDrive",
            [("Model","Drive",null),("Size","Capacity",Bytes),("InterfaceType","Windows-reported interface",null),("MediaType","Media",null),("FirmwareRevision","Firmware",null),("Partitions","Partitions",null),("BytesPerSector","Bytes per sector",null)])+Environment.NewLine+Environment.NewLine+
            ReadRows("SELECT DeviceID,FileSystem,Size,FreeSpace FROM Win32_LogicalDisk WHERE DriveType=3",
            [("DeviceID","Volume",null),("FileSystem","File system",null),("Size","Capacity",Bytes),("FreeSpace","Free",Bytes)])),
        new("Board & BIOS",ReadRows("SELECT Manufacturer,Product,Version FROM Win32_BaseBoard",
            [("Manufacturer","Manufacturer",null),("Product","Board",null),("Version","Revision",null)])+Environment.NewLine+Environment.NewLine+
            ReadRows("SELECT Manufacturer,SMBIOSBIOSVersion,ReleaseDate FROM Win32_BIOS",
            [("Manufacturer","BIOS vendor",null),("SMBIOSBIOSVersion","BIOS version",null),("ReleaseDate","Release date",Date)])),
        new("Displays",ReadDisplays()),
        new("Network",ReadRows("SELECT Name,Manufacturer,NetConnectionID,NetEnabled,Speed FROM Win32_NetworkAdapter WHERE PhysicalAdapter=True",
            [("Name","Network adapter",null),("Manufacturer","Manufacturer",null),("NetConnectionID","Connection",null),("NetEnabled","Enabled",null),("Speed","Reported link rate",v=>ulong.TryParse(Value(v),out var n)&&n>0&&n<long.MaxValue?$"{n/1000000d:0.##} Mbit/s":"Not connected / not reported")])),
        new("Audio",ReadAudio())
    ];

    private static string MonitorText(object? value)=>value is ushort[] chars&&chars.Any(c=>c!=0)?new string(chars.Where(c=>c!=0).Select(c=>(char)c).ToArray()):"Not reported";

    private static string ReadDisplays()
    {
        // Match EDID metadata by device identity, never by friendly name (two
        // identical models may have different active modes).
        static string Key(string path)
        {
            string result=path.Replace(@"\\?\","").Replace('#','\\');int suffix=result.IndexOf(@"\{");
            if(suffix>=0)result=result[..suffix];
            if(result.EndsWith("_0",StringComparison.Ordinal))result=result[..^2];
            return result;
        }
        var metadata=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var search=new ManagementObjectSearcher(@"root\wmi","SELECT InstanceName,ManufacturerName,WeekOfManufacture,YearOfManufacture FROM WmiMonitorID WHERE Active=True");search.Options.Timeout=TimeSpan.FromSeconds(4);
            using var objects=search.Get();foreach(ManagementObject item in objects)using(item)
                metadata[Key(Value(item["InstanceName"]))]=string.Join(Environment.NewLine,"Manufacturer ID: "+MonitorText(item["ManufacturerName"]),"Manufacture week: "+Value(item["WeekOfManufacture"]),"Manufacture year: "+Value(item["YearOfManufacture"]));
        }
        catch { }
        var blocks=new List<string>();
        foreach(var display in MonitorRefreshRate.ReadActiveDisplays())
        {
            var screen=System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s=>s.DeviceName.Equals(display.Device,StringComparison.OrdinalIgnoreCase));
            string hz=display.Denominator>0&&display.Numerator>0?$"{display.Numerator/(double)display.Denominator:0.##} Hz":"Not reported";
            string resolution=display.Width>0&&display.Height>0?$"{display.Width} × {display.Height} px":"Not reported";
            var lines=new List<string>{"Monitor: "+display.Name,"Active signal resolution: "+resolution,"Refresh rate: "+hz,"Windows display: "+display.Device};
            if(screen!=null){lines.Add($"Desktop resolution: {screen.Bounds.Width} × {screen.Bounds.Height} px");lines.Add("Primary display: "+(screen.Primary?"Yes":"No"));}
            lines.Add("Rotation: "+(display.Rotation switch{1=>"0°",2=>"90°",3=>"180°",4=>"270°",_=>"Not reported"}));
            if(metadata.TryGetValue(Key(display.Path),out string? detail))lines.Add(detail);
            blocks.Add(string.Join(Environment.NewLine,lines));
        }
        return blocks.Count>0?string.Join(Environment.NewLine+Environment.NewLine,blocks):"Active monitor modes could not be read from Windows.";
    }

    private static string ReadAudio()
    {
        var devices=new List<string>();
        try
        {
            using var enumerator=new NAudio.CoreAudioApi.MMDeviceEnumerator();
            foreach(var device in enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.All,NAudio.CoreAudioApi.DeviceState.Active))
            using(device)
            {
                string text="Audio endpoint: "+device.FriendlyName+Environment.NewLine+"Direction: "+(device.DataFlow==NAudio.CoreAudioApi.DataFlow.Render?"Playback":"Recording");
                try { using var client=device.CreateAudioClient();var f=client.MixFormat;text+=$"\nShared-mode sample rate: {f.SampleRate} Hz\nChannels: {f.Channels}\nShared-mode format: {f.BitsPerSample} bit · {f.Encoding}"; } catch { }
                devices.Add(text.Replace("\r\n","\n").Replace("\n",Environment.NewLine));
            }
        }
        catch { }
        return devices.Count>0?string.Join(Environment.NewLine+Environment.NewLine,devices):ReadRows("SELECT Name,Manufacturer FROM Win32_SoundDevice",[("Name","Audio device",null),("Manufacturer","Manufacturer",null)]);
    }

    internal static HardwareReading[] LiveReadings(PcSpecificationDevice device)
    {
        if(device.HardwareKind.Length==0)return [];
        static string Normalize(string name)
        {
            name=System.Text.RegularExpressions.Regex.Replace(name,@"\b\d+-Core Processor\b","",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            name=name.Replace("(TM)","",StringComparison.OrdinalIgnoreCase).Replace("(R)","",StringComparison.OrdinalIgnoreCase);
            return new string(name.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        }
        var matches=HardwareTelemetry.Readings.Where(r=>r.HardwareKind.StartsWith(device.HardwareKind,StringComparison.OrdinalIgnoreCase)&&Normalize(r.Device)==Normalize(device.HardwareName)).ToArray();
        // Never mix identically named adapters in different hardware slots.
        if(matches.Select(r=>string.Join("/",r.Id.Split('/').Take(3))).Distinct().Count()>1)return [];
        return matches.Where(r=>r.Unit is "MHz" or "W" or "°C" or "%" or "MB" or "RPM" or "V")
            .OrderBy(r=>r.Unit switch {"MHz"=>0,"W"=>1,"°C"=>2,"%"=>3,_=>4}).ThenBy(r=>r.Name).ToArray();
    }
}
