namespace AetherLauncher.Services;

/// <summary>Kurze relative Zeitangaben für Zeilen wie "2 d ago".</summary>
public static class Time
{
    public static string Ago(DateTimeOffset when)
    {
        var span = DateTimeOffset.Now - when;
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;

        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours} h ago";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays} d ago";
        if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30)} mo ago";
        return $"{(int)(span.TotalDays / 365)} y ago";
    }
}
