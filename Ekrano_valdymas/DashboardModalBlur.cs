using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.Effects;

namespace Ekrano_valdymas;

// Several owned confirmations may overlap. Restore the prior dashboard effect
// only after the last confirmation closes, including cancellation and failures.
internal static class DashboardModalBlur
{
    private sealed class State
    {
        internal int Count;
        internal Effect? Previous;
        internal readonly BlurEffect Blur=new(){Radius=6};
    }
    private static readonly ConditionalWeakTable<UIElement,State> states=new();
    internal static IDisposable Acquire(Window? owner)
    {
        Window? dashboard=owner;
        while(dashboard!=null&&dashboard is not MainWindow)dashboard=dashboard.Owner;
        dashboard??=Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
        if(dashboard?.Content is not UIElement content)return new Lease(null,null);
        var state=states.GetValue(content,_=>new State());
        if(state.Count++==0){state.Previous=content.Effect;content.SetCurrentValue(UIElement.EffectProperty,state.Blur);}
        return new Lease(content,state);
    }
    private sealed class Lease(UIElement? content,State? state):IDisposable
    {
        private bool disposed;
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            if(content==null||state==null||--state.Count>0)return;
            if(ReferenceEquals(content.Effect,state.Blur))content.SetCurrentValue(UIElement.EffectProperty,state.Previous);
            state.Previous=null;
        }
    }
}
