namespace Hud1920x480;
internal static class GpuMusicVisualizer {
    internal static double MapSpectrumPhase(double phase, bool invert)
    {
        phase = Math.Clamp(phase, 0, 1);
        var mirroredPhase = phase <= 0.5
            ? phase * 2
            : (1 - phase) * 2;
        return invert ? 1 - mirroredPhase : mirroredPhase;
    }
}
internal readonly record struct MusicVisualizerStyle(
    double InnerRadius,
    double Reaction,
    double Scale,
    double Glow,
    double Smoothing,
    double CenterReactiveness,
    int StartBin,
    int EndBin,
    bool Invert);
