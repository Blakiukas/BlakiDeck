using System.Runtime.InteropServices;
namespace Ekrano_valdymas;

internal static class NativeEcoQoS
{
    [StructLayout(LayoutKind.Sequential)] private struct PowerState
    { internal uint Version, ControlMask, StateMask; }
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool SetProcessInformation(nint process,int infoClass,ref PowerState state,uint size);
    [DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    // Called only inside the dedicated sensor helper, never the UI/audio process.
    internal static bool SetCurrentHelper(bool enabled)
    {
        var state=new PowerState{Version=1,ControlMask=enabled?1u:0u,StateMask=enabled?1u:0u};
        try{return SetProcessInformation(GetCurrentProcess(),4,ref state,(uint)Marshal.SizeOf<PowerState>());}
        catch(EntryPointNotFoundException){return false;}
    }
}
