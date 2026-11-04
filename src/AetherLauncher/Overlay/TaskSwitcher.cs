using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AetherLauncher.Overlay;

/// <summary>
/// Nimmt die Fenster des Overlays aus Alt+Tab, Win+Tab und der Taskleiste.
///
/// <para>
/// <c>ShowInTaskbar</c> blendet nur den Knopf in der Leiste aus - Windows führt die Fenster
/// trotzdem im Fensterwechsler. Zwei Dinge zusammen halten sie dort heraus: der erweiterte
/// Stil <c>WS_EX_TOOLWINDOW</c> und ein Besitzerfenster. Ein Fenster mit Besitzer ist kein
/// eigener Eintrag im Wechsler; nur das Besitzerfenster selbst könnte einer sein. Aether
/// benutzt dafür ein unsichtbares Fenster, das keinem Benutzer je auffällt.
/// </para>
/// </summary>
internal static class TaskSwitcher
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExAppWindow = 0x00040000;
    private const int GwOwner = 4;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    /// <summary>
    /// Setzt den Toolwindow-Stil. Muss nach jedem Anzeigen erneut laufen: WPF legt beim
    /// Fensteraufbau seine eigenen Stile an (Layered für die Transparenz, Topmost fürs
    /// Overlay) und würde den Toolwindow-Stil sonst wieder verlieren.
    /// </summary>
    public static void Hide(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        var style = GetWindowLong(handle, GwlExStyle);
        style = (style | WsExToolWindow) & ~WsExAppWindow;
        SetWindowLong(handle, GwlExStyle, style);

        // Ohne FRAMECHANGED merkt sich der Fensterwechsler den alten Stil und zeigt das
        // Fenster weiter an. SetWindowPos verschiebt hier nichts - es stößt nur das
        // Übernehmen des neuen Stils an.
        SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    /// <summary>Hat dieses Fenster einen Besitzer? Ohne Besitzer hilft der Stil allein nicht immer.</summary>
    public static bool HasOwner(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        return handle != IntPtr.Zero && GetWindow(handle, GwOwner) != IntPtr.Zero;
    }

    /// <summary>Trägt das Fenster den Toolwindow-Stil und ist kein Appwindow?</summary>
    public static bool HasToolWindowStyle(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return false;

        var style = GetWindowLong(handle, GwlExStyle);
        return (style & WsExToolWindow) != 0 && (style & WsExAppWindow) == 0;
    }

    /// <summary>Ist das Fenster vollständig aus dem Fensterwechsler heraus?</summary>
    public static bool IsHidden(Window window) => HasToolWindowStyle(window) && HasOwner(window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr window, int index, int value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, int command);
}
