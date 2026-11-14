using System.Windows;
using System.Windows.Media;

namespace Ekrano_valdymas;

internal static class ModuleHeadingTypography
{
    internal static void Apply(OpticallyCenteredText title)
    {
        title.SetResourceReference(OpticallyCenteredText.TextFontFamilyProperty,"TekoFont");
        title.TextFontWeight=FontWeights.Bold;title.TextFontSize=27;
        title.SetResourceReference(OpticallyCenteredText.TextBrushProperty,"ModuleTitleBrush");
    }
}
