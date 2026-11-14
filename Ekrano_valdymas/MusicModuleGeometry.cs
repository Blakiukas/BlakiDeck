using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Ekrano_valdymas;

internal static class MusicModuleGeometry
{
    internal const double CanvasWidth = 1900;
    internal const double CanvasHeight = 437;
    internal const double RightFastenerAxisX = 1866;

    internal static readonly Rect Chassis = new(0, 0, 1900, 437);
    internal static readonly Rect MetalOuter = new(1, 1, 1898, 435);
    internal static readonly Rect Album = new(68, 52, 335, 335);
    internal static readonly Rect InformationGroup = new(423, 72, 433, 313);
    internal static readonly Rect Information = new(423, 72, 433, 167);
    internal static readonly Rect Transport = new(439, 247, 401, 138);
    internal static readonly Rect PreviousButtonAperture = new(464.8333333333333, 275, 82, 82);
    internal static readonly Rect PlayButtonAperture = new(590.5, 267, 98, 98);
    internal static readonly Rect NextButtonAperture = new(732.1666666666666, 275, 82, 82);
    internal static readonly Rect VuOuter = new(876, 52, 835, 335);
    internal static readonly Rect LeftVuCavity = new(899.5058823529412, 71.01714285714286, 375.4164705882353, 293.1685714285714);
    internal static readonly Rect VuDivider = new(1274.915294117647, 54, 33.24, 327.2171428571428);
    internal static readonly Rect RightVuCavity = new(1308.162352941176, 71.01714285714286, 375.4164705882353, 293.1685714285714);
    internal static readonly Rect OutputShoulder = new(1711, 52, RightFastenerAxisX - 1711, 335);
    internal static readonly Rect OutputTrack = new(1780, 103, 18, 209);
    internal static readonly Rect PercentScreen = new(1749, 330, 80, 36);
    internal static readonly Rect OutputIndicator = new(1785, 379, 8, 8);

    internal static IReadOnlyDictionary<string, Rect> AcceptanceZones { get; } = new Dictionary<string, Rect>
    {
        ["Chassis"] = Chassis,
        ["Album"] = Album,
        ["Spotify information"] = InformationGroup,
        ["VU instrument opening"] = VuOuter,
        ["Left VU cavity"] = LeftVuCavity,
        ["Center divider"] = VuDivider,
        ["Right VU cavity"] = RightVuCavity,
        ["Output control"] = OutputShoulder,
        ["Output track"] = OutputTrack,
        ["74 percent instrument"] = PercentScreen
    };

    internal static Geometry CreateMetalMask()
    {
        GeometryGroup mask = new() { FillRule = FillRule.EvenOdd };
        mask.Children.Add(new RectangleGeometry(MetalOuter, 18, 18));
        mask.Children.Add(new RectangleGeometry(Album, 8, 8));
        mask.Children.Add(new RectangleGeometry(Information, 6, 6));
        mask.Children.Add(new EllipseGeometry(PreviousButtonAperture));
        mask.Children.Add(new EllipseGeometry(PlayButtonAperture));
        mask.Children.Add(new EllipseGeometry(NextButtonAperture));
        // Exact match for the machined outer silhouette of the current VU frame.
        mask.Children.Add(new RectangleGeometry(VuOuter, 10, 10));
        mask.Children.Add(new RectangleGeometry(PercentScreen, 5, 5));
        mask.Children.Add(new RectangleGeometry(OutputTrack, 8, 8));
        mask.Children.Add(new EllipseGeometry(OutputIndicator));
        mask.Freeze();
        return mask;
    }
}
