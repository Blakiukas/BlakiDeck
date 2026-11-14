using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ekrano_valdymas;

internal sealed class MacroStep
{
    public string Kind {get;set;}="Launch";
    public string Value {get;set;}="";
    public string DeviceId {get;set;}="";
    public string DeviceName {get;set;}="";
    public string Operation {get;set;}="Toggle";
    public int Milliseconds {get;set;}=1000;
    public double Brightness {get;set;}=50;
    public string SleepTarget {get;set;}="Computer";
    public int CountdownSeconds {get;set;}=10;
    public string DisplayTitle => Kind switch
    {
        "MicMute" => "Microphone: " + (Operation=="Toggle"?"mute / unmute":Operation=="Off"?"mute":"unmute"),
        "Camera" => "Camera: " + (Operation=="Toggle"?"enable / disable":Operation=="Off"?"disable":"enable"),
        "Hue" => "Philips Hue: " + (Operation=="Brightness"?$"brightness {Brightness:0}%":Operation=="Toggle"?"on / off":Operation.ToLowerInvariant()),
        "Wait" => $"Wait {Milliseconds} ms",
        "Sleep" => SleepTarget=="Monitors"?"Sleep monitors":"Sleep computer",
        "Shutdown" => "Shut down computer", "Restart" => "Restart computer",
        "Launch" => "Open program or file", "Keys" => "Send key combination",
        "OBS" => Value=="SaveReplay"?"OBS: save replay":Value=="ReplayBuffer"?"OBS: toggle replay buffer":"OBS: toggle recording",
        _ => Kind
    };
    public string DisplayDetail => Kind is "MicMute" or "Camera" or "Hue" ? (string.IsNullOrWhiteSpace(DeviceName)?"Default device":DeviceName)
        : Kind is "Sleep" or "Shutdown" or "Restart" ? $"Countdown: {CountdownSeconds} s" : Kind is "Launch" or "Keys" ? Value : "";
    public override string ToString()=>DisplayTitle+(DisplayDetail.Length>0?" — "+DisplayDetail:"");
}
internal sealed class MacroDefinition
{
    public Guid Id {get;set;}=Guid.NewGuid();
    public string Name {get;set;}="New macro";
    public string Hotkey {get;set;}="";
    public string Icon {get;set;}="⚡";
    public string? MicMutedIcon {get;set;}
    public string? MicOnIcon {get;set;}
    public string? CameraOffIcon {get;set;}
    public string? CameraReadyIcon {get;set;}
    public string? CameraActiveIcon {get;set;}
    public string? Presentation {get;set;}
    public LabelTypography? Typography {get;set;}
    public string? IconMode {get;set;}
    public string IconMeaning {get;set;}="Current";
    public string? StateSourceKey {get;set;}
    public Dictionary<string,string> StateIcons {get;set;}=new();
    public List<MacroStep> AlternateSteps {get;set;}=new();
    public bool NextActionB {get;set;}
    public string? LastAction {get;set;}
    public List<MacroStep> Steps {get;set;}=new();
    public MacroDefinition Clone()=>JsonSerializer.Deserialize<MacroDefinition>(JsonSerializer.Serialize(this))!;
}
internal sealed partial class MacroLibrary
{
    private static MacroLibrary? current;
    internal static MacroLibrary Current
    {
        get
        {
            if(current==null){current=new(Path.Combine(DiagnosticState.LocalApplicationData,"Ekrano_valdymas","macros.json"));current.StartMicrophoneMonitor();}
            return current;
        }
    }
    private bool? microphoneMuted;
    private CameraActivity? cameraActivity;
    private readonly Dictionary<string,string> cameraStates=new();
    private readonly Dictionary<string,bool?> microphoneStates=new();
    private System.Windows.Threading.DispatcherTimer? microphoneTimer;
    private void StartMicrophoneMonitor()
    {
        if(DiagnosticState.Root!=null)return;
        if(!Items.Any(m=>m.IconMode=="State"&&MacroStates.Source(m)?.Kind=="OBS")){obsIconsClient?.Dispose();obsIconsClient=null;obsIconStates.Clear();}
        if(microphoneTimer!=null){microphoneTimer.IsEnabled=HasMicrophoneIcons;if(!Items.Any(m=>m.Steps.Any(s=>s.Kind=="Camera"))){cameraActivity?.Dispose();cameraActivity=null;}return;}
        var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
        microphoneTimer=timer;
        timer.Tick+=(_,_)=>
        {
            if(Application.Current.MainWindow is {IsVisible:true,WindowState:not WindowState.Minimized}&&
                HasMicrophoneIcons)RefreshDeviceStates();
        };
        timer.IsEnabled=HasMicrophoneIcons;Application.Current.Exit+=(_,_)=>{timer.Stop();cameraActivity?.Dispose();obsIconsDisposed=true;obsIconsClient?.Dispose();};
        if(HasMicrophoneIcons)RefreshDeviceStates();
    }
    private bool HasMicrophoneIcons=>Items.Any(m=>m.Steps.Concat(m.AlternateSteps).Any(s=>s.Kind is "MicMute" or "Camera")||m.IconMode=="State"&&MacroStates.Source(m)?.Kind=="OBS");
    internal void RefreshDeviceStates()
    {
        _=RefreshObsIconsAsync();
        RefreshMicrophoneState();
        bool changed=false;
        foreach(string id in Items.SelectMany(m=>m.Steps.Concat(m.AlternateSteps)).Where(s=>s.Kind=="MicMute"&&!string.IsNullOrWhiteSpace(s.DeviceId)).Select(s=>s.DeviceId).Distinct())
        {
            bool? state=null;try{using var devices=new NAudio.CoreAudioApi.MMDeviceEnumerator();using var device=devices.GetDevice(id);if(device.State==NAudio.CoreAudioApi.DeviceState.Active)state=device.AudioEndpointVolume.Mute;}catch{}
            if(!microphoneStates.TryGetValue(id,out var previous)||previous!=state){microphoneStates[id]=state;changed=true;}
        }
        var cameras=Items.SelectMany(m=>m.Steps.Concat(m.AlternateSteps)).Where(s=>s.Kind=="Camera").Select(s=>s.DeviceId).Distinct().ToArray();
        if(cameras.Length>0)cameraActivity??=new CameraActivity();else {cameraActivity?.Dispose();cameraActivity=null;}
        foreach(string id in cameras)
        {
            string state=CameraControl.Enabled(id) switch{false=>"Off",true=>cameraActivity?.Streaming(id) switch{true=>"Active",false=>"Ready",_=>"Unknown"},_=>"Unknown"};
            if(!cameraStates.TryGetValue(id,out var previous)||previous!=state){cameraStates[id]=state;changed=true;}
        }
        if(changed){icons.Clear();Changed?.Invoke(this,EventArgs.Empty);}
    }
    internal void RefreshMicrophoneState()
    {
        bool? muted=null;
        try{using var devices=new NAudio.CoreAudioApi.MMDeviceEnumerator();using var device=devices.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Capture,NAudio.CoreAudioApi.Role.Communications);muted=device.AudioEndpointVolume.Mute;}catch{ }
        SetMicrophoneState(muted);
    }
    internal void SetMicrophoneState(bool? muted)
    {
        if(microphoneMuted==muted)return;
        microphoneMuted=muted;icons.Clear();Changed?.Invoke(this,EventArgs.Empty);
    }
    internal static string IconValue(MacroDefinition? macro,bool? muted)
    {
        string? stateIcon=macro?.Steps.Any(s=>s.Kind=="MicMute")==true?muted switch{true=>macro.MicMutedIcon,false=>macro.MicOnIcon,_=>null}:null;
        return string.IsNullOrWhiteSpace(stateIcon)?macro?.Icon??"?":stateIcon;
    }
    internal static string CameraIconValue(MacroDefinition macro,string state)
    {
        string? icon=state switch{"Off"=>macro.CameraOffIcon,"Ready"=>macro.CameraReadyIcon,"Active"=>macro.CameraActiveIcon,_=>null};
        return state=="Unknown"?"?":string.IsNullOrWhiteSpace(icon)?macro.Icon:icon;
    }
    internal const string Prefix="blakideck-macro:";
    private readonly string path;
    private readonly Dictionary<string,ImageSource> icons=new();
    private Exception? loadFailure;
    internal List<MacroDefinition> Items {get;private set;}=new();
    internal event EventHandler? Changed;
    internal int Revision {get;private set;}
    // Visual-only draft: execution and persistence always read the saved definition.
    private readonly Dictionary<Guid,LabelTypography> typographyPreviews=new();
    private readonly Dictionary<Guid,MacroDefinition> appearancePreviews=new();
    internal MacroDefinition? AppearanceFor(string? target)
    {
        var saved=Find(target);return saved==null?null:appearancePreviews.GetValueOrDefault(saved.Id)??saved;
    }
    internal void PreviewAppearance(Guid id,MacroDefinition? draft)
    {
        if(draft==null){if(!appearancePreviews.Remove(id))return;}
        else
        {
            var visual=draft.Clone();
            if(Items.FirstOrDefault(m=>m.Id==id) is {} saved)
            {
                // Device/action edits remain a draft until Save. Live icon state and VU
                // must continue to describe the same saved endpoint during appearance preview.
                visual.Steps=saved.Steps;visual.AlternateSteps=saved.AlternateSteps;visual.StateSourceKey=saved.StateSourceKey;
                visual.NextActionB=saved.NextActionB;visual.LastAction=saved.LastAction;
            }
            appearancePreviews[id]=visual;
        }
        icons.Clear();Changed?.Invoke(this,EventArgs.Empty);
    }
    internal LabelTypography? TypographyFor(string? target)
    {
        var macro=AppearanceFor(target);return macro==null?null:typographyPreviews.GetValueOrDefault(macro.Id)??macro.Typography??new LabelTypography{Size=16};
    }
    internal void PreviewTypography(Guid id,LabelTypography? value)
    {
        if(value==null){if(!typographyPreviews.Remove(id))return;}
        else typographyPreviews[id]=value with{};
        Changed?.Invoke(this,EventArgs.Empty);
    }
    internal MacroLibrary(string file)
    {
        path=file;
        if(!File.Exists(path)&&!File.Exists(path+".previous"))
        {
            Items=new(){Preset("Sleep computer","☾","Sleep","Computer"),Preset("Sleep monitors","▣","Sleep","Monitors"),Preset("Shutdown","⏻","Shutdown"),Preset("Restart","↻","Restart"),new(){Name="Microphone",Icon=MacroDefaultIcons.For("MicMute"),Presentation="IconText",IconMode="State",StateSourceKey="MicMute||",StateIcons=new(){["Off"]=MacroDefaultIcons.Reference("mute mic.png"),["On"]=MacroDefaultIcons.Reference("mic.png")},Steps=new(){new(){Kind="MicMute"}}}};AtomicJson.Write(path,Items);return;
        }
        Exception? error=null;
        foreach(string candidate in new[]{path,path+".previous"})
        {
            try{if(!File.Exists(candidate))continue;var items=JsonSerializer.Deserialize<List<MacroDefinition>>(File.ReadAllText(candidate))??throw new InvalidDataException("Empty macro library");
                if(items.Any(m=>m.Id==Guid.Empty)||items.Select(m=>m.Id).Distinct().Count()!=items.Count)throw new InvalidDataException("Duplicate macro identities");
                Items=items;
                bool portable=false;
                foreach(var item in Items){string icon=MacroDefaultIcons.Portable(item.Icon);portable|=icon!=item.Icon;item.Icon=icon;foreach(string key in item.StateIcons.Keys.ToArray()){string value=MacroDefaultIcons.Portable(item.StateIcons[key]);portable|=value!=item.StateIcons[key];item.StateIcons[key]=value;}}
                if(portable)AtomicJson.Write(path,Items);
                return;
            }catch(Exception ex){error=ex;}
        }
        loadFailure=new InvalidDataException("Cannot read macro library; existing files preserved.",error);
        InteractionDiagnostics.RecordFailure("Macro library load",loadFailure);
    }
    private static MacroDefinition Preset(string name,string icon,string kind,string target="Computer")=>new(){Name=name,Icon=MacroDefaultIcons.For(kind,target),Presentation="Icon",Steps=new(){new(){Kind=kind,SleepTarget=target}}};
    internal MacroDefinition EnsureSystemAction(string kind,string sleepTarget="Computer")
    {
        var found=Items.FirstOrDefault(m=>m.Steps.Count==1&&m.Steps[0].Kind==kind&&(kind!="Sleep"||m.Steps[0].SleepTarget==sleepTarget));
        if(found!=null)return found;
        var preset=Preset(kind=="Sleep"?"Sleep "+sleepTarget.ToLowerInvariant():kind,kind=="Shutdown"?"⏻":kind=="Restart"?"↻":sleepTarget=="Monitors"?"▣":"☾",kind,sleepTarget);
        Save(preset);return preset;
    }
    internal static bool IsReference(string? target)=>target?.StartsWith(Prefix,StringComparison.OrdinalIgnoreCase)==true;
    internal MacroDefinition EnsureObsAction(string action)
    {
        if(!ObsMacroAction.IsSupported(action))throw new ArgumentException("Unknown OBS action.");
        var found=Items.FirstOrDefault(m=>m.Steps.Count==1&&m.Steps[0].Kind=="OBS"&&m.Steps[0].Value==action);
        if(found!=null)return found;
        var macro=new MacroDefinition{Name=action switch{"Record"=>"OBS recording — start / stop","ReplayBuffer"=>"OBS replay buffer — start / stop",_=>"OBS save replay"},Icon=action=="Record"?"●":action=="ReplayBuffer"?"↻":"▣",Steps=new(){new(){Kind="OBS",Value=action}}};
        macro.Icon=MacroDefaultIcons.For(action);macro.Presentation="Icon";
        Save(macro);return macro;
    }
    internal MacroDefinition? Find(string? target)=>IsReference(target)&&Guid.TryParse(target![Prefix.Length..],out var id)?Items.FirstOrDefault(m=>m.Id==id):null;
    internal static string Target(MacroDefinition macro)=>Prefix+macro.Id;
    internal string Title(string? target)=>AppearanceFor(target)?.Name??"Macro unavailable";
    internal string Description(string? target)
    {
        var macro=Find(target);if(macro==null)return "Macro unavailable";
        if(macro.IconMode=="Alternate")return $"{macro.Name}\nNext action: {(macro.NextActionB?"B":"A")}\nLast completed: {macro.LastAction??"none"}";
        if(macro.IconMode=="State")return $"{macro.Name}\n{MacroStates.Source(macro)}\nState: {ReadIconState(macro)}";
        var step=macro.Steps.FirstOrDefault(s=>s.Kind is "Camera" or "MicMute");if(step==null)return macro.Name;
        string device=string.IsNullOrEmpty(step.DeviceName)?"Windows default communications microphone":step.DeviceName;
        string state;
        if(step.Kind=="Camera")state=cameraStates.GetValueOrDefault(step.DeviceId,"Unknown") switch{"Off"=>"Disabled system-wide","Ready"=>"Ready — not streaming","Active"=>"In use — streaming",_=>"State unavailable / activity not yet reported"};
        else state=(string.IsNullOrEmpty(step.DeviceId)?microphoneMuted:microphoneStates.GetValueOrDefault(step.DeviceId)) switch{true=>"Microphone muted",false=>"Microphone on",_=>"Microphone unavailable"};
        return $"{macro.Name}\n{device}\n{state}";
    }
    internal string? PresentationFor(string? target,string? fallback)=>AppearanceFor(target)?.Presentation??fallback;
    internal void Save(MacroDefinition macro)
    {
        Validate(macro);macro.Hotkey=MacroHotkeys.Normalize(macro.Hotkey);
        if(macro.Hotkey.Length>0&&Items.Any(m=>m.Id!=macro.Id&&MacroHotkeys.Normalize(m.Hotkey)==macro.Hotkey))throw new ArgumentException("These keys are already assigned to another macro.");
        MacroHotkeys.Current?.CheckAvailable(macro);
        var next=Items.Select(m=>m.Clone()).ToList();int index=next.FindIndex(m=>m.Id==macro.Id);
        if(index<0)next.Add(macro.Clone());else next[index]=macro.Clone();Commit(next);
    }
    internal void Remove(Guid id)=>Commit(Items.Where(m=>m.Id!=id).Select(m=>m.Clone()).ToList());
    internal MacroDefinition Duplicate(MacroDefinition macro){var copy=macro.Clone();copy.Id=Guid.NewGuid();copy.Name+=" copy";copy.Hotkey="";copy.NextActionB=false;copy.LastAction=null;Save(copy);return copy;}
    private void Commit(List<MacroDefinition> next){if(loadFailure!=null)throw loadFailure;AtomicJson.Write(path,next);Items=next;icons.Clear();Revision++;if(ReferenceEquals(current,this))StartMicrophoneMonitor();Changed?.Invoke(this,EventArgs.Empty);}
    internal static void Validate(MacroDefinition macro)
    {
        MacroHotkeys.Normalize(macro.Hotkey);
        if(macro.Id==Guid.Empty||string.IsNullOrWhiteSpace(macro.Name)||macro.Name.Length>80)throw new ArgumentException("Enter a name of 1–80 characters.");
        if(macro.Presentation!=null&&ShortcutAppearanceEditor.Normalize(macro.Presentation)==null)throw new ArgumentException("Choose Text, Icon or Icon and text.");
        if(macro.Steps==null||macro.Steps.Count is <1 or >100)throw new ArgumentException("Add 1–100 steps.");
        MacroStates.Validate(macro);
        foreach(var step in macro.Steps.Concat(macro.IconMode=="Alternate"?macro.AlternateSteps:Enumerable.Empty<MacroStep>()))
        {
            if(step.Kind is "Camera" or "MicMute"&&step.Operation is not ("Toggle" or "On" or "Off"))throw new ArgumentException("Choose Toggle, On or Off.");
            if(step.Kind=="Camera"&&string.IsNullOrWhiteSpace(step.DeviceId))throw new ArgumentException("Select a camera.");
            if(step.Kind is "Shutdown" or "Restart" or "Sleep" && step.CountdownSeconds is <0 or >3600)throw new ArgumentException("Countdown must be between 0 and 3600 seconds.");
            if(step.Kind=="Hue"){HueMacroAction.Validate(step);continue;}
            if(step.Kind=="Launch"){if(string.IsNullOrWhiteSpace(step.Value)||IsReference(step.Value))throw new ArgumentException("Choose a program, file or web target.");}
            else if(step.Kind=="Wait"){if(step.Milliseconds is <0 or >3600000)throw new ArgumentException("Wait must be between 0 and 3600000 ms.");}
            else if(step.Kind=="Keys")MacroKeys.Parse(step.Value);
            else if(step.Kind=="OBS"){if(!ObsMacroAction.IsSupported(step.Value))throw new ArgumentException("Choose an OBS action.");}
            else if(step.Kind=="Sleep"){if(step.SleepTarget is not ("Computer" or "Monitors"))throw new ArgumentException("Choose Computer or Monitors for sleep.");}
            else if(step.Kind is not ("Shutdown" or "Restart" or "MicMute" or "Camera"))throw new ArgumentException("Unknown macro action: "+step.Kind);
        }
    }
    internal ImageSource IconFor(string? target)
    {
        string cacheKey=target??"";if(icons.TryGetValue(cacheKey,out var cached))return cached;
        var macro=AppearanceFor(target);var mic=macro?.Steps.FirstOrDefault(s=>s.Kind=="MicMute");
        string value=IconValue(macro,!string.IsNullOrWhiteSpace(mic?.DeviceId)?microphoneStates.GetValueOrDefault(mic.DeviceId):microphoneMuted);
        if(macro?.Steps.FirstOrDefault(s=>s.Kind=="Camera") is {} camera)
        {
            string state=cameraStates.GetValueOrDefault(camera.DeviceId,"Unknown");
            value=CameraIconValue(macro,state);
        }
        if(macro?.IconMode!=null)value=MacroStates.Icon(macro,ReadIconState(macro));
        var rendered=RenderIcon(value);icons[cacheKey]=rendered;return rendered;
    }
    internal static ImageSource RenderIcon(string value)
    {
        value=MacroDefaultIcons.Resolve(value);
        value=PackagedIconPath.Resolve(value)!;
        if(File.Exists(value)){try{var image=IconBitmap.Load(value);if(image!=null)return image;}catch{}}
        var text=new FormattedText(string.IsNullOrWhiteSpace(value)?"⚡":value,System.Globalization.CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface("Segoe UI Symbol"),42,Brushes.White,1);
        var geometry=text.BuildGeometry(new Point());
        // Shell icons include transparent edge space. Preserve the symbol's aspect
        // ratio and center while giving generated icons the same optical inset.
        Rect canvas=geometry.Bounds;canvas.Inflate(canvas.Width/14,canvas.Height/14);
        var drawing=new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(Brushes.Transparent,null,new RectangleGeometry(canvas)));
        drawing.Children.Add(new GeometryDrawing(Brushes.White,null,geometry));
        drawing.Freeze();var result=new DrawingImage(drawing);result.Freeze();return result;
    }
}

internal interface IMacroExecutor
{
    Task ExecuteAsync(MacroStep step,CancellationToken token);
    void ReleaseKeys();
}
internal static class MacroKeys
{
    internal static byte[] Parse(string value)
    {
        var tokens=value.Split('+',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        if(tokens.Length is <1 or >5)throw new ArgumentException("Use a combination such as Ctrl+Shift+S.");
        var keys=new List<byte>();
        foreach(string token in tokens)
        {
            int code=token.ToLowerInvariant() switch {"ctrl" or "control"=>0x11,"alt"=>0x12,"shift"=>0x10,"win" or "windows"=>0x5b,_=>0};
            if(code==0){try{var key=(Key)new KeyConverter().ConvertFromInvariantString(token)!;code=KeyInterop.VirtualKeyFromKey(key);}catch{throw new ArgumentException("Unknown key: "+token);}}
            if(code<=0||code>255)throw new ArgumentException("Invalid key: "+token);keys.Add((byte)code);
        }
        if(keys.Distinct().Count()!=keys.Count)throw new ArgumentException("A key may appear only once.");return keys.ToArray();
    }
}
internal sealed class WindowsMacroExecutor:IMacroExecutor
{
    internal Action<string,bool>? MicrophoneStateChanged;
    internal Action<string,bool>? CameraStateChanged;
    private readonly List<byte> held=new();
    [DllImport("user32.dll")]private static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
    [DllImport("user32.dll",SetLastError=true)]private static extern IntPtr SendMessageTimeout(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam,uint flags,uint timeout,out IntPtr result);
    [DllImport("powrprof.dll",SetLastError=true)]private static extern bool SetSuspendState(bool hibernate,bool force,bool disableWake);
    public async Task ExecuteAsync(MacroStep step,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(DiagnosticState.Root!=null)throw new InvalidOperationException("Real macro execution is disabled in diagnostics.");
        switch(step.Kind)
        {
            case "Wait":await Task.Delay(step.Milliseconds,token);break;
            case "OBS":await ObsMacroAction.ExecuteAsync(step.Value,token);break;
            case "Hue":await HueMacroAction.ExecuteAsync(step,token);break;
            case "Launch":Process.Start(new ProcessStartInfo(step.Value){UseShellExecute=true})?.Dispose();break;
            case "MicMute":
                using(var devices=new NAudio.CoreAudioApi.MMDeviceEnumerator())
                using(var microphone=string.IsNullOrWhiteSpace(step.DeviceId)?devices.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Capture,NAudio.CoreAudioApi.Role.Communications):devices.GetDevice(step.DeviceId))
                {
                    if(microphone.State!=NAudio.CoreAudioApi.DeviceState.Active)throw new InvalidOperationException("Selected microphone is unavailable.");
                    bool requested=step.Operation switch{"On"=>false,"Off"=>true,_=>!microphone.AudioEndpointVolume.Mute};
                    microphone.AudioEndpointVolume.Mute=requested;
                    bool actual=microphone.AudioEndpointVolume.Mute;
                    MacroLibrary.Current.RefreshDeviceStates();
                    if(actual!=requested)throw new InvalidOperationException("Windows did not change the microphone mute state.");
                    MicrophoneStateChanged?.Invoke(microphone.FriendlyName,actual);
                }
                break;
            case "Camera":await CameraControl.Execute(step,token);CameraStateChanged?.Invoke(step.DeviceName,CameraControl.Enabled(step.DeviceId)==true);break;
            case "Keys":
                try{foreach(byte key in MacroKeys.Parse(step.Value)){token.ThrowIfCancellationRequested();held.Add(key);keybd_event(key,0,0,UIntPtr.Zero);}await Task.Delay(60,token);}finally{ReleaseKeys();}break;
            case "Sleep":
                if(step.SleepTarget=="Monitors")await Task.Run(()=>SendMessageTimeout(new IntPtr(0xffff),0x112,new IntPtr(0xf170),new IntPtr(2),2,1000,out _),token);
                else await Task.Run(()=>{if(!SetSuspendState(false,false,false))throw new System.ComponentModel.Win32Exception();},token);break;
            case "Shutdown":case "Restart":
                using(var process=Process.Start(new ProcessStartInfo("shutdown.exe",step.Kind=="Shutdown"?"/s /t 0":"/r /t 0"){UseShellExecute=false,CreateNoWindow=true}) ?? throw new InvalidOperationException("Could not start Windows shutdown."))
                {
                    await process.WaitForExitAsync(token);
                    if(process.ExitCode!=0)throw new InvalidOperationException($"Windows rejected {step.Kind} (code {process.ExitCode}).");
                }
                break;
            default:throw new ArgumentException("Unknown macro action.");
        }
    }
    public void ReleaseKeys(){for(int i=held.Count-1;i>=0;i--)keybd_event(held[i],0,2,UIntPtr.Zero);held.Clear();}
}
internal sealed class MacroRunner
{
    internal static MacroRunner Current {get;}=new();
    private CancellationTokenSource? cancellation;
    private IMacroExecutor? activeExecutor;
    internal bool IsRunning=>cancellation!=null;
    internal string Status {get;private set;}="Ready";
    internal int? SecondsRemaining {get;private set;}
    internal event EventHandler? Changed;
    internal void Stop(){cancellation?.Cancel();activeExecutor?.ReleaseKeys();}
    internal async Task RunAsync(MacroDefinition definition,IMacroExecutor executor,Func<int,CancellationToken,Task>? delay=null,Action<MacroDefinition>? completed=null)
    {
        if(IsRunning)throw new InvalidOperationException("A macro is running. Stop it before starting another.");
        var snapshot=definition.Clone();MacroLibrary.Validate(snapshot);
        var executionSteps=snapshot.IconMode=="Alternate"&&snapshot.NextActionB?snapshot.AlternateSteps:snapshot.Steps;
        using var source=new CancellationTokenSource();cancellation=source;activeExecutor=executor;
        try
        {
            for(int i=0;i<executionSteps.Count;i++)
            {
                source.Token.ThrowIfCancellationRequested();var step=executionSteps[i];
                if(step.Kind is "Shutdown" or "Restart" or "Sleep")
                    for(int seconds=step.CountdownSeconds;seconds>0;seconds--)
                    {
                        SecondsRemaining=seconds;Status=$"{(step.Kind=="Sleep"?"Sleep "+step.SleepTarget.ToLowerInvariant():step.Kind)} — {seconds} s";Changed?.Invoke(this,EventArgs.Empty);
                        await (delay??Task.Delay)(1000,source.Token);
                        source.Token.ThrowIfCancellationRequested();
                    }
                SecondsRemaining=null;Status=$"{snapshot.Name} — {i+1}/{executionSteps.Count}";Changed?.Invoke(this,EventArgs.Empty);
                source.Token.ThrowIfCancellationRequested();await executor.ExecuteAsync(step,source.Token);
            }
            source.Token.ThrowIfCancellationRequested();completed?.Invoke(snapshot);Status=snapshot.Name+" — complete";
        }
        catch(OperationCanceledException){Status=snapshot.Name+" — stopped";}
        catch{Status=snapshot.Name+" — failed";throw;}
        finally{try{executor.ReleaseKeys();}finally{SecondsRemaining=null;activeExecutor=null;cancellation=null;Changed?.Invoke(this,EventArgs.Empty);}}
    }
    internal static bool TryActivate(string? target,Window? owner)
    {
        if(!MacroLibrary.IsReference(target))return false;
        _=ActivateAsync(target,owner);return true;
    }
    private static async Task ActivateAsync(string? target,Window? owner)
    {
        try{var macro=MacroLibrary.Current.Find(target)??throw new InvalidOperationException("This macro was deleted or is unavailable. Choose another macro from the library.");
            if(Current.IsRunning)throw new InvalidOperationException("A macro is already running.");
            using var countdown=new MacroCountdown(Current,owner,MacroLibrary.Current.IconFor(target),target);await Current.RunAsync(macro,new WindowsMacroExecutor{MicrophoneStateChanged=(device,muted)=>MicrophoneFeedback.Show(owner,device,muted),CameraStateChanged=(device,on)=>MicrophoneFeedback.ShowState(owner,device,on?"CAMERA ENABLED":"CAMERA DISABLED",!on)},completed:MacroLibrary.Current.CompleteAction);}
        catch(Exception ex){InteractionDiagnostics.RecordFailure("Macro execution",ex);if(owner!=null)AppMessage.Show(owner,ex.Message,"Macro");else AppMessage.Show(ex.Message,"Macro");}
    }
}


