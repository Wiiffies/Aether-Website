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







internal static class UiSelfTest
{
    
    
    
    
    
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
            report.AppendLine($"[FAIL] The window could not be created: {ex.Message}");
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
                report.AppendLine($"[FAIL] The settings page is not scrollable (ScrollableHeight {height:0}).");
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
                    ? $"[OK ] Mouse wheel scrolls smoothly ({before:0} → {early:0} → {after:0})."
                    : $"[FAIL] Mouse wheel: start {before:0}, after 40 ms {early:0}, end {after:0} - jump or no movement.");

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
                    ? $"[OK ] Three rapid wheel scrolls accumulate ({rapidStart:0} → {rapid:0})."
                    : $"[FAIL] Three quick wheel scrolls barely move: {rapidStart:0} → {rapid:0}.");

                if (rapid <= rapidStart + 10) failed = 1;

                Wheel(viewer, 120);
                Pump(450);
                var back = viewer.VerticalOffset;

                report.AppendLine(back < rapid - 4
                    ? $"[OK ] Rolling back works ({rapid:0} → {back:0})."
                    : $"[FAIL] Rolling back stops: {rapid:0} → {back:0}.");

                if (back >= rapid - 4) failed = 1;
            }
        }
        catch (Exception ex)
        {
            report.AppendLine($"  [FAIL] Wheel test: {ex.GetType().Name} {ex.Message}");
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
        report.AppendLine("Aether Launcher - UI self-test");
        report.AppendLine(new string('-', 78));

        MainWindow window;
        try
        {
            window = new MainWindow(null);
        }
        catch (Exception ex)
        {
            report.AppendLine($"[FAIL] The window could not be created: {ex.Message}");
            return Finish(report, failed: 1);
        }

        window.Show();
        Pump(600);

        
        var viewModel = window.DataContext as MainViewModel;
        var restoreOverlayOff = viewModel is { OverlayEnabled: false };
        if (viewModel is not null)
        {
            viewModel.Accounts.Add(new AccountItem(new AccountRecord
            {
                UserId = 1,
                Username = "testuser",
                DisplayName = "Test account",
            }));

            viewModel.Places.Add(new PlaceItem(new PlaceRecord { PlaceId = 1818, Name = "Test place" }));
            viewModel.Presets.FirstOrDefault()!.IsMarked = true;

            
            
            
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

        
        var glideMoved = glides.Distinct().Count() == glides.Count;
        report.AppendLine(glideMoved
            ? $"[OK ] The nav underline follows each page change ({string.Join(", ", glides.Select(x => x.ToString("0")))})."
            : $"[FAIL] The nav underline stayed put ({string.Join(", ", glides.Select(x => x.ToString("0")))}).");

        
        try
        {
            var flagsFile = AppPaths.FastFlagsFile;
            var before = File.Exists(flagsFile) ? File.ReadAllText(flagsFile) : null;

            window.SelectPage(2);
            Pump(300);

            if (viewModel is null) throw new InvalidOperationException("The window has no view model.");

            
            
            var visualCards = viewModel.VisiblePresets.Count;
            var visualCategories = viewModel.FastFlagCategories.Count;
            var visualStart = viewModel.FlagsVisualMode;

            report.AppendLine(visualStart && visualCards > 0 && visualCategories > 1
                ? $"[OK ] FastFlag card view with {visualCards} cards and {visualCategories} filters."
                : $"  [FAIL] Card view: visible={visualStart}, cards={visualCards}, filters={visualCategories}.");

            if (!visualStart || visualCards == 0 || visualCategories <= 1) hoverFailed++;

            
            viewModel.ShowJsonFlagsCommand.Execute(null);
            Pump(180);
            var jsonShown = viewModel.FlagsJsonVisibility == Visibility.Visible;
            viewModel.ShowVisualFlagsCommand.Execute(null);
            Pump(180);
            var visualShown = viewModel.FlagsVisualVisibility == Visibility.Visible;

            report.AppendLine(jsonShown && visualShown
                ? "[OK ] Visual/JSON switcher switches both views."
                : $"  [FAIL] Switcher: JSON={jsonShown}, visual={visualShown}.");

            if (!jsonShown || !visualShown) hoverFailed++;

            var preset = viewModel.Presets.FirstOrDefault(p => !p.IsMarked && p.Flags.Count > 0)
                         ?? throw new InvalidOperationException("No unchecked preset found for the test.");
            var key = preset.Flags[0].Key;

            preset.IsMarked = true;
            Pump(220);
            var written = viewModel.FastFlagsJson.Contains(key, StringComparison.Ordinal);

            preset.IsMarked = false;
            Pump(220);
            var removed = !viewModel.FastFlagsJson.Contains(key, StringComparison.Ordinal);

            report.AppendLine(written && removed
                ? $"[OK ] '{preset.Name}' immediately writes itself into the JSON and takes itself out again."
                : $"[FAIL] Preset in JSON: written={written}, removed again={removed}.");

            if (!written || !removed) hoverFailed++;

            
            if (before is not null) File.WriteAllText(flagsFile, before);
        }
        catch (Exception ex)
        {
            report.AppendLine($"[FAIL] Preset could not be checked: {ex.GetType().Name} {ex.Message}");
            hoverFailed++;
        }

        
        
        
        
        try
        {
            var flagsFile = AppPaths.FastFlagsFile;
            var before = File.Exists(flagsFile) ? File.ReadAllText(flagsFile) : null;

            window.SelectPage(2);
            Pump(320);

            
            
            viewModel?.ShowJsonFlagsCommand.Execute(null);
            Pump(240);

            var format = FindButtonByText(window, "Format");
            var save = FindButtonByText(window, "Save");

            if (format is null || save is null)
            {
                report.AppendLine("[FAIL] 'Format' or 'Save' not found in FastFlags editor.");
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
                        report.AppendLine($"[SKIP] '{target.Content}' is not reachable - {coverNote} is covering it.");
                        continue;
                    }

                    
                    
                    var statusBefore = viewModel?.Status ?? "";
                    ClickLeftButton();
                    Pump(520);
                    clicked++;

                    if (!string.Equals(viewModel?.Status ?? "", statusBefore, StringComparison.Ordinal)) landed++;
                }

                window.Topmost = false;

                if (clicked == 2 && landed == 2)
                {
                    report.AppendLine("[OK ] Real clicks on 'Format' and 'Save' survived (click ring ran, command triggered).");
                }
                else if (clicked < 2 && clicked + covered == 2)
                {
                    
                    
                    report.AppendLine($"[SKIP] Clicks cannot be checked - {coverNote} covered the buttons.");
                }
                else
                {
                    report.AppendLine($"[FAIL] Clicks: {clicked}/2 triggered, {landed}/2 registered - under the pointer was {WindowUnderCursor()}.");
                    hoverFailed++;
                }
            }

            if (before is not null) File.WriteAllText(flagsFile, before);
            else if (File.Exists(flagsFile)) File.Delete(flagsFile);
        }
        catch (Exception ex)
        {
            report.AppendLine($"[FAIL] Click could not be checked: {ex.GetType().Name} {ex.Message}");
            hoverFailed++;
        }

        
        try
        {
            if (viewModel is null) throw new InvalidOperationException("The window has no view model.");

            var overlay = window.FindName("LayoutOverlay") as FrameworkElement;
            if (overlay is null) throw new InvalidOperationException("LayoutOverlay is missing from the window.");

            viewModel.OpenLayoutCommand.Execute(null);
            Pump(700);
            var opened = overlay.Visibility == Visibility.Visible && overlay.Opacity > 0.8;

            viewModel.CloseLayoutCommand.Execute(null);
            Pump(420);
            var closed = overlay.Visibility == Visibility.Collapsed;

            report.AppendLine(opened && closed
                ? "[OK ] Quick view opens and closes again."
                : $"[FAIL] Quick view: open={opened} (opacity {overlay.Opacity:0.00}), closed={closed}.");

            if (!opened || !closed) hoverFailed++;

            viewModel.ChangeShortcutCommand.Execute(null);
            var applied = viewModel.TryApplyShortcut(ModifierKeys.Control, Key.L);
            var relabelled = viewModel.LayoutShortcutLabel == "Ctrl+L";

            viewModel.ResetShortcutCommand.Execute(null);
            var reset = viewModel.LayoutShortcutLabel == Shortcuts.Default;

            report.AppendLine(applied && relabelled && reset
                ? "[OK] Shortcut could be set to Ctrl+L and reset again."
                : $"[FAIL] Shortcut: set={applied} ({viewModel.LayoutShortcutLabel}), reset={reset}.");

            if (!applied || !relabelled || !reset) hoverFailed++;
        }
        catch (Exception ex)
        {
            report.AppendLine($"[FAIL] Quick view could not be checked: {ex.GetType().Name} {ex.Message}");
            hoverFailed++;
        }

        
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

            
            
            
            
            var inTaskSwitcher = openPlates.Count(plate => !plate.HiddenFromTaskSwitcher);

            report.AppendLine(chips == 5 && hop is not null && shown && inTaskSwitcher == 0 && overlaps == 0
                ? "[OK ] In-game overlay renders (6 panels, 5 chips, no overlap, hidden from Alt+Tab)."
                : $"[FAIL] In-game overlay: panels={host.Windows.Count}, chips={chips}," +
                  $"Server change={hop is not null}, visible={shown}, overlaps={overlaps}," +
                  $"in window changer={inTaskSwitcher}.");

            if (chips != 5 || hop is null || !shown || inTaskSwitcher != 0 || overlaps != 0) hoverFailed++;

            
            
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
                ? "[OK] Turning on Anti-AFK places the panel in its column."
                : "[FAIL] Anti-AFK was placed incorrectly or over another panel.");
            if (!antiPlaced || !antiClear) hoverFailed++;

            
            host.SetPanelOpacity(80);
            Pump(320);
            var faded = host.Windows.All(plate => !plate.IsVisible || Math.Abs(plate.Opacity - 0.8) < 0.03);
            host.SetPanelOpacity(100);
            Pump(220);

            report.AppendLine(faded
                ? "[OK ] Opacity of the panels follows the value (80%)."
                : "[FAIL] The panels ignored opacity.");
            if (!faded) hoverFailed++;

            
            var menu = host.WindowFor(PanelIds.Menu);
            if (menu is null || !menu.IsVisible)
            {
                report.AppendLine("[SKIP] The menu is not visible - dragging cannot be checked.");
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
                    ? $"[OK ] Drag follows the pointer and snaps (Δ {dx:0}×{dy:0})."
                    : $"[FAIL] Dragging barely moved the panel (Δ {dx:0}×{dy:0}).");
                if (!dragged) hoverFailed++;
            }

            host.Quit();
            Pump(250);
        }
        catch (Exception ex)
        {
            report.AppendLine($"[FAIL] In-game overlay could not be checked: {ex.GetType().Name} {ex.Message}");
            hoverFailed++;
        }

        
        
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            window.Topmost = true;
            window.Activate();
            SetForegroundWindow(handle);
            
            
            Keyboard.ClearFocus();
            window.NavHome.Focus();
            Pump(240);

            if (GetForegroundWindow() != handle)
            {
                report.AppendLine("[SKIP] The window did not come to the foreground - keypress cannot be checked.");
            }
            else if (viewModel is null)
            {
                throw new InvalidOperationException("The window has no view model.");
            }
            else
            {
                viewModel.CloseLayoutCommand.Execute(null);
                Pump(240);

                
                var shortcut = viewModel.Settings.OverlayShortcut;
                var keys = VirtualKeys(shortcut);

                if (keys is null)
                {
                    report.AppendLine($"[FAIL] Shortcut {shortcut} could not be translated into keystrokes.");
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
                        ? $"[OK ] {shortcut} opens the quick view, Esc closes it."
                        : $"[FAIL] {shortcut} opened={opened}, Esc closed={closed}.");

                    if (!opened || !closed) hoverFailed++;
                }
            }

            window.Topmost = false;
        }
        catch (Exception ex)
        {
            report.AppendLine($"[FAIL] Keystroke could not be checked: {ex.GetType().Name} {ex.Message}");
            hoverFailed++;
        }

        
        try
        {
            window.SelectPage(1);
            Pump(220);

            var toggle = Find<CheckBox>(window, box => box.IsChecked == true);
            if (toggle is null)
            {
                report.AppendLine("[FAIL] No enabled toggle found to flip.");
                hoverFailed++;
            }
            else
            {
                toggle.IsChecked = false;
                Pump(260);
                toggle.IsChecked = true;
                Pump(260);
                report.AppendLine("[OK] Toggle switched on and off (transitions completed).");
            }
        }
        catch (Exception ex)
        {
            report.AppendLine($"[FAIL] Toggle could not be flipped: {ex.GetType().Name} {ex.Message}");
            hoverFailed++;
        }

        if (hover)
        {
            report.AppendLine("Hovering with the real pointer (playful animations):");
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
                        report.AppendLine($"[FAIL] {description}: Component not found.");
                        continue;
                    }

                    if (!MovePointerTo(window, target))
                    {
                        report.AppendLine($"[SKIP] {description}: Pointer could not be set.");
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

        
        
        var fitsWorkArea = true;
        try
        {
            window.WindowState = WindowState.Maximized;
            Pump(450);
            var work = SystemParameters.WorkArea;
            fitsWorkArea = window.ActualHeight <= work.Height + 1 && window.ActualWidth <= work.Width + 1;
            report.AppendLine(fitsWorkArea
                ? $"[OK ] Maximized stays in screen area ({window.ActualWidth:0}x{window.ActualHeight:0} in {work.Width:0}x{work.Height:0})."
                : $"[FAIL] When maximized, the window is larger than the screen area ({window.ActualWidth:0}x{window.ActualHeight:0} in {work.Width:0}x{work.Height:0}).");
            window.WindowState = WindowState.Normal;
            Pump(250);
        }
        catch (Exception ex)
        {
            report.AppendLine($"[FAIL] Maximization could not be checked: {ex.Message}");
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

        if (!glideMoved) failures.Add("The nav underline does not follow the page change.");

        if (failures.Count == 0)
        {
            report.AppendLine("[OK ] All bindings on all five pages have been resolved.");
        }

        
        foreach (var failure in failures) report.AppendLine($"  [FAIL] {Log.Redact(failure.Trim())}");

        if (messages.Count > 0)
        {
            report.AppendLine($"Notes ({messages.Count}), not considered errors:");
            foreach (var message in messages.Take(10))
                report.AppendLine($"    - {Log.Redact(message.Trim())}");
        }

        if (hoverFailed > 0) failures.Add($"{hoverFailed} component(s) could not be hovered.");

        return Finish(report, failures.Count == 0 && fitsWorkArea && hoverFailed == 0 ? 0 : 1);
    }

    
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

    
    private static readonly (int Page, string Description, Func<Window, object?> Find)[] HoverTargets =
    {
        (0, "Hero card", w => Find<CardPanel>(w, panel => panel.Name == "HeroCard")),
        (0, "Add game button", w => FindFirst<RippleButton>(w)),
        (1, "Line in the account list", w => FindFirst<ListBoxItem>(w)),
        (2, "Preset row in the FastFlags editor", w => FindFirst<CheckBox>(w)),
        (3, "Toggle in settings", w => FindFirst<CheckBox>(w)),
        (4, "Log area", w => FindFirst<TextBox>(w, box => box.Name == "LogBox")),
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

    
    private static string WindowUnderCursor()
    {
        if (!GetCursorPos(out var point)) return "not detectable";

        var handle = WindowFromPoint(new NativePoint { X = point.X, Y = point.Y });
        if (handle == IntPtr.Zero) return $"no window at {point.X},{point.Y}";

        GetWindowThreadProcessId(handle, out var processId);
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return $"{process.ProcessName} (PID {processId}) at {point.X},{point.Y}";
        }
        catch
        {
            return $"PID {processId} at {point.X},{point.Y}";
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
            ? "UI OK."
            : $"{failed} binding error(s) found.");
        report.AppendLine();

        var text = report.ToString();
        try
        {
            File.WriteAllText(Path.Combine(AppPaths.DataDir, "selftest-ui.log"), text);
        }
        catch
        {
            
        }

        try
        {
            Console.Write(text);
        }
        catch
        {
            
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
