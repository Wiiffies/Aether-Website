using System.Windows;
using AetherLauncher.Services;

namespace AetherLauncher;

public partial class App : Application
{
    private SingleInstance? _singleInstance;

    
    public static event Action<string>? ExternalLaunchRequested;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        
        
        var settings = new SettingsStore().Load();
        ThemeManager.Apply(settings.Theme);
        AppFonts.LoadOwnFonts(settings);
        AppFonts.Apply(settings);

        
        
        RobloxLoginWindow.CleanupStaleProfiles();

        
        if (e.Args.Contains(MultiInstance.WatcherArgument, StringComparer.OrdinalIgnoreCase))
        {
            MultiInstance.RunWatcher();
            Shutdown(0);
            return;
        }

        if (e.Args.Contains("--selftest", StringComparer.OrdinalIgnoreCase))
        {
            Shutdown(SelfTest.Run());
            return;
        }

        if (e.Args.Contains("--selftest-scroll", StringComparer.OrdinalIgnoreCase))
        {
            Shutdown(UiSelfTest.RunScroll());
            return;
        }

        if (e.Args.Contains("--selftest-ui", StringComparer.OrdinalIgnoreCase))
        {
            
            Shutdown(UiSelfTest.Run(e.Args.Contains("--hover", StringComparer.OrdinalIgnoreCase)));
            return;
        }

        var downloadIndex = Array.FindIndex(e.Args, a => a.Equals("--download", StringComparison.OrdinalIgnoreCase));
        if (downloadIndex >= 0)
        {
            var target = downloadIndex + 1 < e.Args.Length ? e.Args[downloadIndex + 1] : "current";
            Shutdown(DownloadMode.Run(target));
            return;
        }

        
        
        if (e.Args.Contains(GameOverlay.Argument, StringComparer.OrdinalIgnoreCase))
        {
            InstallCrashHandlers();
            if (!OverlayMode.Start()) Shutdown(0);
            return;
        }

        var screenshotIndex = Array.FindIndex(e.Args, a => a.Equals("--screenshot", StringComparison.OrdinalIgnoreCase));
        if (screenshotIndex >= 0)
        {
            var target = screenshotIndex + 1 < e.Args.Length
                ? e.Args[screenshotIndex + 1]
                : Path.Combine(AppPaths.DataDir, "screenshots");
            Shutdown(ScreenshotMode.Run(target));
            return;
        }

        
        
        InstallCrashHandlers();

        
        
        WaitForRestartTarget(e.Args);

        var launchUri = FindLaunchRequest(e.Args);

        
        
        if (string.Equals(launchUri, SingleInstance.ShowRequest, StringComparison.Ordinal)) launchUri = null;

        _singleInstance = new SingleInstance();
        if (!_singleInstance.IsFirstInstance)
        {
            
            if (launchUri is not null) _singleInstance.SendLaunch(launchUri);
            else if (e.Args.Contains("--show", StringComparer.OrdinalIgnoreCase))
                _singleInstance.SendLaunch(SingleInstance.ShowRequest);
            _singleInstance.Dispose();
            Shutdown(0);
            return;
        }

        _singleInstance.StartListener(uri =>
            Dispatcher.BeginInvoke(() => ExternalLaunchRequested?.Invoke(uri)));

        ShutdownMode = ShutdownMode.OnMainWindowClose;

        var window = new MainWindow(launchUri);
        MainWindow = window;
        window.Show();
    }

    
    
    
    
    
    private void InstallCrashHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            ReportCrash(args.Exception, fatal: true);
            args.Handled = true;
            Shutdown(1);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ReportCrash(args.ExceptionObject as Exception, fatal: false);
    }

    private static void ReportCrash(Exception? exception, bool fatal)
    {
        var text = exception?.ToString() ?? "Unknown error.";

        try
        {
            Log.Error("Unhandled error: " + text);
        }
        catch
        {
            
        }

        string? crashFile = null;
        try
        {
            crashFile = Path.Combine(AppPaths.DataDir, "crash.log");
            File.AppendAllText(crashFile, $"\n==== {DateTime.Now:dd.MM.yyyy HH:mm:ss} ====\n{text}\n");
        }
        catch
        {
            
        }

        if (!fatal) return;

        try
        {
            MessageBox.Show(
                $"Aether hit an error and has to close.\n\n{exception?.Message}\n\n" +
                $"The details are in {crashFile ?? Path.Combine(AppPaths.DataDir, "crash.log")}",
                "Aether", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch
        {
            
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        AppFonts.Release();
        base.OnExit(e);
    }

    private static void WaitForRestartTarget(string[] args)
    {
        var index = Array.FindIndex(args, a => a.Equals("--restart-wait", StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length || !int.TryParse(args[index + 1], out var oldProcessId)) return;

        try
        {
            using var previous = Process.GetProcessById(oldProcessId);
            previous.WaitForExit(8000);
        }
        catch
        {
            
        }
    }

    
    private static string? FindLaunchRequest(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--show", StringComparison.OrdinalIgnoreCase)) return SingleInstance.ShowRequest;
            if (args[i] is "--launch" or "-l" && i + 1 < args.Length) return args[i + 1];

            if (args[i].StartsWith("roblox-player:", StringComparison.OrdinalIgnoreCase) ||
                args[i].StartsWith("roblox:", StringComparison.OrdinalIgnoreCase) ||
                args[i].StartsWith("roblox://", StringComparison.OrdinalIgnoreCase))
                return args[i];
        }

        return null;
    }
}
