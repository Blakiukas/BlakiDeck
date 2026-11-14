namespace Ekrano_valdymas;

// Session discovery/COM reads must not block the WPF needle-rendering frame.
// Dispose stops the current worker; a later visible session may start a new one.
internal sealed class BackgroundMusicSourceMeter : IDisposable
{
    private readonly object gate = new();
    private CancellationTokenSource? cancellation;
    private string[] names = [];
    private double left, right;
    private long sampleTime;
    internal void Read(string[] processes, out double l, out double r)
    {
        lock (gate)
        {
            if (!names.SequenceEqual(processes, StringComparer.OrdinalIgnoreCase))
            { names = processes.ToArray(); left = right = 0; }
            if (cancellation == null)
            {
                cancellation = new();
                var current = cancellation;
                _ = Task.Run(() => Poll(current));
            }
            bool fresh = Environment.TickCount64 - sampleTime < 250;
            l = fresh ? left : 0; r = fresh ? right : 0;
        }
    }
    private async Task Poll(CancellationTokenSource current)
    {
        using var meter = new MusicSourceMeter();
        try
        {
            while (!current.IsCancellationRequested)
            {
                string[] selected; lock (gate) selected = names;
                meter.Read(selected, out double l, out double r);
                lock (gate)
                    if (ReferenceEquals(cancellation, current) && names.SequenceEqual(selected))
                    { left = l; right = r; sampleTime = Environment.TickCount64; }
                await Task.Delay(20, current.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally { current.Dispose(); }
    }
    public void Dispose()
    {
        lock (gate)
        { cancellation?.Cancel(); cancellation = null; left = right = 0; sampleTime = 0; }
    }
}
