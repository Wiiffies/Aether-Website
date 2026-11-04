using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using AetherLauncher.Services;
using Microsoft.Web.WebView2.Core;

namespace AetherLauncher;

/// <summary>
/// Roblox' eigene Anmeldung in einem eingebetteten Browser.
///
/// Aether bekommt das Passwort nie zu sehen: es wird in Roblox' Seite getippt und geht direkt
/// an roblox.com. Nach der Anmeldung liest Aether nur das <c>.ROBLOSECURITY</c>-Cookie aus dem
/// temporären Browserprofil und übergibt es an den Kontospeicher, der es verschlüsselt ablegt.
/// Das Profil liegt in %TEMP% und wird beim Schließen gelöscht - es bleibt keine zweite Kopie
/// des Schlüssels zurück.
/// </summary>
public partial class RobloxLoginWindow : Window
{
    private const string LoginUrl = "https://www.roblox.com/login";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly string _profileFolder =
        Path.Combine(Path.GetTempPath(), "aether-roblox-login-" + Guid.NewGuid().ToString("N"));

    private readonly DispatcherTimer _poll;
    private bool _found;

    public RobloxLoginWindow()
    {
        InitializeComponent();

        _poll = new DispatcherTimer(DispatcherPriority.Background) { Interval = PollInterval };
        _poll.Tick += async (_, _) => await CheckForCookieAsync();

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    /// <summary>Das gefundene Cookie - null, wenn die Anmeldung abgebrochen wurde.</summary>
    public string? Cookie { get; private set; }

    /// <summary>Räumt Profile auf, die nach einem Absturz während der Anmeldung liegen blieben.</summary>
    public static void CleanupStaleProfiles()
    {
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(
                         Path.GetTempPath(), "aether-roblox-login-*"))
            {
                try
                {
                    if (Directory.GetCreationTimeUtc(directory) < DateTime.UtcNow.AddHours(-2))
                        Directory.Delete(directory, recursive: true);
                }
                catch
                {
                    // Ein noch laufender Browser hält die Dateien - beim nächsten Start erneut.
                }
            }
        }
        catch
        {
            // %TEMP% nicht lesbar: kein Grund, den Start zu verhindern.
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var runtime = CoreWebView2Environment.GetAvailableBrowserVersionString();
            if (string.IsNullOrWhiteSpace(runtime))
                throw new InvalidOperationException("The Microsoft WebView2 Runtime is not installed.");

            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null, userDataFolder: _profileFolder);

            await Browser.EnsureCoreWebView2Async(environment);

            var core = Browser.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;

            core.NewWindowRequested += OnNewWindowRequested;
            core.NavigationCompleted += (_, args) =>
            {
                LoadingLayer.Visibility = Visibility.Collapsed;

                StatusLine.Text = args.IsSuccess
                    ? "Signed in? Aether picks the session up automatically."
                    : "Roblox's page did not load - check your connection and try again.";
            };

            core.Navigate(LoginUrl);
            _poll.Start();
        }
        catch (Exception ex)
        {
            Log.Error($"Roblox sign-in could not start: {ex.Message}");

            LoadingTitle.Text = "Roblox sign-in is unavailable";
            LoadingText.Text =
                "Install the free Microsoft WebView2 Runtime, then reopen this window. " +
                "Alternatively sign in in your normal browser and paste the launch key.";
            FallbackButton.Visibility = Visibility.Visible;
            StatusLine.Text = ex.Message;
        }
    }

    /// <summary>
    /// Roblox' Anmeldung darf keine eigenen Fenster aufmachen; alles bleibt in diesem
    /// Browser, damit das Cookie im selben temporären Profil landet.
    /// </summary>
    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;

        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            Browser.CoreWebView2?.Navigate(uri.AbsoluteUri);
        }
    }

    private async Task CheckForCookieAsync()
    {
        if (_found || Browser.CoreWebView2 is null) return;

        try
        {
            var cookies = await Browser.CoreWebView2.CookieManager.GetCookiesAsync("https://www.roblox.com");
            var domainCookies = await Browser.CoreWebView2.CookieManager.GetCookiesAsync("https://roblox.com");

            var session = cookies
                .Concat(domainCookies)
                .FirstOrDefault(cookie =>
                    cookie.Name.TrimStart('.').Equals("ROBLOSECURITY", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(cookie.Value));

            if (session is null) return;

            _found = true;
            _poll.Stop();
            Cookie = session.Value.Trim();
            StatusLine.Text = "Signed in - adding the account…";

            try
            {
                DialogResult = true;
            }
            catch (InvalidOperationException)
            {
                Close();
            }
        }
        catch (Exception ex)
        {
            // Nicht bei jedem Tick erneut ins Log schreiben.
            Log.Warn($"The Roblox login cookie could not be read: {ex.Message}");
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            DialogResult = false;
        }
        catch (InvalidOperationException)
        {
            Close();
        }
    }

    private void Fallback_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(LoginUrl) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn($"The Roblox login page could not be opened: {ex.Message}");
        }

        try
        {
            DialogResult = false;
        }
        catch (InvalidOperationException)
        {
            Close();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _poll.Stop();

        try
        {
            Browser.Dispose();
        }
        catch
        {
            // Der Browserprozess räumt selbst auf.
        }

        _ = DeleteProfileAsync(_profileFolder);
    }

    private static async Task DeleteProfileAsync(string path)
    {
        // WebView2 hält die Dateien noch einen Moment - ein paar Versuche reichen.
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                if (!Directory.Exists(path)) return;
                Directory.Delete(path, recursive: true);
                return;
            }
            catch
            {
                await Task.Delay(250);
            }
        }

        Log.Warn("The temporary Roblox login profile could not be removed yet.");
    }
}
