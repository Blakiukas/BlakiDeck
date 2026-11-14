using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Ekrano_valdymas;

internal sealed record CameraDevice(string Id,string Name)
{
    public override string ToString()=>Name;
}
internal static class CameraControl
{
    [StructLayout(LayoutKind.Sequential)] private struct DevInfo {public int Size;public Guid ClassGuid;public uint DevInst;public IntPtr Reserved;}
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode)] private static extern IntPtr SetupDiGetClassDevsW(ref Guid g,IntPtr e,IntPtr h,uint flags);
    [DllImport("setupapi.dll")] private static extern bool SetupDiEnumDeviceInfo(IntPtr h,uint i,ref DevInfo d);
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode)] private static extern bool SetupDiGetDeviceInstanceIdW(IntPtr h,ref DevInfo d,StringBuilder id,int size,out int required);
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode)] private static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr h,ref DevInfo d,uint property,out uint type,byte[] data,int size,out int required);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr h);
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] private static extern uint CM_Locate_DevNodeW(out uint node,string id,uint flags);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_DevNode_Status(out uint status,out uint problem,uint node,uint flags);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Disable_DevNode(uint node,uint flags);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Enable_DevNode(uint node,uint flags);
    internal static List<CameraDevice> Devices()
    {
        var result=new List<CameraDevice>();
        foreach(var cls in new[]{"ca3e7ab9-b4c3-4ae6-8251-579ef933890f","6bdd1fc6-810f-11d0-bec7-08002be2092f"})
        {
            var guid=new Guid(cls);var handle=SetupDiGetClassDevsW(ref guid,IntPtr.Zero,IntPtr.Zero,2);
            if(handle==new IntPtr(-1))continue;
            try
            {
                for(uint i=0;;i++)
                {
                    var d=new DevInfo{Size=Marshal.SizeOf<DevInfo>()};if(!SetupDiEnumDeviceInfo(handle,i,ref d))break;
                    string Property(uint p){var data=new byte[4096];return SetupDiGetDeviceRegistryPropertyW(handle,ref d,p,out _,data,data.Length,out _)?Encoding.Unicode.GetString(data).Split('\0')[0]:"";}
                    // Legacy Image class also contains scanners; never allow a scanner action.
                    if(cls.StartsWith("6bdd")&&!Property(4).Equals("usbvideo",StringComparison.OrdinalIgnoreCase))continue;
                    var id=new StringBuilder(1024);if(!SetupDiGetDeviceInstanceIdW(handle,ref d,id,id.Capacity,out _))continue;
                    string name=Property(12);if(name.Length==0)name=Property(0);result.Add(new(id.ToString(),name));
                }
            }
            finally{SetupDiDestroyDeviceInfoList(handle);}
        }
        return result;
    }
    internal static bool? Enabled(string id)
    {
        if(string.IsNullOrWhiteSpace(id)||CM_Locate_DevNodeW(out var node,id,0)!=0||CM_Get_DevNode_Status(out var status,out var problem,node,0)!=0)return null;
        if(problem==22)return false;
        return problem==0&&(status&8)!=0?true:null;
    }
    internal static int SetEnabled(string id,bool enabled)
    {
        if(!Devices().Any(d=>d.Id.Equals(id,StringComparison.OrdinalIgnoreCase)))return 13;
        if(CM_Locate_DevNodeW(out var node,id,0)!=0)return 13;
        return (int)(enabled?CM_Enable_DevNode(node,0):CM_Disable_DevNode(node,12));
    }
    internal static async Task Execute(MacroStep step,CancellationToken token)
    {
        bool before=Enabled(step.DeviceId)??throw new InvalidOperationException("Camera unavailable. Select a connected camera in Edit action.");
        bool requested=step.Operation switch{"On"=>true,"Off"=>false,_=>!before};
        if(before!=requested)
        {
            int code=SetEnabled(step.DeviceId,requested);
            if(code==0x33)
            {
                var info=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden};
                info.ArgumentList.Add("--camera-device-action");info.ArgumentList.Add(requested?"On":"Off");info.ArgumentList.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(step.DeviceId)));
                using var process=Process.Start(info)??throw new InvalidOperationException("Camera action was not started.");
                await process.WaitForExitAsync();code=process.ExitCode;
            }
            if(code!=0)throw new InvalidOperationException($"Windows rejected camera change (code {code}).");
            for(int i=0;i<20&&Enabled(step.DeviceId)!=requested;i++)await Task.Delay(100);
        }
        if(Enabled(step.DeviceId)!=requested)throw new InvalidOperationException("Camera state could not be confirmed.");
        MacroLibrary.Current.RefreshDeviceStates();
    }
}


