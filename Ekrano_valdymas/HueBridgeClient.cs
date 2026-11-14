using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text;

namespace Ekrano_valdymas
{
    internal sealed class HueLinkButtonRequiredException : Exception { }
    internal sealed record HueRoom(
        string Name, string GroupedLightId, bool IsOn, double Brightness,
        double? ColorX, double? ColorY, bool SupportsColor,
        bool SupportsColorTemperature, int? ColorTemperatureMirek, bool? IsReachable = null)
    {
        internal HueLight[] Lights { get; init; } = [];
        public override string ToString()=>Name;
    }

    internal sealed class HueBridgeClient : IDisposable
    {
        private readonly HueBridgeInfo bridge;
        private readonly HttpClient client;

        internal HueBridgeClient(HueBridgeInfo bridge)
        {
            this.bridge = bridge;
            HttpClientHandler handler = new();
            handler.ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
                errors == System.Net.Security.SslPolicyErrors.None ||
                IsExpectedBridgeCertificate(certificate, bridge.Id);
            client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        }

        internal async Task<string> CreateApplicationKeyAsync(
            CancellationToken cancellationToken = default)
        {
            string deviceName = Environment.MachineName
                .Replace(' ', '-')
                .ToLowerInvariant();
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                $"https://{bridge.InternalIpAddress}/api",
                new { devicetype = $"blakideck#{deviceName}", generateclientkey = true },
                cancellationToken);
            response.EnsureSuccessStatusCode();
            JsonElement root = await response.Content.ReadFromJsonAsync<JsonElement>(
                cancellationToken: cancellationToken);
            foreach (JsonElement item in root.EnumerateArray())
            {
                if (item.TryGetProperty("success", out JsonElement success) &&
                    success.TryGetProperty("username", out JsonElement username))
                    return username.GetString() ?? throw new InvalidOperationException("Empty Hue key.");
                if (item.TryGetProperty("error", out JsonElement error) &&
                    error.TryGetProperty("type", out JsonElement type) && type.GetInt32() == 101)
                    throw new HueLinkButtonRequiredException();
            }
            throw new InvalidOperationException("Hue Bridge rejected the pairing request.");
        }

        internal async Task<bool> ValidateApplicationKeyAsync(string applicationKey,
            CancellationToken cancellationToken = default)
        {
            using HttpRequestMessage request = new(HttpMethod.Get,
                $"https://{bridge.InternalIpAddress}/clip/v2/resource/bridge");
            request.Headers.Add("hue-application-key", applicationKey);
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }

        internal async Task<IReadOnlyList<HueRoom>> GetRoomsAsync(string applicationKey,
            CancellationToken cancellationToken = default)
        {
            var resources = await Task.WhenAll(new[]{"room","grouped_light","light","zigbee_connectivity"}.Select(resource=>GetV2Async(resource,applicationKey,cancellationToken)));
            return ReadRooms(resources[0],resources[1],resources[2],resources[3]);
        }

        internal static IReadOnlyList<HueRoom> ReadRooms(JsonElement roomsRoot,JsonElement lightsRoot,JsonElement individualRoot,JsonElement connectivityRoot)
        {
            var connections = connectivityRoot.GetProperty("data").EnumerateArray()
                .Where(c=>c.TryGetProperty("owner",out _)&&c.TryGetProperty("status",out _))
                .GroupBy(c=>c.GetProperty("owner").GetProperty("rid").GetString()!)
                .ToDictionary(g=>g.Key,g=>g.Any(c=>c.GetProperty("status").GetString()=="connected"));
            Dictionary<string, (bool On, double Brightness, double? X, double? Y,
                bool SupportsColor, bool SupportsColorTemperature, int? Mirek)> states = [];
            foreach (JsonElement light in lightsRoot.GetProperty("data").EnumerateArray())
            {
                string? id = light.GetProperty("id").GetString();
                if (id == null) continue;
                bool on = light.TryGetProperty("on", out JsonElement onObject) &&
                          onObject.TryGetProperty("on", out JsonElement onValue) && onValue.GetBoolean();
                double brightness = light.TryGetProperty("dimming", out JsonElement dimming) &&
                                    dimming.TryGetProperty("brightness", out JsonElement value)
                    ? value.GetDouble() : 0;
                double? colorX = null;
                double? colorY = null;
                bool supportsColor = light.TryGetProperty("color", out JsonElement color);
                if (supportsColor &&
                    color.TryGetProperty("xy", out JsonElement xy))
                {
                    if (xy.TryGetProperty("x", out JsonElement x)) colorX = x.GetDouble();
                    if (xy.TryGetProperty("y", out JsonElement y)) colorY = y.GetDouble();
                }
                bool supportsColorTemperature = light.TryGetProperty(
                    "color_temperature", out JsonElement colorTemperature);
                int? mirek = supportsColorTemperature &&
                             colorTemperature.TryGetProperty("mirek", out JsonElement mirekValue) &&
                             mirekValue.ValueKind == JsonValueKind.Number
                    ? mirekValue.GetInt32()
                    : null;
                states[id] = (on, brightness, colorX, colorY, supportsColor,
                    supportsColorTemperature, mirek);
            }

            List<HueRoom> rooms = [];
            foreach (JsonElement room in roomsRoot.GetProperty("data").EnumerateArray())
            {
                string name = room.GetProperty("metadata").GetProperty("name").GetString() ?? "Room";
                string? groupedLightId = room.GetProperty("services").EnumerateArray()
                    .Where(service => service.TryGetProperty("rtype", out JsonElement type) &&
                                      type.GetString() == "grouped_light")
                    .Select(service => service.GetProperty("rid").GetString())
                    .FirstOrDefault();
                if (groupedLightId == null || !states.TryGetValue(groupedLightId, out var state)) continue;
                var devices=room.GetProperty("children").EnumerateArray().Where(c=>c.GetProperty("rtype").GetString()=="device").Select(c=>c.GetProperty("rid").GetString()).ToHashSet();
                var members=individualRoot.GetProperty("data").EnumerateArray().Where(l=>l.TryGetProperty("owner",out var owner)&&devices.Contains(owner.GetProperty("rid").GetString())).ToArray();
                var connected=members.Where(l=>connections.GetValueOrDefault(l.GetProperty("owner").GetProperty("rid").GetString()!)).ToArray();
                bool? reachable=connected.Length>0?true:members.Length>0&&members.All(l=>connections.ContainsKey(l.GetProperty("owner").GetProperty("rid").GetString()!))?false:null;
                // Grouped state can retain ON indefinitely when mains power is removed.
                // Use reachable members when available; preserve last reported data if unknown/offline.
                bool on=connected.Length>0?connected.Any(l=>l.GetProperty("on").GetProperty("on").GetBoolean()):state.On;
                rooms.Add(new HueRoom(name, groupedLightId, on, state.Brightness,
                    state.X, state.Y, state.SupportsColor, state.SupportsColorTemperature,
                    state.Mirek, reachable) { Lights = members.Select(l => HueLight.Read(l, connections)).ToArray() });
            }
            return rooms.OrderBy(room => room.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }

        internal Task SetRoomOnAsync(string applicationKey, string groupedLightId, bool on,
            CancellationToken cancellationToken = default) =>
            PutV2Async($"grouped_light/{groupedLightId}", applicationKey,
                JsonSerializer.Serialize(new { on = new { on } }), cancellationToken);

        internal Task SetRoomBrightnessAsync(string applicationKey, string groupedLightId,
            double brightness, CancellationToken cancellationToken = default) =>
            PutV2Async($"grouped_light/{groupedLightId}", applicationKey,
                JsonSerializer.Serialize(new
                {
                    on = new { on = true },
                    dimming = new { brightness = Math.Clamp(brightness, 0.1, 100) }
                }), cancellationToken);

        internal Task SetRoomColorAsync(string applicationKey, string groupedLightId,
            double x, double y, CancellationToken cancellationToken = default) =>
            PutV2Async($"grouped_light/{groupedLightId}", applicationKey,
                JsonSerializer.Serialize(new
                {
                    on = new { on = true },
                    color = new { xy = new { x = Math.Clamp(x, 0, 1), y = Math.Clamp(y, 0, 1) } }
                }), cancellationToken);

        internal Task SetLightOnAsync(string applicationKey, string lightId, bool on,
            CancellationToken cancellationToken = default) =>
            PutV2Async($"light/{lightId}", applicationKey,
                JsonSerializer.Serialize(new { on = new { on } }), cancellationToken);

        internal Task SetLightColorsAsync(string applicationKey, HueLight light, HuePoint[] points,
            CancellationToken cancellationToken = default) =>
            PutV2Async($"light/{light.Id}", applicationKey, LightColorsPayload(light, points), cancellationToken);

        internal static string LightColorsPayload(HueLight light, HuePoint[] points)
        {
            if (!light.SupportsColor || points.Length == 0 || points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || p.X < 0 || p.Y <= 0 || p.X > 1 || p.Y > 1))
                throw new ArgumentException("Invalid light color.");
            if (points.Length > 1 && light.GradientCapacity >= 2)
            {
                if (points.Length < 2 || points.Length > light.GradientCapacity) throw new ArgumentException("Unsupported gradient size.");
                return JsonSerializer.Serialize(new { gradient = new { points = points.Select(p => new { color = new { xy = new { x = p.X, y = p.Y } } }) } });
            }
            if (points.Length != 1) throw new ArgumentException("This light supports one color.");
            return JsonSerializer.Serialize(new { color = new { xy = new { x = points[0].X, y = points[0].Y } } });
        }

        private async Task<JsonElement> GetV2Async(string resource, string applicationKey,
            CancellationToken cancellationToken)
        {
            using HttpRequestMessage request = new(HttpMethod.Get,
                $"https://{bridge.InternalIpAddress}/clip/v2/resource/{resource}");
            request.Headers.Add("hue-application-key", applicationKey);
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        }

        private async Task PutV2Async(string resource, string applicationKey, string json,
            CancellationToken cancellationToken)
        {
            using HttpRequestMessage request = new(HttpMethod.Put,
                $"https://{bridge.InternalIpAddress}/clip/v2/resource/{resource}");
            request.Headers.Add("hue-application-key", applicationKey);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            JsonElement result = await response.Content.ReadFromJsonAsync<JsonElement>(
                cancellationToken: cancellationToken);
            if (result.TryGetProperty("errors", out JsonElement errors) &&
                errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            {
                string description = errors[0].TryGetProperty("description", out JsonElement message)
                    ? message.GetString() ?? "Hue Bridge rejected the command."
                    : "Hue Bridge rejected the command.";
                throw new InvalidOperationException(description);
            }
        }

        private static bool IsExpectedBridgeCertificate(X509Certificate2? certificate, string bridgeId)
        {
            if (certificate == null || DateTime.Now < certificate.NotBefore || DateTime.Now > certificate.NotAfter)
                return false;
            string normalizedId = bridgeId.Replace(":", "", StringComparison.Ordinal).ToLowerInvariant();
            string subject = certificate.Subject.Replace(":", "", StringComparison.Ordinal).ToLowerInvariant();
            return subject.Contains(normalizedId, StringComparison.Ordinal);
        }

        public void Dispose() => client.Dispose();
    }
}
