using System.Collections.Concurrent;
using System.Windows.Media.Imaging;

namespace AetherLauncher.Services;

/// <summary>Lädt Profilbilder und Spiel-Icons und legt sie im Datenordner ab.</summary>
public static class ImageCache
{
    /// <summary>
    /// Eine Sperre je Datei: zwei Listen können dasselbe Icon anfordern (Spiel gespeichert und
    /// zuletzt gespielt). Ohne das schreiben beide gleichzeitig und das Lesen scheitert dann mit
    /// "being used by another process" - das Icon fehlte, bis man neu startet.
    /// </summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates =
        new(StringComparer.OrdinalIgnoreCase);

    public static Task<BitmapImage?> GetAvatarAsync(long userId, CancellationToken ct = default)
        => LoadOrDownloadAsync(
            Path.Combine(AppPaths.AvatarDir, $"{userId}.png"),
            () => RobloxApi.DownloadAvatarAsync(userId, ct));

    public static Task<BitmapImage?> GetGameIconAsync(long universeId, CancellationToken ct = default)
        => LoadOrDownloadAsync(
            Path.Combine(AppPaths.IconDir, $"{universeId}.png"),
            () => RobloxApi.DownloadGameIconAsync(universeId, ct));

    private static async Task<BitmapImage?> LoadOrDownloadAsync(string path, Func<Task<byte[]?>> download)
    {
        var gate = Gates.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync().ConfigureAwait(false);

        try
        {
            if (!File.Exists(path))
            {
                var bytes = await download().ConfigureAwait(false);
                if (bytes is null || bytes.Length == 0) return null;

                // Erst daneben schreiben, dann umbenennen: ein halb geschriebenes Bild kann so
                // nie gelesen werden, auch nicht von einem zweiten Fenster (dem Overlay).
                var temp = $"{path}.{Environment.ProcessId}.part";
                await File.WriteAllBytesAsync(temp, bytes).ConfigureAwait(false);
                File.Move(temp, path, overwrite: true);
            }

            return FromFile(path);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"Could not load an image: {ex.Message}");
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Liest das Bild vollständig ein und friert es ein - so ist es threadübergreifend nutzbar.</summary>
    private static BitmapImage? FromFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not read the cached image {Path.GetFileName(path)}: {ex.Message}");
            return null;
        }
    }
}
