using Ming_AutoClicker.Models;

namespace Ming_AutoClicker.Services;

public sealed record MacroKeyOption(uint VirtualKey, string DisplayName, bool IsExtended = false)
{
    // The application's ComboBox template displays SelectionBoxItem directly.
    public override string ToString() => DisplayName;
}

/// <summary>Keyboard actions use a broader key set than application control hotkeys.</summary>
public static class MacroKeyCatalog
{
    public static IReadOnlyList<MacroKeyOption> Keys { get; } = CreateKeys().AsReadOnly();

    public static bool IsSupported(uint virtualKey) => Keys.Any(key => key.VirtualKey == virtualKey);
    public static string GetDisplayName(uint virtualKey) =>
        Keys.FirstOrDefault(key => key.VirtualKey == virtualKey)?.DisplayName ?? $"0x{virtualKey:X2}";
    public static bool IsExtended(uint virtualKey) =>
        Keys.FirstOrDefault(key => key.VirtualKey == virtualKey)?.IsExtended ?? false;

    public static string Format(uint virtualKey, KeyModifiers modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(KeyModifiers.Windows)) parts.Add("Win");
        parts.Add(GetDisplayName(virtualKey));
        return string.Join("+", parts);
    }

    private static List<MacroKeyOption> CreateKeys()
    {
        var keys = new List<MacroKeyOption>
        {
            new(0x0D, "Enter"), new(0x09, "Tab"), new(0x1B, "Esc"), new(0x20, "Space"),
            new(0x08, "Backspace"), new(0x2E, "Delete", true), new(0x2D, "Insert", true),
            new(0x24, "Home", true), new(0x23, "End", true),
            new(0x21, "Page Up", true), new(0x22, "Page Down", true),
            new(0x25, "←", true), new(0x26, "↑", true),
            new(0x27, "→", true), new(0x28, "↓", true)
        };
        for (uint key = 0x41; key <= 0x5A; key++) keys.Add(new(key, ((char)key).ToString()));
        for (uint key = 0x30; key <= 0x39; key++) keys.Add(new(key, ((char)key).ToString()));
        for (uint number = 1; number <= 24; number++) keys.Add(new(0x6F + number, $"F{number}"));
        for (uint number = 0; number <= 9; number++) keys.Add(new(0x60 + number, $"Num {number}"));
        keys.AddRange(new MacroKeyOption[]
        {
            new(0x6A, "Num *"), new(0x6B, "Num +"), new(0x6D, "Num -"),
            new(0x6E, "Num ."), new(0x6F, "Num /", true)
        });
        return keys;
    }
}
