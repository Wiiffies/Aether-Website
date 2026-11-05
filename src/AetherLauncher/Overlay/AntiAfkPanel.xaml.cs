using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AetherLauncher.Services;

namespace AetherLauncher.Overlay;





public partial class AntiAfkPanel : UserControl
{
    private OverlayHost? _host;
    private bool _loading;
    private DateTime _holdStatusUntil;

    
    
    private readonly DispatcherTimer _clock;

    public AntiAfkPanel()
    {
        InitializeComponent();

        _clock = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) =>
        {
            if (_host is null || DateTime.UtcNow < _holdStatusUntil) return;
            StatusLine.Text = _host.AntiAfkLine;
        };
        _clock.Start();
    }

    internal void Attach(OverlayHost host)
    {
        _host = host;
        Refresh();
        host.AntiAfkUpdated += Refresh;
    }

    internal void Refresh()
    {
        if (_host is null) return;

        _loading = true;
        try
        {
            EnabledSwitch.IsChecked = _host.AntiAfkEnabled;
            Settings.IsEnabled = _host.AntiAfkEnabled;

            foreach (var chip in IntervalChips.Children.OfType<RadioButton>())
                chip.IsChecked = chip.Tag is string raw && int.TryParse(raw, out var seconds) && seconds == _host.AntiAfkInterval;

            KeyBadge.Text = _host.AntiAfkKey;
        }
        finally
        {
            _loading = false;
        }

        PanelUi.Say(StatusLine, _host.AntiAfkLine);
    }

    private void Enabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _host is null) return;

        _host.SetAntiAfkEnabled(EnabledSwitch.IsChecked == true);
        Refresh();
    }

    private void Interval_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || _host is null || sender is not RadioButton { Tag: string raw } || !int.TryParse(raw, out var seconds))
            return;

        _host.SetAntiAfkInterval(seconds);
        Refresh();
    }

    private void ChangeKey_Click(object sender, RoutedEventArgs e)
    {
        if (_host is null) return;

        PanelUi.Say(StatusLine, "Press the key that should be tapped - Esc cancels.");

        _host.BeginCapture(KeyCaptureKind.SingleKey,
            text =>
            {
                if (text is null)
                {
                    Refresh();
                    return;
                }

                _host.SetAntiAfkKey(text);
                Refresh();
            },
            Refresh);
    }

    private async void NudgeNow_Click(object sender, RoutedEventArgs e)
    {
        if (_host is null) return;

        NudgeButton.IsEnabled = false;
        try
        {
            
            var message = await Task.Run(() => _host.NudgeNow());
            _holdStatusUntil = DateTime.UtcNow.AddSeconds(4);
            PanelUi.Say(StatusLine, message);
        }
        finally
        {
            NudgeButton.IsEnabled = true;
        }
    }

    private async void SelfCheck_Click(object sender, RoutedEventArgs e)
    {
        SelfCheckButton.IsEnabled = false;
        _holdStatusUntil = DateTime.UtcNow.AddSeconds(4);
        PanelUi.Say(StatusLine, "Checking the input path ...");

        try
        {
            var (works, message) = await Task.Run(() =>
            {
                var ok = AntiAfk.TryTestInput(out var text);
                return (ok, text);
            });

            _holdStatusUntil = DateTime.UtcNow.AddSeconds(4);
            PanelUi.Say(StatusLine, message);
            Log.Info($"Anti-AFK self-check: {message}");
            if (!works) Log.Warn("Anti-AFK self-check could not move the pointer.");
        }
        finally
        {
            SelfCheckButton.IsEnabled = true;
        }
    }
}
