using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ekrano_valdymas;

public partial class QuickLaunchBar
{
    private List<object> AtomicMiniLayout()=>_layoutItems.Where(item=>item is not QuickLaunchSlot {IsAirthingsReserved:true}).ToList();
    private void ApplyAtomicMiniLayout(IEnumerable<object> next)
    {
        var reservations=new Dictionary<object,object>();
        for(int i=0;i+1<_layoutItems.Count;i++)if(_layoutItems[i] is QuickLaunchSlot {IsAirthingsMini:true} owner&&_layoutItems[i+1] is QuickLaunchSlot {IsAirthingsReserved:true} reserved)reservations[owner]=reserved;
        _layoutItems.Clear();foreach(var item in next){_layoutItems.Add(item);if(reservations.TryGetValue(item,out var reserved))_layoutItems.Add(reserved);}
    }
    internal MiniPlacement.Board MiniBoard()=>new(this,_layoutItems.Select(item=>item is QuickLaunchSlot slot
        ?new MiniPlacement.Cell(slot,"top:"+slot.Index,slot.Target,slot.DisplayName,slot.Presentation,slot.IconPath,slot.IsAirthingsMini||slot.IsAirthingsReserved,slot.IsEmpty,(t,n,p,i)=>{slot.SetTarget(t,n);slot.Presentation=p;slot.IconPath=i;},()=>slot.SetTarget(AirthingsMini.ReservedTarget,""))
        :new MiniPlacement.Cell(item,"",null,"",null,null,true,false,(_,_,_,_)=>{},()=>{})).ToArray(),int.MaxValue,()=>{NormalizeAirthingsMiniSpans();RebuildTopBarLayout();SaveShortcuts();});
    internal bool AddAirthingsMini(QuickLaunchSlot destination,AirthingsMiniOptions options)=>MiniPlacement.Place(this,destination,options,destination.IsAirthingsMini?this:null,destination.IsAirthingsMini?destination:null);
    private bool NormalizeAirthingsMiniSpans()
    {
        bool changed=false;var reservations=new HashSet<QuickLaunchSlot>();
        for(int i=0;i<_layoutItems.Count;i++)
            if(_layoutItems[i] is QuickLaunchSlot {IsAirthingsMini:true} slot&&AirthingsMini.Span(slot.Target)==2)
            {
                if(i+1<_layoutItems.Count&&_layoutItems[i+1] is QuickLaunchSlot next&&(next.IsEmpty||next.IsAirthingsReserved))
                {reservations.Add(next);if(!next.IsAirthingsReserved){next.SetTarget(AirthingsMini.ReservedTarget,"");changed=true;}}
                else{slot.SetTarget(AirthingsMini.Encode(AirthingsMini.Decode(slot.Target) with{Cells=1}),"Airthings");changed=true;}
            }
        foreach(var slot in _slots.Where(s=>s.IsAirthingsReserved&&!reservations.Contains(s))){slot.Clear();changed=true;}
        return changed;
    }
    private void EditAirthingsMini(QuickLaunchSlot slot)=>CreateAirthingsMiniEditor(slot).ShowDialog();
    internal SettingsWindow CreateAirthingsMiniEditor(QuickLaunchSlot slot)
    {
        string? target=slot.Target,presentation=slot.Presentation,icon=slot.IconPath;string name=slot.DisplayName;
        var original=AirthingsMini.Decode(target);
        int index=_layoutItems.IndexOf(slot);var next=index>=0&&index+1<_layoutItems.Count?_layoutItems[index+1] as QuickLaunchSlot:null;
        string? nextTarget=next?.Target,nextPresentation=next?.Presentation,nextIcon=next?.IconPath;string? nextName=next?.DisplayName;
        bool available=next!=null&&(next.IsEmpty||next.IsAirthingsReserved);
        void Reflow(){bool loaded=_settingsLoadCompleted;try{_settingsLoadCompleted=false;RebuildTopBarLayout();}finally{_settingsLoadCompleted=loaded;}}
        void Restore()
        {
            bool spanChanged=AirthingsMini.Span(slot.Target)!=original.Span;
            slot.SetTarget(target,name);slot.Presentation=presentation;slot.IconPath=icon;
            if(available&&next!=null){next.SetTarget(nextTarget,nextName);next.Presentation=nextPresentation;next.IconPath=nextIcon;}
            if(spanChanged)Reflow();
        }
        void Validate(AirthingsMiniOptions options){if(options.Span==2&&!available)throw new InvalidOperationException("Free the next shortcut cell before choosing two cells.");}
        void Apply(AirthingsMiniOptions options)
        {
            if(options==original){Restore();return;}
            Validate(options);bool spanChanged=AirthingsMini.Span(slot.Target)!=options.Span;
            slot.SetTarget(AirthingsMini.Encode(options),name);
            if(available&&next!=null){if(options.Span==2)next.SetTarget(AirthingsMini.ReservedTarget,"");else next.SetTarget(null,null);}
            if(spanChanged)Reflow();
        }
        return AirthingsMiniEditor.Create(Window.GetWindow(this),original,options=>{Validate(options);Restore();if(!AddAirthingsMini(slot,options))throw new InvalidOperationException("Free the next shortcut cell before choosing two cells.");},Apply);
    }
}

public partial class ShortcutsWidget
{
    internal MiniPlacement.Board MiniBoard()=>new(this,Slots.Select(slot=>new MiniPlacement.Cell(slot,SensorLocation(slot),slot.Target,slot.Name,slot.Presentation,slot.IconPath,slot.HasControl||slot.IsReserved||slot.IsAirthingsMini,slot.IsEmpty,(t,n,p,i)=>{slot.Clear();slot.SetTarget(t,n);slot.Presentation=p;slot.IconPath=i;},()=>slot.SetReserved("airthings"))).ToArray(),GridColumns,()=>{NormalizeAirthingsMiniSpans();SaveSlots();});
    internal bool AddAirthingsMini(Slot destination,AirthingsMiniOptions options)=>MiniPlacement.Place(this,destination,options,destination.IsAirthingsMini?this:null,destination.IsAirthingsMini?destination:null);
    private void NormalizeAirthingsMiniSpans()
    {
        var reservations=new HashSet<Slot>();
        for(int i=0;i<Slots.Count;i++)
            if(Slots[i].IsAirthingsMini&&AirthingsMini.Span(Slots[i].Target)==2)
            {
                if(ShortcutsGridPlanner.CanPlaceWideControl(i,Slots.Count,GridColumns)&&(Slots[i+1].IsEmpty||Slots[i+1].ReservedKey=="airthings"))
                {reservations.Add(Slots[i+1]);if(Slots[i+1].ReservedKey!="airthings")Slots[i+1].SetReserved("airthings");}
                else Slots[i].SetTarget(AirthingsMini.Encode(AirthingsMini.Decode(Slots[i].Target) with{Cells=1}),"Airthings");
            }
        foreach(var slot in Slots.Where(s=>s.ReservedKey=="airthings"&&!reservations.Contains(s)))slot.Clear();
        FindVisualDescendant<ShortcutsSpanPanel>(shortcutsItems,_=>true)?.InvalidateMeasure();
    }
    private void EditAirthingsMini(Slot slot)=>CreateAirthingsMiniEditor(slot).ShowDialog();
    internal SettingsWindow CreateAirthingsMiniEditor(Slot slot)
    {
        string? target=slot.Target,presentation=slot.Presentation,icon=slot.IconPath,name=slot.CustomName;
        var original=AirthingsMini.Decode(target);int index=Slots.IndexOf(slot);
        var next=index>=0&&index+1<Slots.Count?Slots[index+1]:null;
        bool available=next!=null&&ShortcutsGridPlanner.CanPlaceWideControl(index,Slots.Count,GridColumns)&&(next.IsEmpty||next.ReservedKey=="airthings");
        string? nextTarget=next?.Target,nextName=next?.CustomName,nextReserved=next?.ReservedKey,nextPresentation=next?.Presentation,nextIcon=next?.IconPath;
        void Reflow()=>FindVisualDescendant<ShortcutsSpanPanel>(shortcutsItems,_=>true)?.InvalidateMeasure();
        void Restore()
        {
            slot.SetTarget(target,name);slot.Presentation=presentation;slot.IconPath=icon;
            if(available&&next!=null){next.Clear();if(nextReserved!=null)next.SetReserved(nextReserved);else next.SetTarget(nextTarget,nextName);next.Presentation=nextPresentation;next.IconPath=nextIcon;}
            Reflow();
        }
        void Validate(AirthingsMiniOptions options){if(options.Span==2&&!available)throw new InvalidOperationException("Free the next cell in this row before choosing two cells.");}
        void Apply(AirthingsMiniOptions options)
        {
            if(options==original){Restore();return;}
            Validate(options);bool spanChanged=AirthingsMini.Span(slot.Target)!=options.Span;slot.SetTarget(AirthingsMini.Encode(options),name);
            if(spanChanged&&available&&next!=null){if(options.Span==2)next.SetReserved("airthings");else next.Clear();}
            if(spanChanged)Reflow();
        }
        return AirthingsMiniEditor.Create(Window.GetWindow(this),original,options=>{Validate(options);Restore();if(!AddAirthingsMini(slot,options))throw new InvalidOperationException("Free the next cell in this row before choosing two cells.");},Apply);
    }
}
