using System.Diagnostics;
using System.IO;
using System.IO.Pipes;

namespace Ekrano_valdymas;

internal static class IsolatedRendererWorker
{
    public static async Task RunAsync(int parentProcessId, string? pipeName,
        CancellationToken cancellationToken = default)
    {
        if (parentProcessId <= 0 || parentProcessId == Environment.ProcessId) return;

        using Mutex instanceMutex = new(
            initiallyOwned: false,
            name: $@"Local\BlakiDeck.VisualRenderer.{parentProcessId}",
            createdNew: out bool createdNew);
        if (!createdNew) return;

        Process? parent = null;
        try
        {
            parent = Process.GetProcessById(parentProcessId);
            if (!string.IsNullOrWhiteSpace(pipeName))
            {
                using CancellationTokenSource lifetime =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _ = MonitorParentAsync(parent, lifetime);
                using var pipe = new NamedPipeClientStream(".", pipeName,
                    PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(5000, lifetime.Token);
                using var reader = new StreamReader(pipe);
                using var writer = new StreamWriter(pipe) { AutoFlush = true };
                await writer.WriteLineAsync("READY".AsMemory(), lifetime.Token);
                while (!lifetime.IsCancellationRequested)
                {
                    string? command = await reader.ReadLineAsync(lifetime.Token);
                    if (command == null || command == "STOP") break;
                    if (command == "PING")
                        await writer.WriteLineAsync("PONG".AsMemory(), lifetime.Token);
                }
                return;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                parent.Refresh();
                if (parent.HasExited) break;
                await Task.Delay(250, cancellationToken);
            }
        }
        catch (ArgumentException)
        {
            // Pagrindinis procesas jau baigėsi prieš rendererio startą.
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            parent?.Dispose();
        }
    }

    private static async Task MonitorParentAsync(Process parent, CancellationTokenSource lifetime)
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                parent.Refresh();
                if (parent.HasExited) break;
                await Task.Delay(250, lifetime.Token);
            }
        }
        catch { }
        finally { lifetime.Cancel(); }
    }
}
