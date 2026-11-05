namespace AetherLauncher.Services;


public static class AppPaths
{
    public static string DataDir { get; } = EnsureDir(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AetherLauncher"));

    public static string AccountsFile => Path.Combine(DataDir, "accounts.json");
    public static string PlacesFile => Path.Combine(DataDir, "places.json");

    
    public static string RecentFile => Path.Combine(DataDir, "recent.json");

    
    public static string OverlayPlacementFile => Path.Combine(DataDir, "overlay.json");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string FastFlagsFile => Path.Combine(DataDir, "FastFlags.json");

    
    public static string FastFlagsFavoritesFile => Path.Combine(DataDir, "fastflags-favorites.txt");
    public static string PendingLaunchFile => Path.Combine(DataDir, "pending-launch.txt");

    
    public static string VaultKeyFile => Path.Combine(DataDir, "vault.key");

    
    public static string NotesFile => Path.Combine(DataDir, "notes.txt");
    public static string LogFile => Path.Combine(DataDir, "launcher.log");
    public static string AvatarDir => EnsureDir(Path.Combine(DataDir, "avatars"));
    public static string IconDir => EnsureDir(Path.Combine(DataDir, "icons"));

    
    public static string FontDir => EnsureDir(Path.Combine(DataDir, "fonts"));

    
    public static string CursorImageFile => Path.Combine(DataDir, "cursor.png");

    
    public static string DownloadDir => EnsureDir(Path.Combine(DataDir, "downloads"));

    
    public static string ExecutablePath =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "AetherLauncher.exe");

    
    public static string DefaultRobloxRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox");

    
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
            
        }

        return path;
    }
}
