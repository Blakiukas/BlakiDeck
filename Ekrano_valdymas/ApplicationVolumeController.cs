using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System.Diagnostics;

namespace Ekrano_valdymas;

internal static class ApplicationVolumeController
{
    public static float? GetVolume(string[] processNames)
    {
        float? result = null;
        WithSessions(processNames, session =>
        {
            result ??= session.SimpleAudioVolume.Volume;
            // Prefer a currently playing session over an old, inactive one.
            if (session.State != AudioSessionState.AudioSessionStateActive) return false;
            result = session.SimpleAudioVolume.Volume;
            return true;
        });
        return result;
    }

    public static void SetVolume(string[] processNames, float level) =>
        WithSessions(processNames, session =>
        {
            session.SimpleAudioVolume.Volume = Math.Clamp(level, 0, 1);
            return false;
        });

    public static int? FindAudioProcessId(string[] processNames)
    {
        int? result = null;
        WithSessions(processNames, session =>
        {
            int processId = checked((int)session.GetProcessID);
            result ??= processId;
            if (session.State != AudioSessionState.AudioSessionStateActive) return false;
            result = processId;
            return true;
        });
        return result;
    }

    private static void WithSessions(string[] processNames, Func<AudioSessionControl, bool> action)
    {
        if (processNames.Length == 0) return;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var device in devices)
            {
                using (device)
                {
                    try
                    {
                        var sessions = device.AudioSessionManager.Sessions;
                        for (int i = 0; i < sessions.Count; i++)
                        {
                            using var session = sessions[i];
                            try
                            {
                                uint processId = session.GetProcessID;
                                if (processId == 0) continue;
                                using var process = Process.GetProcessById(checked((int)processId));
                                if (processNames.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase)
                                    && action(session)) return;
                            }
                            catch { /* A process/session can disappear during enumeration. */ }
                        }
                    }
                    catch { /* A disconnected endpoint must not hide the other outputs. */ }
                }
            }
        }
        catch { /* No active output/audio service: leave the source unavailable. */ }
    }
}
