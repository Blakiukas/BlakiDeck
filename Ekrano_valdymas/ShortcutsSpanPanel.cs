using System;
using System.Windows;
using System.Windows.Controls;

namespace Ekrano_valdymas
{
    internal sealed class ShortcutsSpanPanel : Panel
    {
        private const int Rows = 3;
        public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
            nameof(Columns), typeof(int), typeof(ShortcutsSpanPanel),
            new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsMeasure |
                                             FrameworkPropertyMetadataOptions.AffectsArrange));

        public int Columns
        {
            get => (int)GetValue(ColumnsProperty);
            set => SetValue(ColumnsProperty, value);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            int columns = Math.Max(1, Columns);
            double width = SanitizeLength(availableSize.Width, 120 * columns);
            double height = SanitizeLength(availableSize.Height, 100 * Rows);
            double cellWidth = width / columns;
            double cellHeight = height / Rows;
            for (int index = 0; index < InternalChildren.Count; index++)
            {
                UIElement child = InternalChildren[index];
                ShortcutsWidget.Slot? slot = (child as FrameworkElement)?.DataContext as ShortcutsWidget.Slot;
                int span = slot?.ControlKey == "clock" ? slot.ClockStyle.Span : slot?.IsAirthingsMini==true?AirthingsMini.Span(slot.Target):slot?.IsSensor==true?slot.SensorSpan:1;
                child.Measure(index >= columns * Rows || slot?.IsReserved == true
                    ? new Size()
                    : new Size(cellWidth * span, cellHeight));
            }
            return new Size(width, height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double width = SanitizeLength(finalSize.Width, 0);
            double height = SanitizeLength(finalSize.Height, 0);
            int columns = Math.Max(1, Columns);
            double cellWidth = width / columns;
            double cellHeight = height / Rows;
            for (int index = 0; index < InternalChildren.Count; index++)
            {
                UIElement child = InternalChildren[index];
                ShortcutsWidget.Slot? slot = (child as FrameworkElement)?.DataContext as ShortcutsWidget.Slot;
                if (index >= columns * Rows) { child.Arrange(new Rect()); continue; }
                if (slot?.IsReserved == true) { child.Arrange(new Rect(0, 0, 0, 0)); continue; }
                int span = slot?.ControlKey == "clock" ? slot.ClockStyle.Span : slot?.IsAirthingsMini==true?AirthingsMini.Span(slot.Target):slot?.IsSensor==true?slot.SensorSpan:1;
                if (index >= columns * Rows ||
                    span == 2 && !ShortcutsGridPlanner.CanPlaceWideControl(index, InternalChildren.Count, columns))
                {
                    child.Arrange(new Rect(0, 0, 0, 0));
                    continue;
                }
                child.Arrange(new Rect((index % columns) * cellWidth, (index / columns) * cellHeight,
                    Math.Max(0, span * cellWidth), cellHeight));
            }
            return new Size(width, height);
        }

        private static double SanitizeLength(double value, double infinityFallback) =>
            double.IsNaN(value) || value < 0 ? 0 :
            double.IsPositiveInfinity(value) ? infinityFallback : value;
    }
}
