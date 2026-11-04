using System.Diagnostics;
using AetherLauncher.Models;

namespace AetherLauncher.Services;

/// <summary>Ergebnis eines Startversuchs.</summary>
public sealed record LaunchResult(bool Success, string Message, int? ProcessId = null, long? PlaceId = null);

/// <summary>
/// Führt einen Start komplett aus: Version finden, FastFlags übernehmen, Multi-Instanz
/// vorbereiten, ggf. Auth-Ticket besorgen und den Client mit der Start-URI aufrufen.
/// </summary>
public sealed class LaunchService
{
    private readonly LauncherSettings _settings;
    private readonly AccountStore _accounts;

    public LaunchService(LauncherSettings settings, AccountStore accounts)
    {
        _settings = settings;
        _accounts = accounts;
    }

    public RobloxVersion? FindInstall(out string searchedRoots)
        => RobloxInstall.Resolve(_settings, out searchedRoots);

    /// <summary>Startet einen Platz als bestimmtes Konto (oder mit der aktuellen Anmeldung).</summary>
    public async Task<LaunchResult> LaunchPlaceAsync(PlaceRecord place, AccountRecord? account, CancellationToken ct = default, string? jobId = null)
    {
        var version = FindInstall(out var roots);
        if (version is null)
            return new LaunchResult(false, $"No Roblox client found. Looked in: {roots}");

        Prepare(version);

        string uri;
        if (account is not null)
        {
            var cookie = _accounts.GetCookie(account);
            if (string.IsNullOrEmpty(cookie))
                return new LaunchResult(false, $"No valid key stored for {account.Username}.", PlaceId: place.PlaceId);

            var ticket = await RobloxApi.GetAuthTicketAsync(cookie, ct).ConfigureAwait(false);
            if (ticket is null)
                return new LaunchResult(false, $"Signing in as {account.Username} failed - check the key.", PlaceId: place.PlaceId);

            uri = RobloxUri.BuildPlayUri(place.PlaceId, place.AccessCode, ticket, _settings.Channel, _settings.BrowserTrackerId, jobId);
            Log.Info($"Launching place {place.PlaceId} as {account.Username}" +
                     (string.IsNullOrWhiteSpace(jobId) ? "." : $" on server {jobId}."));
        }
        else
        {
            uri = RobloxUri.BuildDeepLink(place.PlaceId, place.AccessCode);
            Log.Info($"Launching place {place.PlaceId} with the current Roblox login.");
        }

        // Ziel fürs Ingame-Overlay merken, bevor der Host startet - er liest settings.json selbst.
        _settings.LastPlaceId = place.PlaceId;
        _settings.LastPlaceAccessCode = place.AccessCode;
        _settings.LastAccountId = account?.Id;
        new SettingsStore().Save(_settings);

        var result = Start(version, uri);
        if (result.Success)
        {
            RecordRecent(place.PlaceId, place.AccessCode, place.Name, place.UniverseId, account);

            if (account is not null)
            {
                account.LastLaunchedAt = DateTimeOffset.Now;
                _accounts.Save();
            }
        }

        return result with { PlaceId = place.PlaceId };
    }

    /// <summary>
    /// Startet die Roblox-Anwendung selbst (Startseite) statt eines Spiels.
    ///
    /// Mit Konto wird der App-Modus der Start-URI mit frischem Auth-Ticket benutzt; ohne Konto
    /// wird der Client ganz ohne Argumente gestartet - genau so öffnet Windows die Roblox-App.
    /// </summary>
    public async Task<LaunchResult> LaunchAppAsync(AccountRecord? account, CancellationToken ct = default)
    {
        var version = FindInstall(out var roots);
        if (version is null)
            return new LaunchResult(false, $"No Roblox client found. Looked in: {roots}");

        Prepare(version);

        if (account is null)
        {
            Log.Info("Opening the Roblox app with the current login.");
            var plain = Start(version, null);
            return plain.Success ? plain with { Message = "Opening the Roblox app…" } : plain;
        }

        var cookie = _accounts.GetCookie(account);
        if (string.IsNullOrEmpty(cookie))
            return new LaunchResult(false, $"No valid key stored for {account.Username}.");

        var ticket = await RobloxApi.GetAuthTicketAsync(cookie, ct).ConfigureAwait(false);
        if (ticket is null)
            return new LaunchResult(false, $"Signing in as {account.Username} failed - check the key.");

        var uri = RobloxUri.BuildAppUri(ticket, _settings.Channel, _settings.BrowserTrackerId);
        Log.Info($"Opening the Roblox app as {account.Username}.");

        var result = Start(version, uri);
        if (result.Success)
        {
            account.LastLaunchedAt = DateTimeOffset.Now;
            _accounts.Save();
        }

        return result.Success ? result with { Message = $"Opening the Roblox app as {account.Username}…" } : result;
    }

    /// <summary>
    /// Startet eine URI, die von außen kommt (Roblox-Website über unseren Protokollhandler).
    /// Die URI wird unverändert weitergegeben - sie enthält bereits das passende Ticket.
    /// </summary>
    public Task<LaunchResult> LaunchExternalUriAsync(string uri, CancellationToken ct = default)
    {
        var version = FindInstall(out var roots);
        if (version is null)
            return Task.FromResult(new LaunchResult(false, $"No Roblox client found. Looked in: {roots}"));

        Prepare(version);

        RobloxUri.TryParsePlace(uri, out var placeId, out var accessCode);

        if (placeId > 0)
        {
            // Die URI bringt ihr eigenes Ticket mit; für einen späteren Server hop merken wir
            // uns nur den Platz - gestartet wird dann über die aktuelle Anmeldung.
            _settings.LastPlaceId = placeId;
            _settings.LastPlaceAccessCode = accessCode;
            _settings.LastAccountId = null;
            new SettingsStore().Save(_settings);
        }

        var result = Start(version, uri);

        // Der Name fehlt hier (die URI bringt nur die ID mit) - die Startseite holt ihn nach.
        if (result.Success && placeId > 0) RecordRecent(placeId, accessCode, null, null, null);

        return Task.FromResult(result with { PlaceId = placeId > 0 ? placeId : null });
    }

    /// <summary>Trägt den Start in die Liste "Recently played" ein (recents.json).</summary>
    private static void RecordRecent(long placeId, string? accessCode, string? name, long? universeId, AccountRecord? account)
    {
        if (placeId <= 0) return;

        try
        {
            var recent = new RecentStore();
            recent.Load();
            recent.Record(placeId, accessCode, name, universeId, account?.Username);
        }
        catch (Exception ex)
        {
            Log.Warn($"The recently played list could not be updated: {ex.Message}");
        }
    }

    /// <summary>
    /// Alles vorbereiten, was der Start braucht. Im Vanilla-Modus wird nichts angefasst:
    /// der Client startet genau so, wie Roblox ihn installiert hat.
    /// </summary>
    internal void Prepare(RobloxVersion version)
    {
        if (_settings.VanillaMode)
        {
            Log.Info($"Vanilla mode: {version.ShortGuid} starts exactly as Roblox installed it - nothing is written, no extra process.");
            return;
        }

        if (_settings.MultiInstance) MultiInstance.EnsureWatcher();

        if (_settings.ApplyFastFlags && FastFlags.Apply(version.Directory, out var message) && message.Length > 0)
            Log.Info(message);

        if (_settings.InGameCursorEnabled)
        {
            ClientMods.ApplySettings(_settings, version.Directory, out var cursorMessage);
            if (cursorMessage.Length > 0) Log.Info(cursorMessage);
        }

        Log.Info($"Prepared {version.ShortGuid} for launch.");
    }

    private LaunchResult Start(RobloxVersion version, string? uri)
    {
        try
        {
            var startInfo = new ProcessStartInfo(version.PlayerExe)
            {
                UseShellExecute = false,
                WorkingDirectory = version.Directory,
            };

            // Als einziges Argument - genau so ruft Windows den Client als Protokollhandler auf.
            // Ohne Argument startet der Client seine eigene Startseite.
            if (!string.IsNullOrEmpty(uri)) startInfo.ArgumentList.Add(uri);

            using var process = Process.Start(startInfo);
            Log.Info($"Roblox client started (version {version.ShortGuid}).");

            // Das Overlay läuft als eigener Prozess weiter, auch wenn der Launcher zugeht.
            if (_settings.GameOverlayEnabled) GameOverlay.EnsureRunning();

            return new LaunchResult(true, "Starting Roblox…", process?.Id);
        }
        catch (Exception ex)
        {
            Log.Error($"Client could not be started: {ex.Message}");
            return new LaunchResult(false, $"Client could not be started: {ex.Message}");
        }
    }
}
