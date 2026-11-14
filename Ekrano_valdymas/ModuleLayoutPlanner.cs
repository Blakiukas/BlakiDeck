namespace Ekrano_valdymas
{
    internal static class ModuleLayoutPlanner
    {
        internal static bool IsBlockedByWide(int slot, int activeSpan) =>
            activeSpan is 2 or 3 && slot >= 0 && slot < activeSpan;

        internal static bool Verify() =>
            IsBlockedByWide(1, 2) && !IsBlockedByWide(2, 2) &&
            IsBlockedByWide(2, 3);
    }
}
