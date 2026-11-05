using System.Windows.Input;

namespace AetherLauncher.Services;





public static class Shortcuts
{
    
    
    
    
    
    public const string Default = "Delete";

    
    private static readonly HashSet<Key> NotUsableAlone = new()
    {
        Key.None, Key.System, Key.LeftShift, Key.RightShift, Key.LeftCtrl, Key.RightCtrl,
        Key.LeftAlt, Key.RightAlt, Key.LWin, Key.RWin,
    };

    
    public static string? Format(ModifierKeys modifiers, Key key)
    {
        if (!IsUsable(key)) return null;

        var parsed = Normalize(modifiers);
        var parts = new List<string>();
        if ((parsed & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((parsed & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((parsed & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((parsed & ModifierKeys.Windows) != 0) parts.Add("Win");

        
        
        
        if (parts.Count == 0 && !UsableAlone(key)) return null;

        parts.Add(Name(key));
        return string.Join("+", parts);
    }

    
    public static bool UsableAlone(Key key) => key is >= Key.F1 and <= Key.F24 || key is
        Key.Delete or Key.Insert or Key.Home or Key.End or Key.PageUp or Key.PageDown or
        Key.Pause or Key.Scroll or Key.PrintScreen or Key.CapsLock or Key.NumLock;

    
    public static bool TryParseKey(string? text, out Key key)
    {
        key = Key.None;
        if (string.IsNullOrWhiteSpace(text)) return false;

        key = ParseKey(text.Trim());
        return key != Key.None;
    }

    
    
    
    
    public static IEnumerable<Key> CaptureCandidates(bool includeModifiers = false)
    {
        for (var key = Key.A; key <= Key.Z; key++) yield return key;
        for (var key = Key.D0; key <= Key.D9; key++) yield return key;
        for (var key = Key.F1; key <= Key.F24; key++) yield return key;

        foreach (var key in new[]
                 {
                     Key.Tab, Key.Space, Key.Back, Key.Enter, Key.Insert, Key.Delete, Key.Home, Key.End,
                     Key.PageUp, Key.PageDown, Key.Left, Key.Right, Key.Up, Key.Down, Key.OemTilde,
                     Key.OemMinus, Key.OemPlus, Key.OemOpenBrackets, Key.OemCloseBrackets, Key.OemSemicolon,
                     Key.OemQuotes, Key.OemComma, Key.OemPeriod, Key.OemQuestion, Key.OemBackslash,
                     Key.Pause, Key.Scroll, Key.PrintScreen, Key.CapsLock, Key.NumLock,
                 })
            yield return key;

        if (!includeModifiers) yield break;

        foreach (var key in new[] { Key.LeftShift, Key.RightShift, Key.LeftCtrl, Key.RightCtrl, Key.LeftAlt, Key.RightAlt })
            yield return key;
    }

    
    public static bool IsModifierKey(Key key) => NotUsableAlone.Contains(key) ||
        key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl or
            Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System;

    public static bool TryParse(string? text, out ModifierKeys modifiers, out Key key)
    {
        modifiers = ModifierKeys.None;
        key = Key.None;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "alt":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "shift":
                    modifiers |= ModifierKeys.Shift;
                    break;
                case "win" or "windows":
                    modifiers |= ModifierKeys.Windows;
                    break;
                default:
                    if (key != Key.None) return false;
                    key = ParseKey(part);
                    break;
            }
        }

        return IsUsable(key);
    }

    
    public static bool Matches(string? shortcut, ModifierKeys modifiers, Key key)
    {
        if (!TryParse(shortcut, out var expectedModifiers, out var expectedKey)) return false;
        return expectedKey == key && expectedModifiers == Normalize(modifiers);
    }

    
    public static string Name(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num" + (key - Key.NumPad0),
        _ => key.ToString(),
    };

    private static Key ParseKey(string text)
    {
        if (text.Length == 1 && char.IsDigit(text[0])) return Key.D0 + (text[0] - '0');
        if (text.Length == 1 && char.IsLetter(text[0]))
            return Enum.TryParse<Key>(char.ToUpperInvariant(text[0]).ToString(), out var letter) ? letter : Key.None;

        return Enum.TryParse<Key>(text, ignoreCase: true, out var key) ? key : Key.None;
    }

    private static bool IsUsable(Key key) => key != Key.None && !NotUsableAlone.Contains(key);

    private static ModifierKeys Normalize(ModifierKeys modifiers)
        => modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows);
}
