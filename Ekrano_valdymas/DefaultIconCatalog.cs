using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Ekrano_valdymas;

internal static class DefaultIconCatalog
{
    private static string? selectedDirectory;
    internal static string PackagedDirectory => Path.Combine(Path.GetDirectoryName(typeof(DefaultIconCatalog).Assembly.Location)??AppContext.BaseDirectory,"Assets","DefaultIcons");
    internal static string InitialDirectory => Directory.Exists(selectedDirectory)?selectedDirectory!:Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

    internal static OpenFileDialog CreatePicker() => new()
    {
        Filter="Images|*.png;*.jpg;*.jpeg;*.ico;*.bmp",
        InitialDirectory=InitialDirectory,
        // A fresh dialog identity prevents persisted shell history from replacing
        // the application's process-only directory choice after a restart.
        ClientGuid=Guid.NewGuid(),
        AddToRecent=false
    };

    internal static string? ChooseImage(Window? owner)
    {
        var picker=CreatePicker();
        if(picker.ShowDialog(owner)!=true)return null;
        RememberSelection(picker.FileName);
        return picker.FileName;
    }

    internal static void RememberSelection(string fileName)
    {
        if(string.IsNullOrWhiteSpace(fileName))return;
        try
        {
            string? directory=Path.GetDirectoryName(Path.GetFullPath(fileName));
            if(Directory.Exists(directory))selectedDirectory=directory;
        }
        catch(ArgumentException){}
        catch(NotSupportedException){}
        catch(PathTooLongException){}
    }

    internal static string? GetPath(string fileName)
    {
        if(string.IsNullOrWhiteSpace(fileName)||fileName!=Path.GetFileName(fileName))return null;
        string path=Path.Combine(PackagedDirectory,fileName);
        return File.Exists(path)?path:null;
    }

    // Reuse the existing bounded, display-sized decoder rather than loading all
    // supplied 1254px images when a tab or picker is created.
    internal static ImageSource? GetImage(string fileName) => IconBitmap.Load(GetPath(fileName));
    internal static string? MusicSourceFileName(MusicService source) => source switch
    {
        MusicService.Spotify=>"Spotify.png",
        MusicService.Tidal=>"Tidal.png",
        MusicService.Deezer=>"Deezer.png",
        MusicService.AppleMusic=>"Apple_music.png",
        MusicService.AmazonMusic=>"Amazon_music.png",
        MusicService.MusicBee=>"MusicBee.png",
        MusicService.Local=>"Local player.png",
        _=>null
    };
}
