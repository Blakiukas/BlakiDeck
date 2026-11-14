using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Win32.SafeHandles;

namespace Ekrano_valdymas;

// Called on a worker only when the detected game changes. No process, native
// icon handle, or bitmap cache is retained after a frozen image is returned.
internal static class GameApplicationIcon
{
    private const uint WmGetIcon = 0x007F;
    private const uint AbortIfHungOrExited = 0x0002 | 0x0020;
    private const uint IconMessageTimeoutMilliseconds = 150;
    private const uint QueryLimitedInformation = 0x1000;

    internal static BitmapSource? Read(string? application) => ReadForProcess(application, null);

    internal static BitmapSource? ReadForProcess(string? application, int? expectedProcessId)
    {
        Process[] processes = [];
        try
        {
            if (string.IsNullOrWhiteSpace(application) || expectedProcessId is <= 0) return null;
            string executableName = Path.GetFileName(application.Trim());
            if (!Path.GetExtension(executableName).Equals(
                    ".exe", StringComparison.OrdinalIgnoreCase)) return null;
            string processName = Path.GetFileNameWithoutExtension(executableName);
            if (string.IsNullOrWhiteSpace(processName)) return null;

            processes = Process.GetProcessesByName(processName);
            nint foreground = GetForegroundWindow();
            _ = GetWindowThreadProcessId(foreground, out uint foregroundProcessId);
            foreach (Process process in processes.OrderByDescending(
                         process => ReadProcessId(process) == foregroundProcessId))
            {
                try
                {
                    uint processId = ReadProcessId(process);
                    if (processId == 0 ||
                        (expectedProcessId.HasValue && processId != (uint)expectedProcessId.Value))
                        continue;
                    BitmapSource? executableIcon = null;
                    string? path = ReadExecutablePath(process, processId);
                    if (path != null && Path.GetFileName(path).Equals(
                            executableName, StringComparison.OrdinalIgnoreCase))
                        executableIcon = QuickLaunchBar.GameExecutableIcon(path);
                    // The embedded frame retains its genuine source dimensions.
                    // Prefer it over the usually 16/32-pixel title-bar icon.
                    if (executableIcon is { PixelWidth: >= 128, PixelHeight: >= 128 })
                        return executableIcon;

                    nint window = nint.Zero;
                    try { window = process.MainWindowHandle; } catch { }
                    if (window == nint.Zero && processId == foregroundProcessId)
                        window = foreground;
                    if (window != nint.Zero &&
                        GetWindowThreadProcessId(window, out uint windowProcessId) != 0 &&
                        windowProcessId == processId &&
                        ReadWindowIcon(window) is { } windowIcon)
                        return HigherResolution(executableIcon, windowIcon);
                    if (executableIcon != null) return executableIcon;
                }
                catch
                {
                    // The game can exit, replace its window, or deny access
                    // between any two queries. Try another exact-name process.
                }
            }
        }
        catch { }
        finally
        {
            // Early successful returns must also dispose every process object.
            foreach (Process process in processes)
                try { process.Dispose(); } catch { }
        }
        return null;
    }

    internal static BitmapSource? ReadWindowIcon(nint window)
    {
        if (window == nint.Zero) return null;
        BitmapSource? bestIcon = null;
        try
        {
            uint dpi = GetDpiForWindow(window);
            nint iconDpi = (nint)(dpi == 0 ? 96u : dpi);
            // Keep the largest genuine window/class frame. Resizing a small
            // HICON here cannot recover the application's original artwork.
            foreach (nuint kind in new nuint[] { 1, 2, 0 })
            {
                if (SendMessageTimeout(window, WmGetIcon, kind, iconDpi,
                        AbortIfHungOrExited, IconMessageTimeoutMilliseconds,
                        out nint icon) != nint.Zero &&
                    CopyWindowIcon(icon) is { } image)
                    bestIcon = HigherResolution(bestIcon, image);
            }
            foreach (int index in new[] { -14, -34 })
            {
                nint icon = nint.Size == 8
                    ? GetClassLongPtr(window, index)
                    : (nint)GetClassLong(window, index);
                if (CopyWindowIcon(icon) is { } image)
                    bestIcon = HigherResolution(bestIcon, image);
            }
        }
        catch { }
        return bestIcon;
    }

    private static BitmapSource HigherResolution(BitmapSource? first, BitmapSource second) =>
        first != null && (long)first.PixelWidth * first.PixelHeight >=
            (long)second.PixelWidth * second.PixelHeight ? first : second;

    private static BitmapSource? CopyWindowIcon(nint borrowedIcon)
    {
        if (borrowedIcon == nint.Zero) return null;
        nint ownedIcon = CopyIcon(borrowedIcon);
        if (ownedIcon == nint.Zero) return null;
        try
        {
            BitmapSource image = Imaging.CreateBitmapSourceFromHIcon(
                ownedIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        finally
        {
            // WM_GETICON/GetClassLongPtr return borrowed handles. Only destroy
            // our independent CopyIcon result, never the game's own handle.
            _ = DestroyIcon(ownedIcon);
        }
    }

    private static uint ReadProcessId(Process process)
    {
        try { return (uint)process.Id; }
        catch { return 0; }
    }

    private static string? ReadExecutablePath(Process process, uint processId)
    {
        try
        {
            string? path = process.MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(path)) return path;
        }
        catch { }
        try
        {
            // This narrower query can succeed when MainModule is denied by a
            // protected game. Failure still leaves its window-icon route above.
            using SafeProcessHandle handle = OpenProcess(QueryLimitedInformation, false, processId);
            if (handle.IsInvalid) return null;
            StringBuilder path = new(32768);
            uint length = (uint)path.Capacity;
            return QueryFullProcessImageName(handle, 0, path, ref length)
                ? path.ToString() : null;
        }
        catch { return null; }
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SendMessageTimeout(nint window, uint message, nuint wParam,
        nint lParam, uint flags, uint timeoutMilliseconds, out nint result);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    private static extern nint GetClassLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "GetClassLongW")]
    private static extern uint GetClassLong(nint window, int index);
    [DllImport("user32.dll")]
    private static extern nint CopyIcon(nint icon);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process,
        uint flags, StringBuilder path, ref uint length);
}
