using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Ekrano_valdymas;

// One transient input stream per device, shared by visible macro references.
// Only channel peaks survive each callback; PCM is never retained or written.
internal static class MicrophoneLevels
{
    internal sealed record Reading(int Channels,double Left,double Right,bool Muted,bool Available,long Timestamp)
    {
        internal static readonly Reading Missing=new(0,0,0,false,false,0);
    }
    private static readonly Dictionary<string,Entry> entries=new(StringComparer.OrdinalIgnoreCase);
    internal sealed record CaptureEvidence(string Endpoint,string Format,int BlockAlign,int Bytes,string Flags,long Callbacks,long SilentCallbacks,long EmptyCallbacks,double RawPeak,double MaximumPeak);
    internal static CaptureEvidence? DiagnosticCapture;
    internal sealed class Lease : IDisposable
    {
        private Entry? entry;
        internal Lease(Entry value){entry=value;}
        internal Reading Read()=>entry?.Read()??Reading.Missing;
        public void Dispose(){if(entry is not {} value)return;entry=null;lock(entries){if(--value.Users==0){entries.Remove(value.Key);value.Stop.Cancel();}}}
    }
    internal static Lease Acquire(string? id)
    {
        string key=id??"";
        lock(entries){if(!entries.TryGetValue(key,out var value)){value=new Entry(key);entries.Add(key,value);}value.Users++;return new Lease(value);}
    }
    internal sealed class Entry
    {
        internal readonly string Key;internal int Users;internal readonly CancellationTokenSource Stop=new();
        private Reading reading=Reading.Missing;private volatile bool muted;
        internal Entry(string key){Key=key;if(DiagnosticState.Root==null||DiagnosticState.LiveMicrophone)_=Task.Run(Run);}
        internal Reading Read()
        {
            var r=Volatile.Read(ref reading);
            return muted||Environment.TickCount64-r.Timestamp>350?r with{Left=0,Right=0,Muted=muted}:r with{Muted=muted};
        }
        private async Task Run()
        {
            MMDevice? device=null;WasapiRecorder? capture=null;string? current=null;int failed=0;
            void Close(){var old=capture;capture=null;if(old!=null){try{old.StopRecording();}catch{}try{old.Dispose();}catch{}}try{device?.Dispose();}catch{}device=null;current=null;}
            try
            {
                using var devices=new MMDeviceEnumerator();
                while(!Stop.IsCancellationRequested)
                {
                    try
                    {
                        var selected=string.IsNullOrWhiteSpace(Key)?devices.GetDefaultAudioEndpoint(DataFlow.Capture,Role.Communications):devices.GetDevice(Key);
                        if(selected.State!=DeviceState.Active){selected.Dispose();throw new InvalidOperationException("Microphone unavailable");}
                        if(selected.ID!=current||Volatile.Read(ref failed)!=0)
                        {
                            Close();device=selected;current=selected.ID;Interlocked.Exchange(ref failed,0);
                            var input=await new WasapiRecorderBuilder().WithDevice(device).WithSharedMode().WithEventSync().WithBufferLength(50).BuildAsync();capture=input;
                            var format=input.WaveFormat;
                            long callbacks=0,silentCallbacks=0,emptyCallbacks=0;double maximumPeak=0;
                            Volatile.Write(ref reading,new(format.Channels,0,0,device.AudioEndpointVolume.Mute,true,Environment.TickCount64));
                            input.DataAvailable+=(buffer,flags,position,qpc)=>
                            {
                                if(!ReferenceEquals(capture,input))return;
                                var (left,right)=(flags&AudioClientBufferFlags.Silent)!=0?(0d,0d):Peaks(buffer,buffer.Length,format);
                                if(DiagnosticState.LiveMicrophone){callbacks++;if(buffer.IsEmpty)emptyCallbacks++;if((flags&AudioClientBufferFlags.Silent)!=0)silentCallbacks++;maximumPeak=Math.Max(maximumPeak,Math.Max(left,right));Volatile.Write(ref DiagnosticCapture,new(current!,format.ToString(),format.BlockAlign,buffer.Length,flags.ToString(),callbacks,silentCallbacks,emptyCallbacks,Math.Max(left,right),maximumPeak));}
                                // A callback without frames is not a silent audio packet. Keep the
                                // last real sample; Read() expires it if capture actually stalls.
                                if(buffer.IsEmpty)return;
                                Volatile.Write(ref reading,new(format.Channels,left,right,muted,true,Environment.TickCount64));
                            };
                            input.RecordingStopped+=(_,_)=>{if(ReferenceEquals(capture,input))Interlocked.Exchange(ref failed,1);};
                            input.StartRecording();
                        }
                        else selected.Dispose();
                        muted=device!.AudioEndpointVolume.Mute;
                    }
                    catch{Close();Volatile.Write(ref reading,Reading.Missing);}
                    await Task.Delay(500,Stop.Token);
                }
            }
            catch(OperationCanceledException){}
            finally{Close();Stop.Dispose();}
        }
    }
    internal static (double Left,double Right) Peaks(ReadOnlySpan<byte> data,int count,WaveFormat format)
    {
        int channels=format.Channels,bytes=format.BitsPerSample/8,frame=format.BlockAlign;
        if(channels<1||bytes<1||frame<channels*bytes)return(0,0);
        bool floating=format.Encoding==WaveFormatEncoding.IeeeFloat||format is WaveFormatExtensible ext&&ext.SubFormat==new Guid("00000003-0000-0010-8000-00aa00389b71");
        static double Sample(ReadOnlySpan<byte> data,int at,int bytes,bool floating)
        {
            double value=floating&&bytes==4?BitConverter.ToSingle(data.Slice(at,4)):bytes switch
            {1=>(data[at]-128)/128d,2=>BitConverter.ToInt16(data.Slice(at,2))/32768d,3=>((data[at]|data[at+1]<<8|data[at+2]<<16)<<8>>8)/8388608d,4=>BitConverter.ToInt32(data.Slice(at,4))/2147483648d,_=>0};
            return double.IsFinite(value)?Math.Clamp(Math.Abs(value),0,1):0;
        }
        double left=0,right=0;for(int at=0;at+frame<=Math.Min(count,data.Length);at+=frame){left=Math.Max(left,Sample(data,at,bytes,floating));right=Math.Max(right,channels==1?left:Sample(data,at+bytes,bytes,floating));}
        return(left,channels==1?left:right);
    }
}
