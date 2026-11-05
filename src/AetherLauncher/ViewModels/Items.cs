using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AetherLauncher.Infrastructure;
using AetherLauncher.Models;
using AetherLauncher.Services;

namespace AetherLauncher.ViewModels;


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

    
    public bool IsMarked
    {
        get => _isMarked;
        set => Set(ref _isMarked, value);
    }

    
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

    
    public Brush? AvatarBrush => _avatar is null
        ? null
        : new ImageBrush(_avatar) { Stretch = Stretch.UniformToFill };

    public void Refresh() => RaiseAll();
}


public sealed class PlaceItem : ObservableObject
{
    private ImageSource? _icon;

    public PlaceItem(PlaceRecord record) => Record = record;

    public PlaceRecord Record { get; }

    public string Name => string.IsNullOrWhiteSpace(Record.Name) ? $"Place {Record.PlaceId}" : Record.Name;

    public string Title => Record.AccessCode is null ? Name : $"{Name} (private server)";

    
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

    
    public bool IsPinned => Record.IsPinned;

    
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

    
    public void RefreshPin()
    {
        Raise(nameof(IsPinned));
        Raise(nameof(PinGlyph));
        Raise(nameof(PinTip));
    }
}





public sealed class RecentItem : ObservableObject
{
    private ImageSource? _icon;
    private bool _isSaved;

    public RecentItem(RecentRecord record) => Record = record;

    public RecentRecord Record { get; }

    public string Name => string.IsNullOrWhiteSpace(Record.Name) ? $"Place {Record.PlaceId}" : Record.Name;

    public string Title => Name;

    
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


public sealed class PresetItem : ObservableObject
{
    private bool _isMarked;
    private bool _isFavorite;
    private readonly Brush _accent;
    private readonly Brush _accentSoft;
    private readonly string _glyph;

    public PresetItem(FastFlagPreset preset)
    {
        Preset = preset;
        var look = LookFor(preset.Group);
        _accent = MakeBrush(look.Accent, 255);
        _accentSoft = MakeBrush(look.Accent, 42);
        _glyph = look.Glyph;

        ToggleCommand = new RelayCommand(() => IsMarked = !IsMarked);
        FavoriteCommand = new RelayCommand(() => IsFavorite = !IsFavorite);
    }

    public FastFlagPreset Preset { get; }
    public string Name => Preset.Name;
    public string Description => Preset.Description;
    public string Group => Preset.Group;
    public IReadOnlyList<KeyValuePair<string, string>> Flags => Preset.Flags;

    
    public string Keys => Preset.Keys;

    
    
    
    
    public bool Allowlisted => Preset.Allowlisted;

    public string HonorText => Preset.Allowlisted ? "still active" : "usually ignored";

    public string HonorTip => Preset.Allowlisted
        ? "This flag is on Roblox' allowlist - the client still reads it."
        : "Roblox only honours allowlisted flags since September 2025, so the client normally ignores this one. Aether writes it anyway, in case you run a pinned older client.";

    public string BadgeText => Preset.Allowlisted ? "ALLOWLISTED" : "LEGACY";

    public string FlagsLabel => Preset.Flags.Count == 1 ? "1 flag" : $"{Preset.Flags.Count} flags";

    public string Glyph => _glyph;

    
    public string GroupLabel => Group switch
    {
        FastFlags.FrameRateGroup => "Frame rate",
        FastFlags.GraphicsGroup => "Graphics",
        FastFlags.SkyGroup => "Lighting",
        FastFlags.RenderGroup => "Effects",
        FastFlags.DetailGroup => "Textures",
        FastFlags.InterfaceGroup => "Interface",
        FastFlags.NetworkGroup => "Network",
        _ => Group,
    };

    public Brush AccentBrush => _accent;

    public Brush AccentSoftBrush => _accentSoft;

    public string CardTip => $"{Description}\n\n{HonorTip}\n\n{Keys}";

    public ICommand ToggleCommand { get; }

    public ICommand FavoriteCommand { get; }

    public string StateLabel => _isMarked ? "ACTIVE" : "ENABLE";

    public string StateTip => _isMarked ? "Click to remove these flags" : "Click to write these flags";

    public string FavoriteGlyph => _isFavorite ? "★" : "☆";

    public string FavoriteTip => _isFavorite ? "Remove from favorites" : "Keep in favorites";

    public bool IsMarked
    {
        get => _isMarked;
        set
        {
            if (!Set(ref _isMarked, value)) return;
            Raise(nameof(StateLabel));
            Raise(nameof(StateTip));
        }
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (!Set(ref _isFavorite, value)) return;
            Raise(nameof(FavoriteGlyph));
            Raise(nameof(FavoriteTip));
        }
    }

    private static (string Accent, string Glyph) LookFor(string group) => group switch
    {
        FastFlags.FrameRateGroup => ("#4EA1FF", "FPS"),
        FastFlags.GraphicsGroup => ("#43D6A0", "GFX"),
        FastFlags.SkyGroup => ("#FFB84D", "SKY"),
        FastFlags.RenderGroup => ("#B07CFF", "FX"),
        FastFlags.DetailGroup => ("#FF7A90", "TEX"),
        FastFlags.InterfaceGroup => ("#66D9E8", "UI"),
        FastFlags.NetworkGroup => ("#F5D76E", "NET"),
        _ => ("#7C8CFF", "FF"),
    };

    private static SolidColorBrush MakeBrush(string hex, byte alpha)
    {
        var color = ColorConverter.ConvertFromString(hex) is Color parsed ? parsed : Colors.SlateGray;
        color.A = alpha;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}


public sealed class FastFlagCategoryItem : ObservableObject
{
    private bool _isSelected;

    public FastFlagCategoryItem(string key, string label, int count) => (Key, Label, Count) = (key, label, count);

    public string Key { get; }
    public string Label { get; }
    public int Count { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }
}





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


public sealed class VersionItem : ObservableObject
{
    private bool _isPinned;

    public VersionItem(RobloxVersion? version, bool isDeployed)
    {
        Version = version;
        IsDeployed = isDeployed;
    }

    public RobloxVersion? Version { get; }

    
    public bool IsAutomatic => Version is null;

    
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

    
    public Visibility PinnedVisibility => _isPinned ? Visibility.Visible : Visibility.Collapsed;
}


public sealed class BuildItem
{
    public BuildItem(RobloxBuild build, bool installed)
    {
        Build = build;
        IsInstalled = installed;
    }

    public RobloxBuild Build { get; }

    
    public bool IsInstalled { get; }

    public string Title => Build.Guid;
    public string Detail => Build.Detail;
    public Visibility InstalledVisibility => IsInstalled ? Visibility.Visible : Visibility.Collapsed;
}


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

    
    public FontFamily Preview => new(string.IsNullOrWhiteSpace(Family) ? AppFonts.DefaultFamily : Family);

    public string? Detail => IsDefault ? "The Windows default" : null;
    public Visibility DetailVisibility => Detail is null ? Visibility.Collapsed : Visibility.Visible;
}
