using Ming_AutoClicker.Models;

namespace Ming_AutoClicker.Services;

/// <summary>
/// A registered application hotkey. Null Target denotes the global start/stop hotkey.
/// The gesture is copied both on entry and on access to keep snapshots immutable.
/// </summary>
public sealed class RegisteredHotkey
{
    private readonly HotkeyGesture _gesture;

    public string? Target { get; }
    public HotkeyGesture Gesture => _gesture.Clone();

    public RegisteredHotkey(string? target, HotkeyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        Target = target;
        _gesture = gesture.Clone();
    }
}

public sealed record MacroHotkeyConflict(int ActionIndex, RegisteredHotkey Hotkey);

/// <summary>
/// Compares complete gestures without registering hotkeys or sending input.
/// </summary>
public static class MacroHotkeyValidator
{
    public static MacroHotkeyConflict? FindConflict(
        MacroProfile profile,
        IReadOnlyList<RegisteredHotkey> registeredHotkeys)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(registeredHotkeys);

        for (var index = 0; index < profile.Actions.Count; index++)
        {
            if (profile.Actions[index] is not KeyPressAction keyPress)
                continue;

            foreach (var hotkey in registeredHotkeys)
            {
                var gesture = hotkey.Gesture;
                if (keyPress.VirtualKey == gesture.VirtualKey &&
                    (uint)keyPress.Modifiers == (uint)gesture.Modifiers)
                    return new MacroHotkeyConflict(index, hotkey);
            }
        }

        return null;
    }
}
