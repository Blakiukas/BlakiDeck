using System.Windows;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using MediaPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;

namespace Hud1920x480;

public sealed class WpfMusicVisualizer : FrameworkElement
{
    private const int ContourPointCount=96;
    private static readonly WpfPoint[] ContourDirections=CreateContourDirections();
    private static readonly double[] LayerExponents =
        [1, 1.12, 1.14, 1.3, 1.33, 1.36, 1.5, 1.52];
    private static readonly int[] LayerSmoothMargins = [0, 2, 2, 3, 3, 3, 5, 5];
    private readonly double[] _smoothedSpectrum = new double[64];
    private readonly double[][] _spectrumHistory = new double[8][];
    private readonly System.Diagnostics.Stopwatch _frameClock =
        System.Diagnostics.Stopwatch.StartNew();
    private readonly DrawingVisual _drawingVisual = new();
    private readonly DrawingVisual _mirrorVisual = new();
    private readonly DrawingGroup _contourFrame = new();
    private readonly ScaleTransform _mirrorTransform = new(1,-1);
    private bool _mirrorSpectrum;
    private int _historyCursor = -1;
    private int _historyCount;
    private double _lastFrameSeconds;
    private double _dynamicRadius = 79;
    private double _moduleHalfWidth = 135;
    private double _moduleHalfHeight = 135;
    private MusicVisualizerStyle _style;
    private MediaColor _accent;
    private MediaColor _primary;
    internal long FrameRevision {get;private set;}
    internal long MirrorFrameRevision {get;private set;}
    internal Rect FrameInkBounds=>_drawingVisual.ContentBounds;
    internal Rect MirroredInkBounds=>_mirrorSpectrum?_mirrorVisual.ContentBounds:Rect.Empty;
    internal Size ModuleEnvelope=>new(_moduleHalfWidth,_moduleHalfHeight);
    internal bool RadialBars { get; set; }
    internal void ConfigureModuleEnvelope(double halfWidth,double halfHeight)
    {
        if(!double.IsFinite(halfWidth)||!double.IsFinite(halfHeight)||halfWidth<=0||halfHeight<=0)
            throw new ArgumentOutOfRangeException(nameof(halfWidth));
        _moduleHalfWidth=halfWidth;
        _moduleHalfHeight=halfHeight;
    }
    internal bool MirrorSpectrum
    {
        get=>_mirrorSpectrum;
        set
        {
            if(_mirrorSpectrum==value)return;
            _mirrorSpectrum=value;
            if(!value){using(_mirrorVisual.RenderOpen()){}MirrorFrameRevision=0;}
        }
    }

    public WpfMusicVisualizer()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = false;
        AddVisualChild(_drawingVisual);
        AddLogicalChild(_drawingVisual);
        AddVisualChild(_mirrorVisual);
        AddLogicalChild(_mirrorVisual);
        for (var index = 0; index < _spectrumHistory.Length; index++)
            _spectrumHistory[index] = new double[_smoothedSpectrum.Length];
    }

    internal double Update(
        ReadOnlySpan<double> spectrum,
        MediaColor accent,
        MediaColor primary,
        MusicVisualizerStyle style)
    {
        var now = _frameClock.Elapsed.TotalSeconds;
        var elapsed = _lastFrameSeconds <= 0
            ? 1d / 60d
            : Math.Clamp(now - _lastFrameSeconds, 1d / 240d, 0.2);
        _lastFrameSeconds = now;
        const double timeConstant = 0.008;
        var blend = 1 - Math.Exp(-elapsed / timeConstant);
        var smoothCount = Math.Min(spectrum.Length, _smoothedSpectrum.Length);
        for (var index = 0; index < smoothCount; index++)
            _smoothedSpectrum[index] +=
                (spectrum[index] - _smoothedSpectrum[index]) * blend;

        _historyCursor = (_historyCursor + 1) % _spectrumHistory.Length;
        Array.Clear(_spectrumHistory[_historyCursor]);
        _smoothedSpectrum.AsSpan(0, smoothCount).CopyTo(
            _spectrumHistory[_historyCursor]);
        _historyCount = Math.Min(_historyCount + 1, _spectrumHistory.Length);

        var firstBin = Math.Clamp(style.StartBin, 0,
            Math.Max(0, smoothCount - 1));
        var lastBin = Math.Clamp(style.EndBin, firstBin,
            Math.Max(0, smoothCount - 1));
        var sum = 0d;
        for (var index = firstBin; index <= lastBin; index++)
            sum += _smoothedSpectrum[index];
        var average = sum / Math.Max(1, lastBin - firstBin + 1);
        var energyCurve = 5 * (-Math.Pow(average, 1.2) + 1.2 * average);
        var energyResponse = Math.Pow(Math.Max(0, energyCurve), 0.63794) *
                             2.7575;
        var baseRadius = style.InnerRadius * style.Scale;
        var strokeReserve=ContourStrokeRadius(style.Glow);
        var centerGrowthLimit=Math.Max(0,
            Math.Min(_moduleHalfWidth,_moduleHalfHeight)-strokeReserve-baseRadius-2);
        _dynamicRadius = baseRadius + SoftLimitDisplacement(
            energyResponse * style.CenterReactiveness * 7.5,centerGrowthLimit);
        _style = style;
        _accent = accent;
        _primary = primary;
        FrameRevision++;
        if(_mirrorSpectrum)
        {
            // Record the contour once, then replay that same completed frame.
            // The symmetric center ring remains a single drawing, not a second
            // brighter ring. No extra audio analysis or history update occurs.
            using(var contours=_contourFrame.Open())RenderContours(contours);
            using(var drawingContext=_drawingVisual.RenderOpen())
            {
                drawingContext.DrawDrawing(_contourFrame);
                RenderCenterRing(drawingContext);
            }
            _mirrorTransform.CenterY=Math.Max(1,ActualHeight)/2;
            using(var mirrored=_mirrorVisual.RenderOpen())
            {
                mirrored.PushTransform(_mirrorTransform);
                mirrored.DrawDrawing(_contourFrame);
                mirrored.Pop();
            }
            MirrorFrameRevision=FrameRevision;
        }
        else
        {
            // Preserve the original single-contour / center-ring draw order.
            using(var drawingContext=_drawingVisual.RenderOpen())RenderFrame(drawingContext);
        }
        return Math.Clamp(
            _dynamicRadius / Math.Max(1, baseRadius), 1, 1.28);
    }

    protected override int VisualChildrenCount => 2;

    protected override Visual GetVisualChild(int index) => index switch
    {
        0=>_drawingVisual,1=>_mirrorVisual,_=>throw new ArgumentOutOfRangeException(nameof(index))
    };

    private void RenderFrame(DrawingContext drawingContext)
    {
        if (_historyCount <= 0) return;
        if(RadialBars)RenderRadialBars(drawingContext);else RenderContours(drawingContext);
        RenderCenterRing(drawingContext);
    }

    private void RenderRadialBars(DrawingContext context)
    {
        const int count=48;
        var center=new WpfPoint(Math.Max(1,ActualWidth)/2,Math.Max(1,ActualHeight)/2);
        int first=Math.Clamp(_style.StartBin,0,41),last=Math.Clamp(_style.EndBin,first,41);
        var pen=new MediaPen(CreateBrush(_accent,.95),3){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
        var halo=new MediaPen(CreateBrush(_accent,.08+_style.Glow*.16),3+_style.Glow*6){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
        for(int i=0;i<count;i++)
        {
            double phase=i/(double)count,source=1-Math.Abs(2*phase-1);
            if(_style.Invert)source=1-source;
            double bin=first+source*(last-first);int lo=(int)bin,hi=Math.Min(last,lo+1);
            double level=Math.Clamp(_smoothedSpectrum[lo]*(1-(bin-lo))+_smoothedSpectrum[hi]*(bin-lo),0,1);
            double angle=phase*Math.PI*2-Math.PI/2;
            var direction=new WpfPoint(Math.Cos(angle),Math.Sin(angle));
            double start=_dynamicRadius+3,reserve=halo.Thickness/2+1;
            double room=Math.Max(0,EllipseRadius(direction,_moduleHalfWidth-reserve,_moduleHalfHeight-reserve)-start);
            double length=SoftLimitDisplacement(2+level*255*_style.Reaction*_style.Scale,room);
            var a=new WpfPoint(center.X+direction.X*start,center.Y+direction.Y*start);
            var b=new WpfPoint(center.X+direction.X*(start+length),center.Y+direction.Y*(start+length));
            if(_style.Glow>.001)context.DrawLine(halo,a,b);
            context.DrawLine(pen,a,b);
        }
    }

    private void RenderContours(DrawingContext drawingContext)
    {
        var width = Math.Max(1, ActualWidth);
        var height = Math.Max(1, ActualHeight);
        for (var layer = _spectrumHistory.Length - 1; layer >= 0; layer--)
        {
            var availableDelay = Math.Min(layer, _historyCount - 1);
            var slot = (_historyCursor - availableDelay +
                        _spectrumHistory.Length) % _spectrumHistory.Length;
            var geometry = CreateSpectrumGeometry(
                _spectrumHistory[slot], width, height, _style,
                _dynamicRadius, LayerExponents[layer],
                LayerSmoothMargins[layer],
                _moduleHalfWidth-ContourStrokeRadius(_style.Glow),
                _moduleHalfHeight-ContourStrokeRadius(_style.Glow));
            var layerAlpha = layer == 0
                ? 0.86
                : 0.045 + (7 - layer) * 0.018;
            drawingContext.DrawGeometry(
                CreateBrush(_accent, layerAlpha), null, geometry);
            if (layer != 0) continue;

            if (_style.Glow > 0.001)
            {
                var glow = CreateBrush(
                    _accent, 0.08 + _style.Glow * 0.16);
                drawingContext.DrawGeometry(null,
                    new MediaPen(glow, 2 + _style.Glow * 8){LineJoin=PenLineJoin.Round}, geometry);
                drawingContext.DrawGeometry(null,
                    new MediaPen(glow, 1 + _style.Glow * 4){LineJoin=PenLineJoin.Round}, geometry);
            }
            drawingContext.DrawGeometry(null,
                new MediaPen(CreateBrush(_primary, 0.9), 1.15){LineJoin=PenLineJoin.Round}, geometry);
        }

    }

    private void RenderCenterRing(DrawingContext drawingContext)
    {
        var center = new WpfPoint(Math.Max(1,ActualWidth) / 2, Math.Max(1,ActualHeight) / 2);
        var ringRadius = _dynamicRadius - 4;
        drawingContext.DrawEllipse(null,
            new MediaPen(CreateBrush(_primary, 0.72), 1.4),
            center, ringRadius, ringRadius);
    }

    private static StreamGeometry CreateSpectrumGeometry(
        ReadOnlySpan<double> spectrum,
        double width,
        double height,
        MusicVisualizerStyle style,
        double dynamicRadius,
        double exponent,
        int smoothMargin,
        double envelopeHalfWidth,
        double envelopeHalfHeight)
    {
        const int points = ContourPointCount;
        var geometry = new StreamGeometry { FillRule = FillRule.EvenOdd };
        using var context = geometry.Open();
        var centerX = width / 2;
        var centerY = height / 2;
        var innerRadius = dynamicRadius;
        for (var index = 0; index < points; index++)
        {
            var phase = index / (double)points;
            var sourcePhase = GpuMusicVisualizer.MapSpectrumPhase(
                phase, style.Invert);
            var firstBin = Math.Clamp(style.StartBin, 0,
                Math.Max(0, spectrum.Length - 1));
            var lastBin = Math.Clamp(style.EndBin, firstBin,
                Math.Max(0, spectrum.Length - 1));
            var source = firstBin + sourcePhase * (lastBin - firstBin);
            var sourceLow = Math.Clamp((int)Math.Floor(source), 0,
                Math.Max(0, spectrum.Length - 1));
            var sourceHigh = Math.Clamp(sourceLow + 1, 0,
                Math.Max(0, spectrum.Length - 1));
            var fraction = source - Math.Floor(source);
            var level = spectrum.Length == 0 ? 0 : Math.Clamp(
                SampleSmoothed(spectrum, sourceLow, smoothMargin) *
                    (1 - fraction) +
                SampleSmoothed(spectrum, sourceHigh, smoothMargin) * fraction,
                0, 1);
            var dropoff = 1 - Math.Pow(sourcePhase, 4);
            var displacement = Math.Pow(
                level * 255 * style.Reaction, exponent) *
                dropoff * style.Scale;
            var direction=ContourDirections[index];
            var maximumDisplacement = Math.Max(0,
                EllipseRadius(direction,envelopeHalfWidth,envelopeHalfHeight)-innerRadius-2);
            var radius = innerRadius + 2 +
                         SoftLimitDisplacement(displacement,maximumDisplacement);
            var point = new WpfPoint(
                centerX + radius * direction.X,
                centerY + radius * direction.Y);
            if (index == 0) context.BeginFigure(point, true, true);
            else context.LineTo(point, true, false);
        }
        for (var index = points - 1; index >= 0; index--)
        {
            var direction=ContourDirections[index];
            var point = new WpfPoint(
                centerX + innerRadius * direction.X,
                centerY + innerRadius * direction.Y);
            if (index == points - 1) context.BeginFigure(point, true, true);
            else context.LineTo(point, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    // A linear response below the knee joins a monotone bounded shoulder with
    // the same slope. Strong peaks retain motion instead of hitting a flat cap.
    internal static double SoftLimitDisplacement(double displacement,double limit)
    {
        if(!double.IsFinite(limit)||limit<=0||double.IsNaN(displacement)||displacement<=0)return 0;
        if(double.IsPositiveInfinity(displacement))return limit;
        double knee=limit*.7;
        if(displacement<=knee)return displacement;
        double headroom=limit-knee,excess=displacement-knee;
        return knee+headroom*(excess/(headroom+excess));
    }

    // The smooth ellipse follows the module's usable width and height. The
    // spectrum layout rectangle is only its drawing origin, never a hard cap.
    internal static double EnvelopeRadius(double angle,double halfWidth,double halfHeight)
    {
        if(!double.IsFinite(angle)||!double.IsFinite(halfWidth)||!double.IsFinite(halfHeight)||halfWidth<=0||halfHeight<=0)return 0;
        return EllipseRadius(new WpfPoint(Math.Cos(angle),Math.Sin(angle)),halfWidth,halfHeight);
    }

    private static double EllipseRadius(WpfPoint direction,double halfWidth,double halfHeight)
    {
        double x=direction.X/halfWidth,y=direction.Y/halfHeight;
        return 1/Math.Sqrt(x*x+y*y);
    }

    private static WpfPoint[] CreateContourDirections()
    {
        var directions=new WpfPoint[ContourPointCount];
        for(int index=0;index<directions.Length;index++)
        {
            double angle=-Math.PI/2+index/(double)ContourPointCount*Math.PI*2;
            directions[index]=new WpfPoint(Math.Cos(angle),Math.Sin(angle));
        }
        return directions;
    }

    private static double ContourStrokeRadius(double glow)=>
        Math.Max(.575,glow>.001?1+glow*4:0);

    private static double SampleSmoothed(
        ReadOnlySpan<double> spectrum, int center, int margin)
    {
        if (spectrum.Length == 0) return 0;
        var sum = 0d;
        var weight = 0d;
        for (var distance = 0; distance <= margin; distance++)
        {
            if (center - distance < 0 ||
                center + distance >= spectrum.Length)
                break;
            sum += spectrum[center - distance] +
                   spectrum[center + distance];
            weight += (margin - distance + 1) * 2;
        }
        return weight <= 0 ? spectrum[center] : sum / weight;
    }

    private static SolidColorBrush CreateBrush(MediaColor color, double alpha)
    {
        alpha = Math.Clamp(alpha, 0, 1);
        var brush = new SolidColorBrush(MediaColor.FromArgb(
            (byte)Math.Round(alpha * 255),
            (byte)Math.Round(color.R * alpha),
            (byte)Math.Round(color.G * alpha),
            (byte)Math.Round(color.B * alpha)));
        brush.Freeze();
        return brush;
    }
}
