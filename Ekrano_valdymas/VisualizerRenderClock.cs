using System.Windows.Media;

namespace Ekrano_valdymas;

internal static class VisualizerRenderClock
{
    private static readonly List<Action<TimeSpan>> Subscribers = new();

    public static void Subscribe(Action<TimeSpan> callback)
    {
        if (Subscribers.Contains(callback)) return;
        Subscribers.Add(callback);
        if (Subscribers.Count == 1)
            CompositionTarget.Rendering += CompositionTarget_Rendering;
    }

    public static void Unsubscribe(Action<TimeSpan> callback)
    {
        Subscribers.Remove(callback);
        if (Subscribers.Count == 0)
            CompositionTarget.Rendering -= CompositionTarget_Rendering;
    }

    private static void CompositionTarget_Rendering(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs args) return;
        for (int i = 0; i < Subscribers.Count; i++)
            Subscribers[i](args.RenderingTime);
    }
}
