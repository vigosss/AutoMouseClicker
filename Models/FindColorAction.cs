using System.Buffers;
using System.Drawing;
using System.Text.Json.Serialization;
using Ming_AutoClicker.Services;

namespace Ming_AutoClicker.Models;

[JsonConverter(typeof(JsonStringEnumConverter<ColorOperation>))]
public enum ColorOperation { Click, RightClick, MoveTo, SavePosition }

public sealed class FindColorAction : MacroAction
{
    public override ActionType Type => ActionType.FindColor;
    public string TargetColor { get; set; } = "#4EB86F";
    public int Tolerance { get; set; } = 5;
    public ImageSearchScope SearchScope { get; set; } = new();
    public int TimeoutMs { get; set; } = 30_000;
    public ImageNotFoundBehavior NotFoundBehavior { get; set; } = ImageNotFoundBehavior.Stop;
    public ColorOperation Operation { get; set; } = ColorOperation.Click;
    public string VariableName { get; set; } = string.Empty;

    public static bool TryParseColor(string? value, out Color color)
    {
        color = Color.Empty;
        Span<byte> rgb = stackalloc byte[3];
        if (value is not { Length: 7 } || value[0] != '#' ||
            Convert.FromHexString(value.AsSpan(1), rgb, out _, out _) != OperationStatus.Done)
            return false;
        color = Color.FromArgb(rgb[0], rgb[1], rgb[2]);
        return true;
    }

    public override string GetDescription()
    {
        var strings = LocalizationService.Current;
        var operation = Operation switch
        {
            ColorOperation.Click => strings.GetString("EditorLeftClick"),
            ColorOperation.RightClick => strings.GetString("EditorRightClick"),
            ColorOperation.MoveTo => strings.GetString("EditorMoveTo"),
            ColorOperation.SavePosition => $"{strings.GetString("EditorSavePosition")} {VariableName}",
            _ => Operation.ToString()
        };
        return strings.Format("ActionFindColor", TargetColor, operation);
    }

    public override string ToString() => GetDescription();

    public override FindColorAction Clone() => new()
    {
        Order = Order,
        Note = Note,
        TargetColor = TargetColor,
        Tolerance = Tolerance,
        SearchScope = SearchScope.Clone(),
        TimeoutMs = TimeoutMs,
        NotFoundBehavior = NotFoundBehavior,
        Operation = Operation,
        VariableName = VariableName
    };
}
