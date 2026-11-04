using System.Threading;
using System.Windows;
using AetherLauncher.Services;

namespace AetherLauncher;

/// <summary>
/// Betriebsart <c>--overlay</c>: ein eigener, kleiner Prozess, der die Platten über dem
/// Roblox-Client hält. Er läuft ohne Hauptfenster und beendet sich selbst, wenn eine Weile
/// kein Client mehr da ist. Ein zweiter Overlay-Prozess beendet sich sofort.
/// </summary>
internal static class OverlayMode
{
    // Muss am Leben bleiben, solange der Host läuft - sonst gibt Windows die Sperre frei.
    private static Mutex? _guard;

    /// <summary>
    /// Baut den Overlay-Host und überlässt die Nachrichtenschleife der Anwendung selbst.
    /// Liefert false, wenn schon ein Host läuft; der Aufrufer beendet dann den Prozess.
    /// </summary>
    public static bool Start()
    {
        _guard = new Mutex(true, GameOverlay.HostMutexName, out var first);

        if (!first)
        {
            // Ein abgestürzter Vorgänger hinterlässt ein verwaistes Objekt - dann übernehmen wir.
            try
            {
                first = _guard.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                first = true;
            }
        }

        if (!first)
        {
            _guard.Dispose();
            _guard = null;
            return false;
        }

        GameOverlay.Hosted = true;

        // Kein Hauptfenster und kein automatisches Ende beim Zumachen: der Host lebt, bis
        // das Overlay selbst Quit ruft oder Roblox eine Weile weg ist.
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        new OverlayHost().Start();

        return true;
    }
}
