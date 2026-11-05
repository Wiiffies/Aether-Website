using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using AetherLauncher.Services;
using Microsoft.Web.WebView2.Core;

namespace AetherLauncher;










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

    
    public string? Cookie { get; private set; }

    
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
                    
                }
            }
        }
        catch
        {
            
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
            core.NavigationStarting += OnNavigationStarting;
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

    
    
    
    
    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;

        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && IsTrustedLoginUri(uri))
        {
            Browser.CoreWebView2?.Navigate(uri.AbsoluteUri);
        }
        else
        {
            Log.Warn("Roblox sign-in blocked a pop-up outside roblox.com.");
            StatusLine.Text = "That link leaves Roblox's sign-in page and was blocked.";
        }
    }

    
    
    
    
    
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || !IsTrustedLoginUri(uri))
        {
            e.Cancel = true;
            Log.Warn("Roblox sign-in blocked a navigation outside roblox.com.");
            StatusLine.Text = "That link leaves Roblox's sign-in page and was blocked.";
        }
    }

    private static bool IsTrustedLoginUri(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps) return false;

        var host = uri.Host;
        return host.Equals("roblox.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".roblox.com", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("rbxcdn.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".rbxcdn.com", StringComparison.OrdinalIgnoreCase);
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
            
        }

        _ = DeleteProfileAsync(_profileFolder);
    }

    private static async Task DeleteProfileAsync(string path)
    {
        
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
