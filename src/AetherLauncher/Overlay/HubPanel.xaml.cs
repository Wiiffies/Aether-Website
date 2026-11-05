using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AetherLauncher.Overlay;





public partial class HubPanel : UserControl
{
    private OverlayHost? _host;
    private bool _loading;
    private readonly Dictionary<string, TextBlock> _panelStates = new(StringComparer.OrdinalIgnoreCase);

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
        RefreshPanelRows();
    }

    private void BuildPanelRows()
    {
        if (_host is null) return;

        PanelRows.Children.Clear();
        _panelStates.Clear();

        foreach (var choice in _host.PanelChoices)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8), Tag = choice.Id };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var id = choice.Id;

            var toggle = new CheckBox
            {
                Style = (Style)FindResource("Switch"),
                IsChecked = _host.IsPanelVisible(id),
                Tag = id,
            };

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

            var copy = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
            copy.Children.Add(new TextBlock
            {
                Text = choice.Title,
                FontSize = 11,
                Foreground = (Brush)FindResource("TextMidBrush"),
            });
            copy.Children.Add(new TextBlock
            {
                Text = choice.Hint,
                Style = (Style)FindResource("Note"),
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = (Brush)FindResource("TextFaintBrush"),
                TextWrapping = TextWrapping.Wrap,
            });

            var state = new TextBlock
            {
                Text = "OPEN",
                FontSize = 8.5,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 3, 0, 0),
                Foreground = (Brush)FindResource("TextFaintBrush"),
            };
            _panelStates[id] = state;

            var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            right.Children.Add(toggle);
            right.Children.Add(state);

            Grid.SetColumn(copy, 0);
            Grid.SetColumn(right, 1);
            row.Children.Add(copy);
            row.Children.Add(right);

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
            foreach (var row in PanelRows.Children.OfType<Grid>())
            {
                if (row.Tag is not string id) continue;

                var toggle = row.Children.OfType<StackPanel>()
                    .SelectMany(panel => panel.Children.OfType<CheckBox>())
                    .FirstOrDefault();
                if (toggle is not null) toggle.IsChecked = _host.IsPanelVisible(id);

                if (_panelStates.TryGetValue(id, out var state))
                {
                    var open = _host.IsPanelVisible(id);
                    state.Text = open ? "OPEN" : "HIDDEN";
                    state.Foreground = (Brush)FindResource(open ? "TextMidBrush" : "TextFaintBrush");
                }
            }
        }
        finally
        {
            _loading = false;
        }

        RefreshSummary();
    }

    private void RefreshSummary()
    {
        if (_host is null) return;
        SummaryLine.Text = $"{_host.EnabledPanelCount} of {_host.PanelChoices.Count} panels enabled · {_host.ShortcutText}";
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

    private void ShowAll_Click(object sender, RoutedEventArgs e)
    {
        if (_host is null) return;

        _host.ShowAll();
        RefreshPanelRows();
        PanelUi.Say(StatusLine, "Every enabled panel is back over the game.");
    }
}
