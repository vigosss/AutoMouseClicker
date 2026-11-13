using System.Text.Json.Serialization;
using Ming_AutoClicker.Services;

namespace Ming_AutoClicker.Models;

[Flags]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum KeyModifiers : uint
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8
}

public sealed class KeyPressAction : MacroAction
{
    public override ActionType Type => ActionType.KeyPress;
    public uint VirtualKey { get; set; } = 0x0D;
    public KeyModifiers Modifiers { get; set; }

    public override string GetDescription() => LocalizationService.Current.Format(
        "ActionKeyPress", MacroKeyCatalog.Format(VirtualKey, Modifiers));

    public override string ToString() => GetDescription();

    public KeyPressAction Clone() => new()
    {
        Order = Order,
        Note = Note,
        VirtualKey = VirtualKey,
        Modifiers = Modifiers
    };
}
