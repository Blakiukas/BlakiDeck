namespace Ekrano_valdymas;

// Needle motion follows stereo PCM energy, not the session peak hold. A shared
// slow peak-ratio calibration removes loopback attenuation without normalizing
// the musical envelope or changing the stereo balance. Silent/stale PCM is zero.
internal sealed class VuEnergyInput
{
    private double elapsed, referencePeak, capturedPeak, gain;
    internal double CalibrationGain => gain;
    internal void Read(double rmsL, double rmsR, double peakL, double peakR,
        double referenceL, double referenceR, double dt, out double left, out double right)
    {
        elapsed += Math.Clamp(dt, 0, .1);
        referencePeak = Math.Max(referencePeak, Math.Max(referenceL, referenceR));
        capturedPeak = Math.Max(capturedPeak, Math.Max(peakL, peakR));
        if (elapsed >= (gain == 0 ? .25 : 1))
        {
            if (capturedPeak > .000001 && referencePeak > .0001)
            {
                double measured = Math.Clamp(referencePeak / capturedPeak, .1, 1000);
                // Large changes denote an output-volume/routing change; smaller
                // differences are capture-window alignment, not music dynamics.
                gain = gain == 0 || measured / gain is < .67 or > 1.5
                    ? measured : gain * .8 + measured * .2;
            }
            elapsed = referencePeak = capturedPeak = 0;
        }
        left = Math.Clamp(rmsL * gain * Math.Sqrt(2), 0, 2);
        right = Math.Clamp(rmsR * gain * Math.Sqrt(2), 0, 2);
    }
}
