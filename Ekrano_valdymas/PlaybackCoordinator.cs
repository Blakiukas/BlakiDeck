using System.Threading;
using Windows.Media.Control;

namespace Ekrano_valdymas;

// All starts, including local track changes, use one process-wide critical section.
internal static class PlaybackCoordinator
{
    private static readonly SemaphoreSlim gate = new(1, 1);
    internal static async Task<bool> RunAsync(Func<Task<bool>> command)
    {
        await gate.WaitAsync();
        try { return await command(); }
        finally { gate.Release(); }
    }

    internal static async Task PauseOthersAsync(GlobalSystemMediaTransportControlsSession? target,bool keepLocal=false)
    {
        // Diagnostic playback must never control the user's real media sessions.
        if (DiagnosticState.Root != null) {if(!keepLocal)LocalPlayback.Instance.Pause();return;}
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        foreach (var session in manager.GetSessions())
        {
            if (session == target || (target != null && session.SourceAppUserModelId == target.SourceAppUserModelId)) continue;
            if (session.GetPlaybackInfo().PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) continue;
            await ConfirmPauseAsync(session.SourceAppUserModelId,async()=>await session.TryPauseAsync(),()=>session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
        }
        if (!keepLocal) LocalPlayback.Instance.Pause();
    }

    internal static async Task ConfirmPauseAsync(string name,Func<Task<bool>> pause,Func<bool> playing)
    {
        if(!await pause())throw new InvalidOperationException($"Could not pause {name}. The new source was not started.");
        for(int i=0;i<15&&playing();i++)await Task.Delay(100);
        if(playing())throw new InvalidOperationException($"{name} is still playing. The new source was not started.");
    }

    internal static async Task ObserveExternalPlaybackAsync()
    {
        if (DiagnosticState.Root != null || !LocalPlayback.Instance.Playing) return;
        await RunAsync(async () => {
            var manager=await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if(manager.GetSessions().Any(s=>s.GetPlaybackInfo().PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing))
                LocalPlayback.Instance.Pause();
            return true;
        });
    }
}
