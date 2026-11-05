using AetherLauncher.Infrastructure;

namespace AetherLauncher.Services;


public sealed class OverlaySpot
{
    public double Left { get; set; }
    public double Top { get; set; }

    
    public bool Visible { get; set; } = true;
}









public sealed class OverlayState
{
    
    
    
    
    public const int CurrentLayoutVersion = 2;

    public int LayoutVersion { get; set; }

    
    public Dictionary<string, OverlaySpot> Panels { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    
    
    
    
    
    public bool HideOverOtherApps { get; set; } = true;

    
    public int PanelOpacity { get; set; } = 100;

    public bool AntiAfkEnabled { get; set; }
    public int AntiAfkIntervalSeconds { get; set; } = AntiAfk.DefaultIntervalSeconds;
    public string AntiAfkKey { get; set; } = AntiAfk.DefaultKey;

    
    public int AntiAfkVersion { get; set; }
}


public static class OverlayPlacement
{
    public static OverlayState Load(string? path = null)
    {
        var state = JsonFile.Load(path ?? AppPaths.OverlayPlacementFile, () => new OverlayState());

        foreach (var (key, spot) in state.Panels.ToList())
        {
            
            if (double.IsNaN(spot.Left) || double.IsNaN(spot.Top) ||
                double.IsInfinity(spot.Left) || double.IsInfinity(spot.Top))
                state.Panels.Remove(key);
        }

        if (!AntiAfk.ValidInterval(state.AntiAfkIntervalSeconds))
            state.AntiAfkIntervalSeconds = AntiAfk.DefaultIntervalSeconds;

        
        
        if (state.AntiAfkVersion < 1)
        {
            if (string.Equals(state.AntiAfkKey, "LeftShift", StringComparison.OrdinalIgnoreCase))
                state.AntiAfkKey = AntiAfk.DefaultKey;

            state.AntiAfkVersion = 1;
        }

        state.PanelOpacity = Math.Clamp(state.PanelOpacity, 55, 100);

        return state;
    }

    public static bool Save(OverlayState state, string? path = null) =>
        JsonFile.Save(path ?? AppPaths.OverlayPlacementFile, state);

    
    public static bool Clear(string? path = null)
    {
        try
        {
            var file = path ?? AppPaths.OverlayPlacementFile;
            if (File.Exists(file)) File.Delete(file);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"The saved overlay layout could not be removed: {ex.Message}");
            return false;
        }
    }
}
