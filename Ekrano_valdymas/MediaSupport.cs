using System.IO;

namespace Ekrano_valdymas;

internal static class MediaSupport
{
    internal static bool WindowsMediaComponentsPresent =>
        File.Exists(Path.Combine(Environment.SystemDirectory, "mfplat.dll")) &&
        File.Exists(Path.Combine(Environment.SystemDirectory, "wmvcore.dll"));

    internal static string MissingWindowsMediaMessage => LanguageManager.Text(
        "Windows media components are unavailable. On Windows N, install Media Feature Pack in Settings → Apps → Optional features, then restart Windows. Streaming players remain independent of local playback.",
        "Neprieinami Windows medijos komponentai. Windows N sistemoje įdiekite Media Feature Pack: Parametrai → Programos → Pasirinktinės funkcijos, tada paleiskite Windows iš naujo. Srautiniai grotuvai veikia nepriklausomai nuo vietinio atkūrimo.");

    internal static string PlaybackFailure(string? path, bool windowsMediaAvailable) =>
        !windowsMediaAvailable ? MissingWindowsMediaMessage : LanguageManager.Text(
            $"Windows could not play '{Path.GetFileName(path) ?? "this file"}'. The file may be damaged or use a format or codec unavailable on this PC. Try another audio file.",
            $"Windows nepavyko atkurti „{Path.GetFileName(path) ?? "šio failo"}“. Failas gali būti sugadintas arba jo formatas ar kodekas neprieinamas šiame kompiuteryje. Pabandykite kitą garso failą.");

    internal static bool IsCaptureUnsupported(Exception error) => error is PlatformNotSupportedException ||
        error.HResult is unchecked((int)0x80004001) or unchecked((int)0x80004002) or unchecked((int)0x80070032);

    internal static string CaptureFailure(Exception error) => IsCaptureUnsupported(error)
        ? LanguageManager.Text(
            "VU unavailable: this Windows version does not provide compatible per-player audio capture. Update Windows. Music playback is unaffected.",
            "VU neprieinamas: ši Windows versija nepalaiko suderinamo atskiro grotuvo garso nuskaitymo. Atnaujinkite Windows. Muzikos atkūrimas nenutraukiamas.")
        : LanguageManager.Text(
            "VU unavailable: Windows could not capture this player's audio. Check its output device and audio driver. BlakiDeck will retry; music playback is unaffected.",
            "VU neprieinamas: Windows nepavyko nuskaityti šio grotuvo garso. Patikrinkite jo išvesties įrenginį ir garso tvarkyklę. BlakiDeck bandys dar kartą; muzikos atkūrimas nenutraukiamas.");

    internal static string MissingPlayerMessage(string name,bool running) => running
        ? LanguageManager.Text(
            $"{name} is running but has not exposed a compatible Windows media session. Start a track in {name}, then try again. Browser playback may not identify itself as this player; use its desktop app when controls are unavailable.",
            $"{name} veikia, bet nepateikia suderinamos Windows medijos sesijos. Paleiskite kūrinį programoje {name} ir bandykite dar kartą. Naršyklės atkūrimas gali būti neatpažįstamas kaip šis grotuvas; jei valdymas neveikia, naudokite jo darbalaukio programą.")
        : LanguageManager.Text(
            $"BlakiDeck could not find or start {name}. Install the player or open it yourself, start a track, then try again.",
            $"BlakiDeck nepavyko rasti arba paleisti {name}. Įdiekite grotuvą arba atidarykite jį patys, paleiskite kūrinį ir bandykite dar kartą.");
}
