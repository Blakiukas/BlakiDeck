using System.Runtime.InteropServices;
namespace Ekrano_valdymas;
internal static class HudDisplayRefreshRate
{
    [StructLayout(LayoutKind.Explicit,Size=220,CharSet=CharSet.Unicode)] private struct DevMode { [FieldOffset(68)]public ushort Size;[FieldOffset(184)]public uint Frequency; }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct MonitorInfo {public int Size;public int Left,Top,Right,Bottom,WorkLeft,WorkTop,WorkRight,WorkBottom;public uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Device;}
    [DllImport("user32.dll")]private static extern nint MonitorFromWindow(nint hwnd,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool GetMonitorInfo(nint monitor,ref MonitorInfo info);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool EnumDisplaySettings(string device,int mode,ref DevMode settings);
    internal static int Read(nint window){var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>(),Device=""};var mode=new DevMode{Size=220};return GetMonitorInfo(MonitorFromWindow(window,2),ref info)&&EnumDisplaySettings(info.Device,-1,ref mode)&&mode.Frequency is >1 and <=1000?(int)mode.Frequency:0;}
}
