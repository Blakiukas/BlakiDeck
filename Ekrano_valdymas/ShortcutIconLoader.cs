using System.Collections.Concurrent;
using System.Threading;
using System.Windows.Media;
namespace Ekrano_valdymas;

// Shell extensions and .lnk resolution can block for seconds on a cold access.
// Run them on a dedicated STA, never during WPF layout or a tab click.
internal static class ShortcutIconLoader
{
 private static readonly BlockingCollection<Action> work = new();
 internal static Func<string, string?, ImageSource?>? ProbeResolver;
 static ShortcutIconLoader()
 {
  var thread = new Thread(() => { foreach(var action in work.GetConsumingEnumerable()) action(); }) { IsBackground=true, Name="Shortcut icon resolver" };
  thread.SetApartmentState(ApartmentState.STA);thread.Start();
 }
 internal static Task<ImageSource?> LoadAsync(string target,string? custom)
 {
  var completion=new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
  work.Add(()=>{
   try {
    var resolver=ProbeResolver;
    var image=resolver!=null?resolver(target,custom):ShortcutIcon.Load(custom)??QuickLaunchBar.GetShellIcon(target);
    if(image!=null&&!image.IsFrozen)image.Freeze();completion.SetResult(image);
   }catch(Exception e){InteractionDiagnostics.RecordFailure("Shortcut icon background load",e);completion.SetResult(null);}
  });
  return completion.Task;
 }
}
