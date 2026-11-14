using System.Globalization;
using System.Windows;
using System.Windows.Controls;
namespace Ekrano_valdymas;

// Title, value and right-aligned interval share one cap-to-baseline bottom,
// through the same layout as dual Network. Interval growth stays above it.
internal sealed class SensorHeaderGrid:Grid
{
    internal OpticallyCenteredText? Title,Value,Duration,Hardware;
    internal bool Graph,AutoFit=true;
    internal double TitleSize=14,ValueSize=16,DurationSize=10;
    internal double HardwareInset;
    private readonly GraphHeaderLayout layout=new();
    protected override Size MeasureOverride(Size available)
    {
        if(!Graph&&Value?.Parent==this&&ColumnDefinitions.Count>2)ColumnDefinitions[2].Width=new GridLength(Value.Visibility!=Visibility.Collapsed?Value.GetNaturalMetrics(Value.TextFontSize).Width+8:0);
        if(!Graph||Title==null||Value?.Parent!=this||Duration==null)return base.MeasureOverride(available);
        return layout.Measure(available,[Title,Value],[TitleSize,ValueSize],Duration,DurationSize,AutoFit,Hardware,HardwareInset);
    }
    protected override Size ArrangeOverride(Size size)
    {
        if(!Graph||Value?.Parent!=this)return base.ArrangeOverride(size);
        return layout.Arrange(size);
    }
}
