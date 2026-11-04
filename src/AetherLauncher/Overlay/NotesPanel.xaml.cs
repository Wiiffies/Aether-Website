using System.Windows.Controls;
using System.Windows.Threading;
using AetherLauncher.Controls;
using AetherLauncher.Services;

namespace AetherLauncher.Overlay;

/// <summary>
/// Notizen als eigene Platte - man kann sie also irgendwohin schieben, wo sie beim Spielen
/// nicht stören, und sie speichert sich selbst.
/// </summary>
public partial class NotesPanel : UserControl
{
    private readonly DispatcherTimer _timer;
    private bool _loading;

    public NotesPanel()
    {
        InitializeComponent();

        // Geschrieben wird kurz nach dem letzten Tastendruck - nicht bei jedem Zeichen.
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(700) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Save(quiet: true);
        };
    }

    internal void Attach(OverlayHost host)
    {
        _ = host;

        _loading = true;
        try
        {
            NotesBox.Text = Notes.Load();
        }
        finally
        {
            _loading = false;
        }

        NotesLine.Text = "Saved automatically.";
    }

    /// <summary>Was gerade im Feld steht, sofort sichern (beim Zumachen des Overlays).</summary>
    internal void Flush()
    {
        if (!_timer.IsEnabled) return;

        _timer.Stop();
        Save(quiet: true);
    }

    private void NotesBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;

        NotesLine.Text = "Typing…";
        _timer.Stop();
        _timer.Start();
    }

    private void Save(bool quiet)
    {
        if (Notes.TrySave(NotesBox.Text, out var error))
        {
            NotesLine.Text = $"Saved at {DateTime.Now:HH:mm}.";
            Anim.Fade(NotesLine, 0.4, 1, 260);
            return;
        }

        NotesLine.Text = error ?? "The notes could not be saved.";
        if (!quiet) Anim.Fade(NotesLine, 0.4, 1, 260);
    }
}
