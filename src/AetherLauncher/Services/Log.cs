using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace AetherLauncher.Services;

/// <summary>
/// Zentrales Log. Schreibt in eine Datei und hält die letzten Zeilen für die UI vor.
/// Wichtig: Auth-Tickets und Cookies werden vor der Ausgabe maskiert.
/// </summary>
public static partial class Log
{
    private const int MaxUiLines = 500;
    private static readonly object Gate = new();
    private static Dispatcher? _ui;

    public static ObservableCollection<string> Lines { get; } = new();

    /// <summary>Wird von der Oberfläche gesetzt, damit Log-Aufrufe aus Hintergrundthreads erlaubt sind.</summary>
    public static void AttachUi(Dispatcher dispatcher) => _ui = dispatcher;

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message) => Write("FEHLER", message);

    public static string Text => string.Join(Environment.NewLine, Lines);

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss} [{level}] {Redact(message)}";

        lock (Gate)
        {
            try
            {
                File.AppendAllText(AppPaths.LogFile, line + Environment.NewLine);
            }
            catch
            {
                // Logging darf die App nie zum Absturz bringen.
            }

            var dispatcher = _ui;
            if (dispatcher is null || dispatcher.HasShutdownStarted)
            {
                AppendToUi(line);
            }
            else
            {
                dispatcher.BeginInvoke(() => AppendToUi(line));
            }
        }
    }

    private static void AppendToUi(string line)
    {
        Lines.Add(line);
        while (Lines.Count > MaxUiLines) Lines.RemoveAt(0);
    }

    /// <summary>
    /// Entfernt Geheimnisse aus Log-Zeilen: das Auth-Ticket aus einer roblox-player URI
    /// und alle .ROBLOSECURITY-Werte. Die Log-Seite ist sichtbar und wird oft gescreenshottet.
    /// </summary>
    public static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        text = TicketPattern().Replace(text, "gameinfo:<ticket>");
        text = CookiePattern().Replace(text, "<cookie>");
        return text;
    }

    [GeneratedRegex(@"gameinfo:[^+&\s]*", RegexOptions.IgnoreCase)]
    private static partial Regex TicketPattern();

    [GeneratedRegex(@"_?\|?_?WARNING:-DO-NOT-SHARE[^\s""';]*|\.ROBLOSECURITY=[^\s""';]+", RegexOptions.IgnoreCase)]
    private static partial Regex CookiePattern();
}
