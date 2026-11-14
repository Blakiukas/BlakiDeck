using Microsoft.Web.WebView2.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace Ekrano_valdymas;

// The browser owns its profile. Never delete profile files or call the
// parameterless ClearBrowsingDataAsync overload for this command.
internal static class WebCacheMaintenance
{
    internal const CoreWebView2BrowsingDataKinds ClearedKinds = CoreWebView2BrowsingDataKinds.DiskCache;
    private static readonly SemaphoreSlim maintenance = new(1, 1);

    internal static async Task ClearDiskCacheAsync(Window owner, CoreWebView2? current = null)
    {
        await maintenance.WaitAsync();
        CoreWebView2Controller? temporary = null;
        try
        {
            if (current != null)
            {
                await current.Profile.ClearBrowsingDataAsync(ClearedKinds);
                return;
            }

            // An invisible native controller gives the command access to the
            // existing profile even with Web closed. It adds no WPF/shell window.
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: WebProfileLocation.Resolve());
            temporary = await environment.CreateCoreWebView2ControllerAsync(new WindowInteropHelper(owner).EnsureHandle());
            temporary.IsVisible = false;
            await temporary.CoreWebView2.Profile.ClearBrowsingDataAsync(ClearedKinds);
        }
        finally
        {
            try { temporary?.Close(); }
            finally { maintenance.Release(); }
        }
    }

    internal static MenuItem CreateCommand(Window owner, Func<Task> clear)
    {
        var command = new MenuItem
        {
            Header = "Clear Web cache",
            Icon = new TextBlock { Text = "\uE74D", FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"), FontSize = 16, VerticalAlignment = VerticalAlignment.Center },
            ToolTip = "Clear temporary website files. Keeps cookies, sign-ins, saved passwords and website settings."
        };
        // Inherit the menu's icon-aware item template. MiniMenuItemStyle renders
        // only Header and silently discards the Icon property.
        command.Click += async (_, _) =>
        {
            if (!command.IsEnabled) return;
            command.IsEnabled = false;
            try
            {
                await clear();
                AppMessage.Show(owner, "Temporary Web files cleared. Your sign-ins and website settings were kept.", "Web cache");
            }
            catch (Exception ex) { AppMessage.Show(owner, ex.Message, "Could not clear Web cache", MessageBoxButton.OK, MessageBoxImage.Warning); }
            finally { command.IsEnabled = true; }
        };
        return command;
    }
}
