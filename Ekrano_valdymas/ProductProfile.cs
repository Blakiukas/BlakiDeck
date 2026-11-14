using System.IO;
namespace Ekrano_valdymas;

// EXE and packaged installations have separate profiles. A package family's
// storage and mutex remain stable across version updates; no implicit migration.
internal static class ProductProfile
{
    private sealed record Location(bool Packaged,string Folder,string Mutex);
    private static readonly Lazy<Location> current=new(()=>
    {
        string? family=PackageIdentity.CurrentFamilyName;
        // LocalCache persists across package updates, without backing up browser
        // sessions and device-bound credentials to the Windows cloud backup.
        string? packageCache=family==null?null:Windows.Storage.ApplicationData.Current.LocalCacheFolder.Path;
        return new(family!=null,ResolveFolder(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),packageCache),ResolveMutex(family));
    });
    internal static bool IsPackaged=>current.Value.Packaged;
    internal static string InstanceMutex=>current.Value.Mutex;
    internal static string LocalApplicationData=>current.Value.Folder;
    internal static string ResolveFolder(string desktopLocalData,string? packageCache)=>packageCache==null
        ?Path.Combine(desktopLocalData,"BlakiDeck","Release")
        :Path.Combine(packageCache,"BlakiDeck");
    internal static string ResolveMutex(string? packageFamily)=>packageFamily==null
        ?@"Local\BlakiDeck.Release.SingleInstance"
        :@"Local\BlakiDeck.Package."+packageFamily+".SingleInstance";
}
