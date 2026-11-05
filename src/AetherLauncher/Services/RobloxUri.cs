using System.Text;

namespace AetherLauncher.Services;












public static class RobloxUri
{
    private const string PlaceLauncherBase = "https://assetgame.roblox.com/game/PlaceLauncher.ashx";

    
    
    
    
    public static string BuildPlayUri(long placeId, string? accessCode, string authTicket, string channel, long browserTrackerId, string? jobId = null)
    {
        var specificServer = !string.IsNullOrWhiteSpace(jobId) && string.IsNullOrWhiteSpace(accessCode);

        var placeLauncher = new StringBuilder(PlaceLauncherBase)
            .Append(specificServer ? "?request=RequestGameJob" : "?request=RequestGame")
            .Append("&browserTrackerId=").Append(browserTrackerId)
            .Append("&placeId=").Append(placeId);

        if (!string.IsNullOrWhiteSpace(accessCode))
            placeLauncher.Append("&accessCode=").Append(Uri.EscapeDataString(accessCode.Trim()));
        else if (specificServer)
            placeLauncher.Append("&gameId=").Append(Uri.EscapeDataString(jobId!.Trim()));

        placeLauncher.Append("&isPlayTogetherGame=false");

        var uri = new StringBuilder("roblox-player:1")
            .Append("+launchmode:play")
            .Append("+gameinfo:").Append(authTicket)
            .Append("+launchtime:").Append(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            .Append("+placelauncherurl:").Append(Uri.EscapeDataString(placeLauncher.ToString()))
            .Append("+browsertrackerid:").Append(browserTrackerId)
            .Append("+robloxLocale:en_us")
            .Append("+gameLocale:en_us");

        uri.Append("+channel:").Append(string.Equals(channel, "production", StringComparison.OrdinalIgnoreCase) ? "" : channel);

        return uri.ToString();
    }

    
    
    
    
    public static string BuildAppUri(string? authTicket, string channel, long browserTrackerId)
    {
        var uri = new StringBuilder("roblox-player:1")
            .Append("+launchmode:app")
            .Append("+gameinfo:").Append(authTicket ?? "")
            .Append("+launchtime:").Append(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            .Append("+browsertrackerid:").Append(browserTrackerId)
            .Append("+robloxLocale:en_us")
            .Append("+gameLocale:en_us");

        uri.Append("+channel:").Append(string.Equals(channel, "production", StringComparison.OrdinalIgnoreCase) ? "" : channel);

        return uri.ToString();
    }

    
    public static string BuildDeepLink(long placeId, string? accessCode)
    {
        var uri = new StringBuilder("roblox://experiences/start?placeId=").Append(placeId);
        if (!string.IsNullOrWhiteSpace(accessCode))
            uri.Append("&accessCode=").Append(Uri.EscapeDataString(accessCode.Trim()));
        return uri.ToString();
    }

    
    
    
    
    
    public static bool TryParsePlace(string? input, out long placeId, out string? accessCode)
    {
        placeId = 0;
        accessCode = null;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var text = input.Trim().Trim('"');

        if (long.TryParse(text, out var bare) && bare > 0)
        {
            placeId = bare;
            return true;
        }

        
        var encodedIndex = text.IndexOf("placelauncherurl:", StringComparison.OrdinalIgnoreCase);
        if (encodedIndex >= 0)
        {
            var rest = text[(encodedIndex + "placelauncherurl:".Length)..];
            var end = rest.IndexOf('+');
            var encoded = end >= 0 ? rest[..end] : rest;
            text = Uri.UnescapeDataString(encoded);
        }

        if (!long.TryParse(ReadRawQueryValue(text, "placeId"), out placeId) || placeId <= 0)
        {
            
            var match = System.Text.RegularExpressions.Regex.Match(text, @"/games/(\d+)");
            placeId = match.Success && long.TryParse(match.Groups[1].Value, out var fromPath) ? fromPath : 0;
        }

        if (placeId <= 0) return false;

        
        var linkCode = ReadRawQueryValue(text, "privateServerLinkCode");
        accessCode = linkCode.Length > 0 ? linkCode : ReadRawQueryValue(text, "accessCode");

        return true;
    }

    private static string ReadRawQueryValue(string text, string key)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            text, $@"[?&]{System.Text.RegularExpressions.Regex.Escape(key)}=([^&\s""]+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : "";
    }
}
