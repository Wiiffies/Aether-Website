using System.Text.Json;
using System.Text.Json.Serialization;

namespace AetherLauncher.Infrastructure;

/// <summary>Kleiner Helfer für robustes Laden/Speichern von JSON-Dateien.</summary>
public static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Lädt eine Datei; bei Fehlern oder fehlender Datei wird <paramref name="fallback"/> benutzt.</summary>
    public static T Load<T>(string path, Func<T> fallback) where T : class
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var value = JsonSerializer.Deserialize<T>(json, Options);
                    if (value is not null) return value;
                }
            }
        }
        catch (Exception ex)
        {
            Services.Log.Error($"Could not read {Path.GetFileName(path)}: {ex.Message}");
        }

        return fallback();
    }

    /// <summary>Schreibt atomar über eine temporäre Datei, damit kein halber Zustand entsteht.</summary>
    public static bool Save<T>(string path, T value)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            Services.Log.Error($"Could not write {Path.GetFileName(path)}: {ex.Message}");
            return false;
        }
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
