using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AetherLauncher.Models;
using AetherLauncher.Services;
using AetherLauncher.ViewModels;

namespace AetherLauncher;

/// <summary>
/// Rendert jede Seite des Fensters als PNG. Damit lässt sich die Oberfläche prüfen und
/// dokumentieren, ohne ein Foto vom Bildschirm zu machen.
/// Aufruf: AetherLauncher.exe --screenshot &lt;zielordner&gt;
///
/// Die Bilder zeigen Beispieldaten (das offizielle Roblox-Konto und der Platz 1818), damit
/// die Listen nicht leer aussehen. Gespeicherte Konten des Benutzers werden nicht geschrieben.
/// </summary>
internal static class ScreenshotMode
{
    private static readonly string[] PageNames = { "home", "accounts", "engine", "settings", "log" };

    public static int Run(string outputDirectory)
    {
        try
        {
            Directory.CreateDirectory(outputDirectory);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Cannot create {outputDirectory}: {ex.Message}");
            return 1;
        }

        // Beim Schließen des Fensters würde WPF die Anwendung beenden - hier soll nur die
        // Aufnahme enden, also den automatischen Shutdown ausschalten.
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var task = ShootAsync(outputDirectory);
        var deadline = DateTime.UtcNow.AddSeconds(90);

        // Die Continuations brauchen die Nachrichtenschleife, deshalb wird gepumpt.
        while (!task.IsCompleted && DateTime.UtcNow < deadline) Pump(100);

        if (!task.IsCompleted)
        {
            Console.Error.WriteLine("Screenshot mode timed out.");
            return 1;
        }

        return task.GetAwaiter().GetResult();
    }

    private static async Task<int> ShootAsync(string outputDirectory)
    {
        var window = new MainWindow(null);
        window.Show();
        await WaitAsync(1200); // Client-Erkennung und Bilder abwarten

        if (window.DataContext is MainViewModel viewModel)
        {
            var account = new AccountItem(new AccountRecord
            {
                UserId = 1,
                Username = "Roblox",
                DisplayName = "Roblox",
            });
            account.Avatar = await ImageCache.GetAvatarAsync(1).ConfigureAwait(true);
            viewModel.Accounts.Add(account);
            viewModel.SelectedAccount = account;
            viewModel.UseCurrentLogin = false;

            var game = new PlaceItem(new PlaceRecord
            {
                PlaceId = 1818,
                Name = "Crossroads",
                LaunchCount = 12,
                LastPlayedAt = DateTimeOffset.Now.AddDays(-2),
            });

            var resolved = await RobloxApi.ResolveGameAsync(1818).ConfigureAwait(true);
            if (resolved is not null)
            {
                game.Record.Name = resolved.Value.Name;
                game.Record.UniverseId = resolved.Value.UniverseId;
                game.Icon = await ImageCache.GetGameIconAsync(resolved.Value.UniverseId).ConfigureAwait(true);
            }

            game.Refresh();
            viewModel.Places.Add(game);
            viewModel.SelectedPlace = game;
            // Keine Vorschau-Flags anheften: der Screenshot-Modus darf die gespeicherten
            // FastFlags des Benutzers nicht verändern.
            viewModel.Status = "Screenshot mode: sample account and sample game.";
            viewModel.AddGameOpen = false;

            // Die Versionsliste kommt von WEAO und Roblox - hier wirklich holen, damit das
            // Bild zeigt, was die Karte leistet (ohne Netz bleibt sie leer).
            if (viewModel.LoadBuildsCommand.CanExecute(null)) viewModel.LoadBuildsCommand.Execute(null);
            await WaitAsync(3000);
        }

        for (var page = 0; page < PageNames.Length; page++)
        {
            window.SelectPage(page);
            await WaitAsync(600); // die Einblendung der Karten auslaufen lassen

            var path = Path.Combine(outputDirectory, $"aether-{PageNames[page]}.png");
            Save(window, path);
            Console.WriteLine(path);

            // Die Einstellungen sind länger als das Fenster - der untere Teil als eigenes Bild.
            if (PageNames[page] == "settings")
            {
                window.ScrollSettingsToEnd();
                await WaitAsync(300);

                var lower = Path.Combine(outputDirectory, "aether-settings-lower.png");
                Save(window, lower);
                Console.WriteLine(lower);
            }
        }

        // Die Schnellansicht über der zuletzt gezeigten Seite aufnehmen.
        if (window.DataContext is MainViewModel layoutModel)
        {
            // Der Screenshot soll das Feature zeigen, auch wenn es in den echten
            // Einstellungen gerade ausgeschaltet ist. Danach wird der alte Wert wiederhergestellt.
            var overlayWasEnabled = layoutModel.OverlayEnabled;
            if (!overlayWasEnabled) layoutModel.OverlayEnabled = true;

            layoutModel.OpenLayoutCommand.Execute(null);
            await WaitAsync(800);

            var layout = Path.Combine(outputDirectory, "aether-layout.png");
            Save(window, layout);
            Console.WriteLine(layout);

            layoutModel.CloseLayoutCommand.Execute(null);
            await WaitAsync(400);
            if (!overlayWasEnabled) layoutModel.OverlayEnabled = false;
        }

        // Die Ingame-Platten: jede ist ein eigenes Fenster - fürs Bild werden sie so
        // zusammengelegt, wie sie auf dem Schirm nebeneinander stehen.
        var host = new OverlayHost(preview: true, settings: new LauncherSettings
        {
            LastPlaceId = 1818,
            OverlayShortcut = Shortcuts.Default,
            PlayfulMode = true,
        });

        host.Start();
        await WaitAsync(900);

        // Für die Aufnahme alle sechs Platten zeigen - auch Anti-AFK, das sonst zu ist.
        host.SetPanelVisible(PanelIds.AntiAfk, true);
        await WaitAsync(500);

        var overlayPath = Path.Combine(outputDirectory, "aether-ingame-overlay.png");
        SavePanels(host.Windows, overlayPath);
        Console.WriteLine(overlayPath);

        host.Quit();
        await WaitAsync(200);

        window.Hide();
        await WaitAsync(150);
        return 0;
    }

    /// <summary>Legt alle sichtbaren Overlay-Platten auf ein Bild - mit der Lage wie am Schirm.</summary>
    private static void SavePanels(IReadOnlyList<Window> windows, string path)
    {
        var visible = windows.Where(window => window.IsVisible).ToList();
        if (visible.Count == 0) return;

        const int margin = 16;
        var left = visible.Min(window => window.Left);
        var top = visible.Min(window => window.Top);
        var width = (int)Math.Ceiling(visible.Max(window => window.Left + window.ActualWidth) - left) + (margin * 2);
        var height = (int)Math.Ceiling(visible.Max(window => window.Top + window.ActualHeight) - top) + (margin * 2);

        var shots = visible.Select(window => (Window: window, Bitmap: RenderWindow(window))).ToList();

        var canvas = new DrawingVisual();
        using (var dc = canvas.RenderOpen())
        {
            // Ein dunkler Grund, damit die Platten auch als Datei lesbar bleiben.
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x10, 0x14, 0x1C)), null,
                new Rect(0, 0, Math.Max(1, width), Math.Max(1, height)));

            foreach (var (window, bitmap) in shots)
            {
                dc.DrawImage(bitmap, new Rect(
                    window.Left - left + margin,
                    window.Top - top + margin,
                    bitmap.Width,
                    bitmap.Height));
            }
        }

        var final = new RenderTargetBitmap(Math.Max(1, width), Math.Max(1, height), 96, 96, PixelFormats.Pbgra32);
        final.Render(canvas);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(final));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static RenderTargetBitmap RenderWindow(Window window)
    {
        var width = Math.Max(1, (int)Math.Round(window.ActualWidth));
        var height = Math.Max(1, (int)Math.Round(window.ActualHeight));

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        return bitmap;
    }

    private static void Save(Window window, string path)
    {
        var width = Math.Max(1, (int)Math.Round(window.ActualWidth));
        var height = Math.Max(1, (int)Math.Round(window.ActualHeight));

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>Lässt die Nachrichtenschleife laufen, damit Animationen und Layout fertig werden.</summary>
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(milliseconds),
            DispatcherPriority.Background,
            (_, _) => frame.Continue = false,
            Dispatcher.CurrentDispatcher);

        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    private static async Task WaitAsync(int milliseconds)
    {
        // Aufteilen, damit zwischendurch gepumpt wird (sonst steht der UI-Thread).
        var remaining = milliseconds;
        while (remaining > 0)
        {
            var slice = Math.Min(50, remaining);
            remaining -= slice;
            await Task.Delay(slice);
        }
    }
}
