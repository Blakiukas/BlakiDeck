using System.Diagnostics;
using System.IO;

namespace Ekrano_valdymas;
// Monotonic play time; stopped games retain their display for five minutes.
internal sealed class GameSessionClock
{
    internal static readonly TimeSpan Retention=TimeSpan.FromMinutes(5);
    private readonly Func<long> timestamp;
    private readonly Func<DateTime> utcNow;
    private GameApplicationDetector.GameProcess? game;
    private TimeSpan elapsedAtAnchor;
    private long anchor,stoppedAt;
    private bool running,hasStarted,ageCalibrationAllowed=true;
    internal GameSessionClock():this(Stopwatch.GetTimestamp,()=>DateTime.UtcNow){}
    internal GameSessionClock(Func<long> timestamp,Func<DateTime> utcNow){this.timestamp=timestamp;this.utcNow=utcNow;}
    private TimeSpan Since(long then)=>TimeSpan.FromSeconds(Math.Max(0,timestamp()-then)/(double)Stopwatch.Frequency);
    private void Expire(){if(game!=null&&!running&&Since(stoppedAt)>=Retention){game=null;elapsedAtAnchor=TimeSpan.Zero;}}
    internal GameApplicationDetector.GameProcess? Process{get{Expire();return game;}}
    internal bool IsRunning=>running;
    internal TimeSpan? Elapsed{get{Expire();return game==null?null:elapsedAtAnchor+(running?Since(anchor):TimeSpan.Zero);}}
    internal static bool SameSession(GameApplicationDetector.GameProcess? first,GameApplicationDetector.GameProcess? second)
        =>SameGame(first,second)&&first!.ProcessId==second!.ProcessId
            &&(!first.StartTimeUtc.HasValue||!second.StartTimeUtc.HasValue||first.StartTimeUtc==second.StartTimeUtc);
    internal static bool SameGame(GameApplicationDetector.GameProcess? first,GameApplicationDetector.GameProcess? second)
    {
        if(first==null||second==null)return false;
        bool sameName=first.Application.Equals(second.Application,StringComparison.OrdinalIgnoreCase);
        // Protected games can first expose their anti-cheat bootstrap, then the
        // main process. These are one game, not two sessions on every relaunch.
        if(!sameName&&!GameApplicationDetector.AreRelatedGameProcessNames(first.Application,second.Application))return false;
        if(string.IsNullOrEmpty(first.ExecutablePath)||string.IsNullOrEmpty(second.ExecutablePath))return true;
        if(sameName)return first.ExecutablePath.Equals(second.ExecutablePath,StringComparison.OrdinalIgnoreCase);
        // Do not merge distinct installations merely because their names match.
        return string.Equals(Path.GetDirectoryName(first.ExecutablePath),Path.GetDirectoryName(second.ExecutablePath),StringComparison.OrdinalIgnoreCase);
    }
    internal void Reset(){game=null;elapsedAtAnchor=TimeSpan.Zero;running=false;hasStarted=false;ageCalibrationAllowed=true;anchor=stoppedAt=0;}
    internal void Start(GameApplicationDetector.GameProcess? next)
    {
        Expire();
        if(next==null||next.IsSuspended)
        {
            if(running){elapsedAtAnchor=Elapsed??TimeSpan.Zero;running=false;stoppedAt=timestamp();ageCalibrationAllowed=false;}
            // A suspended different game replaces the previous display immediately.
            if(next!=null&&!SameGame(game,next))
            {game=next;elapsedAtAnchor=TimeSpan.Zero;stoppedAt=timestamp();hasStarted=true;ageCalibrationAllowed=false;}
            return;
        }
        if(running&&SameSession(game,next))
        {
            if(ageCalibrationAllowed&&game!.StartTimeUtc==null&&next.StartTimeUtc.HasValue)
            {elapsedAtAnchor=TimeSpan.FromTicks(Math.Max((Elapsed??TimeSpan.Zero).Ticks,InitialElapsed(next.StartTimeUtc).Ticks));anchor=timestamp();}
            game=next with{StartTimeUtc=next.StartTimeUtc??game!.StartTimeUtc,ExecutablePath=next.ExecutablePath??game!.ExecutablePath};return;
        }
        // A close/relaunch can happen between two detector samples. A new PID
        // of the same game still keeps its session; another game always resets.
        bool resume=SameGame(game,next);
        if(resume&&running){elapsedAtAnchor=Elapsed??TimeSpan.Zero;ageCalibrationAllowed=false;}
        if(resume&&SameSession(game,next))next=next with{StartTimeUtc=next.StartTimeUtc??game!.StartTimeUtc,ExecutablePath=next.ExecutablePath??game!.ExecutablePath};
        if(hasStarted&&!resume)ageCalibrationAllowed=false;
        if(!resume)elapsedAtAnchor=hasStarted?TimeSpan.Zero:InitialElapsed(next.StartTimeUtc);
        game=next;anchor=timestamp();running=true;hasStarted=true;
    }
    private TimeSpan InitialElapsed(DateTime? start)
        =>start.HasValue&&utcNow()>start.Value?utcNow()-start.Value:TimeSpan.Zero;
}
