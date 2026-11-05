using System.Windows;
using System.Windows.Controls;
using AetherLauncher.Services;

namespace AetherLauncher.Overlay;





public partial class ServerPanel : UserControl
{
    private OverlayHost? _host;
    private bool _busy;

    public ServerPanel() => InitializeComponent();

    internal void Attach(OverlayHost host)
    {
        _host = host;
        Refresh();
        host.SettingsChanged += Refresh;
    }

    internal void Refresh()
    {
        if (_host is null) return;

        
        
        
        var place = RobloxLogImport.LastJoin()?.PlaceId ?? _host.Settings.LastPlaceId;
        var known = place is not null;

        HopButton.IsEnabled = known && !_busy;
        RestartButton.IsEnabled = known && !_busy;
        GamePageButton.IsEnabled = known;
        CopyLinkButton.IsEnabled = known;

        PlaceLine.Text = known
            ? $"Current place: {place}."
            : "No place known yet - start a game in the launcher and this switches servers afterwards.";
    }

    private async void Hop_Click(object sender, RoutedEventArgs e)
    {
        if (_host is null || _busy) return;

        _busy = true;
        SetBusy(true);

        try
        {
            var message = await _host.HopAsync(text => PanelUi.Say(StatusLine, text));
            PanelUi.Say(StatusLine, message);
        }
        finally
        {
            _busy = false;
            SetBusy(false);
            Refresh();
        }
    }

    private async void Restart_Click(object sender, RoutedEventArgs e)
    {
        if (_host is null || _busy) return;

        _busy = true;
        SetBusy(true);

        try
        {
            var message = await _host.RestartAsync(text => PanelUi.Say(StatusLine, text));
            PanelUi.Say(StatusLine, message);
        }
        finally
        {
            _busy = false;
            SetBusy(false);
            Refresh();
        }
    }

    private void GamePage_Click(object sender, RoutedEventArgs e) => _host?.OpenGamePage();

    private void CopyLink_Click(object sender, RoutedEventArgs e)
    {
        if (_host is null) return;
        PanelUi.Say(StatusLine, _host.CopyGameLink());
    }

    private void SetBusy(bool busy)
    {
        HopLabel.Text = busy ? "Working…" : "Leave & rejoin";

        if (_host is not null)
        {
            var place = RobloxLogImport.LastJoin()?.PlaceId ?? _host.Settings.LastPlaceId;
            HopButton.IsEnabled = !busy && place is not null;
        }
    }
}
