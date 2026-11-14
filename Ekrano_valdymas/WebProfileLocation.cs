using System.IO;

namespace Ekrano_valdymas;

// Keep the existing profile intact; pin its location rather than copying live databases.
internal static class WebProfileLocation
{
    internal static string Resolve()
    {
        if (DiagnosticState.Root != null) return Path.Combine(DiagnosticState.Root, "WebView2");
        string settings = Path.Combine(DiagnosticState.LocalApplicationData, "Ekrano_valdymas");
        return ProductProfile.IsPackaged
            ?Resolve(settings,null,true)
            :Resolve(settings,Environment.ProcessPath+".WebView2");
    }

    internal static string Resolve(string settings, string? legacy,bool confined=false)
    {
        if(confined)ValidateUnlinked(Path.Combine(settings,"WebView2"),settings);
        Directory.CreateDirectory(settings);
        string pointer = Path.Combine(settings, "web-profile-location.txt");
        if (File.Exists(pointer))
        {
            string saved = File.ReadAllText(pointer).Trim();
            if (!Path.IsPathFullyQualified(saved) || !Directory.Exists(saved))
                throw new IOException("The saved Web profile is unavailable. Restore its folder before opening Web; a replacement profile will not be created.");
            if(confined && !string.Equals(Path.GetFullPath(saved),Path.GetFullPath(Path.Combine(settings,"WebView2")),StringComparison.OrdinalIgnoreCase))
                throw new IOException("This packaged copy cannot use a Web profile outside its own settings. Its saved browser data was not changed.");
            if(confined)ValidateUnlinked(saved,settings);
            return saved;
        }
        string profile = !confined&&legacy!=null&&Directory.Exists(legacy) ? Path.GetFullPath(legacy) : Path.Combine(settings, "WebView2");
        if(confined)ValidateUnlinked(profile,settings);
        Directory.CreateDirectory(profile);
        using (var stream = new FileStream(pointer, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        using (var writer = new StreamWriter(stream)) writer.Write(profile);
        return profile;
    }

    private static void ValidateUnlinked(string profile,string settings)
    {
        foreach(string path in new[]{Path.GetDirectoryName(Path.GetFullPath(settings))!,settings,profile,Path.Combine(settings,"web-profile-location.txt")})
            if((Directory.Exists(path)||File.Exists(path))&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)
                throw new IOException("The packaged Web profile is a linked folder. Browser data was not opened or changed.");
    }
}
