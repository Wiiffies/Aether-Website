using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AetherLauncher.Infrastructure;
using AetherLauncher.Models;
using AetherLauncher.Services;

namespace AetherLauncher.ViewModels;

/// <summary>Ein Konto, wie es die Oberfläche braucht: Avatar, Kurzzeile, Markierung, Status.</summary>
public sealed class AccountItem : ObservableObject
{
    private bool _isMarked;
    private string _status = "";
    private ImageSource? _avatar;
    private bool _hasProblem;

    public AccountItem(AccountRecord record) => Record = record;

    public AccountRecord Record { get; }

    public long UserId => Record.UserId;
    public string Username => Record.Username;
    public string Handle => "@" + Record.Username;
    public string Title => string.IsNullOrWhiteSpace(Record.DisplayName) ? Record.Username : Record.DisplayName;

    /// <summary>Zeile wie im Aether-Hub: "key stored · added 2 d ago".</summary>
    public string Subtitle => _hasProblem
        ? _status
        : $"key stored · added {Time.Ago(Record.AddedAt)}";

    public bool HasProblem
    {
        get => _hasProblem;
        set
        {
            if (!Set(ref _hasProblem, value)) return;
            Raise(nameof(Subtitle));
        }
    }

    public string Initials
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(Title) ? "?" : Title.Trim();
            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 1
                ? $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}"
                : name[..1].ToUpperInvariant();
        }
    }

    /// <summary>Wird im Multi-Start markiert.</summary>
    public bool IsMarked
    {
        get => _isMarked;
        set => Set(ref _isMarked, value);
    }

    /// <summary>Kurze Rückmeldung der letzten Aktion (z. B. "Launched").</summary>
    public string Status
    {
        get => _status;
        set
        {
            if (_hasProblem && !Set(ref _status, value)) return;
            _status = value;
            Raise();
            Raise(nameof(Subtitle));
        }
    }

    /// <summary>Setzt eine dauerhafte Warnung (Cookie ungültig) statt einer Momentaufnahme.</summary>
    public void SetProblem(string message)
    {
        _status = message;
        HasProblem = true;
    }

    public void ClearProblem()
    {
        HasProblem = false;
        Status = "";
    }

    public ImageSource? Avatar
    {
        get => _avatar;
        set
        {
            if (!Set(ref _avatar, value)) return;
            Raise(nameof(AvatarBrush));
        }
    }

    /// <summary>Rund zugeschnittenes Bild für die Zeile.</summary>
    public Brush? AvatarBrush => _avatar is null
        ? null
        : new ImageBrush(_avatar) { Stretch = Stretch.UniformToFill };

    public void Refresh() => RaiseAll();
}

/// <summary>Ein gespeicherter Platz, wie ihn die Oberfläche braucht.</summary>
public sealed class PlaceItem : ObservableObject
{
    private ImageSource? _icon;

    public PlaceItem(PlaceRecord record) => Record = record;

    public PlaceRecord Record { get; }

    public string Name => string.IsNullOrWhiteSpace(Record.Name) ? $"Place {Record.PlaceId}" : Record.Name;

    public string Title => Record.AccessCode is null ? Name : $"{Name} (private server)";

    /// <summary>Untertitel der Zeile: "Place-ID 1818 · 3× · 2 d ago".</summary>
    public string Subtitle
    {
        get
        {
            var parts = new List<string> { $"Place-ID {Record.PlaceId}" };
            if (Record.LaunchCount > 0) parts.Add($"{Record.LaunchCount}×");
            if (Record.LastPlayedAt is not null) parts.Add(Time.Ago(Record.LastPlayedAt.Value));
            return string.Join("  ·  ", parts);
        }
    }

    /// <summary>Angeheftet (Favorit): steht oben in der Liste, egal wie alt der letzte Start ist.</summary>
    public bool IsPinned => Record.IsPinned;

    /// <summary>Der Stern neben der Zeile.</summary>
    public string PinGlyph => Record.IsPinned ? "★" : "☆";

    public string PinTip => Record.IsPinned
        ? "Unpin - back into the normal order"
        : "Pin to the top";

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (!Set(ref _icon, value)) return;
            Raise(nameof(IconBrush));
        }
    }

    public Brush? IconBrush => _icon is null
        ? null
        : new ImageBrush(_icon) { Stretch = Stretch.UniformToFill };

    public void Refresh() => RaiseAll();

    /// <summary>Nach dem An- oder Abheften: Symbol und Hinweis neu zeichnen.</summary>
    public void RefreshPin()
    {
        Raise(nameof(IsPinned));
        Raise(nameof(PinGlyph));
        Raise(nameof(PinTip));
    }
}

/// <summary>
/// Ein Eintrag der Liste "Recently played": ein Start, der mitgeschrieben wurde - auch wenn
/// das Spiel nie gespeichert wurde. Von hier lässt es sich in die eigene Sammlung übernehmen.
/// </summary>
public sealed class RecentItem : ObservableObject
{
    private ImageSource? _icon;
    private bool _isSaved;

    public RecentItem(RecentRecord record) => Record = record;

    public RecentRecord Record { get; }

    public string Name => string.IsNullOrWhiteSpace(Record.Name) ? $"Place {Record.PlaceId}" : Record.Name;

    public string Title => Name;

    /// <summary>"Place-ID 1818 · 3× · 2 h ago · as @name"</summary>
    public string Subtitle
    {
        get
        {
            var parts = new List<string> { $"Place-ID {Record.PlaceId}" };
            if (Record.Sessions > 1) parts.Add($"{Record.Sessions}×");
            parts.Add(Time.Ago(Record.PlayedAt));
            if (Record.FromLog) parts.Add("Roblox session");
            if (!string.IsNullOrWhiteSpace(Record.AccountName)) parts.Add("as @" + Record.AccountName);
            return string.Join("  ·  ", parts);
        }
    }

    /// <summary>Steckt der Platz schon in der eigenen Sammlung?</summary>
    public bool IsSaved
    {
        get => _isSaved;
        set
        {
            if (!Set(ref _isSaved, value)) return;
            Raise(nameof(SaveGlyph));
            Raise(nameof(SaveTip));
        }
    }

    public string SaveGlyph => _isSaved ? "★" : "+";

    public string SaveTip => _isSaved
        ? "Already in your games"
        : "Add to your games";

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (!Set(ref _icon, value)) return;
            Raise(nameof(IconBrush));
        }
    }

    public Brush? IconBrush => _icon is null
        ? null
        : new ImageBrush(_icon) { Stretch = Stretch.UniformToFill };

    public void Refresh()
    {
        RaiseAll();
    }
}

/// <summary>Ein FastFlag-Vorschlag.</summary>
public sealed class PresetItem : ObservableObject
{
    private bool _isMarked;

    public PresetItem(FastFlagPreset preset) => Preset = preset;

    public FastFlagPreset Preset { get; }
    public string Name => Preset.Name;
    public string Description => Preset.Description;
    public string Group => Preset.Group;
    public IReadOnlyList<KeyValuePair<string, string>> Flags => Preset.Flags;

    /// <summary>Die Flag-Namen unter dem Vorschlag - ein Vorschlag kann mehrere Flags setzen.</summary>
    public string Keys => Preset.Keys;

    /// <summary>
    /// Steht der Vorschlag auf Roblox' Allowlist? Nur dann liest der Client das Flag noch -
    /// seit September 2025 wirft er alles andere stillschweigend weg.
    /// </summary>
    public bool Allowlisted => Preset.Allowlisted;

    public string HonorText => Preset.Allowlisted ? "still active" : "usually ignored";

    public string HonorTip => Preset.Allowlisted
        ? "This flag is on Roblox' allowlist - the client still reads it."
        : "Roblox only honours allowlisted flags since September 2025, so the client normally ignores this one. Aether writes it anyway, in case you run a pinned older client.";

    public bool IsMarked
    {
        get => _isMarked;
        set => Set(ref _isMarked, value);
    }
}

/// <summary>
/// Eine Gruppe von Vorschlägen ("GRAPHICS", "NETWORK", ...). Die Gruppe lässt sich
/// zuklappen: bei gut 60 Vorschlägen ist die Liste sonst eine lange Rolle.
/// </summary>
public sealed class PresetGroupItem : ObservableObject
{
    private bool _isOpen = true;

    public PresetGroupItem(string name, IEnumerable<PresetItem> items)
    {
        Name = name;
        Items = new ObservableCollection<PresetItem>(items);
        ToggleCommand = new RelayCommand(() => IsOpen = !IsOpen);
    }

    public string Name { get; }

    public ObservableCollection<PresetItem> Items { get; }

    public ICommand ToggleCommand { get; }

    /// <summary>"9 presets · 4 still active" - steht am Gruppenkopf.</summary>
    public string Summary =>
        $"{Items.Count} preset{(Items.Count == 1 ? "" : "s")} · {Items.Count(item => item.Allowlisted)} still active";

    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (!Set(ref _isOpen, value)) return;
            Raise(nameof(ItemsVisibility));
            Raise(nameof(Chevron));
        }
    }

    public Visibility ItemsVisibility => _isOpen ? Visibility.Visible : Visibility.Collapsed;

    public string Chevron => _isOpen ? "▾" : "▸";
}

/// <summary>Eine Zeile im Versionsschalter: "Automatisch" oder eine installierte Version.</summary>
public sealed class VersionItem : ObservableObject
{
    private bool _isPinned;

    public VersionItem(RobloxVersion? version, bool isDeployed)
    {
        Version = version;
        IsDeployed = isDeployed;
    }

    public RobloxVersion? Version { get; }

    /// <summary>Die Zeile "Automatic" (keine Version angeheftet).</summary>
    public bool IsAutomatic => Version is null;

    /// <summary>Die Version, die Roblox im eingestellten Kanal gerade ausliefert.</summary>
    public bool IsDeployed { get; }

    public string Guid => Version?.Guid ?? "automatic";

    public string Title => IsAutomatic ? "Automatic" : Version!.Guid;

    public string Subtitle
    {
        get
        {
            if (IsAutomatic) return "Take the newest installed version - what Roblox itself keeps up to date.";

            var parts = new List<string>();
            if (!string.IsNullOrEmpty(Version!.NumericVersion)) parts.Add(Version.NumericVersion);
            parts.Add("installed " + Time.Ago(Version.Modified));
            if (IsDeployed) parts.Add("served by the channel");
            return string.Join("  ·  ", parts);
        }
    }

    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (!Set(ref _isPinned, value)) return;
            Raise(nameof(PinnedVisibility));
        }
    }

    /// <summary>Die Markierung "IN USE" nur an der gerade benutzten Zeile.</summary>
    public Visibility PinnedVisibility => _isPinned ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>Ein Roblox-Build in der Download-Liste.</summary>
public sealed class BuildItem
{
    public BuildItem(RobloxBuild build, bool installed)
    {
        Build = build;
        IsInstalled = installed;
    }

    public RobloxBuild Build { get; }

    /// <summary>Steckt dieser Build schon unter den installierten Versionen?</summary>
    public bool IsInstalled { get; }

    public string Title => Build.Guid;
    public string Detail => Build.Detail;
    public Visibility InstalledVisibility => IsInstalled ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>Eine Farbwelt in der Auswahl. Der Punkt zeigt sofort, was man bekommt.</summary>
public sealed class ThemeItem
{
    public ThemeItem(ThemePalette palette)
    {
        Id = palette.Id;
        Name = palette.Name;

        var swatch = new SolidColorBrush(ThemeManager.Parse(palette.Accent));
        swatch.Freeze();
        Swatch = swatch;

        var surface = new SolidColorBrush(ThemeManager.Parse(palette.Card));
        surface.Freeze();
        Surface = surface;
    }

    public string Id { get; }
    public string Name { get; }
    public Brush Swatch { get; }
    public Brush Surface { get; }
}

/// <summary>Eine Schriftart in der Auswahlliste.</summary>
public sealed class FontItem
{
    public FontItem(string? family, bool isDefault)
    {
        Family = family;
        IsDefault = isDefault;
    }

    public string? Family { get; }
    public bool IsDefault { get; }
    public string Title => IsDefault ? AppFonts.DefaultLabel : Family!;

    /// <summary>Der Eintrag zeigt sich selbst in seiner eigenen Schrift - so sieht man sofort, was man bekommt.</summary>
    public FontFamily Preview => new(string.IsNullOrWhiteSpace(Family) ? AppFonts.DefaultFamily : Family);

    public string? Detail => IsDefault ? "The Windows default" : null;
    public Visibility DetailVisibility => Detail is null ? Visibility.Collapsed : Visibility.Visible;
}
