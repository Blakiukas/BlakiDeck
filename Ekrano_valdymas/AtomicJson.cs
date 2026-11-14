using System.IO;
using System.Text.Json;

namespace Ekrano_valdymas;
internal static class AtomicJson
{
    internal static void Write<T>(string path,T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp=path+".tmp";
        File.WriteAllText(temp,JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));
        if(File.Exists(path))File.Replace(temp,path,path+".previous",true);else File.Move(temp,path);
    }
}
