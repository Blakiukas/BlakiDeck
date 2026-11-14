using System.Diagnostics;
using System.IO;
using System.IO.Pipes;

namespace Ekrano_valdymas;

internal sealed class RendererProcessHost : IAsyncDisposable
{
    private readonly NamedPipeServerStream pipe;
    private readonly StreamReader reader;
    private readonly StreamWriter writer;
    private readonly Process process;

    private RendererProcessHost(NamedPipeServerStream pipe, Process process)
    {
        this.pipe = pipe;
        this.process = process;
        reader = new StreamReader(pipe);
        writer = new StreamWriter(pipe);
    }

    public static async Task<RendererProcessHost> StartAsync(CancellationToken cancellationToken = default)
    {
        string pipeName = $"BlakiDeck.VisualRenderer.{Environment.ProcessId}.{Guid.NewGuid():N}";
        var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException(
            "BlakiDeck executable path is unavailable.");
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--visual-renderer");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        startInfo.ArgumentList.Add(pipeName);
        Process? process = Process.Start(startInfo);
        if (process == null)
        {
            pipe.Dispose();
            throw new InvalidOperationException("Could not start the renderer process.");
        }

        var host = new RendererProcessHost(pipe, process);
        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await pipe.WaitForConnectionAsync(timeout.Token);
            host.writer.AutoFlush = true;
            string? ready = await host.reader.ReadLineAsync(timeout.Token);
            if (!string.Equals(ready, "READY", StringComparison.Ordinal))
                throw new InvalidDataException("Renderer did not complete its handshake.");
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        await writer.WriteLineAsync("PING".AsMemory(), cancellationToken);
        string? response = await reader.ReadLineAsync(cancellationToken);
        return string.Equals(response, "PONG", StringComparison.Ordinal);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (pipe.IsConnected) await writer.WriteLineAsync("STOP");
        }
        catch { }

        try
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
            await process.WaitForExitAsync(timeout.Token);
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        }
        writer.Dispose();
        reader.Dispose();
        pipe.Dispose();
        process.Dispose();
    }
}
