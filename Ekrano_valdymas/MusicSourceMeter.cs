using NAudio.CoreAudioApi;
using System.Diagnostics;
namespace Ekrano_valdymas;
// Session metering is tapped before session volume; verified at 100/50/10/0%.
internal sealed class MusicSourceMeter : IDisposable
{
    private MMDeviceEnumerator? enumerator;
    private readonly List<MMDevice> devices = new();
    private readonly List<AudioSessionControl> sessions = new();
    private string key="";
    private long nextRefresh;
    internal void Read(string[] processes,out double left,out double right)
    {
        left=right=0;
        try
        {
            string current=string.Join("|",processes);
            if(current!=key||Environment.TickCount64>=nextRefresh)
            {
                Dispose();key=current;nextRefresh=Environment.TickCount64+1500;
                enumerator=new MMDeviceEnumerator();
                using var outputs=enumerator.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active);
                foreach(var device in outputs)
                {
                    devices.Add(device);
                    try
                    {
                        var all=device.AudioSessionManager.Sessions;
                        for(int i=0;i<all.Count;i++)
                        {
                            var session=all[i];string name="";
                            try{using var process=Process.GetProcessById((int)session.GetProcessID);name=process.ProcessName;}catch{}
                            if(processes.Contains(name,StringComparer.OrdinalIgnoreCase))sessions.Add(session);else session.Dispose();
                        }
                    }
                    catch { /* Continue with other active output devices. */ }
                }
            }
            foreach(var session in sessions)
            {
                var channels=session.AudioMeterInformation.PeakValues;
                double l=channels.Count>0?channels[0]:session.AudioMeterInformation.MasterPeakValue;
                double r=channels.Count>1?channels[1]:l;
                left=Math.Max(left,l);right=Math.Max(right,r);
            }
        }
        catch {Dispose();nextRefresh=Environment.TickCount64+1500;}
    }
    public void Dispose(){foreach(var s in sessions)s.Dispose();sessions.Clear();foreach(var d in devices)d.Dispose();devices.Clear();enumerator?.Dispose();enumerator=null;}
}
