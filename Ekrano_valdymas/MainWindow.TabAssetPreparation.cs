using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Ekrano_valdymas;
public partial class MainWindow
{
    private readonly CancellationTokenSource tabAssetPreparation = new();
    private Task? tabAssetPreparationTask;
    private void StartTabAssetPreparation()
    {
        if (tabAssetPreparationTask != null || DiagnosticState.Root != null) return;
        tabAssetPreparationTask = PrepareTabAssetsAsync();
    }
    private async Task PrepareTabAssetsAsync()
    {
        var cancellation = tabAssetPreparation.Token;
        try
        {
            // Let the initial frame and pending user input finish first.
            await Task.Delay(750, cancellation);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle, cancellation);
            var finishes = dashboardLayouts.Take(quickLaunchBar.VisibleLayoutTabCount)
                .Where(p => p?.MusicConsoleEnabled == true).Select(p => p.Player?.Finish ?? "dark")
                .Distinct().Take(2).ToArray();
            foreach (var finish in finishes)
            {
                cancellation.ThrowIfCancellationRequested();
                await MusicConsoleControl.PreparePanelAsync(finish, cancellation);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle, cancellation);
            }
            string root = Path.Combine(DiagnosticState.LocalApplicationData, "Ekrano_valdymas");
            await Task.Run(() =>
            {
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                // Read only saved icon fields; never open shortcut targets or media.
                foreach (string file in new[] { "quick-launch.json", "top-bar-tabs-v1.json", "macros.json", "web-shortcuts.json" })
                {
                    cancellation.ThrowIfCancellationRequested();
                    try
                    {
                        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, file)));
                        Collect(document.RootElement, false);
                    }
                    catch (IOException) { }
                    catch (JsonException) { }
                }
                foreach (string path in paths.Take(64))
                {
                    cancellation.ThrowIfCancellationRequested();
                    IconBitmap.Load(path, 128);
                    cancellation.ThrowIfCancellationRequested();
                    IconBitmap.Load(path, 192);
                }
                void Collect(JsonElement node, bool icon)
                {
                    if (node.ValueKind == JsonValueKind.Object)
                        foreach (var property in node.EnumerateObject())
                            Collect(property.Value, icon || property.Name is "Icon" or "IconPath" or "StateIcons" || property.Name.EndsWith("Icon", StringComparison.Ordinal));
                    else if (node.ValueKind == JsonValueKind.Array)
                        foreach (var value in node.EnumerateArray()) Collect(value, icon);
                    else if (icon && node.ValueKind == JsonValueKind.String && node.GetString() is string value)
                    {
                        const string prefix = "blakideck-icon:";
                        string? path = value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                            ? DefaultIconCatalog.GetPath(value[prefix.Length..]) : value;
                        if (path != null && Path.IsPathFullyQualified(path) && !path.StartsWith(@"\\") && File.Exists(path)) paths.Add(path);
                    }
                }
            }, cancellation);
            InteractionDiagnostics.Log("TabAssets preparation complete");
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { InteractionDiagnostics.Log("TabAssets preparation skipped: " + error.GetType().Name); }
    }
}
