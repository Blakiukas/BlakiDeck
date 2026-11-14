using System;

namespace Ekrano_valdymas;

// Set only by an explicit isolated probe, before constructing any controls.
internal static class DiagnosticState
{
    internal static string? Root { get; set; }
    internal static bool HideOtherWindows { get; set; }
    internal static bool LiveMicrophone { get; set; }
    internal static bool SuppressAudioCapture { get; set; }
    internal static string LocalApplicationData => Root ?? ProductProfile.LocalApplicationData;
}
