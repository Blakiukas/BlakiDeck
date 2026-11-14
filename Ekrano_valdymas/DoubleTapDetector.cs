using System.Windows;

namespace Ekrano_valdymas
{
    internal sealed class DoubleTapDetector
    {
        private long lastUpTick;
        private Point lastPosition;
        public bool IsSecondTap(long tick, Point position)
        {
            long elapsed = tick - lastUpTick;
            bool match = elapsed is > 0 and <= 450 && (position - lastPosition).Length <= 42;
            if (match) lastUpTick = 0;
            return match;
        }
        public void RecordTapUp(long tick, Point position) { lastUpTick = tick; lastPosition = position; }
        public void Reset() => lastUpTick = 0;
        internal static bool Verify()
        {
            DoubleTapDetector detector = new();
            detector.RecordTapUp(1000, new Point(100,100));
            if (!detector.IsSecondTap(1300, new Point(120,115))) return false;
            detector.RecordTapUp(2000, new Point(100,100));
            if (detector.IsSecondTap(2500, new Point(100,100))) return false;
            detector.RecordTapUp(3000, new Point(100,100));
            return !detector.IsSecondTap(3200, new Point(160,100));
        }
    }
}
