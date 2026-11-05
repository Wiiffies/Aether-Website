using System.Net.Http;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace AetherLauncher.Services;

public static class WebView2Bootstrap
{
    private const string BootstrapperUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
    private const string RuntimeHelpUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";

    public static string? InstalledVersion()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return string.IsNullOrWhiteSpace(version) ? null : version;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Log.Warn($"WebView2 check failed: {ex.Message}");
            return null;
        }
    }

    public static void EnsureAvailable()
    {
        var version = InstalledVersion();
        if (version is not null)
        {
            Log.Info($"Dependencies: .NET runtime bundled; Microsoft Edge WebView2 Runtime {version} is installed.");
            return;
        }

        Log.Warn("Dependencies: Microsoft Edge WebView2 Runtime is missing.");

        var answer = MessageBox.Show(
            "Aether needs the Microsoft Edge WebView2 Runtime for Roblox sign-in.\n\n" +
            "The .NET runtime is already included in this executable. Aether can download " +
            "and install WebView2 from Microsoft now. This needs an internet connection.\n\n" +
            "Install WebView2 now?",
            "Aether dependencies",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
        {
            Log.Warn("WebView2 installation was skipped. Roblox sign-in will be unavailable until it is installed.");
            return;
        }

        try
        {
            Install();
        }
        catch (Exception ex)
        {
            Log.Error("WebView2 installation failed: " + ex.Message);
            MessageBox.Show(
                "Microsoft Edge WebView2 Runtime could not be installed.\n\n" +
                ex.Message + "\n\n" +
                "You can install it manually from:\n" + RuntimeHelpUrl,
                "Aether dependencies",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var installed = WaitForVersion();
        if (installed is null)
        {
            MessageBox.Show(
                "The WebView2 installer finished, but Aether could not detect the runtime yet. " +
                "Restart Aether and try again.\n\nManual installer: " + RuntimeHelpUrl,
                "Aether dependencies",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Log.Info($"Dependencies: installed Microsoft Edge WebView2 Runtime {installed}.");
        MessageBox.Show(
            "Installed Microsoft Edge WebView2 Runtime " + installed + ".\n\n" +
            "The .NET runtime is bundled in this executable and needed no separate installation.",
            "Aether dependencies",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static void Install()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Aether");
        Directory.CreateDirectory(folder);
        var installer = Path.Combine(folder, "MicrosoftEdgeWebview2Setup.exe");

        try
        {
            using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("AetherLauncher/1.2");
                var bytes = client.GetByteArrayAsync(BootstrapperUrl).GetAwaiter().GetResult();
                File.WriteAllBytes(installer, bytes);
            }

            var start = new ProcessStartInfo
            {
                FileName = installer,
                Arguments = "/silent /install",
                UseShellExecute = true,
                WorkingDirectory = folder
            };

            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("The WebView2 installer could not be started.");

            if (!process.WaitForExit((int)TimeSpan.FromMinutes(5).TotalMilliseconds))
                throw new TimeoutException("The WebView2 installer did not finish within five minutes.");

            if (process.ExitCode is not 0 and not 3010)
                throw new InvalidOperationException($"The WebView2 installer exited with code {process.ExitCode}.");
        }
        finally
        {
            try
            {
                if (File.Exists(installer)) File.Delete(installer);
            }
            catch
            {
            }
        }
    }

    private static string? WaitForVersion()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var version = InstalledVersion();
            if (version is not null) return version;
            Thread.Sleep(500);
        }

        return null;
    }
}
