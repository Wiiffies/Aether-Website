using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AetherLauncher.Services;

/// <summary>Eine Sitzung, die Roblox selbst gestartet hat.</summary>
public readonly record struct RobloxSession(long PlaceId, long? UniverseId, DateTimeOffset When);

/// <summary>Der letzte Beitritt aus Roblox' Log: Platz und Server.</summary>
public readonly record struct RobloxJoin(long PlaceId, string? JobId);

/// <summary>
/// Liest aus Roblox' eigenen Logdateien, was zuletzt gespielt wurde.
///
/// Der Client legt bei jedem Start eine Datei unter <c>%LOCALAPPDATA%\Roblox\logs</c> ab; darin
/// steht beim Beitritt eine Zeile wie
/// <c>Joining game '&lt;jobId&gt;' place 123456789 at …</c>. Aether liest ausschließlich diese
/// Platz- und Universe-IDs - sonst nichts aus den Dateien.
/// </summary>
public static class RobloxLogImport
{
    /// <summary>So weit zurück wird geschaut.</summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(14);

    /// <summary>So viele Sitzungen wandern höchstens in die Liste.</summary>
    private const int MaxEntries = 10;

    /// <summary>So viele Logdateien werden höchstens angesehen.</summary>
    private const int MaxFiles = 80;

    private static readonly Regex JoinPattern = new(
        @"Joining game\s+'[^']*'\s+place\s+(\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Dieselbe Zeile, aber mit der Job-ID des Servers - für den Serverwechsel.</summary>
    private static readonly Regex JoinDetailPattern = new(
        @"Joining game\s+'([^']*)'\s+place\s+(\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex UniversePattern = new(
        @"universeid\s*:\s*(\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex StampPattern = new(
        @"_(\d{8})T(\d{6})Z_",
        RegexOptions.Compiled);

    /// <summary>Die letzten Sitzungen, neueste zuerst - eine je Platz-ID.</summary>
    public static IReadOnlyList<RobloxSession> Sessions(DateTimeOffset? now = null)
    {
        var reference = now ?? DateTimeOffset.Now;
        var limit = reference - MaxAge;

        var found = new List<RobloxSession>();
        var seen = new HashSet<long>();

        foreach (var file in LogFiles().Take(MaxFiles))
        {
            if (found.Count >= MaxEntries) break;

            var when = StartedAt(file);
            if (when < limit) continue;

            if (!TryRead(file, out var placeId, out var universeId)) continue;
            if (placeId <= 0 || !seen.Add(placeId)) continue;

            found.Add(new RobloxSession(placeId, universeId, when));
        }

        return found;
    }

    private static IEnumerable<string> LogFiles()
    {
        var root = Path.Combine(AppPaths.DefaultRobloxRoot, "logs");
        if (!Directory.Exists(root)) return Array.Empty<string>();

        try
        {
            // Nur echte Spielsitzungen: die Crash-Handler-Dateien enthalten keine Platz-IDs.
            return Directory.EnumerateFiles(root, "*Player*.log")
                .Where(path => !path.Contains("CrashHandler", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Warn($"Roblox' log folder could not be read: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    /// <summary>Zeitpunkt aus dem Dateinamen (der Client schreibt ihn in UTC mit).</summary>
    private static DateTimeOffset StartedAt(string path) => TryParseStamp(Path.GetFileName(path), out var when)
        ? when
        : new DateTimeOffset(File.GetLastWriteTime(path));

    /// <summary>Der Startzeitpunkt steht im Dateinamen: 0.744.0_20261027T210619Z_Player_…</summary>
    public static bool TryParseStamp(string fileName, out DateTimeOffset when)
    {
        when = DateTimeOffset.MinValue;

        var match = StampPattern.Match(fileName);
        if (!match.Success) return false;

        if (!DateTimeOffset.TryParseExact(
                $"{match.Groups[1].Value}T{match.Groups[2].Value}Z",
                "yyyyMMdd'T'HHmmss'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var stamp)) return false;

        when = stamp.ToLocalTime();
        return true;
    }

    /// <summary>
    /// Sucht in einem Logtext die letzte Zeile "Joining game … place N" und die Universe-ID.
    /// Genau diese beiden Angaben braucht die Liste - alles andere in der Datei bleibt ungelesen.
    /// </summary>
    public static bool TryParseSession(string text, out long placeId, out long? universeId)
    {
        placeId = 0;
        universeId = null;
        if (string.IsNullOrEmpty(text)) return false;

        var joins = JoinPattern.Matches(text);
        if (joins.Count > 0 && long.TryParse(joins[^1].Groups[1].Value, out var last)) placeId = last;

        var universe = UniversePattern.Matches(text);
        if (universe.Count > 0 && long.TryParse(universe[^1].Groups[1].Value, out var found)) universeId = found;

        return placeId > 0;
    }

    /// <summary>
    /// Der letzte Beitritt des zuletzt schreibenden Clients - Platz und Job-ID. Wird für den
    /// Serverwechsel gebraucht: Aether soll den Platz nehmen, in dem der Spieler gerade sitzt,
    /// auch wenn er ihn über Roblox' Startseite gewechselt hat. Und der aktuelle Server darf
    /// nicht noch einmal als Ziel gewählt werden.
    /// </summary>
    public static RobloxJoin? LastJoin()
    {
        foreach (var file in LogFiles().Take(6))
        {
            var text = ReadFrom(file, 0);
            if (text.Length == 0) continue;

            var joins = JoinDetailPattern.Matches(text);
            for (var index = joins.Count - 1; index >= 0; index--)
            {
                if (!long.TryParse(joins[index].Groups[2].Value, out var placeId) || placeId <= 0)
                    continue;

                var jobId = joins[index].Groups[1].Value.Trim();
                return new RobloxJoin(placeId, jobId.Length == 0 ? null : jobId);
            }
        }

        return null;
    }

    /// <summary>Die Job-ID des Servers, auf dem der zuletzt schreibende Client gerade sitzt.</summary>
    public static string? LastServerId() => LastJoin()?.JobId;

    /// <summary>
    /// Merkt sich, wie weit die Logdateien beim Start eines Serverwechsels waren. Danach
    /// zählt nur, was neu dazukommt - so lässt sich erkennen, ob Roblox den Wechsel im
    /// laufenden Client erledigt hat, statt einen zweiten zu starten.
    /// </summary>
    public static JoinWatch Watch()
    {
        var lengths = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in LogFiles())
        {
            try
            {
                lengths[file] = new FileInfo(file).Length;
            }
            catch
            {
                // Datei verschwunden - dann eben von vorne lesen.
            }
        }

        return new JoinWatch(lengths);
    }

    /// <summary>
    /// Sieht nur den neuen Teil der Roblox-Logs durch. Taucht dort ein Beitritt zum Platz
    /// (und optional genau zur Job-ID) auf, hat der Wechsel geklappt - auch ohne neuen
    /// Prozess.
    /// </summary>
    public sealed class JoinWatch
    {
        private readonly Dictionary<string, long> _lengths;

        internal JoinWatch(Dictionary<string, long> lengths) => _lengths = lengths;

        public bool SawJoin(long placeId, string? jobId = null)
        {
            foreach (var file in LogFiles())
            {
                var offset = _lengths.TryGetValue(file, out var length) ? length : 0L;
                var text = ReadFrom(file, offset);
                if (text.Length == 0) continue;

                foreach (Match join in JoinDetailPattern.Matches(text))
                {
                    if (!long.TryParse(join.Groups[2].Value, out var joinedPlace) || joinedPlace != placeId)
                        continue;

                    if (jobId is null || string.Equals(join.Groups[1].Value, jobId, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }
    }

    /// <summary>Liest eine Logdatei ab einer Byte-Position (oder von vorne) - ohne sie zu sperren.</summary>
    private static string ReadFrom(string path, long offset)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            if (offset > 0)
            {
                // Nichts Neues (oder die Datei wurde gekürzt) - dann gibt es hier auch
                // keinen neuen Beitritt zu melden.
                if (offset >= stream.Length) return "";
                stream.Seek(offset, SeekOrigin.Begin);
            }

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Die letzte Zeile "Joining game … place N" der Datei zählt - das ist der Server, auf dem
    /// die Sitzung am Ende war. Gelesen wird nur der Anfang der Datei; mehr steht dort nicht.
    /// </summary>
    private static bool TryRead(string path, out long placeId, out long? universeId)
    {
        placeId = 0;
        universeId = null;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            return TryParseSession(reader.ReadToEnd(), out placeId, out universeId);
        }
        catch
        {
            return false;
        }
    }
}
