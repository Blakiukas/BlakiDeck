using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace Ekrano_valdymas;

internal enum HardwareDriverState { Available, Missing, PermissionRequired, Unavailable }

internal sealed record HardwareAccessStatus(HardwareDriverState Driver, int ErrorCode = 0, string? InitializationProblem = null)
{
    internal string? Problem => InitializationProblem ?? (Driver switch
    {
        HardwareDriverState.Missing => "PawnIO sensor support is not available. Install or repair PawnIO, then retry sensor access. Some temperatures, power and motherboard readings need it.",
        HardwareDriverState.PermissionRequired => "Windows has not granted access to the sensor driver. Retry sensor access and allow the administrator prompt to enable advanced readings.",
        HardwareDriverState.Unavailable => $"The sensor driver could not be opened (Windows error {ErrorCode}). Repair PawnIO and check Windows security notifications, then retry sensor access.",
        _ => null
    });

    internal static HardwareAccessStatus FromError(int error) => new(error switch
    {
        0 => HardwareDriverState.Available,
        2 or 3 => HardwareDriverState.Missing,
        5 => HardwareDriverState.PermissionRequired,
        _ => HardwareDriverState.Unavailable
    }, error);

    // Probe the same kernel device used by LHM. A registry entry or a DLL on disk
    // is not proof that the driver is installed, loaded and accessible.
    internal static HardwareAccessStatus Check()
    {
        using var device = CreateFileW(@"\\?\GLOBALROOT\Device\PawnIO", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        return FromError(device.IsInvalid ? Marshal.GetLastWin32Error() : 0);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
}
