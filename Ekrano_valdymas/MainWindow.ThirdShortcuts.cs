namespace Ekrano_valdymas;

public partial class MainWindow
{
    private ShortcutsWidget? shortcutsWidget3;

    // Use the same per-tab lazy cache as the first two regular modules. No
    // hidden tab creates a widget, and the wide family remains independent.
    private ShortcutsWidget GetThirdShortcuts() =>
        shortcutsWidget3 = GetTabShortcuts(TabShortcutStorage.ThirdKind);

    private void ApplyThirdShortcutNameVisibility()
    {
        if (shortcutsWidget3 != null)
            shortcutsWidget3.ShowShortcutNames = showShortcutNames;
    }
}
