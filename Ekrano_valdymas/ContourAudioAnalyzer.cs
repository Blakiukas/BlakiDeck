using NAudio.Dsp;

namespace Ekrano_valdymas;

// Called on the capture worker, not the dispatcher. 4096 FFT, 1024 sample hop.
internal sealed class ContourAudioAnalyzer
{
    internal const int BandCount = 24;
    private readonly float[] ring = new float[4096];
    private readonly Complex[] fft = new Complex[4096];
    private readonly double[] latest = new double[BandCount];
    private readonly object gate = new();
    private int write, count, hop;
    private double rms;
    private long stamp;
    internal int AnalysisCount { get; private set; }

    internal void Feed(ReadOnlySpan<byte> pcm, bool silent = false)
    {
        for (int i = 0; i + 8 <= pcm.Length; i += 8)
        {
            float left = silent ? 0 : BitConverter.ToSingle(pcm.Slice(i, 4));
            float right = silent ? 0 : BitConverter.ToSingle(pcm.Slice(i + 4, 4));
            float value = (left + right) * .5f;
            ring[write] = float.IsFinite(value) ? value : 0;
            write = (write + 1) % ring.Length;
            count = Math.Min(count + 1, ring.Length);
            if (++hop < 1024 || count < ring.Length) continue;
            hop = 0;
            double squares = 0;
            for (int j = 0; j < ring.Length; j++)
            {
                float sample = ring[(write + j) % ring.Length];
                squares += sample * sample;
                fft[j].X = (float)(sample * FastFourierTransform.HammingWindow(j, ring.Length));
                fft[j].Y = 0;
            }
            FastFourierTransform.FFT(true, 12, fft);
            lock (gate)
            {
                double[] boundaries = [40, 180, 2500, 16000];
                for (int layer = 0; layer < 3; layer++) for (int b = 0; b < 8; b++)
                {
                    double f0 = boundaries[layer] * Math.Pow(boundaries[layer + 1] / boundaries[layer], b / 8d);
                    double f1 = boundaries[layer] * Math.Pow(boundaries[layer + 1] / boundaries[layer], (b + 1) / 8d);
                    int start = Math.Max(1, (int)(f0 * 4096 / 44100));
                    int end = Math.Min(2048, Math.Max(start + 1, (int)Math.Ceiling(f1 * 4096 / 44100)));
                    double energy = 0;
                    for (int j = start; j < end; j++) energy += fft[j].X * fft[j].X + fft[j].Y * fft[j].Y;
                    latest[layer * 8 + b] = Math.Sqrt(energy / (end - start));
                }
                rms = Math.Sqrt(squares / ring.Length);
                stamp = Environment.TickCount64;
                AnalysisCount++;
            }
        }
    }

    internal double Copy(double[] destination, bool expire = true)
    {
        // Never wait for FFT work on the render thread. Keep its previous snapshot on contention.
        if (!Monitor.TryEnter(gate)) return rms;
        try
        {
            if (expire && Environment.TickCount64 - stamp > 180)
            { Array.Clear(destination); return 0; }
            Array.Copy(latest, destination, Math.Min(destination.Length, latest.Length));
            return rms;
        }
        finally { Monitor.Exit(gate); }
    }
}
