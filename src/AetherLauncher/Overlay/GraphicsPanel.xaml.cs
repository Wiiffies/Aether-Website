using System.Windows;
using System.Windows.Controls;
using AetherLauncher.Controls;

namespace AetherLauncher.Overlay;

/// <summary>Grafik: Stufe, Texturen, Kantenglättung und Gras - alles allowlistete Flags.</summary>
public partial class GraphicsPanel : UserControl
{
    private OverlayHost? _host;
    private bool _ready;
    private bool _busy;

    public GraphicsPanel() => InitializeComponent();

    internal void Attach(OverlayHost host)
    {
        _host = host;
        Refresh();
        host.SettingsChanged += Refresh;
    }

    internal void Refresh()
    {
        if (_host is null) return;

        var (_, quality, texture, msaa, grassOff) = _host.ReadFlags();
        var vanilla = _host.IsVanilla;

        _ready = false;
        try
        {
            Check(QualityChips, quality);
            Check(TextureChips, texture);
            Check(MsaaChips, msaa);
            Check(GrassChips, grassOff is null ? null : grassOff == true ? "off" : "on");
        }
        finally
        {
            _ready = true;
        }

        foreach (var panel in new Panel[] { QualityChips, TextureChips, MsaaChips, GrassChips })
            panel.IsEnabled = !vanilla;

        VanillaNote.Visibility = vanilla ? Visibility.Visible : Visibility.Collapsed;
        RefreshActions();
    }

    private static void Check(Panel row, object? value)
    {
        foreach (var chip in row.Children.OfType<RadioButton>())
        {
            var tag = chip.Tag is string raw ? raw : chip.Tag?.ToString();
            chip.IsChecked = value is not null && string.Equals(tag, value.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }

    private void RefreshActions()
    {
        if (_host is null) return;

        var usable = !_host.IsVanilla && !_busy;
        RejoinButton.IsEnabled = usable;
        RestartButton.IsEnabled = usable && _host.Settings.LastPlaceId is not null;
    }

    private void QualityChip_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready || _host is null || sender is not RadioButton { Tag: string raw } || !int.TryParse(raw, out var level))
            return;

        Anim.Pulse((FrameworkElement)sender, 1.06, 220);
        PanelUi.Say(StatusLine, _host.ApplyQuality(level));
    }

    private void TextureChip_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready || _host is null || sender is not RadioButton { Tag: string raw } || !int.TryParse(raw, out var level))
            return;

        Anim.Pulse((FrameworkElement)sender, 1.06, 220);
        PanelUi.Say(StatusLine, _host.ApplyTexture(level));
    }

    private void MsaaChip_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready || _host is null || sender is not RadioButton { Tag: string raw } || !int.TryParse(raw, out var samples))
            return;

        Anim.Pulse((FrameworkElement)sender, 1.06, 220);
        PanelUi.Say(StatusLine, _host.ApplyMsaa(samples));
    }

    private void GrassChip_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready || _host is null || sender is not RadioButton { Tag: string raw }) return;

        Anim.Pulse((FrameworkElement)sender, 1.06, 220);
        PanelUi.Say(StatusLine, _host.ApplyGrass(string.Equals(raw, "off", StringComparison.OrdinalIgnoreCase)));
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
