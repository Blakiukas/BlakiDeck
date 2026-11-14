using NAudio.Dsp;
using NAudio.Wave;

namespace Hud1920x480;

public sealed class AudioSpectrumService : IDisposable
{
    private const int EqualizerFftLength = 4096;
    private const int EqualizerFftPower = 12;
    // 8192 samples retain 5.38 Hz/bin resolution at 44.1 kHz, giving the
    // 18..140 Hz visualizer band 22.7 independent bins. The 1024-sample hop
    // produces a new analysis every 23.2 ms. Together with the 40 ms UI poll,
    // this bounds the analysis age at about 156 ms instead of 319 ms for the
    // previous 16384/4096 path.
    private const int VisualizerFftLength = 8192;
    private const int VisualizerFftPower = 13;
    private const int VisualizerFftHopLength = 1024;
    private WasapiLoopbackCapture? _capture;
    private readonly Complex[] _equalizerFftBuffer =
        new Complex[EqualizerFftLength];
    private readonly float[] _latestEqualizerSpectrum =
        new float[EqualizerFftLength / 2];
    private readonly Complex[] _visualizerFftBuffer =
        new Complex[VisualizerFftLength];
    private readonly float[] _latestVisualizerSpectrum =
        new float[VisualizerFftLength / 2];
    private readonly double[] _visualizerWebSpectrum = new double[64];
    private readonly float[] _visualizerSampleRing =
        new float[VisualizerFftLength];
    private readonly object _sync = new();
    private int _equalizerFftPosition;
    private int _visualizerSampleWritePosition;
    private int _visualizerSampleCount;
    private int _visualizerSamplesSinceFft;
    private bool _running;
    private int _sampleRate = 48000;
    private long _dataCallbackCount;
    private long _processedFrameCount;
    private long _visualizerFftCount;
    private string _captureFormat = "uninitialized";
    private double _visualizerInputGain = 1;
    private long _lastSpectrumTick;
    private long _gainRevision = -1;
    private long _lastPacketTick;
    internal bool HasFreshSpectrum => _lastSpectrumTick != 0 && Environment.TickCount64 - _lastSpectrumTick < 250;
    public bool IsActive => _running;

    public AudioSpectrumService() { }

    public void SetActive(bool active)
    {
        if (active == _running) return;
        try
        {
            if (active)
            {
                DisposeCapture();
                var capture = new WasapiLoopbackCapture();
                capture.DataAvailable += OnDataAvailable;
                capture.RecordingStopped += OnRecordingStopped;
                _capture = capture;
                _sampleRate = Math.Max(1, capture.WaveFormat.SampleRate);
                _captureFormat = capture.WaveFormat.ToString();
                capture.StartRecording();
                _running = true;
            }
            else
            {
                _running = false;
                DisposeCapture();
                lock (_sync)
                {
                    Array.Clear(_latestEqualizerSpectrum);
                    Array.Clear(_latestVisualizerSpectrum);
                    Array.Clear(_visualizerWebSpectrum);
                }
                Array.Clear(_visualizerSampleRing);
                _visualizerInputGain = 1;
                _equalizerFftPosition = 0;
                _visualizerSampleWritePosition = 0;
                _visualizerSampleCount = 0;
                _visualizerSamplesSinceFft = 0;
                Interlocked.Exchange(ref _visualizerFftCount, 0);
                _lastSpectrumTick = 0;
                _gainRevision = -1;
            }
        }
        catch
        {
            _running = false;
            DisposeCapture();
        }
    }

    public double[] GetLevels(int count)
    {
        var result = new double[count];
        lock (_sync)
        {
            var sampleRate = Math.Max(1, _sampleRate);
            const double lowestFrequency = 45;
            var highestFrequency = Math.Min(18000, sampleRate * 0.48);
            for (var band = 0; band < count; band++)
            {
                var startFrequency = lowestFrequency * Math.Pow(
                    highestFrequency / lowestFrequency, band / (double)count);
                var endFrequency = lowestFrequency * Math.Pow(
                    highestFrequency / lowestFrequency, (band + 1d) / count);
                var start = Math.Max(1,
                    (int)Math.Floor(startFrequency * EqualizerFftLength /
                                    sampleRate));
                var end = Math.Max(start + 1,
                    (int)Math.Ceiling(endFrequency * EqualizerFftLength /
                                     sampleRate));
                end = Math.Min(end, _latestEqualizerSpectrum.Length);

                var peak = 0f;
                for (var bin = start; bin < end; bin++)
                    peak = Math.Max(peak, _latestEqualizerSpectrum[bin]);

                var decibels = 20 * Math.Log10(Math.Max(peak, 0.000001f));
                result[band] = Math.Clamp((decibels + 76) / 64, 0, 1);
            }
        }
        return result;
    }

    public double[] GetLinearFrequencyLevels(
        int count, double lowestFrequency, double highestFrequency,
        double smoothingTimeConstant = 0.243289)
    {
        var result = new double[Math.Max(0, count)];
        if (result.Length == 0) return result;
        lock (_sync)
        {
            if (!HasFreshSpectrum) { Array.Clear(_visualizerWebSpectrum); return result; }
            var sampleRate = Math.Max(1, _sampleRate);
            var nyquist = sampleRate / 2d;
            var low = Math.Clamp(lowestFrequency, 1, nyquist);
            var high = Math.Clamp(highestFrequency, low, nyquist);
            var smoothing = Math.Clamp(smoothingTimeConstant, 0, 1);
            var framePeak = 0d;
            for (var index = 0; index < result.Length; index++)
            {
                var amount = result.Length == 1
                    ? 0d
                    : index / (double)(result.Length - 1);
                var frequency = low + (high - low) * amount;
                var exactBin = frequency * VisualizerFftLength / sampleRate;
                var lowerBin = Math.Clamp(
                    (int)Math.Floor(exactBin), 1,
                    _latestVisualizerSpectrum.Length - 1);
                var upperBin = Math.Clamp(
                    lowerBin + 1, 1,
                    _latestVisualizerSpectrum.Length - 1);
                var fraction = exactBin - Math.Floor(exactBin);
                var magnitude =
                    _latestVisualizerSpectrum[lowerBin] * (1 - fraction) +
                    _latestVisualizerSpectrum[upperBin] * fraction;
                result[index] = magnitude;
                framePeak = Math.Max(framePeak, magnitude);
            }

            // Web Audio visualizers fed from an uploaded media element see a
            // stable source level. Windows loopback reflects the user's output
            // volume instead. Calibrate that level to the analyzer's -30 dB
            // upper bound so the same track does not disappear at low speaker
            // volume. Rise gain slowly, reduce it quickly, and never chase
            // silence.
            const double targetPeakMagnitude = 0.03162277660168379; // -30 dB
            long revision = Interlocked.Read(ref _visualizerFftCount);
            if (framePeak > 0.00001 && revision != _gainRevision)
            {
                var targetGain = Math.Clamp(
                    targetPeakMagnitude / framePeak, 0.5, 32);
                var gainBlend = targetGain < _visualizerInputGain ? 0.28 : 0.07;
                _visualizerInputGain +=
                    (targetGain - _visualizerInputGain) * gainBlend;
            }
            _gainRevision = revision;

            for (var index = 0; index < result.Length; index++)
            {
                var magnitude = result[index];
                // NAudio's forward FFT already normalizes its result by the
                // transform length. A full-scale, bin-aligned sine through
                // this Hamming window measures about 0.27 (-11.4 dB).
                // Dividing by the FFT length again suppresses real audio by
                // roughly 90 dB and leaves the visualizer effectively inert.
                var scalarMagnitude = CalibrateVisualizerMagnitude(magnitude) *
                                      _visualizerInputGain;
                var previous = index < _visualizerWebSpectrum.Length
                    ? _visualizerWebSpectrum[index]
                    : 0;
                var smoothed = previous * smoothing +
                               scalarMagnitude * (1 - smoothing);
                if (index < _visualizerWebSpectrum.Length)
                    _visualizerWebSpectrum[index] = smoothed;
                var decibels = 20 * Math.Log10(Math.Max(smoothed, 0.0000001));
                result[index] = Math.Clamp((decibels + 40) / 10, 0, 1);
            }

            // JSNation2 applies one centered three-point pass after conversion
            // to byte-spectrum values. Preserve the endpoints as its helper does.
            if (result.Length > 2)
            {
                var unsmoothed = (double[])result.Clone();
                for (var index = 1; index < result.Length - 1; index++)
                    result[index] = (unsmoothed[index - 1] +
                                     unsmoothed[index] +
                                     unsmoothed[index + 1]) / 3;
            }
        }
        return result;
    }

    internal static string CaptureVisualizerFftCalibrationReport()
    {
        const int diagnosticBin = 120;
        const double diagnosticAmplitude = 0.1;
        var buffer = new Complex[VisualizerFftLength];
        for (var index = 0; index < buffer.Length; index++)
        {
            var sample = diagnosticAmplitude * Math.Sin(
                2 * Math.PI * diagnosticBin * index / VisualizerFftLength);
            buffer[index].X = (float)(sample *
                FastFourierTransform.HammingWindow(
                    index, VisualizerFftLength));
        }
        FastFourierTransform.FFT(true, VisualizerFftPower, buffer);
        var magnitude = Math.Sqrt(
            buffer[diagnosticBin].X * buffer[diagnosticBin].X +
            buffer[diagnosticBin].Y * buffer[diagnosticBin].Y);
        var calibrated = CalibrateVisualizerMagnitude(magnitude);
        var decibels = 20 * Math.Log10(Math.Max(calibrated, 0.0000001));
        var mappedLevel = Math.Clamp((decibels + 40) / 10, 0, 1);
        var pass = magnitude is > 0.026 and < 0.028 &&
                   mappedLevel is > 0.84 and < 0.89;
        return $"NAudio FFT calibration: magnitude={magnitude:0.000000}; " +
               $"dB={decibels:0.000}; mapped={mappedLevel:0.000}; " +
               (pass ? "PASS" : "FAIL");
    }

    internal static string CaptureVisualizerTimingReport(int sampleRate = 44100)
    {
        sampleRate = Math.Max(1, sampleRate);
        var windowMilliseconds = 1000d * VisualizerFftLength / sampleRate;
        var groupDelayMilliseconds = windowMilliseconds / 2;
        var hopMilliseconds = 1000d * VisualizerFftHopLength / sampleRate;
        var binWidth = sampleRate / (double)VisualizerFftLength;
        var lowBandBins = (140d - 18d) / binWidth;
        const double uiPollMilliseconds = 40;
        var worstAnalysisAge = groupDelayMilliseconds + hopMilliseconds +
                               uiPollMilliseconds;
        return $"visualizer timing at {sampleRate} Hz: " +
               $"FFT={VisualizerFftLength}; hop={VisualizerFftHopLength}; " +
               $"window={windowMilliseconds:0.0} ms; " +
               $"groupDelay={groupDelayMilliseconds:0.0} ms; " +
               $"hopInterval={hopMilliseconds:0.0} ms; " +
               $"binWidth={binWidth:0.00} Hz; " +
               $"18..140HzBins={lowBandBins:0.0}; " +
               $"worstAgeWith40msPoll={worstAnalysisAge:0.0} ms";
    }

    private static double CalibrateVisualizerMagnitude(double magnitude) =>
        magnitude;

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (sender is not WasapiLoopbackCapture capture ||
            !ReferenceEquals(capture, _capture))
            return;
        Interlocked.Increment(ref _dataCallbackCount);
        var format = capture.WaveFormat;
        var bytesPerSample = format.BitsPerSample / 8;
        var frameSize = bytesPerSample * format.Channels;
        if (frameSize <= 0) return;
        long now=Environment.TickCount64;
        if(_lastPacketTick!=0&&now-_lastPacketTick>=250)
        {
            Array.Clear(_visualizerSampleRing);
            _visualizerSampleCount=_visualizerSamplesSinceFft=_visualizerSampleWritePosition=0;
            _equalizerFftPosition=0;
        }
        _lastPacketTick=now;

        for (var offset = 0; offset + frameSize <= e.BytesRecorded; offset += frameSize)
        {
            Interlocked.Increment(ref _processedFrameCount);
            float sample;
            if (format.Encoding == WaveFormatEncoding.IeeeFloat && bytesPerSample == 4)
                sample = BitConverter.ToSingle(e.Buffer, offset);
            else if (bytesPerSample == 2)
                sample = BitConverter.ToInt16(e.Buffer, offset) / 32768f;
            else
                continue;

            _equalizerFftBuffer[_equalizerFftPosition].X = (float)(
                sample * FastFourierTransform.HammingWindow(
                    _equalizerFftPosition, EqualizerFftLength));
            _equalizerFftBuffer[_equalizerFftPosition].Y = 0;
            _equalizerFftPosition++;
            if (_equalizerFftPosition == EqualizerFftLength)
            {
                _equalizerFftPosition = 0;
                FastFourierTransform.FFT(
                    true, EqualizerFftPower, _equalizerFftBuffer);
                lock (_sync)
                {
                    for (var index = 0;
                         index < _latestEqualizerSpectrum.Length;
                         index++)
                        _latestEqualizerSpectrum[index] = MathF.Sqrt(
                            _equalizerFftBuffer[index].X *
                            _equalizerFftBuffer[index].X +
                            _equalizerFftBuffer[index].Y *
                            _equalizerFftBuffer[index].Y);
                }
            }

            _visualizerSampleRing[_visualizerSampleWritePosition] = sample;
            _visualizerSampleWritePosition =
                (_visualizerSampleWritePosition + 1) % VisualizerFftLength;
            _visualizerSampleCount = Math.Min(
                _visualizerSampleCount + 1, VisualizerFftLength);
            _visualizerSamplesSinceFft++;

            if (_visualizerSampleCount < VisualizerFftLength ||
                _visualizerSamplesSinceFft < VisualizerFftHopLength)
                continue;
            _visualizerSamplesSinceFft = 0;
            CalculateLatestVisualizerSpectrum();
        }
    }

    internal AudioSpectrumRuntimeSnapshot CaptureRuntimeSnapshot()
    {
        lock (_sync)
        {
            var sampleRate = Math.Max(1, _sampleRate);
            var lowBin = Math.Clamp(
                (int)Math.Floor(18d * VisualizerFftLength / sampleRate),
                1, _latestVisualizerSpectrum.Length - 1);
            var highBin = Math.Clamp(
                (int)Math.Ceiling(140d * VisualizerFftLength / sampleRate),
                lowBin + 1, _latestVisualizerSpectrum.Length);
            var lowFrequencyPeak = 0f;
            for (var index = lowBin; index < highBin; index++)
                lowFrequencyPeak = Math.Max(
                    lowFrequencyPeak, _latestVisualizerSpectrum[index]);
            return new AudioSpectrumRuntimeSnapshot(
                _running,
                Interlocked.Read(ref _dataCallbackCount),
                Interlocked.Read(ref _processedFrameCount),
                _captureFormat,
                _latestEqualizerSpectrum.Length == 0
                    ? 0 : _latestEqualizerSpectrum.Max(),
                lowFrequencyPeak,
                _visualizerInputGain,
                sampleRate,
                Interlocked.Read(ref _visualizerFftCount));
        }
    }

    private void CalculateLatestVisualizerSpectrum()
    {
        // Once the ring is full, the write position identifies its oldest
        // sample. The 1024-sample hop retains 87.5% of the previous window,
        // preserving low-frequency detail while producing a new spectrum at
        // audio cadence rather than in visibly stepped blocks.
        for (var index = 0; index < VisualizerFftLength; index++)
        {
            var ringIndex = (_visualizerSampleWritePosition + index) %
                            VisualizerFftLength;
            _visualizerFftBuffer[index].X = (float)(
                _visualizerSampleRing[ringIndex] *
                FastFourierTransform.HammingWindow(
                    index, VisualizerFftLength));
            _visualizerFftBuffer[index].Y = 0;
        }
        FastFourierTransform.FFT(
            true, VisualizerFftPower, _visualizerFftBuffer);
        lock (_sync)
        {
            for (var index = 0;
                 index < _latestVisualizerSpectrum.Length;
                 index++)
                _latestVisualizerSpectrum[index] = MathF.Sqrt(
                    _visualizerFftBuffer[index].X *
                    _visualizerFftBuffer[index].X +
                    _visualizerFftBuffer[index].Y *
                    _visualizerFftBuffer[index].Y);
            Interlocked.Increment(ref _visualizerFftCount);
            _lastSpectrumTick = Environment.TickCount64;
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (ReferenceEquals(sender, _capture))
            _running = false;
    }

    private void DisposeCapture()
    {
        var capture = _capture;
        if (capture is null) return;
        _capture = null;
        capture.DataAvailable -= OnDataAvailable;
        capture.RecordingStopped -= OnRecordingStopped;
        try { capture.StopRecording(); } catch { }
        capture.Dispose();
    }

    public void Dispose()
    {
        _running = false;
        DisposeCapture();
    }
}

internal readonly record struct AudioSpectrumRuntimeSnapshot(
    bool Running,
    long DataCallbacks,
    long ProcessedFrames,
    string Format,
    double EqualizerRawPeak,
    double VisualizerBassRawPeak,
    double VisualizerInputGain,
    int SampleRate,
    long VisualizerFftCount);

