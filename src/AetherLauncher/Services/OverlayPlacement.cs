using AetherLauncher.Infrastructure;

namespace AetherLauncher.Services;

/// <summary>Wo eine einzelne Platte des Overlays zuletzt stand - und ob sie offen war.</summary>
public sealed class OverlaySpot
{
    public double Left { get; set; }
    public double Top { get; set; }

    /// <summary>War die Platte beim letzten Mal zu sehen? Das Menü selbst ist immer da.</summary>
    public bool Visible { get; set; } = true;
}

/// <summary>
/// Alles, was das Ingame-Overlay selbst besitzt: die Lage und Sichtbarkeit jeder Platte und
/// die Anti-AFK-Einstellungen.
///
/// Bewusst eine eigene Datei. In settings.json würde das Overlay mit dem Launcher um dieselbe
/// Datei streiten: der Launcher schreibt beim Beenden seinen Stand zurück und würde die gerade
/// verschobene Platte damit wieder an die alte Stelle setzen.
/// </summary>
public sealed class OverlayState
{
    /// <summary>
    /// Fassung der Startaufteilung. Ältere Dateien kennen die dreispaltige Aufteilung noch
    /// nicht; sie werden beim ersten Start einmal neu gesetzt.
    /// </summary>
    public const int CurrentLayoutVersion = 2;

    public int LayoutVersion { get; set; }

    /// <summary>Platte (Schlüssel, siehe PanelIds) → gemerkte Stelle.</summary>
    public Dictionary<string, OverlaySpot> Panels { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Die Platten zurücknehmen, sobald ein anderes Programm vorne ist. Damit liegt das
    /// Overlay nie über dem Browser, dem Launcher oder Discord - und ist wieder da, sobald
    /// man im Spiel ist.
    /// </summary>
    public bool HideOverOtherApps { get; set; } = true;

    /// <summary>Deckkraft aller Platten in Prozent. Unter 55 % wird der Text unlesbar.</summary>
    public int PanelOpacity { get; set; } = 100;

    public bool AntiAfkEnabled { get; set; }
    public int AntiAfkIntervalSeconds { get; set; } = AntiAfk.DefaultIntervalSeconds;
    public string AntiAfkKey { get; set; } = AntiAfk.DefaultKey;

    /// <summary>Fassung der Anti-AFK-Einstellungen - ältere Dateien kennen den Pop-up-Tipp noch nicht.</summary>
    public int AntiAfkVersion { get; set; }
}

/// <summary>Merkt sich, wie das Ingame-Overlay zuletzt aussah (overlay.json).</summary>
public static class OverlayPlacement
{
    public static OverlayState Load(string? path = null)
    {
        var state = JsonFile.Load(path ?? AppPaths.OverlayPlacementFile, () => new OverlayState());

        foreach (var (key, spot) in state.Panels.ToList())
        {
            // Unsinnige Werte (NaN, Infinity) würden eine Platte unauffindbar machen.
            if (double.IsNaN(spot.Left) || double.IsNaN(spot.Top) ||
                double.IsInfinity(spot.Left) || double.IsInfinity(spot.Top))
                state.Panels.Remove(key);
        }

        if (!AntiAfk.ValidInterval(state.AntiAfkIntervalSeconds))
            state.AntiAfkIntervalSeconds = AntiAfk.DefaultIntervalSeconds;

        // Die alte, stille Variante hat die Taste nie benutzt; ihr Standard war LeftShift.
        // Beim Umstieg auf den Pop-up-Tipp wird daraus die unauffällige F8.
        if (state.AntiAfkVersion < 1)
        {
            if (string.Equals(state.AntiAfkKey, "LeftShift", StringComparison.OrdinalIgnoreCase))
                state.AntiAfkKey = AntiAfk.DefaultKey;

            state.AntiAfkVersion = 1;
        }

        state.PanelOpacity = Math.Clamp(state.PanelOpacity, 55, 100);

        return state;
    }

    public static bool Save(OverlayState state, string? path = null) =>
        JsonFile.Save(path ?? AppPaths.OverlayPlacementFile, state);

    /// <summary>Vergisst alle gemerkten Stellen - die Platten sitzen dann wieder über dem Spiel.</summary>
    public static bool Clear(string? path = null)
    {
        try
        {
            var file = path ?? AppPaths.OverlayPlacementFile;
            if (File.Exists(file)) File.Delete(file);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"The saved overlay layout could not be removed: {ex.Message}");
            return false;
        }
    }
}
