using System.Text.Json;
using System.Text.Json.Serialization;

namespace AetherLauncher.Infrastructure;


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
