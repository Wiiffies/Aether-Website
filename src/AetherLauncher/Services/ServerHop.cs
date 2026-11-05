using AetherLauncher.Models;

namespace AetherLauncher.Services;


public sealed record HopResult(bool Success, string Message);










public static class ServerHop
{
    
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(20);

    
    private static readonly TimeSpan WindowGrace = TimeSpan.FromSeconds(12);

    
    private static readonly TimeSpan JoinGrace = TimeSpan.FromSeconds(30);

    
    public static HashSet<int> FreshIds(IEnumerable<int> before, IEnumerable<int> after)
    {
        var known = before as ISet<int> ?? new HashSet<int>(before);
        return after.Where(id => !known.Contains(id)).ToHashSet();
    }

    
    
    
    
    
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

    
    public static async Task<HopResult> HopAsync(
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

        RobloxApi.PublicServer? target = null;

        
        
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

        
        
        return await HandoffAsync(settings, accounts, place, null, null, rejoin: true, stage, ct)
            .ConfigureAwait(false);
    }

    
    
    
    
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
                $"The old client left, but the new one couldn't start: {launch.Message}");

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

        
        GameOverlay.MoveWindowTo(fresh.Value, old.Left, old.Top, old.Width, old.Height);
        GameOverlay.ActivateProcess(fresh.Value);

        stage("Waiting until the new client is in the game…");
        var joined = WaitForJoin(watch, place.PlaceId, targetJobId, JoinGrace, ct);

        
        
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
