namespace Ekrano_valdymas;

internal static class VuResponse
{
    // Visual VU reference, independent of application/output volume. Preserve
    // the established +6 dB calibration, with a soft shoulder instead of clipping.
    internal const double ConsoleInputGain = 2;
    internal const double ConsoleAttack = .030, ConsoleRelease = .18;
    internal const double InputGain = 8.8;
    internal static double CalibratedInput(double value) => Math.Clamp(value * InputGain, 0, 1);
    internal static double Decibels(double value) => value <= .000001 ? -40 : Math.Max(-40, 20 * Math.Log10(value) + 18);
    internal static double ConsoleAngle(double value)
    {
        if(value <= .000001)return -50;
        double level=value*ConsoleInputGain,headroom=Math.Pow(10,3d/20);
        if(level>1)level=1+(headroom-1)*(1-Math.Exp(-(level-1)/(headroom-1)));
        return -50+100*level/headroom;
    }
}
internal sealed class VuBallistics(double attack=.065,double release=.42)
{
    private double value;
    internal double Step(double target, double dt)
    {
        double tau = target > value ? attack : release;
        value += (target - value) * (1 - Math.Exp(-Math.Max(0, dt) / tau));
        return value;
    }
    internal void Reset() => value = 0;
}


