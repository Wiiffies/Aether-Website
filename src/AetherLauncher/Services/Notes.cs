namespace AetherLauncher.Services;

/// <summary>
/// Die Notizen der Schnellansicht: eine einfache Textdatei im Datenordner. Sie wird beim
/// Tippen gespeichert, damit nichts verloren geht, wenn das Fenster schnell zugeht.
/// </summary>
public static class Notes
{
    public static string Load(string? path = null)
    {
        var file = path ?? AppPaths.NotesFile;
        try
        {
            return File.Exists(file) ? File.ReadAllText(file) : "";
        }
        catch (Exception ex)
        {
            Log.Warn($"The notes could not be read: {ex.Message}");
            return "";
        }
    }

    public static bool TrySave(string text, out string? error, string? path = null)
    {
        error = null;
        var file = path ?? AppPaths.NotesFile;

        try
        {
            var directory = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            File.WriteAllText(file, text ?? "");
            return true;
        }
        catch (Exception ex)
        {
            error = $"The notes could not be saved: {ex.Message}";
            Log.Warn(error);
            return false;
        }
    }
}
