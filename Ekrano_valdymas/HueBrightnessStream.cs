namespace Ekrano_valdymas;

// One serial sender per room. New input replaces only unsent values, never an
// in-flight request; continuous dragging therefore cannot postpone every send.
internal sealed class HueBrightnessStream
{
    private readonly Func<double, CancellationToken, Task> send;
    private readonly CancellationTokenSource cancellation = new();
    private double? latest;
    internal HueBrightnessStream(Func<double, CancellationToken, Task> send) => this.send = send;
    internal void Set(double value) => latest = value;
    internal void Cancel() => cancellation.Cancel();
    internal async Task RunAsync()
    {
        try
        {
            while (latest is double value)
            {
                latest = null;
                cancellation.Token.ThrowIfCancellationRequested();
                await send(value, cancellation.Token);
                await Task.Delay(250, cancellation.Token);
            }
        }
        finally { cancellation.Dispose(); }
    }
}
