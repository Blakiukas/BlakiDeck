namespace Ekrano_valdymas;

internal static class MacroStates
{
    internal static bool Supports(MacroStep step)=>step.Kind is "MicMute" or "Camera" || step.Kind=="OBS"&&step.Value is "Record" or "ReplayBuffer";
    internal static string Key(MacroStep step)=>$"{step.Kind}|{step.DeviceId}|{(step.Kind=="OBS"?step.Value:"")}";
    internal static MacroStep? Source(MacroDefinition macro)=>macro.Steps.FirstOrDefault(s=>Supports(s)&&Key(s)==macro.StateSourceKey);
    internal static void UpgradeEditor(MacroDefinition macro)
    {
        if(macro.IconMode!=null)return;
        var source=macro.Steps.FirstOrDefault(s=>s.Kind=="Camera")??macro.Steps.FirstOrDefault(s=>s.Kind=="MicMute");
        macro.IconMode=source==null?"Single":"State";
        if(source==null)return;
        macro.StateSourceKey=Key(source);
        if(source.Kind=="Camera")
        {
            macro.StateIcons["Off"]=macro.CameraOffIcon??"";macro.StateIcons["Ready"]=macro.CameraReadyIcon??"";
            macro.StateIcons["Active"]=macro.CameraActiveIcon??"";macro.StateIcons["Unknown"]="?";
        }
        else {macro.StateIcons["Off"]=macro.MicMutedIcon??"";macro.StateIcons["On"]=macro.MicOnIcon??"";}
    }
    internal static string Icon(MacroDefinition macro,string state)
    {
        if(macro.IconMode=="Single")return macro.Icon;
        string key=state;
        if(macro.IconMode=="Alternate")key=macro.IconMeaning=="Next"?(macro.NextActionB?"B":"A"):macro.LastAction??"Unknown";
        else if(macro.IconMeaning=="Next")
        {
            var source=Source(macro);
            if(source==null||state=="Unknown")key="Unknown";
            else if(source.Kind=="OBS")key=state=="On"?"Off":"On";
            else key=source.Operation switch {"On"=>source.Kind=="Camera"?"Ready":"On","Off"=>"Off",_=>state=="Off"?(source.Kind=="Camera"?"Ready":"On"):"Off"};
        }
        return macro.StateIcons.TryGetValue(key,out var icon)&&!string.IsNullOrWhiteSpace(icon)?icon:macro.Icon;
    }
    internal static void Validate(MacroDefinition macro)
    {
        if(macro.IconMode is not (null or "Single" or "State" or "Alternate"))throw new ArgumentException("Choose an icon mode.");
        if(macro.IconMeaning is not ("Current" or "Next"))throw new ArgumentException("Choose current state or next action.");
        if(macro.StateIcons==null||macro.AlternateSteps==null)throw new ArgumentException("Invalid state settings.");
        if(macro.IconMode=="State"&&Source(macro)==null)throw new ArgumentException("Choose a microphone, camera or OBS toggle step as the state source.");
        if(macro.IconMode=="State"&&macro.IconMeaning=="Next"&&macro.Steps.Count(s=>Supports(s)&&Key(s)==macro.StateSourceKey)>1)throw new ArgumentException("Next-action icons need a single step for the selected state source. Use current state for repeated controls.");
        if(macro.IconMode=="Alternate"&&macro.AlternateSteps.Count is <1 or >100)throw new ArgumentException("Add 1–100 steps to action B.");
        if(macro.LastAction is not (null or "A" or "B"))throw new ArgumentException("Invalid last action.");
    }
}

internal sealed partial class MacroLibrary
{
    private ObsWebSocketClient? obsIconsClient;
    private bool obsIconsBusy,obsIconsDisposed;
    private DateTime obsRetryAt;
    private readonly Dictionary<string,string> obsIconStates=new();
    internal string ReadIconState(MacroDefinition macro)
    {
        var source=MacroStates.Source(macro);
        if(source==null)return "Unknown";
        if(source.Kind=="Camera")return cameraStates.GetValueOrDefault(source.DeviceId,"Unknown");
        if(source.Kind=="OBS")return obsIconStates.GetValueOrDefault(source.Value,"Unknown");
        return (string.IsNullOrEmpty(source.DeviceId)?microphoneMuted:microphoneStates.GetValueOrDefault(source.DeviceId)) switch{true=>"Off",false=>"On",_=>"Unknown"};
    }
    internal void SetObsIconState(string action,string state)
    {
        if(obsIconStates.GetValueOrDefault(action,"Unknown")==state)return;
        obsIconStates[action]=state;icons.Clear();Changed?.Invoke(this,EventArgs.Empty);
    }
    private async Task RefreshObsIconsAsync()
    {
        if(DiagnosticState.Root!=null||obsIconsDisposed||obsIconsBusy||DateTime.UtcNow<obsRetryAt)return;
        var actions=Items.Where(m=>m.IconMode=="State").Select(MacroStates.Source).Where(s=>s?.Kind=="OBS").Select(s=>s!.Value).Distinct().ToArray();
        if(actions.Length==0){obsIconsClient?.Dispose();obsIconsClient=null;obsIconStates.Clear();return;}
        obsIconsBusy=true;
        try
        {
            obsIconsClient??=new ObsWebSocketClient();
            if(!obsIconsClient.IsConnected)await obsIconsClient.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(3));
            foreach(string action in actions)
            {
                var status=await obsIconsClient.SendRequestAsync(action=="Record"?"GetRecordStatus":"GetReplayBufferStatus").WaitAsync(TimeSpan.FromSeconds(3));
                if(!obsIconsDisposed)SetObsIconState(action,status.GetProperty("outputActive").GetBoolean()?"On":"Off");
            }
        }
        catch
        {
            obsIconsClient?.Dispose();obsIconsClient=null;obsRetryAt=DateTime.UtcNow.AddSeconds(10);
            if(!obsIconsDisposed)foreach(string action in actions)SetObsIconState(action,"Unknown");
        }
        finally {obsIconsBusy=false;}
    }
    internal void CompleteAction(MacroDefinition executed)
    {
        if(executed.IconMode!="Alternate"){RefreshDeviceStates();return;}
        var currentMacro=Find(Target(executed));
        if(currentMacro==null||currentMacro.IconMode!="Alternate")return;
        var next=currentMacro.Clone();next.LastAction=executed.NextActionB?"B":"A";next.NextActionB=!executed.NextActionB;Save(next);
    }
}
