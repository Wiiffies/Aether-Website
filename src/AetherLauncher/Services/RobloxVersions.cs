using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AetherLauncher.Services;

/// <summary>Ein Roblox-Build, den man herunterladen kann.</summary>
public sealed record RobloxBuild(string Guid, string NumericVersion, DateTimeOffset? Date, string Source)
{
    public string ShortGuid => Guid.StartsWith("version-", StringComparison.OrdinalIgnoreCase) ? Guid["version-".Length..] : Guid;

    /// <summary>Zeile unter dem Namen: Version, Datum und woher der Eintrag stammt.</summary>
    public string Detail
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(NumericVersion)) parts.Add(NumericVersion);
            if (Date is not null) parts.Add(Date.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture));
            parts.Add(Source);
            return string.Join("  ·  ", parts);
        }
    }
}

/// <summary>
/// Woher die Builds kommen:
///
///  · WEAO (https://docs.weao.xyz) - verfolgt die aktuellen, kommenden und vorherigen
///    Versionen für Windows. Genau die Angaben, die man zum Zurücksetzen (Downgrade)
///    braucht; der Dienst verlangt den User-Agent "WEAO-3PService".
///  · DeployHistory.txt auf setup.rbxcdn.com - die offizielle Liste aller jemals
///    ausgelieferten Builds. Enthält rund tausend Windows-Builds mit Hash und Datum.
///
/// Heruntergeladen wird immer der offizielle Installer des jeweiligen Builds direkt vom
/// Roblox-CDN. Es werden keine fremden Dateien benutzt.
/// </summary>
public static class RobloxVersions
{
    private const string InstallerHost = "https://setup.rbxcdn.com";
    private const string DeployHistoryUrl = InstallerHost + "/DeployHistory.txt";
    private const string WeaoHost = "https://weao.xyz";

    /// <summary>Der von WEAO geforderte User-Agent; ohne ihn antwortet der Dienst nicht.</summary>
    private const string WEAOUserAgentName = "WEAO-3PService";

    private static readonly Regex DeployLine = new(
        @"^New WindowsPlayer version-(?<hash>[0-9a-f]{16}) at (?<date>.+?), file version: (?<file>.+?), git hash:",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly object CacheLock = new();
    private static List<RobloxBuild> _cache = new();

    public static IReadOnlyList<RobloxBuild> Cached
    {
        get
        {
            lock (CacheLock) return _cache;
        }
    }

    /// <summary>
    /// Sammelt alle bekannten Builds: die drei von WEAO verfolgten plus die Liste aus der
    /// offiziellen DeployHistory. Neueste zuerst.
    /// </summary>
    public static async Task<List<RobloxBuild>> GetAvailableAsync(CancellationToken ct = default)
    {
        var builds = new List<RobloxBuild>();

        builds.AddRange(await GetWeaoAsync(ct).ConfigureAwait(false));
        builds.AddRange(await GetDeployHistoryAsync(ct).ConfigureAwait(false));

        var result = builds
            .GroupBy(b => b.Guid, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(b => SourceRank(b.Source)).First())
            .OrderByDescending(b => b.Date ?? DateTimeOffset.MinValue)
            .ThenBy(b => SourceRank(b.Source))
            .ThenBy(b => b.Guid, StringComparer.OrdinalIgnoreCase)
            .ToList();

        lock (CacheLock) _cache = result;
        return result;
    }

    /// <summary>Die von WEAO verfolgten Versionen: aktuell, als Nächstes und die vorherige.</summary>
    public static async Task<List<RobloxBuild>> GetWeaoAsync(CancellationToken ct = default)
    {
        var builds = new List<RobloxBuild>();
        foreach (var (path, label) in new[] { ("current", "WEAO · current"), ("future", "WEAO · next"), ("past", "WEAO · previous") })
        {
            try
            {
                using var client = WeaoClient();
                using var response = await client.GetAsync($"{WeaoHost}/api/versions/{path}", ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    Log.Warn($"WEAO answered {path} with {(int)response.StatusCode}.");
                    continue;
                }

                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var document = JsonDocument.Parse(json);

                if (!document.RootElement.TryGetProperty("Windows", out var guid) || guid.GetString() is not { Length: > 0 } version)
                    continue;

                var numeric = "";
                if (document.RootElement.TryGetProperty("WindowsResponse", out var meta) &&
                    meta.TryGetProperty("version", out var number))
                    numeric = number.GetString() ?? "";

                DateTimeOffset? date = null;
                if (document.RootElement.TryGetProperty("WindowsDate", out var stamp))
                    date = ParseTimestamp(stamp.GetString());

                builds.Add(new RobloxBuild(version, numeric, date, label));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Warn($"WEAO ({path}) could not be read: {ex.Message}");
            }
        }

        Log.Info($"WEAO delivered {builds.Count} tracked Windows version(s).");
        return builds;
    }

    /// <summary>Alle Windows-Builds aus der offiziellen DeployHistory (ohne die versteckten).</summary>
    public static async Task<List<RobloxBuild>> GetDeployHistoryAsync(CancellationToken ct = default)
    {
        var builds = new List<RobloxBuild>();
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            var text = await client.GetStringAsync(DeployHistoryUrl, ct).ConfigureAwait(false);

            foreach (var line in text.Split('\n'))
            {
                var match = DeployLine.Match(line.Trim());
                if (!match.Success) continue;

                var file = match.Groups["file"].Value.Trim();
                var numeric = string.Join('.', file.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

                var date = ParseTimestamp(match.Groups["date"].Value);

                builds.Add(new RobloxBuild("version-" + match.Groups["hash"].Value, numeric, date, "Roblox deploy history"));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"The Roblox deploy history could not be read: {ex.Message}");
        }

        Log.Info($"The deploy history delivered {builds.Count} download version(s).");
        return builds;
    }

    /// <summary>Die offizielle Installer-Adresse eines Builds.</summary>
    public static string InstallerUrl(string guid, string? channel = null)
    {
        var useChannel = !string.IsNullOrWhiteSpace(channel) &&
                         !string.Equals(channel, "production", StringComparison.OrdinalIgnoreCase);

        return useChannel
            ? $"{InstallerHost}/channel/{channel!.Trim()}/{guid}-RobloxPlayerInstaller.exe"
            : $"{InstallerHost}/{guid}-RobloxPlayerInstaller.exe";
    }

    /// <summary>
    /// Lädt den Installer eines Builds herunter. Der Fortschritt läuft in Prozent über
    /// <paramref name="progress"/> (null erlaubt).
    /// </summary>
    public static async Task<bool> DownloadInstallerAsync(string guid, string targetFile, string? channel,
        IProgress<double>? progress, CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);

            using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            using var response = await client.GetAsync(InstallerUrl(guid, channel), HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                Log.Warn($"The installer for {guid} answered {(int)response.StatusCode}.");
                return false;
            }

            var total = response.Content.Headers.ContentLength ?? 0;
            await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var target = File.Create(targetFile);

            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                if (total > 0) progress?.Report(done * 100.0 / total);
            }

            Log.Info($"Installer for {guid} downloaded ({done / 1024 / 1024} MB).");
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"The installer for {guid} could not be downloaded: {ex.Message}");
            return false;
        }
    }

    private static HttpClient WeaoClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(WEAOUserAgentName);
        return client;
    }

    private static int SourceRank(string source) => source.StartsWith("WEAO", StringComparison.OrdinalIgnoreCase) ? 0 : 1;

    /// <summary>
    /// Zeitstempel wie "10/27/2026, 4:01:04 PM UTC" und "3/11/2026 2:06:34 PM".
    /// Das anhängende " UTC" versteht die Standardanalyse nicht - deshalb wird es vorher entfernt.
    /// </summary>
    internal static DateTimeOffset? ParseTimestamp(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var cleaned = text.Replace("UTC", "", StringComparison.OrdinalIgnoreCase).Trim().TrimEnd(',').Trim();
        return DateTime.TryParse(cleaned, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? new DateTimeOffset(parsed, TimeSpan.Zero)
            : null;
    }
}
