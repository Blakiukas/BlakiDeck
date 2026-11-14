using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Ekrano_valdymas;

public partial class QuickLaunchBar
{
    private ClockAppearance clockAppearance=new(){Size=82.67};
    private string clockAppearanceFile="";
    private string defaultClockStyle="original";
    internal event Action<string,bool>? ClockStyleSaved;
    internal void RestoreTabClockStyle(string? style)
    {
        clockAppearance.MaterialStyle=IconAppearance.Normalize(style??defaultClockStyle);
        clockAppearance.Apply(clockText);
    }
    internal ClockAppearance CurrentClockAppearance=>clockAppearance.Clone();
    internal void SetClockAppearance(ClockAppearance appearance)
    {
        if(ShowClock&&appearance.Span>clockAppearance.Span&&!_slots.Any(s=>s.IsEmpty))throw new InvalidOperationException("Free one shortcut cell before choosing a two-cell clock.");
        var shared=appearance.Clone();shared.MaterialStyle=defaultClockStyle;
        AtomicJson.Write(clockAppearanceFile,shared);clockAppearance=appearance.Clone();clockAppearance.Apply(clockText);
        NormalizeShortcutSlotCount();RebuildTopBarLayout();SaveShortcuts();
        ClockStyleSaved?.Invoke(clockAppearance.EffectiveMaterial,false);
    }
    internal void SetDefaultClockStyle(string style)
    {
        defaultClockStyle=IconAppearance.Normalize(style);
        var shared=clockAppearance.Clone();shared.MaterialStyle=defaultClockStyle;
        AtomicJson.Write(clockAppearanceFile,shared);
        RestoreTabClockStyle(defaultClockStyle);
        ClockStyleSaved?.Invoke(defaultClockStyle,true);
    }
    private void InitializeClockAppearance(string folder)
    {
        clockAppearanceFile=Path.Combine(folder,"top-bar-clock-appearance.json");
        try{if(File.Exists(clockAppearanceFile))clockAppearance=JsonSerializer.Deserialize<ClockAppearance>(File.ReadAllText(clockAppearanceFile))??new(){Size=82.67};}catch(Exception ex){InteractionDiagnostics.RecordFailure("Top bar clock appearance",ex);}
        defaultClockStyle=clockAppearance.EffectiveMaterial;clockAppearance.Apply(clockText);
        Loaded+=(_,_)=>IconAppearance.Changed+=RefreshClockSkin;
        Unloaded+=(_,_)=>IconAppearance.Changed-=RefreshClockSkin;
        var menu=new ContextMenu{Tag="ClockMenu",PlacementTarget=clockButton,Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom};menu.SetResourceReference(StyleProperty,"MiniContextMenuStyle");clockButton.ContextMenu=menu;
        var edit=new MenuItem{Header="Edit"};edit.SetResourceReference(StyleProperty,"MiniMenuItemStyle");menu.Items.Add(edit);
        edit.Click+=(_,_)=>ClockEditor.Create(Window.GetWindow(this),clockAppearance.Clone(),p=>p.Apply(clockText),SetClockAppearance,82.67,SetDefaultClockStyle).ShowDialog();
        var close=new MenuItem{Header="Close"};close.SetResourceReference(StyleProperty,"MiniMenuItemStyle");menu.Items.Add(close);
        close.Click+=(_,_)=>TrySetShowClock(false,out _);
    }
    private void RefreshClockSkin()=>clockText.InvalidateVisual();
}
