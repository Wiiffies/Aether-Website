using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AetherLauncher.Controls;
using AetherLauncher.Models;
using AetherLauncher.Services;
using AetherLauncher.ViewModels;

namespace AetherLauncher;

/// <summary>
/// Oberflächen-Selbsttest: WPF meldet fehlerhafte Bindings nur im Debug-Output, die Anzeige
/// bleibt still kaputt. Dieser Test bindet eine Sammlung von Trace-Listenern ein, öffnet das
/// echte Fenster, schaltet alle Seiten durch und prüft, ob dabei Binding-Fehler auftreten.
/// Aufruf: AetherLauncher.exe --selftest-ui
/// </summary>
internal static class UiSelfTest
{
    /// <summary>
    /// Nur der Rad-Test: beweist, dass die Einstellungsseite beim Mausrad wirklich rollt,
    /// schnell aufeinanderfolgende Stöße sammelt und nicht an den Anfang springt.
    /// Aufruf: AetherLauncher.exe --selftest-scroll
    /// </summary>
    public static int RunScroll()
    {
        var report = new StringBuilder();

        MainWindow window;
        try
        {
            window = new MainWindow(null);
        }
        catch (Exception ex)
        {
            report.AppendLine($"  [FAIL] Das Fenster ließ sich nicht erzeugen: {ex.Message}");
            return Finish(report, failed: 1);
        }

        var failed = 0;

        try
        {
            window.Show();
            Pump(500);
            window.SelectPage(3);
            Pump(450);

            var viewer = window.PageSettings;
            var height = viewer.ScrollableHeight;

            if (height < 200)
            {
                report.AppendLine($"  [FAIL] Die Einstellungsseite ist nicht scrollbar (ScrollableHeight {height:0}).");
                failed = 1;
            }
            else
            {
                var start = Math.Min(160, height * 0.25);
                viewer.ScrollToVerticalOffset(start);
                Pump(80);
                var before = viewer.VerticalOffset;

                Wheel(viewer, -120);
                Pump(40);
                var early = viewer.VerticalOffset;
                Pump(450);
                var after = viewer.VerticalOffset;

                var noJump = early >= before - 1 && early > 0.5;
                var moved = after > before + 8;

                report.AppendLine(noJump && moved
                    ? $"  [OK  ] Mausrad rollt weich ({before:0} → {early:0} → {after:0})."
                    : $"  [FAIL] Mausrad: Start {before:0}, nach 40 ms {early:0}, Ende {after:0} - Sprung oder kein Weg.");

                if (!noJump || !moved) failed = 1;

                var rapidStart = viewer.VerticalOffset;
                for (var i = 0; i < 3; i++)
                {
                    Wheel(viewer, -120);
                    Pump(16);
                }

                Pump(450);
                var rapid = viewer.VerticalOffset;

                report.AppendLine(rapid > rapidStart + 10
                    ? $"  [OK  ] Drei schnelle Radstöße sammeln sich ({rapidStart:0} → {rapid:0})."
                    : $"  [FAIL] Drei schnelle Radstöße bewegen kaum: {rapidStart:0} → {rapid:0}.");

                if (rapid <= rapidStart + 10) failed = 1;

                Wheel(viewer, 120);
                Pump(450);
                var back = viewer.VerticalOffset;

                report.AppendLine(back < rapid - 4
                    ? $"  [OK  ] Zurückrollen funktioniert ({rapid:0} → {back:0})."
                    : $"  [FAIL] Zurückrollen bleibt stehen: {rapid:0} → {back:0}.");

                if (back >= rapid - 4) failed = 1;
            }
        }
        catch (Exception ex)
        {
            report.AppendLine($"  [FAIL] Rad-Test: {ex.GetType().Name} {ex.Message}");
            failed = 1;
        }
        finally
        {
            window.Close();
            Pump(80);
        }

        return Finish(report, failed);
    }

    private static void Wheel(ScrollViewer viewer, int delta)
    {
        viewer.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = viewer,
        });
    }

    /// <summary>
    /// Der Test kann zusätzlich den echten Mauszeiger über die Bauteile fahren. Nur so werden
    /// die Übergänge (Storyboards in den Vorlagen) wirklich ausgeführt: ein kaputter Pfad in
    /// einem Trigger fällt sonst erst beim Benutzer auf.
    /// </summary>
    public static int Run(bool hover = false)
    {
        var report = new StringBuilder();
        var messages = new List<string>();
        var hoverFailed = 0;
        var listener = new CollectingListener(messages);

        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;

        report.AppendLine();
        report.AppendLine("Aether Launcher - Oberflächen-Selbsttest");
        report.AppendLine(new string('-', 78));

        MainWindow window;
        try
        {
            window = new MainWindow(null);
        }
        catch (Exception ex)
        {
            report.AppendLine($"  [FAIL] Das Fenster ließ sich nicht erzeugen: {ex.Message}");
            return Finish(report, failed: 1);
        }

        window.Show();
        Pump(600);

        // Beispielzeilen einhängen, damit auch die Zeilenvorlagen (Konten, Spiele) Bindings auflösen.
        var viewModel = window.DataContext as MainViewModel;
        var restoreOverlayOff = viewModel is { OverlayEnabled: false };
        if (viewModel is not null)
        {
            viewModel.Accounts.Add(new AccountItem(new AccountRecord
            {
                UserId = 1,
                Username = "testuser",
                DisplayName = "Testkonto",
            }));

            viewModel.Places.Add(new PlaceItem(new PlaceRecord { PlaceId = 1818, Name = "Testspiel" }));
            viewModel.Presets.FirstOrDefault()!.IsMarked = true;

            // Der Test läuft mit den echten Einstellungen des Benutzers. Ist die Schnellansicht
            // dort absichtlich aus, wird sie nur für die Dauer des Tests eingeschaltet und
            // danach wieder zurückgestellt - sonst wären die beiden Tastaturprüfungen sinnlos.
            if (restoreOverlayOff) viewModel.OverlayEnabled = true;
            Pump(250);
        }

        var glides = new List<double>();
        for (var page = 0; page < 5; page++)
        {
            window.SelectPage(page);
            Pump(320);
            glides.Add(window.NavUnderlineX);

            if (page == 3) window.ScrollSettingsToEnd();
        }

        // Der Reiterstrich muss sich mitbewegen, sonst gleitet die Anzeige nicht mit.
        var glideMoved = glides.Distinct().Count() == glides.Count;
        report.AppendLine(glideMoved
            ? $"  [OK  ] Der Reiterstrich folgt dem Wechsel ({string.Join(", ", glides.Select(x => x.ToString("0")))})."
            : $"  [FAIL] Der Reiterstrich bleibt stehen ({string.Join(", ", glides.Select(x => x.ToString("0")))}).");

        // Ein Vorschlag muss sich sofort ins JSON schreiben (und wieder herausnehmen).
        try
        {
            var flagsFile = AppPaths.FastFlagsFile;
            var before = File.Exists(flagsFile) ? File.ReadAllText(flagsFile) : null;

            window.SelectPage(2);
            Pump(300);

            if (viewModel is null) throw new InvalidOperationException("Das Fenster hat kein ViewModel.");

            // Die Kartenansicht ist der Start: Karten und Filter müssen gefüllt sein, nicht nur
            // die (unsichtbare) JSON-Spalte dahinter.
            var visualCards = viewModel.VisiblePresets.Count;
            var visualCategories = viewModel.FastFlagCategories.Count;
            var visualStart = viewModel.FlagsVisualMode;

            report.AppendLine(visualStart && visualCards > 0 && visualCategories > 1
                ? $"  [OK  ] FastFlag-Kartenansicht mit {visualCards} Karten und {visualCategories} Filtern."
                : $"  [FAIL] Kartenansicht: sichtbar={visualStart}, Karten={visualCards}, Filter={visualCategories}.");

            if (!visualStart || visualCards == 0 || visualCategories <= 1) hoverFailed++;

            // Und der Umschalter muss beide Ansichten wirklich umschalten.
            viewModel.ShowJsonFlagsCommand.Execute(null);
            Pump(180);
            var jsonShown = viewModel.FlagsJsonVisibility == Visibility.Visible;
            viewModel.ShowVisualFlagsCommand.Execute(null);
            Pump(180);
            var visualShown = viewModel.FlagsVisualVisibility == Visibility.Visible;

            report.AppendLine(jsonShown && visualShown
                ? "  [OK  ] Visual/JSON-Umschalter wechselt beide Ansichten."
                : $"  [FAIL] Umschalter: JSON={jsonShown}, Visual={visualShown}.");

            if (!jsonShown || !visualShown) hoverFailed++;

            var preset = viewModel.Presets.FirstOrDefault(p => !p.IsMarked && p.Flags.Count > 0)
                         ?? throw new InvalidOperationException("Kein freier Vorschlag zum Prüfen gefunden.");
            var key = preset.Flags[0].Key;

            preset.IsMarked = true;
            Pump(220);
            var written = viewModel.FastFlagsJson.Contains(key, StringComparison.Ordinal);

            preset.IsMarked = false;
            Pump(220);
            var removed = !viewModel.FastFlagsJson.Contains(key, StringComparison.Ordinal);

            report.AppendLine(written && removed
                ? $"  [OK  ] '{preset.Name}' schreibt sich sofort ins JSON und nimmt sich wieder heraus."
                : $"  [FAIL] Vorschlag im JSON: geschrieben={written}, wieder entfernt={removed}.");

            if (!written || !removed) hoverFailed++;

            // Die Prüfung hat die Datei geschrieben - hier wieder auf den alten Stand bringen.
            if (before is not null) File.WriteAllText(flagsFile, before);
        }
        catch (Exception ex)
        {
            report.AppendLine($"  [FAIL] Vorschlag ließ sich nicht prüfen: {ex.GetType().Name} {ex.Message}");
            hoverFailed++;
        }

        // Ein echter Mausklick auf eine Schaltfläche: dabei läuft der Klick-Ring los. Genau
        // hier starb die App früher lautlos, weil die Animation von einem nicht gesetzten
        // Canvas.Left (NaN) aus rechnen wollte. Format und Save sind die harmlose Variante
        // desselben Wegs - danach wird FastFlags.json wieder auf den alten Stand gebracht.
        try
        {
            var flagsFile = AppPaths.FastFlagsFile;
            var before = File.Exists(flagsFile) ? File.ReadAllText(flagsFile) : null;

            window.SelectPage(2);
            Pump(320);

            // Die JSON-Werkzeugleiste liegt hinter dem Visual-Umschalter; für den echten
            // Klick-Test erst auf JSON stellen.
            viewModel?.ShowJsonFlagsCommand.Execute(null);
            Pump(240);

            var format = FindButtonByText(window, "Format");
            var save = FindButtonByText(window, "Save");

            if (format is null || save is null)
            {
                report.AppendLine("  [FAIL] 'Format' oder 'Save' im FastFlags-Editor nicht gefunden.");
                hoverFailed++;
            }
            else
            {
                window.Topmost = true;
                window.Activate();
                Pump(180);

                var clicked = 0;
                var landed = 0;
                var covered = 0;
                var coverNote = "";

                foreach (var target in new[] { format, save })
                {
                    if (!MovePointerTo(window, target))
                    {
                        covered++;
                        coverNote = WindowUnderCursor();
                        report.AppendLine($"  [SKIP] '{target.Content}' liegt nicht frei - dort ist {coverNote}.");
                        continue;
                    }

                    // Der Klick muss auch ankommen: ändert sich der Statustext nicht, hat der
                    // Test daneben geklickt und würde den Absturz gar nicht bemerken.
                    var statusBefore = viewModel?.Status ?? "";
                    ClickLeftButton();
                    Pump(520);
                    clicked++;

                    if (!string.Equals(viewModel?.Status ?? "", statusBefore, StringComparison.Ordinal)) landed++;
                }

                window.Topmost = false;

                if (clicked == 2 && landed == 2)
                {
                    report.AppendLine("  [OK  ] Echte Klicks auf 'Format' und 'Save' überlebt (Klick-Ring gelaufen, Befehl ausgelöst).");
                }
                else if (clicked < 2 && clicked + covered == 2)
                {
                    // Ein anderes Fenster lag über den Knöpfen (z. B. das laufende Overlay
                    // über einem Spiel). Dann lässt sich das hier nicht prüfen - ehrlich melden.
                    report.AppendLine($"  [SKIP] Klicks nicht prüfbar - über den Knöpfen lag {coverNote}.");
                }
                else
                {
                    report.AppendLine($"  [FAIL] Klicks: {clicked} von 2 ausgelöst, {landed} von 2 angekommen - unter dem Zeiger lag {WindowUnderCursor()}.");
                    hoverFailed++;
                }
            }

            if (before is not null) File.WriteAllText(flagsFile, before);
            else if (File.Exists(flagsFile)) File.Delete(flagsFile);
        }
        catch (Exception ex)
        {
            report.AppendLine($"  [FAIL] Klicken ließ sich nicht prüfen: {ex.GetType().Name} {ex.Message}");
            hoverFailed++;
        }

        // Schnellansicht: aufgehen, schließen und das Kürzel umbelegen und zurücksetzen.
        try
        {
            if (viewModel is null) throw new InvalidOperationException("Das Fenster hat kein ViewModel.");

            var overlay = window.FindName("LayoutOverlay") as FrameworkElement;
            if (overlay is null) throw new InvalidOperationException("LayoutOverlay fehlt im Fenster.");

            viewModel.OpenLayoutCommand.Execute(null);
            Pump(700);
            var opened = overlay.Visibility == Visibility.Visible && overlay.Opacity > 0.8;

            viewModel.CloseLayoutCommand.Execute(null);
            Pump(420);
            var closed = overlay.Visibility == Visibility.Collapsed;

            report.AppendLine(opened && closed
                ? "  [OK  ] Schnellansicht geht auf und wieder zu."
                : $"  [FAIL] Schnellansicht: auf={opened} (Deckkraft {overlay.Opacity:0.00}), zu={closed}.");

            if (!opened || !closed) hoverFailed++;

            viewModel.ChangeShortcutCommand.Execute(null);
            var applied = viewModel.TryApplyShortcut(ModifierKeys.Control, Key.L);
            var relabelled = viewModel.LayoutShortcutLabel == "Ctrl+L";

            viewModel.ResetShortcutCommand.Execute(null);
            var reset = viewModel.LayoutShortcutLabel == Shortcuts.Default;

            report.AppendLine(applied && relabelled && reset
                ? "  [OK  ] Kürzel ließ sich auf Ctrl+L legen und wieder zurücksetzen."
                : $"  [FAIL] Kürzel: gesetzt={applied} ({viewModel.LayoutShortcutLabel}), zurückgesetzt={reset}.");

            if (!applied || !relabelled || !reset) hoverFailed++;
        }
        catch (Exception ex)
        {
            report.AppendLine($"  [FAIL] Schnellansicht ließ sich nicht prüfen: {ex.GetType().Name} {ex.Message}");
            hoverFailed++;
        }

        // Das Ingame-Overlay besteht aus mehreren Platten: Chips, Serverwechsel, Fenster.
        try
        {
            var host = new OverlayHost(preview: true, settings: new LauncherSettings
            {
                LastPlaceId = 1818,
                OverlayShortcut = Shortcuts.Default,
            });

            host.Start();
            Pump(700);

            var fpsWindow = host.WindowFor(PanelIds.Fps);
            var serverWindow = host.WindowFor(PanelIds.Server);
            var chips = fpsWindow is null
                ? 0
                : Descendants(fpsWindow).OfType<RadioButton>().Count(box => box.GroupName == "Fps");
            var hop = serverWindow is null ? null : FindButtonByText(serverWindow, "Leave & rejoin");
            var shown = host.Windows.Count(window => window.IsVisible) >= 5;

            // Die Startaufteilung darf sich nicht selbst überdecken - sonst verdeckt die
            // untere Platte den Rest der oberen, sobald neue Zeilen dazukommen.
            var openPlates = host.Windows.Where(plate => plate.IsVisible).ToList();
            var overlaps = 0;
            for (var a = 0; a < openPlates.Count; a++)
            {
                for (var b = a + 1; b < openPlates.Count; b++)
                {
                    var first = new Rect(openPlates[a].Left, openPlates[a].Top,
                        openPlates[a].ActualWidth, openPlates[a].ActualHeight);
                    var second = new Rect(openPlates[b].Left, openPlates[b].Top,
                        openPlates[b].ActualWidth, openPlates[b].ActualHeight);
                    var shared = Rect.Intersect(first, second);
                    if (!shared.IsEmpty && shared.Width > 4 && shared.Height > 4) overlaps++;
                }
            }

            // Alt+Tab und Win+Tab dürfen die Platten nicht als eigene Fenster zeigen:
            // jede sichtbare Platte muss den Toolwindow-Stil tragen und darf kein Appwindow
            // sein. Versteckte Platten sind egal - sie werden erst beim Öffnen geprüft
            // (und bekommen den Stil dann nach dem Anzeigen erneut).
            var inTaskSwitcher = openPlates.Count(plate => !plate.HiddenFromTaskSwitcher);

            report.AppendLine(chips == 5 && hop is not null && shown && inTaskSwitcher == 0 && overlaps == 0
                ? "  [OK  ] Ingame-Overlay rendert (6 Platten, 5 Chips, überlappungsfrei, aus Alt+Tab ausgeblendet)."
                : $"  [FAIL] Ingame-Overlay: Platten={host.Windows.Count}, Chips={chips}, " +
                  $"Serverwechsel={hop is not null}, sichtbar={shown}, Überdeckungen={overlaps}, " +
                  $"im Fensterwechsler={inTaskSwitcher}.");

            if (chips != 5 || hop is null || !shown || inTaskSwitcher != 0 || overlaps != 0) hoverFailed++;

            // Eine verborgene Platte über das Menü einschalten: sie muss in ihrer Spalte
            // unter dem stehen, was dort schon liegt - nicht bei 0,0 über dem Menü.
            host.SetPanelVisible(PanelIds.AntiAfk, true);
            Pump(420);
            var anti = host.WindowFor(PanelIds.AntiAfk);
            var antiPlaced = anti is { IsVisible: true } && (anti.Left != 0 || anti.Top != 0);
            var server = host.WindowFor(PanelIds.Server);
            var antiClear = true;
            if (anti is not null && server is not null)
            {
                var first = new Rect(anti.Left, anti.Top, anti.ActualWidth, anti.ActualHeight);
                var second = new Rect(server.Left, server.Top, server.ActualWidth, server.ActualHeight);
                var shared = Rect.Intersect(first, second);
                antiClear = shared.IsEmpty || shared.Width <= 4 || shared.Height <= 4;
            }

            report.AppendLine(antiPlaced && antiClear
                ? "  [OK  ] Anti-AFK einschalten setzt die Platte in ihre Spalte."
                : "  [FAIL] Anti-AFK landete falsch oder über einer anderen Platte.");
            if (!antiPlaced || !antiClear) hoverFailed++;

            // Deckkraft: die Platten müssen dem Wert folgen, ohne dass etwas flackert.
            host.SetPanelOpacity(80);
            Pump(320);
            var faded = host.Windows.All(plate => !plate.IsVisible || Math.Abs(plate.Opacity - 0.8) < 0.03);
            host.SetPanelOpacity(100);
            Pump(220);

            report.AppendLine(faded
                ? "  [OK  ] Deckkraft der Platten folgt dem Wert (80 %)."
                : "  [FAIL] Die Platten ignorierten die Deckkraft.");
            if (!faded) hoverFailed++;

            // Ziehen mit dem echten Zeiger: Die Platte muss folgen, nicht springen.
            var menu = host.WindowFor(PanelIds.Menu);
            if (menu is null || !menu.IsVisible)
            {
                report.AppendLine("  [SKIP] Das Menü ist nicht sichtbar - Ziehen nicht prüfbar.");
            }
            else
            {
                var startLeft = menu.Left;
                var startTop = menu.Top;
                var anchor = menu.PointToScreen(new Point(110, 12));

                SetCursorPos((int)anchor.X, (int)anchor.Y);
                Pump(90);
                mouse_event(LeftDown, 0, 0, 0, UIntPtr.Zero);
                Pump(90);

                for (var step = 1; step <= 6; step++)
                {
                    SetCursorPos((int)anchor.X + (step * 9), (int)anchor.Y + (step * 4));
                    Pump(30);
                }

                mouse_event(LeftUp, 0, 0, 0, UIntPtr.Zero);
                Pump(520);

                var dx = menu.Left - startLeft;
                var dy = menu.Top - startTop;
                var dragged = dx > 25 && dy > 10;

                report.AppendLine(dragged
                    ? $"  [OK  ] Ziehen folgt dem Zeiger und rastet ein (Δ {dx:0}×{dy:0})."
                    : $"  [FAIL] Ziehen bewegte die Platte kaum (Δ {dx:0}×{dy:0}).");
                if (!dragged) hoverFailed++;
            }

            host.Quit();
            Pump(250);
        }
        catch (Exception ex)
        {
            report.AppendLine($"  [FAIL] Ingame-Overlay ließ sich nicht prüfen: {ex.GetType().Name} {ex.Message}");
            hoverFailed++;
        }

        // Das eingestellte Kürzel muss die Schnellansicht wirklich öffnen - mit einem echten
        // Tastendruck, sonst prüft der Test nur, dass der Befehl funktioniert, nicht das Kürzel.
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            window.Topmost = true;
            window.Activate();
            SetForegroundWindow(handle);
            // Ein Eingabefeld aus dem vorherigen Test darf eine blanke Taste nicht schlucken.
            // Ein normaler Reiter ist der realistische Fokus für den Tastendruck.
            Keyboard.ClearFocus();
            window.NavHome.Focus();
            Pump(240);

            if (GetForegroundWindow() != handle)
            {
                report.AppendLine("  [SKIP] Das Fenster kam nicht in den Vordergrund - Tastendruck nicht prüfbar.");
            }
            else if (viewModel is null)
            {
                throw new InvalidOperationException("Das Fenster hat kein ViewModel.");
            }
            else
            {
                viewModel.CloseLayoutCommand.Execute(null);
                Pump(240);

                // Geprüft wird genau das eingestellte Kürzel - es darf sich ändern.
                var shortcut = viewModel.Settings.OverlayShortcut;
                var keys = VirtualKeys(shortcut);

                if (keys is null)
                {
                    report.AppendLine($"  [FAIL] Kürzel {shortcut} ließ sich nicht in Tastendrücke übersetzen.");
                    hoverFailed++;
                }
                else
                {
                    PressKeys(keys);
                    Pump(420);
                    var opened = viewModel.LayoutOpen;

                    PressKeys(VkEscape);
                    Pump(420);
                    var closed = !viewModel.LayoutOpen;

                    report.AppendLine(opened && closed
                        ? $"  [OK  ] {shortcut} öffnet die Schnellansicht, Esc schließt sie."
                        : $"  [FAIL] {shortcut} geöffnet={opened}, Esc geschlossen={closed}.");

                    if (!opened || !closed) hoverFailed++;
                }
            }

            window.Topmost = false;
        }
        catch (Exception ex)
        {
            report.AppendLine($"  [FAIL] Tastendruck ließ sich nicht prüfen: {ex.GetType().Name} {ex.Message}");
            hoverFailed++;
        }

        // Schalter wirklich umlegen: dabei laufen die Übergänge der Gleise und Knöpfe.
        try
        {
            window.SelectPage(1);
            Pump(220);

            var toggle = Find<CheckBox>(window, box => box.IsChecked == true);
            if (toggle is null)
            {
                report.AppendLine("  [FAIL] Kein eingeschalteter Schalter zum Umlegen gefunden.");
                hoverFailed++;
            }
            else
            {
                toggle.IsChecked = false;
                Pump(260);
                toggle.IsChecked = true;
                Pump(260);
                report.AppendLine("  [OK  ] Schalter hin und zurück geschaltet (Übergänge gelaufen).");
            }
        }
        catch (Exception ex)
        {
            report.AppendLine($"  [FAIL] Schalter ließ sich nicht umlegen: {ex.GetType().Name} {ex.Message}");
            hoverFailed++;
        }

        if (hover)
        {
            report.AppendLine("  Überfahren mit dem echten Zeiger (Playful-Animationen):");
            foreach (var (page, description, find) in HoverTargets)
            {
                try
                {
                    window.SelectPage(page);
                    window.ScrollSettingsToEnd();
                    Pump(260);

                    var target = find(window) as FrameworkElement;
                    if (target is null)
                    {
                        report.AppendLine($"    [FAIL] {description}: Bauteil nicht gefunden.");
                        continue;
                    }

                    if (!MovePointerTo(window, target))
                    {
                        report.AppendLine($"    [SKIP] {description}: Zeiger ließ sich nicht setzen.");
                        continue;
                    }

                    Pump(320);
                    report.AppendLine($"    [OK  ] {description}");
                }
                catch (Exception ex)
                {
                    report.AppendLine($"    [FAIL] {description}: {ex.GetType().Name} {ex.Message}");
                    hoverFailed++;
                }
            }
        }

        // Rahmenlose Fenster decken beim Maximieren gern die Taskleiste ab: prüfen,
        // ob das Fenster in der Arbeitsfläche bleibt.
        var fitsWorkArea = true;
        try
        {
            window.WindowState = WindowState.Maximized;
            Pump(450);
            var work = SystemParameters.WorkArea;
            fitsWorkArea = window.ActualHeight <= work.Height + 1 && window.ActualWidth <= work.Width + 1;
            report.AppendLine(fitsWorkArea
                ? $"  [OK  ] Maximiert bleibt im Bildschirmbereich ({window.ActualWidth:0}x{window.ActualHeight:0} in {work.Width:0}x{work.Height:0})."
                : $"  [FAIL] Maximiert ist das Fenster größer als der Bildschirmbereich ({window.ActualWidth:0}x{window.ActualHeight:0} in {work.Width:0}x{work.Height:0}).");
            window.WindowState = WindowState.Normal;
            Pump(250);
        }
        catch (Exception ex)
        {
            report.AppendLine($"  [FAIL] Maximieren ließ sich nicht prüfen: {ex.Message}");
            fitsWorkArea = false;
        }

        if (restoreOverlayOff && viewModel is not null)
        {
            viewModel.OverlayEnabled = false;
            Pump(120);
        }

        window.Close();
        Pump(150);

        var failures = messages
            .Where(m => m.Contains("path error", StringComparison.OrdinalIgnoreCase) ||
                        m.Contains("Cannot find source", StringComparison.OrdinalIgnoreCase) ||
                        m.Contains("Cannot resolve", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!glideMoved) failures.Add("Der Reiterstrich folgt dem Seitenwechsel nicht.");

        if (failures.Count == 0)
        {
            report.AppendLine("  [OK  ] Alle Bindings aller fünf Seiten wurden aufgelöst.");
        }

        // Erst die echten Fehler - auch dann, wenn es zusätzlich Hinweise gab.
        foreach (var failure in failures) report.AppendLine($"  [FAIL] {Log.Redact(failure.Trim())}");

        if (messages.Count > 0)
        {
            report.AppendLine($"  Hinweise ({messages.Count}), nicht als Fehler gewertet:");
            foreach (var message in messages.Take(10))
                report.AppendLine($"    - {Log.Redact(message.Trim())}");
        }

        if (hoverFailed > 0) failures.Add($"{hoverFailed} Bauteil(e) ließen sich nicht überfahren.");

        return Finish(report, failures.Count == 0 && fitsWorkArea && hoverFailed == 0 ? 0 : 1);
    }

    /// <summary>Lässt die WPF-Nachrichtenschleife weiterlaufen, damit Layout und Bindings fertig werden.</summary>
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

    /// <summary>Bauteile, über die der Zeiger im erweiterten Test fährt.</summary>
    private static readonly (int Page, string Description, Func<Window, object?> Find)[] HoverTargets =
    {
        (0, "Hero-Karte", w => Find<CardPanel>(w, panel => panel.Name == "HeroCard")),
        (0, "Schaltfläche 'Add game'", w => FindFirst<RippleButton>(w)),
        (1, "Zeile in der Kontenliste", w => FindFirst<ListBoxItem>(w)),
        (2, "Vorschlagszeile im FastFlags-Editor", w => FindFirst<CheckBox>(w)),
        (3, "Umschalter in den Einstellungen", w => FindFirst<CheckBox>(w)),
        (4, "Log-Fläche", w => FindFirst<TextBox>(w, box => box.Name == "LogBox")),
    };

    private static T? FindFirst<T>(DependencyObject root, Func<T, bool>? match = null) where T : DependencyObject
        => Descendants(root).OfType<T>().FirstOrDefault(item => match is null || match(item));

    private static T? Find<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
        => Descendants(root).OfType<T>().FirstOrDefault(match);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;

            foreach (var grandChild in Descendants(child)) yield return grandChild;
        }
    }

    /// <summary>Sucht die Schaltfläche mit diesem Aufdruck (Text im Inhalt oder als TextBlock).</summary>
    private static RippleButton? FindButtonByText(DependencyObject root, string text)
    {
        foreach (var button in Descendants(root).OfType<RippleButton>())
        {
            if (button.Content is string label && label.Equals(text, StringComparison.Ordinal)) return button;
            if (Descendants(button).OfType<TextBlock>().Any(t => t.Text.Equals(text, StringComparison.Ordinal)))
                return button;
        }

        return null;
    }

    /// <summary>Echter Linksklick auf die aktuelle Zeigerposition.</summary>
    private static void ClickLeftButton()
    {
        mouse_event(LeftDown, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(30);
        mouse_event(LeftUp, 0, 0, 0, UIntPtr.Zero);
    }

    private const uint LeftDown = 0x0002;
    private const uint LeftUp = 0x0004;

    private const byte VkTab = 0x09;
    private const byte VkShift = 0x10;
    private const byte VkControl = 0x11;
    private const byte VkMenu = 0x12;
    private const byte VkEscape = 0x1B;
    private const byte VkLWin = 0x5B;
    private const uint KeyUp = 0x0002;

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    /// <summary>
    /// Übersetzt ein Kürzel in die virtuellen Tasten, die dafür zu drücken sind - erst die
    /// Zusatztasten, dann die eigentliche. So prüft der Test immer das, was gerade eingestellt ist.
    /// </summary>
    private static byte[]? VirtualKeys(string shortcut)
    {
        if (!Shortcuts.TryParse(shortcut, out var modifiers, out var key)) return null;

        var keys = new List<byte>();
        if ((modifiers & ModifierKeys.Control) != 0) keys.Add(VkControl);
        if ((modifiers & ModifierKeys.Alt) != 0) keys.Add(VkMenu);
        if ((modifiers & ModifierKeys.Shift) != 0) keys.Add(VkShift);
        if ((modifiers & ModifierKeys.Windows) != 0) keys.Add(VkLWin);

        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0) return null;

        keys.Add((byte)virtualKey);
        return keys.ToArray();
    }

    /// <summary>Drückt Tasten nacheinander und lässt sie in umgekehrter Reihenfolge los.</summary>
    private static void PressKeys(params byte[] keys)
    {
        foreach (var key in keys) keybd_event(key, 0, 0, UIntPtr.Zero);
        Thread.Sleep(45);
        for (var index = keys.Length - 1; index >= 0; index--) keybd_event(keys[index], 0, KeyUp, UIntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    /// <summary>
    /// Setzt den Mauszeiger auf die Mitte eines Bauteils (nur Windows, nur im Test). Liefert
    /// false, wenn dort nicht mehr unser Fenster liegt: ein Klick träfe dann ein fremdes
    /// Programm, und der Test würde einen Fehler melden, den es nicht gibt.
    /// </summary>
    private static bool MovePointerTo(Window window, FrameworkElement element)
    {
        if (element.ActualWidth <= 1 || element.ActualHeight <= 1) return false;

        var center = element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        if (!SetCursorPos((int)center.X, (int)center.Y)) return false;

        if (!GetCursorPos(out var point)) return true;
        var under = WindowFromPoint(new NativePoint { X = point.X, Y = point.Y });
        if (under == IntPtr.Zero) return false;

        GetWindowThreadProcessId(under, out var processId);
        return processId == (uint)Environment.ProcessId;
    }

    /// <summary>Was liegt gerade unter dem Zeiger? Macht aus einem Rätsel einen Befund.</summary>
    private static string WindowUnderCursor()
    {
        if (!GetCursorPos(out var point)) return "nicht feststellbar";

        var handle = WindowFromPoint(new NativePoint { X = point.X, Y = point.Y });
        if (handle == IntPtr.Zero) return $"kein Fenster an {point.X},{point.Y}";

        GetWindowThreadProcessId(handle, out var processId);
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return $"{process.ProcessName} (PID {processId}) an {point.X},{point.Y}";
        }
        catch
        {
            return $"PID {processId} an {point.X},{point.Y}";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    // WindowFromPoint erwartet zwei 32-Bit-Ganzzahlen (POINT). WPF-<see cref="Point"/>
    // besteht dagegen aus zwei Doubles - damit kamen hier unsinnige Koordinaten an, und
    // der Test meldete "kein Fenster" an einer Stelle, an der das Fenster steht.
    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);


    private static int Finish(StringBuilder report, int failed)
    {
        report.AppendLine(new string('-', 78));
        report.AppendLine(failed == 0
            ? "  Oberfläche in Ordnung."
            : $"  {failed} Binding-Fehler gefunden.");
        report.AppendLine();

        var text = report.ToString();
        try
        {
            File.WriteAllText(Path.Combine(AppPaths.DataDir, "selftest-ui.log"), text);
        }
        catch
        {
            // Nicht kritisch.
        }

        try
        {
            Console.Write(text);
        }
        catch
        {
            // Siehe selftest-ui.log.
        }

        return failed;
    }

    private sealed class CollectingListener : TraceListener
    {
        private readonly List<string> _messages;

        public CollectingListener(List<string> messages) => _messages = messages;

        public override void Write(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message)) _messages.Add(message);
        }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message)) _messages.Add(message);
        }
    }
}
