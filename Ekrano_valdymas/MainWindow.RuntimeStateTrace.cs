using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
namespace Ekrano_valdymas;
public partial class MainWindow
{
    private static IEnumerable<DependencyObject> RuntimeTraceDescendants(DependencyObject root)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=VisualTreeHelper.GetChild(root,i);
            yield return child;
            foreach(var descendant in RuntimeTraceDescendants(child))yield return descendant;
        }
    }
    private async void TraceRuntimeStateAfterStartup()
    {
        if(!InteractionDiagnostics.Enabled||DiagnosticState.Root!=null)return;
        var operations=new System.Collections.Concurrent.ConcurrentDictionary<string,int>();
        var methodField=typeof(System.Windows.Threading.DispatcherOperation).GetFields(BindingFlags.Instance|BindingFlags.NonPublic).FirstOrDefault(f=>typeof(Delegate).IsAssignableFrom(f.FieldType));
        void Posted(object? sender,System.Windows.Threading.DispatcherHookEventArgs e)
        {
            var method=(methodField?.GetValue(e.Operation) as Delegate)?.Method;
            string key=e.Operation.Priority+":"+method?.DeclaringType?.FullName+"."+method?.Name;
            operations.AddOrUpdate(key,1,(_,value)=>value+1);
        }
        Dispatcher.Hooks.OperationPosted+=Posted;
        try
        {
            foreach(int delay in new[]{3000,5000})
            {
                await Task.Delay(delay);if(!IsVisible)return;
                var system=systemMonitorWidget;
                object? Field(object obj,string name)=>obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(obj);
                var snapshot=new{operations=operations.ToArray(),cpuMs=System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds,allocated=GC.GetTotalAllocatedBytes(),fontScale,handlers=system==null?0:(Field(system,"ReadingsChanged") as Delegate)?.GetInvocationList().Length,build=typeof(MainWindow).Module.ModuleVersionId,viewport=new{ActualWidth,ActualHeight},dpi=VisualTreeHelper.GetDpi(this),activeDashboardLayoutTab,
                    system=system==null?null:new{system.IsLoaded,system.IsVisible,system.SensorsActive,system.SensorTimersRunning,shared=ReferenceEquals(system,SystemMonitorWidget.Shared),lastSampleTime=Field(system,"lastSampleTime"),latest=system.Latest.ToDictionary(p=>p.Key.ToString(),p=>p.Value),slots=system.ActiveSlots.Select(s=>new{kind=s.Kind.ToString(),s.IsShortcut,s.DisplayValue}).ToArray()},
                    tiles=RuntimeTraceDescendants(this).OfType<SensorTile>().Where(t=>t.IsVisible).Select(t=>new{t.Target,slot=t.Slot?.Kind.ToString(),serviceIsMain=ReferenceEquals(Field(t,"service"),system),text=RuntimeTraceDescendants(t).OfType<OpticallyCenteredText>().Where(v=>Equals(v.Tag,"SensorValue")).Select(v=>new{v.Text,v.ActualWidth,v.ActualHeight,v.IsMeasureValid,v.IsArrangeValid}).ToArray()}).ToArray(),
                    clocks=RuntimeTraceDescendants(this).OfType<OpticallyCenteredText>().Where(v=>Equals(v.Tag,"ClockText")||ReferenceEquals(v,quickLaunchBar.FindName("clockText"))).Select(v=>new{v.Text,v.IsVisible,v.IsLoaded}).ToArray(),
                    mini=RuntimeTraceDescendants(this).OfType<AirthingsMini>().Where(m=>m.IsVisible).Select(m=>new{m.ActualWidth,m.ActualHeight,active=m.IsActiveForProbe,m.RefreshCountForProbe,m.SizeChangesForProbe,values=RuntimeTraceDescendants(m).OfType<OpticallyCenteredText>().Select(t=>new{t.Tag,t.Text,t.ActualWidth,t.ActualHeight,t.IsMeasureValid,t.IsArrangeValid,ink=t.GetRenderedInkBounds()}).ToArray()}).ToArray()};
                InteractionDiagnostics.Log("RuntimeState "+JsonSerializer.Serialize(snapshot,new JsonSerializerOptions{NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals}));
            }
        }
        catch(Exception ex){InteractionDiagnostics.RecordFailure("Runtime state trace",ex);}
        finally{Dispatcher.Hooks.OperationPosted-=Posted;}
    }
}
