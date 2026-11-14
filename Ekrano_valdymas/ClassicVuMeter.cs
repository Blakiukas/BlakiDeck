namespace Ekrano_valdymas;

// Rectified PCM drives a moving-coil model. UI reads never advance its clock.
internal sealed class ClassicVuMeter
{
    internal const int SampleRate = 44100;
    internal const double Damping = .8127169864286775, NaturalFrequency = 13.511912562532714;
    // A sine with peak amplitude -18 dBFS reads 0 VU at unity gain, before
    // the user-adjustable presentation sensitivity.
    internal static readonly double ReferencePeak = Math.Pow(10, -18d / 20);
    internal static readonly double ReferenceMean = ReferencePeak * 2 / Math.PI;
    private readonly object gate = new();
    private double left, right, leftVelocity, rightVelocity;
    private static readonly double a = Damping * NaturalFrequency;
    private static readonly double b = NaturalFrequency * Math.Sqrt(1 - Damping * Damping);
    private static readonly double decay = Math.Exp(-a / SampleRate);
    private static readonly double cosine = Math.Cos(b / SampleRate);
    private static readonly double sineOverB = Math.Sin(b / SampleRate) / b;

    // Exact zero-order-hold solution, one audio sample per step.
    private static void Step(ref double position, ref double velocity, double sample)
    {
        double target = double.IsFinite(sample) ? Math.Abs(sample) / ReferenceMean : 0;
        double error = position - target;
        double next = target + decay * (error * cosine + (velocity + a * error) * sineOverB);
        velocity = decay * (velocity * cosine - (a * velocity + NaturalFrequency * NaturalFrequency * error) * sineOverB);
        position = next;
    }

    internal void Process(ReadOnlySpan<byte> pcm, bool silent)
    {
        lock (gate)
            for (int offset = 0; offset + 8 <= pcm.Length; offset += 8)
            {
                Step(ref left, ref leftVelocity, silent ? 0 : BitConverter.ToSingle(pcm.Slice(offset, 4)));
                Step(ref right, ref rightVelocity, silent ? 0 : BitConverter.ToSingle(pcm.Slice(offset + 4, 4)));
            }
    }

    internal (double Left, double Right) Read() { lock (gate) return (left, right); }
    internal State CaptureState() { lock (gate) return new(left, right, leftVelocity, rightVelocity); }
    internal readonly record struct State(double Left, double Right, double LeftVelocity, double RightVelocity)
    {
        // Exact free response from the power-off instant; evaluation cadence cannot
        // change the trajectory. No integration or smoothing on the rendering clock.
        internal (double Left, double Right) AfterSilence(double seconds)
        {
            double t = Math.Max(0, seconds), e = Math.Exp(-a * t), c = Math.Cos(b * t), s = Math.Sin(b * t) / b;
            return (e * (Left * c + (LeftVelocity + a * Left) * s),
                    e * (Right * c + (RightVelocity + a * Right) * s));
        }
    }
    internal void Reset() { lock (gate) left = right = leftVelocity = rightVelocity = 0; }
    internal static double Angle(double amplitude) => -50 + 100 * Math.Clamp(amplitude, 0, Math.Pow(10, 3d / 20)) / Math.Pow(10, 3d / 20);
}
