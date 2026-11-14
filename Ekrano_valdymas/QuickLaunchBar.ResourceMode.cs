using System.Diagnostics;
using System.Globalization;
using System.Windows.Media;
namespace Ekrano_valdymas;
public partial class QuickLaunchBar
{
    private readonly Dictionary<System.Windows.Controls.Primitives.Popup,IDisposable> confirmationBlurs=new();
    private void AttachConfirmationBlur()
    {
        foreach(var popup in new[]{sleepConfirmationPopup,shutdownConfirmationPopup,restartConfirmationPopup})
        {
            popup.Opened+=(_,_)=>{if(confirmationBlurs.Remove(popup,out var old))old.Dispose();confirmationBlurs[popup]=DashboardModalBlur.Acquire(System.Windows.Window.GetWindow(this));};
            popup.Closed+=(_,_)=>{if(confirmationBlurs.Remove(popup,out var lease))lease.Dispose();};
        }
        Unloaded+=(_,_)=>{foreach(var lease in confirmationBlurs.Values)lease.Dispose();confirmationBlurs.Clear();};
    }
    private bool gameModeIndicator,compactIconState;
    private TimeSpan? gameElapsed;
    private long gameElapsedTimestamp;
    private bool gameElapsedRunning=true;
    internal void SetGameElapsedRunning(bool running){gameElapsedRunning=running;}
    internal void SetGameModeIndicator(bool enabled,ImageSource? gameIcon=null,TimeSpan? elapsed=null)
    {
        compactGameIcon.Source=enabled?gameIcon:null;
        gameModeIndicator=enabled;SetGameElapsed(enabled?elapsed:null);RefreshResourceModeIcon();RefreshMilitarySticker();
    }
    internal void SetGameElapsed(TimeSpan? elapsed)
    {
        gameElapsed=elapsed.HasValue&&elapsed.Value<TimeSpan.Zero?TimeSpan.Zero:elapsed;
        gameElapsedTimestamp=Stopwatch.GetTimestamp();
        RefreshGameElapsed();
    }
    private void RefreshGameElapsed()
    {
        bool visible=gameModeIndicator&&compactGameIcon.Source!=null&&gameElapsed.HasValue;
        compactGameElapsed.Visibility=visible?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;
        string next=visible?FormatGameElapsed(gameElapsed!.Value+(gameElapsedRunning?Stopwatch.GetElapsedTime(gameElapsedTimestamp):TimeSpan.Zero)):string.Empty;
        if(compactGameElapsed.Text!=next)compactGameElapsed.Text=next;
    }
    internal static string FormatGameElapsed(TimeSpan elapsed)
    {
        long seconds=Math.Max(0,elapsed.Ticks/TimeSpan.TicksPerSecond);
        return seconds<3600
            ?string.Create(CultureInfo.InvariantCulture,$"{seconds/60:00}:{seconds%60:00}")
            :string.Create(CultureInfo.InvariantCulture,$"{seconds/3600}:{seconds/60%60:00}:{seconds%60:00}");
    }
    private void RefreshResourceModeIcon()
    {
        bool hasApplicationIcon=gameModeIndicator&&compactGameIcon.Source!=null;
        compactGameIndicator.Visibility=hasApplicationIcon?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;
        compactGameIcon.Visibility=hasApplicationIcon?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;
        compactToggleIcon.Visibility=hasApplicationIcon?System.Windows.Visibility.Collapsed:System.Windows.Visibility.Visible;
        compactToggleIcon.Data=Geometry.Parse(compactIconState?"M 4,15 L 12,7 L 20,15":"M 4,7 L 12,15 L 20,7");
    }
}
