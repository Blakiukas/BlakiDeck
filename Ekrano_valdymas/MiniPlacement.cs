using System.Windows;
namespace Ekrano_valdymas;

// Plan the whole block before changing any cell. Occupants return to the vacated
// source block; library copies require empty space and never evict content.
internal static class MiniPlacement
{
    internal sealed record Cell(object Id,string Location,string? Target,string Name,string? Presentation,string? Icon,bool Blocked,bool Empty,Action<string?,string?,string?,string?> Write,Action Reserve);
    internal sealed record Board(object Owner,Cell[] Cells,int Columns,Action Save);
    private static Board? Get(object? owner)=>owner switch{QuickLaunchBar bar=>bar.MiniBoard(),ShortcutsWidget widget=>widget.MiniBoard(),_=>null};
    internal static bool Place(object owner,object destination,AirthingsMiniOptions options,object? sourceOwner=null,object? sourceSlot=null)
    {
        var target=Get(owner);if(target==null)return false;
        int drop=Array.FindIndex(target.Cells,c=>ReferenceEquals(c.Id,destination));if(drop<0)return false;
        var source=Get(sourceOwner);int old=source==null?-1:Array.FindIndex(source.Cells,c=>ReferenceEquals(c.Id,sourceSlot));
        if(sourceSlot!=null&&(source==null||old<0))return false;
        if(source!=null&&ReferenceEquals(source.Owner,target.Owner))source=target;
        var original=old>=0?source!.Cells.Skip(old).Take(AirthingsMini.Span(source.Cells[old].Target)).ToArray():[];
        bool Own(Cell c)=>original.Any(o=>ReferenceEquals(o.Id,c.Id));
        foreach(int start in options.Span==2?new[]{drop,drop-1}:new[]{drop}){
            if(start<0||start+options.Span>target.Cells.Length||start/target.Columns!=(start+options.Span-1)/target.Columns)continue;
            var block=target.Cells.Skip(start).Take(options.Span).ToArray();
            if(block.Any(c=>c.Blocked&&!Own(c)))continue;
            var displaced=block.Where(c=>!Own(c)&&!c.Empty).ToArray();
            var freed=original.Where(c=>block.All(b=>!ReferenceEquals(b.Id,c.Id))).ToArray();
            if(displaced.Length>freed.Length)continue;
            var affected=original.Concat(block).DistinctBy(c=>c.Id).ToArray();
            var preferences=new Dictionary<string,SystemMonitorWidget.MetricPresentation?>();
            foreach(var c in affected)preferences[c.Location]=null;
            for(int i=0;i<displaced.Length;i++)preferences[freed[i].Location]=SensorCellPreferences.Capture(displaced[i].Location,displaced[i].Target);
            // No normalization or save occurs until every cell has its final role.
            foreach(var c in affected)c.Write(null,null,null,null);
            for(int i=0;i<displaced.Length;i++){var c=displaced[i];freed[i].Write(c.Target,c.Name,c.Presentation,c.Icon);}
            block[0].Write(AirthingsMini.Encode(options),"Airthings",null,null);if(options.Span==2)block[1].Reserve();
            SensorCellPreferences.ApplyPlacement(preferences);
            target.Save();if(source!=null&&!ReferenceEquals(source.Owner,target.Owner))source.Save();return true;
        }
        return false;
    }
}
