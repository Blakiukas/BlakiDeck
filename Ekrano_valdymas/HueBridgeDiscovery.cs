using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace Ekrano_valdymas
{
    internal sealed record HueBridgeInfo(string Id, string InternalIpAddress);

    internal static class HueBridgeDiscovery
    {
        private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(8) };

        internal static async Task<IReadOnlyList<HueBridgeInfo>> DiscoverAsync(
            CancellationToken cancellationToken = default)
        {
            using HttpResponseMessage response = await Client.GetAsync(
                "https://discovery.meethue.com/", cancellationToken);
            response.EnsureSuccessStatusCode();
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            JsonElement root = await JsonSerializer.DeserializeAsync<JsonElement>(stream,
                cancellationToken: cancellationToken);
            if (root.ValueKind != JsonValueKind.Array) return [];

            List<HueBridgeInfo> bridges = [];
            foreach (JsonElement item in root.EnumerateArray())
            {
                string? id = item.TryGetProperty("id", out JsonElement idValue) ? idValue.GetString() : null;
                string? ip = item.TryGetProperty("internalipaddress", out JsonElement ipValue)
                    ? ipValue.GetString() : null;
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(ip))
                    bridges.Add(new HueBridgeInfo(id, ip));
            }
            return bridges;
        }
    }
}
