using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.Windows;

namespace Ekrano_valdymas;

internal static class WebRuntimeSupport
{
    internal const string DownloadPage = "https://developer.microsoft.com/microsoft-edge/webview2/";

    internal static void CheckAvailable()
    {
        if (string.IsNullOrWhiteSpace(CoreWebView2Environment.GetAvailableBrowserVersionString()))
            throw new WebView2RuntimeNotFoundException();
    }

    internal static void OpenExternal(Window owner, Uri address)
    {
        if (address.Scheme is not ("http" or "https")) return;
        try { Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true })?.Dispose(); }
        catch (Exception)
        {
            AppMessage.Show(owner, LanguageManager.Text(
                "Windows could not open the default browser. Choose a default web browser in Windows Settings, or open this address manually:\n\n",
                "Windows nepavyko atidaryti numatytosios naršyklės. Pasirinkite ją Windows nustatymuose arba atidarykite šį adresą patys:\n\n") + address.AbsoluteUri,
                "Web browser", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    internal static void ShowInitializationFailure(Window owner, Exception exception, string stage)
    {
        if (exception is WebView2RuntimeNotFoundException)
        {
            var answer = AppMessage.Show(owner, LanguageManager.Text(
                "Microsoft Edge WebView2 Runtime is missing or could not be found. Install or repair the Evergreen Runtime (x64), then reopen Web in BlakiDeck. Your saved Web data will be kept.\n\nOpen Microsoft's download page in your browser?",
                "Microsoft Edge WebView2 Runtime nerastas. Įdiekite arba pataisykite Evergreen Runtime (x64), tada iš naujo atidarykite BlakiDeck Web. Išsaugoti naršyklės duomenys išliks.\n\nAtidaryti Microsoft atsisiuntimo puslapį naršyklėje?"),
                "WebView2 Runtime", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer == MessageBoxResult.Yes) OpenExternal(owner, new Uri(DownloadPage));
            return;
        }

        string message = exception is DllNotFoundException or BadImageFormatException
            ? LanguageManager.Text("BlakiDeck's browser component is missing or damaged. Repair or reinstall BlakiDeck, keeping your user settings.",
                "BlakiDeck naršyklės komponento trūksta arba jis pažeistas. Pataisykite arba iš naujo įdiekite BlakiDeck, išsaugodami nustatymus.")
            : stage == "profile" || exception is UnauthorizedAccessException
                ? LanguageManager.Text("BlakiDeck cannot open its saved browser data. Check that the profile folder is accessible and writable, then reopen Web. Your data has not been reset.",
                    "BlakiDeck negali atidaryti išsaugotų naršyklės duomenų. Patikrinkite profilio aplanko prieinamumą ir rašymo teises, tada iš naujo atidarykite Web. Duomenys neatkurti į numatytuosius.")
                : LanguageManager.Text("The embedded browser could not start. Close other BlakiDeck windows and try again. If this continues, repair Microsoft Edge WebView2 Runtime or reinstall BlakiDeck, keeping your settings.",
                    "Integruotos naršyklės paleisti nepavyko. Uždarykite kitus BlakiDeck langus ir bandykite dar kartą. Jei klaida kartojasi, pataisykite Microsoft Edge WebView2 Runtime arba iš naujo įdiekite BlakiDeck, išsaugodami nustatymus.");
        AppMessage.Show(owner, message + $"\n\nError: 0x{exception.HResult:X8}", "Web browser", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
