using AetherLauncher.Services;

namespace AetherLauncher;

/// <summary>
/// Hilfsmodus ohne Oberfläche: lädt den offiziellen Installer eines Builds herunter.
/// Aufruf: AetherLauncher.exe --download current|next|past|version-&lt;hash&gt;
///
/// Damit lässt sich der Downloadweg (WEAO-Auflösung, Adresse, Fortschritt, Datei) auch
/// ohne Klicks prüfen - und für Skripte benutzen.
/// </summary>
internal static class DownloadMode
{
    public static int Run(string target)
    {
        Console.WriteLine($"Aether - downloading a Roblox installer: {target}");

        var guid = target;
        if (!target.StartsWith("version-", StringComparison.OrdinalIgnoreCase))
        {
            var wanted = target.ToLowerInvariant() switch
            {
                "current" or "live" => "WEAO · current",
                "next" or "future" => "WEAO · next",
                "past" or "previous" => "WEAO · previous",
                _ => null,
            };

            var builds = RobloxVersions.GetWeaoAsync().GetAwaiter().GetResult();
            var match = wanted is null
                ? builds.FirstOrDefault(b =>
                    b.Guid.Contains(target, StringComparison.OrdinalIgnoreCase) ||
                    b.NumericVersion.StartsWith(target, StringComparison.OrdinalIgnoreCase))
                : builds.FirstOrDefault(b => b.Source == wanted);

            if (match is null)
            {
                Console.Error.WriteLine("No matching build found. Try current, next, past or a version hash.");
                return 1;
            }

            guid = match.Guid;
            Console.WriteLine($"  resolved: {guid} ({match.NumericVersion}) - {match.Source}");
        }

        var file = Path.Combine(AppPaths.DownloadDir, guid + "-RobloxPlayerInstaller.exe");
        Console.WriteLine($"  target:   {file}");

        // Bewusst kein Progress<T>: der würde auf den Oberflächen-Thread schreiben, den es hier nicht gibt.
        var progress = new DirectProgress();
        var success = RobloxVersions
            .DownloadInstallerAsync(guid, file, null, progress)
            .GetAwaiter()
            .GetResult();

        Console.WriteLine();
        if (!success || !File.Exists(file))
        {
            Console.Error.WriteLine("Download failed - see the log.");
            return 1;
        }

        var megabytes = new FileInfo(file).Length / 1024.0 / 1024.0;
        Console.WriteLine($"Downloaded {Path.GetFileName(file)} ({megabytes:0.0} MB).");
        return 0;
    }

    private sealed class DirectProgress : IProgress<double>
    {
        public void Report(double value) => Console.Write($"\r  {value,5:0}%");
    }
}
