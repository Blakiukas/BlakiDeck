using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ekrano_valdymas;

internal sealed record ObsInstallation(string Executable, bool Portable)
{
    internal string ConfigDirectory => Portable
        ? Path.Combine(Directory.GetParent(Path.GetDirectoryName(Executable)!)!.Parent!.FullName, "config", "obs-studio")
        : ObsInstallationLocator.RoamingConfigDirectory;
}

internal sealed class ObsSetupException(string english, string lithuanian) : Exception(english)
{
    internal string Lithuanian { get; } = lithuanian;
}

// Read only known installation locations. Never search drives or copy OBS credentials.
internal static class ObsInstallationLocator
{
    internal static string RoamingConfigDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio");
    private static string SelectionPath => Path.Combine(DiagnosticState.LocalApplicationData, "Ekrano_valdymas", "obs-location.json");
    internal static ObsInstallation? Current { get; private set; }
    private static readonly object Gate = new();
    private static int lastProcessId;
    private static long lastProcessStartTicks;
    private static string? lastProcessPath;
    private static bool lastPortableArgument;

    internal static ObsInstallation? Resolve()
    {
        lock (Gate)
        {
            // A failed discovery must not leave another installation's profile
            // available to readers such as the hotkey labels.
            Current = null;
            var saved = ReadSelection();
            var processes = Process.GetProcessesByName("obs64");
            try
            {
                var candidates = new List<(Process Process, string Path)>();
                foreach (var process in processes)
                {
                    try { if (process.MainModule?.FileName is string path) candidates.Add((process, path)); }
                    catch (Exception) { }
                }
                if (processes.Length > 0)
                {
                    if (candidates.Count != processes.Length)
                        throw new ObsSetupException("Cannot read the running OBS location. Run OBS and BlakiDeck under the same Windows account.", "Nepavyko nustatyti veikiančio OBS vietos. Paleiskite OBS ir BlakiDeck ta pačia Windows paskyra.");
                    if (candidates.Count > 1)
                        candidates = candidates.Where(candidate => saved != null && string.Equals(candidate.Path, saved.Executable, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (candidates.Count != 1)
                        throw new ObsSetupException("Several OBS instances are running. Close the extra instances before connecting.", "Veikia kelios OBS kopijos. Prieš jungdamiesi uždarykite papildomas kopijas.");
                    var selected = candidates[0];
                    long started;
                    try { started = selected.Process.StartTime.ToUniversalTime().Ticks; }
                    catch (Exception)
                    {
                        throw new ObsSetupException("Cannot identify the running OBS process. Restart OBS under the same Windows account as BlakiDeck, then reconnect.", "Nepavyko atpažinti veikiančio OBS proceso. Paleiskite OBS ta pačia Windows paskyra kaip BlakiDeck ir junkitės iš naujo.");
                    }
                    if (lastProcessId != selected.Process.Id || lastProcessStartTicks != started || lastProcessPath != selected.Path)
                    {
                        bool markedPortable = FromExecutable(selected.Path)?.Portable == true;
                        bool? portableArgument = markedPortable ? true : ReadPortableArgument(selected.Process.Id);
                        if (!portableArgument.HasValue)
                            throw new ObsSetupException("Cannot identify the running OBS profile. Restart OBS under the same Windows account as BlakiDeck, then reconnect.", "Nepavyko nustatyti veikiančio OBS profilio. Paleiskite OBS ta pačia Windows paskyra kaip BlakiDeck ir junkitės iš naujo.");
                        lastPortableArgument = portableArgument.Value;
                        lastProcessId = selected.Process.Id;
                        lastProcessStartTicks = started;
                        lastProcessPath = selected.Path;
                    }
                    return Current = FromExecutable(selected.Path, lastPortableArgument);
                }
            }
            finally { foreach (var process in processes) process.Dispose(); }

            lastProcessId = 0;
            lastProcessStartTicks = 0;
            if (saved != null) return Current = saved;
            foreach (string candidate in RegisteredCandidates().Concat(StandardCandidates()))
                if (FromExecutable(candidate) is { } installation) return Current = installation;
            foreach (var candidate in StartMenuCandidates())
                if (FromExecutable(candidate.Path, candidate.Portable) is { } installation) return Current = installation;
            return Current = null;
        }
    }

    internal static ObsInstallation? FromExecutable(string? path, bool portableArgument = false)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            if (!Path.IsPathFullyQualified(path)) return null;
            path = Path.GetFullPath(path);
            if (!Path.GetFileName(path).Equals("obs64.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return null;
            // OBS's supported Windows layout is <root>/bin/64bit/obs64.exe.
            var binaryDirectory = new DirectoryInfo(Path.GetDirectoryName(path)!);
            bool standardTree = binaryDirectory.Name.Equals("64bit", StringComparison.OrdinalIgnoreCase) && binaryDirectory.Parent?.Name.Equals("bin", StringComparison.OrdinalIgnoreCase) == true;
            string? root = standardTree ? binaryDirectory.Parent!.Parent?.FullName : null;
            bool marker = root != null && new[] { "portable_mode", "portable_mode.txt", "obs_portable_mode", "obs_portable_mode.txt" }.Any(name => File.Exists(Path.Combine(root, name)));
            if (portableArgument && root == null)
                throw new ObsSetupException("The portable OBS folder must keep its bin/64bit layout.", "Nešiojamoje OBS kopijoje turi likti bin/64bit aplankų išdėstymas.");
            return new(path, portableArgument || marker);
        }
        catch (ArgumentException) { return null; }
        catch (IOException) { return null; }
        catch (NotSupportedException) { return null; }
    }

    internal static void RememberSelection(ObsInstallation installation)
    {
        AtomicJson.Write(SelectionPath, installation);
        Current = installation;
    }

    private static ObsInstallation? ReadSelection()
    {
        try
        {
            if (File.Exists(SelectionPath) && JsonSerializer.Deserialize<ObsInstallation>(File.ReadAllText(SelectionPath)) is { } saved)
                return FromExecutable(saved.Executable, saved.Portable);
        }
        catch (Exception) { }
        return null;
    }

    private static bool? ReadPortableArgument(int processId)
    {
        try
        {
            using var query = new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE ProcessId=" + processId);
            query.Options.Timeout = TimeSpan.FromSeconds(2);
            using var rows = query.Get();
            foreach (ManagementObject row in rows)
            {
                using (row)
                    if (row["CommandLine"] is string command) return HasPortableArgument(command);
            }
        }
        catch (Exception) { }
        return null;
    }

    internal static bool HasPortableArgument(string command) => Regex.IsMatch(command, "(?:^|\\s)\"?(?:--portable|-p)\"?(?=\\s|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static IEnumerable<string> StandardCandidates()
    {
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
            yield return Path.Combine(Environment.GetFolderPath(folder), "obs-studio", "bin", "64bit", "obs64.exe");
    }

    private static IEnumerable<string> RegisteredCandidates()
    {
        var paths = new List<string>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var app = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\obs64.exe");
                if (app?.GetValue(null) is string executable) paths.Add(executable);
                using var uninstall = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\OBS Studio");
                if (uninstall?.GetValue("InstallLocation") is string directory)
                    paths.Add(Path.Combine(directory, "bin", "64bit", "obs64.exe"));
                if (uninstall?.GetValue("DisplayIcon") is string icon)
                    paths.Add(Regex.Replace(icon.Trim(), @",\s*-?\d+$", "").Trim('"'));
            }
            catch (Exception) { }
        }
        return paths;
    }

    private static IEnumerable<(string Path, bool Portable)> StartMenuCandidates()
    {
        var result = new List<(string, bool)>();
        object? shell = null;
        try
        {
            Type? type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return result;
            shell = Activator.CreateInstance(type);
            if (shell == null) return result;
            foreach (var folder in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms })
            {
                string root = Environment.GetFolderPath(folder);
                if (!Directory.Exists(root)) continue;
                var links = Directory.EnumerateFiles(root, "*OBS*.lnk", SearchOption.TopDirectoryOnly)
                    .Concat(Directory.EnumerateDirectories(root, "*OBS*", SearchOption.TopDirectoryOnly)
                        .SelectMany(directory => Directory.EnumerateFiles(directory, "*.lnk", SearchOption.TopDirectoryOnly))).Take(64);
                foreach (var link in links)
                {
                    object? shortcut = null;
                    try
                    {
                        shortcut = ((dynamic)shell).CreateShortcut(link);
                        result.Add((((dynamic)shortcut).TargetPath as string ?? "", HasPortableArgument(((dynamic)shortcut).Arguments as string ?? "")));
                    }
                    catch (Exception) { }
                    finally { if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut); }
                }
            }
        }
        catch (Exception) { }
        finally { if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell); }
        return result;
    }
}
