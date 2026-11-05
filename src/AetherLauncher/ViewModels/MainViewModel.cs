using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AetherLauncher.Infrastructure;
using AetherLauncher.Models;
using AetherLauncher.Services;

namespace AetherLauncher.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly SettingsStore _settingsStore = new();
    private readonly AccountStore _accountStore = new();
    private readonly PlaceStore _placeStore = new();
    private readonly RecentStore _recentStore = new();
    private readonly LaunchService _launcher;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _launchGate = new(1, 1);

    private AccountItem? _selectedAccount;
    private PlaceItem? _selectedPlace;
    private RobloxVersion? _installedVersion;
    private RobloxDeployment? _deployed;
    private string _status = "";
    private string _footerNote = "Games, launch keys and settings stay on this PC - nothing is uploaded.";
    private string _installSummary = "Looking for the Roblox client…";
    private string _fastFlagsJson = "";
    private string _newCookie = "";
    private string _addAccountError = "";
    private string _newPlaceInput = "";
    private bool _addAccountOpen;
    private bool _addGameOpen;
    private bool _busy;
    private VersionItem? _selectedVersion;
    private FontItem? _selectedFont;
    private ThemeItem? _selectedTheme;
    private string _cursorSummary = "";
    private string _presetSearch = "";
    private string _presetCountLine = "";
    private string _fastFlagFilter = "all";
    private string _visualFlagsCountLine = "";
    private bool _flagsVisualMode = true;
    private string _flagsCountLabel = "0 flags";
    private string _flagsStatus = "";
    private bool _flagsHasError;
    private bool _syncingPresets;
    private bool _loadingRecent;
    private bool _recentReloadQueued;
    private string _buildSearch = "";
    private string _buildsSummary = "No version list loaded yet.";
    private double _downloadPercent;
    private bool _downloading;
    private string _installerLine = "Nothing downloaded yet.";
    private string? _installerPath;
    private BuildItem? _selectedBuild;
    private readonly List<BuildItem> _allBuilds = new();

    // Schnellansicht ("Layout") und ihre Notizen
    private bool _layoutOpen;
    private bool _shortcutCapture;
    private string _notesText = "";
    private string _notesLine = "Saved automatically while you type.";
    private readonly DispatcherTimer _notesTimer;

    public MainViewModel()
    {
        Log.AttachUi(Application.Current.Dispatcher);

        // Ohne dieses Abo bliebe die Log-Seite auf dem Stand vom Öffnen stehen.
        Log.Lines.CollectionChanged += (_, _) => Raise(nameof(LogText));

        Settings = _settingsStore.Load();
        _launcher = new LaunchService(Settings, _accountStore);

        _accountStore.Load();
        _placeStore.Load();
        _recentStore.Load();

        Accounts = new ObservableCollection<AccountItem>(_accountStore.Accounts.Select(CreateAccountItem));
        Places = new ObservableCollection<PlaceItem>(_placeStore.Places.Select(p => new PlaceItem(p)));
        Recents = new ObservableCollection<RecentItem>(_recentStore.Entries.Select(e => new RecentItem(e)));
        SortPlaces();

        // Der Multi-Start-Zähler soll immer zu dem passen, was in der Liste steht.
        Accounts.CollectionChanged += (_, _) =>
        {
            RaiseMultiStart();
            RaiseCommandStates();
            Raise(nameof(LayoutAccountLine));
        };
        Presets = new ObservableCollection<PresetItem>(FastFlags.Presets.Select(p => new PresetItem(p)));
        PresetGroups = new ObservableCollection<PresetGroupItem>(
            Presets.GroupBy(p => p.Group).Select(g => new PresetGroupItem(g.Key, g.ToList())));

        // Favoriten kommen aus einer eigenen kleinen Datei; erst danach abonnieren, damit
        // das Laden nicht sofort wieder gespeichert wird.
        var favoriteNames = FastFlags.LoadFavorites();
        foreach (var preset in Presets)
        {
            preset.IsFavorite = favoriteNames.Contains(preset.Name);
            preset.PropertyChanged += OnPresetChanged;
        }

        Builds = new ObservableCollection<BuildItem>();
        RecallDownloadedInstaller();

        // Notizen werden kurz nach dem letzten Tastendruck geschrieben - nicht bei jedem Zeichen.
        _notesTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(700) };
        _notesTimer.Tick += (_, _) =>
        {
            _notesTimer.Stop();
            SaveNotes(quiet: true);
        };

        _notesText = Services.Notes.Load();

        InstalledVersions = new ObservableCollection<VersionItem>();
        FontFamilies = new ObservableCollection<FontItem>(
            new[] { new FontItem(null, isDefault: true) }
                .Concat(AppFonts.InstalledFamilies().Select(name => new FontItem(name, isDefault: false))));
        _selectedFont = FontFamilies.FirstOrDefault(f => string.Equals(f.Family, Settings.UiFontFamily,
                           StringComparison.CurrentCultureIgnoreCase)) ?? FontFamilies[0];

        Themes = new ObservableCollection<ThemeItem>(ThemeManager.All.Select(palette => new ThemeItem(palette)));
        _selectedTheme = Themes.FirstOrDefault(theme => string.Equals(theme.Id, Settings.Theme,
                             StringComparison.OrdinalIgnoreCase)) ?? Themes[0];

        FastFlagsJson = FastFlags.LoadJsonText();
        RefreshFlagState();
        ApplyPresetFilter();
        SelectedAccount = Accounts.FirstOrDefault();
        SelectedPlace = Places.FirstOrDefault();

        ToggleAddAccountCommand = new RelayCommand(() =>
        {
            AddAccountOpen = !AddAccountOpen;
            AddAccountError = "";
        }, () => true);

        AddAccountCommand = new AsyncRelayCommand(AddAccountAsync, () => !Busy && NewCookie.Trim().Length > 0);

        RefreshAccountsCommand = new AsyncRelayCommand(RefreshAccountsAsync, () => !Busy && Accounts.Count > 0);

        RemoveAccountCommand = new RelayCommand(parameter =>
        {
            if (parameter is not AccountItem item) return;
            _accountStore.Remove(item.Record);
            Accounts.Remove(item);
            if (ReferenceEquals(SelectedAccount, item)) SelectedAccount = Accounts.FirstOrDefault();
            Status = $"{item.Handle} removed.";
            RaiseMultiStart();
            RaiseCommandStates();
        });

        ToggleAddGameCommand = new RelayCommand(() =>
        {
            AddGameOpen = !AddGameOpen;
            if (AddGameOpen) FooterNote = "Paste a place ID, a roblox.com/games link or a private server link.";
        });

        AddPlaceCommand = new AsyncRelayCommand(AddPlaceAsync, () => !Busy && NewPlaceInput.Trim().Length > 0);

        RemovePlaceCommand = new RelayCommand(parameter =>
        {
            if (parameter is not PlaceItem item) return;
            _placeStore.Remove(item.Record);
            Places.Remove(item);
            if (ReferenceEquals(SelectedPlace, item)) SelectedPlace = Places.FirstOrDefault();
            RaiseCommandStates();
        });

        PinPlaceCommand = new RelayCommand(parameter =>
        {
            if (parameter is not PlaceItem item) return;

            var pinned = !item.Record.IsPinned;
            _placeStore.SetPinned(item.Record, pinned);
            item.RefreshPin();

            if (pinned)
            {
                // Angeheftet heißt: nach oben - und dort bleibt es, bis man es wieder löst.
                var order = Places.ToList();
                order.Remove(item);
                order.Insert(0, item);
                ApplyPlaceOrder(order);
            }
            else
            {
                SortPlaces();
            }

            Status = pinned
                ? $"{item.Name} pinned to the top."
                : $"{item.Name} unpinned.";
        });

        // Aus der Zuletzt-Liste in die eigene Sammlung übernehmen.
        UseRecentCommand = new RelayCommand(parameter =>
        {
            if (parameter is not RecentItem item) return;

            if (Places.FirstOrDefault(p => p.Record.PlaceId == item.Record.PlaceId) is { } existing)
            {
                SelectedPlace = existing;
                Status = $"{existing.Name} is already in your games.";
                return;
            }

            var record = _placeStore.Add(item.Record.PlaceId, item.Record.AccessCode, item.Name);
            record.UniverseId = item.Record.UniverseId;
            _placeStore.Save();

            var place = new PlaceItem(record);
            place.Icon = item.Icon;
            Places.Insert(0, place);
            ApplyPlaceOrder(Places.ToList());
            SelectedPlace = place;
            item.IsSaved = true;
            Raise(nameof(PlacesEmptyVisibility));
            Status = $"{item.Name} added to your games.";

            if (place.Record.UniverseId is null) _ = ResolveGameAsync(place);
        });

        ForgetRecentCommand = new RelayCommand(parameter =>
        {
            if (parameter is not RecentItem item) return;

            _recentStore.Forget(item.Record.PlaceId);
            Recents.Remove(item);
            RaiseRecents();
            Status = $"{item.Name} removed from the recently played list.";
        });

        ClearRecentsCommand = new RelayCommand(() =>
        {
            _recentStore.Clear();
            Recents.Clear();
            RaiseRecents();
            Status = "The recently played list is empty again.";
        });

        // Ohne gewähltes Spiel startet der Knopf die Roblox-App selbst (Startseite).
        LaunchCommand = new AsyncRelayCommand(LaunchSelectedAsync, () => !Busy);
        LaunchAppCommand = new AsyncRelayCommand(LaunchAppForAccountAsync, () => !Busy);

        LaunchPlaceCommand = new AsyncRelayCommand(parameter =>
        {
            if (parameter is not PlaceItem item) return Task.CompletedTask;
            SelectedPlace = item;
            return LaunchSelectedAsync();
        }, _ => !Busy);

        LaunchAccountCommand = new AsyncRelayCommand(parameter =>
        {
            if (parameter is not AccountItem item) return Task.CompletedTask;
            SelectedAccount = item;
            UseCurrentLogin = false;

            // Ohne Spiel in der Liste öffnet dieser Knopf die Roblox-App als dieses Konto.
            return SelectedPlace is null
                ? LaunchAppAsync(item.Record)
                : LaunchCoreAsync(SelectedPlace.Record, item.Record);
        }, _ => !Busy);

        LoginAccountCommand = new RelayCommand(parameter =>
        {
            if (parameter is not AccountItem item) return;
            AddAccountOpen = true;
            AddAccountError = "";
            OpenUrl("https://www.roblox.com/login");
            Status = $"Sign in as {item.Handle} in the browser, then paste the fresh key above.";
        });

        MultiLaunchCommand = new AsyncRelayCommand(MultiLaunchAsync,
            () => !Busy && SelectedPlace is not null && Accounts.Any(a => a.IsMarked));

        SaveFastFlagsCommand = new RelayCommand(SaveFastFlags);
        ApplyFastFlagsCommand = new RelayCommand(ApplyFastFlagsNow);

        RegisterProtocolCommand = new RelayCommand(RegisterProtocol);
        RestoreProtocolCommand = new RelayCommand(RestoreProtocol,
            () => !string.IsNullOrEmpty(Settings.SavedProtocolHandler) || Settings.ProtocolHandlerRegistered);

        OpenConfigFolderCommand = new RelayCommand(() => OpenPath(AppPaths.DataDir));
        OpenLogFileCommand = new RelayCommand(() => OpenPath(AppPaths.LogFile));
        OpenClientFolderCommand = new RelayCommand(OpenClientFolder, () => _installedVersion is not null);
        DownloadStudioCommand = new RelayCommand(() => OpenUrl(StudioUrl));
        ClientActionCommand = new AsyncRelayCommand(ClientActionAsync, () => !Busy);
        ClearLogCommand = new RelayCommand(() => Log.Lines.Clear());
        RefreshInstallCommand = new AsyncRelayCommand(RefreshInstallAsync, () => !Busy);
        RestartCommand = new RelayCommand(Restart);

        ApplyCursorCommand = new RelayCommand(ApplyCursorNow, () => !Busy && _installedVersion is not null);
        RestoreCursorCommand = new RelayCommand(RestoreCursorNow, () => !Busy && _installedVersion is not null);
        ChooseCursorImageCommand = new RelayCommand(ChooseCursorImage);
        UseBundledCursorCommand = new RelayCommand(UseBundledCursor);
        LoadFontFileCommand = new RelayCommand(LoadFontFile);
        OpenFontFolderCommand = new RelayCommand(() => OpenPath(AppPaths.FontDir));

        FormatFlagsCommand = new RelayCommand(FormatFlags);
        CopyFlagsCommand = new RelayCommand(CopyFlags);
        ImportFlagsCommand = new RelayCommand(ImportFlags);
        ExportFlagsCommand = new RelayCommand(ExportFlags);
        ClearFlagsCommand = new RelayCommand(ClearFlags);
        ShowVisualFlagsCommand = new RelayCommand(() => FlagsVisualMode = true);
        ShowJsonFlagsCommand = new RelayCommand(() => FlagsVisualMode = false);
        SelectFlagFilterCommand = new RelayCommand(parameter => SelectFlagFilter(parameter as string ?? "all"));
        ClearPresetSearchCommand = new RelayCommand(() => PresetSearch = "");

        LoadBuildsCommand = new AsyncRelayCommand(LoadBuildsAsync, () => !Busy);
        DownloadBuildCommand = new AsyncRelayCommand(parameter =>
        {
            if (parameter is not BuildItem item) return Task.CompletedTask;
            return DownloadBuildAsync(item);
        }, parameter => !Busy && parameter is BuildItem);
        InstallBuildCommand = new RelayCommand(InstallBuild, () => _installerPath is not null && File.Exists(_installerPath));
        OpenDownloadFolderCommand = new RelayCommand(() => OpenPath(AppPaths.DownloadDir));

        StartGameOverlayCommand = new RelayCommand(() =>
        {
            // "Start now" ist eine klare Absicht - der Schalter geht dabei mit an.
            if (!Settings.GameOverlayEnabled) GameOverlayEnabled = true;
            else GameOverlay.EnsureRunning();

            Status = $"The in-game overlay is starting - press {Settings.OverlayShortcut} over Roblox.";
        });

        OpenLayoutCommand = new RelayCommand(ToggleLayout);
        CloseLayoutCommand = new RelayCommand(CloseLayout);
        QuickLaunchCommand = new AsyncRelayCommand(QuickLaunchAsync, _ => !Busy);
        ChangeShortcutCommand = new RelayCommand(BeginShortcutCapture);
        ResetShortcutCommand = new RelayCommand(ResetShortcut,
            () => !string.Equals(Settings.OverlayShortcut, Shortcuts.Default, StringComparison.Ordinal));
        SaveNotesCommand = new RelayCommand(() => SaveNotes(quiet: false));
        OpenNotesFileCommand = new RelayCommand(() => OpenPath(AppPaths.NotesFile));
    }

    private const string PlayerDownloadUrl = "https://www.roblox.com/download";
    private const string StudioUrl = "https://www.roblox.com/download/studio";

    public LauncherSettings Settings { get; }

    public ObservableCollection<AccountItem> Accounts { get; }
    public ObservableCollection<PlaceItem> Places { get; }
    public ObservableCollection<RecentItem> Recents { get; }
    public ObservableCollection<PresetItem> Presets { get; }
    public ObservableCollection<PresetGroupItem> PresetGroups { get; }
    public ObservableCollection<PresetItem> VisiblePresets { get; } = new();
    public ObservableCollection<FastFlagCategoryItem> FastFlagCategories { get; } = new();
    public ObservableCollection<VersionItem> InstalledVersions { get; }
    public ObservableCollection<FontItem> FontFamilies { get; }
    public ObservableCollection<BuildItem> Builds { get; }

    public ICommand ToggleAddAccountCommand { get; }
    public ICommand AddAccountCommand { get; }
    public ICommand RefreshAccountsCommand { get; }
    public ICommand RemoveAccountCommand { get; }
    public ICommand LoginAccountCommand { get; }
    public ICommand LaunchAccountCommand { get; }
    public ICommand ToggleAddGameCommand { get; }
    public ICommand AddPlaceCommand { get; }
    public ICommand RemovePlaceCommand { get; }
    public ICommand PinPlaceCommand { get; }
    public ICommand UseRecentCommand { get; }
    public ICommand ForgetRecentCommand { get; }
    public ICommand ClearRecentsCommand { get; }
    public ICommand LaunchCommand { get; }
    public ICommand LaunchAppCommand { get; }
    public ICommand LaunchPlaceCommand { get; }
    public ICommand MultiLaunchCommand { get; }
    public ICommand SaveFastFlagsCommand { get; }
    public ICommand ApplyFastFlagsCommand { get; }
    public ICommand RegisterProtocolCommand { get; }
    public ICommand RestoreProtocolCommand { get; }
    public ICommand OpenConfigFolderCommand { get; }
    public ICommand OpenLogFileCommand { get; }
    public ICommand OpenClientFolderCommand { get; }
    public ICommand DownloadStudioCommand { get; }
    public ICommand ClientActionCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand RefreshInstallCommand { get; }
    public ICommand RestartCommand { get; }
    public ICommand ApplyCursorCommand { get; }
    public ICommand RestoreCursorCommand { get; }
    public ICommand ChooseCursorImageCommand { get; }
    public ICommand UseBundledCursorCommand { get; }
    public ICommand LoadFontFileCommand { get; }
    public ICommand OpenFontFolderCommand { get; }
    public ICommand FormatFlagsCommand { get; }
    public ICommand CopyFlagsCommand { get; }
    public ICommand ImportFlagsCommand { get; }
    public ICommand ExportFlagsCommand { get; }
    public ICommand ClearFlagsCommand { get; }
    public ICommand ShowVisualFlagsCommand { get; }
    public ICommand ShowJsonFlagsCommand { get; }
    public ICommand SelectFlagFilterCommand { get; }
    public ICommand ClearPresetSearchCommand { get; }
    public ICommand LoadBuildsCommand { get; }
    public ICommand DownloadBuildCommand { get; }
    public ICommand InstallBuildCommand { get; }
    public ICommand OpenDownloadFolderCommand { get; }
    public ICommand StartGameOverlayCommand { get; }
    public ICommand OpenLayoutCommand { get; }
    public ICommand CloseLayoutCommand { get; }
    public ICommand QuickLaunchCommand { get; }
    public ICommand ChangeShortcutCommand { get; }
    public ICommand ResetShortcutCommand { get; }
    public ICommand SaveNotesCommand { get; }
    public ICommand OpenNotesFileCommand { get; }

    // ---------------------------------------------------------------- Zustand

    public AccountItem? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (!Set(ref _selectedAccount, value)) return;
            RaiseHero();
            RaiseCommandStates();
        }
    }

    public PlaceItem? SelectedPlace
    {
        get => _selectedPlace;
        set
        {
            if (!Set(ref _selectedPlace, value)) return;
            RaiseHero();
            RaiseCommandStates();
        }
    }

    /// <summary>Spiegelt die Einstellung, damit die Anzeige beim Umschalten mitgeht.</summary>
    public bool UseCurrentLogin
    {
        get => Settings.UseCurrentLogin;
        set
        {
            if (Settings.UseCurrentLogin == value) return;
            Settings.UseCurrentLogin = value;
            Raise();
            RaiseHero();
        }
    }

    public string Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    // ------------------------------------------------ Versionsschalter, Schrift, Zeiger

    /// <summary>Der Versionsschalter: "Automatic" oder eine installierte Version.</summary>
    public VersionItem? SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            if (!Set(ref _selectedVersion, value)) return;
            ApplyVersionChoice();
        }
    }

    public string VersionChoiceLine => string.IsNullOrWhiteSpace(Settings.PinnedVersionGuid)
        ? "Automatic - newest installed version"
        : $"Pinned to {Settings.PinnedVersionGuid}";

    public FontItem? SelectedFont
    {
        get => _selectedFont;
        set
        {
            if (!Set(ref _selectedFont, value)) return;
            ApplyFontChoice();
        }
    }

    /// <summary>
    /// Vanilla-Modus: der Client bleibt, wie Roblox ihn installiert hat. Damit finden
    /// Programme wie Velocity, Madium oder Volt den normalen Zweig, den sie erwarten.
    /// </summary>
    public bool VanillaMode
    {
        get => Settings.VanillaMode;
        set
        {
            if (Settings.VanillaMode == value) return;
            Settings.VanillaMode = value;
            SaveSettings();
            Raise();
            RaiseMultiStart();
            RaiseClient();
            RaiseCommandStates();
            Status = value
                ? "Vanilla mode on - the client starts exactly as Roblox installed it."
                : "Vanilla mode off - Aether prepares the client again (flags, pointer, multi-instance).";
        }
    }

    public ObservableCollection<ThemeItem> Themes { get; }

    /// <summary>Farbwelt. Der Wechsel färbt die vorhandenen Pinsel sofort um.</summary>
    public ThemeItem? SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (!Set(ref _selectedTheme, value) || value is null) return;

            var palette = ThemeManager.Apply(value.Id);
            Settings.Theme = palette.Id;
            SaveSettings();
            Status = $"{palette.Name} theme applied.";
        }
    }

    /// <summary>Federnde Einblendungen und Glanz beim Überfahren.</summary>
    public bool PlayfulMode
    {
        get => Settings.PlayfulMode;
        set
        {
            if (Settings.PlayfulMode == value) return;
            Settings.PlayfulMode = value;
            SaveSettings();
            Raise();
        }
    }

    // ------------------------------------------------ Schnellansicht ("Layout")

    /// <summary>Steht die Schnellansicht gerade über der App?</summary>
    public bool LayoutOpen
    {
        get => _layoutOpen;
        set
        {
            if (!Set(ref _layoutOpen, value)) return;
            if (value) return;

            // Beim Zumachen die Notizen sofort sichern, damit nichts hängen bleibt.
            _notesTimer.Stop();
            SaveNotes(quiet: true);
        }
    }

    /// <summary>Die Schnellansicht als Ganzes - ab Werk an, Schalter auf der Engine-Seite.</summary>
    public bool OverlayEnabled
    {
        get => Settings.OverlayEnabled;
        set
        {
            if (Settings.OverlayEnabled == value) return;

            Settings.OverlayEnabled = value;
            SaveSettings();
            if (!value) LayoutOpen = false;

            Raise();
            Raise(nameof(LayoutShortcutHint));
            Status = value
                ? $"Quick layout on - press {Settings.OverlayShortcut} to open it."
                : "Quick layout off.";
        }
    }

    /// <summary>
    /// Die Schnellansicht über dem Roblox-Fenster. Sie ist ein eigener Prozess und zeigt
    /// nur ein Fenster über dem Spiel - nichts wird in den Client eingeschleust.
    /// </summary>
    public bool GameOverlayEnabled
    {
        get => Settings.GameOverlayEnabled;
        set
        {
            if (Settings.GameOverlayEnabled == value) return;

            Settings.GameOverlayEnabled = value;
            SaveSettings();
            Raise();

            if (!value)
            {
                Status = "The in-game overlay is off - it will not start with the next launch.";
                return;
            }

            GameOverlay.EnsureRunning();
            Status = $"In-game overlay on - press {Settings.OverlayShortcut} over Roblox.";
        }
    }

    /// <summary>Soll das Overlay aufgehen, sobald ein Roblox-Fenster da ist?</summary>
    public bool GameOverlayAutoShow
    {
        get => Settings.GameOverlayAutoShow;
        set
        {
            if (Settings.GameOverlayAutoShow == value) return;
            Settings.GameOverlayAutoShow = value;
            SaveSettings();
            Raise();
            Status = value
                ? "The in-game overlay opens automatically when Roblox starts."
                : $"The in-game overlay now waits for {Settings.OverlayShortcut}.";
        }
    }

    /// <summary>Das eingestellte Kürzel, wie es in der Oberfläche steht.</summary>
    public string LayoutShortcutLabel => Settings.OverlayShortcut;

    public string LayoutShortcutHint => _shortcutCapture
        ? "Press the new shortcut now - Esc cancels."
        : $"Opens with {Settings.OverlayShortcut}.";

    /// <summary>Wartet auf den nächsten Tastendruck für ein neues Kürzel.</summary>
    public bool ShortcutCapture
    {
        get => _shortcutCapture;
        private set
        {
            if (!Set(ref _shortcutCapture, value)) return;
            Raise(nameof(LayoutShortcutHint));
        }
    }

    /// <summary>Notizen der Schnellansicht - der Text wird kurz nach dem Tippen gespeichert.</summary>
    public string NotesText
    {
        get => _notesText;
        set
        {
            if (!Set(ref _notesText, value)) return;
            _notesTimer.Stop();
            _notesTimer.Start();
        }
    }

    public string NotesLine
    {
        get => _notesLine;
        private set => Set(ref _notesLine, value);
    }

    public string LayoutAccountLine => Accounts.Count == 0
        ? "No accounts saved yet - add one under Accounts."
        : $"Start with one of {Accounts.Count} saved account(s).";

    /// <summary>Der Aether-Pfeil als Zeiger der Anwendung.</summary>
    public bool CustomCursor
    {
        get => Settings.CustomCursor;
        set
        {
            if (Settings.CustomCursor == value) return;
            Settings.CustomCursor = value;
            SaveSettings();
            Raise();
        }
    }

    public bool CursorEnabled
    {
        get => Settings.InGameCursorEnabled;
        set
        {
            if (Settings.InGameCursorEnabled == value) return;
            Settings.InGameCursorEnabled = value;
            SaveSettings();
            Raise();

            if (value) ApplyCursorNow();
            else RestoreCursorNow();
        }
    }

    public string CursorImageLine => string.IsNullOrWhiteSpace(Settings.InGameCursorPath)
        ? "Bundled Aether pointer"
        : Path.GetFileName(Settings.InGameCursorPath!);

    public string CursorSummary
    {
        get => _cursorSummary;
        private set => Set(ref _cursorSummary, value);
    }

    public string FooterNote
    {
        get => _footerNote;
        set => Set(ref _footerNote, value);
    }

    public string InstallSummary
    {
        get => _installSummary;
        set => Set(ref _installSummary, value);
    }

    public string FastFlagsJson
    {
        get => _fastFlagsJson;
        set
        {
            if (!Set(ref _fastFlagsJson, value)) return;
            RefreshFlagState();
        }
    }

    /// <summary>Filter für die Vorschlagsliste im FastFlags-Editor.</summary>
    public string PresetSearch
    {
        get => _presetSearch;
        set
        {
            if (Set(ref _presetSearch, value)) ApplyPresetFilter();
        }
    }

    /// <summary>"all", "favorites" oder der Name einer Gruppe (z. B. "GRAPHICS").</summary>
    public string FastFlagFilter => _fastFlagFilter;

    /// <summary>Die Kartenansicht ist der Start; JSON bleibt über den Umschalter erreichbar.</summary>
    public bool FlagsVisualMode
    {
        get => _flagsVisualMode;
        set
        {
            if (!Set(ref _flagsVisualMode, value)) return;
            Raise(nameof(FlagsJsonMode));
            Raise(nameof(FlagsVisualVisibility));
            Raise(nameof(FlagsJsonVisibility));
        }
    }

    public bool FlagsJsonMode => !_flagsVisualMode;

    public Visibility FlagsVisualVisibility => _flagsVisualMode ? Visibility.Visible : Visibility.Collapsed;

    public Visibility FlagsJsonVisibility => _flagsVisualMode ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Zeile unter der Kartenansicht: sichtbare und bereits aktive Karten.</summary>
    public string VisualFlagsCountLine
    {
        get => _visualFlagsCountLine;
        private set => Set(ref _visualFlagsCountLine, value);
    }

    /// <summary>Wie viele Karten auf der aktuellen Allowlist stehen - ehrlich, nicht geschätzt.</summary>
    public string FastFlagHealthLine =>
        $"{Presets.Count} cards · {Presets.Count(p => p.Allowlisted)} still read by current Roblox";

    /// <summary>"Nothing matches" in der Kartenansicht.</summary>
    public Visibility VisualEmptyVisibility =>
        VisiblePresets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Der Hinweistext im Suchfeld, solange es leer ist.</summary>
    public Visibility PresetSearchPlaceholderVisibility =>
        string.IsNullOrWhiteSpace(_presetSearch) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Zeile unter dem Filter: wie viele Vorschläge gerade sichtbar sind.</summary>
    public string PresetCountLine
    {
        get => _presetCountLine;
        private set => Set(ref _presetCountLine, value);
    }

    /// <summary>"Nothing matches" - nur wenn der Filter alles weggefiltert hat.</summary>
    public Visibility PresetEmptyVisibility =>
        PresetGroups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Anzahl der Flags im Editor - steht als Marke an der Überschrift.</summary>
    public string FlagsCountLabel
    {
        get => _flagsCountLabel;
        private set => Set(ref _flagsCountLabel, value);
    }

    /// <summary>Zeile unter dem Editor: Anzahl der Flags, Anzahl gesetzter Vorschläge oder der Fehler.</summary>
    public string FlagsStatus
    {
        get => _flagsStatus;
        private set => Set(ref _flagsStatus, value);
    }

    public bool FlagsHasError
    {
        get => _flagsHasError;
        private set => Set(ref _flagsHasError, value);
    }

    // ------------------------------------------------ Versionsdownload

    public string BuildSearch
    {
        get => _buildSearch;
        set
        {
            if (Set(ref _buildSearch, value)) ApplyBuildFilter();
        }
    }

    public string BuildsSummary
    {
        get => _buildsSummary;
        private set => Set(ref _buildsSummary, value);
    }

    public double DownloadPercent
    {
        get => _downloadPercent;
        private set => Set(ref _downloadPercent, value);
    }

    public bool Downloading
    {
        get => _downloading;
        private set
        {
            if (!Set(ref _downloading, value)) return;
            Raise(nameof(DownloadingVisibility));
        }
    }

    public Visibility DownloadingVisibility => _downloading ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Der in der Download-Liste angeklickte Build.</summary>
    public BuildItem? SelectedBuild
    {
        get => _selectedBuild;
        set
        {
            if (!Set(ref _selectedBuild, value)) return;
            RaiseCommandStates();
        }
    }

    public string InstallerLine
    {
        get => _installerLine;
        private set => Set(ref _installerLine, value);
    }

    /// <summary>Wird vom PasswordBox im Code-Behind gesetzt - nie gebunden, damit der Schlüssel nicht im UI-Baum landet.</summary>
    public string NewCookie
    {
        get => _newCookie;
        set
        {
            if (Set(ref _newCookie, value)) RaiseCommandStates();
        }
    }

    public string AddAccountError
    {
        get => _addAccountError;
        set => Set(ref _addAccountError, value);
    }

    public string NewPlaceInput
    {
        get => _newPlaceInput;
        set
        {
            if (Set(ref _newPlaceInput, value)) RaiseCommandStates();
        }
    }

    public bool AddAccountOpen
    {
        get => _addAccountOpen;
        set
        {
            if (!Set(ref _addAccountOpen, value)) return;
            Raise(nameof(AddAccountPanelVisibility));
        }
    }

    public bool AddGameOpen
    {
        get => _addGameOpen;
        set
        {
            if (!Set(ref _addGameOpen, value)) return;
            Raise(nameof(AddGamePanelVisibility));
        }
    }

    /// <summary>Schreibt die Flags vor jedem Start - der Schalter liegt auch in der Schnellansicht.</summary>
    public bool ApplyFastFlagsOnLaunch
    {
        get => Settings.ApplyFastFlags;
        set
        {
            if (Settings.ApplyFastFlags == value) return;

            Settings.ApplyFastFlags = value;
            SaveSettings();
            Raise();
            RaiseClient();
            Status = value
                ? "FastFlags are written into the client before every start."
                : "FastFlags are no longer written on launch.";
        }
    }

    public Visibility AddAccountPanelVisibility => AddAccountOpen ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AddGamePanelVisibility => AddGameOpen ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BusyVisibility => Busy ? Visibility.Visible : Visibility.Collapsed;

    public bool Busy
    {
        get => _busy;
        set
        {
            if (!Set(ref _busy, value)) return;
            Raise(nameof(BusyVisibility));
            RaiseCommandStates();
        }
    }

    public string LogText => Log.Text;

    public Visibility AccountsEmptyVisibility => Accounts.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    public Visibility PlacesEmptyVisibility => Places.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Die Zuletzt-Liste zeigt sich nur, wenn es etwas zu zeigen gibt.</summary>
    public Visibility RecentsVisibility => Recents.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RecentsEmptyVisibility => Recents.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// Die Zeile über der Liste erklärt nur, woher sie kommt - die Spiele selbst stehen
    /// darunter. Roblox' eigene Sitzungen werden beim Start aus den Logdateien nachgetragen.
    /// </summary>
    public string RecentsLine => Recents.Count == 0
        ? "Start a game and it turns up here - Roblox's own sessions are picked up too."
        : "Newest first - from your Roblox sessions and every start through Aether.";

    public string AppVersion => "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");
    public string VersionLine => "version " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");
    public string DataFolderLine => AppPaths.DataDir;

    // ---------------------------------------------------------------- Kopfkarte

    public string HeroTitle => SelectedPlace?.Title ?? "Roblox home";

    /// <summary>Beschriftung des Hauptknopfes: mit Spiel "Play", ohne Spiel "Open Roblox".</summary>
    public string PlayLabel => SelectedPlace is null ? "Open Roblox" : "Play";

    public string HeroSubtitle
    {
        get
        {
            if (SelectedPlace is null)
            {
                var who = UseCurrentLogin || SelectedAccount is null
                    ? "the current Roblox login"
                    : SelectedAccount.Handle;
                return $"No game selected - this opens the Roblox app with {who}.";
            }

            var parts = new List<string> { SelectedPlace.Subtitle };
            parts.Add(UseCurrentLogin || SelectedAccount is null
                ? "current Roblox login"
                : $"as {SelectedAccount.Handle}");
            return string.Join("  ·  ", parts);
        }
    }

    public Brush? HeroIconBrush => SelectedPlace?.IconBrush;

    // ---------------------------------------------------------------- Clientkarte

    public string ClientVersionLine => _installedVersion is null
        ? "No Roblox client found"
        : $"{_installedVersion.Guid}  ·  {_installedVersion.NumericVersion}";

    public string ClientServesLine => _deployed is null
        ? $"Roblox Client serves: not checked yet (channel {Settings.Channel})"
        : $"Roblox Client serves {_deployed.NumericVersion} ({_deployed.Guid})";

    public string ClientVanillaLine => Settings.VanillaMode
        ? "Vanilla mode: nothing is written into this client."
        : $"FastFlags: {(Settings.ApplyFastFlags ? "on" : "off")}  ·  in-game pointer: {(Settings.InGameCursorEnabled ? "on" : "off")}";

    public string ClientActionText
    {
        get
        {
            if (_installedVersion is null) return "Download Roblox";
            return IsClientBehind() ? "Update Roblox" : "Roblox is up to date";
        }
    }

    public string MultiLaunchSummary
    {
        get
        {
            if (Settings.VanillaMode) return "vanilla mode - one client at a time";
            return Accounts.Count == 0 ? "no accounts yet" : $"{Accounts.Count(a => a.IsMarked)} of {Accounts.Count} selected";
        }
    }

    // ---------------------------------------------------------------- Multi-Start-Pause

    public bool DelayIs4
    {
        get => Settings.MultiLaunchStaggerMs == 4000;
        set { if (value) SetDelay(4000); }
    }

    public bool DelayIs8
    {
        get => Settings.MultiLaunchStaggerMs == 8000;
        set { if (value) SetDelay(8000); }
    }

    public bool DelayIs15
    {
        get => Settings.MultiLaunchStaggerMs == 15000;
        set { if (value) SetDelay(15000); }
    }

    private void SetDelay(int milliseconds)
    {
        Settings.MultiLaunchStaggerMs = milliseconds;
        Raise(nameof(DelayIs4));
        Raise(nameof(DelayIs8));
        Raise(nameof(DelayIs15));
    }

    // ---------------------------------------------------------------- Start

    public async Task InitializeAsync(string? startupLaunchUri)
    {
        await RefreshInstallAsync().ConfigureAwait(true);

        Settings.UseCurrentLogin = Settings.UseCurrentLogin || Accounts.Count == 0;
        Raise(nameof(UseCurrentLogin));
        RaiseHero();

        ImportRobloxSessions();
        _ = LoadImagesSafeAsync();

        if (startupLaunchUri is not null) await HandleExternalLaunchAsync(startupLaunchUri).ConfigureAwait(true);

        Log.Info("Aether Launcher ready.");
        Status = _installedVersion is null
            ? "No Roblox client found - see Settings."
            : $"Ready - {_installedVersion.Guid}.";
    }

    /// <summary>Startanfrage von außen (Protokollhandler).</summary>
    public async Task HandleExternalLaunchAsync(string uri)
    {
        Status = "Handling an external launch request…";
        Log.Info("External launch request received.");

        await _launchGate.WaitAsync(_cts.Token).ConfigureAwait(true);
        try
        {
            Busy = true;
            var result = await _launcher.LaunchExternalUriAsync(uri, _cts.Token).ConfigureAwait(true);
            Status = result.Message;
            if (result.PlaceId is { } placeId) RememberRecentPlace(placeId);
        }
        finally
        {
            Busy = false;
            _launchGate.Release();
        }
    }

    private async Task RefreshInstallAsync()
    {
        Busy = true;
        try
        {
            _installedVersion = _launcher.FindInstall(out var roots);

            if (_installedVersion is null)
            {
                InstallSummary = "No Roblox client found";
                Log.Warn($"No Roblox client found. Looked in: {roots}");
            }
            else
            {
                var root = Path.GetDirectoryName(Path.GetDirectoryName(_installedVersion.Directory));
                var count = RobloxInstall.Discover(root).Count;
                InstallSummary = $"{_installedVersion.Guid}  ·  {count} version(s) installed";
                Log.Info($"Roblox client found: {_installedVersion.Directory}");
            }

            _deployed = await RobloxInstall.GetDeployedVersionAsync(Settings.Channel, _cts.Token).ConfigureAwait(true);

            RefreshVersions();

            // "Neu einlesen" heißt auch: Roblox' eigene Sitzungen noch einmal nachtragen. So
            // steht ein Spiel, das ohne Aether lief, sofort in der Zuletzt-Liste - ohne Neustart.
            ImportRobloxSessions();
            ReloadRecents();
            await LoadRecentIconsAsync().ConfigureAwait(true);

            RaiseCursor();
            RaiseClient();
            RaiseCommandStates();
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>
    /// Bilder und Namen laden, ohne dass ein Fehler den Start stört. Ohne das Fangnetz endet
    /// die Aufgabe unsichtbar - dann bliebe die Zuletzt-Liste bei "Place 123…" stehen, und
    /// im Protokoll stünde kein Wort darüber.
    /// </summary>
    private async Task LoadImagesSafeAsync()
    {
        try
        {
            await LoadImagesAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Beim Schließen des Fensters ist das der normale Weg.
        }
        catch (Exception ex)
        {
            Log.Warn($"An image or the recently played list could not be refreshed: {ex.Message}");
        }
    }

    private async Task LoadImagesAsync()
    {
        foreach (var account in Accounts.ToList())
            account.Avatar = await ImageCache.GetAvatarAsync(account.UserId, _cts.Token).ConfigureAwait(true);

        foreach (var place in Places.ToList())
        {
            if (place.Record.UniverseId is null) await ResolveGameAsync(place).ConfigureAwait(true);
            else place.Icon = await ImageCache.GetGameIconAsync(place.Record.UniverseId.Value, _cts.Token).ConfigureAwait(true);
        }

        await LoadRecentIconsAsync().ConfigureAwait(true);
    }

    /// <summary>Holt Name und Spielebilder der Zuletzt-Liste nach (läuft im Hintergrund weiter).</summary>
    private async Task LoadRecentIconsAsync()
    {
        // Zwei Auslöser können kurz nacheinander kommen (Start und ein Import). Ohne diese
        // Sperre liefen zwei Runden gleichzeitig und fragten Roblox doppelt.
        if (_loadingRecent)
        {
            _recentReloadQueued = true;
            return;
        }

        _loadingRecent = true;
        try
        {
            do
            {
                _recentReloadQueued = false;

                foreach (var recent in Recents.ToList())
                {
                    // Ein Eintrag gilt erst als fertig, wenn Bild <b>und</b> Name da sind. Nur auf
                    // das Bild zu schauen war der Fehler: ein zwischengespeichertes Icon ließ die
                    // Zeile für immer als "Place 123…" stehen.
                    if (recent.Icon is not null && !NeedsName(recent)) continue;

                    // Eine Sitzung aus Roblox' Logs kennt nur die IDs - der Name fehlt noch.
                    // Deshalb wird auch dann gefragt, wenn die Universe-ID schon dasteht.
                    if (recent.Record.UniverseId is null || NeedsName(recent))
                        await ResolveRecentAsync(recent).ConfigureAwait(true);
                    else
                        recent.Icon = await ImageCache.GetGameIconAsync(recent.Record.UniverseId.Value, _cts.Token).ConfigureAwait(true);
                }
            }
            while (_recentReloadQueued);
        }
        finally
        {
            _loadingRecent = false;
        }
    }

    /// <summary>Fehlt der echte Name noch? "Place 123…" ist nur der Platzhalter.</summary>
    private static bool NeedsName(RecentItem item) =>
        string.IsNullOrWhiteSpace(item.Record.Name) ||
        string.Equals(item.Record.Name, $"Place {item.Record.PlaceId}", StringComparison.Ordinal);

    /// <summary>Holt Name und Icon eines Eintrags der Zuletzt-Liste nach.</summary>
    private async Task ResolveRecentAsync(RecentItem item)
    {
        var resolved = await RobloxApi.ResolveGameAsync(item.Record.PlaceId, _cts.Token).ConfigureAwait(true);
        if (resolved is null)
        {
            // Auch das wird gesagt: sonst bliebe "Place 123…" stehen, ohne dass irgendwo
            // stünde, warum. "Rescan" versucht es erneut.
            Log.Warn($"No name for place {item.Record.PlaceId} - Roblox did not answer.");
            return;
        }

        // Erst den Eintrag in der Datei treffen, dann die Zeile: oft sind beide dasselbe
        // Objekt. Würde erst die Zeile gesetzt, sähe Update keinen Unterschied mehr und
        // würde nichts schreiben - genau daran blieb "Place 128…" stehen.
        _recentStore.Update(item.Record.PlaceId, resolved.Value.Name, resolved.Value.UniverseId);
        _recentStore.Save();

        item.Record.Name = resolved.Value.Name;
        item.Record.UniverseId = resolved.Value.UniverseId;
        item.Refresh();

        item.Icon = await ImageCache.GetGameIconAsync(resolved.Value.UniverseId, _cts.Token).ConfigureAwait(true);
    }

    /// <summary>Holt Spielname, Universe-ID und Icon nach (z. B. nach einem Import).</summary>
    private async Task ResolveGameAsync(PlaceItem item)
    {
        var resolved = await RobloxApi.ResolveGameAsync(item.Record.PlaceId, _cts.Token).ConfigureAwait(true);
        if (resolved is null) return;

        item.Record.Name = resolved.Value.Name;
        item.Record.UniverseId = resolved.Value.UniverseId;
        _placeStore.Save();
        item.Refresh();

        item.Icon = await ImageCache.GetGameIconAsync(resolved.Value.UniverseId, _cts.Token).ConfigureAwait(true);
    }

    private async Task AddAccountAsync()
    {
        var cookie = NewCookie.Trim();
        if (cookie.Length == 0)
        {
            AddAccountError = "Sign in with Roblox or paste a launch key first.";
            return;
        }

        await AddAccountWithCookieAsync(cookie);
    }

    /// <summary>
    /// Nimmt ein Cookie aus der eingebetteten Roblox-Anmeldung oder aus dem Einfügefeld,
    /// prüft es und legt das Konto an. Liefert false, wenn der Schlüssel nicht gültig ist.
    /// </summary>
    public async Task<bool> AddAccountWithCookieAsync(string cookie)
    {
        cookie = cookie.Trim();
        if (cookie.Length == 0)
        {
            AddAccountError = "The login did not return a session key. Try again.";
            return false;
        }

        if (Busy) return false;

        Busy = true;
        Status = "Checking the key with roblox.com…";
        try
        {
            var info = await RobloxApi.ValidateCookieAsync(cookie, _cts.Token).ConfigureAwait(true);
            if (info is null)
            {
                AddAccountError = "That key is not valid (or has expired). Sign in again and try once more.";
                Status = "Account not added.";
                return false;
            }

            var existing = Accounts.FirstOrDefault(a => a.UserId == info.UserId);
            var record = _accountStore.Upsert(cookie, info);

            if (existing is null)
            {
                var item = CreateAccountItem(record);
                Accounts.Add(item);
                SelectedAccount = item;
                item.Avatar = await ImageCache.GetAvatarAsync(info.UserId, _cts.Token).ConfigureAwait(true);
            }
            else
            {
                existing.ClearProblem();
                existing.Refresh();
                SelectedAccount = existing;
            }

            AddAccountError = "";
            AddAccountOpen = false;
            NewCookie = "";
            UseCurrentLogin = false;
            Status = existing is null ? $"{info.Username} added." : $"{info.Username} signed in again.";
            Log.Info($"Account {(existing is null ? "added" : "refreshed")}: {info.Username} (ID {info.UserId}).");
            return true;
        }
        catch (Exception ex)
        {
            AddAccountError = $"The account could not be added: {ex.Message}";
            Status = "Account not added.";
            Log.Error($"Account could not be added: {ex.Message}");
            return false;
        }
        finally
        {
            Busy = false;
            RaiseAccounts();
        }
    }

    private async Task RefreshAccountsAsync()
    {
        Busy = true;
        try
        {
            foreach (var account in Accounts.ToList())
            {
                var cookie = _accountStore.GetCookie(account.Record);
                if (cookie is null)
                {
                    account.SetProblem("key missing - add it again");
                    continue;
                }

                var info = await RobloxApi.ValidateCookieAsync(cookie, _cts.Token).ConfigureAwait(true);
                if (info is null)
                {
                    account.SetProblem("key expired - add it again");
                    Log.Warn($"{account.Handle}: key rejected by roblox.com.");
                    continue;
                }

                account.Record.Username = info.Username;
                account.Record.DisplayName = info.DisplayName;
                account.Record.LastValidatedAt = DateTimeOffset.Now;
                account.ClearProblem();
                account.Refresh();
            }

            _accountStore.Save();
            Status = "Accounts checked.";
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task AddPlaceAsync()
    {
        if (!RobloxUri.TryParsePlace(NewPlaceInput, out var placeId, out var accessCode))
        {
            FooterNote = "That is not a valid place ID, game link or private server link.";
            return;
        }

        Busy = true;
        Status = $"Looking up place {placeId}…";
        try
        {
            var resolved = await RobloxApi.ResolveGameAsync(placeId, _cts.Token).ConfigureAwait(true);
            var record = _placeStore.Add(placeId, accessCode, resolved?.Name ?? $"Place {placeId}");
            if (resolved is not null)
            {
                record.UniverseId = resolved.Value.UniverseId;
                _placeStore.Save();
            }

            var existing = Places.FirstOrDefault(p => p.Record.Id == record.Id);
            if (existing is null)
            {
                var item = new PlaceItem(record);
                Places.Insert(0, item);
                ApplyPlaceOrder(Places.ToList());
                SelectedPlace = item;
                if (record.UniverseId is not null)
                    item.Icon = await ImageCache.GetGameIconAsync(record.UniverseId.Value, _cts.Token).ConfigureAwait(true);
            }
            else
            {
                existing.Refresh();
                SelectedPlace = existing;
            }

            NewPlaceInput = "";
            AddGameOpen = false;
            Status = $"{record.Name} added.";
            Raise(nameof(PlacesEmptyVisibility));
            RaiseCommandStates();
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task LaunchSelectedAsync()
    {
        // Ohne gewähltes Spiel ist der Start kein Fehler mehr: dann öffnet sich die Roblox-App.
        if (SelectedPlace is null)
        {
            await LaunchAppAsync(UseCurrentLogin ? null : SelectedAccount?.Record).ConfigureAwait(true);
            return;
        }

        var account = UseCurrentLogin ? null : SelectedAccount?.Record;
        await LaunchCoreAsync(SelectedPlace.Record, account).ConfigureAwait(true);
    }

    /// <summary>Die Roblox-App (Startseite) öffnen - mit dem gerade gewählten Konto.</summary>
    private Task LaunchAppForAccountAsync()
        => LaunchAppAsync(UseCurrentLogin ? null : SelectedAccount?.Record);

    private async Task LaunchAppAsync(AccountRecord? account)
    {
        await _launchGate.WaitAsync(_cts.Token).ConfigureAwait(true);
        Busy = true;
        try
        {
            var result = await _launcher.LaunchAppAsync(account, _cts.Token).ConfigureAwait(true);
            Status = result.Message;
        }
        finally
        {
            Busy = false;
            _launchGate.Release();
        }
    }

    private async Task LaunchCoreAsync(PlaceRecord place, AccountRecord? account)
    {
        await _launchGate.WaitAsync(_cts.Token).ConfigureAwait(true);
        Busy = true;
        try
        {
            var result = await _launcher.LaunchPlaceAsync(place, account, _cts.Token).ConfigureAwait(true);
            Status = result.Message;

            if (!result.Success) return;

            place.LastPlayedAt = DateTimeOffset.Now;
            place.LaunchCount++;
            _placeStore.Save();
            SelectedPlace?.Refresh();
            SortPlaces();
            ReloadRecents();
            RaiseHero();
        }
        finally
        {
            Busy = false;
            _launchGate.Release();
        }
    }

    /// <summary>Startet denselben Platz nacheinander für alle eingeschalteten Konten.</summary>
    private async Task MultiLaunchAsync()
    {
        if (SelectedPlace is null) return;

        var targets = Accounts.Where(a => a.IsMarked).ToList();
        if (targets.Count == 0)
        {
            Status = "Switch on the accounts you want in the multi-start.";
            return;
        }

        if (Settings.VanillaMode)
        {
            Status = "Vanilla mode is on - Aether holds nothing back, so only one client starts. Turn it off for a multi-start.";
            return;
        }

        if (!Settings.MultiInstance)
        {
            Status = "Multiple instances must be enabled for a multi-start.";
            return;
        }

        await _launchGate.WaitAsync(_cts.Token).ConfigureAwait(true);
        Busy = true;
        try
        {
            Log.Info($"Multi-start: {targets.Count} account(s) into {SelectedPlace.Name}.");

            for (var i = 0; i < targets.Count; i++)
            {
                var account = targets[i];
                Status = $"Multi-start {i + 1}/{targets.Count}: {account.Handle}…";

                var result = await _launcher.LaunchPlaceAsync(SelectedPlace.Record, account.Record, _cts.Token).ConfigureAwait(true);
                account.Status = result.Success ? "launched" : result.Message;
                if (!result.Success) Log.Warn($"{account.Handle}: {result.Message}");

                // Roblox braucht zwischen zwei Clients einen Moment, sonst überholen sie sich.
                if (i < targets.Count - 1) await Task.Delay(Settings.MultiLaunchStaggerMs, _cts.Token).ConfigureAwait(true);
            }

            SelectedPlace.Record.LastPlayedAt = DateTimeOffset.Now;
            SelectedPlace.Record.LaunchCount++;
            _placeStore.Save();
            SelectedPlace.Refresh();
            RaiseHero();
            Status = $"Multi-start done ({targets.Count} accounts).";
        }
        catch (OperationCanceledException)
        {
            Status = "Multi-start cancelled.";
        }
        finally
        {
            Busy = false;
            _launchGate.Release();
        }
    }

    // ---------------------------------------------------------------- Engine / Einstellungen

    // ------------------------------------------------ FastFlags-Editor

    /// <summary>Ein Vorschlag wurde angehakt oder abgehakt - das JSON folgt sofort.</summary>
    private void OnPresetChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_syncingPresets) return;
        if (sender is not PresetItem preset) return;

        if (e.PropertyName is nameof(PresetItem.IsFavorite))
        {
            FastFlags.SaveFavorites(Presets.Where(p => p.IsFavorite).Select(p => p.Name));
            ApplyPresetFilter();
            return;
        }

        if (e.PropertyName is not (nameof(PresetItem.IsMarked) or null)) return;

        ApplyPresetChoice(preset);
    }

    private void ApplyPresetChoice(PresetItem preset)
    {
        if (FastFlags.ParseJson(FastFlagsJson, out var flags) is { } error)
        {
            // Ohne lesbares JSON wird nichts geschrieben - der Haken geht zurück und der Grund steht da.
            _syncingPresets = true;
            try
            {
                preset.IsMarked = !preset.IsMarked;
            }
            finally
            {
                _syncingPresets = false;
            }

            FlagsStatus = error;
            FlagsHasError = true;
            Status = "Presets need valid JSON on the left.";
            return;
        }

        var changed = preset.IsMarked
            ? FastFlags.MergePreset(flags, preset.Preset)
            : FastFlags.RemovePreset(flags, preset.Preset);

        FastFlagsJson = FastFlags.Serialize(flags);
        FastFlags.TrySaveJson(FastFlagsJson, out _);

        Status = preset.IsMarked
            ? $"{preset.Name}: {changed} flag(s) written into the JSON."
            : changed > 0
                ? $"{preset.Name}: {changed} flag(s) removed again."
                : $"{preset.Name}: those values were edited by hand, so nothing was removed.";

        Log.Info(Status);
    }

    /// <summary>Gleicht Statuszeile und Haken der Vorschläge an den JSON-Text an.</summary>
    private void RefreshFlagState()
    {
        if (FastFlags.ParseJson(FastFlagsJson, out var flags) is not null)
        {
            FlagsStatus = "Invalid JSON - presets stay untouched until this parses.";
            FlagsHasError = true;
            return;
        }

        FlagsHasError = false;

        var guard = _syncingPresets;
        _syncingPresets = true;
        try
        {
            foreach (var preset in Presets)
                preset.IsMarked = FastFlags.ContainsPreset(flags, preset.Preset);
        }
        finally
        {
            _syncingPresets = guard;
        }

        var marked = Presets.Count(p => p.IsMarked);
        FlagsStatus = $"{flags.Count} flag(s) in the editor" +
                      (marked > 0 ? $"  ·  {marked} preset(s) active" : "  ·  no preset active");

        FlagsCountLabel = flags.Count == 1 ? "1 flag" : $"{flags.Count} flags";
    }

    private void ApplyPresetFilter()
    {
        var needle = _presetSearch.Trim();
        var matched = Presets
            .Where(p => needle.Length == 0 || Matches(p, needle))
            .ToList();

        var selected = _fastFlagFilter switch
        {
            "favorites" => matched.Where(p => p.IsFavorite).ToList(),
            "all" => matched,
            _ => matched.Where(p => string.Equals(p.Group, _fastFlagFilter, StringComparison.Ordinal)).ToList(),
        };

        // Noch gelesene Karten zuerst - sonst beginnt die Übersicht mit lauter
        // "usually ignored"-Karten. Danach in der Reihenfolge der sichtbaren Filter:
        // FPS, Graphics, Lighting, Effects, Textures, Interface, Network.
        var visible = selected
            .OrderByDescending(p => p.Allowlisted)
            .ThenBy(p => GroupOrder(p.Group))
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        VisiblePresets.Clear();
        foreach (var preset in visible) VisiblePresets.Add(preset);

        PresetGroups.Clear();
        foreach (var group in visible.GroupBy(p => p.Group))
            PresetGroups.Add(new PresetGroupItem(group.Key, group));

        FastFlagCategories.Clear();
        FastFlagCategories.Add(new FastFlagCategoryItem("all", $"All · {matched.Count}", matched.Count));
        FastFlagCategories.Add(new FastFlagCategoryItem("favorites", $"★ Favorites · {matched.Count(p => p.IsFavorite)}",
            matched.Count(p => p.IsFavorite)));

        foreach (var group in Presets.Select(p => p.Group).Distinct())
            FastFlagCategories.Add(new FastFlagCategoryItem(group, $"{CategoryLabel(group)} · {matched.Count(p => p.Group == group)}",
                matched.Count(p => p.Group == group)));

        foreach (var category in FastFlagCategories)
            category.IsSelected = string.Equals(category.Key, _fastFlagFilter, StringComparison.Ordinal);

        var shown = VisiblePresets.Count;
        PresetCountLine = needle.Length == 0
            ? $"{shown} presets shown in {PresetGroups.Count} groups"
            : $"{shown} of {Presets.Count} presets match \"{needle}\"";

        VisualFlagsCountLine = $"{shown} card{(shown == 1 ? "" : "s")} shown · {visible.Count(p => p.IsMarked)} active";

        Raise(nameof(PresetEmptyVisibility));
        Raise(nameof(VisualEmptyVisibility));
        Raise(nameof(PresetSearchPlaceholderVisibility));
    }

    private void SelectFlagFilter(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) key = "all";
        _fastFlagFilter = key;
        ApplyPresetFilter();
    }

    private static string CategoryLabel(string group) => group switch
    {
        FastFlags.FrameRateGroup => "FPS",
        FastFlags.GraphicsGroup => "Graphics",
        FastFlags.SkyGroup => "Lighting",
        FastFlags.RenderGroup => "Effects",
        FastFlags.DetailGroup => "Textures",
        FastFlags.InterfaceGroup => "Interface",
        FastFlags.NetworkGroup => "Network",
        _ => group,
    };

    private static int GroupOrder(string group) => group switch
    {
        FastFlags.FrameRateGroup => 0,
        FastFlags.GraphicsGroup => 1,
        FastFlags.SkyGroup => 2,
        FastFlags.RenderGroup => 3,
        FastFlags.DetailGroup => 4,
        FastFlags.InterfaceGroup => 5,
        FastFlags.NetworkGroup => 6,
        _ => 7,
    };

    private static bool Matches(PresetItem preset, string needle)
        => preset.Name.Contains(needle, StringComparison.CurrentCultureIgnoreCase) ||
           preset.Description.Contains(needle, StringComparison.CurrentCultureIgnoreCase) ||
           preset.Keys.Contains(needle, StringComparison.CurrentCultureIgnoreCase);

    private void FormatFlags()
    {
        if (FastFlags.ParseJson(FastFlagsJson, out var flags) is { } error)
        {
            Status = error;
            FlagsStatus = error;
            FlagsHasError = true;
            return;
        }

        FastFlagsJson = FastFlags.Serialize(flags);
        Status = $"Formatted - {flags.Count} flag(s).";
    }

    private void CopyFlags()
    {
        try
        {
            Clipboard.SetText(FastFlagsJson);
            Status = "The JSON is in the clipboard.";
        }
        catch (Exception ex)
        {
            Status = $"The clipboard is busy: {ex.Message}";
        }
    }

    private void ImportFlags()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import FastFlags",
            Filter = "JSON (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            FastFlagsJson = File.ReadAllText(dialog.FileName);
            Status = $"Imported {Path.GetFileName(dialog.FileName)} - press Save to keep it.";
        }
        catch (Exception ex)
        {
            Status = $"Could not read the file: {ex.Message}";
        }
    }

    private void ExportFlags()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export FastFlags",
            FileName = "ClientAppSettings.json",
            Filter = "JSON (*.json)|*.json",
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            File.WriteAllText(dialog.FileName, FastFlagsJson);
            Status = $"Exported to {Path.GetFileName(dialog.FileName)}.";
        }
        catch (Exception ex)
        {
            Status = $"Could not write the file: {ex.Message}";
        }
    }

    private void ClearFlags()
    {
        FastFlagsJson = "{\n}\n";
        Status = "All flags cleared - press Save to keep the empty list.";
    }

    // ------------------------------------------------ Versionen herunterladen

    private async Task LoadBuildsAsync()
    {
        Busy = true;
        Status = "Asking WEAO and Roblox for the version list…";
        try
        {
            var builds = await RobloxVersions.GetAvailableAsync(_cts.Token).ConfigureAwait(true);

            var installed = new HashSet<string>(
                RobloxInstall.DiscoverAll(Settings).Select(v => v.Guid), StringComparer.OrdinalIgnoreCase);

            _allBuilds.Clear();
            _allBuilds.AddRange(builds.Select(b => new BuildItem(b, installed.Contains(b.Guid))));
            ApplyBuildFilter();

            var tracked = builds.Count(b => b.Source.StartsWith("WEAO", StringComparison.Ordinal));
            BuildsSummary = builds.Count == 0
                ? "No version list received - see the log."
                : $"{builds.Count} build(s) known: {tracked} tracked by WEAO, {builds.Count - tracked} from the deploy history.";
            Status = BuildsSummary;
        }
        finally
        {
            Busy = false;
        }
    }

    private void ApplyBuildFilter()
    {
        var needle = _buildSearch.Trim();
        var matches = _allBuilds
            .Where(b => needle.Length == 0 ||
                        b.Build.Guid.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                        b.Build.NumericVersion.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                        b.Build.Source.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .Take(300)
            .ToList();

        Builds.Clear();
        foreach (var item in matches) Builds.Add(item);

        if (_allBuilds.Count > matches.Count)
            BuildsSummary = $"{matches.Count} of {_allBuilds.Count} build(s) shown - narrow the filter.";

        RaiseCommandStates();
    }

    private async Task DownloadBuildAsync(BuildItem item)
    {
        Busy = true;
        Downloading = true;
        DownloadPercent = 0;
        Status = $"Downloading {item.Build.Guid}…";
        Log.Info($"Downloading the installer for {item.Build.Guid} from the Roblox CDN.");

        try
        {
            var target = Path.Combine(AppPaths.DownloadDir, item.Build.Guid + "-RobloxPlayerInstaller.exe");
            var progress = new Progress<double>(percent =>
            {
                DownloadPercent = percent;
                Status = $"Downloading {item.Build.Guid}… {percent:0}%";
            });

            var success = await RobloxVersions
                .DownloadInstallerAsync(item.Build.Guid, target, Settings.Channel, progress, _cts.Token)
                .ConfigureAwait(true);

            if (!success)
            {
                _installerPath = null;
                InstallerLine = "Nothing downloaded yet.";
                Status = $"The installer for {item.Build.Guid} could not be downloaded - see the log.";
                return;
            }

            _installerPath = target;
            var megabytes = new FileInfo(target).Length / 1024.0 / 1024.0;
            InstallerLine = $"{Path.GetFileName(target)} ({megabytes:0.0} MB) - ready to install.";
            Status = $"Installer for {item.Build.Guid} is ready - press Install to run it.";
        }
        finally
        {
            Downloading = false;
            DownloadPercent = 0;
            Busy = false;
            RaiseCommandStates();
        }
    }

    /// <summary>Einen bereits heruntergeladenen Installer wieder anbieten (nach einem Neustart).</summary>
    private void RecallDownloadedInstaller()
    {
        try
        {
            var newest = new DirectoryInfo(AppPaths.DownloadDir)
                .EnumerateFiles("*-RobloxPlayerInstaller.exe")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            if (newest is null) return;

            _installerPath = newest.FullName;
            InstallerLine = $"{newest.Name} ({newest.Length / 1024.0 / 1024.0:0.0} MB) - ready to install.";
        }
        catch (Exception ex)
        {
            Log.Warn($"The download folder could not be read: {ex.Message}");
        }
    }

    private void InstallBuild()
    {
        if (_installerPath is null || !File.Exists(_installerPath)) return;

        try
        {
            Process.Start(new ProcessStartInfo(_installerPath) { UseShellExecute = true })?.Dispose();
            Status = "Roblox's installer is running - afterwards press Rescan and pin the version above.";
            Log.Info($"Installer started: {Path.GetFileName(_installerPath)}");
        }
        catch (Exception ex)
        {
            Status = $"The installer could not be started: {ex.Message}";
            Log.Error($"The installer could not be started: {ex.Message}");
        }
    }

    private void SaveFastFlags()
    {
        var error = FastFlags.TrySaveJson(FastFlagsJson, out var flags);
        if (error is not null)
        {
            Status = error;
            return;
        }

        FastFlagsJson = FastFlags.LoadJsonText();
        Status = $"{flags.Count} FastFlag(s) saved.";
    }

    private void ApplyFastFlagsNow()
    {
        SaveFastFlags();
        if (_installedVersion is null)
        {
            Status = "No Roblox client found.";
            return;
        }

        if (Settings.VanillaMode)
        {
            Status = "Vanilla mode is on - the flags are saved but not written into the client.";
            return;
        }

        FastFlags.Apply(_installedVersion.Directory, out var message);
        Status = message;
    }

    // ------------------------------------------------ Versionsschalter

    private void ApplyVersionChoice()
    {
        var guid = _selectedVersion?.Version?.Guid;
        if (string.Equals(Settings.PinnedVersionGuid, guid, StringComparison.OrdinalIgnoreCase)) return;

        Settings.PinnedVersionGuid = guid;
        SaveSettings();
        RefreshVersionBadges();

        Log.Info(guid is null
            ? "Version switch: automatic (newest installed version)."
            : $"Version switch: pinned to {guid}.");
        Status = guid is null
            ? "Version switch: automatic - the newest installed version."
            : $"Version switch: {guid} starts from now on.";

        _ = RefreshInstallAsync();
    }

    /// <summary>Füllt den Versionsschalter aus den gefundenen Installationen.</summary>
    private void RefreshVersions()
    {
        var installed = RobloxInstall.DiscoverAll(Settings);
        var items = new List<VersionItem> { new(null, false) };
        items.AddRange(installed.Select(v => new VersionItem(v,
            string.Equals(v.Guid, _deployed?.Guid, StringComparison.OrdinalIgnoreCase))));

        InstalledVersions.Clear();
        foreach (var item in items) InstalledVersions.Add(item);

        // Auswahl wiederherstellen, ohne sie erneut zu setzen (sonst würde sie zurückgeschrieben).
        _selectedVersion = InstalledVersions.FirstOrDefault(i => string.Equals(
                              i.Version?.Guid, Settings.PinnedVersionGuid, StringComparison.OrdinalIgnoreCase))
                          ?? InstalledVersions[0];
        Raise(nameof(SelectedVersion));
        RefreshVersionBadges();
    }

    private void RefreshVersionBadges()
    {
        foreach (var item in InstalledVersions)
            item.IsPinned = string.Equals(item.Version?.Guid, Settings.PinnedVersionGuid, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------ Schrift

    private void ApplyFontChoice()
    {
        var name = _selectedFont is null || _selectedFont.IsDefault ? null : _selectedFont.Family;
        if (string.Equals(Settings.UiFontFamily, name, StringComparison.CurrentCultureIgnoreCase)) return;

        Settings.UiFontFamily = name;
        SaveSettings();
        AppFonts.Apply(Settings);

        Status = name is null ? "UI font back to the Windows default." : $"UI font: {name}.";
        Log.Info($"UI font set to {AppFonts.Label(Settings)}.");
    }

    /// <summary>Lädt eine eigene Schriftdatei (.ttf/.otf) in den Datenordner.</summary>
    private void LoadFontFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Load a font",
            Filter = "Fonts (*.ttf;*.otf)|*.ttf;*.otf",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true) return;

        var family = AppFonts.Import(dialog.FileName);
        if (family is null)
        {
            Status = "That file is not a font Aether can read.";
            return;
        }

        Settings.UiFontFile = Path.Combine(AppPaths.FontDir, Path.GetFileName(dialog.FileName));
        if (FontFamilies.All(f => !string.Equals(f.Family, family, StringComparison.CurrentCultureIgnoreCase)))
            FontFamilies.Add(new FontItem(family, isDefault: false));

        SelectedFont = FontFamilies.FirstOrDefault(f =>
                           string.Equals(f.Family, family, StringComparison.CurrentCultureIgnoreCase)) ?? FontFamilies[0];
        SaveSettings();
        Status = $"{family} loaded and set as the UI font.";
    }

    // ------------------------------------------------ Ingame-Zeiger

    private void ChooseCursorImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose the pointer image",
            Filter = "PNG image (*.png)|*.png",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true) return;

        var size = ImageTool.SizeOf(dialog.FileName);
        if (size is null)
        {
            Status = "That image could not be read.";
            return;
        }

        Settings.InGameCursorPath = dialog.FileName;
        SaveSettings();
        Raise(nameof(CursorImageLine));
        Status = $"Pointer image: {Path.GetFileName(dialog.FileName)} ({size.Value.Width}x{size.Value.Height}).";
    }

    private void UseBundledCursor()
    {
        var path = ImageTool.EnsureBundledCursor(AppPaths.CursorImageFile);
        if (string.IsNullOrEmpty(path))
        {
            Status = "The bundled pointer image could not be unpacked.";
            return;
        }

        Settings.InGameCursorPath = path;
        SaveSettings();
        Raise(nameof(CursorImageLine));
        Status = "The bundled Aether pointer is selected.";
    }

    private void ApplyCursorNow()
    {
        if (_installedVersion is null)
        {
            Status = "No Roblox client found.";
            return;
        }

        if (Settings.VanillaMode)
        {
            Status = "Vanilla mode is on - the client's textures stay untouched.";
            return;
        }

        var source = ClientMods.ResolveCursorImage(Settings);
        SaveSettings();
        Raise(nameof(CursorImageLine));

        ClientMods.ApplyCursor(_installedVersion.Directory, source, out var message);
        Status = message;
        RaiseCursor();
    }

    private void RestoreCursorNow()
    {
        if (_installedVersion is null)
        {
            Status = "No Roblox client found.";
            return;
        }

        ClientMods.RestoreCursor(_installedVersion.Directory, out var message);
        Status = message;
        RaiseCursor();
    }

    private void RaiseCursor()
    {
        if (_installedVersion is null)
        {
            CursorSummary = "No client found";
        }
        else
        {
            var available = ClientMods.DiscoverCursorTextures(_installedVersion.Directory).Count;
            var modified = ClientMods.CountModified(_installedVersion.Directory);
            CursorSummary = modified > 0
                ? $"{modified} of {available} pointer textures currently replaced"
                : $"{available} pointer textures in {_installedVersion.ShortGuid}";
        }

        Raise(nameof(CursorEnabled));
    }

    private void RegisterProtocol()
    {
        Settings.SavedProtocolHandler = ProtocolHandler.ReadCurrentCommand();
        if (ProtocolHandler.Register(AppPaths.ExecutablePath, out var message))
        {
            Settings.ProtocolHandlerRegistered = true;
            SaveSettings();
        }

        Status = message;
        RaiseCommandStates();
    }

    private void RestoreProtocol()
    {
        if (ProtocolHandler.Restore(Settings.SavedProtocolHandler, out var message))
        {
            Settings.ProtocolHandlerRegistered = false;
            SaveSettings();
        }

        Status = message;
        RaiseCommandStates();
    }

    /// <summary>Download bzw. Update des Clients - beides über die offizielle Installerseite.</summary>
    private async Task ClientActionAsync()
    {
        if (_installedVersion is null)
        {
            OpenUrl(PlayerDownloadUrl);
            Status = "Opening the official Roblox download…";
            return;
        }

        await RefreshInstallAsync().ConfigureAwait(true);

        if (IsClientBehind())
        {
            OpenUrl(PlayerDownloadUrl);
            Status = $"Update available ({_deployed?.Guid}) - the official installer will fetch it.";
            Log.Info($"Client {_installedVersion.Guid} is behind {_deployed?.Guid}.");
            return;
        }

        Status = "The installed client matches the deployment channel.";
    }

    private bool IsClientBehind()
        => _installedVersion is not null && _deployed is not null &&
           !string.Equals(_deployed.Guid, _installedVersion.Guid, StringComparison.OrdinalIgnoreCase);

    private void OpenClientFolder()
    {
        if (_installedVersion is null) return;
        OpenPath(_installedVersion.Directory);
    }

    /// <summary>Neustart: der neue Prozess wartet, bis dieser beendet ist (sonst greift die Einzelinstanz-Sperre).</summary>
    private void Restart()
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppPaths.ExecutablePath, "--restart-wait " + Environment.ProcessId)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            })?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error($"Restart failed: {ex.Message}");
        }

        Application.Current.Shutdown();
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not open {url}: {ex.Message}");
        }
    }

    private static void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not open {path}: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------- Schnellansicht

    private void ToggleLayout()
    {
        if (!Settings.OverlayEnabled)
        {
            Status = "The quick layout is switched off - turn it on under Engine.";
            return;
        }

        LayoutOpen = !LayoutOpen;
    }

    private void CloseLayout()
    {
        ShortcutCapture = false;
        LayoutOpen = false;
    }

    /// <summary>Aus der Schnellansicht heraus als ein bestimmtes Konto starten.</summary>
    private async Task QuickLaunchAsync(object? parameter)
    {
        if (parameter is not AccountItem item) return;

        LayoutOpen = false;
        SelectedAccount = item;
        UseCurrentLogin = false;

        if (SelectedPlace is null) await LaunchAppAsync(item.Record).ConfigureAwait(true);
        else await LaunchCoreAsync(SelectedPlace.Record, item.Record).ConfigureAwait(true);
    }

    private void BeginShortcutCapture()
    {
        ShortcutCapture = true;
        Status = "Press the new shortcut for the quick layout - Esc cancels.";
    }

    /// <summary>Esc während des Umbelegens - das alte Kürzel bleibt.</summary>
    public void CancelShortcutCapture()
    {
        if (!ShortcutCapture) return;
        ShortcutCapture = false;
        Status = "The shortcut is unchanged.";
    }

    /// <summary>Nimmt die gedrückte Kombination als neues Kürzel - false, wenn sie sich nicht eignet.</summary>
    public bool TryApplyShortcut(ModifierKeys modifiers, Key key)
    {
        var text = Shortcuts.Format(modifiers, key);
        if (text is null)
        {
            Status = "That key will not do - use one key like Delete, Insert or F8, or add Ctrl, Alt or Shift.";
            return false;
        }

        ApplyShortcut(text);
        return true;
    }

    private void ResetShortcut()
    {
        ApplyShortcut(Shortcuts.Default);
        Status = $"Quick layout shortcut back to {Shortcuts.Default}.";
    }

    private void ApplyShortcut(string text)
    {
        Settings.OverlayShortcut = text;

        // Hier absichtlich ohne Abgleich: das ist der neue Stand, den das Overlay später liest.
        SaveSettings(mergeShortcut: false);
        ShortcutCapture = false;
        RaiseShortcut();
        Status = $"Quick layout shortcut: {text}.";
        Log.Info($"The quick layout shortcut is now {text}.");
    }

    /// <summary>
    /// Speichert die Einstellungen. Vorher wird das Kürzel vom Datenträger nachgezogen:
    /// das Ingame-Overlay kann es selbst umbelegen, und der Launcher hätte sonst noch seinen
    /// Stand vom Start im Speicher - der würde die Änderung beim Speichern stillschweigend
    /// zurücksetzen.
    /// </summary>
    private void SaveSettings(bool mergeShortcut = true)
    {
        if (mergeShortcut)
        {
            try
            {
                Settings.OverlayShortcut = _settingsStore.Load().OverlayShortcut;
            }
            catch
            {
                // Ist die Datei gerade nicht lesbar, bleibt der eigene Stand stehen.
            }
        }

        _settingsStore.Save(Settings);
    }

    private void RaiseShortcut()
    {
        Raise(nameof(LayoutShortcutLabel));
        Raise(nameof(LayoutShortcutHint));
        (ResetShortcutCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void SaveNotes(bool quiet)
    {
        if (Services.Notes.TrySave(NotesText, out var error))
        {
            NotesLine = $"Saved at {DateTime.Now:HH:mm}.";
            if (!quiet) Status = "Notes saved.";
            return;
        }

        NotesLine = error ?? "The notes could not be saved.";
        if (!quiet) Status = NotesLine;
    }

    // ---------------------------------------------------------------- Hilfen

    private AccountItem CreateAccountItem(AccountRecord record)
    {
        var item = new AccountItem(record);
        item.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not (nameof(AccountItem.IsMarked) or null)) return;
            RaiseMultiStart();
            RaiseCommandStates();
        };
        return item;
    }

    private void RememberRecentPlace(long placeId)
    {
        if (placeId <= 0) return;

        var record = _placeStore.Find(placeId) ?? _placeStore.Add(placeId, null, $"Place {placeId}");
        var existing = Places.FirstOrDefault(p => p.Record.Id == record.Id);
        if (existing is not null)
        {
            SelectedPlace = existing;
            return;
        }

        var item = new PlaceItem(record);
        Places.Insert(0, item);
        ApplyPlaceOrder(Places.ToList());
        SelectedPlace = item;
        Raise(nameof(PlacesEmptyVisibility));
        _ = ResolveGameAsync(item);
    }

    /// <summary>
    /// Verschiebt ein Spiel in der Liste - mit der Maus gezogen. Die Reihenfolge landet in
    /// places.json, damit sie nach einem Neustart noch genauso aussieht.
    /// </summary>
    public void MovePlace(PlaceItem item, PlaceItem target, bool before)
    {
        if (ReferenceEquals(item, target)) return;

        var order = Places.ToList();
        if (!order.Remove(item)) return;

        var index = order.IndexOf(target);
        if (index < 0) return;

        order.Insert(before ? index : index + 1, item);
        ApplyPlaceOrder(order);

        SelectedPlace = item;
        Status = $"{item.Name} moved.";
    }

    /// <summary>Übernimmt eine Reihenfolge in die Liste und schreibt sie fest.</summary>
    private void ApplyPlaceOrder(IReadOnlyList<PlaceItem> order)
    {
        for (var index = 0; index < order.Count; index++)
        {
            var current = Places.IndexOf(order[index]);
            if (current != index) Places.Move(current, index);
        }

        _placeStore.SetOrder(order.Select(item => item.Record));
    }

    /// <summary>
    /// Roblox' eigene Sitzungen nachtragen: der Client schreibt bei jedem Start eine Logdatei
    /// mit der Platz-ID. So steht in "Recently played" auch, was ohne Aether gespielt wurde.
    /// </summary>
    private void ImportRobloxSessions()
    {
        try
        {
            var sessions = RobloxLogImport.Sessions();
            if (sessions.Count == 0) return;

            var added = 0;
            foreach (var session in sessions)
            {
                if (_recentStore.Import(session.PlaceId, session.UniverseId, session.When)) added++;
            }

            if (added == 0) return;

            ReloadRecents();
            Log.Info($"{added} Roblox session(s) were added to the recently played list.");
        }
        catch (Exception ex)
        {
            Log.Warn($"Roblox's own history could not be read: {ex.Message}");
        }
    }

    /// <summary>
    /// Die Reihenfolge der Liste: gezogene Spiele stehen genau da, wo sie stehen. Solange
    /// noch nie gezogen wurde, gilt die alte Regel - Angeheftete zuerst, dann das zuletzt
    /// Gespielte.
    /// </summary>
    private void SortPlaces()
    {
        var manual = Places.Any(place => place.Record.SortIndex > 0);

        var ordered = manual
            ? Places.OrderBy(place => place.Record.SortIndex).ToList()
            : Places.OrderByDescending(place => place.Record.IsPinned)
                .ThenByDescending(place => place.Record.PinnedAt ?? DateTimeOffset.MinValue)
                .ThenByDescending(place => place.Record.LastPlayedAt ?? place.Record.AddedAt)
                .ToList();

        for (var target = 0; target < ordered.Count; target++)
        {
            var current = Places.IndexOf(ordered[target]);
            if (current != target) Places.Move(current, target);
        }
    }

    /// <summary>Liest die Zuletzt-Liste neu ein (nach einem Start schreibt der Dienst sie).</summary>
    private void ReloadRecents()
    {
        _recentStore.Load();

        var savedIds = Places.Select(p => p.Record.PlaceId).ToHashSet();
        var known = Recents.ToDictionary(r => r.Record.PlaceId);

        Recents.Clear();
        foreach (var entry in _recentStore.Entries)
        {
            // Bekannte Zeilen behalten ihr Bild - sonst lädt die Liste nach jedem Start neu.
            // Die Zeile wird trotzdem neu gebaut, damit sie auf dem Eintrag sitzt, der gerade
            // in der Datei steht: sonst schriebe ein späterer Name ins Leere.
            var item = new RecentItem(entry)
            {
                Icon = known.TryGetValue(entry.PlaceId, out var existing) ? existing.Icon : null,
            };
            item.IsSaved = savedIds.Contains(entry.PlaceId);
            Recents.Add(item);
        }

        RaiseRecents();
        _ = LoadRecentIconsAsync();
    }

    private void RaiseRecents()
    {
        Raise(nameof(RecentsEmptyVisibility));
        Raise(nameof(RecentsLine));
    }

    private void RaiseHero()
    {
        Raise(nameof(HeroTitle));
        Raise(nameof(HeroSubtitle));
        Raise(nameof(HeroIconBrush));
        Raise(nameof(PlayLabel));
    }

    private void RaiseClient()
    {
        Raise(nameof(ClientVersionLine));
        Raise(nameof(ClientServesLine));
        Raise(nameof(ClientActionText));
        Raise(nameof(ClientVanillaLine));
        Raise(nameof(VersionChoiceLine));
    }

    private void RaiseMultiStart()
    {
        Raise(nameof(MultiLaunchSummary));
        Raise(nameof(AccountsEmptyVisibility));
    }

    private void RaiseAccounts()
    {
        Raise(nameof(AccountsEmptyVisibility));
        RaiseMultiStart();
        RaiseCommandStates();
    }

    private void RaiseCommandStates()
    {
        foreach (var command in new ICommand[]
                 {
                     ToggleAddAccountCommand, AddAccountCommand, RefreshAccountsCommand, AddPlaceCommand,
                     LaunchCommand, LaunchPlaceCommand, LaunchAccountCommand, MultiLaunchCommand,
                     RestoreProtocolCommand, RefreshInstallCommand, ClientActionCommand, OpenClientFolderCommand,
                     ApplyCursorCommand, RestoreCursorCommand, LaunchCommand, LaunchAppCommand,
                     LoadBuildsCommand, DownloadBuildCommand, InstallBuildCommand,
                     QuickLaunchCommand, ResetShortcutCommand,
                 })
        {
            switch (command)
            {
                case AsyncRelayCommand async: async.RaiseCanExecuteChanged(); break;
                case RelayCommand relay: relay.RaiseCanExecuteChanged(); break;
            }
        }

        Raise(nameof(PlacesEmptyVisibility));
        Raise(nameof(AccountsEmptyVisibility));
    }

    public void Dispose()
    {
        _notesTimer.Stop();
        SaveNotes(quiet: true);
        SaveSettings();
        try
        {
            _cts.Cancel();
        }
        catch
        {
            // egal
        }

        _cts.Dispose();
    }
}
