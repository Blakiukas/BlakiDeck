using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Ekrano_valdymas;

internal static class PackageIdentity
{
    internal static string? CurrentFamilyName => IsPackagedProcess()
        ? Windows.ApplicationModel.Package.Current.Id.FamilyName
        : null;

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, IntPtr packageFullName);

    internal static bool IsPackagedProcess()
    {
        uint length = 0;
        return HasIdentityForResult(GetCurrentPackageFullName(ref length, IntPtr.Zero));
    }

    // A null-buffer query reports the required size when an identity exists.
    // Other failures must not misidentify an installed package as portable.
    internal static bool HasIdentityForResult(int result) => result switch
    {
        122 => true,    // ERROR_INSUFFICIENT_BUFFER
        15700 => false, // APPMODEL_ERROR_NO_PACKAGE
        _ => throw new Win32Exception(result)
    };
}
