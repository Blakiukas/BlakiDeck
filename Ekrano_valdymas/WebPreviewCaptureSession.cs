using System.IO;

namespace Ekrano_valdymas;

// UI-thread ownership: navigation, association and persistence are checked again
// after every asynchronous operation, before publishing a new thumbnail.
internal sealed class WebPreviewCaptureSession : IDisposable
{
    internal static readonly TimeSpan AutomaticDelay=TimeSpan.FromSeconds(5);
    private readonly Func<bool> associated,viewportReady,hasExisting;
    private readonly Func<string> allocatePath;
    private readonly Func<Stream,Task> capture;
    private readonly Func<string,bool> commit;
    private readonly Func<TimeSpan,CancellationToken,Task> delay;
    private CancellationTokenSource? automatic;
    private ulong navigationId;
    private long generation;
    private bool ready,busy,disposed;
    internal event Action? Changed;
    internal bool IsBusy=>busy;
    internal bool CanCapture=>!disposed&&ready&&!busy&&associated()&&viewportReady();
    internal Exception? LastFailure {get;private set;}

    internal WebPreviewCaptureSession(Func<bool> associated,Func<bool> viewportReady,
        Func<bool> hasExisting,Func<string> allocatePath,Func<Stream,Task> capture,
        Func<string,bool> commit,Func<TimeSpan,CancellationToken,Task>? delay=null)
    {
        this.associated=associated;this.viewportReady=viewportReady;this.hasExisting=hasExisting;
        this.allocatePath=allocatePath;this.capture=capture;this.commit=commit;
        this.delay=delay??((duration,token)=>Task.Delay(duration,token));
    }

    internal void NavigationStarting(ulong id)
    {
        navigationId=id;generation++;ready=false;CancelAutomatic();Changed?.Invoke();
    }

    internal void NavigationCompleted(ulong id,bool success)
    {
        if(disposed||id!=navigationId)return;
        ready=success;Changed?.Invoke();
        if(!success||hasExisting()||!associated())return;
        CancelAutomatic();var pending=new CancellationTokenSource();automatic=pending;
        _=CaptureInitialAfterDelay(generation,pending);
    }

    private async Task CaptureInitialAfterDelay(long expected,CancellationTokenSource pending)
    {
        try
        {
            await delay(AutomaticDelay,pending.Token);
            if(!pending.IsCancellationRequested&&generation==expected&&!disposed&&!hasExisting())
                await CaptureAsync(automaticCapture:true);
        }
        catch(OperationCanceledException)when(pending.IsCancellationRequested){}
        catch(Exception exception){LastFailure=exception;}
        finally{if(ReferenceEquals(automatic,pending))automatic=null;pending.Dispose();}
    }

    internal Task<bool> CaptureAsync()=>CaptureAsync(automaticCapture:false);
    private async Task<bool> CaptureAsync(bool automaticCapture)
    {
        if(!CanCapture||(automaticCapture&&hasExisting()))return false;
        CancelAutomatic();busy=true;LastFailure=null;Changed?.Invoke();
        long expected=generation;string? path=null,temp=null;bool published=false,committed=false;
        bool Current()=>!disposed&&ready&&generation==expected&&associated()&&viewportReady()&&(!automaticCapture||!hasExisting());
        try
        {
            path=Path.GetFullPath(allocatePath());
            temp=path+".capture-"+Guid.NewGuid().ToString("N")+".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,FileOptions.Asynchronous))
            {
                await capture(stream);await stream.FlushAsync();
                if(stream.Length==0)throw new IOException("The page capture produced an empty image.");
            }
            if(!Current())return false;
            // A fresh destination preserves the previous image if capture or
            // settings persistence fails. No file is overwritten in place.
            File.Move(temp,path);published=true;
            if(!Current())return false;
            committed=commit(path);return committed;
        }
        catch(Exception exception){LastFailure=exception;return false;}
        finally
        {
            if(temp!=null)DeleteUncommitted(temp);
            if(published&&!committed&&path!=null)DeleteUncommitted(path);
            busy=false;if(!disposed)Changed?.Invoke();
        }
    }

    internal void RefreshState(){if(!disposed)Changed?.Invoke();}
    internal void Invalidate()
    {
        generation++;ready=false;CancelAutomatic();if(!disposed)Changed?.Invoke();
    }
    private void CancelAutomatic(){var pending=automatic;automatic=null;pending?.Cancel();}
    private static void DeleteUncommitted(string path){try{File.Delete(path);}catch{}}
    public void Dispose(){if(disposed)return;disposed=true;generation++;ready=false;CancelAutomatic();Changed=null;}
}
