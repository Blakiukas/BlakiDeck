using System.IO;
using System.Runtime.InteropServices;

namespace Ekrano_valdymas;

internal static class ShellTargetPath
{
    // Start menu drops may expose a known-folder GUID instead of a drive path.
    // Resolve only at the I/O boundary: saved target keys also own appearance settings.
    internal static string Resolve(string target)
    {
        if(target.Length<38 || target[0]!='{' || target[37]!='}' ||
           (target.Length>38 && target[38]!='\\' && target[38]!='/') ||
           !Guid.TryParse(target[..38],out var folder))return target;
        IntPtr path=IntPtr.Zero;
        try
        {
            if(SHGetKnownFolderPath(ref folder,0,IntPtr.Zero,out path)<0)return target;
            string? root=Marshal.PtrToStringUni(path);
            return string.IsNullOrEmpty(root)?target:Path.Combine(root,target[38..].TrimStart('\\','/'));
        }
        catch { return target; }
        finally { if(path!=IntPtr.Zero)Marshal.FreeCoTaskMem(path); }
    }

    [DllImport("shell32.dll",ExactSpelling=true)]
    private static extern int SHGetKnownFolderPath(ref Guid folder,uint flags,IntPtr token,out IntPtr path);
}
