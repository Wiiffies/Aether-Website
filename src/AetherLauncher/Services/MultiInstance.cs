using System.Diagnostics;
using System.Threading;

namespace AetherLauncher.Services;











public static class MultiInstance
{
    public const string WatcherArgument = "--multi-watcher";

    
    
    
    
    
    private static readonly string[] SingletonObjectNames =
    {
        "ROBLOX_singletonMutex",
        "ROBLOX_singletonEvent",
        "ROBLOX_SingletonEvent",
    };

    private static readonly TimeSpan StartGrace = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan IdleLimit = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    
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
                    owned = true; 
                }

                if (owned) held.Add(mutex);
                else mutex.Dispose();
            }
            catch
            {
                
            }
        }

        if (held.Count == 0) return 0; 

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
                    
                }

                mutex.Dispose();
            }
        }

        return 0;
    }

    
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
