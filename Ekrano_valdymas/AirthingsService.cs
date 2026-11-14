using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Ekrano_valdymas;

internal sealed record AirthingsReading(string SensorType,double? Value,string Unit);
internal sealed record AirthingsDevice(string Account,string Serial,string Home,string Name,string Type,string[] Supported,AirthingsReading[] Readings,DateTimeOffset? Recorded,string? RecordedText=null);
internal sealed record AirthingsCredentials(string ClientId,string ClientSecret);
internal sealed partial class AirthingsService:IDisposable
{
    internal static AirthingsService Shared {get;}=new();
    private readonly HttpClient http;
    private readonly CancellationTokenSource lifetime=new();
    private readonly SemaphoreSlim gate=new(1,1);
    private readonly Queue<DateTimeOffset> requests=new();
    private string? token;
    private DateTimeOffset expires,nextRead;
    private AirthingsCredentials? credentials;
    private Task? loop;
    internal AirthingsArchive Archive {get;}=new();
    internal event Action? Changed;
    internal AirthingsDevice[] Devices {get;private set;}=[];
    internal string Status {get;private set;}="Not connected — open Setup";
    internal bool HasCredentials=>credentials!=null;
    internal DateTimeOffset? RetrievedAt {get;private set;}
    private static string SettingsPath=>Path.Combine(DiagnosticState.LocalApplicationData,"Ekrano_valdymas","airthings-protected.json");
    private static readonly byte[] entropy=Encoding.UTF8.GetBytes("BlakiDeck.Airthings.v1");
    internal AirthingsService(HttpMessageHandler? handler=null){http=handler==null?new(new HttpClientHandler{AllowAutoRedirect=false}):new(handler);http.Timeout=TimeSpan.FromSeconds(20);}
    internal void Start(){if(DiagnosticState.Root!=null||loop!=null)return;credentials=Load();if(credentials!=null)loop=Run();}
    private static AirthingsCredentials? Load(string? path=null){try{return JsonSerializer.Deserialize<AirthingsCredentials>(Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(File.ReadAllText(path??SettingsPath)),entropy,DataProtectionScope.CurrentUser)));}catch{return null;}}
    internal async Task Configure(string id,string secret)
    {
        if(string.IsNullOrWhiteSpace(id)||string.IsNullOrWhiteSpace(secret)){Status="Enter Client ID and Client secret";Changed?.Invoke();return;}
        await gate.WaitAsync(lifetime.Token);
        try{credentials=new(id.Trim(),secret.Trim());token=null;Devices=[];RetrievedAt=null;nextRead=default;
            var protectedValue=Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(credentials)),entropy,DataProtectionScope.CurrentUser));Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);File.WriteAllText(SettingsPath+".tmp",protectedValue);File.Move(SettingsPath+".tmp",SettingsPath,true);
        }finally{gate.Release();}
        await Refresh();if(loop==null&&DiagnosticState.Root==null)loop=Run();
    }
    private async Task Run(){while(!lifetime.IsCancellationRequested){await Refresh();try{await Task.Delay(TimeSpan.FromSeconds(30),lifetime.Token);}catch(OperationCanceledException){break;}}}
    private async Task<JsonDocument> Send(HttpRequestMessage message,CancellationToken ct)
    {
        var now=DateTimeOffset.UtcNow;while(requests.Count>0&&now-requests.Peek()>TimeSpan.FromHours(1))requests.Dequeue();
        if(requests.Count>=100){nextRead=requests.Peek().AddHours(1);throw new HttpRequestException("Local request budget",null,HttpStatusCode.TooManyRequests);}requests.Enqueue(now);
        using(message)using(var response=await http.SendAsync(message,ct)){
            if(response.StatusCode==HttpStatusCode.TooManyRequests)nextRead=now+(response.Headers.RetryAfter?.Delta??TimeSpan.FromMinutes(5));
            if(response.StatusCode==HttpStatusCode.Unauthorized)token=null;
            response.EnsureSuccessStatusCode();return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));}
    }
    private Task<JsonDocument> Get(string relative,CancellationToken ct){var request=new HttpRequestMessage(HttpMethod.Get,"https://consumer-api.airthings.com/v1/"+relative);request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);return Send(request,ct);}
    internal async Task Refresh(bool retry=false)
    {
        if(retry&&!Status.StartsWith("Rate limit"))nextRead=default;
        if(credentials==null||DateTimeOffset.UtcNow<nextRead||!await gate.WaitAsync(0))return;
        try{
            Status=Devices.Length==0?"Loading devices…":"Refreshing…";Changed?.Invoke();var ct=lifetime.Token;
            if(token==null||DateTimeOffset.UtcNow>=expires){using var auth=await Send(new HttpRequestMessage(HttpMethod.Post,"https://accounts-api.airthings.com/v1/token"){Content=JsonContent.Create(new{grant_type="client_credentials",client_id=credentials.ClientId,client_secret=credentials.ClientSecret,scope=new[]{"read:device:current_values"}})},ct);token=auth.RootElement.GetProperty("access_token").GetString();expires=DateTimeOffset.UtcNow.AddSeconds(Math.Max(1,auth.RootElement.GetProperty("expires_in").GetDouble()-60));}
            using var accounts=await Get("accounts",ct);var devices=new List<AirthingsDevice>();
            foreach(var account in accounts.RootElement.GetProperty("accounts").EnumerateArray()){
                string id=account.GetProperty("id").GetString()!;string route="accounts/"+Uri.EscapeDataString(id);
                using var inventory=await Get(route+"/devices",ct);var samples=new Dictionary<string,(AirthingsReading[],DateTimeOffset?,string)>();
                for(int page=1;page<=20;page++){using var readings=await Get(route+"/sensors?unit=metric&pageNumber="+page,ct);foreach(var sample in readings.RootElement.GetProperty("results").EnumerateArray()){
                    var values=sample.GetProperty("sensors").EnumerateArray().Select(s=>new AirthingsReading(s.GetProperty("sensorType").GetString()!,s.TryGetProperty("value",out var v)&&v.ValueKind==JsonValueKind.Number&&v.TryGetDouble(out double n)&&double.IsFinite(n)?n:null,Text(s,"unit"))).ToArray();
                    samples[Text(sample,"serialNumber")]=(values,ParseRecorded(Text(sample,"recorded")),Text(sample,"recorded"));}
                    if(!readings.RootElement.TryGetProperty("hasNext",out var more)||!more.GetBoolean())break;if(page==20)throw new InvalidDataException("Pagination limit");}
                foreach(var d in inventory.RootElement.GetProperty("devices").EnumerateArray()){string serial=Text(d,"serialNumber");samples.TryGetValue(serial,out var sample);devices.Add(new(id,serial,Text(d,"home"),Text(d,"name"),Text(d,"type"),d.GetProperty("sensors").EnumerateArray().Select(x=>x.GetString()!).ToArray(),sample.Item1??[],sample.Item2,sample.Item3));}
            }
            Devices=devices.ToArray();RetrievedAt=DateTimeOffset.UtcNow;Status=Devices.Length==0?"Connected — no devices":"Connected";
            try{await Task.Run(()=>Archive.Record(Devices),ct);}catch{Status="Connected — local history could not be saved";}
            nextRead=DateTimeOffset.UtcNow.AddMinutes(Math.Max(5,1+devices.Select(d=>d.Account).Distinct().Count()));
        }
        catch(OperationCanceledException){Status="Connection cancelled";}
        catch(HttpRequestException e){Status=e.StatusCode switch{HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden=>"Access denied — check Client Credentials and read:device:current_values",HttpStatusCode.TooManyRequests=>"Rate limit — waiting before retry",_=>"Connection failed — check network, then Retry"};nextRead=nextRead>DateTimeOffset.UtcNow?nextRead:DateTimeOffset.UtcNow.AddMinutes(5);}
        catch{Status="Unexpected API response — Retry or check setup";nextRead=DateTimeOffset.UtcNow.AddMinutes(5);}
        finally{gate.Release();Changed?.Invoke();}
    }
    internal static DateTimeOffset? ParseRecorded(string? text)=>DateTimeOffset.TryParse(text,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.AssumeUniversal|System.Globalization.DateTimeStyles.AdjustToUniversal,out var date)?date:null;
    private static string Text(JsonElement e,string key)=>e.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
    internal async Task ReadSavedAccountForProbe(){credentials=Load(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Ekrano_valdymas","airthings-protected.json"));Devices=[];RetrievedAt=null;nextRead=default;if(credentials==null){Status="No readable saved credentials";Changed?.Invoke();return;}await Refresh();}
    internal void SetFixture(AirthingsDevice[] devices,string status){Devices=devices;Status=status;RetrievedAt=DateTimeOffset.UtcNow;Changed?.Invoke();}
    public void Dispose(){lifetime.Cancel();http.Dispose();}
}
