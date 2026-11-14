using System;
using System.Collections.Generic;
using System.Linq;

namespace Ekrano_valdymas
{
    internal static class TopBarLayoutPlanner
    {
        internal static bool PreservesShortcutUnitCount(
            int legacyShortcutUnits,
            int fixedTabUnits,
            int remainingShortcutUnits) =>
            fixedTabUnits + remainingShortcutUnits == legacyShortcutUnits;

        internal static int ResolveUnitDelta(double deltaPixels, double cellWidth)
        {
            if (cellWidth <= 0 || !double.IsFinite(cellWidth))
                throw new ArgumentOutOfRangeException(nameof(cellWidth));
            if (deltaPixels > 0)
                return (int)Math.Ceiling(deltaPixels / cellWidth);
            if (deltaPixels < 0)
                return (int)Math.Floor(deltaPixels / cellWidth);
            return 0;
        }

        internal static int ResolveInsertionIndexForUnit(
            IReadOnlyList<int> itemWidths,
            int desiredUnit)
        {
            int bestIndex = 0;
            int currentUnit = 0;
            int bestDistance = Math.Abs(desiredUnit);
            for (int index = 0; index < itemWidths.Count; index++)
            {
                currentUnit += itemWidths[index];
                int distance = Math.Abs(currentUnit - desiredUnit);
                if (distance < bestDistance || distance == bestDistance && currentUnit <= desiredUnit)
                {
                    bestDistance = distance;
                    bestIndex = index + 1;
                }
            }
            return bestIndex;
        }

        internal static int GetStartUnitForInsertionIndex(
            IReadOnlyList<int> itemWidths,
            int insertionIndex)
        {
            if (insertionIndex < 0 || insertionIndex > itemWidths.Count)
                throw new ArgumentOutOfRangeException(nameof(insertionIndex));
            return itemWidths.Take(insertionIndex).Sum();
        }

        internal static List<T> MoveFixedBlock<T>(
            IReadOnlyList<T> items,
            Func<T, int> getWidth,
            T source,
            int destinationStart,
            IEqualityComparer<T>? comparer = null)
        {
            comparer ??= EqualityComparer<T>.Default;
            List<T> cells = new();
            foreach (T item in items)
            {
                int width = getWidth(item);
                if (width <= 0) throw new ArgumentOutOfRangeException(nameof(getWidth));
                for (int unit = 0; unit < width; unit++) cells.Add(item);
            }

            int sourceStart = cells.FindIndex(item => comparer.Equals(item, source));
            if (sourceStart < 0) throw new ArgumentException("Source item was not found.", nameof(source));
            int sourceWidth = getWidth(source);
            destinationStart = Math.Clamp(destinationStart, 0, cells.Count - sourceWidth);

            HashSet<int> sourceRange = Enumerable.Range(sourceStart, sourceWidth).ToHashSet();
            HashSet<int> destinationRange = Enumerable.Range(destinationStart, sourceWidth).ToHashSet();
            List<int> vacatedCells = sourceRange.Except(destinationRange).OrderBy(index => index).ToList();
            List<T> displacedCells = destinationRange.Except(sourceRange)
                .OrderBy(index => index)
                .Select(index => cells[index])
                .ToList();
            if (vacatedCells.Count != displacedCells.Count)
                throw new InvalidOperationException("Source and destination blocks do not have matching free cells.");

            foreach (int index in destinationRange) cells[index] = source;
            for (int index = 0; index < vacatedCells.Count; index++)
                cells[vacatedCells[index]] = displacedCells[index];

            List<T> result = new();
            for (int index = 0; index < cells.Count;)
            {
                T item = cells[index];
                result.Add(item);
                int width = getWidth(item);
                for (int unit = 1; unit < width; unit++)
                {
                    if (index + unit >= cells.Count || !comparer.Equals(cells[index + unit], item))
                        throw new InvalidOperationException("A multi-cell item was split by the move.");
                }
                index += width;
            }
            return result;
        }

        internal static bool CanMoveFixedBlock<T>(
            IReadOnlyList<T> items,
            Func<T, int> getWidth,
            T source,
            int destinationStart,
            IEqualityComparer<T>? comparer = null)
        {
            comparer ??= EqualityComparer<T>.Default;
            List<T> cells = new();
            foreach (T item in items)
                for (int unit = 0; unit < getWidth(item); unit++) cells.Add(item);

            int sourceStart = cells.FindIndex(item => comparer.Equals(item, source));
            if (sourceStart < 0) return false;
            int sourceWidth = getWidth(source);
            destinationStart = Math.Clamp(destinationStart, 0, cells.Count - sourceWidth);
            HashSet<int> destinationRange = Enumerable.Range(destinationStart, sourceWidth).ToHashSet();

            foreach (T item in destinationRange.Select(index => cells[index]).Distinct(comparer))
            {
                if (comparer.Equals(item, source)) continue;
                int itemStart = cells.FindIndex(cell => comparer.Equals(cell, item));
                int itemWidth = getWidth(item);
                if (!Enumerable.Range(itemStart, itemWidth).All(destinationRange.Contains))
                    return false;
            }
            return true;
        }

        internal static bool Verify()
        {
            IReadOnlyList<int> remainingWidths = [1, 1, 1, 1];
            int oneStepRight = ResolveUnitDelta(1, 180);
            int oneStepLeft = ResolveUnitDelta(-1, 180);
            int rightInsertion = ResolveInsertionIndexForUnit(remainingWidths, 2 + oneStepRight);
            int leftInsertion = ResolveInsertionIndexForUnit(remainingWidths, 2 + oneStepLeft);

            string[] adjacentInitial = ["sleep", "empty", "clock", "shutdown"];
            List<string> adjacentResult = MoveFixedBlock(
                adjacentInitial,
                item => item == "clock" ? 2 : 1,
                "clock",
                3);
            string[] emptyTargetInitial = ["empty-1", "empty-2", "app", "clock", "shutdown"];
            List<string> emptyTargetResult = MoveFixedBlock(
                emptyTargetInitial,
                item => item == "clock" ? 2 : 1,
                "clock",
                0);
            string[] singleControlInitial = ["steam", "shutdown", "warning", "sleep", "empty", "clock"];
            bool canSwapShutdownWithEmpty = CanMoveFixedBlock(
                singleControlInitial,
                item => item == "clock" ? 2 : 1,
                "shutdown",
                4);
            List<string> singleControlResult = MoveFixedBlock(
                singleControlInitial,
                item => item == "clock" ? 2 : 1,
                "shutdown",
                4);
            bool refusesToSplitClock = !CanMoveFixedBlock(
                singleControlInitial,
                item => item == "clock" ? 2 : 1,
                "shutdown",
                5);

            return oneStepRight == 1 &&
                   PreservesShortcutUnitCount(8, 3, 5) &&
                   !PreservesShortcutUnitCount(8, 3, 8) &&
                   oneStepLeft == -1 &&
                   rightInsertion == 3 &&
                   GetStartUnitForInsertionIndex(remainingWidths, rightInsertion) == 3 &&
                   leftInsertion == 1 &&
                   GetStartUnitForInsertionIndex(remainingWidths, leftInsertion) == 1 &&
                   adjacentResult.SequenceEqual(["sleep", "empty", "shutdown", "clock"]) &&
                   emptyTargetResult.SequenceEqual(["clock", "app", "empty-1", "empty-2", "shutdown"]) &&
                   canSwapShutdownWithEmpty &&
                   singleControlResult.SequenceEqual(["steam", "empty", "warning", "sleep", "shutdown", "clock"]) &&
                   refusesToSplitClock;
        }
    }
}
