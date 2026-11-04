namespace AetherLauncher.Services;

/// <summary>Alle Pfade, die der Launcher benutzt. Standard: %LOCALAPPDATA%\AetherLauncher.</summary>
public static class AppPaths
{
    public static string DataDir { get; } = EnsureDir(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AetherLauncher"));

    public static string AccountsFile => Path.Combine(DataDir, "accounts.json");
    public static string PlacesFile => Path.Combine(DataDir, "places.json");

    /// <summary>Was zuletzt gestartet wurde - auch Spiele, die nie gespeichert wurden.</summary>
    public static string RecentFile => Path.Combine(DataDir, "recent.json");

    /// <summary>Gemerkte Lage des Ingame-Overlays (eigene Datei, siehe OverlayPlacement).</summary>
    public static string OverlayPlacementFile => Path.Combine(DataDir, "overlay.json");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string FastFlagsFile => Path.Combine(DataDir, "FastFlags.json");
    public static string PendingLaunchFile => Path.Combine(DataDir, "pending-launch.txt");

    /// <summary>AES-Schlüssel für die Cookie-Ablage; selbst DPAPI-verschlüsselt.</summary>
    public static string VaultKeyFile => Path.Combine(DataDir, "vault.key");

    /// <summary>Notizen aus der Schnellansicht.</summary>
    public static string NotesFile => Path.Combine(DataDir, "notes.txt");
    public static string LogFile => Path.Combine(DataDir, "launcher.log");
    public static string AvatarDir => EnsureDir(Path.Combine(DataDir, "avatars"));
    public static string IconDir => EnsureDir(Path.Combine(DataDir, "icons"));

    /// <summary>Eigene Schriftdateien, die nur für diesen Prozess angemeldet werden.</summary>
    public static string FontDir => EnsureDir(Path.Combine(DataDir, "fonts"));

    /// <summary>Ablage des mitgelieferten Zeigerbildes (Quelle für den Ingame-Cursor).</summary>
    public static string CursorImageFile => Path.Combine(DataDir, "cursor.png");

    /// <summary>Heruntergeladene Roblox-Installer.</summary>
    public static string DownloadDir => EnsureDir(Path.Combine(DataDir, "downloads"));

    /// <summary>Pfad der laufenden Anwendung (funktioniert auch bei Single-File-Veröffentlichung).</summary>
    public static string ExecutablePath =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "AetherLauncher.exe");

    /// <summary>Standard-Installationsordner des Roblox-Players.</summary>
    public static string DefaultRobloxRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox");

    /// <summary>Von Bloxstrap/Fishstrap verwaltete Installationen - als Ausweichoption.</summary>
    public static IEnumerable<string> AlternateRobloxRoots()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(local, "Bloxstrap");
        yield return Path.Combine(local, "Fishstrap");
    }

    private static string EnsureDir(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch
        {
            // Wenn das nicht klappt, scheitert später das Speichern mit einer klaren Meldung.
        }

        return path;
    }
}
