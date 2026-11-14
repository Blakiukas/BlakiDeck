using System.Windows.Media;

namespace Ekrano_valdymas;

public partial class MainWindow
{
    private MusicService? hudVisualizerIconService;
    private ImageSource? hudVisualizerSourceIcon;

    private void InitializeHudVisualizerMedia()
    {
        // Reuse the player's one metadata stream and bounded source-icon decoder.
        // The embedded visualizer never starts another media or image poller.
        SpotifyWidget.AlbumArtChanged += HudVisualizerAlbumChanged;
        Closed += (_, _) => SpotifyWidget.AlbumArtChanged -= HudVisualizerAlbumChanged;
    }

    private void HudVisualizerAlbumChanged(object? sender, ImageSource? image)
    {
        if (Dispatcher.CheckAccess()) UpdateHudVisualizerMedia();
        else Dispatcher.BeginInvoke(UpdateHudVisualizerMedia);
    }

    private HudVisualizerCard CreateHudVisualizer()
    {
        var card = new HudVisualizerCard(() => spotifyWidget.IsPlaying);
        card.Loaded += (_, _) => UpdateHudVisualizerMedia();
        SetHudVisualizerMedia(card);
        return card;
    }

    private void UpdateHudVisualizerMedia()
    {
        if (hudVisualizerCard != null) SetHudVisualizerMedia(hudVisualizerCard);
    }

    private void SetHudVisualizerMedia(HudVisualizerCard card)
    {
        var service = spotifyWidget.SelectedService;
        if (hudVisualizerIconService != service)
        {
            hudVisualizerIconService = service;
            string? file = DefaultIconCatalog.MusicSourceFileName(service);
            hudVisualizerSourceIcon = file == null ? null : DefaultIconCatalog.GetImage(file);
        }
        card.SetMediaContext(hudVisualizerSourceIcon, SpotifyWidget.CurrentAlbumArt,
            service == MusicService.Local ? "Local player" : service.ToString());
    }
}
