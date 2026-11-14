using Microsoft.Win32;
using System.IO;
using Windows.ApplicationModel;
namespace Ekrano_valdymas;

internal sealed record WindowsStartupStatus(bool IsEnabled, bool CanChange, string? Message = null, bool OpenWindowsSettings = false);

internal static class WindowsAutoStart
{
    // Must match the windows.startupTask extension in the MSIX manifest.
    internal const string PackagedTaskId = "BlakiDeckStartup";
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovalPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "BlakiDeckRelease";
    private static bool previewEnabled;
    internal static string Command => "\"" + (Environment.ProcessPath ?? throw new InvalidOperationException("Program path unavailable.")) + "\"";
    internal static async Task<WindowsStartupStatus> GetStatusAsync()
    {
        if (DiagnosticState.Root != null) return new(previewEnabled, true);
        if (PackageIdentity.IsPackagedProcess())
            return FromTaskState((await GetPackagedTaskAsync()).State);
        return new(IsUnpackagedEnabled(), true);
    }

    internal static async Task<WindowsStartupStatus> SetEnabledAsync(bool enabled)
    {
        if (DiagnosticState.Root != null) { previewEnabled = enabled; return new(previewEnabled, true); }
        if (PackageIdentity.IsPackagedProcess())
        {
            var task = await GetPackagedTaskAsync();
            // Keep the caller's UI synchronization context for RequestEnableAsync.
            return await ApplyPackagedSettingAsync(enabled, () => task.State,
                async () => await task.RequestEnableAsync(), task.Disable);
        }
        SetUnpackagedEnabled(enabled);
        return new(IsUnpackagedEnabled(), true);
    }

    private static async Task<StartupTask> GetPackagedTaskAsync()
    {
        // A missing declaration/API failure must never fall through to HKCU Run.
        return await StartupTask.GetAsync(PackagedTaskId)
            ?? throw new InvalidOperationException("This package does not declare its Windows startup task.");
    }

    internal static WindowsStartupStatus FromTaskState(StartupTaskState state) => state switch
    {
        StartupTaskState.Disabled => new(false, true),
        StartupTaskState.Enabled => new(true, true),
        StartupTaskState.DisabledByUser => new(false, true,
            "Start with Windows was disabled in Windows. Enable BlakiDeck in Windows Settings > Apps > Startup.", true),
        StartupTaskState.DisabledByPolicy => new(false, false,
            "Windows or your administrator has disabled startup for this app."),
        StartupTaskState.EnabledByPolicy => new(true, false,
            "Your administrator requires this app to start with Windows."),
        _ => throw new InvalidOperationException("Windows returned an unknown startup state. No startup setting was changed.")
    };

    // The same transition logic is exercised with in-memory delegates in the
    // existing test harness, without changing Windows startup registration.
    internal static async Task<WindowsStartupStatus> ApplyPackagedSettingAsync(bool enabled,
        Func<StartupTaskState> readState, Func<Task<StartupTaskState>> requestEnable, Action disable)
    {
        var state = readState();
        var status = FromTaskState(state);
        if (!status.CanChange || state == StartupTaskState.DisabledByUser || status.IsEnabled == enabled)
            return status;
        if (enabled)
            return FromTaskState(await requestEnable());
        disable();
        return FromTaskState(readState());
    }

    private static bool IsUnpackagedEnabled()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunPath);
        using var approval = Registry.CurrentUser.OpenSubKey(ApprovalPath);
        bool disabled = approval?.GetValue(ValueName) is byte[] { Length: > 0 } bytes && bytes[0] == 3;
        return !disabled && string.Equals(run?.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase);
    }
    private static void SetUnpackagedEnabled(bool enabled)
    {
        using var run = Registry.CurrentUser.CreateSubKey(RunPath, true);
        if (enabled)
        {
            if (!File.Exists(Environment.ProcessPath)) throw new IOException("Program executable was not found.");
            run.SetValue(ValueName, Command, RegistryValueKind.String);
            // An explicit re-enable also clears a previous Task Manager disable.
            using var approval = Registry.CurrentUser.OpenSubKey(ApprovalPath, true);
            approval?.DeleteValue(ValueName, false);
        }
        else if (string.Equals(run.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase))
            run.DeleteValue(ValueName, false);
    }
}
