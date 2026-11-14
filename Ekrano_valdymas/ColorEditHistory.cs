using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ekrano_valdymas;

internal sealed class ColorEditHistory<T>
{
    private readonly List<T> states = new();
    private int index = -1;
    internal bool Restoring { get; private set; }
    internal event Action? Changed;
    internal bool CanUndo => index > 0;
    internal bool CanRedo => index + 1 < states.Count;
    internal void Record(T value)
    {
        if (Restoring || index >= 0 && EqualityComparer<T>.Default.Equals(states[index], value)) return;
        states.RemoveRange(index + 1, states.Count - index - 1);
        states.Add(value); index++;
        if (states.Count > 256) { states.RemoveAt(0); index--; }
        Changed?.Invoke();
    }
    internal void Move(int direction, Action<T> restore)
    {
        if (direction < 0 ? !CanUndo : !CanRedo) return;
        Restoring = true;
        try { index += direction; restore(states[index]); }
        finally { Restoring = false; Changed?.Invoke(); }
    }
    internal StackPanel Buttons(Button defaults, Action<T> restore)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(defaults);
        var undo = MacroUi.Button("Undo", (_, _) => Move(-1, restore));
        var redo = MacroUi.Button("Redo", (_, _) => Move(1, restore));
        foreach (var button in new[] { undo, redo }) { button.Width = 60; button.MinWidth = 0; button.Padding = new Thickness(5); row.Children.Add(button); }
        Changed += () => { undo.IsEnabled = CanUndo; redo.IsEnabled = CanRedo; };
        undo.IsEnabled = CanUndo; redo.IsEnabled = CanRedo;
        return row;
    }
}

internal static class RecentColors
{
    private static string FileName => Path.Combine(DiagnosticState.LocalApplicationData, "Ekrano_valdymas", "recent-colors.json");
    internal static string[] Load()
    {
        try { return (JsonSerializer.Deserialize<string[]>(File.ReadAllText(FileName)) ?? []).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().Take(7).ToArray(); }
        catch { return []; }
    }
    internal static void Save(IEnumerable<string?> colors)
    {
        var values = colors.Where(c => c != null).Cast<string>().Concat(Load()).Distinct().Take(7).ToArray();
        AtomicJson.Write(FileName, values);
    }
    internal static FrameworkElement Row(Action<string> choose)
    {
        var row = new System.Windows.Controls.Primitives.UniformGrid { Columns = 7, Margin = new Thickness(0, 10, 0, 0) };
        var colors = Load();
        for (int i = 0; i < 7; i++)
        {
            string? color = i < colors.Length ? colors[i] : null;
            var button = MacroUi.Button("", (_, _) => { if (color != null) choose(color); });
            button.Name = "RecentColor"; button.Height = 28; button.MinHeight = 0; button.MinWidth = 0; button.Margin = new Thickness(2); button.Padding = new Thickness(0);
            button.Background = color == null ? Brushes.Transparent : new SolidColorBrush(CompactColorPicker.Parse(color, Colors.Transparent));
            button.ToolTip = color ?? "No recent color"; button.IsEnabled = color != null; row.Children.Add(button);
        }
        return row;
    }
}
