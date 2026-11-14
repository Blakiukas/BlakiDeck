using System.Text.Json;

namespace Ekrano_valdymas;
internal static class ObsMacroAction
{
 internal static bool IsSupported(string action)=>action is "Record" or "ReplayBuffer" or "SaveReplay";
 internal static async Task ExecuteAsync(string action,CancellationToken token)
 {
  using var client=new ObsWebSocketClient();
  await client.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(8),token);
  await RunAsync(action,request=>client.SendRequestAsync(request).WaitAsync(TimeSpan.FromSeconds(8),token),token);
 }
 internal static async Task RunAsync(string action,Func<string,Task<JsonElement>> request,CancellationToken token)
 {
  if(!IsSupported(action))throw new ArgumentException("Choose an OBS action.");
  token.ThrowIfCancellationRequested();
  var status=await request(action=="Record"?"GetRecordStatus":"GetReplayBufferStatus");
  bool active=status.GetProperty("outputActive").GetBoolean();
  if(action=="SaveReplay"&&!active)throw new InvalidOperationException("Start the OBS replay buffer before saving a replay.");
  token.ThrowIfCancellationRequested();
  await request(action switch {"Record"=>active?"StopRecord":"StartRecord","ReplayBuffer"=>active?"StopReplayBuffer":"StartReplayBuffer",_=>"SaveReplayBuffer"});
 }
}
