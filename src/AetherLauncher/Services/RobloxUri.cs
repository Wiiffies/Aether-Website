using System.Text;

namespace AetherLauncher.Services;

/// <summary>
/// Baut und liest die Start-URIs des Roblox-Clients.
///
/// Start als bestimmtes Konto:
///   roblox-player:1+launchmode:play+gameinfo:&lt;auth-ticket&gt;+launchtime:&lt;ms&gt;
///                  +placelauncherurl:&lt;url-encoded PlaceLauncher.ashx&gt;+browsertrackerid:&lt;id&gt;
///                  +robloxLocale:en_us+gameLocale:en_us+channel:&lt;kanal&gt;
/// Ohne Konto wird der dokumentierte Deep-Link benutzt, damit sich der Client mit der
/// bereits vorhandenen Roblox-Anmeldung verbindet:
///   roblox://experiences/start?placeId=1818
/// </summary>
public static class RobloxUri
{
    private const string PlaceLauncherBase = "https://assetgame.roblox.com/game/PlaceLauncher.ashx";

    /// <param name="jobId">
    /// Konkreter Server (Job-ID). Ist er gesetzt, landet der Client genau dort - sonst
    /// entscheidet Roblox, welcher Server genommen wird. Für private Server bleibt er leer.
    /// </param>
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

    /// <summary>
    /// Startet die Roblox-Anwendung selbst (Startseite) statt eines Spiels.
    /// Der Modus heißt "app"; mit Ticket wird dabei das gewählte Konto angemeldet.
    /// </summary>
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

    /// <summary>Deep-Link für den Start ohne eigenes Konto (nutzt die vorhandene Roblox-Anmeldung).</summary>
    public static string BuildDeepLink(long placeId, string? accessCode)
    {
        var uri = new StringBuilder("roblox://experiences/start?placeId=").Append(placeId);
        if (!string.IsNullOrWhiteSpace(accessCode))
            uri.Append("&accessCode=").Append(Uri.EscapeDataString(accessCode.Trim()));
        return uri.ToString();
    }

    /// <summary>
    /// Nimmt alles entgegen, was ein Benutzer einfügen kann: reine Place-ID, eine
    /// roblox.com/games-URL (inkl. privateServerLinkCode), einen roblox:// Deep-Link
    /// oder eine vollständige roblox-player URI aus einer externen Startanfrage.
    /// </summary>
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

        // roblox-player URI: die Place-ID steckt in der kodierten placelauncherurl.
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
            // /games/<id>/... oder /games/<id>
            var match = System.Text.RegularExpressions.Regex.Match(text, @"/games/(\d+)");
            placeId = match.Success && long.TryParse(match.Groups[1].Value, out var fromPath) ? fromPath : 0;
        }

        if (placeId <= 0) return false;

        // privateServerLinkCode aus dem Browser-Link ist genau der accessCode des Clients.
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
