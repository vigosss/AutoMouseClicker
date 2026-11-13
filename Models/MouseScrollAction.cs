using System.Text.Json.Serialization;
using Ming_AutoClicker.Services;

namespace Ming_AutoClicker.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScrollDirection
{
    Up,
    Down,
    Left,
    Right
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScrollPositionMode
{
    CurrentCursor,
    Coordinates
}

public sealed class MouseScrollAction : MacroAction
{
    public override ActionType Type => ActionType.MouseScroll;
    public ScrollDirection Direction { get; set; } = ScrollDirection.Down;
    public int Notches { get; set; } = 1;
    public ScrollPositionMode PositionMode { get; set; } = ScrollPositionMode.CurrentCursor;
    public int X { get; set; }
    public int Y { get; set; }

    public override string GetDescription()
    {
        var direction = LocalizationService.Current.GetString($"EditorScroll{Direction}");
        return PositionMode == ScrollPositionMode.Coordinates
            ? LocalizationService.Current.Format("ActionScrollCoordinates", direction, Notches, X, Y)
            : LocalizationService.Current.Format("ActionScrollCurrent", direction, Notches);
    }

    public override string ToString() => GetDescription();

    public MouseScrollAction Clone() => new()
    {
        Order = Order,
        Note = Note,
        Direction = Direction,
        Notches = Notches,
        PositionMode = PositionMode,
        X = X,
        Y = Y
    };
}
