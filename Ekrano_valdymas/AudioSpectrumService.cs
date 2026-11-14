using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace Ekrano_valdymas;

public sealed class AudioSpectrumService : IDisposable
{
    private const int FftLength = 4096;
    private static readonly WaveFormat CaptureFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
    private readonly Complex[] fft = new Complex[FftLength];
    private readonly float[] spectrum = new float[FftLength / 2];
    private readonly object gate = new();
    private readonly object lifecycleGate = new();
    private readonly HashSet<object> activeConsumers = new();
    private readonly Dictionary<WasapiRecorder,CaptureDataAvailableHandler> recorderCallbacks = new();
    private string[] targetProcessNames = ["Spotify"];
    private WasapiRecorder? recorder;
    private int recorderProcessId, position;
    private int targetGeneration;
    private long nextStartAttemptTicks;
    private volatile bool analyzing;
    private bool startScheduled, disposed;
    private int recorderCleanupPending, capturePauseCount;
    private string captureProblem="", lastCaptureFailureKey="";
    private double stereoLeft;
    private double stereoRight;
    private double rmsLeft, rmsRight, peakLeft, peakRight;
    private readonly ClassicVuMeter classicVu = new();
    internal string CaptureProblem {get{lock(lifecycleGate)return analyzing?captureProblem:"";}}
    internal string CaptureStatus
    {
        get
        {
            if(!analyzing)return "VU: inactive";
            lock(lifecycleGate)
            {
                if(captureProblem.Length>0)return captureProblem;
                if(recorder==null)return "VU: audio source unavailable — start playback in the selected player";
            }
            lock(gate)return Environment.TickCount64-lastPcmTicks<200
                ? "VU: receiving audio" : "VU: connected — no recent audio";
        }
    }
    internal (double Left, double Right) ReadClassicVu()
    {
        // The player can open before its audio session exists. Retry through the
        // same throttled start path as the other visualizers, without a tab switch.
        if (analyzing && recorder == null) EnsureCaptureStarted();
        // Missing capture is unavailable input, not an indefinitely held note.
        // This guard never advances the mechanical model on the UI clock.
        lock (gate) return Environment.TickCount64 - lastPcmTicks < 200 ? classicVu.Read() : (0, 0);
    }
    internal void ResetClassicVu() => classicVu.Reset();
    internal ClassicVuMeter.State CaptureClassicVuState()
    {
        lock (gate) return Environment.TickCount64 - lastPcmTicks < 200 ? classicVu.CaptureState() : default;
    }
    private long lastPcmTicks;
    internal readonly record struct PcmTiming(long Sequence, long Timestamp, int Frames);
    private PcmTiming pcmTiming;
    internal PcmTiming ReadPcmTiming() { lock (gate) return pcmTiming; }

    internal void CopyStereoPcm(out double left, out double right, out double peakL, out double peakR)
    {
        if (analyzing && recorder == null) EnsureCaptureStarted();
        lock (gate)
        {
            bool fresh = Environment.TickCount64 - lastPcmTicks < 200;
            left = fresh ? rmsLeft : 0; right = fresh ? rmsRight : 0;
            peakL = fresh ? peakLeft : 0; peakR = fresh ? peakRight : 0;
        }
    }

    public void SetTargetProcessNames(string[] processNames)
    {
        string[] names = processNames.Length == 0 ? ["Spotify"] : processNames.ToArray();
        bool changed;
        WasapiRecorder? obsolete=null;
        lock (lifecycleGate)
        {
            changed = !targetProcessNames.SequenceEqual(names, StringComparer.OrdinalIgnoreCase);
            targetProcessNames = names;
            if (changed) {targetGeneration++;nextStartAttemptTicks=0;captureProblem="";obsolete=recorder;recorder=null;recorderProcessId=0;}
        }
        QueueDisposeRecorder(obsolete);
        if(changed)ClearSpectrum();
        if (changed && analyzing) EnsureCaptureStarted();
    }

    public void SetActive(bool active)
    {
        WasapiRecorder? obsolete=null;
        bool changed;
        lock(lifecycleGate)
        {
            active&=!disposed;
            changed=analyzing!=active;
            analyzing=active;
            if(!active&&(changed||recorder!=null))
            {
                // Invalidate an in-flight builder before detaching its native
                // capture. Returning to a consumer starts from fresh PCM.
                targetGeneration++;nextStartAttemptTicks=0;
                obsolete=recorder;recorder=null;recorderProcessId=0;
                if(changed)capturePauseCount++;
            }
        }
        QueueDisposeRecorder(obsolete);
        if(active)EnsureCaptureStarted();
        else if(changed||obsolete!=null)ClearSpectrum();
    }

    public void SetActive(object consumer, bool active)
    {
        lock (activeConsumers)
        {
            lock(lifecycleGate)if(disposed)return;
            if (active) activeConsumers.Add(consumer); else activeConsumers.Remove(consumer);
            SetActive(activeConsumers.Count > 0);
        }
    }

    public double[] GetLevels(int count)
    {
        if (analyzing && recorder == null) EnsureCaptureStarted();
        var result = new double[count];
        CopyLevels(result);
        return result;
    }

    public void CopyLevels(double[] destination)
    {
        if (analyzing && recorder == null) EnsureCaptureStarted();
        lock (gate)
        {
            const int rate = 44100;
            const double low = 45;
            const double high = 18000;
            for (int band = 0; band < destination.Length; band++)
            {
                double f0 = low * Math.Pow(high / low, band / (double)destination.Length);
                double f1 = low * Math.Pow(high / low, (band + 1d) / destination.Length);
                int start = Math.Max(1, (int)(f0 * FftLength / rate));
                int end = Math.Min(spectrum.Length,
                    Math.Max(start + 1, (int)Math.Ceiling(f1 * FftLength / rate)));
                float peak = 0;
                for (int i = start; i < end; i++) peak = Math.Max(peak, spectrum[i]);
                destination[band] = Math.Clamp(
                    (20 * Math.Log10(Math.Max(peak, .000001f)) + 76) / 64, 0, 1);
            }
        }
    }

    public void CopyStereoLevels(out double left, out double right)
    {
        if (analyzing && recorder == null) EnsureCaptureStarted();
        lock (gate)
        {
            left = stereoLeft;
            right = stereoRight;
        }
    }

    private void EnsureCaptureStarted()
    {
        if(DiagnosticState.SuppressAudioCapture)return;
        lock (lifecycleGate)
        {
            long now = Environment.TickCount64;
            if (disposed || !analyzing || recorder!=null || startScheduled || now < nextStartAttemptTicks) return;
            startScheduled = true;
            nextStartAttemptTicks = now + 750;
        }
        _ = Task.Run(StartProcessCaptureAsync);
    }

    private async Task StartProcessCaptureAsync()
    {
        WasapiRecorder? replacement = null;
        WasapiRecorder? previous = null;
        bool replacementStarted = false;
        int generation=-1;
        try
        {
            string[] names;
            lock (lifecycleGate) {if(disposed||!analyzing)return;names=targetProcessNames.ToArray();generation=targetGeneration;}
            int? processId = ApplicationVolumeController.FindAudioProcessId(names);
            if (processId is not > 0) return;

            lock (lifecycleGate)
                if (disposed || !analyzing || generation!=targetGeneration || recorderProcessId == processId.Value && recorder != null) return;

            replacement = await new WasapiRecorderBuilder()
                .WithProcessLoopback((uint)processId.Value, ProcessLoopbackMode.IncludeTargetProcessTree)
                .WithFormat(CaptureFormat)
                .WithBufferLength(40)
                .WithMmcssThreadPriority("Audio")
                .BuildAsync();
            CaptureDataAvailableHandler data=(buffer,flags,devicePosition,qpcPosition)=>OnData(generation,buffer,flags,devicePosition,qpcPosition);
            lock(lifecycleGate)recorderCallbacks[replacement]=data;
            replacement.DataAvailable += data;
            replacement.RecordingStopped += OnRecordingStopped;
            lock(lifecycleGate)if(disposed||!analyzing||generation!=targetGeneration)return;
            replacement.StartRecording();
            replacementStarted = true;

            lock (lifecycleGate)
            {
                if (disposed || !analyzing || generation!=targetGeneration) return;
                previous = recorder;
                recorder = replacement;
                recorderProcessId = processId.Value;
                captureProblem="";lastCaptureFailureKey="";
                replacement = null;
            }
            QueueDisposeRecorder(previous);
        }
        catch(Exception error)
        {
            bool current;
            lock(lifecycleGate)current=!disposed&&generation==targetGeneration;
            if(current){ReportCaptureFailure(error,generation);ClearSpectrum();}
        }
        finally
        {
            // Releasing an obsolete native recorder must not hold the start
            // reservation for the next active consumer.
            QueueDisposeRecorder(replacement, replacementStarted);
            bool retarget;
            lock (lifecycleGate) {startScheduled=false;retarget=!disposed&&analyzing&&generation!=targetGeneration;}
            if(retarget)EnsureCaptureStarted();
        }
    }

    private void ReportCaptureFailure(Exception error,int generation)
    {
        bool record;
        lock(lifecycleGate)
        {
            if(disposed||generation!=targetGeneration)return;
            captureProblem=MediaSupport.CaptureFailure(error);
            nextStartAttemptTicks=Environment.TickCount64+CaptureRetryDelayMilliseconds(error);
            string key=error.GetType().FullName+":"+error.HResult;
            record=key!=lastCaptureFailureKey;lastCaptureFailureKey=key;
        }
        if(record)InteractionDiagnostics.RecordFailure("Player audio capture",error);
    }
    internal static int CaptureRetryDelayMilliseconds(Exception error)=>
        MediaSupport.IsCaptureUnsupported(error)?30000:5000;

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        WasapiRecorder? obsolete;
        int generation;
        lock (lifecycleGate)
        {
            if (!ReferenceEquals(sender, recorder)) return;
            generation=targetGeneration;
            obsolete=recorder;
            recorder = null;
            recorderProcessId = 0;
            nextStartAttemptTicks = Environment.TickCount64 + (e.Exception==null?750:CaptureRetryDelayMilliseconds(e.Exception));
        }
        if(e.Exception!=null)ReportCaptureFailure(e.Exception,generation);
        QueueDisposeRecorder(obsolete);
        ClearSpectrum();
        if (analyzing) EnsureCaptureStarted();
    }

    private void OnData(int generation,ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags,
        long devicePosition, long qpcPosition)
    {
        lock(gate)
        {
            if (!analyzing||generation!=Volatile.Read(ref targetGeneration)) return;
            bool silent = (flags & AudioClientBufferFlags.Silent) != 0;
            AnalyzePcm(buffer, silent);
        }
    }

    // Shared by live capture and non-persistent recorded-audio diagnostics.
    internal void AnalyzePcm(ReadOnlySpan<byte> buffer, bool silent = false)
    {
        lock(gate)
        {
        if (Environment.TickCount64 - lastPcmTicks >= 200) {classicVu.Reset();}
        classicVu.Process(buffer, silent);
        const int frameBytes = sizeof(float) * 2;
        double leftSquares = 0;
        double rightSquares = 0;
        double leftPeak = 0;
        double rightPeak = 0;
        int frameCount = 0;
        for (int offset = 0; offset + frameBytes <= buffer.Length; offset += frameBytes)
        {
            float leftSample = silent ? 0 : BitConverter.ToSingle(buffer.Slice(offset, sizeof(float)));
            float rightSample = silent ? 0 : BitConverter.ToSingle(buffer.Slice(offset + sizeof(float), sizeof(float)));
            float sample = (leftSample + rightSample) * .5f;
            leftSquares += leftSample * leftSample;
            rightSquares += rightSample * rightSample;
            leftPeak = Math.Max(leftPeak, Math.Abs(leftSample));
            rightPeak = Math.Max(rightPeak, Math.Abs(rightSample));
            frameCount++;
            fft[position].X = (float)(sample * FastFourierTransform.HammingWindow(position, FftLength));
            fft[position].Y = 0;
            if (++position < FftLength) continue;
            position = 0;
            FastFourierTransform.FFT(true, 12, fft);
            lock (gate)
                for (int i = 0; i < spectrum.Length; i++)
                {
                    spectrum[i] = MathF.Sqrt(fft[i].X * fft[i].X + fft[i].Y * fft[i].Y);
                }
        }
        if (frameCount > 0)
        {
            double leftRms = Math.Sqrt(leftSquares / frameCount);
            double rightRms = Math.Sqrt(rightSquares / frameCount);
            lock (gate)
            {
                rmsLeft = leftRms; rmsRight = rightRms;
                peakLeft = leftPeak; peakRight = rightPeak;
                lastPcmTicks = Environment.TickCount64;
                pcmTiming = new(pcmTiming.Sequence + 1, System.Diagnostics.Stopwatch.GetTimestamp(), frameCount);
                stereoLeft = Math.Clamp(leftRms * .78 + leftPeak * .22, 0, 1);
                stereoRight = Math.Clamp(rightRms * .78 + rightPeak * .22, 0, 1);
            }
        }
        }
    }

    private void ClearSpectrum()
    {
        lock (gate)
        {
            classicVu.Reset();
            Array.Clear(spectrum);
            position = 0;
            stereoLeft = stereoRight = 0;
            rmsLeft = rmsRight = peakLeft = peakRight = 0;
            lastPcmTicks = 0;
            pcmTiming = new(pcmTiming.Sequence,0,0);
        }
    }

    private void QueueDisposeRecorder(WasapiRecorder? value, bool started = true)
    {
        if(value==null)return;
        // Detach callbacks before a new consumer can start another recorder.
        // Stop/Dispose may wait for native audio work, so keep it off the UI.
        DetachRecorderCallbacks(value);
        lock(lifecycleGate)recorderCleanupPending++;
        _=Task.Run(async ()=>
        {
            try
            {
                // NAudio 3 preview.19 starts its capture thread asynchronously.
                // Stop during Starting can be overwritten by that thread's
                // Capturing assignment; Dispose then waits forever in Join.
                // Wait only for recorders whose StartRecording returned: a
                // failed initialization can leave Starting without any thread.
                while(started && value.CaptureState == CaptureState.Starting)
                    await Task.Delay(5).ConfigureAwait(false);
                DisposeRecorder(value);
            }
            finally{lock(lifecycleGate)recorderCleanupPending--;}
        });
    }

    internal object ResourceStateForAudit()
    {
        lock(activeConsumers)lock(lifecycleGate)return new
        {
            analyzing,consumers=activeConsumers.Count,captureConnected=recorder!=null,
            startScheduled,recorderCleanupPending,capturePauseCount,targetGeneration,disposed,callbackCount=recorderCallbacks.Count
        };
    }

    private void DisposeRecorder(WasapiRecorder? value)
    {
        if (value == null) return;
        DetachRecorderCallbacks(value);
        try { value.StopRecording(); } catch { }
        try { value.Dispose(); }
        catch(Exception error) { InteractionDiagnostics.RecordFailure("Audio capture release",error); }
    }

    private void DetachRecorderCallbacks(WasapiRecorder value)
    {
        CaptureDataAvailableHandler? data;
        lock(lifecycleGate)recorderCallbacks.Remove(value,out data);
        if(data!=null)value.DataAvailable-=data;
        value.RecordingStopped-=OnRecordingStopped;
    }

    public void Dispose()
    {
        WasapiRecorder? value;
        lock (lifecycleGate)
        {
            if (disposed) return;
            disposed = true;
            analyzing=false;targetGeneration++;
            value = recorder;
            recorder = null;
            recorderProcessId = 0;
        }
        lock(activeConsumers)activeConsumers.Clear();
        QueueDisposeRecorder(value);
        ClearSpectrum();
    }
}
