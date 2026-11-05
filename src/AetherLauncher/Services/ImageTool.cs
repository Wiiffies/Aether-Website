using System.Windows;
using System.Windows.Media.Imaging;

namespace AetherLauncher.Services;





public static class ImageTool
{
    
    
    
    
    
    public static (int Width, int Height)? SizeOf(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;

            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) return null;

            var frame = decoder.Frames[0];
            if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0) return null;
            return (frame.PixelWidth, frame.PixelHeight);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not read the image size of {Path.GetFileName(path)}: {ex.Message}");
            return null;
        }
    }

    
    public static bool ResizePng(string sourcePath, string targetPath, int width, int height, out string error)
    {
        error = "";
        try
        {
            var source = Load(sourcePath);
            if (source is null)
            {
                error = $"The image {Path.GetFileName(sourcePath)} could not be read.";
                return false;
            }

            var scaled = new TransformedBitmap(source,
                new System.Windows.Media.ScaleTransform(
                    width / (double)source.PixelWidth,
                    height / (double)source.PixelHeight));
            scaled.Freeze();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(scaled));

            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            using var stream = File.Create(targetPath);
            encoder.Save(stream);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Could not write {Path.GetFileName(targetPath)}: {ex.Message}";
            return false;
        }
    }

    
    public static BitmapSource? Load(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not load {Path.GetFileName(path)}: {ex.Message}");
            return null;
        }
    }

    
    public static string EnsureBundledCursor(string targetPath)
    {
        try
        {
            if (File.Exists(targetPath)) return targetPath;

            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/cursor.png"));
            if (resource is null) return "";

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            using var stream = File.Create(targetPath);
            resource.Stream.CopyTo(stream);
            return targetPath;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not unpack the bundled cursor: {ex.Message}");
            return "";
        }
    }
}
