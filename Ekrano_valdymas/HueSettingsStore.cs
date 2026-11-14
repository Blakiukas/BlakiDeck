using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ekrano_valdymas
{
    internal sealed class HueSettings
    {
        public string? BridgeId { get; set; }
        public string? BridgeIp { get; set; }
        public string? ProtectedApplicationKey { get; set; }
        public string[]? SelectedGroupedLightIds { get; set; }
        public Dictionary<string, string>? SelectedRoomColors { get; set; }
    }

    internal sealed class HueSettingsStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("BlakiDeck.Hue.v1");
        internal string FilePath { get; } = Path.Combine(
            DiagnosticState.LocalApplicationData,
            "Ekrano_valdymas", "hue-settings.json");

        internal HueSettings? Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                string json = File.ReadAllText(FilePath);
                HueSettings? settings = JsonSerializer.Deserialize<HueSettings>(json);
                if (!string.IsNullOrWhiteSpace(settings?.BridgeId)) return settings;
                using JsonDocument legacy = JsonDocument.Parse(json);
                JsonElement root = legacy.RootElement;
                return new HueSettings
                {
                    BridgeId = root.TryGetProperty("Id", out JsonElement id) ? id.GetString() : null,
                    BridgeIp = root.TryGetProperty("InternalIpAddress", out JsonElement ip) ? ip.GetString() : null
                };
            }
            catch { return null; }
        }

        internal void Save(HueBridgeInfo bridge, string? applicationKey = null)
        {
            HueSettings previous = Load() ?? new HueSettings();
            HueSettings settings = new()
            {
                BridgeId = bridge.Id,
                BridgeIp = bridge.InternalIpAddress,
                ProtectedApplicationKey = applicationKey == null
                    ? previous.ProtectedApplicationKey
                    : Convert.ToBase64String(ProtectedData.Protect(
                        Encoding.UTF8.GetBytes(applicationKey), Entropy,
                        DataProtectionScope.CurrentUser)),
                SelectedGroupedLightIds = previous.SelectedGroupedLightIds,
                SelectedRoomColors = previous.SelectedRoomColors
            };
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings));
        }

        internal void SaveSelection(IEnumerable<string> groupedLightIds)
        {
            HueSettings settings = Load() ?? new HueSettings();
            settings.SelectedGroupedLightIds = groupedLightIds.Distinct().ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings));
        }

        internal void SaveRoomColor(string groupedLightId, string color)
        {
            HueSettings settings = Load() ?? new HueSettings();
            settings.SelectedRoomColors ??= new Dictionary<string, string>();
            settings.SelectedRoomColors[groupedLightId] = color;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings));
        }

        internal string? UnprotectKey(HueSettings settings)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(settings.ProtectedApplicationKey)) return null;
                byte[] clear = ProtectedData.Unprotect(
                    Convert.FromBase64String(settings.ProtectedApplicationKey), Entropy,
                    DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(clear);
            }
            catch { return null; }
        }
    }
}
