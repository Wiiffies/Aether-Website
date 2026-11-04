using AetherLauncher.Models;

namespace AetherLauncher.Services;

/// <summary>Wie ein Serverwechsel ausgegangen ist.</summary>
public sealed record HopResult(bool Success, string Message);

/// <summary>
/// Serverwechsel und Rejoin als Makro: der laufende Client wird zuerst geschlossen, danach
/// startet sofort der neue Join. Roblox hat keine Schnittstelle, mit der ein laufender Client
/// von außen auf einen anderen Server umziehen kann - es geht nur ein neuer Beitritt.
///
/// Das ist bewusst der schnelle, sichtbare Weg: der alte Client ist weg, Roblox startet neu,
/// und der neue Client übernimmt die Fensterlage des alten. "Restart client" bleibt der
/// getrennte harte Neustart für neue FastFlags.
/// </summary>
public static class ServerHop
{
    /// <summary>Wie lange auf den neuen Client-Prozess gewartet wird.</summary>
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Wie lange ein neuer Client Zeit hat, sein Fenster zu zeigen.</summary>
    private static readonly TimeSpan WindowGrace = TimeSpan.FromSeconds(12);

    /// <summary>Zeit für den neuen Client, bis er wirklich im Spiel ist.</summary>
    private static readonly TimeSpan JoinGrace = TimeSpan.FromSeconds(30);

    /// <summary>Prozesse, die nach dem Start neu sind - also der Client mit dem neuen Server.</summary>
    public static HashSet<int> FreshIds(IEnumerable<int> before, IEnumerable<int> after)
    {
        var known = before as ISet<int> ?? new HashSet<int>(before);
        return after.Where(id => !known.Contains(id)).ToHashSet();
    }

    /// <summary>
    /// Wählt einen spielbaren Server. Volle Server fallen raus; der aktuelle Server wird
    /// übersprungen, damit ein Wechsel wirklich woanders landet. Unter den übrigen wird
    /// zufällig gewählt.
    /// </summary>
    internal static RobloxApi.PublicServer? PickServer(
        IReadOnlyList<RobloxApi.PublicServer> servers,
        string? avoidJobId = null)
    {
        if (servers.Count == 0) return null;

        var playable = servers
            .Where(server => !string.IsNullOrWhiteSpace(server.JobId))
            .Where(server => server.MaxPlayers <= 0 || server.Playing < server.MaxPlayers)
            .Where(server => string.IsNullOrWhiteSpace(avoidJobId)
                             || !string.Equals(server.JobId, avoidJobId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return playable.Count == 0 ? null : playable[Random.Shared.Next(playable.Count)];
    }

    /// <summary>Wechselt auf einen anderen öffentlichen Server desselben Platzes.</summary>
    public static async Task<HopResult> HopAsync(
        LauncherSettings settings,
        AccountStore accounts,
        Action<string> stage,
        CancellationToken ct = default)
    {
        // Der Platz kommt aus Roblox' eigenem Log: so landet der Wechsel auch in dem Spiel,
        // das über Roblox' Startseite geöffnet wurde, nicht in dem zuletzt über Aether
        // gestarteten. Die Job-ID verhindert, dass derselbe Server noch einmal gewählt wird.
        var current = RobloxLogImport.LastJoin();
        var placeId = current?.PlaceId ?? settings.LastPlaceId;
        if (placeId is not { } id || id <= 0)
            return new HopResult(false, "No game started through Aether yet - start one in the launcher first.");

        var place = new PlaceRecord
        {
            PlaceId = id,
            AccessCode = settings.LastPlaceId == id ? settings.LastPlaceAccessCode : null,
        };

        RobloxApi.PublicServer? target = null;

        // Für öffentliche Spiele einen echten Zielserver holen. Private Server haben nur
        // einen Server - dort wäre eine Job-ID falsch.
        if (string.IsNullOrWhiteSpace(place.AccessCode))
        {
            stage("Looking for a different server…");

            try
            {
                var servers = await RobloxApi.GetPublicServersAsync(id, ct).ConfigureAwait(false);
                target = PickServer(servers, current?.JobId);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warn($"The public server list could not be read: {ex.Message}");
            }

            if (target is null)
            {
                return new HopResult(false,
                    "No other public server is open right now, so you were not disconnected. Try again in a moment.");
            }
        }

        stage(target is null
            ? "Leaving and rejoining the private server…"
            : $"Leaving and joining a server with {target.Playing}/{Math.Max(target.MaxPlayers, target.Playing)} players…");

        return await HandoffAsync(
            settings, accounts, place, target?.JobId,
            target is null ? null : $"{target.Playing}/{Math.Max(target.MaxPlayers, target.Playing)} players",
            rejoin: target is null, stage, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Verlässt das Spiel und tritt demselben Platz wieder bei - für neue FastFlags. Der
    /// Client geht zuerst, der neue Join startet sofort danach; ein anderer Server wird
    /// nicht gesucht.
    /// </summary>
    public static async Task<HopResult> RejoinAsync(
        LauncherSettings settings,
        AccountStore accounts,
        Action<string> stage,
        CancellationToken ct = default)
    {
        var current = RobloxLogImport.LastJoin();
        var placeId = current?.PlaceId ?? settings.LastPlaceId;
        if (placeId is not { } id || id <= 0)
            return new HopResult(false, "No game started through Aether yet - start one in the launcher first.");

        var place = new PlaceRecord
        {
            PlaceId = id,
            AccessCode = settings.LastPlaceId == id ? settings.LastPlaceAccessCode : null,
        };

        stage("Leaving and rejoining the place…");

        // Kein Zielserver: Roblox nimmt den nächsten freien. Für neue Flags ist der Server
        // ohnehin egal.
        return await HandoffAsync(settings, accounts, place, null, null, rejoin: true, stage, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Schließt zuerst den laufenden Client und startet direkt danach den neuen Join.
    /// Scheitert der Start, ist das ehrlich gesagt: der alte Client ist dann bereits weg.
    /// </summary>
    private static async Task<HopResult> HandoffAsync(
        LauncherSettings settings,
        AccountStore accounts,
        PlaceRecord place,
        string? targetJobId,
        string? targetDescription,
        bool rejoin,
        Action<string> stage,
        CancellationToken ct)
    {
        var oldWindow = GameOverlay.FindWindow();
        if (oldWindow is not { } old)
            return new HopResult(false, "No running Roblox client was found - start the game first.");

        var oldProcessId = GameOverlay.ProcessIdOf(old.Handle);
        if (oldProcessId <= 0)
            return new HopResult(false, "The running Roblox client could not be identified, so nothing was closed.");

        var before = GameOverlay.ClientProcessIds();
        var watch = RobloxLogImport.Watch();

        // Ein zweiter Client braucht den Singleton-Halter - auch wenn der Schalter für
        // Multi-Instanz aus ist. Ohne ihn schluckt Roblox den Start.
        MultiInstance.EnsureWatcher();

        var account = accounts.Accounts.FirstOrDefault(a => a.Id == settings.LastAccountId);
        var launcher = new LaunchService(settings, accounts);

        stage("Leaving the server…");
        var closed = await Task.Run(
            () => GameOverlay.CloseProcesses(new[] { oldProcessId }, TimeSpan.FromSeconds(3)), ct)
            .ConfigureAwait(false);

        if (!closed)
            return new HopResult(false, "The running client did not close, so the new join was not started.");

        stage("Starting the new client…");
        var launch = await launcher.LaunchPlaceAsync(place, account, ct, targetJobId).ConfigureAwait(false);
        if (!launch.Success)
            return new HopResult(false,
                $"The old client left, but the new one could not start: {launch.Message}");

        stage("Waiting for the new client…");
        var fresh = WaitForFreshClient(before, StartTimeout, ct);
        if (fresh is null)
            return new HopResult(false,
                "The new client did not start a process. Start the game again from the launcher.");

        stage("Waiting for the new window…");
        var hasWindow = WaitForWindow(fresh.Value, WindowGrace, ct);

        if (!hasWindow)
        {
            var closedNew = GameOverlay.CloseProcesses(new[] { fresh.Value }, TimeSpan.FromSeconds(2));
            return new HopResult(false, closedNew
                ? "The new client never opened a window, so it was closed. Start the game again."
                : "The new client never opened a window. Start the game again.");
        }

        // Fensterlage des alten übernehmen: der Wechsel soll nicht wie ein Neustart aussehen.
        GameOverlay.MoveWindowTo(fresh.Value, old.Left, old.Top, old.Width, old.Height);
        GameOverlay.ActivateProcess(fresh.Value);

        stage("Waiting until the new client is in the game…");
        var joined = WaitForJoin(watch, place.PlaceId, targetJobId, JoinGrace, ct);

        // Roblox hat den Wunschserver nicht angenommen und einen anderen genommen? Auch das
        // ist ein erfolgreicher Beitritt - nur eben nicht auf der gewünschten Job-ID.
        if (!joined && targetJobId is not null)
            joined = WaitForJoin(watch, place.PlaceId, null, TimeSpan.FromSeconds(6), ct);

        var where = targetDescription is null ? "" : $" ({targetDescription})";

        if (!joined)
            return new HopResult(true,
                $"Left the server and started a new client{where}. The join was not confirmed yet - check the game.");

        return new HopResult(true, rejoin
            ? "Left and rejoined the place - the new flags are in."
            : $"Left the server and joined a fresh one{where}.");
    }

    private static int? WaitForFreshClient(HashSet<int> before, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var fresh = FreshIds(before, GameOverlay.ClientProcessIds());
            if (fresh.Count > 0) return fresh.Min();

            ct.WaitHandle.WaitOne(250);
        }

        return null;
    }

    private static bool WaitForWindow(int processId, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            if (GameOverlay.HasWindow(processId)) return true;
            ct.WaitHandle.WaitOne(200);
        }

        return false;
    }

    /// <summary>Wartet auf einen neuen Beitritt in Roblox' Log - der Beweis, dass der Join saß.</summary>
    private static bool WaitForJoin(
        RobloxLogImport.JoinWatch watch,
        long placeId,
        string? jobId,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            if (watch.SawJoin(placeId, jobId)) return true;
            ct.WaitHandle.WaitOne(350);
        }

        return false;
    }
}
