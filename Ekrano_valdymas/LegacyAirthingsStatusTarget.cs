using System.Text;
using System.Text.Json;

namespace Ekrano_valdymas;

// The room-status mini was retired. Only recognize old placements so they can
// be cleared safely and their custom sensor limits migrated to the gauge view.
internal static class LegacyAirthingsStatusTarget
{
    internal const string Prefix="airthings-status:";
    internal static bool IsTarget(string? target)=>target?.StartsWith(Prefix,StringComparison.Ordinal)==true;
    internal static AirthingsStatusOptions Decode(string? target)
    {
        try
        {
            return (IsTarget(target)&&target!.Length>Prefix.Length
                ? JsonSerializer.Deserialize<AirthingsStatusOptions>(Encoding.UTF8.GetString(Convert.FromBase64String(target[Prefix.Length..])))
                : null)?.Normalize()??new();
        }
        catch{return new();}
    }
}
