using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace Ekrano_valdymas;

public sealed class WeatherLocation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string AutomaticName { get; set; } = "";
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool FromCoordinates { get; set; }
    public bool NameEdited { get; set; }
    public bool AutomaticNameChanged { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string Icon => NameEdited ? "\uE70F" : FromCoordinates ? "\uE707" : "";
    [System.Text.Json.Serialization.JsonIgnore]
    public string Coordinates => Latitude is double lat && Longitude is double lon
        ? string.Create(CultureInfo.InvariantCulture, $"{lat:0.######}, {lon:0.######}") : "";
    [System.Text.Json.Serialization.JsonIgnore]
    public string Description => NameEdited
        ? LanguageManager.Text("Edited name", "Redaguotas pavadinimas") + (FromCoordinates ? " · " + Coordinates : "")
        : FromCoordinates ? LanguageManager.Text("Name from coordinates", "Pavadinimas pagal koordinates") + " · " + Coordinates
        : AutomaticNameChanged ? LanguageManager.Text("Automatically matched name", "Automatiškai patikslintas pavadinimas") : Name;
    internal string DescriptionFor(string language)
    {
        var translations=new WeatherTranslationStore();
        return NameEdited?translations.Text(language,"Edited name")+(FromCoordinates?" · "+Coordinates:""):
            FromCoordinates?translations.Text(language,"Name from coordinates")+" · "+Coordinates:
            AutomaticNameChanged?translations.Text(language,"Automatically matched name"):Name;
    }

    public void Rename(string name)
    {
        Name = name.Trim();
        NameEdited = !string.Equals(Name, AutomaticName, StringComparison.Ordinal);
    }

    public static bool TryCoordinates(string? value, out double latitude, out double longitude)
    {
        latitude = longitude = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        // Semicolon also permits decimal commas: 59,668; 9,65.
        bool semicolon = value.Contains(';');
        string[] parts = value.Split(semicolon ? ';' : ',', StringSplitOptions.TrimEntries);
        return parts.Length == 2 &&
            double.TryParse(semicolon ? parts[0].Replace(',', '.') : parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out latitude) &&
            double.TryParse(semicolon ? parts[1].Replace(',', '.') : parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out longitude) &&
            double.IsFinite(latitude) && double.IsFinite(longitude) && latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;
    }
}

internal static class WeatherLocationLookup
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly System.Threading.SemaphoreSlim Gate = new(1, 1);
    private static DateTime lastRequest;
    static WeatherLocationLookup() => Client.DefaultRequestHeaders.UserAgent.ParseAdd("BlakiDeck/1.0");

    // User-triggered Add/Save or a debounced, valid coordinate entry.
    // Selection and periodic weather refresh reuse the saved location.
    internal static async Task<WeatherLocation> Resolve(string input, bool coordinates,string language="en")
    {
        double latitude = 0, longitude = 0;
        if (coordinates && !WeatherLocation.TryCoordinates(input, out latitude, out longitude))
            throw new ArgumentException("Use latitude, longitude (for example 59.668, 9.65).");
        if (!coordinates && string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Enter a city.");
        string url = coordinates
            ? string.Create(CultureInfo.InvariantCulture, $"https://photon.komoot.io/reverse?lat={latitude}&lon={longitude}&limit=1&radius=25")
            : "https://geocoding-api.open-meteo.com/v1/search?count=1&language=" + (language=="lt" ? "lt" : "en") + "&name=" + Uri.EscapeDataString(input.Trim());
        await Gate.WaitAsync();
        try
        {
            var delay = TimeSpan.FromSeconds(1) - (DateTime.UtcNow - lastRequest);
            if (delay > TimeSpan.Zero) await Task.Delay(delay);
            lastRequest = DateTime.UtcNow;
            using var document = JsonDocument.Parse(await Client.GetStringAsync(url));
            string? name;
            if (coordinates)
                name = ParseReverseName(document.RootElement);
            else
            {
                if (!document.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                    throw new ArgumentException("City not found. Try a more specific name.");
                var place = results[0];
                name = place.GetProperty("name").GetString();
                latitude = place.GetProperty("latitude").GetDouble();
                longitude = place.GetProperty("longitude").GetDouble();
            }
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("No town found near these coordinates. Try another point.");
            return new WeatherLocation { Name = name, AutomaticName = name, Latitude = latitude, Longitude = longitude,
                FromCoordinates = coordinates, AutomaticNameChanged = !coordinates && name != input.Trim() };
        }
        finally { Gate.Release(); }
    }

    internal static string? ParseReverseName(JsonElement root)
    {
        if (!root.TryGetProperty("features", out var features)) return null;
        foreach (var feature in features.EnumerateArray())
        {
            var properties = feature.GetProperty("properties");
            foreach (string key in new[] { "city", "town", "village", "municipality" })
                if (properties.TryGetProperty(key, out var value) && !string.IsNullOrWhiteSpace(value.GetString())) return value.GetString();
            if (properties.TryGetProperty("osm_value", out var kind) && kind.GetString() is "city" or "town" or "village" or "hamlet" &&
                properties.TryGetProperty("name", out var name)) return name.GetString();
        }
        return null;
    }
}
