using System.IO;

namespace Ekrano_valdymas;

// Packaged installs change their versioned installation directory on update.
// Resolve only this package family's shipped icons; never migrate user files.
internal static class PackagedIconPath
{
    private sealed record Context(string Root,string Icons,string Family);
    private static readonly Lazy<Context?> current=new(()=>PackageIdentity.CurrentFamilyName is string family
        ?new(Windows.ApplicationModel.Package.Current.InstalledLocation.Path,DefaultIconCatalog.PackagedDirectory,family)
        :null);

    internal static string? Resolve(string? path)
    {
        if(string.IsNullOrWhiteSpace(path))return path;
        var context=current.Value;
        return context==null?path:ResolveForPackage(path,context.Root,context.Icons,context.Family);
    }

    internal static string ResolveForPackage(string path,string packageRoot,string iconDirectory,string family)
    {
        try
        {
            if(!Path.IsPathFullyQualified(path)||path.Split(new[]{'\\','/'}).Any(part=>part is "." or ".."))return path;
            string root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(packageRoot));
            string icons=Path.GetFullPath(iconDirectory);
            string candidate=Path.GetFullPath(path);
            if(string.Equals(Path.GetDirectoryName(candidate),icons,StringComparison.OrdinalIgnoreCase))return path;
            string? store=Path.GetDirectoryName(root);
            if(store==null||!Inside(candidate,store)||!Inside(icons,root))return path;
            string relative=Path.GetRelativePath(store,candidate);
            string[] parts=relative.Split(new[]{Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar});
            if(parts.Length<4||!BelongsToFamily(parts[0],family))return path;
            string previousRoot=Path.Combine(store,parts[0]);
            string expectedDirectory=Path.Combine(previousRoot,Path.GetRelativePath(root,icons));
            if(!string.Equals(Path.GetDirectoryName(candidate),expectedDirectory,StringComparison.OrdinalIgnoreCase))return path;
            // This resolver is deliberately limited to the shipped icon folder.
            if(!icons.EndsWith(Path.DirectorySeparatorChar+Path.Combine("Assets","DefaultIcons"),StringComparison.OrdinalIgnoreCase))return path;
            string replacement=Path.Combine(icons,Path.GetFileName(candidate));
            if(!File.Exists(replacement)||HasLink(candidate,store)||HasLink(replacement,store))return path;
            return replacement;
        }
        catch(ArgumentException){return path;}
        catch(NotSupportedException){return path;}
        catch(IOException){return path;}
        catch(UnauthorizedAccessException){return path;}
    }

    private static bool BelongsToFamily(string fullName,string family)
    {
        string[] parts=fullName.Split('_');
        return parts.Length==5 && Version.TryParse(parts[1],out var version) && version.Revision>=0 &&
            string.Equals(parts[0]+"_"+parts[4],family,StringComparison.OrdinalIgnoreCase);
    }

    private static bool Inside(string path,string root)=>path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    private static bool HasLink(string path,string stop)
    {
        for(string? item=path;item!=null&&(Inside(item,stop)||string.Equals(item,stop,StringComparison.OrdinalIgnoreCase));item=Path.GetDirectoryName(item))
        {
            try { if((File.GetAttributes(item)&FileAttributes.ReparsePoint)!=0)return true; }
            catch(FileNotFoundException){}
            catch(DirectoryNotFoundException){}
        }
        return false;
    }
}
