using System.Threading;

namespace AetherLauncher.Services;

/// <summary>
/// Sorgt dafür, dass nur ein Fenster des Launchers läuft. Startet der Benutzer ein zweites
/// Mal (z. B. weil die Roblox-Website unseren Protokollhandler aufruft), wird die
/// Startanfrage an das laufende Fenster übergeben und der neue Prozess beendet sich.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = "AetherLauncher.SingleInstance";
    private const string SignalName = "AetherLauncher.LaunchSignal";

    /// <summary>Kein Start, nur das laufende Fenster nach vorn holen (Knopf im Ingame-Overlay).</summary>
    public const string ShowRequest = "aether:show";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _signal;
    private Thread? _listener;
    private volatile bool _stopping;

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: false, MutexName, out var createdNew);
        IsFirstInstance = createdNew;
        _signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
    }

    public bool IsFirstInstance { get; }

    /// <summary>Übergibt eine Start-URI an das laufende Fenster.</summary>
    public void SendLaunch(string uri)
    {
        try
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    File.AppendAllText(AppPaths.PendingLaunchFile, uri + Environment.NewLine);
                    break;
                }
                catch (IOException) when (attempt < 4)
                {
                    Thread.Sleep(80); // Das andere Fenster liest gerade - kurz warten.
                }
            }

            _signal.Set();
        }
        catch (Exception ex)
        {
            Log.Error($"Could not hand over the launch request: {ex.Message}");
        }
    }

    /// <summary>Hört im Hintergrund auf Startanfragen anderer Prozesse.</summary>
    public void StartListener(Action<string> onLaunch)
    {
        _listener = new Thread(() =>
        {
            while (!_stopping)
            {
                _signal.WaitOne(1000);
                if (_stopping) return;

                foreach (var uri in DrainPending())
                    onLaunch(uri);
            }
        })
        {
            IsBackground = true,
            Name = "Aether-Launch-Listener",
        };

        _listener.Start();
    }

    private static List<string> DrainPending()
    {
        var result = new List<string>();
        try
        {
            if (!File.Exists(AppPaths.PendingLaunchFile)) return result;

            var lines = File.ReadAllLines(AppPaths.PendingLaunchFile);
            File.Delete(AppPaths.PendingLaunchFile);
            result.AddRange(lines.Where(l => !string.IsNullOrWhiteSpace(l)));
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not read pending launch requests: {ex.Message}");
        }

        return result;
    }

    public void Dispose()
    {
        _stopping = true;
        try
        {
            _signal.Set();
        }
        catch
        {
            // egal
        }

        _listener?.Join(500);
        _signal.Dispose();
        _mutex.Dispose();
    }
}
