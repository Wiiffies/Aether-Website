using System.Diagnostics;
using System.Threading;

namespace AetherLauncher.Services;

/// <summary>
/// Erlaubt mehrere Roblox-Clients gleichzeitig.
///
/// Der Client prüft beim Start einen benannten Kernel-Objekt (Singleton). Wenn dort schon
/// jemand "sitzt", beendet er sich bzw. verweigert den zweiten Start. Der Trick - auch von
/// Bloxstrap und anderen Tools benutzt - ist, dieses Objekt SELBST zu erzeugen und zu
/// besitzen, solange Clients laufen. Der Halter ist ein eigener, unsichtbarer Prozess
/// (--multi-watcher), damit die Übernahme auch dann weiterläuft, wenn der Launcher
/// geschlossen wird.
/// </summary>
public static class MultiInstance
{
    public const string WatcherArgument = "--multi-watcher";

    /// <summary>
    /// Die Namen, unter denen Roblox je nach Version sucht. Win32-Namen sind
    /// groß-/kleinschreibungssensitiv und typgebunden, deshalb werden alle Varianten
    /// versucht; was nicht existiert oder einen anderen Typ hat, wird ignoriert.
    /// </summary>
    private static readonly string[] SingletonObjectNames =
    {
        "ROBLOX_singletonMutex",
        "ROBLOX_singletonEvent",
        "ROBLOX_SingletonEvent",
    };

    private static readonly TimeSpan StartGrace = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan IdleLimit = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    /// <summary>Startet den Wächterprozess. Läuft bereits einer, beendet sich der neue von selbst.</summary>
    public static void EnsureWatcher()
    {
        try
        {
            var startInfo = new ProcessStartInfo(AppPaths.ExecutablePath, WatcherArgument)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            Process.Start(startInfo)?.Dispose();
            Log.Info("Multi-instance watcher started.");
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not start the multi-instance watcher: {ex.Message}");
        }
    }

    /// <summary>
    /// Läuft im Wächterprozess: Singleton-Objekte übernehmen, auf Roblox warten und
    /// erst aufräumen, wenn eine Weile kein Client mehr läuft.
    /// </summary>
    public static int RunWatcher()
    {
        var held = new List<Mutex>();
        foreach (var name in SingletonObjectNames)
        {
            try
            {
                var mutex = new Mutex(false, name);
                var owned = false;
                try
                {
                    owned = mutex.WaitOne(0);
                }
                catch (AbandonedMutexException)
                {
                    owned = true; // Vorbesitzer ist abgestürzt, wir übernehmen.
                }

                if (owned) held.Add(mutex);
                else mutex.Dispose();
            }
            catch
            {
                // Name belegt oder anderer Objekttyp (Event statt Mutex) - dann nicht unsere Sache.
            }
        }

        if (held.Count == 0) return 0; // Ein anderer Wächter hält den Singleton bereits.

        try
        {
            if (!WaitForClient(StartGrace)) return 0;

            DateTimeOffset? idleSince = null;
            while (true)
            {
                Thread.Sleep(PollInterval);
                if (IsClientRunning())
                {
                    idleSince = null;
                    continue;
                }

                idleSince ??= DateTimeOffset.UtcNow;
                if (DateTimeOffset.UtcNow - idleSince >= IdleLimit) break;
            }
        }
        finally
        {
            foreach (var mutex in held)
            {
                try
                {
                    mutex.ReleaseMutex();
                }
                catch
                {
                    // Nicht mehr im Besitz - egal.
                }

                mutex.Dispose();
            }
        }

        return 0;
    }

    /// <summary>Läuft gerade ein Roblox-Client?</summary>
    public static bool IsClientRunning()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName("RobloxPlayerBeta");
        }
        catch
        {
            return false;
        }

        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    private static bool WaitForClient(TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (IsClientRunning()) return true;
            Thread.Sleep(1000);
        }

        return false;
    }
}
