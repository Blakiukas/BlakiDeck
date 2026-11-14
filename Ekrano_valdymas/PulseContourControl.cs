using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ekrano_valdymas;

// One closed deformable body; three smooth displacement fields, independent attached markers.
public sealed class PulseContourControl : FrameworkElement
{
    internal const int Samples = 192;
    public ContourTuning Tuning { get; } = new();
    private readonly double[] bands = new double[24];
    private readonly double[] reference = [.035, .008, .002];
    private readonly double[] slowBands = new double[24];
    private readonly double[][] fields = [new double[Samples], new double[Samples], new double[Samples]];
    private readonly double[] scratch = new double[Samples];
    private readonly Point[] points = new Point[Samples];
    private readonly Point[] basePoints = new Point[Samples];
    private double gateLevel;
    internal double MotionEnergy => bands.Average();
    internal Rect ShapeBounds { get; private set; }
    internal Point[] OutlinePoints => points.ToArray();
    private static readonly Brush Body = new SolidColorBrush(Color.FromRgb(5, 5, 7));
    private static readonly Pen Edge = MakePen(Colors.White, 1.7);
    private static readonly Pen Marker = MakePen(Color.FromRgb(240, 236, 247), .8);

    public PulseContourControl()
    {
        var menu = new ContextMenu();
        var tune = new MenuItem { Header = "Tune contour…" };
        tune.Click += (_, _) => ContourTuningWindow.Open(this);
        menu.Items.Add(tune); ContextMenu = menu;
        SizeChanged += (_, _) => InvalidateVisual();
    }

    private static Pen MakePen(Color color, double width)
    {
        var pen = new Pen(new SolidColorBrush(color), width) { LineJoin = PenLineJoin.Round };
        pen.Freeze(); return pen;
    }

    internal void Advance(ReadOnlySpan<double> spectrum, double elapsed, double rms = 1)
    {
        double dt = Math.Clamp(elapsed, 0, .1);
        double gateTarget = rms > Tuning.NoiseGate ? 1 : 0;
        gateLevel += (gateTarget - gateLevel) * (1 - Math.Exp(-dt / (gateTarget > gateLevel ? .01 : .06)));
        double[] gains = [Tuning.LowGain, Tuning.MidGain, Tuning.HighGain];
        double[] attacks = [Tuning.LowAttack, Tuning.MidAttack, Tuning.HighAttack];
        double[] releases = [Tuning.LowRelease, Tuning.MidRelease, Tuning.HighRelease];
        for (int layer = 0; layer < 3; layer++)
        {
            double peak = 0;
            for (int b = 0; b < 8; b++)
            {
                int index = layer * 8 + b;
                double input = index < spectrum.Length && double.IsFinite(spectrum[index]) ? Math.Max(0, spectrum[index]) : 0;
                peak = Math.Max(peak, input);
                double target = Math.Pow(1 - Math.Exp(-input / Math.Max(.00002, reference[layer]) * gains[layer]), .65) * gateLevel;
                if (gateTarget == 0) target = 0;
                double seconds = Math.Max(.001, (target > bands[index] ? attacks[layer] : releases[layer]) / 1000);
                bands[index] += (target - bands[index]) * (1 - Math.Exp(-dt / seconds));
                slowBands[index] += (bands[index] - slowBands[index]) * (1 - Math.Exp(-dt / .40));
            }
            // Seconds-long adaptation, suspended under the gate: never normalize each frame to itself.
            if (gateTarget > 0)
                reference[layer] += (Math.Max(.00002, peak * 1.5) - reference[layer]) *
                    (1 - Math.Exp(-dt / Math.Max(.25, Tuning.NormalizationSeconds)));
            MapField(layer);
        }
        Smooth(fields[0], (int)Tuning.LowSmoothing);
        Smooth(fields[1], (int)Tuning.MidSmoothing);
        Smooth(fields[2], (int)Tuning.HighSmoothing);
        InvalidateVisual();
    }

    private void MapField(int layer)
    {
        // Overlapping region kernels combine several logarithmic bands at every point.
        // Irrational angular offsets and different widths prevent mirrored/repeated FFT rings.
        double mean = 0;
        for (int b = 0; b < 8; b++) mean += bands[layer * 8 + b] / 8;
        for (int i = 0; i < Samples; i++)
        {
            double angle = Math.Tau * i / Samples, sum = 0, slowSum = 0, weightSum = 0;
            for (int b = 0; b < 8; b++)
            {
                double region = b * 2.3999632297 + layer * 1.137;
                double width = layer == 0 ? .50 : layer == 1 ? .30 : .14;
                double distance = Math.Atan2(Math.Sin(angle - region), Math.Cos(angle - region));
                double weight = Math.Exp(-distance * distance / (2 * width * width));
                // Neighbor bands overlap the regional response instead of assigning a bin to a vertex.
                double energy = bands[layer * 8 + b] * .6 + bands[layer * 8 + (b + 1) % 8] * .25 + bands[layer * 8 + (b + 7) % 8] * .15;
                double slow = slowBands[layer * 8 + b] * .6 + slowBands[layer * 8 + (b + 1) % 8] * .25 + slowBands[layer * 8 + (b + 7) % 8] * .15;
                sum += weight * energy; slowSum += weight * slow; weightSum += weight;
            }
            double local = sum / Math.Max(.00001, weightSum);
            double change = local - slowSum / Math.Max(.00001, weightSum);
            fields[layer][i] = Math.Tanh((local - mean * .85) * 3.5 + change * 5);
        }
    }

    private void Smooth(double[] field, int radius)
    {
        radius = Math.Clamp(radius, 0, 32);
        if (radius == 0) return;
        for (int i = 0; i < Samples; i++)
        {
            double sum = 0, weights = 0;
            for (int j = -radius; j <= radius; j++)
            {
                double w = radius + 1 - Math.Abs(j);
                sum += field[(i + j + Samples) % Samples] * w; weights += w;
            }
            scratch[i] = sum / weights;
        }
        Array.Copy(scratch, field, Samples);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        double scale = size / 300;
        Point center = new(ActualWidth / 2, ActualHeight / 2);
        for (int i = 0; i < Samples; i++)
        {
            double a = -Math.PI / 2 + i * Math.Tau / Samples;
            double radius = size * Tuning.BaseScale * (1 + .035 * Math.Sin(3 * a + .7) + .025 * Math.Cos(2 * a));
            basePoints[i] = center + new Vector(Math.Cos(a), Math.Sin(a)) * radius;
        }
        for (int i = 0; i < Samples; i++)
        {
            Vector tangent = basePoints[(i + 1) % Samples] - basePoints[(i + Samples - 1) % Samples];
            Vector normal = new(tangent.Y, -tangent.X); normal.Normalize();
            double displacement = size * Tuning.MasterDisplacement *
                (.19 * fields[0][i] + .065 * fields[1][i] + .012 * fields[2][i]);
            double inwardLimit = (basePoints[i] - center).Length * .62;
            if (displacement < 0) displacement = -inwardLimit * Math.Tanh(-displacement / inwardLimit);
            Point moved = basePoints[i] + normal * displacement;
            Vector radial = moved - center;
            // Compress only excursions beyond the safe region; no flat clipped tips.
            double length = radial.Length / size;
            if (length > .40) length = .40 + .065 * Math.Tanh((length - .40) / .065);
            points[i] = center + radial * (length * size / radial.Length);
        }
        var geometry = Curve(points); ShapeBounds = geometry.Bounds;
        dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        dc.DrawGeometry(Body, Edge, geometry);
        int count = Math.Clamp((int)Tuning.MarkerDensity, 0, Samples);
        for (int marker = 0; marker < count; marker++)
        {
            int i = marker * Samples / count;
            Vector tangent = points[(i + 1) % Samples] - points[(i + Samples - 1) % Samples];
            Vector normal = new(tangent.Y, -tangent.X); normal.Normalize();
            Point start = points[i] - normal * (2.5 * scale);
            // Fixed local length. Marks follow the actual surface normal, never FFT bar heights.
            dc.DrawLine(Marker, start, start - normal * (Tuning.MarkerLength * scale));
        }
        dc.Pop();
    }

    private static StreamGeometry Curve(Point[] points)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], true, true);
            for (int i = 0; i < points.Length; i++)
            {
                Point p0 = points[(i + points.Length - 1) % points.Length];
                Point p1 = points[i], p2 = points[(i + 1) % points.Length];
                Point p3 = points[(i + 2) % points.Length];
                context.BezierTo(p1 + (p2 - p0) / 6, p2 - (p3 - p1) / 6, p2, true, false);
            }
        }
        geometry.Freeze(); return geometry;
    }
}
