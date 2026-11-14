using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace Ekrano_valdymas
{
    internal static class ShortcutShellActions
    {
        private const uint SeeMaskInvokeIdList = 0x0000000C;
        private const int SwShow = 5;

        public static bool IsFileTarget(string? target)
        {
            if (string.IsNullOrWhiteSpace(target)) return false;
            return !Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) || uri.IsFile;
        }

        public static async void ShowProperties(Window? owner, string? target)
        {
            if (!IsFileTarget(target) || !File.Exists(target) && !Directory.Exists(target)) return;
            bool restoreTopmost = owner?.Topmost == true;
            IntPtr ownerHandle = owner == null ? IntPtr.Zero : new WindowInteropHelper(owner).Handle;
            HashSet<IntPtr> previousWindows = EnumerateTopLevelWindows();
            if (owner != null && restoreTopmost) owner.Topmost = false;
            ShellExecuteInfo info = new()
            {
                cbSize = Marshal.SizeOf<ShellExecuteInfo>(),
                fMask = SeeMaskInvokeIdList,
                hwnd = ownerHandle,
                lpVerb = "properties",
                lpFile = target,
                nShow = SwShow
            };
            if (!ShellExecuteEx(ref info))
            {
                if (owner != null && restoreTopmost) owner.Topmost = true;
                return;
            }

            IntPtr propertiesWindow = IntPtr.Zero;
            for (int attempt = 0; attempt < 40 && propertiesWindow == IntPtr.Zero; attempt++)
            {
                await Task.Delay(100);
                foreach (IntPtr window in EnumerateTopLevelWindows())
                {
                    if (previousWindows.Contains(window) || !IsWindowVisible(window)) continue;
                    string title = GetTitle(window);
                    if (GetWindow(window, 4) == ownerHandle || title.Contains("Properties", StringComparison.OrdinalIgnoreCase))
                    {
                        propertiesWindow = window;
                        break;
                    }
                }
            }

            if (propertiesWindow != IntPtr.Zero)
                while (IsWindow(propertiesWindow)) await Task.Delay(200);
            if (owner != null && restoreTopmost && owner.IsLoaded) owner.Topmost = true;
        }

        private static HashSet<IntPtr> EnumerateTopLevelWindows()
        {
            HashSet<IntPtr> result = new();
            EnumWindows((window, _) => { result.Add(window); return true; }, IntPtr.Zero);
            return result;
        }

        private static string GetTitle(IntPtr window)
        {
            StringBuilder value = new(260);
            GetWindowText(window, value, value.Capacity);
            return value.ToString();
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ShellExecuteInfo
        {
            public int cbSize;
            public uint fMask;
            public IntPtr hwnd;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpVerb;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpFile;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpParameters;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectory;
            public int nShow;
            public IntPtr hInstApp;
            public IntPtr lpIDList;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpClass;
            public IntPtr hkeyClass;
            public uint dwHotKey;
            public IntPtr hIcon;
            public IntPtr hProcess;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ShellExecuteEx(ref ShellExecuteInfo info);
        private delegate bool EnumWindowProc(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);
    }
}
