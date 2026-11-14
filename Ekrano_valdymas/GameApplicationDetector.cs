using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
namespace Ekrano_valdymas;

// Classification and protected-game companion matching ported from the user's
// BlakiHUD FrameTelemetryService. No second PresentMon collector is started.
internal sealed class GameApplicationDetector
{
    internal sealed record GameProcess(string Application,int ProcessId,DateTime? StartTimeUtc=null)
    { internal bool IsSuspended {get;init;} internal string? ExecutablePath {get;init;} }
    private DateTime _lastGameScan;
    private GameProcess? _cachedGameApplication;
    internal string? Read() => ReadProcess()?.Application;
    internal GameProcess? ReadProcess() => GetRunningGameApplication();
private static readonly HashSet<string> LauncherExecutables = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "steam.exe", "steamwebhelper.exe", "gameoverlayui.exe",
        "battle.net.exe", "agent.exe", "blizzardbrowser.exe",
        "epicgameslauncher.exe", "epicwebhelper.exe",
        "eadesktop.exe", "ealauncher.exe", "eabackgroundservice.exe",
        "upc.exe", "ubisoftconnect.exe", "ubisoftgamelauncher.exe",
        "ubisoftconnectwebcore.exe",
        "galaxyclient.exe", "galaxyclientservice.exe", "galaxycommunication.exe",
        "riotclientservices.exe", "riotclientux.exe", "riotclientuxrender.exe",
        "riotclientcrashhandler.exe",
        "rockstargameslauncher.exe", "launcherpatcher.exe", "socialclubhelper.exe",
        "gamingservices.exe", "gamingservicesnet.exe", "xboxpcapp.exe",
        "amazon games.exe", "amazon games ui.exe", "playnite.desktopapp.exe"
    };

private static readonly string[] LauncherPathMarkers =
    {
        @"\Epic Games\Launcher\",
        @"\Riot Games\Riot Client\",
        @"\Rockstar Games\Launcher\",
        @"\Ubisoft\Ubisoft Game Launcher\",
        @"\EA Desktop\",
        @"\Electronic Arts\EA Desktop\",
        @"\Battle.net\",
        @"\Blizzard Entertainment\Battle.net\",

        @"\launcher\",
        @"\launchers\"
    };

private static bool IsIgnoredApplication(string application)
    {
        var fileName = Path.GetFileName(application);
        var normalized = fileName.ToLowerInvariant();
        if (normalized is "blender.exe" or "wallpaper32.exe" or "wallpaper64.exe" or "wallpaperui.exe" or "aseprite.exe" or "vtubestudio.exe") return true;
        if (normalized.Contains("steelseries") ||
            normalized.Contains("moments") ||
            normalized.Contains("discord") ||
            normalized.Contains("gamebar") ||
            normalized.Contains("overlay") ||
            normalized.Contains("capture") ||
            normalized.Contains("launcher") ||
            normalized.Contains("service"))
            return true;
        if (LauncherExecutables.Contains(fileName)) return true;
        return fileName.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("firefox.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("Codex.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("ChatGPT.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("Spotify.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("obs64.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("ShellExperienceHost.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("SearchHost.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("TextInputHost.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("csrss.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("smss.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("wininit.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("winlogon.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("services.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("lsass.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("svchost.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("RuntimeBroker.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("taskhostw.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("conhost.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("audiodg.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("SystemSettings.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("BlakiHUD.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("1920x480HUD.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("dwm.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase);
    }

private GameProcess? GetRunningGameApplication()
    {
        if (DateTime.UtcNow - _lastGameScan < TimeSpan.FromSeconds(2))
            return _cachedGameApplication;
        _lastGameScan = DateTime.UtcNow;

        // A second foreground game takes over the indicator. Keep the last
        // accepted game while the user visits a launcher or the dashboard.
        if(GetForegroundApplication() is { } foregroundGame)
        {
            if(foregroundGame.StartTimeUtc==null&&_cachedGameApplication is { } previous&&
                foregroundGame.ProcessId==previous.ProcessId&&foregroundGame.Application.Equals(previous.Application,StringComparison.OrdinalIgnoreCase))
                foregroundGame=foregroundGame with{StartTimeUtc=previous.StartTimeUtc,ExecutablePath=foregroundGame.ExecutablePath??previous.ExecutablePath};
            else if(_cachedGameApplication is {} known&&GameSessionClock.SameSession(known,foregroundGame))
                foregroundGame=foregroundGame with{ExecutablePath=foregroundGame.ExecutablePath??known.ExecutablePath};
            return _cachedGameApplication=foregroundGame;
        }

        if (_cachedGameApplication is { } cached)
        {
            try
            {
                using var running = Process.GetProcessById(cached.ProcessId);
                if ((running.ProcessName+".exe").Equals(cached.Application,StringComparison.OrdinalIgnoreCase) &&
                    !IsIgnoredApplication(cached.Application))
                {
                    var current=cached with{StartTimeUtc=ReadStartTimeUtc(running)??cached.StartTimeUtc,IsSuspended=IsSuspended(running)};
                    try
                    {
                        string path=running.MainModule?.FileName??string.Empty;
                        if(IsEligibleGameExecutable(cached.Application,path))return _cachedGameApplication=current with{ExecutablePath=path};
                    }
                    catch
                    {
                        if(HasSupportedCompanionProcess(running.ProcessName))return _cachedGameApplication=current;
                    }
                }
            }
            catch { }
            _cachedGameApplication = null;
        }

        return null;
    }

private static GameProcess? GetForegroundApplication()
    {
        try
        {
            var window = GetForegroundWindow();
            if (window == IntPtr.Zero) return null;
            _ = GetWindowThreadProcessId(window, out var processId);
            if (processId == 0 || processId == Environment.ProcessId) return null;
            using var process = Process.GetProcessById((int)processId);
            var application = process.ProcessName + ".exe";
            if (IsIgnoredApplication(application)) return null;
            try
            {
                var path = process.MainModule?.FileName ?? string.Empty;
                if (IsEligibleGameExecutable(application, path))
                    return new(application,(int)processId,ReadStartTimeUtc(process)){IsSuspended=IsSuspended(process),ExecutablePath=path};
            }
            catch
            {
                // Protected games can deny their executable path even though
                // a related anti-cheat bootstrap remains readable.
                return HasSupportedCompanionProcess(process.ProcessName) ? new(application,(int)processId,ReadStartTimeUtc(process)){IsSuspended=IsSuspended(process)} : null;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

private static DateTime? ReadStartTimeUtc(Process process)
    {
        try{return process.StartTime.ToUniversalTime();}
        catch{return null;}
    }

    // An in-game pause menu is not observable through Windows. Only a fully
    // OS-suspended process is treated as stopped; ordinary waits are not pauses.
    private static bool IsSuspended(Process process)
    {
        try
        {
            var threads=process.Threads.Cast<ProcessThread>().ToArray();
            try{return threads.Length>0&&threads.All(thread=>thread.ThreadState==System.Diagnostics.ThreadState.Wait&&thread.WaitReason==ThreadWaitReason.Suspended);}
            finally{foreach(var thread in threads)thread.Dispose();}
        }
        catch{return false;}
    }

private static bool HasSupportedCompanionProcess(string protectedProcessName)
    {
        Process[] processes=[];
        try
        {
            processes=Process.GetProcesses();
            foreach (var process in processes)
            {
                using (process)
                {
                    if (!AreRelatedGameProcessNames(
                            protectedProcessName, process.ProcessName))
                        continue;
                    try
                    {
                        var path = process.MainModule?.FileName ?? string.Empty;
                        if (IsEligibleGameExecutable(process.ProcessName + ".exe", path))
                            return true;
                    }
                    catch { }
                }
            }
        }
        catch { }
        finally{foreach(var process in processes)try{process.Dispose();}catch{}}
        return false;
    }

internal static bool AreRelatedGameProcessNames(string first, string second)
    {
        static string Normalize(string value)
        {
            var name = Path.GetFileNameWithoutExtension(value).ToLowerInvariant();
            foreach (var suffix in new[]
                     { "-win64-shipping", "_win64_shipping", "_shipping", "_be", "_eac", "_anticheat" })
                if (name.EndsWith(suffix, StringComparison.Ordinal))
                    name = name[..^suffix.Length];
            return name;
        }

        var left = Normalize(first);
        var right = Normalize(second);
        return left.Length >= 6 && left.Equals(right, StringComparison.Ordinal);
    }

internal static bool IsEligibleGameExecutable(string application, string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            IsIgnoredApplication(application) ||
            LauncherPathMarkers.Any(marker =>
                path.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            return false;

        return path.Contains(@"\steamapps\common\", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\XboxGames\", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\GOG Galaxy\Games\", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\Epic Games\", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\Riot Games\", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\Rockstar Games\", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\EA Games\", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\Ubisoft\", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\Battlestate Games\", StringComparison.OrdinalIgnoreCase);
    }

private static bool IsWindowsSystemPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var windows = Environment.GetFolderPath(
            Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windows)) return false;
        var normalizedWindows = Path.GetFullPath(windows)
            .TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        try
        {
            return Path.GetFullPath(path).StartsWith(
                normalizedWindows, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
}
