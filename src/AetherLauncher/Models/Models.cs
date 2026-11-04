using AetherLauncher.Services;

namespace AetherLauncher.Models;

/// <summary>Ein gespeichertes Roblox-Konto. Das Cookie liegt nur DPAPI-verschlüsselt (Base64) in der Datei.</summary>
public sealed class AccountRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public long UserId { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";

    /// <summary>Base64 von CryptProtectData(.ROBLOSECURITY) - nur für den angemeldeten Windows-Benutzer lesbar.</summary>
    public string CookieProtected { get; set; } = "";

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? LastValidatedAt { get; set; }
    public DateTimeOffset? LastLaunchedAt { get; set; }
}

/// <summary>Ein gespeicherter Spiel-Startplatz.</summary>
public sealed class PlaceRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public long PlaceId { get; set; }
    public string? AccessCode { get; set; }
    public string Name { get; set; } = "";
    public long? UniverseId { get; set; }
    public int LaunchCount { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? LastPlayedAt { get; set; }

    /// <summary>Angeheftet (Favorit): steht oben in der Liste, egal wie alt der letzte Start ist.</summary>
    public bool IsPinned { get; set; }

    /// <summary>Wann angeheftet wurde - neue Favoriten stehen vor älteren.</summary>
    public DateTimeOffset? PinnedAt { get; set; }

    /// <summary>
    /// Platz in der Liste. Wird mit der Maus gesetzt (ziehen) und bleibt in places.json.
    /// Alle Werte 0 heißt: noch nie von Hand sortiert.
    /// </summary>
    public int SortIndex { get; set; }
}

/// <summary>
/// Ein Start aus der Vergangenheit. Anders als <see cref="PlaceRecord"/> wird das bei
/// <b>jedem</b> Start mitgeschrieben - auch wenn das Spiel nie gespeichert wurde. So zeigt
/// die Startseite, was zuletzt lief, und man kann es von dort übernehmen.
/// </summary>
public sealed class RecentRecord
{
    public long PlaceId { get; set; }
    public string? AccessCode { get; set; }
    public string Name { get; set; } = "";
    public long? UniverseId { get; set; }
    public string? AccountName { get; set; }
    public DateTimeOffset PlayedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>Wie oft dieser Platz in der Liste stand - steigt, wenn man ihn erneut startet.</summary>
    public int Sessions { get; set; } = 1;

    /// <summary>Aus Roblox' eigener Sitzungsliste übernommen - nicht über Aether gestartet.</summary>
    public bool FromLog { get; set; }
}

/// <summary>Vom Benutzer konfigurierbare Launcher-Einstellungen.</summary>
public sealed class LauncherSettings
{
    /// <summary>Überschreibt den gesuchten Roblox-Ordner (z. B. Bloxstrap-Installation).</summary>
    public string? RobloxRoot { get; set; }

    /// <summary>Deployment-Kanal, z. B. "production".</summary>
    public string Channel { get; set; } = "production";

    /// <summary>
    /// Angeheftete Client-Version (Ordner "version-..."). Null = die neueste gefundene.
    /// Damit lässt sich die Fassung starten, die andere Programme erwarten.
    /// </summary>
    public string? PinnedVersionGuid { get; set; }

    /// <summary>
    /// Vanilla-Modus: startet den Client so, wie Roblox ihn installiert hat. Keine FastFlags,
    /// keine Texturen, kein zusätzlicher Prozess - damit andere Programme (Velocity, Madium,
    /// Volt und ähnliche) den normalen Zweig finden, den sie unterstützen.
    /// </summary>
    public bool VanillaMode { get; set; }

    /// <summary>Ersetzt den Mauszeiger im Spiel beim Start (nur sichtbare Zeiger-Texturen).</summary>
    public bool InGameCursorEnabled { get; set; }

    /// <summary>Bild für den Ingame-Zeiger; leer = das mitgelieferte Aether-Bild.</summary>
    public string? InGameCursorPath { get; set; }

    /// <summary>Schrift der Oberfläche (null = Segoe UI).</summary>
    public string? UiFontFamily { get; set; }

    /// <summary>Geladene eigene Schriftdatei (nur für diesen Prozess angemeldet).</summary>
    public string? UiFontFile { get; set; }

    /// <summary>Fröhlicher Modus: federnde Einblendungen, Glanz beim Überfahren.</summary>
    public bool PlayfulMode { get; set; } = true;

    /// <summary>Farbwelt der Oberfläche (siehe ThemeManager).</summary>
    public string Theme { get; set; } = ThemeManager.DefaultId;

    /// <summary>Die Schnellansicht ("Layout") über der App - ab Werk an.</summary>
    public bool OverlayEnabled { get; set; } = true;

    /// <summary>Tastenkürzel, mit dem das Menü aufgeht - standardmäßig Entf (Delete).</summary>
    public string OverlayShortcut { get; set; } = "Delete";

    /// <summary>
    /// Die Schnellansicht über dem Roblox-Fenster ("Ingame-Overlay"). Sie zeichnet nur
    /// darüber und schreibt Flags - in den Client wird nichts eingeschleust.
    /// </summary>
    public bool GameOverlayEnabled { get; set; } = true;

    /// <summary>
    /// Das Overlay von selbst aufgehen lassen, sobald ein Roblox-Fenster da ist. Ab Werk
    /// aus: beim Start bleibt das Menü zu und wartet auf das Kürzel.
    /// </summary>
    public bool GameOverlayAutoShow { get; set; }

    /// <summary>
    /// Stand der Einstellungen. Version 2 hat das automatische Öffnen einmalig
    /// abgeschaltet - so bleibt das Menü auch bei vorhandenen Einstellungen zu.
    /// </summary>
    public int SettingsRevision { get; set; }

    /// <summary>Zuletzt gestarteter Platz - Grundlage für "Server hop" im Overlay.</summary>
    public long? LastPlaceId { get; set; }

    /// <summary>Zugangscode des zuletzt gestarteten Platzes (privater Server) oder null.</summary>
    public string? LastPlaceAccessCode { get; set; }

    /// <summary>Konto des letzten Starts (ID aus accounts.json) oder null für die aktuelle Anmeldung.</summary>
    public string? LastAccountId { get; set; }

    /// <summary>Zeigerpfeil von Aether statt des Windows-Pfeils.</summary>
    public bool CustomCursor { get; set; } = true;

    /// <summary>Aktiviert das Halten des Roblox-Singleton-Mutex für mehrere Clients.</summary>
    public bool MultiInstance { get; set; } = true;

    /// <summary>Schreibt die konfigurierten FastFlags vor jedem Start in die ClientSettings des Clients.</summary>
    public bool ApplyFastFlags { get; set; } = true;

    /// <summary>Multi-Start: Wartezeit zwischen zwei Clients in Millisekunden.</summary>
    public int MultiLaunchStaggerMs { get; set; } = 8000;

    public bool UseCurrentLogin { get; set; }

    /// <summary>Bisheriger Befehl des roblox-player-Protokolls, damit die Übernahme rückgängig gemacht werden kann.</summary>
    public string? SavedProtocolHandler { get; set; }

    public bool ProtocolHandlerRegistered { get; set; }

    /// <summary>Zufällige, stabile ID, die (wie im offiziellen Client) in Platz-Start-URLs mitgeschickt wird.</summary>
    public long BrowserTrackerId { get; set; } = Random.Shared.NextInt64(100000, 999999999);
}
