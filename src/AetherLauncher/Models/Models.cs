using AetherLauncher.Services;

namespace AetherLauncher.Models;


public sealed class AccountRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public long UserId { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";

    
    public string CookieProtected { get; set; } = "";

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? LastValidatedAt { get; set; }
    public DateTimeOffset? LastLaunchedAt { get; set; }
}


public sealed class PlaceRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public long PlaceId { get; set; }
    public string? AccessCode { get; set; }
    public string Name { get; set; } = "";
    public long? UniverseId { get; set; }
    public int LaunchCount { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? LastPlayedAt { get; set; }

    
    public bool IsPinned { get; set; }

    
    public DateTimeOffset? PinnedAt { get; set; }

    
    
    
    
    public int SortIndex { get; set; }
}






public sealed class RecentRecord
{
    public long PlaceId { get; set; }
    public string? AccessCode { get; set; }
    public string Name { get; set; } = "";
    public long? UniverseId { get; set; }
    public string? AccountName { get; set; }
    public DateTimeOffset PlayedAt { get; set; } = DateTimeOffset.Now;

    
    public int Sessions { get; set; } = 1;

    
    public bool FromLog { get; set; }
}


public sealed class LauncherSettings
{
    
    public string? RobloxRoot { get; set; }

    
    public string Channel { get; set; } = "production";

    
    
    
    
    public string? PinnedVersionGuid { get; set; }

    
    
    
    
    
    public bool VanillaMode { get; set; }

    
    public bool InGameCursorEnabled { get; set; }

    
    public string? InGameCursorPath { get; set; }

    
    public string? UiFontFamily { get; set; }

    
    public string? UiFontFile { get; set; }

    
    public bool PlayfulMode { get; set; } = true;

    
    public string Theme { get; set; } = ThemeManager.DefaultId;

    
    public bool OverlayEnabled { get; set; } = true;

    
    public string OverlayShortcut { get; set; } = "Delete";

    
    
    
    
    public bool GameOverlayEnabled { get; set; } = true;

    
    
    
    
    public bool GameOverlayAutoShow { get; set; }

    
    
    
    
    public int SettingsRevision { get; set; }

    
    public long? LastPlaceId { get; set; }

    
    public string? LastPlaceAccessCode { get; set; }

    
    public string? LastAccountId { get; set; }

    
    public bool CustomCursor { get; set; } = true;

    
    public bool MultiInstance { get; set; } = true;

    
    public bool ApplyFastFlags { get; set; } = true;

    
    public int MultiLaunchStaggerMs { get; set; } = 8000;

    public bool UseCurrentLogin { get; set; }

    
    public string? SavedProtocolHandler { get; set; }

    public bool ProtocolHandlerRegistered { get; set; }

    
    public long BrowserTrackerId { get; set; } = Random.Shared.NextInt64(100000, 999999999);
}
