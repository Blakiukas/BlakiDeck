using System.Diagnostics;

namespace Ekrano_valdymas;

// Uses the existing interaction-diagnostics switch; fast callbacks do not log.
internal readonly struct UiCallbackTiming : IDisposable
{
    private readonly string name;
    private readonly long start;
    internal UiCallbackTiming(string name)
    {
        this.name = name;
        start = InteractionDiagnostics.Enabled ? Stopwatch.GetTimestamp() : 0;
    }
    public void Dispose()
    {
        if (start == 0) return;
        double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (ms >= 5) InteractionDiagnostics.Log($"callback {name} durationMs={ms:F3} thread={Environment.CurrentManagedThreadId}");
    }
}
