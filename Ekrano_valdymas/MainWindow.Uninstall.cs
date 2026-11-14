using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Ekrano_valdymas;

public partial class MainWindow
{
    private void UninstallMenuItem_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DiagnosticState.Root != null) return;
        try
        {
            if (PackageIdentity.IsPackagedProcess())
            {
                // Windows owns package removal and presents its confirmation.
                using var settings = Process.Start(new ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true });
                return;
            }
            // Use only this installation's uninstaller; never target another copy.
            string path = Path.Combine(AppContext.BaseDirectory, "Uninstall.exe");
            if (!File.Exists(path))
            {
                MessageBox.Show(this, "This copy has no installer. To remove a portable copy, close it and delete its program folder.", "Uninstall", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            // No silent flag: the wizard asks for confirmation before removal.
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            if (process != null) Close();
        }
        catch (Exception error)
        {
            InteractionDiagnostics.RecordFailure("Open uninstaller", error);
            MessageBox.Show(this, "Could not open uninstall options. You can also remove BlakiDeck in Windows Settings > Apps.", "Uninstall", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
