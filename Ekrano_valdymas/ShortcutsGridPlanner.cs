using System;
using System.Collections.Generic;
using System.Linq;

namespace Ekrano_valdymas
{
    internal static class ShortcutsGridPlanner
    {
        internal static bool CanPlaceWideControl(int startIndex, int itemCount, int columns = 4) =>
            startIndex >= 0 &&
            startIndex + 1 < itemCount &&
            columns > 1 &&
            startIndex % columns < columns - 1;

        internal static int ResolveWideStart(int hoveredIndex, int itemCount, int columns = 4)
        {
            if (hoveredIndex < 0 || hoveredIndex >= itemCount || columns < 2) return hoveredIndex;
            return hoveredIndex % columns == columns - 1 ? hoveredIndex - 1 : hoveredIndex;
        }

        internal static List<T> MovePair<T>(IReadOnlyList<T> items, int sourceIndex, int destinationIndex,
            int columns = 4)
        {
            if (!CanPlaceWideControl(sourceIndex, items.Count, columns) ||
                !CanPlaceWideControl(destinationIndex, items.Count, columns))
                throw new ArgumentOutOfRangeException();

            List<T> result = new(items);
            HashSet<int> sourceRange = [sourceIndex, sourceIndex + 1];
            HashSet<int> destinationRange = [destinationIndex, destinationIndex + 1];
            List<int> vacated = sourceRange.Except(destinationRange).OrderBy(index => index).ToList();
            List<T> displaced = destinationRange.Except(sourceRange)
                .OrderBy(index => index)
                .Select(index => result[index])
                .ToList();

            result[destinationIndex] = items[sourceIndex];
            result[destinationIndex + 1] = items[sourceIndex + 1];
            for (int index = 0; index < vacated.Count; index++) result[vacated[index]] = displaced[index];
            return result;
        }

        internal static bool Verify()
        {
            bool validStarts = Enumerable.Range(0, 12).All(index =>
                CanPlaceWideControl(index, 12) == (index % 4 < 3 && index < 11));
            string[] initial = ["a", "b", "c", "d", "clock", "reserved", "e", "f"];
            List<string> overlapRight = MovePair(initial, 4, 5);
            return validStarts &&
                   ResolveWideStart(3, 12) == 2 &&
                   ResolveWideStart(7, 12) == 6 &&
                   ResolveWideStart(11, 12) == 10 &&
                   ResolveWideStart(2, 12) == 2 &&
                   !CanPlaceWideControl(3, 12) &&
                   !CanPlaceWideControl(7, 12) &&
                   !CanPlaceWideControl(11, 12) &&
                   overlapRight.SequenceEqual(["a", "b", "c", "d", "e", "clock", "reserved", "f"]);
        }
    }
}
