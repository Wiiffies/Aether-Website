using AetherLauncher.Models;

namespace AetherLauncher.Services;

/// <summary>
/// Änderungen an Dateien im Client-Ordner. Bisher nur der Mauszeiger im Spiel: der Client
/// zeichnet seinen Pfeil aus PNG-Dateien unter content\textures. Jede Datei wird einmal als
/// .aetherbak gesichert und lässt sich damit vollständig zurückholen.
///
/// Bewusste Grenze: es werden nur Zeiger-Texturen angefasst, keine Programme und keine
/// Skripte. Im Vanilla-Modus passiert hier gar nichts.
/// </summary>
public static class ClientMods
{
    public const string BackupSuffix = ".aetherbak";

    /// <summary>Die Zeiger-Texturen des Clients, relativ zum Versionsordner.</summary>
    public static IReadOnlyList<string> CursorTextures { get; } = new[]
    {
        @"content\textures\ArrowCursor.png",
        @"content\textures\ArrowFarCursor.png",
        @"content\textures\advCursor-default.png",
        @"content\textures\advCursor-white.png",
        @"content\textures\MouseLockedCursor.png",
        @"content\textures\Cursors\KeyboardMouse\ArrowCursor.png",
        @"content\textures\Cursors\KeyboardMouse\ArrowFarCursor.png",
        @"content\textures\Cursors\CrossMouseIcon.png",
    };

    /// <summary>Die tatsächlich vorhandenen Zeiger-Texturen dieser Version.</summary>
    public static List<string> DiscoverCursorTextures(string versionDirectory)
    {
        var found = new List<string>();
        if (string.IsNullOrWhiteSpace(versionDirectory)) return found;

        foreach (var relative in CursorTextures)
        {
            var path = Path.Combine(versionDirectory, relative);
            if (File.Exists(path)) found.Add(path);
        }

        return found;
    }

    /// <summary>Wie viele Zeiger-Texturen gerade ersetzt sind.</summary>
    public static int CountModified(string versionDirectory)
        => DiscoverCursorTextures(versionDirectory).Count(p => File.Exists(p + BackupSuffix));

    /// <summary>
    /// Ersetzt alle vorhandenen Zeiger-Texturen durch das gewählte Bild. Jede Datei behält
    /// dabei ihre ursprüngliche Pixelgröße - der Client rechnet fest mit diesen Maßen.
    /// </summary>
    public static bool ApplyCursor(string versionDirectory, string sourceImage, out string message)
    {
        message = "";
        if (string.IsNullOrWhiteSpace(sourceImage) || !File.Exists(sourceImage))
        {
            message = "No cursor image selected yet.";
            return false;
        }

        var targets = DiscoverCursorTextures(versionDirectory);
        if (targets.Count == 0)
        {
            message = "This client version has no cursor textures to replace.";
            return false;
        }

        var replaced = 0;
        var failed = 0;
        foreach (var target in targets)
        {
            var backup = target + BackupSuffix;
            try
            {
                var size = ImageTool.SizeOf(backup) ?? ImageTool.SizeOf(target);
                if (size is null)
                {
                    failed++;
                    continue;
                }

                // Einmalig das Original sichern - ein zweiter Lauf darf die Sicherung nicht überschreiben.
                if (!File.Exists(backup)) File.Copy(target, backup);

                if (ImageTool.ResizePng(sourceImage, target, size.Value.Width, size.Value.Height, out var error))
                    replaced++;
                else
                {
                    failed++;
                    Log.Warn(error);
                }
            }
            catch (Exception ex)
            {
                failed++;
                Log.Warn($"Cursor texture {Path.GetFileName(target)} could not be replaced: {ex.Message}");
            }
        }

        if (replaced == 0)
        {
            message = failed > 0
                ? "The cursor textures could not be replaced (see the log)."
                : "Nothing replaced.";
            return false;
        }

        message = failed == 0
            ? $"In-game cursor replaced in {replaced} file(s)."
            : $"In-game cursor replaced in {replaced} file(s), {failed} failed (see the log).";
        return true;
    }

    /// <summary>Stellt die ursprünglichen Zeiger-Texturen aus den Sicherungen wieder her.</summary>
    public static bool RestoreCursor(string versionDirectory, out string message)
    {
        var restored = 0;
        foreach (var target in DiscoverCursorTextures(versionDirectory))
        {
            var backup = target + BackupSuffix;
            try
            {
                if (!File.Exists(backup)) continue;
                File.Copy(backup, target, overwrite: true);
                File.Delete(backup);
                restored++;
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not restore {Path.GetFileName(target)}: {ex.Message}");
            }
        }

        message = restored == 0
            ? "There was nothing to restore - the cursor textures are untouched."
            : $"Original cursor restored in {restored} file(s).";
        return restored > 0;
    }

    /// <summary>Wendet die Einstellung auf eine Version an (beim Start und auf Knopfdruck).</summary>
    public static void ApplySettings(LauncherSettings settings, string versionDirectory, out string message)
    {
        message = "";
        if (!settings.InGameCursorEnabled)
        {
            if (CountModified(versionDirectory) > 0) RestoreCursor(versionDirectory, out message);
            return;
        }

        var source = ResolveCursorImage(settings);
        if (string.IsNullOrEmpty(source))
        {
            message = "No cursor image found - pick one in Settings.";
            return;
        }

        ApplyCursor(versionDirectory, source, out message);
    }

    /// <summary>Das gewählte Bild oder das mitgelieferte Aether-Zeigerbild.</summary>
    public static string ResolveCursorImage(LauncherSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.InGameCursorPath) && File.Exists(settings.InGameCursorPath))
            return settings.InGameCursorPath!;

        var bundled = ImageTool.EnsureBundledCursor(Path.Combine(AppPaths.DataDir, "cursor.png"));
        if (!string.IsNullOrEmpty(bundled)) settings.InGameCursorPath = bundled;
        return bundled;
    }
}
