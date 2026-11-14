// Adapted from the actual BlakiHUD CodexUsageService.cs, 2026-10-17.
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Ekrano_valdymas;

public sealed record CodexUsageSnapshot(double? FiveHourRemainingPercent,
    double? WeeklyRemainingPercent, DateTime? FiveHourResetsAt,
    DateTime? WeeklyResetsAt, DateTime RetrievedAt, bool Synthetic=false)
{
    // Match BlakiHUD DrawCodexUsage: a single valid allowance fills the card.
    // A duration/reset timestamp alone must not create a second allowance.
    internal (string Label,double? Remaining,DateTime? Reset)[] Windows
    {
        get
        {
            (string Label,double? Remaining,DateTime? Reset)[] windows=[("5H",FiveHourRemainingPercent,FiveHourResetsAt),("WEEK",WeeklyRemainingPercent,WeeklyResetsAt)];
            bool single=FiveHourRemainingPercent.HasValue!=WeeklyRemainingPercent.HasValue;
            return single?windows.Where(w=>w.Remaining.HasValue).ToArray():windows;
        }
    }
}

public sealed class CodexUsageService
{
    public static bool IsAvailable => ResolveCodexExecutable() is not null;

    public async Task<CodexUsageSnapshot?> ReadAsync(CancellationToken cancellationToken=default)
    {
        var executable = ResolveCodexExecutable();
        if (executable is null) return null;
        using var process = new Process { StartInfo = new ProcessStartInfo
        {
            FileName = executable, Arguments = "app-server --stdio",
            UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true
        }};
        try
        {
            if (!process.Start()) return null;
            // Drain without retaining logs: a full error pipe must not block the server.
            var drainErrors = DrainErrorsAsync(process.StandardError);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
            {
                method = "initialize", id = 1, @params = new { clientInfo = new
                { name = "blakideck", title = "BlakiDeck", version = "1.0" } }
            }));
            if (await ReadResponseAsync(process.StandardOutput, 1, timeout.Token) is null)
                return null;
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(
                new { method = "initialized", @params = new { } }));
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(
                new { method = "account/rateLimits/read", id = 2 }));
            await process.StandardInput.FlushAsync();
            var result = await ReadResponseAsync(process.StandardOutput, 2, timeout.Token);
            return result is { } response ? Parse(response) : null;
        }
        catch { }
        finally { try { if (!process.HasExited) process.Kill(true); } catch { } }
        return null;
    }

    private static string? ResolveCodexExecutable()
    {
        try
        {
            var binRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenAI", "Codex", "bin");
            var newest = Directory.Exists(binRoot)
                ? Directory.EnumerateFiles(binRoot, "codex.exe", SearchOption.AllDirectories)
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .FirstOrDefault()
                : null;
            if (newest is not null) return newest.FullName;

            foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                         .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(directory.Trim(), "codex.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch { }
        return null;
    }

    private static async Task DrainErrorsAsync(StreamReader reader)
    {
        try { var buffer = new char[2048]; while (await reader.ReadAsync(buffer) > 0) { } }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    private static async Task<JsonElement?> ReadResponseAsync(StreamReader reader, int requestId,
        CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number ||
                !id.TryGetInt32(out var number) || number != requestId) continue;
            return root.TryGetProperty("result", out var result) ? result.Clone() : null;
        }
        return null;
    }

    internal static CodexUsageSnapshot? Parse(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object) return null;
        JsonElement limits;
        if (result.TryGetProperty("rateLimitsByLimitId", out var buckets) &&
            buckets.ValueKind == JsonValueKind.Object && buckets.TryGetProperty("codex", out var general))
            limits = general;
        else if (!result.TryGetProperty("rateLimits", out limits)) return null;
        if (limits.ValueKind != JsonValueKind.Object) return null;
        // Never substitute a model-specific allowance for the general Codex allowance.
        if (limits.TryGetProperty("limitId", out var limitId) && limitId.ValueKind != JsonValueKind.Null &&
            (limitId.ValueKind != JsonValueKind.String || limitId.GetString() != "codex")) return null;

        (double? Remaining, DateTime? Reset) five = default, week = default;
        var fiveSeen = false;
        var weekSeen = false;
        foreach (var name in new[] { "primary", "secondary" })
        {
            if (!limits.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object ||
                !window.TryGetProperty("windowDurationMins", out var duration) ||
                duration.ValueKind != JsonValueKind.Number || !duration.TryGetInt32(out var minutes)) continue;
            double? remaining = null;
            if (window.TryGetProperty("usedPercent", out var used) && used.ValueKind == JsonValueKind.Number &&
                used.TryGetDouble(out var percent) && double.IsFinite(percent) && percent >= 0 && percent <= 100)
                remaining = 100 - percent;
            DateTime? reset = null;
            if (window.TryGetProperty("resetsAt", out var time) && time.ValueKind == JsonValueKind.Number &&
                time.TryGetInt64(out var seconds) && seconds >= 0 && seconds <= 253402300799)
                reset = DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime;
            // Duplicate durations are ambiguous; do not silently pick one.
            if (minutes == 300) { five = fiveSeen ? default : (remaining, reset); fiveSeen = true; }
            if (minutes == 10080) { week = weekSeen ? default : (remaining, reset); weekSeen = true; }
        }
        return new(five.Remaining, week.Remaining, five.Reset, week.Reset, DateTime.Now);
    }
}
