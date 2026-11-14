using System.IO;

namespace Ekrano_valdymas;

// Decide once, before XAML constructors or legacy migrations create settings.
// A missing individual preference file never makes an existing profile new.
internal static class InstallationDefaults
{
    private static readonly Dictionary<string,bool> profiles=new(StringComparer.OrdinalIgnoreCase);
    internal static bool IsNewInstall=>Prepare();
    internal static bool Prepare()
    {
        string folder=Path.GetFullPath(Path.Combine(DiagnosticState.LocalApplicationData,"Ekrano_valdymas"));
        if(profiles.TryGetValue(folder,out bool known))return known;
        bool fresh;
        try
        {
            fresh=!Directory.Exists(folder)||!Directory.EnumerateFileSystemEntries(folder)
                .Any(path=>!Directory.Exists(path)||!string.Equals(Path.GetFileName(path),"diagnostics",StringComparison.OrdinalIgnoreCase));
        }
        catch { fresh=false; }
        profiles[folder]=fresh;
        if(fresh)
        {
            FactoryDefaults.Seed(folder);
            SensorFactoryDefaults.SeedNewInstall();
            AtomicJson.Write(Path.Combine(folder,"installation-defaults.json"),new{Version=1,PrimaryFinish=MusicFinish.DefaultName});
        }
        return fresh;
    }
}
