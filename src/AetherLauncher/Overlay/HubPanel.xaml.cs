using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AetherLauncher.Overlay;

/// <summary>
/// Das Menü des Ingame-Overlays: welche Platten offen sind, welches Kürzel sie aufmacht und
/// ob das Overlay vor anderen Programmen zurückweicht.
/// </summary>
public partial class HubPanel : UserControl
{
    private OverlayHost? _host;
    private bool _loading;

    public HubPanel() => InitializeComponent();

    internal void Attach(OverlayHost host)
    {
        _host = host;

        BuildPanelRows();
        _loading = true;
        try
        {
            NoOverlapSwitch.IsChecked = host.HideOverOtherApps;
            RefreshOpacity();
        }
        finally
        {
            _loading = false;
        }

        RefreshKey();

        host.ShortcutChanged += RefreshKey;
        host.SettingsChanged += RefreshPanelRows;
    }

    private void BuildPanelRows()
    {
        if (_host is null) return;

        PanelRows.Children.Clear();

        foreach (var choice in _host.PanelChoices)
        {
            var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 6) };
            var id = choice.Id;

            var toggle = new CheckBox
            {
                Style = (Style)FindResource("Switch"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                IsChecked = _host.IsPanelVisible(id),
                Tag = id,
            };

            DockPanel.SetDock(toggle, Dock.Right);
            toggle.Checked += (_, _) =>
            {
                if (_loading) return;
                _host.SetPanelVisible(id, true);
            };
            toggle.Unchecked += (_, _) =>
            {
                if (_loading) return;
                _host.SetPanelVisible(id, false);
            };

            var label = new TextBlock
            {
                Text = choice.Title,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("TextMidBrush"),
            };

            row.Children.Add(toggle);
            row.Children.Add(label);
            PanelRows.Children.Add(row);
        }
    }

    private void RefreshOpacity()
    {
        if (_host is null) return;

        foreach (var chip in OpacityChips.Children.OfType<RadioButton>())
            chip.IsChecked = chip.Tag is string raw && int.TryParse(raw, out var percent) && percent == _host.PanelOpacity;
    }

    private void Opacity_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || _host is null || sender is not RadioButton { Tag: string raw }) return;
        if (!int.TryParse(raw, out var percent)) return;

        _host.SetPanelOpacity(percent);
        PanelUi.Say(StatusLine, $"Panels are now {percent}% opaque.");
    }

    private void ResetLayout_Click(object sender, RoutedEventArgs e)
    {
        if (_host is null) return;

        _host.ResetAllSpots();
        PanelUi.Say(StatusLine, "Every panel is back in its starting place.");
    }

    private void RefreshPanelRows()
    {
        if (_host is null) return;

        _loading = true;
        try
        {
            foreach (var toggle in PanelRows.Children.OfType<DockPanel>()
                         .SelectMany(row => row.Children.OfType<CheckBox>()))
            {
                if (toggle.Tag is string id) toggle.IsChecked = _host.IsPanelVisible(id);
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private void RefreshKey()
    {
        if (_host is null) return;

        KeyBadge.Text = _host.ShortcutText;
        KeyLine.Text = $"Press {_host.ShortcutText} over Roblox to open everything, again to hide it.";
    }

    private void ChangeKey_Click(object sender, RoutedEventArgs e)
    {
        if (_host is null) return;

        ChangeKeyButton.IsEnabled = false;
        KeyLine.Text = "Press the new combination now - Esc cancels.";

        _host.BeginCapture(KeyCaptureKind.Hotkey,
            text =>
            {
                ChangeKeyButton.IsEnabled = true;

                if (text is null)
                {
                    KeyLine.Text = "That will not do - use one key like Delete, or add Ctrl, Alt or Shift.";
                    return;
                }

                PanelUi.Say(StatusLine, _host.SetShortcut(text));
                RefreshKey();
            },
            () =>
            {
                ChangeKeyButton.IsEnabled = true;
                KeyLine.Text = "Unchanged.";
            });
    }

    private void ResetKey_Click(object sender, RoutedEventArgs e)
    {
        if (_host is null) return;

        PanelUi.Say(StatusLine, _host.ResetShortcut());
        RefreshKey();
    }

    private void PresetDelete_Click(object sender, RoutedEventArgs e) => UsePreset("Delete");

    private void PresetInsert_Click(object sender, RoutedEventArgs e) => UsePreset("Insert");

    private void PresetF8_Click(object sender, RoutedEventArgs e) => UsePreset("F8");

    private void UsePreset(string key)
    {
        if (_host is null) return;

        PanelUi.Say(StatusLine, _host.SetShortcut(key));
        RefreshKey();
    }

    private void NoOverlap_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _host is null) return;

        _host.SetHideOverOtherApps(NoOverlapSwitch.IsChecked == true);
        PanelUi.Say(StatusLine, NoOverlapSwitch.IsChecked == true
            ? "The panels step aside whenever another program is in front."
            : "The panels stay on top, even over other programs.");
    }

    private void OpenLauncher_Click(object sender, RoutedEventArgs e)
    {
        _host?.OpenLauncher();
        if (_host is not null) PanelUi.Say(StatusLine, "Bringing Aether to the front…");
    }

    private void Hide_Click(object sender, RoutedEventArgs e) => _host?.HideAll();
}
