using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Ekrano_valdymas;

public partial class QuickLaunchBar
{
    // Read the first application icon family at its largest native frame. The
    // older fixed-size extractor can upscale a 16/32px frame to 64px and hide
    // that loss of detail behind apparently larger PixelWidth/PixelHeight.
    internal static BitmapSource? GameExecutableIcon(string executablePath)
    {
        nint module = nint.Zero;
        try
        {
            executablePath = Environment.ExpandEnvironmentVariables(executablePath.Trim().Trim('"'));
            if (!Path.GetExtension(executablePath).Equals(
                    ".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(executablePath)) return null;
            module = GameLoadLibraryEx(Path.GetFullPath(executablePath), nint.Zero, 0x0002 | 0x0020);
            if (module != nint.Zero)
            {
                BitmapSource? image = null;
                GameResourceNameCallback readFirstFamily = (library, type, name, _) =>
                {
                    // A resource-name pointer is valid only inside the callback.
                    // Do not select an unrelated toolbar icon from later groups.
                    try
                    {
                        byte[]? group = GameReadResource(library, name, type, 1024 * 1024);
                        if (group != null) image = GameLargestIconFrame(library, group);
                    }
                    catch { }
                    return false;
                };
                _ = GameEnumResourceNames(module, (nint)14, readFirstFamily, nint.Zero);
                GC.KeepAlive(readFirstFamily);
                if (image != null) return image;
            }

            // Retain a usable native fallback if a resource codec is unsupported.
            // ExtractIconEx supplies its native dimensions; do not upscale them.
            nint[] icons = new nint[1];
            if (ExtractIconEx(executablePath, 0, icons, null, 1) == 0 || icons[0] == nint.Zero)
                return null;
            try
            {
                BitmapSource image = Imaging.CreateBitmapSourceFromHIcon(
                    icons[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                image.Freeze();
                return image;
            }
            finally { _ = DestroyIcon(icons[0]); }
        }
        catch { return null; }
        finally { if (module != nint.Zero) _ = GameFreeLibrary(module); }
    }

    private static BitmapSource? GameLargestIconFrame(nint module, byte[] group)
    {
        if (group.Length < 6 || BitConverter.ToUInt16(group, 0) != 0 ||
            BitConverter.ToUInt16(group, 2) != 1) return null;
        int count = BitConverter.ToUInt16(group, 4);
        if (count == 0 || count > 256 || group.Length < 6 + count * 14) return null;
        int Dimension(byte value) => value == 0 ? 256 : value;
        foreach (int offset in Enumerable.Range(0, count).Select(index => 6 + index * 14)
                     .OrderByDescending(offset => Dimension(group[offset]) * Dimension(group[offset + 1]))
                     .ThenByDescending(offset => BitConverter.ToUInt16(group, offset + 6)))
        {
            try
            {
                ushort resourceId = BitConverter.ToUInt16(group, offset + 12);
                byte[]? pixels = GameReadResource(module, (nint)resourceId, (nint)3, 16 * 1024 * 1024);
                if (pixels == null) continue;
                // Reconstruct a single-frame ICO container around the unchanged
                // RT_ICON bytes; the WPF decoder handles both PNG and DIB frames.
                using MemoryStream icon = new(pixels.Length + 22);
                using (BinaryWriter writer = new(icon, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)1);
                    writer.Write(group, offset, 8);
                    writer.Write((uint)pixels.Length); writer.Write(22u); writer.Write(pixels);
                }
                icon.Position = 0;
                BitmapFrame image = BitmapDecoder.Create(icon, BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad).Frames[0];
                if (image.PixelWidth > 2048 || image.PixelHeight > 2048) continue;
                image.Freeze();
                return image;
            }
            catch { }
        }
        return null;
    }

    private static byte[]? GameReadResource(nint module, nint name, nint type, int maximumBytes)
    {
        nint resource = GameFindResource(module, name, type);
        if (resource == nint.Zero) return null;
        uint length = GameSizeofResource(module, resource);
        if (length == 0 || length > maximumBytes) return null;
        nint bytes = GameLockResource(GameLoadResource(module, resource));
        if (bytes == nint.Zero) return null;
        byte[] result = new byte[(int)length];
        Marshal.Copy(bytes, result, 0, result.Length);
        return result;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool GameResourceNameCallback(nint module, nint type, nint name, nint parameter);
    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode)]
    private static extern nint GameLoadLibraryEx(string path, nint file, uint flags);
    [DllImport("kernel32.dll", EntryPoint = "FreeLibrary")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GameFreeLibrary(nint module);
    [DllImport("kernel32.dll", EntryPoint = "EnumResourceNamesW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GameEnumResourceNames(nint module, nint type,
        GameResourceNameCallback callback, nint parameter);
    [DllImport("kernel32.dll", EntryPoint = "FindResourceW", CharSet = CharSet.Unicode)]
    private static extern nint GameFindResource(nint module, nint name, nint type);
    [DllImport("kernel32.dll", EntryPoint = "LoadResource")]
    private static extern nint GameLoadResource(nint module, nint resource);
    [DllImport("kernel32.dll", EntryPoint = "LockResource")]
    private static extern nint GameLockResource(nint resource);
    [DllImport("kernel32.dll", EntryPoint = "SizeofResource")]
    private static extern uint GameSizeofResource(nint module, nint resource);
}
