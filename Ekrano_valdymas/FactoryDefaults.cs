using System.IO;
using System.Text.Json.Nodes;
namespace Ekrano_valdymas;

// User-approved visual configuration, captured independently from private profiles.
internal static class FactoryDefaults
{
    internal static T Read<T>(string name)
    {
        using var stream=typeof(FactoryDefaults).Assembly.GetManifestResourceStream("BlakiDeck.FactoryDefaults.json")!;
        var files=JsonNode.Parse(stream)!.AsObject();
        return System.Text.Json.JsonSerializer.Deserialize<T>(files[name]!.ToJsonString())!;
    }
    internal static void Seed(string folder)
    {
        using var stream=typeof(FactoryDefaults).Assembly.GetManifestResourceStream("BlakiDeck.FactoryDefaults.json")
            ?? throw new InvalidOperationException("Factory defaults are missing.");
        var files=JsonNode.Parse(stream)!.AsObject();
        Directory.CreateDirectory(folder);
        foreach(var entry in files)
        {
            if(Path.GetFileName(entry.Key)!=entry.Key)throw new InvalidDataException("Invalid factory file name.");
            string path=Path.Combine(folder,entry.Key);
            if(File.Exists(path))continue;
            var value=entry.Value!.DeepClone();ResolveIcons(value);
            AtomicJson.Write(path,value);
        }
    }
    private static void ResolveIcons(JsonNode node)
    {
        if(node is JsonObject obj)
            foreach(var key in obj.Select(p=>p.Key).ToArray())
            {
                if(obj[key] is JsonValue v&&v.TryGetValue<string>(out var text)&&text.StartsWith("$DEFAULT_ICONS/",StringComparison.Ordinal))
                    obj[key]=DefaultIconCatalog.GetPath(text[15..])??throw new InvalidDataException("Missing factory icon.");
                else if(obj[key] is {} child)ResolveIcons(child);
            }
        else if(node is JsonArray array)foreach(var child in array)if(child!=null)ResolveIcons(child);
    }
}
