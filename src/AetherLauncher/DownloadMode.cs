using AetherLauncher.Services;

namespace AetherLauncher;








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
