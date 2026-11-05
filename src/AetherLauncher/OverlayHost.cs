using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AetherLauncher.Controls;
using AetherLauncher.Models;
using AetherLauncher.Overlay;
using AetherLauncher.Services;

namespace AetherLauncher;


internal enum KeyCaptureKind
{
    
    Hotkey,

    
    SingleKey,
}


internal readonly record struct PanelChoice(string Id, string Title);









internal sealed class OverlayHost
{
    
    private const double ColumnStep = 360;

    private readonly LauncherSettings _settings;
    private readonly OverlayState _state;
    private readonly bool _preview;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private readonly List<PanelWindow> _panels = new();
    private readonly Dictionary<string, double> _nominal = new();

    
    
    
    private readonly Window _owner;

    private readonly HubPanel _hub;
    private readonly FpsPanel _fps;
    private readonly GraphicsPanel _graphics;
    private readonly NotesPanel _notes;
    private readonly ServerPanel _server;
    private readonly AntiAfkPanel _antiAfk;

    private ModifierKeys _shortcutModifiers = ModifierKeys.Shift;
    private Key _shortcutKey = Key.Tab;

    private bool _shortcutWasDown;
    private bool _open;
    private bool _autoShown;
    private bool _hiddenForOther;
    private bool _busy;
    private int _ticks;
    private GameWindow? _game;
    private DateTimeOffset? _missingSince;
    private bool _hiddenForMissingGame;
    private bool _resumeAfterGameReturns;

    private KeyCapture? _capture;
    private DateTimeOffset _nextNudge;
    private DateTimeOffset? _lastNudgeAt;
    private int _nudges;
    private string _lastNudgeText = "";
    private bool _afkInFlight;
    private bool _lastNudgeOk;
    private int _afkFailures;

    private sealed record KeyCapture(
        KeyCaptureKind Kind,
        Action<string?> Applied,
        Action? Cancelled,
        HashSet<Key> DownAtStart,
        DateTimeOffset StartedAt);

    public OverlayHost(bool preview = false, LauncherSettings? settings = null)
    {
        _preview = preview;
        _settings = settings ?? new SettingsStore().Load();
        _state = preview
            ? new OverlayState { LayoutVersion = OverlayState.CurrentLayoutVersion }
            : OverlayPlacement.Load();

        
        
        
        if (_state.LayoutVersion < OverlayState.CurrentLayoutVersion)
        {
            _state.Panels.Clear();
            _state.LayoutVersion = OverlayState.CurrentLayoutVersion;
            if (!_preview) OverlayPlacement.Save(_state);
        }

        ApplyShortcut(_settings.OverlayShortcut, silent: true);

        _owner = CreateOwnerWindow();

        _dispatcher = Dispatcher.CurrentDispatcher;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(90) };
        _timer.Tick += OnTick;

        
        
        _hub = new HubPanel();
        _fps = new FpsPanel();
        _graphics = new GraphicsPanel();
        _notes = new NotesPanel();
        _server = new ServerPanel();
        _antiAfk = new AntiAfkPanel();

        
        
        AddPanel(PanelIds.Menu, "AETHER OVERLAY", "Roblox keeps running - this only draws over it.", _hub, 0, 680);
        AddPanel(PanelIds.Fps, "FRAME RATE", "Uncapped, fixed or your own value.", _fps, 1, 300);
        AddPanel(PanelIds.Graphics, "GRAPHICS", "On Roblox' allowlist - the client still reads these.", _graphics, 1, 360);
        AddPanel(PanelIds.Notes, "NOTES", "Left open over the game, saved as you type.", _notes, 1, 300);
        AddPanel(PanelIds.Server, "SERVER", "Fresh server for the same place - no trip to the desktop.", _server, 2, 300);
        AddPanel(PanelIds.AntiAfk, "ANTI-AFK", "Pops the game up for a tap, then hides it again.", _antiAfk, 2, 380);

        AttachPanels();
    }

    

    public LauncherSettings Settings => _settings;

    public OverlayState State => _state;

    public bool Playful => _settings.PlayfulMode;

    public bool IsVanilla => _settings.VanillaMode;

    public bool Busy => _busy;

    
    public bool PanelsOpen => _open;

    
    public bool Quitting { get; private set; }

    public string ShortcutText => _settings.OverlayShortcut;

    public bool HideOverOtherApps => _state.HideOverOtherApps;

    public bool AntiAfkEnabled => _state.AntiAfkEnabled;

    public int AntiAfkInterval => _state.AntiAfkIntervalSeconds;

    public string AntiAfkKey => _state.AntiAfkKey;

    
    public int PanelOpacity => _state.PanelOpacity;

    
    public string AntiAfkLine
    {
        get
        {
            if (!_state.AntiAfkEnabled) return "Off - nothing is being sent.";

            var remaining = _nextNudge - DateTimeOffset.UtcNow;
            var next = remaining <= TimeSpan.Zero
                ? "any moment now"
                : remaining.TotalMinutes >= 1
                    ? $"{(int)remaining.TotalMinutes} min"
                    : $"{Math.Max(1, (int)remaining.TotalSeconds)} s";

            if (_lastNudgeAt is null)
                return $"On - pops the game up for a tap. First nudge in {next} ({AntiAfk.DescribeInterval(_state.AntiAfkIntervalSeconds)} apart).";

            var summary = $"{_nudges} nudge(s), last at {_lastNudgeAt:HH:mm}: {_lastNudgeText}";
            return _lastNudgeOk
                ? $"On - {summary} Next in {next}."
                : $"On - retrying in {next}. {summary}";
        }
    }

    
    public IReadOnlyList<PanelChoice> PanelChoices { get; } = new[]
    {
        new PanelChoice(PanelIds.Fps, "Frame rate"),
        new PanelChoice(PanelIds.Graphics, "Graphics"),
        new PanelChoice(PanelIds.Notes, "Notes"),
        new PanelChoice(PanelIds.Server, "Server"),
        new PanelChoice(PanelIds.AntiAfk, "Anti-AFK"),
    };

    public event Action? ShortcutChanged;

    public event Action? SettingsChanged;

    public event Action? AntiAfkUpdated;

    
    public bool IsPanelVisible(string id)
    {
        if (string.Equals(id, PanelIds.Menu, StringComparison.OrdinalIgnoreCase)) return true;

        if (_state.Panels.TryGetValue(id, out var spot)) return spot.Visible;

        
        return !string.Equals(id, PanelIds.AntiAfk, StringComparison.OrdinalIgnoreCase);
    }

    public IReadOnlyList<PanelWindow> Windows => _panels;

    internal PanelWindow? WindowFor(string id) =>
        _panels.FirstOrDefault(panel => string.Equals(panel.PanelId, id, StringComparison.OrdinalIgnoreCase));

    

    
    
    
    
    
    private static Window CreateOwnerWindow()
    {
        var owner = new Window
        {
            Title = "Aether Overlay Host",
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Width = 1,
            Height = 1,
            Left = SystemParameters.VirtualScreenLeft - 4096,
            Top = SystemParameters.VirtualScreenTop - 4096,
            WindowStartupLocation = WindowStartupLocation.Manual,
            ShowInTaskbar = false,
            ShowActivated = false,
            ResizeMode = ResizeMode.NoResize,
            Focusable = false,
            Topmost = false,
        };

        owner.SourceInitialized += (_, _) => TaskSwitcher.Hide(owner);
        owner.Show();
        TaskSwitcher.Hide(owner);
        owner.Hide();

        return owner;
    }

    private void AddPanel(string id, string title, string hint, FrameworkElement body, int column, double nominalHeight)
    {
        var window = new PanelWindow(this, id, title, hint, body) { Tag = column, Owner = _owner };
        window.Opacity = _state.PanelOpacity / 100.0;
        _panels.Add(window);
        _nominal[id] = nominalHeight;
    }

    private void AttachPanels()
    {
        _hub.Attach(this);
        _fps.Attach(this);
        _graphics.Attach(this);
        _notes.Attach(this);
        _server.Attach(this);
        _antiAfk.Attach(this);
    }

    
    public void Start()
    {
        _timer.Start();

        if (!_preview) return;

        ShowAll(animate: false);
        foreach (var panel in _panels) panel.UpdateLayout();
    }

    

    private void OnTick(object? sender, EventArgs e)
    {
        _ticks++;

        
        if (_ticks % 55 == 0) ReloadSettings();

        if (_game is null || _ticks % 6 == 0) _game = GameOverlay.FindWindow();

        if (_game is not { } game)
        {
            
            if (_preview) return;

            
            
            if (!_hiddenForMissingGame)
            {
                _hiddenForMissingGame = true;
                _resumeAfterGameReturns = _open;
                HideAll(animate: false);
                Log.Warn("The Roblox window disappeared - the overlay hid itself.");
            }

            
            
            
            _missingSince ??= DateTimeOffset.UtcNow;
            var clientRunning = GameOverlay.IsClientRunning();
            var grace = _busy ? 60 : clientRunning ? 20 : 3;
            if ((DateTimeOffset.UtcNow - _missingSince.Value).TotalSeconds > grace) Quit();
            return;
        }

        _missingSince = null;

        if (_hiddenForMissingGame)
        {
            _hiddenForMissingGame = false;
            if (_resumeAfterGameReturns)
            {
                _resumeAfterGameReturns = false;
                _autoShown = true;
                ShowAll(animate: false);
            }
        }

        
        
        if (_capture is not null)
        {
            TickCapture();
            return;
        }

        TickAutoShow();
        TickNoOverlap();
        TickHotkey();
        TickAntiAfk(game);
    }

    
    
    
    
    private void TickAutoShow()
    {
        
        
        if (_afkInFlight) return;
        if (!_settings.GameOverlayAutoShow || _autoShown || !GameOverlay.IsForegroundClient()) return;

        _autoShown = true;
        ShowAll();
    }

    
    
    
    
    private void TickNoOverlap()
    {
        if (_afkInFlight || _preview || !_state.HideOverOtherApps) return;

        var ownActive = _panels.Any(panel => panel.IsActive);
        var clientInFront = GameOverlay.IsForegroundClient();

        if (_open && !clientInFront && !ownActive)
        {
            _hiddenForOther = true;
            HideAll();
            return;
        }

        if (_hiddenForOther && clientInFront)
        {
            _hiddenForOther = false;
            ShowAll();
        }
    }

    private void TickHotkey()
    {
        var down = GameOverlay.IsPressed(_shortcutKey) && GameOverlay.AreModifiersDown(_shortcutModifiers);
        var toggled = down && !_shortcutWasDown;
        _shortcutWasDown = down;

        if (!toggled) return;

        
        
        if (!_panels.Any(panel => panel.IsActive) && !GameOverlay.IsForegroundClient()) return;

        if (_open)
        {
            HideAll();
            return;
        }

        _hiddenForOther = false;
        ShowAll();
    }

    private void TickAntiAfk(GameWindow game)
    {
        if (!_state.AntiAfkEnabled || _busy || _afkInFlight) return;
        if (DateTimeOffset.UtcNow < _nextNudge) return;

        _afkInFlight = true;

        var key = _state.AntiAfkKey;

        _ = Task.Run(() =>
        {
            AntiAfk.NudgeOutcome outcome;
            string message;

            try
            {
                outcome = AntiAfk.NudgeEvery(game, key, out message);
            }
            catch (Exception ex)
            {
                outcome = AntiAfk.NudgeOutcome.Failed;
                message = $"The nudge failed: {ex.Message}";
            }

            _ = _dispatcher.BeginInvoke(() =>
            {
                _afkInFlight = false;
                _lastNudgeText = message;
                _lastNudgeOk = outcome == AntiAfk.NudgeOutcome.Sent;

                switch (outcome)
                {
                    case AntiAfk.NudgeOutcome.Sent:
                        _nudges++;
                        _afkFailures = 0;
                        _lastNudgeAt = DateTimeOffset.Now;
                        _nextNudge = DateTimeOffset.UtcNow + Interval();
                        Log.Info($"Anti-AFK: {message}");
                        break;

                    default:
                        
                        _lastNudgeAt = DateTimeOffset.Now;
                        _afkFailures++;
                        _nextNudge = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(
                            Math.Min(120, AntiAfk.RetryDelay.TotalSeconds + _afkFailures * 20));
                        Log.Warn($"Anti-AFK skipped: {message}");
                        break;
                }

                AntiAfkUpdated?.Invoke();
            });
        });
    }

    
    
    
    
    private TimeSpan Interval()
    {
        var seconds = AntiAfk.ValidInterval(_state.AntiAfkIntervalSeconds)
            ? _state.AntiAfkIntervalSeconds
            : AntiAfk.DefaultIntervalSeconds;

        var jitter = 1 + (Random.Shared.NextDouble() * 0.18 - 0.09);
        return TimeSpan.FromSeconds(Math.Max(30, seconds * jitter));
    }

    
    
    
    
    private void ReloadSettings()
    {
        LauncherSettings fresh;
        try
        {
            fresh = new SettingsStore().Load();
        }
        catch
        {
            return; 
        }

        if (!fresh.GameOverlayEnabled)
        {
            Quit();
            return;
        }

        var changed = !string.Equals(_settings.OverlayShortcut, fresh.OverlayShortcut, StringComparison.Ordinal)
                      || _settings.VanillaMode != fresh.VanillaMode
                      || _settings.LastPlaceId != fresh.LastPlaceId
                      || _settings.PlayfulMode != fresh.PlayfulMode
                      || _settings.ApplyFastFlags != fresh.ApplyFastFlags
                      || !string.Equals(_settings.Theme, fresh.Theme, StringComparison.OrdinalIgnoreCase);

        if (!string.Equals(_settings.Theme, fresh.Theme, StringComparison.OrdinalIgnoreCase))
            ThemeManager.Apply(fresh.Theme);

        _settings.GameOverlayAutoShow = fresh.GameOverlayAutoShow;
        _settings.LastPlaceId = fresh.LastPlaceId;
        _settings.LastPlaceAccessCode = fresh.LastPlaceAccessCode;
        _settings.LastAccountId = fresh.LastAccountId;
        _settings.PlayfulMode = fresh.PlayfulMode;
        _settings.VanillaMode = fresh.VanillaMode;
        _settings.ApplyFastFlags = fresh.ApplyFastFlags;
        _settings.Theme = fresh.Theme;

        if (!string.Equals(_settings.OverlayShortcut, fresh.OverlayShortcut, StringComparison.Ordinal))
        {
            ApplyShortcut(fresh.OverlayShortcut, silent: true);
            ShortcutChanged?.Invoke();
        }

        if (changed) SettingsChanged?.Invoke();
    }

    

    
    public void ShowAll(bool animate = true)
    {
        _open = true;
        _game = GameOverlay.FindWindow() ?? _game;

        var area = GameOverlay.WorkArea(_game?.Handle ?? IntPtr.Zero);
        var cursors = new Dictionary<int, double>();
        var index = 0;

        foreach (var panel in _panels)
        {
            if (!IsPanelVisible(panel.PanelId))
            {
                panel.HideSoftly(animate: false);
                continue;
            }

            var spot = _state.Panels.TryGetValue(panel.PanelId, out var saved)
                ? saved
                : DefaultSpot(panel, area, cursors);

            panel.ShowAt(spot.Left, spot.Top, animate, animate ? index++ * 45 : 0);
        }
    }

    
    public void HideAll(bool animate = true)
    {
        _open = false;
        _notes.Flush();

        var hadFocus = _panels.Any(panel => panel.IsActive);
        foreach (var panel in _panels) panel.HideSoftly(animate);

        if (hadFocus)
        {
            var game = GameOverlay.FindWindow();
            if (game is not null) GameOverlay.Focus(game.Value);
        }
    }

    
    public void HidePanel(string id, bool remember)
    {
        if (remember)
        {
            Spot(id).Visible = false;
            SaveState();
            SettingsChanged?.Invoke();
        }

        WindowFor(id)?.HideSoftly(animate: true);
    }

    
    public void SetPanelVisible(string id, bool visible)
    {
        if (string.Equals(id, PanelIds.Menu, StringComparison.OrdinalIgnoreCase)) return;

        var panel = WindowFor(id);
        if (panel is null) return;

        if (!visible)
        {
            Spot(id).Visible = false;
            SaveState();
            panel.HideSoftly(animate: true);
            return;
        }

        _open = true;

        
        
        var placed = _state.Panels.TryGetValue(id, out var saved) && (saved.Left != 0 || saved.Top != 0);
        var spot = placed
            ? saved!
            : DefaultSpot(panel, GameOverlay.WorkArea(_game?.Handle ?? IntPtr.Zero), ColumnCursors(panel));

        spot.Visible = true;
        _state.Panels[id] = spot;
        SaveState();

        panel.ShowAt(spot.Left, spot.Top, animate: true, delay: 0);
        Anim.Pulse(panel, 1.02, 240);
    }

    
    public void ResetSpot(string id)
    {
        _state.Panels.Remove(id);
        SaveState();

        var panel = WindowFor(id);
        if (panel is null) return;

        var area = GameOverlay.WorkArea(_game?.Handle ?? IntPtr.Zero);
        var spot = DefaultSpot(panel, area, new Dictionary<int, double>());

        panel.ShowAt(spot.Left, spot.Top, animate: true, delay: 0);
        Anim.Pulse(panel, 1.02, 260);
    }

    
    public void SaveSpot(PanelWindow panel)
    {
        var spot = Spot(panel.PanelId);
        spot.Left = panel.Left;
        spot.Top = panel.Top;
        SaveState();
    }

    public void SetHideOverOtherApps(bool value)
    {
        _state.HideOverOtherApps = value;
        SaveState();
    }

    
    public void SetPanelOpacity(int percent)
    {
        _state.PanelOpacity = Math.Clamp(percent, 55, 100);
        var target = _state.PanelOpacity / 100.0;

        foreach (var panel in _panels)
        {
            if (panel.IsVisible) Anim.Fade(panel, panel.Opacity, target, 170);
            else panel.Opacity = target;
        }

        SaveState();
    }

    
    public void ResetAllSpots()
    {
        var visibility = _panels.ToDictionary(panel => panel.PanelId, panel => IsPanelVisible(panel.PanelId));
        _state.Panels.Clear();

        var area = GameOverlay.WorkArea(_game?.Handle ?? IntPtr.Zero);
        var cursors = new Dictionary<int, double>();

        foreach (var panel in _panels)
        {
            var spot = DefaultSpot(panel, area, cursors);
            if (visibility.TryGetValue(panel.PanelId, out var visible)) spot.Visible = visible;
            if (panel.IsVisible) panel.ShowAt(spot.Left, spot.Top, animate: true, delay: 0);
        }

        SaveState();
        SettingsChanged?.Invoke();
    }

    private OverlaySpot Spot(string id)
    {
        if (_state.Panels.TryGetValue(id, out var spot)) return spot;

        spot = new OverlaySpot { Visible = IsPanelVisible(id) };
        _state.Panels[id] = spot;
        return spot;
    }

    private void SaveState()
    {
        if (_preview) return;
        if (!OverlayPlacement.Save(_state)) Log.Warn("The overlay layout could not be saved.");
    }

    
    private Dictionary<int, double> ColumnCursors(PanelWindow? except = null)
    {
        var cursors = new Dictionary<int, double>();

        foreach (var panel in _panels)
        {
            if (ReferenceEquals(panel, except) || !panel.IsVisible || panel.Tag is not int column) continue;

            var height = panel.ActualHeight > 1 ? panel.ActualHeight : 300;
            var next = panel.Top + height + 12;
            cursors[column] = cursors.TryGetValue(column, out var known) ? Math.Max(known, next) : next;
        }

        return cursors;
    }

    private OverlaySpot DefaultSpot(PanelWindow panel, (int Left, int Top, int Right, int Bottom) area, Dictionary<int, double> cursors)
    {
        var column = panel.Tag is int index ? index : 0;
        var left = area.Left + 40 + (column * ColumnStep);
        var top = cursors.TryGetValue(column, out var y) ? y : area.Top + 40;

        cursors[column] = top + (_nominal.TryGetValue(panel.PanelId, out var height) ? height : 260) + 12;

        return new OverlaySpot { Left = left, Top = top, Visible = true };
    }

    

    
    
    
    
    
    public void BeginCapture(KeyCaptureKind kind, Action<string?> applied, Action? cancelled = null)
    {
        var down = Shortcuts.CaptureCandidates(kind == KeyCaptureKind.SingleKey)
            .Where(GameOverlay.IsPressed)
            .ToHashSet();

        _capture = new KeyCapture(kind, applied, cancelled, down, DateTimeOffset.UtcNow);
        _shortcutWasDown = true; 
    }

    public void CancelCapture()
    {
        if (_capture is not { } capture) return;

        _capture = null;
        capture.Cancelled?.Invoke();
    }

    private void TickCapture()
    {
        if (_capture is not { } capture) return;

        
        if (GameOverlay.IsPressed(Key.Escape) && !capture.DownAtStart.Contains(Key.Escape))
        {
            CancelCapture();
            return;
        }

        if ((DateTimeOffset.UtcNow - capture.StartedAt).TotalSeconds > 25)
        {
            CancelCapture();
            return;
        }

        foreach (var key in Shortcuts.CaptureCandidates(capture.Kind == KeyCaptureKind.SingleKey))
        {
            if (capture.DownAtStart.Contains(key)) continue;
            if (!GameOverlay.IsPressed(key)) continue;

            _capture = null;

            capture.Applied(capture.Kind == KeyCaptureKind.SingleKey
                ? Shortcuts.Name(key)
                : Shortcuts.Format(GameOverlay.CurrentModifiers(), key));

            return;
        }
    }

    
    public string SetShortcut(string text)
    {
        if (!Shortcuts.TryParse(text, out _, out _))
            return "That will not do for the menu - use one key like Delete or Insert, or add Ctrl, Alt or Shift.";

        ApplyShortcut(text, silent: false);
        ShortcutChanged?.Invoke();
        return $"Menu key: {text}.";
    }

    public string ResetShortcut()
    {
        ApplyShortcut(Shortcuts.Default, silent: false);
        ShortcutChanged?.Invoke();
        return $"Menu key back to {Shortcuts.Default}.";
    }

    private void ApplyShortcut(string text, bool silent)
    {
        if (!Shortcuts.TryParse(text, out var modifiers, out var key))
        {
            modifiers = ModifierKeys.Shift;
            key = Key.Tab;
        }

        _shortcutModifiers = modifiers;
        _shortcutKey = key;
        _shortcutWasDown = true;
        _settings.OverlayShortcut = text;

        if (silent || _preview) return;

        try
        {
            
            
            var store = new SettingsStore();
            var fresh = store.Load();
            fresh.OverlayShortcut = text;
            store.Save(fresh);
            Log.Info($"The in-game overlay shortcut is now {text}.");
        }
        catch (Exception ex)
        {
            Log.Warn($"The shortcut could not be saved: {ex.Message}");
        }
    }

    

    
    public (int? FrameRate, int? Quality, int? Texture, int? Msaa, bool? GrassOff) ReadFlags()
    {
        var grass = FastFlags.Read("FIntFRMMaxGrassDistance");
        return (
            FastFlags.ReadFrameRate(),
            FastFlags.ReadQualityLevel(),
            FastFlags.ReadTextureQuality(),
            FastFlags.ReadMsaa(),
            grass is null ? null : grass == "0");
    }

    public string ApplyFrameRate(int frameRate)
    {
        var text = frameRate >= FastFlags.UncappedFrameRate
            ? "Frame rate cap lifted."
            : $"Frame rate limited to {frameRate}.";

        var problem = FastFlags.TrySet(FastFlags.FrameRateFlag, frameRate.ToString());
        if (problem is not null) return problem;

        return StoreInClient(text);
    }

    public string ApplyQuality(int level) => ApplyFlags(
        level <= 1 ? "Graphics pinned to the lowest level." : "Graphics pinned to the highest level.",
        (FastFlags.QualityFlag, level.ToString()));

    public string ApplyTexture(int level)
    {
        var label = FastFlags.TextureLevels.FirstOrDefault(t => t.Value == level).Label;
        return ApplyFlags(
            label is null ? "Texture quality set." : $"Textures set to {label}.",
            (FastFlags.TextureToggleFlag, "True"),
            (FastFlags.TextureQualityFlag, level.ToString()));
    }

    public string ApplyMsaa(int samples) => ApplyFlags(
        $"Anti-aliasing set to {samples}× sample(s).",
        (FastFlags.MsaaFlag, samples.ToString()));

    public string ApplyGrass(bool off) => ApplyFlags(
        off ? "Grass switched off." : "Grass back on.",
        ("FIntFRMMaxGrassDistance", off ? "0" : null),
        ("FIntFRMMinGrassDistance", off ? "0" : null));

    private string ApplyFlags(string text, params (string Key, string? Value)[] changes)
    {
        var problem = FastFlags.TrySetMany(changes.ToDictionary(change => change.Key, change => change.Value));
        return problem ?? StoreInClient(text);
    }

    
    
    
    
    public string StoreInClient(string text)
    {
        if (_settings.VanillaMode)
            return $"{text} Vanilla mode is on, so the client was left untouched.";

        var message = FastFlags.ApplyToInstalledClient(_settings);
        return $"{text} {message} Roblox reads flags when it starts - a rejoin brings it in.";
    }

    

    
    public async Task<string> HopAsync(Action<string> stage)
    {
        if (_busy) return "Already busy.";

        _busy = true;
        try
        {
            LauncherSettings settings;
            try
            {
                settings = new SettingsStore().Load();
            }
            catch
            {
                settings = _settings;
            }

            var accounts = new AccountStore();
            accounts.Load();

            
            
            
            var safeStage = new Action<string>(text =>
            {
                if (_dispatcher.CheckAccess()) stage(text);
                else _dispatcher.BeginInvoke(() => stage(text));
            });

            var result = await ServerHop.HopAsync(settings, accounts, safeStage).ConfigureAwait(true);
            if (result.Success) _autoShown = true;
            return result.Message;
        }
        catch (Exception ex)
        {
            Log.Error($"Server hop failed: {ex.Message}");
            return $"Server hop failed: {ex.Message}";
        }
        finally
        {
            _busy = false;
        }
    }

    
    
    
    
    public async Task<string> RejoinAsync(Action<string> stage)
    {
        if (_busy) return "Already busy.";

        _busy = true;
        try
        {
            LauncherSettings settings;
            try
            {
                settings = new SettingsStore().Load();
            }
            catch
            {
                settings = _settings;
            }

            var accounts = new AccountStore();
            accounts.Load();

            var safeStage = new Action<string>(text =>
            {
                if (_dispatcher.CheckAccess()) stage(text);
                else _dispatcher.BeginInvoke(() => stage(text));
            });

            var result = await ServerHop.RejoinAsync(settings, accounts, safeStage).ConfigureAwait(true);
            if (result.Success) _autoShown = true;
            return result.Message;
        }
        catch (Exception ex)
        {
            Log.Error($"Rejoin failed: {ex.Message}");
            return $"Rejoin failed: {ex.Message}";
        }
        finally
        {
            _busy = false;
        }
    }

    
    
    
    
    public async Task<string> RestartAsync(Action<string> stage)
    {
        if (_busy) return "Already busy.";

        _busy = true;
        try
        {
            LauncherSettings settings;
            try
            {
                settings = new SettingsStore().Load();
            }
            catch
            {
                settings = _settings;
            }

            var accounts = new AccountStore();
            accounts.Load();
            var account = accounts.Accounts.FirstOrDefault(a => a.Id == settings.LastAccountId);
            var place = new PlaceRecord
            {
                PlaceId = settings.LastPlaceId ?? _settings.LastPlaceId ?? 0,
                AccessCode = settings.LastPlaceAccessCode,
            };

            if (place.PlaceId <= 0) return "No game started through Aether yet - start one in the launcher first.";

            stage("Closing Roblox…");
            await Task.Run(() => GameOverlay.CloseClients()).ConfigureAwait(true);

            var launcher = new LaunchService(settings, accounts);
            var result = await launcher.LaunchPlaceAsync(place, account).ConfigureAwait(true);

            if (result.Success) _autoShown = true;
            return result.Success ? "Client restarted - new flags are in effect." : result.Message;
        }
        catch (Exception ex)
        {
            Log.Error($"Restart failed: {ex.Message}");
            return $"Restart failed: {ex.Message}";
        }
        finally
        {
            _busy = false;
        }
    }

    public void OpenLauncher()
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppPaths.ExecutablePath, "--show") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn($"The launcher could not be brought forward: {ex.Message}");
        }
    }

    
    public string CopyGameLink()
    {
        if ((RobloxLogImport.LastJoin()?.PlaceId ?? Settings.LastPlaceId) is not { } place)
            return "No game started through Aether yet.";

        var link = $"https://www.roblox.com/games/{place}";
        try
        {
            Clipboard.SetText(link);
            Log.Info($"Game link copied: {link}");
            return "Game link copied to the clipboard.";
        }
        catch (Exception ex)
        {
            Log.Warn($"The game link could not be copied: {ex.Message}");
            return "The link could not be copied.";
        }
    }

    public void OpenGamePage()
    {
        var placeId = RobloxLogImport.LastJoin()?.PlaceId ?? _settings.LastPlaceId;
        if (placeId is not { } id) return;

        try
        {
            Process.Start(new ProcessStartInfo($"https://www.roblox.com/games/{id}") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn($"The game page could not be opened: {ex.Message}");
        }
    }

    

    public void SetAntiAfkEnabled(bool enabled)
    {
        _state.AntiAfkEnabled = enabled;
        if (enabled)
        {
            _nextNudge = DateTimeOffset.UtcNow + Interval();
            _lastNudgeOk = false;
            _afkFailures = 0;
        }
        else
        {
            _nudges = 0;
            _lastNudgeAt = null;
            _lastNudgeText = "";
            _lastNudgeOk = false;
        }

        SaveState();
        AntiAfkUpdated?.Invoke();
    }

    public void SetAntiAfkInterval(int seconds)
    {
        if (!AntiAfk.ValidInterval(seconds)) return;

        _state.AntiAfkIntervalSeconds = seconds;
        _nextNudge = DateTimeOffset.UtcNow + Interval();
        SaveState();
        AntiAfkUpdated?.Invoke();
    }

    public void SetAntiAfkKey(string key)
    {
        if (!Shortcuts.TryParseKey(key, out _)) return;

        _state.AntiAfkKey = key;
        SaveState();
        AntiAfkUpdated?.Invoke();
    }

    
    
    
    
    public string NudgeNow()
    {
        var game = GameOverlay.FindWindow();
        if (game is null && !GameOverlay.IsForegroundClient())
            return "No Roblox window found - is the client running?";

        var outcome = AntiAfk.NudgeEvery(game, _state.AntiAfkKey, out var message);
        var sent = outcome == AntiAfk.NudgeOutcome.Sent;

        
        
        void Apply()
        {
            _lastNudgeText = message;
            _lastNudgeOk = sent;
            _lastNudgeAt = DateTimeOffset.Now;

            if (sent)
            {
                _nudges++;
                _nextNudge = DateTimeOffset.UtcNow + Interval();
            }
        }

        if (_dispatcher.CheckAccess()) Apply();
        else _dispatcher.Invoke(Apply);

        if (outcome == AntiAfk.NudgeOutcome.Sent) Log.Info($"Anti-AFK (manual): {message}");
        else Log.Warn($"Anti-AFK (manual) skipped: {message}");

        
        return message;
    }

    
    public string AntiAfkSummary()
    {
        var key = Shortcuts.TryParseKey(_state.AntiAfkKey, out var parsed) ? Shortcuts.Name(parsed) : _state.AntiAfkKey;
        return $"Pop-up tap ({key}) every {AntiAfk.DescribeInterval(_state.AntiAfkIntervalSeconds)}";
    }

    

    public void Quit()
    {
        Quitting = true;
        _timer.Stop();

        foreach (var panel in _panels.ToList())
        {
            try
            {
                panel.Close();
            }
            catch
            {
                
            }
        }

        _panels.Clear();

        try
        {
            _owner.Close();
        }
        catch
        {
            
        }

        if (!_preview) Application.Current?.Shutdown(0);
    }
}


internal static class PanelIds
{
    public const string Menu = "menu";
    public const string Fps = "fps";
    public const string Graphics = "graphics";
    public const string Notes = "notes";
    public const string Server = "server";
    public const string AntiAfk = "antiafk";
}


internal static class PanelUi
{
    public static void Say(System.Windows.Controls.TextBlock line, string text, bool error = false)
    {
        line.Text = text;
        line.Foreground = (Brush)Application.Current.FindResource(error ? "DangerBrush" : "TextFaintBrush");
        Anim.Fade(line, 0.35, 1, 220);
    }
}
