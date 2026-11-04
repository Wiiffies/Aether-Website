using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AetherLauncher.Infrastructure;

namespace AetherLauncher.Services;

/// <summary>
/// Zugriffe auf die Roblox-Web-APIs: Cookie prüfen, Auth-Ticket holen, Avatare und Spieldaten laden.
/// Es wird ausschließlich das offizielle Web-API benutzt - kein Client-Patching, kein Reverse Engineering.
/// </summary>
public static class RobloxApi
{
    private const string UsersAuthenticatedUrl = "https://users.roblox.com/v1/users/authenticated";
    private const string AccountJsonUrl = "https://www.roblox.com/my/account/json";
    private const string AuthTicketUrl = "https://auth.roblox.com/v1/authentication-ticket/";

    private static readonly HttpClient Http = CreateClient();

    private static string? _csrfToken;

    public sealed record AccountInfo(long UserId, string Username, string DisplayName);

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            UseCookies = false, // Cookies werden bewusst pro Anfrage gesetzt.
            AutomaticDecompression = DecompressionMethods.All,
        };

        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        return client;
    }

    /// <summary>Prüft ein Cookie und liefert Benutzer-ID, Benutzername und Anzeigename.</summary>
    public static async Task<AccountInfo?> ValidateCookieAsync(string cookie, CancellationToken ct = default)
    {
        var info = await GetJsonAsync<AccountInfoDto>(UsersAuthenticatedUrl, cookie, ct).ConfigureAwait(false);
        if (info is { Id: > 0 })
            return new AccountInfo(info.Id, info.Name ?? "", string.IsNullOrWhiteSpace(info.DisplayName) ? info.Name ?? "" : info.DisplayName);

        // Älterer Endpunkt als Ausweichlösung.
        var legacy = await GetJsonAsync<AccountJsonDto>(AccountJsonUrl, cookie, ct).ConfigureAwait(false);
        if (legacy is { UserId: > 0 })
            return new AccountInfo(legacy.UserId, legacy.Name ?? "", legacy.Name ?? "");

        return null;
    }

    /// <summary>
    /// Holt ein kurzlebiges Auth-Ticket für den Start als dieses Konto.
    /// Ablauf: POST ohne Token -> 403 mit x-csrf-token -> POST mit Token -> Antwort-Header
    /// "rbx-authentication-ticket". Das Ticket wandert als gameinfo in die roblox-player URI.
    /// </summary>
    public static async Task<string?> GetAuthTicketAsync(string cookie, CancellationToken ct = default)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            using var request = new HttpRequestMessage(HttpMethod.Post, AuthTicketUrl);
            request.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={cookie}");
            request.Headers.TryAddWithoutValidation("Referer", "https://www.roblox.com/");
            request.Headers.TryAddWithoutValidation("Origin", "https://www.roblox.com");

            if (!string.IsNullOrEmpty(_csrfToken))
                request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", _csrfToken);

            // Roblox lehnt seit einer Änderung Anfragen ohne Content-Type mit HTTP 415 ab.
            if (attempt > 0)
                request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);

            if ((int)response.StatusCode == 403 && TryReadHeader(response, "x-csrf-token", out var token))
            {
                _csrfToken = token;
                continue;
            }

            if (response.IsSuccessStatusCode && TryReadHeader(response, "rbx-authentication-ticket", out var ticket))
            {
                Log.Info("Auth ticket received.");
                return ticket;
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized)
            {
                Log.Warn("This account's key is invalid or was signed out.");
                return null;
            }

            Log.Error($"Auth ticket rejected: HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
            return null;
        }

        Log.Error("Could not request an auth ticket (CSRF exchange failed).");
        return null;
    }

    /// <summary>Ein öffentlicher Server eines Platzes - die ID ist die Job-ID für den Join.</summary>
    public sealed record PublicServer(string JobId, int Playing, int MaxPlayers);

    /// <summary>
    /// Öffentliche Server eines Platzes. Wird für den Serverwechsel gebraucht, damit der
    /// neue Client gezielt auf einen anderen Server geht statt auf irgendeinen - und damit
    /// man vorher sieht, wie voll er ist. Schlägt der Abruf fehl, bleibt es beim normalen
    /// Start; der Wechsel funktioniert dann trotzdem.
    /// </summary>
    public static async Task<IReadOnlyList<PublicServer>> GetPublicServersAsync(long placeId, CancellationToken ct = default)
    {
        if (placeId <= 0) return Array.Empty<PublicServer>();

        // excludeFullGames ist Pflicht: ohne den Parameter antwortet der Endpunkt mit 400.
        var dto = await GetJsonAsync<ServersDto>(
            $"https://games.roblox.com/v1/games/{placeId}/servers/Public?limit=100&excludeFullGames=true",
            null, ct).ConfigureAwait(false);

        return dto?.Data is not { Count: > 0 } data
            ? Array.Empty<PublicServer>()
            : data
                .Where(server => !string.IsNullOrWhiteSpace(server.Id))
                .Select(server => new PublicServer(server.Id!, server.Playing, server.MaxPlayers))
                .ToList();
    }

    /// <summary>Lädt das runde Profilbild eines Kontos.</summary>
    public static async Task<byte[]?> DownloadAvatarAsync(long userId, CancellationToken ct = default)
    {
        var url = $"https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds={userId}&size=150x150&format=Png&isCircular=true";
        return await DownloadThumbnailAsync(url, ct).ConfigureAwait(false);
    }

    /// <summary>Ermittelt zur Place-ID die Universe-ID und den Spielnamen.</summary>
    public static async Task<(long UniverseId, string Name)?> ResolveGameAsync(long placeId, CancellationToken ct = default)
    {
        var universe = await GetJsonAsync<UniverseDto>($"https://apis.roblox.com/universes/v1/places/{placeId}/universe", null, ct)
            .ConfigureAwait(false);
        if (universe is not { UniverseId: > 0 }) return null;

        var details = await GetJsonAsync<GamesDto>($"https://games.roblox.com/v1/games?universeIds={universe.UniverseId}", null, ct)
            .ConfigureAwait(false);
        var name = details?.Data?.FirstOrDefault()?.Name;

        return (universe.UniverseId, string.IsNullOrWhiteSpace(name) ? $"Place {placeId}" : name!);
    }

    /// <summary>Lädt das Spiel-Icon zu einer Universe-ID.</summary>
    public static async Task<byte[]?> DownloadGameIconAsync(long universeId, CancellationToken ct = default)
    {
        var url = $"https://thumbnails.roblox.com/v1/games/icons?universeIds={universeId}&returnPolicy=PlaceHolder&size=150x150&format=Png&isCircular=false";
        return await DownloadThumbnailAsync(url, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Lädt ein Vorschaubild. Roblox rendert Bilder beim ersten Abruf oft erst noch
    /// ("Pending"), deshalb wird ein paar Mal nachgefragt, bevor aufgegeben wird.
    /// </summary>
    private static async Task<byte[]?> DownloadThumbnailAsync(string url, CancellationToken ct)
    {
        const int attempts = 4;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var document = JsonDocument.Parse(json);
                if (!document.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0) return null;

                var entry = data[0];
                var state = entry.TryGetProperty("state", out var stateElement) ? stateElement.GetString() : null;

                if (state is not "Completed")
                {
                    if (attempt == attempts - 1) return null;
                    await Task.Delay(700, ct).ConfigureAwait(false);
                    continue;
                }

                if (!entry.TryGetProperty("imageUrl", out var imageUrl)) return null;
                var imageUrlValue = imageUrl.GetString();
                if (string.IsNullOrWhiteSpace(imageUrlValue)) return null;

                using var image = await Http.GetAsync(imageUrlValue, ct).ConfigureAwait(false);
                if (!image.IsSuccessStatusCode) return null;

                return await image.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not download an image: {ex.Message}");
                return null;
            }
        }

        return null;
    }

    private static async Task<T?> GetJsonAsync<T>(string url, string? cookie, CancellationToken ct) where T : class
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(cookie))
                request.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={cookie}");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // Nicht stumm scheitern: sonst steht in der Liste dauerhaft "Place 123…"
                // und im Log steht nichts, wonach man suchen könnte.
                Log.Warn($"Request to {new Uri(url).Host} answered {(int)response.StatusCode}.");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<T>(json, JsonFile.Options);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"Request to {new Uri(url).Host} failed: {ex.Message}");
            return null;
        }
    }

    private static bool TryReadHeader(HttpResponseMessage response, string name, out string value)
    {
        value = "";
        if (!response.Headers.TryGetValues(name, out var values)) return false;
        value = values.FirstOrDefault() ?? "";
        return value.Length > 0;
    }

    private sealed class AccountInfoDto
    {
        public long Id { get; set; }
        public string? Name { get; set; }
        public string? DisplayName { get; set; }
    }

    private sealed class AccountJsonDto
    {
        public long UserId { get; set; }
        public string? Name { get; set; }
    }

    private sealed class UniverseDto
    {
        public long UniverseId { get; set; }
    }

    private sealed class GamesDto
    {
        public List<GameDto>? Data { get; set; }
    }

    private sealed class ServersDto
    {
        public List<ServerDto>? Data { get; set; }
    }

    private sealed class ServerDto
    {
        public string? Id { get; set; }
        public int Playing { get; set; }
        public int MaxPlayers { get; set; }
    }

    private sealed class GameDto
    {
        public string? Name { get; set; }
    }
}
