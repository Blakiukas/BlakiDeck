using System.Windows;
using System.Windows.Controls;

namespace Ekrano_valdymas;

public partial class MainWindow
{
    private void LayoutTabCountMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menu && ReferenceEquals(e.Source, menu))
            quickLaunchBar.PopulateTabCountMenu(menu);
    }
}
