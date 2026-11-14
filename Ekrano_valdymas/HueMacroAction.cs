namespace Ekrano_valdymas;
internal static class HueMacroAction
{
    private static (HueBridgeClient Client,string Key) Connect()
    {
        var store=new HueSettingsStore();var settings=store.Load();
        if(settings?.BridgeId is not string id||settings.BridgeIp is not string ip||store.UnprotectKey(settings) is not string key)
            throw new InvalidOperationException("Connect Philips Hue in the Hue module first.");
        return(new HueBridgeClient(new HueBridgeInfo(id,ip)),key);
    }
    internal static async Task<IReadOnlyList<HueRoom>> RoomsAsync(CancellationToken token)
    {
        if(DiagnosticState.Root!=null)return Array.Empty<HueRoom>();
        var connection=Connect();using var client=connection.Client;
        return await client.GetRoomsAsync(connection.Key,token);
    }
    internal static void Validate(MacroStep step)
    {
        if(!Guid.TryParse(step.DeviceId,out _))throw new ArgumentException("Choose a Hue room / group.");
        if(step.Operation is not ("On" or "Off" or "Toggle" or "Brightness"))throw new ArgumentException("Choose a Hue action.");
        if(!double.IsFinite(step.Brightness)||step.Brightness<0||step.Brightness>100)throw new ArgumentException("Brightness must be 0–100%.");
    }
    internal static (bool? On,double? Brightness) Command(MacroStep step,bool currentOn)
    {
        Validate(step);
        return step.Operation switch{"On"=>(true,null),"Off"=>(false,null),"Toggle"=>(!currentOn,null),_=>step.Brightness==0?(false,null):(null,step.Brightness)};
    }
    internal static async Task ExecuteAsync(MacroStep step,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();Validate(step);
        if(DiagnosticState.Root!=null)throw new InvalidOperationException("Real Hue commands are disabled in diagnostics.");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var connection=Connect();using var client=connection.Client;
        var rooms=await client.GetRoomsAsync(connection.Key,timeout.Token);
        var room=rooms.FirstOrDefault(r=>r.GroupedLightId==step.DeviceId)??throw new InvalidOperationException("The saved Hue room is no longer available. Edit this action.");
        if(room.IsReachable==false)throw new InvalidOperationException("The Hue room is disconnected.");
        var command=Command(step,room.IsOn);
        if(command.On is bool on)await client.SetRoomOnAsync(connection.Key,room.GroupedLightId,on,timeout.Token);
        else await client.SetRoomBrightnessAsync(connection.Key,room.GroupedLightId,command.Brightness!.Value,timeout.Token);
    }
}
