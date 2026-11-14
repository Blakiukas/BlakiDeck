using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ekrano_valdymas;

// Fractional dashboard layout can give Grid an implicit layout clip even with
// ClipToBounds=false. The icon's shadow must be allowed past that inner slot;
// the unchanged outer rail still owns the overall silhouette.
public sealed class IconShadowHost : Grid
{
    internal static readonly DependencyProperty PlayerModeProperty = DependencyProperty.RegisterAttached(
        "PlayerMode", typeof(bool), typeof(IconShadowHost),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsArrange));

    protected override Geometry? GetLayoutClip(Size layoutSlotSize) => null;
}
