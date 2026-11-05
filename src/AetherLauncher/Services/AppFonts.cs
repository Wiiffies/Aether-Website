using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using AetherLauncher.Models;

namespace AetherLauncher.Services;






public static class AppFonts
{
    public const string DefaultFamily = "Segoe UI";
    public const string DefaultLabel = "Default (Segoe UI)";

    private const uint FrPrivate = 0x10;
    private static readonly List<string> LoadedFiles = new();

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int AddFontResourceEx(string file, uint flags, IntPtr reserved);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool RemoveFontResourceEx(string file, uint flags, IntPtr reserved);

    
    public static List<string> InstalledFamilies()
    {
        var names = new List<string>();
        try
        {
            foreach (var family in Fonts.SystemFontFamilies)
            {
                var name = family.FamilyNames.TryGetValue(System.Windows.Markup.XmlLanguage.GetLanguage("en-US"), out var english)
                    ? english
                    : family.Source;
                if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name)) names.Add(name);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not read the installed fonts: {ex.Message}");
        }

        names.Sort(StringComparer.CurrentCultureIgnoreCase);
        return names;
    }

    
    
    
    
    public static string? Import(string sourceFile, string? targetDirectory = null)
    {
        try
        {
            if (!File.Exists(sourceFile)) return null;

            var directory = targetDirectory ?? AppPaths.FontDir;
            Directory.CreateDirectory(directory);

            var target = Path.Combine(directory, Path.GetFileName(sourceFile));
            if (!string.Equals(Path.GetFullPath(sourceFile), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                File.Copy(sourceFile, target, overwrite: true);

            LoadPrivate(target);
            return FamilyNameOf(target);
        }
        catch (Exception ex)
        {
            Log.Warn($"The font {Path.GetFileName(sourceFile)} could not be loaded: {ex.Message}");
            return null;
        }
    }

    
    public static string? FamilyNameOf(string file)
    {
        try
        {
            var families = Fonts.GetFontFamilies(new Uri(Path.GetFullPath(file), UriKind.Absolute));
            foreach (var family in families)
            {
                var name = family.FamilyNames.TryGetValue(System.Windows.Markup.XmlLanguage.GetLanguage("en-US"), out var english)
                    ? english
                    : family.Source;
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not read the family name of {Path.GetFileName(file)}: {ex.Message}");
        }

        return null;
    }

    
    public static void LoadOwnFonts(LauncherSettings settings)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(AppPaths.FontDir, "*.*")
                         .Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
                                     f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)))
            {
                LoadPrivate(file);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not load the font folder: {ex.Message}");
        }

        if (!string.IsNullOrWhiteSpace(settings.UiFontFile) && File.Exists(settings.UiFontFile))
            LoadPrivate(settings.UiFontFile!);
    }

    
    public static FontFamily Resolve(LauncherSettings settings)
    {
        var name = string.IsNullOrWhiteSpace(settings.UiFontFamily) ? DefaultFamily : settings.UiFontFamily!;
        var family = new FontFamily(name);

        
        var known = InstalledFamilies().Contains(name, StringComparer.CurrentCultureIgnoreCase) ||
                    LoadedFiles.Any(file => string.Equals(FamilyNameOf(file), name, StringComparison.CurrentCultureIgnoreCase));

        return known ? family : new FontFamily(DefaultFamily);
    }

    
    public static void Apply(LauncherSettings settings)
    {
        if (Application.Current is null) return;
        Application.Current.Resources["UiFont"] = Resolve(settings);
    }

    
    public static string Label(LauncherSettings settings)
        => string.IsNullOrWhiteSpace(settings.UiFontFamily) ? DefaultLabel : settings.UiFontFamily!;

    private static void LoadPrivate(string file)
    {
        var full = Path.GetFullPath(file);
        if (LoadedFiles.Contains(full, StringComparer.OrdinalIgnoreCase)) return;

        var added = AddFontResourceEx(full, FrPrivate, IntPtr.Zero);
        if (added > 0) LoadedFiles.Add(full);
    }

    
    public static void Release()
    {
        foreach (var file in LoadedFiles.ToList())
        {
            try
            {
                RemoveFontResourceEx(file, FrPrivate, IntPtr.Zero);
            }
            catch
            {
                
            }
        }

        LoadedFiles.Clear();
    }
}
