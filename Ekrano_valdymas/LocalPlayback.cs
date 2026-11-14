using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Ekrano_valdymas;

internal sealed class LocalPlayback
{
    internal sealed record Track(string Path,string Title,string Artist,double Seconds)
    {
        [System.Text.Json.Serialization.JsonIgnore] public string ArtistTitle=>string.IsNullOrWhiteSpace(Artist)?Title:$"{Artist} – {Title}";
        [System.Text.Json.Serialization.JsonIgnore] public bool IsCurrentTrack=>!string.IsNullOrEmpty(Path)&&Instance.Current?.Path==Path;
        [System.Text.Json.Serialization.JsonIgnore] public string PlaybackMark=>!string.IsNullOrEmpty(Path)&&Instance.Current?.Path==Path?(Instance.Playing?"▶":"Ⅱ"):"";
        [System.Text.Json.Serialization.JsonIgnore] public string DurationText=>Seconds>0?TimeSpan.FromSeconds(Seconds).ToString(Seconds>=3600?@"h\:mm\:ss":@"m\:ss"):"—";
    }
    private sealed record Saved(List<Track> Tracks,bool Repeat,bool Shuffle,double? Volume=null);
    internal static LocalPlayback Instance {get;}=new();
    // Do not initialize Windows media merely because the dashboard has a player.
    // Windows N without its optional media features must still open the app.
    private MediaPlayer? audio;
    internal List<Track> Tracks {get;}=new();
    internal Track? Current {get;private set;}
    internal bool Playing {get;private set;}
    internal bool Repeat {get;private set;}
    internal bool Shuffle {get;private set;}
    internal ImageSource? Artwork {get;private set;}
    internal string Format {get;private set;}="";
    internal string Bitrate {get;private set;}="";
    internal string SampleRate {get;private set;}="";
    internal string Error {get;private set;}="";
    internal double Position=>audio?.Position.TotalSeconds??0;
    internal double Duration=>audio?.NaturalDuration.HasTimeSpan==true?audio.NaturalDuration.TimeSpan.TotalSeconds:Current?.Seconds??0;
    private double volume=InstallationDefaults.IsNewInstall?1:.5;
    private bool volumeDirty;
    private readonly DispatcherTimer volumeSaveTimer=new(){Interval=TimeSpan.FromMilliseconds(250)};
    internal double Volume {get=>volume*100;set{double next=Math.Clamp(double.IsFinite(value)?value/100:volume,0,1);if(next==volume)return;volume=next;if(audio!=null)audio.Volume=volume;volumeDirty=true;volumeSaveTimer.Stop();volumeSaveTimer.Start();}}
    internal event Action? Changed;
    private static string StatePath=>Path.Combine(DiagnosticState.LocalApplicationData,"Ekrano_valdymas","local-playlist.json");
    private LocalPlayback()
    {
        Load();
        volumeSaveTimer.Tick+=(_,_)=>Save();
        Application.Current.Exit+=(_,_)=>{volumeSaveTimer.Stop();if(volumeDirty)Save();audio?.Close();};
    }
    private MediaPlayer EnsureAudio()
    {
        if(audio!=null)return audio;
        if(!MediaSupport.WindowsMediaComponentsPresent)throw new InvalidOperationException(MediaSupport.MissingWindowsMediaMessage);
        var created=new MediaPlayer();
        created.MediaOpened+=(_,_)=>{if(Current is {} t && created.NaturalDuration.HasTimeSpan){int i=Tracks.IndexOf(t);Current=t with{Seconds=Duration};if(i>=0)Tracks[i]=Current;Save();}Changed?.Invoke();};
        created.MediaEnded+=async(_,_)=>{try{await NextAsync(1,true);}catch(Exception e){Fail(e);}};
        created.MediaFailed+=(_,e)=>{
            InteractionDiagnostics.RecordFailure("Local media playback",e.ErrorException);
            Fail(new InvalidOperationException(MediaSupport.PlaybackFailure(Current?.Path,MediaSupport.WindowsMediaComponentsPresent),e.ErrorException));
        };
        audio=created;
        return created;
    }
    internal void Fail(Exception e){Playing=false;try{audio?.Pause();}catch{}Error=e.Message;Changed?.Invoke();}
    internal void Pause(){audio?.Pause();Playing=false;Changed?.Invoke();}
    internal void Seek(double seconds){if(audio!=null)audio.Position=TimeSpan.FromSeconds(Math.Clamp(seconds,0,Duration));Changed?.Invoke();}
    internal void SetMode(bool shuffle){if(shuffle)Shuffle=!Shuffle;else Repeat=!Repeat;Save();Changed?.Invoke();}
    internal Task<bool> PlayAsync(int index=-1)=>PlaybackCoordinator.RunAsync(async()=>{
        Track? target=index>=0&&index<Tracks.Count?Tracks[index]:Current??Tracks.FirstOrDefault();
        if(target==null)throw new InvalidOperationException("Add audio files to Playlist.");
        if(!File.Exists(target.Path))throw new FileNotFoundException("The audio file is unavailable.",target.Path);
        var output=EnsureAudio();
        await PlaybackCoordinator.PauseOthersAsync(null,true);
        if(!Tracks.Contains(target))return false;
        try
        {
            if(Current!=target||Error.Length>0){output.Close();Current=target;Artwork=null;Format=Bitrate=SampleRate="";output.Open(new Uri(target.Path));_ = ReadDetailsAsync(target);}
            Error="";output.Volume=volume;output.Play();Playing=true;Changed?.Invoke();return true;
        }
        catch(Exception e)
        {
            InteractionDiagnostics.RecordFailure("Local media playback",e);
            var explained=new InvalidOperationException(MediaSupport.PlaybackFailure(target.Path,MediaSupport.WindowsMediaComponentsPresent),e);
            Fail(explained);throw explained;
        }
    });
    internal async Task<bool> CommandAsync(string command)
    {
        if(command=="pause"||(command=="play"&&Playing)){await PlaybackCoordinator.RunAsync(()=>{Pause();return Task.FromResult(true);});return true;}
        if(command is "previous" or "next")return await NextAsync(command=="previous"?-1:1);
        return await PlayAsync();
    }
    internal Task<bool> NextAsync(int direction,bool ended=false)
    {
        if(Tracks.Count==0)return Task.FromResult(false);
        int current=Current==null?-1:Tracks.IndexOf(Current),next=current+direction;
        if(Shuffle&&Tracks.Count>1){next=Random.Shared.Next(Tracks.Count-1);if(next>=current)next++;}
        if(next>=Tracks.Count){if(ended&&!Repeat){Pause();Seek(0);return Task.FromResult(true);}next=0;}
        if(next<0)next=Tracks.Count-1;
        return PlayAsync(next);
    }
    internal async Task<int> AddAsync(IEnumerable<string> paths)
    {
        int first=Tracks.Count;
        var files=await Task.Run(()=>ExpandAudioPaths(paths.ToArray()));
        foreach(string path in files)
        {
            if(!File.Exists(path)||Tracks.Any(t=>string.Equals(t.Path,path,StringComparison.OrdinalIgnoreCase)))continue;
            string title=Path.GetFileNameWithoutExtension(path),artist="";double duration=0;
            try{var file=await Windows.Storage.StorageFile.GetFileFromPathAsync(path);var info=await file.Properties.GetMusicPropertiesAsync();if(!string.IsNullOrWhiteSpace(info.Title))title=info.Title;artist=info.Artist;duration=info.Duration.TotalSeconds;}catch(Exception e){InteractionDiagnostics.RecordFailure("Local metadata",e);}
            Tracks.Add(new(path,title,artist,duration));
        }
        Save();Changed?.Invoke();return Tracks.Count>first?first:-1;
    }
    internal static string[] ExpandAudioPaths(IEnumerable<string> paths)
    {
        var files=new List<string>();
        var extensions=new HashSet<string>(StringComparer.OrdinalIgnoreCase){".mp3",".flac",".wav",".m4a",".aac",".wma",".ogg"};
        foreach(var path in paths)
        {
            try
            {
                if(Directory.Exists(path))files.AddRange(Directory.GetFiles(path,"*",SearchOption.TopDirectoryOnly).Where(f=>extensions.Contains(Path.GetExtension(f))).OrderBy(f=>f,StringComparer.OrdinalIgnoreCase));
                else if(File.Exists(path)&&extensions.Contains(Path.GetExtension(path)))files.Add(path);
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){InteractionDiagnostics.RecordFailure("Playlist folder",ex);}
        }
        return files.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    internal void Remove(IEnumerable<Track> selected)
    {
        var set=selected.ToHashSet();if(Current!=null&&set.Contains(Current)){Pause();audio?.Close();Current=null;Artwork=null;Format=Bitrate=SampleRate="";}
        Tracks.RemoveAll(set.Contains);Save();Changed?.Invoke();
    }
    internal void Move(IReadOnlyList<Track> selected,int destination)
    {
        var ordered=Tracks.Where(selected.Contains).ToList();destination=Math.Clamp(destination,0,Tracks.Count);
        int adjusted=destination-Tracks.Take(destination).Count(selected.Contains);
        Tracks.RemoveAll(selected.Contains);Tracks.InsertRange(adjusted,ordered);Save();Changed?.Invoke();
    }
    internal void Load(){try{if(File.Exists(StatePath)&&JsonSerializer.Deserialize<Saved>(File.ReadAllText(StatePath)) is {} s){Tracks.Clear();Tracks.AddRange(s.Tracks.Where(t=>!string.IsNullOrWhiteSpace(t.Path)));Repeat=s.Repeat;Shuffle=s.Shuffle;if(s.Volume is double saved&&double.IsFinite(saved))volume=Math.Clamp(saved/100,0,1);}}catch(Exception e){Error=e.Message;}}
    private void Save(){volumeSaveTimer.Stop();try{Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);File.WriteAllText(StatePath+".tmp",JsonSerializer.Serialize(new Saved(Tracks,Repeat,Shuffle,Volume)));File.Move(StatePath+".tmp",StatePath,true);volumeDirty=false;}catch(Exception e){Error="Could not save Playlist: "+e.Message;}}
    internal void SetDiagnosticArtwork(ImageSource image){if(DiagnosticState.Root==null)throw new InvalidOperationException();Artwork=image;Changed?.Invoke();}
    private async Task ReadDetailsAsync(Track track)
    {
        ImageSource? cover=null;string format="",bitrate="",sample="";
        int maximumPixels=AlbumArtwork.MaximumPixels;
        void Publish(){if(Current?.Path!=track.Path)return;Artwork=cover;Format=format;Bitrate=bitrate;SampleRate=sample;Changed?.Invoke();}
        try{
            var embedded=await Task.Run(()=>AudioFileDetails.Read(track.Path));format=embedded.Format;
            if(embedded.Bitrate is >0)bitrate=$"{embedded.Bitrate/1000:0} kb/s";
            if(embedded.SampleRate is >0)sample=$"{embedded.SampleRate/1000:0.###} kHz";
            if(embedded.Artwork is {} bytes)cover=await Task.Run(()=>{using var art=new MemoryStream(bytes);return AlbumArtwork.Decode(art,maximumPixels);});
            Publish();
        }catch(Exception e){InteractionDiagnostics.RecordFailure("Read embedded audio properties",e);}
        try{
            var file=await Windows.Storage.StorageFile.GetFileFromPathAsync(track.Path);
            var info=await file.Properties.GetMusicPropertiesAsync();
            if(bitrate.Length==0&&info.Bitrate>0)bitrate=$"{info.Bitrate/1000d:0} kb/s";
            var properties=await file.Properties.RetrievePropertiesAsync(new[]{"System.Audio.SampleRate"});
            if(sample.Length==0&&properties.TryGetValue("System.Audio.SampleRate",out var rate)&&rate!=null&&Convert.ToDouble(rate)>0)sample=$"{Convert.ToDouble(rate)/1000:0.###} kHz";
            Publish();
            if(cover==null){using var thumbnail=await file.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.MusicView,512);if(thumbnail!=null&&thumbnail.Type==Windows.Storage.FileProperties.ThumbnailType.Image){using var stream=thumbnail.AsStreamForRead();cover=await Task.Run(()=>AlbumArtwork.Decode(stream,maximumPixels));Publish();}}
        }catch(Exception e){InteractionDiagnostics.RecordFailure("Optional Windows audio properties",e);}
    }
}
