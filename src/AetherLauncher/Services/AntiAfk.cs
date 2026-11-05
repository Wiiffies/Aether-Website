using System.Runtime.InteropServices;
using System.Windows.Input;

namespace AetherLauncher.Services;











public static class AntiAfk
{
    public const int DefaultIntervalSeconds = 120;
    public const string DefaultKey = "F8";

    
    public static readonly int[] Intervals = { 60, 120, 300, 600 };

    
    public static readonly string[] PresetKeys = { "F8", "LeftShift", "LeftCtrl", "Space" };

    
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(20);

    
    public static readonly TimeSpan PopupTime = TimeSpan.FromMilliseconds(950);

    
    public enum NudgeOutcome
    {
        Sent,
        NoWindow,
        Failed,
    }

    public static bool ValidInterval(int seconds) => seconds is >= 30 and <= 1800;

    public static string DescribeInterval(int seconds) => seconds switch
    {
        < 60 => $"{seconds} s",
        _ when seconds % 60 == 0 => $"{seconds / 60} min",
        _ => $"{seconds / 60} min {seconds % 60} s",
    };

    
    
    
    
    public static NudgeOutcome Nudge(GameWindow? window, string? keyName, out string message)
    {
        message = "";

        
        var game = window is { } given && given.Handle != IntPtr.Zero ? given : GameOverlay.FindWindow() ?? default;
        if (game.Handle == IntPtr.Zero)
        {
            message = "No Roblox window found - is the client running?";
            return NudgeOutcome.NoWindow;
        }

        var key = Shortcuts.TryParseKey(keyName, out var parsed) ? parsed : Key.F8;
        var handle = game.Handle;
        var foreground = GameOverlay.ForegroundWindow();
        var wasForeground = foreground == handle;
        var restoreTo = wasForeground ? IntPtr.Zero : foreground;

        if (!wasForeground)
        {
            if (!GameOverlay.ActivateAndWait(handle, 900))
            {
                message = "Roblox did not come to the front - nothing was sent.";
                return NudgeOutcome.Failed;
            }

            
            Thread.Sleep((int)PopupTime.TotalMilliseconds);
        }

        
        if (GameOverlay.ForegroundWindow() != handle)
        {
            message = "Roblox lost the focus before the tap - nothing was sent.";
            return NudgeOutcome.Failed;
        }

        var sent = SendKey(key);

        if (!wasForeground)
        {
            Thread.Sleep(60);
            GameOverlay.Minimize(handle);
            Thread.Sleep(60);

            
            
            if (restoreTo != IntPtr.Zero && restoreTo != handle &&
                GameOverlay.ProcessIdOf(restoreTo) != Environment.ProcessId)
                GameOverlay.Activate(restoreTo);
        }

        if (!sent)
        {
            message = "Windows refused the key tap.";
            return NudgeOutcome.Failed;
        }

        message = wasForeground
            ? $"Tapped {Shortcuts.Name(key)} in the game."
            : $"Popped Roblox up, tapped {Shortcuts.Name(key)}, and minimized it again.";

        return NudgeOutcome.Sent;
    }

    
    
    
    
    public static NudgeOutcome NudgeEvery(GameWindow? window, string? keyName, out string message)
    {
        var windows = GameOverlay.ClientWindows();
        if (windows.Count == 0) return Nudge(window, keyName, out message);

        var key = Shortcuts.TryParseKey(keyName, out var parsed) ? Shortcuts.Name(parsed) : Shortcuts.Name(Key.F8);

        var sent = 0;
        var failed = 0;
        var firstFailure = "";

        foreach (var client in windows)
        {
            var outcome = Nudge(client, keyName, out var detail);
            if (outcome == NudgeOutcome.Sent)
            {
                sent++;
            }
            else
            {
                failed++;
                if (firstFailure.Length == 0) firstFailure = detail;
            }
        }

        if (sent == 0)
        {
            message = firstFailure.Length > 0 ? firstFailure : "No client accepted the tap.";
            return NudgeOutcome.Failed;
        }

        var count = windows.Count == 1 ? "1 client" : $"{sent} of {windows.Count} clients";
        message = $"Popped up {count}, tapped {key}, minimized again.";
        if (failed > 0) message += $" {failed} did not answer.";

        return NudgeOutcome.Sent;
    }

    
    
    
    
    
    public static bool TryTestInput(out string message)
    {
        if (!GetCursorPos(out var origin))
        {
            message = "The pointer position could not be read.";
            return false;
        }

        
        var targetX = origin.X + 12;
        if (targetX >= VirtualScreen().Right - 1) targetX = origin.X - 12;

        var there = Send(MouseMoveAbsolute(targetX, origin.Y));
        Thread.Sleep(60);

        var landed = GetCursorPos(out var moved) && (moved.X != origin.X || moved.Y != origin.Y);
        var back = Send(MouseMoveAbsolute(origin.X, origin.Y));
        Thread.Sleep(20);
        SetCursorPos(origin.X, origin.Y);

        if (there == 0 || back == 0)
        {
            message = "Windows refused the synthetic mouse event.";
            return false;
        }

        message = landed
            ? "Input works - the pointer moved and came back."
            : $"SendInput was accepted, but the pointer stayed at {origin.X},{origin.Y}.";

        return landed;
    }

    private static bool SendKey(Key key)
    {
        var virtualKey = (ushort)KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0) return false;

        var scan = (ushort)MapVirtualKey(virtualKey, 0);
        if (Send(KeyInput(virtualKey, scan, down: true)) == 0) return false;

        Thread.Sleep(45);
        return Send(KeyInput(virtualKey, scan, down: false)) != 0;
    }

    private static uint Send(Input input) => SendInput(1, new[] { input }, Marshal.SizeOf<Input>());

    private static Input MouseMoveAbsolute(int x, int y)
    {
        var screen = VirtualScreen();
        var width = Math.Max(1, screen.Right - screen.Left);
        var height = Math.Max(1, screen.Bottom - screen.Top);

        return new Input
        {
            Type = InputMouse,
            Union = new InputUnion
            {
                Mouse = new MouseInput
                {
                    Dx = (int)Math.Round((x - screen.Left) * 65535.0 / Math.Max(1, width - 1)),
                    Dy = (int)Math.Round((y - screen.Top) * 65535.0 / Math.Max(1, height - 1)),
                    Flags = MouseEventMove | MouseEventAbsolute | MouseEventVirtualDesk,
                },
            },
        };
    }

    private static (int Left, int Top, int Right, int Bottom) VirtualScreen() => (
        GetSystemMetrics(SmXVirtualScreen),
        GetSystemMetrics(SmYVirtualScreen),
        GetSystemMetrics(SmXVirtualScreen) + GetSystemMetrics(SmCxVirtualScreen),
        GetSystemMetrics(SmYVirtualScreen) + GetSystemMetrics(SmCyVirtualScreen));

    private static Input KeyInput(ushort virtualKey, ushort scan, bool down) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = virtualKey,
                ScanCode = scan,
                Flags = down ? 0 : KeyEventUp,
            },
        },
    };

    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseEventMove = 0x0001;
    private const uint MouseEventAbsolute = 0x8000;
    private const uint MouseEventVirtualDesk = 0x4000;
    private const uint KeyEventUp = 0x0002;

    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point32 point);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
