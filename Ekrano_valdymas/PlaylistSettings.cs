using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Ekrano_valdymas;

internal sealed record PlaylistSettings
{
    public double? TitleSize {get;set;}
    public double RowGap {get;set;}=4;
    public string? TextColor {get;set;}
    public string Layout {get;set;}="Modern";
    internal double Size(bool wide)=>TitleSize is double v&&double.IsFinite(v)?Math.Clamp(v,12,32):wide?PlaylistAppearance.CapSize(16):17;
    internal double Gap=>double.IsFinite(RowGap)?Math.Clamp(RowGap,0,32):4;
}

public partial class MainWindow
{
    internal void SavePlaylistAppearance(PlaylistCard card,PlaylistSettings value)
    {
        if(card.IsEmbedded)selectedPlayerSettings.WidePlaylist=value;
        else selectedPlayerSettings.Playlist=value;
        card.ApplyAppearance(value);SaveWindowSettings();
    }
}
