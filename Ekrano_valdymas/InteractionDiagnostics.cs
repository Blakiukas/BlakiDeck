using System;
using System.IO;

namespace Ekrano_valdymas;

internal static class InteractionDiagnostics
{
    private static readonly object Sync = new();
    private static string? path;
    private const long MaximumLogBytes = 4 * 1024 * 1024;
    private const long HitTestLogIntervalMilliseconds = 250;
    private static long nextHitTestLogTick;
    private static int suppressedHitTests;

    internal static void RecordFailure(string category, object? exception)
    {
        try
        {
            string directory=Path.Combine(DiagnosticState.LocalApplicationData,"Ekrano_valdymas","diagnostics");
            Directory.CreateDirectory(directory);
            string file=Path.Combine(directory,"failures.log");
            lock(Sync)
            {
                if(File.Exists(file)&&new FileInfo(file).Length>=MaximumLogBytes)File.Move(file,file+".previous",true);
                File.AppendAllText(file,$"{DateTimeOffset.Now:O} PID={Environment.ProcessId} {category} {exception}{Environment.NewLine}");
            }
        }
        catch { }
    }

    internal static bool Enabled => path != null;
    internal static string? OutputDirectory => path==null?null:Path.GetDirectoryName(path);

    internal static void Enable(string outputPath)
    {
        try
        {
            string target = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target) && new FileInfo(target).Length >= MaximumLogBytes)
            {
                string previous = target + ".previous";
                File.Move(target, previous, true);
            }
            lock (Sync)
            {
                path = target;
                nextHitTestLogTick = 0;
                suppressedHitTests = 0;
            }
            Log($"interaction-diagnostics enabled path={target}");
        }
        catch
        {
            path = null;
        }
    }

    // Native hit tests can arrive several times per mouse update. Sample their
    // successful results without blocking the input thread on a file per event.
    // Resize, drag/drop and failure diagnostics retain their ordinary logging.
    internal static void LogHitTest(string message)
    {
        if (!Enabled) return;
        lock (Sync)
        {
            long now = Environment.TickCount64;
            if (now < nextHitTestLogTick)
            {
                suppressedHitTests++;
                return;
            }
            nextHitTestLogTick = now + HitTestLogIntervalMilliseconds;
            int skipped = suppressedHitTests;
            suppressedHitTests = 0;
            Log(skipped > 0 ? $"{message} suppressedHitTests={skipped}" : message);
        }
    }

    internal static void Log(string message)
    {
        string? target = path;
        if (target == null) return;
        try
        {
            lock (Sync)
            {
                if(File.Exists(target)&&new FileInfo(target).Length>=MaximumLogBytes)
                    File.Move(target,target+".previous",true);
                File.AppendAllText(target,
                    $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Diagnostics must never change product behavior.
        }
    }
}
