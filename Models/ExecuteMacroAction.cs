using System.Text.Json.Serialization;
using Ming_AutoClicker.Services;

namespace Ming_AutoClicker.Models;

[JsonConverter(typeof(JsonStringEnumConverter<MacroTransferMode>))]
public enum MacroTransferMode
{
    Call,
    Jump
}

public sealed class ExecuteMacroAction : MacroAction
{
    public override ActionType Type => ActionType.ExecuteMacro;
    public string TargetMacroId { get; set; } = string.Empty;
    public MacroTransferMode Mode { get; set; } = MacroTransferMode.Call;

    [JsonIgnore]
    public string? TargetMacroName { get; set; }

    public override string GetDescription()
    {
        var strings = LocalizationService.Current;
        var target = TargetMacroName ?? (string.IsNullOrWhiteSpace(TargetMacroId)
            ? strings.GetString("MacroTargetUnselected")
            : strings.Format("MacroTargetUnavailable", TargetMacroId));
        return strings.Format(Mode == MacroTransferMode.Jump ? "ActionMacroJump" : "ActionMacroCall", target);
    }

    public override string ToString() => GetDescription();

    public override ExecuteMacroAction Clone() => new()
    {
        Order = Order,
        Note = Note,
        TargetMacroId = TargetMacroId,
        TargetMacroName = TargetMacroName,
        Mode = Mode
    };
}
