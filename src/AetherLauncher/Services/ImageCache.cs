using System.Collections.Concurrent;
using System.Windows.Media.Imaging;

namespace AetherLauncher.Services;


public static class ImageCache
{
    
    
    
    
    
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
