using System.Net.Http;
using Microsoft.Win32;
using AetherLauncher.Models;

namespace AetherLauncher.Services;

/// <summary>Eine installierte Roblox-Client-Version (Ordner "version-<hash>").</summary>
public sealed record RobloxVersion(string Guid, string Directory, string PlayerExe, DateTimeOffset Modified)
{
    private string? _numeric;

    public string ShortGuid => Guid.StartsWith("version-") ? Guid["version-".Length..] : Guid;

    /// <summary>Der Roblox-Ordner, in dem diese Version liegt (z. B. %LOCALAPPDATA%\Roblox).</summary>
    public string Root => Path.GetDirectoryName(Path.GetDirectoryName(Directory)) ?? "";

    /// <summary>Versionsnummer aus der Datei, z. B. 0.738.0.41361 - wie im Aether-Hub angezeigt.</summary>
    public string NumericVersion
    {
        get
        {
            if (_numeric is not null) return _numeric;
            try
            {
                _numeric = FileVersionInfo.GetVersionInfo(PlayerExe).FileVersion ?? "";
            }
            catch
            {
                _numeric = "";
            }

            return _numeric;
        }
    }
}

/// <summary>Die Version, die Roblox im gewählten Kanal gerade ausrollt.</summary>
public sealed record RobloxDeployment(string Guid, string NumericVersion);

/// <summary>Findet installierte Roblox-Clients und die im Kanal aktuell ausgerollte Version.</summary>
public static class RobloxInstall
{
    private const string PlayerExeName = "RobloxPlayerBeta.exe";

    /// <summary>Alle Ordner "version-*" mit einem nutzbaren RobloxPlayerBeta.exe, neueste zuerst.</summary>
    public static List<RobloxVersion> Discover(string? root)
    {
        var result = new List<RobloxVersion>();
        if (string.IsNullOrWhiteSpace(root)) return result;

        var versionsDir = Path.Combine(root, "Versions");
        if (!System.IO.Directory.Exists(versionsDir)) return result;

        try
        {
            foreach (var dir in System.IO.Directory.EnumerateDirectories(versionsDir, "version-*"))
            {
                var exe = Path.Combine(dir, PlayerExeName);
                if (!File.Exists(exe)) continue; // z. B. Studio- oder Launcher-Ordner ohne Player

                DateTimeOffset modified;
                try
                {
                    modified = File.GetLastWriteTimeUtc(exe);
                }
                catch
                {
                    modified = DateTimeOffset.MinValue;
                }

                result.Add(new RobloxVersion(Path.GetFileName(dir), dir, exe, modified));
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Could not read the Roblox folder: {ex.Message}");
        }

        return result.OrderByDescending(v => v.Modified).ToList();
    }

    /// <summary>
    /// Sucht die zu startende Version: Hinweis aus der Registry (das ist die zuletzt
    /// installierte), sonst die neueste gefundene. Bloxstrap-Installationen werden
    /// berücksichtigt, wenn im Roblox-Ordner nichts liegt.
    /// </summary>
    public static RobloxVersion? Resolve(LauncherSettings settings, out string searchedRoots)
    {
        var roots = Roots(settings);
        searchedRoots = string.Join(", ", roots);

        var all = DiscoverAll(settings);
        if (all.Count == 0) return null;

        // Eine angeheftete Version gewinnt: genau damit arbeiten externe Programme.
        if (!string.IsNullOrWhiteSpace(settings.PinnedVersionGuid))
        {
            var pinned = all.FirstOrDefault(v => string.Equals(v.Guid, settings.PinnedVersionGuid, StringComparison.OrdinalIgnoreCase));
            if (pinned is not null) return pinned;

            Log.Warn($"The pinned version {settings.PinnedVersionGuid} is not installed - using the newest one.");
        }

        // Sonst der Hinweis aus der Registry (das ist die zuletzt installierte Version).
        var hint = RegistryVersionHint();
        if (hint is not null)
        {
            var match = all.FirstOrDefault(v => string.Equals(v.Guid, hint, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }

        return all[0];
    }

    /// <summary>Alle Ordner, in denen nach Clients gesucht wird (in dieser Reihenfolge).</summary>
    public static List<string> Roots(LauncherSettings settings)
    {
        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(settings.RobloxRoot)) roots.Add(settings.RobloxRoot!);
        roots.Add(AppPaths.DefaultRobloxRoot);
        roots.AddRange(AppPaths.AlternateRobloxRoots());
        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Alle installierten Versionen über alle Suchordner hinweg, neueste zuerst.</summary>
    public static List<RobloxVersion> DiscoverAll(LauncherSettings settings)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<RobloxVersion>();

        foreach (var root in Roots(settings))
        {
            foreach (var version in Discover(root))
            {
                // Dieselbe Version in zwei Ordnern (z. B. Roblox und Bloxstrap) nur einmal zeigen.
                if (!seen.Add(version.Guid)) continue;
                result.Add(version);
            }
        }

        return result.OrderByDescending(v => v.Modified).ToList();
    }

    /// <summary>Version, die Roblox selbst als Protokoll-Handler hinterlegt hat (HKCU).</summary>
    public static string? RegistryVersionHint()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\roblox-player");
            return key?.GetValue("version") as string;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Fragt beim Roblox-Deployment-Dienst nach der im Kanal ausgerollten Version.</summary>
    public static async Task<RobloxDeployment?> GetDeployedVersionAsync(string channel, CancellationToken ct = default)
    {
        var channelName = string.IsNullOrWhiteSpace(channel) ? "production" : channel.Trim();
        var urls = new[]
        {
            $"https://clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer/channel/{channelName}",
            "https://clientsettings.roblox.com/v2/client-version/WindowsPlayer",
        };

        foreach (var url in urls)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) continue;

                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var document = System.Text.Json.JsonDocument.Parse(json);

                var guid = document.RootElement.TryGetProperty("clientVersionUpload", out var upload)
                    ? upload.GetString()
                    : null;
                var numeric = document.RootElement.TryGetProperty("version", out var version)
                    ? version.GetString()
                    : null;

                if (!string.IsNullOrWhiteSpace(guid))
                    return new RobloxDeployment(guid!, numeric ?? "");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Warn($"Version lookup failed ({url}): {ex.Message}");
            }
        }

        return null;
    }
}
