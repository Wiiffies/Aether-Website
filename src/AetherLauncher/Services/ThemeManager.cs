using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AetherLauncher.Services;





public sealed class ThemePalette
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    public required string Accent { get; init; }
    public required string AccentHi { get; init; }
    public required string AccentSoft { get; init; }
    public required string AccentDeep { get; init; }

    public required string Bg { get; init; }
    public required string BgDeep { get; init; }
    public required string Card { get; init; }
    public required string CardHi { get; init; }

    public required string Border { get; init; }
    public required string BorderSoft { get; init; }
    public required string BorderBlue { get; init; }
    public required string Chip { get; init; }
    public required string Hover { get; init; }
    public required string Bar { get; init; }
    public required string TabOn { get; init; }
    public required string Gutter { get; init; }
    public required string Code { get; init; }

    public required string HeroHi { get; init; }
    public required string HeroLo { get; init; }
    public required string RowOn { get; init; }
    public required string SegOn { get; init; }
    public required string ChipOn { get; init; }
    public required string ChipLine { get; init; }
}





public static class ThemeManager
{
    public const string DefaultId = "midnight";

    public static IReadOnlyList<ThemePalette> All { get; } = new[]
    {
        new ThemePalette
        {
            Id = DefaultId, Name = "Midnight",
            Accent = "#2672FA", AccentHi = "#5696FF", AccentSoft = "#183C82", AccentDeep = "#142240",
            Bg = "#0A0C11", BgDeep = "#07090E", Card = "#10131A", CardHi = "#14171F",
            Border = "#212632", BorderSoft = "#181C25", BorderBlue = "#28427A", Chip = "#1B1F29",
            Hover = "#191D26", Bar = "#1A1F2A", TabOn = "#172136", Gutter = "#0D1017", Code = "#12161E",
            HeroHi = "#143474", HeroLo = "#080D1A", RowOn = "#192C52", SegOn = "#1F4A9C",
            ChipOn = "#1E4A94", ChipLine = "#3460B2",
        },
        new ThemePalette
        {
            Id = "amethyst", Name = "Amethyst",
            Accent = "#8B5CF6", AccentHi = "#B794FF", AccentSoft = "#3A2A66", AccentDeep = "#241A3E",
            Bg = "#0B0912", BgDeep = "#08060E", Card = "#13101C", CardHi = "#181422",
            Border = "#2B2438", BorderSoft = "#1E1929", BorderBlue = "#4C3C78", Chip = "#211B2E",
            Hover = "#1E1827", Bar = "#1D1727", TabOn = "#261C3A", Gutter = "#0E0B16", Code = "#151020",
            HeroHi = "#3B2672", HeroLo = "#0A0714", RowOn = "#2B2148", SegOn = "#5B3FB2",
            ChipOn = "#513A9C", ChipLine = "#7C63D8",
        },
        new ThemePalette
        {
            Id = "emerald", Name = "Emerald",
            Accent = "#16C784", AccentHi = "#4BE0A6", AccentSoft = "#0F5A40", AccentDeep = "#0B2E23",
            Bg = "#07110D", BgDeep = "#050C09", Card = "#0C1813", CardHi = "#101E18",
            Border = "#1E332A", BorderSoft = "#14241D", BorderBlue = "#235743", Chip = "#14251D",
            Hover = "#13231C", Bar = "#12211A", TabOn = "#102A20", Gutter = "#081410", Code = "#0E1C16",
            HeroHi = "#0E4C37", HeroLo = "#050F0B", RowOn = "#15352A", SegOn = "#127A55",
            ChipOn = "#116B4B", ChipLine = "#2FAE81",
        },
        new ThemePalette
        {
            Id = "ember", Name = "Ember",
            Accent = "#FF7A45", AccentHi = "#FFA37B", AccentSoft = "#5E2C17", AccentDeep = "#33180D",
            Bg = "#120C09", BgDeep = "#0D0705", Card = "#1A120E", CardHi = "#20160F",
            Border = "#36251C", BorderSoft = "#261A13", BorderBlue = "#6B3A22", Chip = "#251A14",
            Hover = "#241811", Bar = "#22160F", TabOn = "#2B1A12", Gutter = "#140D0A", Code = "#1D130E",
            HeroHi = "#5C2A12", HeroLo = "#100805", RowOn = "#3A2015", SegOn = "#A94E24",
            ChipOn = "#98441F", ChipLine = "#D86A38",
        },
        new ThemePalette
        {
            Id = "rose", Name = "Rose",
            Accent = "#F45B9A", AccentHi = "#FF8FBE", AccentSoft = "#5E2340", AccentDeep = "#331124",
            Bg = "#120A10", BgDeep = "#0D060B", Card = "#1A1018", CardHi = "#20141D",
            Border = "#38232F", BorderSoft = "#261820", BorderBlue = "#6E2F4C", Chip = "#251620",
            Hover = "#24141D", Bar = "#22131B", TabOn = "#2B1723", Gutter = "#140A11", Code = "#1D1118",
            HeroHi = "#5E1F3D", HeroLo = "#10060C", RowOn = "#3A1B2C", SegOn = "#A63C68",
            ChipOn = "#963659", ChipLine = "#D95A8C",
        },
        new ThemePalette
        {
            Id = "glacier", Name = "Glacier",
            Accent = "#6DD6FF", AccentHi = "#A9E8FF", AccentSoft = "#1D4C63", AccentDeep = "#102B38",
            Bg = "#081015", BgDeep = "#050A0D", Card = "#0C171D", CardHi = "#101F27",
            Border = "#1E333E", BorderSoft = "#14252D", BorderBlue = "#2C5B71", Chip = "#132630",
            Hover = "#11222B", Bar = "#10212A", TabOn = "#102D3A", Gutter = "#09141A", Code = "#0D1C24",
            HeroHi = "#10465E", HeroLo = "#061017", RowOn = "#16394A", SegOn = "#1C6E8C",
            ChipOn = "#195F78", ChipLine = "#3EA3C2",
        },
        new ThemePalette
        {
            Id = "sandstone", Name = "Sandstone",
            Accent = "#E2A55F", AccentHi = "#F4C38C", AccentSoft = "#5C3C20", AccentDeep = "#332111",
            Bg = "#14110C", BgDeep = "#0E0B08", Card = "#1B1711", CardHi = "#221D15",
            Border = "#3C3324", BorderSoft = "#2A2317", BorderBlue = "#6F5734", Chip = "#271F16",
            Hover = "#261F17", Bar = "#241D14", TabOn = "#2D2517", Gutter = "#16120C", Code = "#1F1911",
            HeroHi = "#5E3D1C", HeroLo = "#110C07", RowOn = "#3E2E18", SegOn = "#94602A",
            ChipOn = "#835525", ChipLine = "#C88C45",
        },
        new ThemePalette
        {
            Id = "onyx", Name = "Onyx",
            Accent = "#5F6B7D", AccentHi = "#C9D3E0", AccentSoft = "#343B46", AccentDeep = "#1F242B",
            Bg = "#0B0C0E", BgDeep = "#07080A", Card = "#121417", CardHi = "#171A1E",
            Border = "#2A2E34", BorderSoft = "#1C1F24", BorderBlue = "#4C535E", Chip = "#1B1E23",
            Hover = "#191C20", Bar = "#181B1F", TabOn = "#20242A", Gutter = "#0D0F11", Code = "#14161A",
            HeroHi = "#2B3038", HeroLo = "#0A0B0D", RowOn = "#2C3139", SegOn = "#4C5562",
            ChipOn = "#434A55", ChipLine = "#6C7684",
        },
        new ThemePalette
        {
            Id = "aurora", Name = "Aurora",
            Accent = "#3EDCC8", AccentHi = "#8CF7E9", AccentSoft = "#155B54", AccentDeep = "#0C332F",
            Bg = "#071013", BgDeep = "#04090C", Card = "#0C171A", CardHi = "#101F23",
            Border = "#1E3438", BorderSoft = "#14262A", BorderBlue = "#245C5A", Chip = "#132428",
            Hover = "#112125", Bar = "#102025", TabOn = "#102B2A", Gutter = "#081317", Code = "#0D1B1E",
            HeroHi = "#0F4A45", HeroLo = "#061012", RowOn = "#163A37", SegOn = "#197A6F",
            ChipOn = "#166A60", ChipLine = "#2FAE9F",
        },
        new ThemePalette
        {
            Id = "crimson", Name = "Crimson",
            Accent = "#F25863", AccentHi = "#FF929A", AccentSoft = "#5E2229", AccentDeep = "#331116",
            Bg = "#120A0C", BgDeep = "#0C0507", Card = "#1A0F12", CardHi = "#201317",
            Border = "#3A2227", BorderSoft = "#28171B", BorderBlue = "#6B2E36", Chip = "#25161A",
            Hover = "#241418", Bar = "#221317", TabOn = "#2B161B", Gutter = "#140A0D", Code = "#1D1014",
            HeroHi = "#5C1B24", HeroLo = "#100609", RowOn = "#3A1D23", SegOn = "#AA3541",
            ChipOn = "#98313B", ChipLine = "#D85661",
        },
        new ThemePalette
        {
            Id = "moss", Name = "Moss",
            Accent = "#B7D06A", AccentHi = "#D9EC9E", AccentSoft = "#455A2A", AccentDeep = "#263318",
            Bg = "#0E1108", BgDeep = "#090C05", Card = "#151A0F", CardHi = "#1B2113",
            Border = "#303A22", BorderSoft = "#202817", BorderBlue = "#566B30", Chip = "#1D2414",
            Hover = "#1B2112", Bar = "#191F11", TabOn = "#222B17", Gutter = "#0F1309", Code = "#171D0F",
            HeroHi = "#40551C", HeroLo = "#0B0E06", RowOn = "#2B371B", SegOn = "#6A842F",
            ChipOn = "#5D7529", ChipLine = "#8DAB45",
        },
        new ThemePalette
        {
            Id = "dusk", Name = "Dusk",
            Accent = "#FF9E6B", AccentHi = "#FFC9A3", AccentSoft = "#5E3323", AccentDeep = "#331B14",
            Bg = "#0D0912", BgDeep = "#08050C", Card = "#15101A", CardHi = "#1B1421",
            Border = "#33263A", BorderSoft = "#231A2A", BorderBlue = "#5E3A4A", Chip = "#211723",
            Hover = "#201520", Bar = "#1E141E", TabOn = "#291A25", Gutter = "#0F0A14", Code = "#19111D",
            HeroHi = "#4E2537", HeroLo = "#0B0610", RowOn = "#34202C", SegOn = "#8A4A38",
            ChipOn = "#7C4232", ChipLine = "#B46A50",
        },
    };

    public static ThemePalette Current { get; private set; } = All[0];

    public static event Action<ThemePalette>? Changed;

    public static ThemePalette Find(string? id) =>
        All.FirstOrDefault(theme => string.Equals(theme.Id, id, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    
    public static ThemePalette Apply(string? id)
    {
        var palette = Find(id);
        Current = palette;

        Set("BgBrush", palette.Bg);
        Set("BgDeepBrush", palette.BgDeep);
        Set("CardBrush", palette.Card);
        Set("CardHiBrush", palette.CardHi);
        Set("BorderBrush", palette.Border);
        Set("BorderSoftBrush", palette.BorderSoft);
        Set("BorderBlueBrush", palette.BorderBlue);
        Set("ChipBrush", palette.Chip);
        Set("HoverBrush", palette.Hover);
        Set("BarBrush", palette.Bar);
        Set("TabOnBrush", palette.TabOn);
        Set("GutterBrush", palette.Gutter);
        Set("CodeBrush", palette.Code);
        Set("TrackBrush", palette.Border);
        Set("EdgeBrush", palette.Border);
        Set("PillBrush", palette.CardHi);
        Set("PillBorderBrush", palette.Border);

        Set("AccentBrush", palette.Accent);
        Set("AccentHiBrush", palette.AccentHi);
        Set("AccentSoftBrush", palette.AccentSoft);
        Set("AccentDeepBrush", palette.AccentDeep);
        Set("HeroHiBrush", palette.HeroHi);
        Set("HeroLoBrush", palette.HeroLo);
        Set("RowOnBrush", palette.RowOn);
        Set("SegOnBrush", palette.SegOn);
        Set("ChipOnBrush", palette.ChipOn);
        Set("ChipLineBrush", palette.ChipLine);

        
        var cardColor = Parse(palette.Card);
        SetColor("OverlayCardBrush", Color.FromArgb(0xF2, cardColor.R, cardColor.G, cardColor.B));
        SetColor("OverlayHeaderBrush", Blend(palette.Card, palette.Accent, 0.20));
        SetColor("OverlayBorderBrush", Blend(palette.Border, palette.Accent, 0.40));

        SetGradient("PageGradient", palette.Bg, palette.BgDeep);
        SetGradient("HeroBrush", palette.HeroHi, palette.HeroLo);

        Changed?.Invoke(palette);
        return palette;
    }

    
    public const int TransitionMilliseconds = 420;

    private static readonly IEasingFunction ThemeEase = new CubicEase { EasingMode = EasingMode.EaseOut };

    private static void Set(string key, string hex) => SetColor(key, Parse(hex));

    
    
    
    
    
    private static void SetColor(string key, Color color)
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return;

        if (resources[key] is SolidColorBrush existing)
        {
            if (existing.IsFrozen)
            {
                resources[key] = new SolidColorBrush(color);
                return;
            }

            if (existing.Color == color) return;

            var from = existing.Color;
            existing.Color = color;
            existing.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(from, color,
                TimeSpan.FromMilliseconds(TransitionMilliseconds))
            {
                EasingFunction = ThemeEase,
                FillBehavior = FillBehavior.Stop,
            }, HandoffBehavior.SnapshotAndReplace);
            return;
        }

        resources[key] = new SolidColorBrush(color);
    }

    private static void SetGradient(string key, string from, string to)
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return;

        var start = new Point(0, 0);
        var end = new Point(0, 1);

        if (resources[key] is LinearGradientBrush existing)
        {
            start = existing.StartPoint;
            end = existing.EndPoint;

            
            
            if (!existing.IsFrozen && existing.GradientStops.Count == 2)
            {
                AnimateStop(existing.GradientStops[0], Parse(from));
                AnimateStop(existing.GradientStops[1], Parse(to));
                return;
            }
        }

        var gradient = new LinearGradientBrush { StartPoint = start, EndPoint = end };
        gradient.GradientStops.Add(new GradientStop(Parse(from), 0));
        gradient.GradientStops.Add(new GradientStop(Parse(to), 1));
        resources[key] = gradient;
    }

    private static void AnimateStop(GradientStop stop, Color color)
    {
        if (stop.Color == color) return;

        var from = stop.Color;
        stop.Color = color;
        stop.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(from, color,
            TimeSpan.FromMilliseconds(TransitionMilliseconds))
        {
            EasingFunction = ThemeEase,
            FillBehavior = FillBehavior.Stop,
        }, HandoffBehavior.SnapshotAndReplace);
    }

    public static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;

    public static Color Blend(string from, string to, double amount)
    {
        var a = Parse(from);
        var b = Parse(to);
        amount = Math.Clamp(amount, 0, 1);

        return Color.FromRgb(
            (byte)Math.Round(a.R + ((b.R - a.R) * amount)),
            (byte)Math.Round(a.G + ((b.G - a.G) * amount)),
            (byte)Math.Round(a.B + ((b.B - a.B) * amount)));
    }
}
