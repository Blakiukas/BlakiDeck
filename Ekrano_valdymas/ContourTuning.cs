namespace Ekrano_valdymas;

public sealed class ContourTuning
{
    public double LowGain { get; set; } = 1.15;
    public double MidGain { get; set; } = 1;
    public double HighGain { get; set; } = .8;
    public double MasterDisplacement { get; set; } = 1;
    public double LowAttack { get; set; } = 20;
    public double MidAttack { get; set; } = 12;
    public double HighAttack { get; set; } = 5;
    public double LowRelease { get; set; } = 220;
    public double MidRelease { get; set; } = 140;
    public double HighRelease { get; set; } = 70;
    public double LowSmoothing { get; set; } = 14;
    public double MidSmoothing { get; set; } = 6;
    public double HighSmoothing { get; set; } = 2;
    public double NoiseGate { get; set; } = .0003;
    public double NormalizationSeconds { get; set; } = 2.5;
    public double BaseScale { get; set; } = .29;
    public double MarkerLength { get; set; } = 3.5;
    public double MarkerDensity { get; set; } = 128;
}
