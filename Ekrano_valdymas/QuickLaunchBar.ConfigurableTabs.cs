using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Ekrano_valdymas;

public partial class QuickLaunchBar
{
    internal const int MaximumLayoutTabs = 11;
    private int _visibleTabCount = 4;
    private bool _normalPoolExpanded, _suspendTopBarRebuild;
    private string _configurableTabsFile = "";
    private ConfigurableTopBarState? _loadedNormalPool;
    private int TabCount => _visibleTabCount;
    internal int VisibleLayoutTabCount => TabCount;
    private int NormalBackingUnitCapacity => _normalPoolExpanded ? MaximumLayoutTabs - 1 : SlotCount;
    private int HiddenNormalUnits => _normalPoolExpanded ? TabCount - 1 : 0;

    // Entries and their order form one atomic document. Changing the tab count
    // projects this shared pool; it never removes an obscured cell or its payload.
    private sealed class ConfigurableTopBarState
    {
        public int Version { get; set; } = 1;
        public int TabCount { get; set; } = 4;
        public int ClockSpan { get; set; } = 2;
        public bool FactoryLayout { get; set; }
        public QuickLaunchEntry?[] Entries { get; set; } = [];
        public string[] Order { get; set; } = [];
        public QuickLaunchEntry?[] Overflow { get; set; } = [];
    }

    private void LoadConfigurableTabs(string folder)
    {
        _visibleTabCount = InstallationDefaults.IsNewInstall ? 4 : 5;
        _configurableTabsFile = Path.Combine(folder, "top-bar-tabs-v1.json");
        foreach (string path in new[] { _configurableTabsFile, _configurableTabsFile + ".previous" })
        {
            if (!File.Exists(path)) continue;
            try
            {
                var state = JsonSerializer.Deserialize<ConfigurableTopBarState>(File.ReadAllText(path));
                if (state == null || state.Version != 1 || state.TabCount is < 1 or > MaximumLayoutTabs ||
                    state.Entries == null || state.Order == null || state.Overflow == null ||
                    state.Order.Distinct().Count() != state.Order.Length ||
                    state.Order.Any(token => !ValidLayoutToken(token, state.Entries.Length)))
                    throw new InvalidDataException("Invalid configurable top-bar state.");
                var indices = state.Order.Where(t => t.StartsWith("slot:", StringComparison.Ordinal))
                    .Select(t => int.Parse(t[5..])).Order().ToArray();
                if (!indices.SequenceEqual(Enumerable.Range(0, state.Entries.Length)))
                    throw new InvalidDataException("Incomplete normal-cell order.");
                if (state.ClockSpan is < 1 or > 2)
                    throw new InvalidDataException("Invalid normal-pool clock span.");
                int units = state.Order.Sum(token => token.StartsWith("slot:", StringComparison.Ordinal)
                    ? state.Entries[int.Parse(token[5..])]?.Target is string target && target == AirthingsMini.ReservedTarget
                        ? 0 : AirthingsMini.Span(state.Entries[int.Parse(token[5..])]?.Target)
                    : token == "clock" ? state.ClockSpan : 1);
                if (units != MaximumLayoutTabs - 1)
                    throw new InvalidDataException("Normal-cell capacity must be ten units.");
                _loadedNormalPool = state;
                _visibleTabCount = state.TabCount;
                _normalPoolExpanded = true;
                break;
            }
            catch (Exception ex) { InteractionDiagnostics.RecordFailure("Configurable top bar load", ex); }
        }
    }

    private static bool ValidLayoutToken(string token, int count) =>
        token is "clock" or "sleep" or "shutdown" or "restart" or "obs-record" or "obs-buffer" ||
        token != null && token.StartsWith("slot:", StringComparison.Ordinal) &&
        int.TryParse(token[5..], out int index) && index >= 0 && index < count;

    private bool LoadNormalPoolEntries()
    {
        if (_loadedNormalPool is not { } state) return false;
        for (int i = 0; i < state.Entries.Length; i++)
        {
            var entry = state.Entries[i];
            _slots.Add(new QuickLaunchSlot(i, entry?.Target, entry?.DisplayName)
            { IconPath = entry?.IconPath, Presentation = entry?.Presentation });
        }
        foreach (var entry in state.Overflow) _savedOverflow.Enqueue(entry);
        return true;
    }

    private void RestoreNormalPoolClockSpan()
    {
        if (_loadedNormalPool is { } state && state.Order.Contains("clock"))
        {
            // Cell ownership and span are committed together. A separately
            // interrupted clock-style save cannot invalidate the normal pool.
            clockAppearance.Cells = state.ClockSpan;
            clockAppearance.Apply(clockText);
        }
    }

    private bool LoadNormalPoolOrder()
    {
        if (_loadedNormalPool is not { } state) return false;
        foreach (string token in state.Order)
            _layoutItems.Add(token.StartsWith("slot:", StringComparison.Ordinal)
                ? _slots[int.Parse(token[5..])] : token);
        return true;
    }

    private void ExpandNormalPool()
    {
        if (_normalPoolExpanded) return;
        _suspendTopBarRebuild = true;
        try
        {
            // The original six ordinary units stay in their physical positions.
            // Former tabs 2–5 acquire independent empty ordinary cells.
            var prefix = new List<object>();
            for (int i = 0; i < MaximumLayoutTabs - 1 - SlotCount; i++)
            {
                var slot = new QuickLaunchSlot(_slots.Count, null, null) { ShowName = _showShortcutNames };
                _slots.Add(slot);
                prefix.Add(slot);
            }
            _layoutItems.InsertRange(0, prefix);
            _normalPoolExpanded = true;
        }
        finally { _suspendTopBarRebuild = false; }
    }

    private void SaveNormalPool()
    {
        if (!_settingsLoadCompleted || !_normalPoolExpanded || _suspendTopBarRebuild) return;
        AtomicJson.Write(_configurableTabsFile, new ConfigurableTopBarState
        {
            TabCount = TabCount,
            ClockSpan = clockAppearance.Span,
            Entries = _slots.Select(slot => slot.IsEmpty ? null :
                new QuickLaunchEntry(slot.Target!, slot.SavedDisplayName, slot.Presentation, slot.IconPath)).ToArray(),
            Order = _layoutItems.Select(item => item is QuickLaunchSlot slot ?
                $"slot:{_slots.IndexOf(slot)}" : (string)item).ToArray(),
            Overflow = _savedOverflow.ToArray()
        });
    }

    internal bool TrySetLayoutTabCount(int count, out string message)
    {
        message = "";
        if (count is < 1 or > MaximumLayoutTabs) { message = "Choose between 1 and 11 tabs."; return false; }
        if (count == TabCount) return true;
        if (!_settingsLoadCompleted) { message = "Settings are still loading."; return false; }
        int previous = _visibleTabCount;
        // Persist before replacing the rendered projection. A failed write leaves
        // both the active dashboard and the visible count unchanged.
        try { _visibleTabCount = count; SaveNormalPool(); }
        catch (Exception ex)
        {
            _visibleTabCount = previous;
            InteractionDiagnostics.RecordFailure("Tab count save", ex);
            message = "Could not save the tab count."; return false;
        }
        CancelShortcutTabHover();
        tabHoldTimer.Stop(); heldTab = null;
        _pressedTab?.ReleaseMouseCapture(); _pressedTab = null;
        _tabTouchDevice?.Capture(null); _tabTouchDevice = null;
        _topBarControlDragSource?.ReleaseMouseCapture();
        _topBarControlTouchDevice?.Capture(null); _topBarControlTouchDevice = null;
        ResetTopBarControlDragVisuals(); _topBarControlDragKey = null;
        bool switched = _activeTab >= count;
        if (switched) _activeTab = count - 1;
        RebuildTopBarLayout();
        if (switched) { LayoutTabChanged?.Invoke(_activeTab); SaveActiveTab(); }
        return true;
    }

    internal void PopulateTabCountMenu(MenuItem menu)
    {
        var existing = menu.Items.OfType<MenuItem>().ToArray();
        if (existing.Length == MaximumLayoutTabs - 1 && menu.Items.Count == existing.Length &&
            existing.Select(item => item.Tag).SequenceEqual(Enumerable.Range(2, MaximumLayoutTabs - 1).Select(n => (object)n)))
        {
            foreach (var item in existing) item.IsChecked = (int)item.Tag == TabCount;
            return;
        }
        menu.Items.Clear();
        menu.ToolTip = "Tab 1 is always present. Uncheck the selected count to leave only Tab 1.";
        for (int count = 2; count <= MaximumLayoutTabs; count++)
        {
            var item = new MenuItem { Header = count.ToString(), Tag = count, IsCheckable = true,
                IsChecked = TabCount == count, StaysOpenOnClick = true };
            item.SetResourceReference(StyleProperty, "MiniMenuItemStyle");
            item.Padding=new Thickness(13,0,13,0);
            item.Margin=new Thickness(0);
            item.SetBinding(MinHeightProperty,new System.Windows.Data.Binding(nameof(MenuItem.ActualHeight)){Source=menu});
            var label = new OpticallyCenteredText { Text = count.ToString(), CompactLineBox=true, TextBrush = System.Windows.Media.Brushes.White };
            label.SetBinding(OpticallyCenteredText.TextFontSizeProperty, new System.Windows.Data.Binding(nameof(MenuItem.FontSize)) { Source = item });
            label.SetBinding(OpticallyCenteredText.TextFontFamilyProperty, new System.Windows.Data.Binding(nameof(MenuItem.FontFamily)) { Source = item });
            label.SetBinding(OpticallyCenteredText.TextFontWeightProperty, new System.Windows.Data.Binding(nameof(MenuItem.FontWeight)) { Source = item });
            var indicator = new CheckBox { Content = label, IsHitTestVisible = false, Focusable = false };
            indicator.SetResourceReference(StyleProperty, "DashboardCheckBox");
            indicator.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
                new System.Windows.Data.Binding(nameof(MenuItem.IsChecked)) { Source = item, Mode = System.Windows.Data.BindingMode.OneWay });
            item.Header = indicator;
            System.Windows.Automation.AutomationProperties.SetName(item, count.ToString());
            item.Click += (_, _) =>
            {
                if (!TrySetLayoutTabCount(item.IsChecked ? (int)item.Tag : 1, out string message))
                    MessageBox.Show(Window.GetWindow(this), message, "Tabs", MessageBoxButton.OK, MessageBoxImage.Information);
                foreach (MenuItem choice in menu.Items) choice.IsChecked = (int)choice.Tag == TabCount;
            };
            menu.Items.Add(item);
        }
    }
}
