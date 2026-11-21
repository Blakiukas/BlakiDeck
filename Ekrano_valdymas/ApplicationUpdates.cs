using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Ekrano_valdymas;

internal static class ApplicationUpdates
{
    internal const string Repository = "Blakiukas/BlakiDeck";
    private static readonly HttpClient client = CreateClient();
    private static bool checking;
    private static Window? updateWindow;
    internal static Version? AvailableVersion { get; private set; }
    internal sealed record Release(Version Version, string Url, string Digest, long Size, string Notes);
    private static HttpClient CreateClient()
    {
        var result = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        result.DefaultRequestHeaders.UserAgent.ParseAdd("BlakiDeck-Updater/1.0"); return result;
    }
    internal static Release? ParseRelease(string json, Version current)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        if (!Version.TryParse(root.GetProperty("tag_name").GetString()?.TrimStart('v'), out var version) || version <= new Version(current.Major, current.Minor, Math.Max(0, current.Build))) return null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? "";
            if (name != $"BlakiDeck-{version}-Clean-Setup-x64.exe") continue;
            string url = asset.GetProperty("browser_download_url").GetString() ?? "";
            string digest = asset.TryGetProperty("digest", out var hash) ? hash.GetString() ?? "" : "";
            if (!url.StartsWith($"https://github.com/{Repository}/releases/download/v{version}/", StringComparison.Ordinal) || !digest.StartsWith("sha256:") || digest.Length != 71 || !digest[7..].All(Uri.IsHexDigit)) throw new InvalidDataException("The release has no valid installer verification data.");
            return new(version, url, digest[7..], asset.GetProperty("size").GetInt64(), root.TryGetProperty("body", out var notes) ? notes.GetString() ?? "" : "");
        }
        throw new InvalidDataException("The release installer is not available yet.");
    }
    internal static async Task CheckAsync(Window owner, bool manual)
    {
        if (checking || DiagnosticState.Root != null) return;
        if (manual && updateWindow != null) { updateWindow.Activate(); return; }
        if (PackageIdentity.IsPackagedProcess()) { if (manual) ShowStatus(owner, "Use Microsoft Store to update this installation.", "Šią programos versiją atnaujinkite per Microsoft Store."); return; }
        string stamp = Path.Combine(DiagnosticState.LocalApplicationData, "Ekrano_valdymas", "update-check.json");
        if (!manual) { try { if (File.Exists(stamp) && DateTime.UtcNow - JsonSerializer.Deserialize<DateTime>(File.ReadAllText(stamp)) < TimeSpan.FromDays(1)) return; } catch { } }
        checking = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            string json = await client.GetStringAsync($"https://api.github.com/repos/{Repository}/releases/latest", timeout.Token);
            var release = ParseRelease(json, typeof(ApplicationUpdates).Assembly.GetName().Version!); AtomicJson.Write(stamp, DateTime.UtcNow);
            AvailableVersion = release?.Version;
            if (release == null) { if (manual) ShowStatus(owner, "BlakiDeck is up to date.", "BlakiDeck jau atnaujinta."); return; }
            if (!manual)
            {
                // AvailableVersion is shown in About; background checks never interrupt work.
                return;
            }
            ShowDownload(owner, release);
        }
        catch (Exception error) { if (manual) ShowStatus(owner, "Could not check for updates.\n" + error.Message, "Nepavyko patikrinti atnaujinimų.\n" + error.Message); }
        finally { checking = false; }
    }
    internal static SettingsWindow CreateStatus(Window owner, string message, bool lithuanian)
    {
        var dialog = AppearanceEditorUi.Window(lithuanian ? "Atnaujinimai" : "Updates", owner, 230);
        dialog.Width = 520;
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 18, Margin = new Thickness(0, 4, 0, 24) });
        var close = MacroUi.Button(lithuanian ? "Gerai" : "OK", (_, _) => dialog.Close());
        close.IsDefault = true; close.IsCancel = true; close.MinWidth = 100; close.HorizontalAlignment = HorizontalAlignment.Right;
        panel.Children.Add(close); dialog.Content = panel; AppearanceEditorUi.Frame(dialog, 190, 420); return dialog;
    }
    private static void ShowStatus(Window owner, string english, string lithuanian)
    {
        bool lt = (owner as AboutWindow)?.DisplayLanguage == "lt" || owner is not AboutWindow && LanguageSwitch.CurrentLanguage == "lt";
        CreateStatus(owner, lt ? lithuanian : english, lt).ShowDialog();
    }
    private static void ShowDownload(Window owner, Release release)
    {
        var window = AppearanceEditorUi.Window("BlakiDeck update", owner, 420);
        updateWindow = window;
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = $"Version {release.Version} · {release.Size / 1048576d:0} MB", FontSize = 22 });
        panel.Children.Add(new TextBlock { Text = "Download from GitHub. Your settings will be kept.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) });
        panel.Children.Add(new ScrollViewer { MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new TextBlock { Text = release.Notes, TextWrapping = TextWrapping.Wrap } });
        var status = new TextBlock { Text = "Ready to download", TextWrapping = TextWrapping.Wrap }; panel.Children.Add(status);
        var progress = new ProgressBar { Height = 12, Margin = new Thickness(0, 12, 0, 12), Maximum = 100, Foreground = System.Windows.Media.Brushes.DarkOrange, Background = System.Windows.Media.Brushes.DimGray }; panel.Children.Add(progress);
        var cancel = new CancellationTokenSource(); string? installer = null; bool downloading = false;
        var download = MacroUi.Button("Download", (_, _) => { }); panel.Children.Add(download);
        var install = MacroUi.Button("Install and restart", (_, _) =>
        {
            if (installer == null) return;
            try
            {
                // NSIS waits on the app mutex; the user's profile is saved by normal shutdown.
                using var process = Process.Start(new ProcessStartInfo(installer) { Arguments = $"/WAITPID={Environment.ProcessId}", UseShellExecute = true }) ?? throw new IOException("Could not start Setup.");
                window.Close(); Application.Current.Shutdown();
            }
            catch (Exception error) { status.Text = error.Message; }
        }); install.IsEnabled = false; install.Opacity = .45; panel.Children.Add(install);
        panel.Children.Add(MacroUi.Button("Later", (_, _) => window.Close()));
        download.Click += async (_, _) =>
        {
            if (downloading) return; downloading = true; download.IsEnabled = false;
            string folder = Path.Combine(Path.GetTempPath(), "BlakiDeck-update"); Directory.CreateDirectory(folder);
            string partial = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".part");
            try
            {
                using var response = await client.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead, cancel.Token); response.EnsureSuccessStatusCode();
                await using (var input = await response.Content.ReadAsStreamAsync(cancel.Token))
                await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                {
                    var buffer = new byte[81920]; long received = 0; int count;
                    while ((count = await input.ReadAsync(buffer, cancel.Token)) > 0) { await output.WriteAsync(buffer.AsMemory(0, count), cancel.Token); received += count; if (received > release.Size) throw new InvalidDataException("Unexpected download size."); progress.Value = 100d * received / release.Size; status.Text = $"Downloaded {received / 1048576d:0} / {release.Size / 1048576d:0} MB"; }
                    if (received != release.Size) throw new InvalidDataException("Incomplete download.");
                }
                await VerifyInstallerAsync(partial, release, cancel.Token);
                installer = Path.Combine(folder, $"BlakiDeck-{release.Version}-Setup.exe"); File.Move(partial, installer, true); status.Text = "Download verified. Ready to install."; install.IsEnabled = true; install.Opacity = 1;
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { status.Text = "Download failed. " + error.Message; download.IsEnabled = true; }
            finally { downloading = false; if (File.Exists(partial)) File.Delete(partial); }
        };
        window.Closed += (_, _) => { updateWindow = null; cancel.Cancel(); }; window.Content = panel; AppearanceEditorUi.Frame(window, 340, 420); window.Show();
    }
    internal static async Task VerifyInstallerAsync(string path, Release release, CancellationToken cancellation = default)
    {
        if (new FileInfo(path).Length != release.Size) throw new InvalidDataException("Incomplete download.");
        await using var input = File.OpenRead(path);
        string digest = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellation));
        if (!digest.Equals(release.Digest, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Installer verification failed.");
    }
}
