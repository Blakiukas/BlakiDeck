using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Ekrano_valdymas;

// One shared system-output capture for both contour slots. No microphone or player dependency.
internal sealed class ContourSystemAudio
{
    internal static ContourSystemAudio Shared { get; } = new();
    internal ContourAudioAnalyzer Analyzer { get; } = new();
    private readonly HashSet<object> consumers = new();
    private readonly object gate = new();
    private Task? worker;
    private bool active;
    internal string Status { get; private set; } = "Inactive";

    internal void SetActive(object consumer, bool value)
    {
        lock (gate)
        {
            if (value) consumers.Add(consumer); else consumers.Remove(consumer);
            active = consumers.Count > 0;
            if (active && worker is null) worker = Task.Run(Run);
        }
    }

    private async Task Run()
    {
        WasapiRecorder? recorder = null;
        MMDevice? device = null;
        bool stopped = false;
        void OnData(ReadOnlySpan<byte> data, AudioClientBufferFlags flags, long position, long qpc)
            => Analyzer.Feed(data, (flags & AudioClientBufferFlags.Silent) != 0);
        void OnStopped(object? sender, StoppedEventArgs args) => stopped = true;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            while (true)
            {
                lock (gate) if (!active) break;
                try
                {
                    using var current = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    if (recorder is null || stopped || current.ID != device?.ID)
                    {
                        if (recorder != null)
                        {
                            recorder.DataAvailable -= OnData; recorder.RecordingStopped -= OnStopped;
                            recorder.StopRecording(); recorder.Dispose(); recorder = null;
                        }
                        device?.Dispose(); device = enumerator.GetDevice(current.ID);
                        recorder = await new WasapiRecorderBuilder().WithDevice(device).WithLoopbackCapture()
                            .WithFormat(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2))
                            .WithBufferLength(20).WithMmcssThreadPriority("Audio").BuildAsync();
                        recorder.DataAvailable += OnData; recorder.RecordingStopped += OnStopped;
                        stopped = false; recorder.StartRecording();
                        Status = "System output: " + device.FriendlyName;
                    }
                }
                catch (Exception ex) { Status = "Audio unavailable: " + ex.Message; stopped = true; }
                await Task.Delay(500);
            }
        }
        catch (Exception ex) { Status = "Audio unavailable: " + ex.Message; await Task.Delay(1000); }
        finally
        {
            if (recorder != null)
            {
                recorder.DataAvailable -= OnData; recorder.RecordingStopped -= OnStopped;
                try { recorder.StopRecording(); } catch { }
                recorder.Dispose();
            }
            device?.Dispose();
            lock (gate)
            {
                worker = null;
                if (active) worker = Task.Run(Run);
                else Status = "Inactive";
            }
        }
    }
}
