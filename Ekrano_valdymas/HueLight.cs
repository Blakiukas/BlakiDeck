using System.Text.Json;
namespace Ekrano_valdymas;
internal sealed record HuePoint(double X, double Y);
internal sealed record HueLight(string Id, string Name, bool SupportsColor, HuePoint? Color,
    int? Mirek, int GradientCapacity, HuePoint[] Gradient, bool? Reachable)
{
    internal bool SupportsPower { get; init; }
    internal bool IsOn { get; init; }
    internal int ColorChoiceCount => Mirek == null && GradientCapacity >= 2 && Gradient.Length >= 2
        ? Math.Min(GradientCapacity, Gradient.Length) : 1;
    internal static HuePoint? ReadPoint(JsonElement element)
    {
        if (element.TryGetProperty("xy", out var xy) && xy.TryGetProperty("x", out var x) && xy.TryGetProperty("y", out var y)
            && x.TryGetDouble(out double px) && y.TryGetDouble(out double py) && double.IsFinite(px) && double.IsFinite(py) && py > 0)
            return new(px, py);
        return null;
    }
    internal static HueLight Read(JsonElement light, Dictionary<string,bool> connections)
    {
        string id = light.TryGetProperty("id", out var idNode) ? idNode.GetString() ?? "" : "";
        string name = light.TryGetProperty("metadata", out var metadata) && metadata.TryGetProperty("name", out var n) ? n.GetString() ?? "Light" : "Light";
        bool color = light.TryGetProperty("color", out var c);
        int? mirek = null;
        if (light.TryGetProperty("color_temperature", out var ct) && ct.TryGetProperty("mirek_valid", out var valid) && valid.ValueKind == JsonValueKind.True
            && ct.TryGetProperty("mirek", out var m) && m.TryGetInt32(out int value) && value > 0) mirek = value;
        int capacity = 0; HuePoint[] points = [];
        if (light.TryGetProperty("gradient", out var gradient))
        {
            if (gradient.TryGetProperty("points_capable", out var cap)) cap.TryGetInt32(out capacity);
            if (gradient.TryGetProperty("points", out var list) && list.ValueKind == JsonValueKind.Array)
                points = list.EnumerateArray().Where(p => p.TryGetProperty("color", out _)).Select(p => ReadPoint(p.GetProperty("color"))).OfType<HuePoint>().ToArray();
        }
        string owner = light.GetProperty("owner").GetProperty("rid").GetString()!;
        return new(id, name, color, color ? ReadPoint(c) : null, mirek, capacity, points,
            connections.TryGetValue(owner, out bool reachable) ? reachable : null) {
                SupportsPower=light.TryGetProperty("on",out var power)&&power.TryGetProperty("on",out _),
                IsOn=light.TryGetProperty("on",out var on)&&on.TryGetProperty("on",out var state)&&state.ValueKind==JsonValueKind.True
            };
    }
}
