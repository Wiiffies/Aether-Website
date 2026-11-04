using System.Windows;
using System.Windows.Controls;
using AetherLauncher.Controls;
using AetherLauncher.Services;

namespace AetherLauncher.Overlay;

/// <summary>Bildrate: ohne Deckel, fester Wert oder ein eigener.</summary>
public partial class FpsPanel : UserControl
{
    private OverlayHost? _host;
    private bool _ready;
    private bool _busy;

    public FpsPanel() => InitializeComponent();

    internal void Attach(OverlayHost host)
    {
        _host = host;
        Refresh();
        host.SettingsChanged += Refresh;
    }

    internal void Refresh()
    {
        if (_host is null) return;

        var (frameRate, _, _, _, _) = _host.ReadFlags();
        var vanilla = _host.IsVanilla;

        _ready = false;
        try
        {
            foreach (var chip in FpsChips.Children.OfType<RadioButton>())
            {
                chip.IsChecked = chip.Tag is string raw && int.TryParse(raw, out var value) && value == frameRate;
            }

            CustomFpsBox.Text = frameRate is { } cap ? cap.ToString() : "";
        }
        finally
        {
            _ready = true;
        }

        FpsChips.IsEnabled = !vanilla;
        CustomFpsBox.IsEnabled = !vanilla;
        ApplyButton.IsEnabled = !vanilla;
        VanillaNote.Visibility = vanilla ? Visibility.Visible : Visibility.Collapsed;

        RefreshActions();
    }

    private void RefreshActions()
    {
        if (_host is null) return;

        var usable = !_host.IsVanilla && !_busy;
        RejoinButton.IsEnabled = usable;
        RestartButton.IsEnabled = usable && _host.Settings.LastPlaceId is not null;
    }

    private void FrameRateChip_Checked(object sender, RoutedEventArgs e)
    {
        if (_ready || _host is null || sender is not RadioButton { Tag: string raw } || !int.TryParse(raw, out var frameRate))
            return;

        Anim.Pulse((FrameworkElement)sender, 1.06, 220);
        PanelUi.Say(StatusLine, _host.ApplyFrameRate(frameRate));
    }

    private void ApplyCustom_Click(object sender, RoutedEventArgs e)
    {
        if (_host is null) return;

        if (!int.TryParse(CustomFpsBox.Text.Trim(), out var frameRate) || frameRate is < 10 or > FastFlags.UncappedFrameRate)
        {
            PanelUi.Say(StatusLine, $"Enter a number between 10 and {FastFlags.UncappedFrameRate}.", error: true);
            return;
        }

        PanelUi.Say(StatusLine, _host.ApplyFrameRate(frameRate));
        Refresh();
    }

    private async void Rejoin_Click(object sender, RoutedEventArgs e)
    {
        if (_host is null || _busy) return;

        _busy = true;
        RefreshActions();

        try
        {
            var message = await _host.RejoinAsync(text => PanelUi.Say(StatusLine, text));
            PanelUi.Say(StatusLine, message);
        }
        finally
        {
            _busy = false;
            RefreshActions();
        }
    }

    private async void Restart_Click(object sender, RoutedEventArgs e)
    {
        if (_host is null || _busy) return;

        _busy = true;
        RefreshActions();

        try
        {
            var message = await _host.RestartAsync(text => PanelUi.Say(StatusLine, text));
            PanelUi.Say(StatusLine, message);
        }
        finally
        {
            _busy = false;
            RefreshActions();
        }
    }
}
