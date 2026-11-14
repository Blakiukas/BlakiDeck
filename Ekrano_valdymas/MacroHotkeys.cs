using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Ekrano_valdymas;

// One registration per macro, independent of how many dashboard shortcuts use it.
internal sealed class MacroHotkeys : IDisposable
{
    internal static MacroHotkeys? Current { get; private set; }
    private readonly Window owner;
    private readonly HwndSource source;
    private readonly Dictionary<int,(Guid Id,string Keys)> registered=new();
    private readonly Dictionary<Guid,string> errors=new();
    private int revision=-1;
    private bool pending,disposed;
    [DllImport("user32.dll",SetLastError=true)] private static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd,int id);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);

    internal MacroHotkeys(Window owner,HwndSource source)
    {
        this.owner=owner;this.source=source;Current=this;
        source.AddHook(Hook);MacroLibrary.Current.Changed+=Changed;Refresh();
    }
    internal static string Normalize(string? value)
    {
        if(string.IsNullOrWhiteSpace(value))return "";
        var keys=MacroKeys.Parse(value);
        var main=keys.Where(k=>k is not (0x10 or 0x11 or 0x12 or 0x5b)).ToArray();
        if(main.Length!=1||keys.Distinct().Count()!=keys.Length)throw new ArgumentException("Choose one key with Ctrl, Alt, Shift or Win, or a function key.");
        if(main[0]==0x7b)throw new ArgumentException("F12 is reserved by Windows. Choose another key.");
        if(keys.Length==1&&(main[0]<0x70||main[0]>0x87))throw new ArgumentException("Use Ctrl, Alt, Shift or Win with this key.");
        return string.Concat(keys.Contains((byte)0x11)?"Ctrl+":"",keys.Contains((byte)0x12)?"Alt+":"",keys.Contains((byte)0x10)?"Shift+":"",keys.Contains((byte)0x5b)?"Win+":"")+KeyInterop.KeyFromVirtualKey(main[0]);
    }
    private bool Register(int id,string value)
    {
        var keys=MacroKeys.Parse(value);uint mods=0x4000; // MOD_NOREPEAT
        foreach(var key in keys)mods|=key switch{0x12=>1u,0x11=>2u,0x10=>4u,0x5b=>8u,_=>0u};
        return RegisterHotKey(source.Handle,id,mods,keys.Last(k=>k is not (0x10 or 0x11 or 0x12 or 0x5b)));
    }
    internal string Error(Guid id)=>errors.GetValueOrDefault(id,"");
    internal void CheckAvailable(MacroDefinition macro)
    {
        if(macro.Hotkey.Length==0||registered.Values.Any(r=>r.Id==macro.Id&&r.Keys==macro.Hotkey))return;
        const int probe=0x6fff;
        if(!Register(probe,macro.Hotkey))throw new ArgumentException("These keys are unavailable or used by another application. Choose another combination.");
        UnregisterHotKey(source.Handle,probe);
    }
    private void Changed(object? sender,EventArgs e){if(revision!=MacroLibrary.Current.Revision)Refresh();}
    private void Refresh()
    {
        foreach(int id in registered.Keys)UnregisterHotKey(source.Handle,id);
        registered.Clear();errors.Clear();revision=MacroLibrary.Current.Revision;int next=0x6000;
        foreach(var macro in MacroLibrary.Current.Items)
        {
            try
            {
                string keys=Normalize(macro.Hotkey);if(keys.Length==0)continue;
                int id=next++;
                if(Register(id,keys))registered[id]=(macro.Id,keys);
                else errors[macro.Id]="Shortcut unavailable: another application may be using these keys.";
            }
            catch(Exception ex){errors[macro.Id]=ex.Message;}
        }
    }
    private IntPtr Hook(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(message!=0x312||!registered.TryGetValue(wParam.ToInt32(),out var binding))return IntPtr.Zero;
        handled=true;
        if(!pending&&SettingsWindow.Sessions.Count==0&&!MacroRunner.Current.IsRunning)_=Activate(binding);
        return IntPtr.Zero;
    }
    private async Task Activate((Guid Id,string Keys) binding)
    {
        pending=true;
        try
        {
            // Do not send a macro's Keys action while its trigger modifiers are held.
            var keys=MacroKeys.Parse(binding.Keys);int waits=0;
            while(keys.Any(k=>(GetAsyncKeyState(k)&0x8000)!=0)&&waits++<500)await Task.Delay(20);
            if(!disposed&&waits<500&&SettingsWindow.Sessions.Count==0&&!MacroRunner.Current.IsRunning&&registered.Values.Contains(binding))
                MacroRunner.TryActivate(MacroLibrary.Prefix+binding.Id,owner);
        }
        finally{pending=false;}
    }
    public void Dispose()
    {
        disposed=true;MacroLibrary.Current.Changed-=Changed;
        foreach(int id in registered.Keys)UnregisterHotKey(source.Handle,id);
        registered.Clear();source.RemoveHook(Hook);if(ReferenceEquals(Current,this))Current=null;
    }
}
