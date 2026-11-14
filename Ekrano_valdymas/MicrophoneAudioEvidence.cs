using NAudio.CoreAudioApi;

namespace Ekrano_valdymas;
// Read-only endpoint/session evidence. No volume writes and no retained PCM.
internal static class MicrophoneAudioEvidence
{
    internal static object? Comparison;
    internal static async Task Compare(string id,CancellationToken stop)
    {
        try{
            using var devices=new MMDeviceEnumerator();using var device=string.IsNullOrEmpty(id)?devices.GetDefaultAudioEndpoint(DataFlow.Capture,Role.Communications):devices.GetDevice(id);
            using var input=await new NAudio.Wave.WasapiRecorderBuilder().WithDevice(device).WithSharedMode().WithBufferLength(50).BuildAsync();
            long packets=0;double max=0;
            await foreach(var packet in input.CaptureAsync(stop)){
                var peaks=MicrophoneLevels.Peaks(packet.Data.Span,packet.Data.Length,input.WaveFormat);max=Math.Max(max,Math.Max(peaks.Left,peaks.Right));
                Comparison=new{packets=++packets,bytes=packet.Data.Length,lastPeak=peaks.Left,maximumPeak=max,format=input.WaveFormat.ToString()};
            }
        }catch(OperationCanceledException){}catch(Exception ex){Comparison=new{error=ex.ToString()};}
    }
    internal static object Read(string id)
    {
        using var devices=new MMDeviceEnumerator();
        using var microphone=string.IsNullOrEmpty(id)?devices.GetDefaultAudioEndpoint(DataFlow.Capture,Role.Communications):devices.GetDevice(id);
        var sessions=new List<object>();
        foreach(var flow in new[]{DataFlow.Capture,DataFlow.Render})
            foreach(var endpoint in devices.EnumerateAudioEndPoints(flow,DeviceState.Active))using(endpoint)
            {
                try{
                    var collection=endpoint.AudioSessionManager.Sessions;
                    for(int i=0;i<collection.Count;i++)using(var session=collection[i])
                    {
                        string process="";try{process=System.Diagnostics.Process.GetProcessById((int)session.GetProcessID).ProcessName;}catch{}
                        if(session.GetProcessID!=(uint)Environment.ProcessId&&!process.Equals("BlakiDeck",StringComparison.OrdinalIgnoreCase))continue;
                        sessions.Add(new{flow=flow.ToString(),endpoint=endpoint.FriendlyName,pid=session.GetProcessID,process,volume=session.SimpleAudioVolume.Volume,muted=session.SimpleAudioVolume.Mute,state=session.State.ToString(),peak=session.AudioMeterInformation.MasterPeakValue});
                    }
                }catch(Exception ex){sessions.Add(new{flow=flow.ToString(),endpoint=endpoint.FriendlyName,error=ex.Message});}
            }
        return new{endpoint=microphone.FriendlyName,id=microphone.ID,windowsEndpointPeak=microphone.AudioMeterInformation.MasterPeakValue,endpointVolume=microphone.AudioEndpointVolume.MasterVolumeLevelScalar,endpointMuted=microphone.AudioEndpointVolume.Mute,capture=MicrophoneLevels.DiagnosticCapture,comparison=Comparison,sessions};
    }
}
