using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;

namespace AetherLauncher.Services;

/// <summary>Ein gefundenes Roblox-Spielfenster mit seiner Lage auf dem Bildschirm.</summary>
public readonly record struct GameWindow(IntPtr Handle, int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public int CenterX => Left + (Width / 2);
}

/// <summary>
/// Das Ingame-Overlay: ein eigenes, kleines Fenster über dem Roblox-Client.
///
/// Es wird <b>nichts</b> in den Client eingeschleust - das Overlay zeichnet nur über das
/// Spielfenster und arbeitet mit Dingen, die ohnehin erlaubt sind: FastFlags in den
/// ClientSettings, ein normaler Neustart des Clients und der Launcher selbst.
///
/// Der Host läuft als eigener Prozess (<c>--overlay</c>), damit das Overlay auch dann da ist,
/// wenn der Launcher geschlossen wurde. Solange das Overlay zu ist, zeichnet das Fenster
/// nichts und der Timer prüft nur die Tastenkombination.
/// </summary>
public static class GameOverlay
{
    public const string Argument = "--overlay";
    public const string HostMutexName = @"Local\AetherOverlayHost";

    /// <summary>Läuft dieser Prozess selbst als Overlay-Host? Dann startet er keinen zweiten.</summary>
    public static bool Hosted { get; set; }

    /// <summary>Startet den Overlay-Prozess. Läuft schon einer, beendet der neue sich selbst.</summary>
    public static void EnsureRunning()
    {
        if (Hosted) return;

        try
        {
            Process.Start(new ProcessStartInfo(AppPaths.ExecutablePath, Argument)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            })?.Dispose();

            Log.Info("In-game overlay host started.");
        }
        catch (Exception ex)
        {
            Log.Warn($"The in-game overlay could not be started: {ex.Message}");
        }
    }

    /// <summary>
    /// Das größte sichtbare Fenster eines Roblox-Clients. Bewusst über alle Fenster statt
    /// über MainWindowHandle: der Client hat teils einen kleinen Startbildschirm, und der
    /// wäre als Anker für das Overlay unbrauchbar.
    /// </summary>
    public static GameWindow? FindWindow()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName("RobloxPlayerBeta");
        }
        catch
        {
            return null;
        }

        var ids = new HashSet<uint>();
        try
        {
            foreach (var process in processes) ids.Add((uint)process.Id);
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }

        if (ids.Count == 0) return null;

        GameWindow? best = null;

        try
        {
            EnumWindows((handle, _) =>
            {
                if (!IsWindowVisible(handle)) return true;

                GetWindowThreadProcessId(handle, out var processId);
                if (!ids.Contains(processId)) return true;
                if (!GetWindowRect(handle, out var rect)) return true;

                var width = rect.Right - rect.Left;
                var height = rect.Bottom - rect.Top;
                if (width < 200 || height < 150) return true;

                if (best is null || (long)width * height > (long)best.Value.Width * best.Value.Height)
                    best = new GameWindow(handle, rect.Left, rect.Top, rect.Right, rect.Bottom);

                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.Warn($"The Roblox window could not be enumerated: {ex.Message}");
        }

        return best;
    }

    /// <summary>
    /// Das sichtbare Hauptfenster jedes laufenden Clients, ein Eintrag pro Prozess.
    /// Für Anti-AFK: mehrere Instanzen dürfen nicht nur die erste erwischen.
    /// </summary>
    public static List<GameWindow> ClientWindows()
    {
        var ids = ClientProcessIds();
        var found = new Dictionary<int, GameWindow>();
        if (ids.Count == 0) return new List<GameWindow>();

        try
        {
            EnumWindows((handle, _) =>
            {
                if (!IsWindowVisible(handle)) return true;

                GetWindowThreadProcessId(handle, out var processId);
                var pid = (int)processId;
                if (!ids.Contains(pid)) return true;
                if (!GetWindowRect(handle, out var rect)) return true;

                var width = rect.Right - rect.Left;
                var height = rect.Bottom - rect.Top;
                if (width < 200 || height < 150) return true;

                var area = (long)width * height;
                if (found.TryGetValue(pid, out var known) && (long)known.Width * known.Height >= area) return true;

                found[pid] = new GameWindow(handle, rect.Left, rect.Top, rect.Right, rect.Bottom);
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.Warn($"The Roblox windows could not be enumerated: {ex.Message}");
        }

        return found.Values.ToList();
    }

    /// <summary>Die Prozess-IDs aller laufenden Roblox-Clients.</summary>
    public static HashSet<int> ClientProcessIds()
    {
        var ids = new HashSet<int>();
        try
        {
            foreach (var process in Process.GetProcessesByName("RobloxPlayerBeta"))
            {
                ids.Add(process.Id);
                process.Dispose();
            }
        }
        catch
        {
            // Ohne Prozessliste ist eben kein Client bekannt.
        }

        return ids;
    }

    /// <summary>Läuft gerade irgendein Roblox-Client? Auch ohne sichtbares Fenster.</summary>
    public static bool IsClientRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName("RobloxPlayerBeta");
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Hat dieser Client schon ein Fenster? (Der Startbildschirm zählt mit.)</summary>
    public static bool HasWindow(int processId) => WindowOf(processId) != IntPtr.Zero;

    /// <summary>Zu welchem Prozess gehört dieses Fenster?</summary>
    public static int ProcessIdOf(IntPtr window)
    {
        if (window == IntPtr.Zero) return 0;
        GetWindowThreadProcessId(window, out var processId);
        return (int)processId;
    }

    /// <summary>
    /// Das sichtbare Spielfenster eines bestimmten Clients. MainWindowHandle ist dafür
    /// ungeeignet: Roblox legt sein Fenster auf einem anderen Thread an, dort bleibt das
    /// Handle oft leer - deshalb wird wie in <see cref="FindWindow"/> aufgezählt.
    /// </summary>
    public static IntPtr WindowOf(int processId)
    {
        if (processId <= 0) return IntPtr.Zero;

        var found = IntPtr.Zero;
        long bestArea = 0;

        try
        {
            EnumWindows((handle, _) =>
            {
                if (!IsWindowVisible(handle)) return true;

                GetWindowThreadProcessId(handle, out var owner);
                if (owner != (uint)processId) return true;
                if (!GetWindowRect(handle, out var rect)) return true;

                var width = rect.Right - rect.Left;
                var height = rect.Bottom - rect.Top;
                if (width < 200 || height < 150) return true;

                var area = (long)width * height;
                if (area <= bestArea) return true;

                bestArea = area;
                found = handle;
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.Warn($"The Roblox window of PID {processId} could not be found: {ex.Message}");
        }

        return found;
    }

    /// <summary>
    /// Setzt das Fenster eines Clients auf dieselbe Größe und Stelle wie das alte. Damit
    /// wirkt der Serverwechsel wie ein Fensterwechsel und nicht wie ein Neustart.
    /// </summary>
    public static bool MoveWindowTo(int processId, int left, int top, int width, int height)
    {
        var handle = WindowOf(processId);
        if (handle == IntPtr.Zero || width < 200 || height < 150) return false;

        try
        {
            return MoveWindow(handle, left, top, width, height, true);
        }
        catch (Exception ex)
        {
            Log.Warn($"The Roblox window of PID {processId} could not be moved: {ex.Message}");
            return false;
        }
    }

    /// <summary>Holt den neuen Client nach vorn, sobald sein Fenster da ist.</summary>
    public static bool ActivateProcess(int processId)
    {
        var handle = WindowOf(processId);
        return handle != IntPtr.Zero && ActivateAndWait(handle, 1200);
    }

    /// <summary>Steht genau dieses Fenster im Vordergrund?</summary>
    public static bool IsForeground(IntPtr window) => window != IntPtr.Zero && GetForegroundWindow() == window;

    /// <summary>Welches Fenster hat gerade den Fokus? (0, wenn keins.)</summary>
    public static IntPtr ForegroundWindow() => GetForegroundWindow();

    /// <summary>Legt ein Fenster in die Taskleiste - für den Anti-AFK-Pop-up danach.</summary>
    public static bool Minimize(IntPtr window) => window != IntPtr.Zero && ShowWindow(window, ShowMinimize);

    public static bool IsMinimized(IntPtr window) => window != IntPtr.Zero && IsIconic(window);

    /// <summary>
    /// Holt ein Fenster nach vorn. Windows weigert sich normalerweise, den Fokus an einen
    /// fremden Prozess zu geben - mit angehängter Eingabeschlange geht es trotzdem.
    /// </summary>
    public static bool Activate(IntPtr window)
    {
        if (window == IntPtr.Zero) return false;
        if (GetForegroundWindow() == window) return true;

        var target = GetWindowThreadProcessId(window, out _);
        var current = GetCurrentThreadId();
        var attached = target != 0 && target != current && AttachThreadInput(current, target, true);

        try
        {
            // Nur ein minimiertes Fenster wiederherstellen. SW_RESTORE auf einem sichtbaren,
            // maximierten Spielfenster würde es verkleinern - beim Anti-AFK-Pop-up wäre das
            // nach jedem Intervall eine kleine Fensteränderung.
            if (IsIconic(window)) ShowWindow(window, ShowRestore);

            SetForegroundWindow(window);
            SetFocus(window);
        }
        finally
        {
            if (attached) AttachThreadInput(current, target, false);
        }

        return GetForegroundWindow() == window;
    }

    /// <summary>
    /// Holt das Fenster nach vorn und wartet, bis Windows den Fokus wirklich umgestellt hat.
    /// SetForegroundWindow ist asynchron: ohne dieses Warten geht der erste Stoß noch an das
    /// vorherige Fenster - genau deshalb wirkte Anti-AFK manchmal, manchmal nicht.
    /// </summary>
    public static bool ActivateAndWait(IntPtr window, int timeoutMs = 900)
    {
        if (window == IntPtr.Zero) return false;
        if (GetForegroundWindow() == window) return true;

        var deadline = Environment.TickCount64 + timeoutMs;
        var attempt = 0;

        while (Environment.TickCount64 < deadline)
        {
            if (GetForegroundWindow() == window) return true;

            Activate(window);
            Thread.Sleep(attempt == 0 ? 35 : 70);
            attempt++;
        }

        return GetForegroundWindow() == window;
    }

    /// <summary>Gehört das Fenster im Vordergrund zu diesem Programm (Overlay selbst)?</summary>
    public static bool IsForegroundOwn()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;

        GetWindowThreadProcessId(foreground, out var processId);
        return processId == (uint)Environment.ProcessId;
    }

    /// <summary>Steht der Roblox-Client gerade im Vordergrund? Nur dann reagiert das Kürzel.</summary>
    public static bool IsForegroundClient()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;

        GetWindowThreadProcessId(foreground, out var processId);
        if (processId == 0) return false;

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName.Equals("RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Ist die Taste gedrückt? (Abfrage, kein Haken - funktioniert auch ohne Fokus.)</summary>
    public static bool IsPressed(Key key)
    {
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        return virtualKey != 0 && (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    /// <summary>Welche Zusatztasten sind gerade gedrückt?</summary>
    public static ModifierKeys CurrentModifiers()
    {
        var modifiers = ModifierKeys.None;
        if (Down(VkShift)) modifiers |= ModifierKeys.Shift;
        if (Down(VkControl)) modifiers |= ModifierKeys.Control;
        if (Down(VkMenu)) modifiers |= ModifierKeys.Alt;
        if (Down(VkLeftWindows) || Down(VkRightWindows)) modifiers |= ModifierKeys.Windows;
        return modifiers;
    }

    /// <summary>Stimmen die Zusatztasten genau mit der Angabe überein?</summary>
    public static bool AreModifiersDown(ModifierKeys modifiers) => CurrentModifiers() == modifiers;

    /// <summary>Gibt den Fokus an das Spielfenster zurück (nach dem Zumachen des Overlays).</summary>
    public static void Focus(GameWindow window)
    {
        if (window.Handle != IntPtr.Zero) SetForegroundWindow(window.Handle);
    }

    /// <summary>Arbeitsfläche des Bildschirms, auf dem das Fenster liegt (für das Andocken).</summary>
    public static (int Left, int Top, int Right, int Bottom) WorkArea(IntPtr window)
    {
        var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
        return AreaOf(monitor);
    }

    /// <summary>
    /// Arbeitsfläche des Bildschirms an einem Punkt. Wird beim Verschieben gebraucht: das
    /// Overlay darf nirgends landen, wo man es nicht mehr greifen kann.
    /// </summary>
    public static (int Left, int Top, int Right, int Bottom) WorkAreaAt(int x, int y)
    {
        var monitor = MonitorFromPoint(new Point32 { X = x, Y = y }, MonitorDefaultToNearest);
        return AreaOf(monitor);
    }

    private static (int Left, int Top, int Right, int Bottom) AreaOf(IntPtr monitor)
    {
        if (monitor != IntPtr.Zero)
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
                return (info.WorkArea.Left, info.WorkArea.Top, info.WorkArea.Right, info.WorkArea.Bottom);
        }

        var area = SystemParameters.WorkArea;
        return ((int)area.Left, (int)area.Top, (int)area.Right, (int)area.Bottom);
    }

    /// <summary>
    /// Schließt alle laufenden Clients - erst höflich, nach der Frist hart. Wird nur auf
    /// ausdrücklichen Knopfdruck benutzt (Server hop / Neustart für neue Flags).
    /// </summary>
    public static bool CloseClients(TimeSpan? grace = null) => CloseProcesses(ClientProcessIds(), grace);

    /// <summary>Schließt genau diese Clients - nicht mehr und nicht weniger.</summary>
    public static bool CloseProcesses(IEnumerable<int> processIds, TimeSpan? grace = null)
    {
        var wanted = new HashSet<int>(processIds);
        if (wanted.Count == 0) return true;

        var processes = new List<Process>();
        foreach (var id in wanted)
        {
            try
            {
                processes.Add(Process.GetProcessById(id));
            }
            catch
            {
                // Schon weg.
            }
        }

        if (processes.Count == 0) return true;

        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero) process.CloseMainWindow();
                }
                catch
                {
                    // Schon weg oder ohne Fenster.
                }
            }

            var deadline = DateTime.UtcNow + (grace ?? TimeSpan.FromSeconds(5));
            while (DateTime.UtcNow < deadline && processes.Any(HasExited) == false) Thread.Sleep(120);

            foreach (var process in processes)
            {
                if (HasExited(process)) continue;
                try
                {
                    process.Kill();
                }
                catch
                {
                    // Nicht kritisch.
                }
            }

            var killed = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            while (DateTime.UtcNow < killed && processes.Any(HasExited) == false) Thread.Sleep(80);
            return processes.All(HasExited);
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch
        {
            return true;
        }
    }

    private static bool Down(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLeftWindows = 0x5B;
    private const int VkRightWindows = 0x5C;
    private const int MonitorDefaultToNearest = 2;
    private const int ShowMinimize = 6;
    private const int ShowRestore = 9;

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, bool attachFlag);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr window);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out WindowRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool MoveWindow(IntPtr window, int x, int y, int width, int height, bool repaint);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, int flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point32 point, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public int Flags;
    }
}
