using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ekrano_valdymas;

internal enum ProfileResetScope { Layout, Full }

// A reset is applied by the replacement process after the old process has exited,
// before any settings, services or WebView controls are constructed.
internal static class ApplicationProfileReset
{
    private sealed record Request(string Token, ProfileResetScope Scope, int ProcessId, long StartTicks);
    private static string Folder => Path.GetFullPath(Path.Combine(DiagnosticState.LocalApplicationData,"Ekrano_valdymas"));
    private static string RequestPath => Path.Combine(Folder,"pending-profile-reset.json");

    internal static ProcessStartInfo CreateRestart(ProfileResetScope scope)
    {
        if(DiagnosticState.Root != null) throw new InvalidOperationException("Diagnostic windows cannot restart the real application.");
        using var process=Process.GetCurrentProcess();
        string token=Guid.NewGuid().ToString("N");
        AtomicJson.Write(RequestPath,new Request(token,scope,process.Id,process.StartTime.ToUniversalTime().Ticks));
        var start=new ProcessStartInfo(Environment.ProcessPath ?? throw new IOException("The application path is unavailable."))
        { UseShellExecute=false,WorkingDirectory=AppContext.BaseDirectory,WindowStyle=ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("--apply-profile-reset");start.ArgumentList.Add(token);
        return start;
    }

    internal static void WaitForPreviousProcess(string token)
    {
        var request=ReadRequest(token);
        try
        {
            using var previous=Process.GetProcessById(request.ProcessId);
            if(previous.StartTime.ToUniversalTime().Ticks!=request.StartTicks ||
                !string.Equals(previous.MainModule?.FileName,Environment.ProcessPath,StringComparison.OrdinalIgnoreCase))
                throw new IOException("The original application identity has changed. Nothing was reset.");
            if(!previous.WaitForExit(20000))throw new IOException("The application is still closing. Nothing was reset.");
        }
        catch(ArgumentException) { /* Original process has already exited. */ }
    }

    internal static void ApplyPending(string token)
    {
        var request=ReadRequest(token);
        Apply(request.Scope);
        if(File.Exists(RequestPath))File.Delete(RequestPath);
        if(File.Exists(RequestPath+".previous"))File.Delete(RequestPath+".previous");
    }

    private static Request ReadRequest(string token)
    {
        if(token.Length!=32 || !Guid.TryParseExact(token,"N",out _))throw new IOException("Invalid reset request.");
        var request=JsonSerializer.Deserialize<Request>(File.ReadAllText(RequestPath));
        if(request==null || request.Token!=token || !Enum.IsDefined(request.Scope))throw new IOException("The confirmed reset request is unavailable.");
        return request;
    }

    internal static void SeedTopBarDefaults(string folder)
    {
        AtomicJson.Write(Path.Combine(folder,"top-bar-tabs-v1.json"),new {
            Version=1,TabCount=4,ClockSpan=2,FactoryLayout=true,
            Entries=new object?[8],Order=Enumerable.Range(0,8).Select(i=>$"slot:{i}").Append("clock").ToArray(),Overflow=Array.Empty<object>() });
    }

    internal static void Apply(ProfileResetScope scope)
    {
        string folder=Folder;
        if(!Enum.IsDefined(scope))throw new ArgumentOutOfRangeException(nameof(scope));
        string profileRoot=Path.GetFullPath(DiagnosticState.LocalApplicationData);
        if(Directory.Exists(profileRoot)&&(File.GetAttributes(profileRoot)&FileAttributes.ReparsePoint)!=0)
            throw new IOException("The profile directory is a linked folder. Nothing was reset.");
        if(Directory.Exists(folder) && (File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)
            throw new IOException("The settings directory is a linked folder. Nothing was reset.");
        if(scope==ProfileResetScope.Full)
        {
            string? legacy=LegacyBrowserLocation(DiagnosticState.Root,DiagnosticState.Root==null&&ProductProfile.IsPackaged,Environment.ProcessPath);
            string pointer=Path.Combine(folder,"web-profile-location.txt");
            if(File.Exists(pointer))
            {
                string profile=Path.GetFullPath(File.ReadAllText(pointer).Trim());
                if(!Inside(profile,folder) && !string.Equals(profile,legacy,StringComparison.OrdinalIgnoreCase))
                    throw new IOException("The Web profile uses another location. Its ownership must be verified before a full reset.");
            }
            // All targets are resolved and checked before the first deletion.
            if(legacy!=null&&Directory.Exists(legacy))DeleteWithRetry(legacy);
            if(Directory.Exists(folder))DeleteWithRetry(folder);
            return;
        }

        var window=ReadWindowSettings(folder);
        JsonArray? previousTabs=null;
        foreach(string suffix in new[]{"",".previous"})
            try { previousTabs=JsonNode.Parse(File.ReadAllText(Path.Combine(folder,"dashboard-layout-tabs.json"+suffix))) as JsonArray;if(previousTabs!=null)break; }catch(IOException){}catch(JsonException){}
        Directory.CreateDirectory(folder);
        foreach(string file in Directory.EnumerateFiles(folder))
            if(IsLayoutFile(Path.GetFileName(file)))File.Delete(file);
        window["Modules"]=new JsonArray("Shortcuts","Shortcuts2","Shortcuts3");
        window["WideShortcutsSpan"]=0;window["MusicConsoleEnabled"]=false;window["MusicTopBarHidden"]=false;
        window["ShowClock"]=true;window["IsCompact"]=false;
        // A browser restored from the old layout must not cover the new layout.
        window["OpenWebShortcutUrl"]=null;window["BrowserPhysicalBounds"]=null;
        string settings=Path.Combine(folder,"window-settings.json");
        AtomicJson.Write(settings,window);
        File.Delete(settings+".previous");
        SeedTopBarDefaults(folder);
        var tabs=new JsonArray();
        for(int index=0;index<11;index++)
        {
            var previous=previousTabs!=null && index<previousTabs.Count ? previousTabs[index] as JsonObject : null;
            tabs.Add(new JsonObject {
                ["DefaultsVersion"]=1,["Modules"]=new JsonArray("Shortcuts","Shortcuts2","Shortcuts3"),["WideShortcutsSpan"]=0,
                ["MusicConsoleEnabled"]=false,["MusicTopBarHidden"]=false,
                ["MusicService"]=previous?["MusicService"]?.DeepClone() ?? JsonValue.Create("Spotify"),
                ["Player"]=previous?["Player"]?.DeepClone() });
        }
        AtomicJson.Write(Path.Combine(folder,"dashboard-layout-tabs.json"),tabs);
    }

    internal static string? LegacyBrowserLocation(string? diagnosticRoot,bool packaged,string? processPath)=>diagnosticRoot!=null
        ?Path.GetFullPath(Path.Combine(diagnosticRoot,"WebView2"))
        :packaged?null:Path.GetFullPath((processPath??throw new IOException("The application path is unavailable."))+".WebView2");

    private static JsonObject ReadWindowSettings(string folder)
    {
        foreach(string path in new[]{Path.Combine(folder,"window-settings.json"),Path.Combine(folder,"window-settings.json.previous")})
            try { if(File.Exists(path)&&JsonNode.Parse(File.ReadAllText(path)) is JsonObject settings)return settings; } catch(JsonException){}
        return new JsonObject();
    }

    internal static bool IsLayoutFile(string name) => System.Text.RegularExpressions.Regex.IsMatch(name,@"^tab-\d+-shortcuts",System.Text.RegularExpressions.RegexOptions.IgnoreCase) || new[]{
        "dashboard-layout-tabs.json","dashboard-tab-appearance.json","top-bar-","quick-launch", "module-shortcuts",
        "dashboard-tab-", "tab-1-shortcuts", "tab-2-shortcuts", "tab-3-shortcuts", "tab-shortcuts-migration",
        "sensor-cells.json","system-monitor.json","system-monitor.active.json","system-monitor-presentation.json",
        "airthings-display.json","airthings-gauge-display.json","airthings-presentation.json","airthings-selection.json",
        "macro-library-view.json","macro-library-clock.json","macro-library-airthings.json" }
        .Any(prefix=>name.StartsWith(prefix,StringComparison.OrdinalIgnoreCase));

    private static bool Inside(string path,string parent)=>path.StartsWith(parent+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    private static void DeleteWithRetry(string root)
    {
        for(int attempt=0;;attempt++)
        {
            try { if(Directory.Exists(root))DeleteTree(root);return; }
            catch(IOException)when(attempt<20){Thread.Sleep(250);}
        }
    }
    private static void DeleteTree(string root)
    {
        // Never follow junctions or symbolic links out of a product-owned root.
        if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0){Directory.Delete(root);return;}
        foreach(string child in Directory.EnumerateDirectories(root))DeleteTree(child);
        foreach(string file in Directory.EnumerateFiles(root))
        {
            if((File.GetAttributes(file)&FileAttributes.ReparsePoint)==0)File.SetAttributes(file,FileAttributes.Normal);
            File.Delete(file);
        }
        Directory.Delete(root);
    }
}
