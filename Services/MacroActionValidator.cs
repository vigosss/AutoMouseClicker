using Ming_AutoClicker.Models;

namespace Ming_AutoClicker.Services;

public static class MacroActionValidator
{
    public const int MaximumScrollNotches = int.MaxValue / 120;
    private const KeyModifiers AllModifiers = KeyModifiers.Control | KeyModifiers.Alt |
                                              KeyModifiers.Shift | KeyModifiers.Windows;

    public static bool TryValidate(MacroAction action, out string error)
    {
        string? errorKey = action switch
        {
            FindImageAction find when !IsValidFindImage(find) => "MacroInvalidFindImage",
            FindColorAction { Operation: ColorOperation.SavePosition } color
                when string.IsNullOrWhiteSpace(color.VariableName) => "MacroMissingPositionName",
            FindColorAction color when !IsValidFindColor(color) => "MacroInvalidFindColor",
            MouseClickAction click when !Enum.IsDefined(click.PositionSource) ||
                (click.PositionSource == ClickPositionSource.Variable && string.IsNullOrWhiteSpace(click.VariableName))
                => "MacroInvalidClickVariable",
            ExecuteMacroAction transfer when string.IsNullOrWhiteSpace(transfer.TargetMacroId) => "MacroSelectTarget",
            ExecuteMacroAction transfer when !Enum.IsDefined(transfer.Mode) => "MacroInvalidTransferMode",
            MouseScrollAction scroll when !Enum.IsDefined(scroll.Direction) => "MacroInvalidScrollDirection",
            MouseScrollAction scroll when !Enum.IsDefined(scroll.PositionMode) => "MacroInvalidScrollPosition",
            MouseScrollAction scroll when scroll.Notches < 1 || scroll.Notches > MaximumScrollNotches => "MacroInvalidScrollNotches",
            KeyPressAction key when !MacroKeyCatalog.IsSupported(key.VirtualKey) => "MacroInvalidKey",
            KeyPressAction key when (key.Modifiers & ~AllModifiers) != 0 => "MacroInvalidModifiers",
            _ => null
        };
        error = errorKey == null ? string.Empty : LocalizationService.Current.GetString(errorKey);
        return errorKey == null;
    }

    private static bool IsValidFindImage(FindImageAction action)
    {
        if (string.IsNullOrWhiteSpace(action.ImagePath) || action.TimeoutMs < 0 ||
            !Enum.IsDefined(action.NotFoundBehavior) || !Enum.IsDefined(action.SelectionMode) || action.MatchIndex < 1 ||
            !IsValidSearchScope(action.SearchScope) ||
            action.AlternateTemplates == null || action.IgnoredRegions == null ||
            !(string.Equals(action.Operation, "Click", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(action.Operation, "RightClick", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(action.Operation, "MoveTo", StringComparison.OrdinalIgnoreCase))) return false;

        foreach (var template in action.GetTemplates())
        {
            if (template == null || string.IsNullOrWhiteSpace(template.ImagePath) ||
                template.IgnoredRegions == null ||
                template.IgnoredRegions.Any(region => !IsValidRelativeRegion(region))) return false;
            if (template.CaptureDpi is double dpi && (!double.IsFinite(dpi) || dpi <= 0)) return false;
            if (template.ClickPoint is { } point &&
                (!IsUnitValue(point.X) || !IsUnitValue(point.Y))) return false;
        }

        return true;
    }

    private static bool IsValidFindColor(FindColorAction action) =>
        FindColorAction.TryParseColor(action.TargetColor, out _) && action.Tolerance is >= 0 and <= 255 &&
        action.TimeoutMs >= 0 && IsValidSearchScope(action.SearchScope) &&
        Enum.IsDefined(action.NotFoundBehavior) && Enum.IsDefined(action.Operation);

    private static bool IsValidSearchScope(ImageSearchScope? scope)
    {
        if (scope == null || !Enum.IsDefined(scope.Kind)) return false;
        if (scope.Kind == ImageSearchScopeKind.Region &&
            (scope.Width <= 0 || scope.Height <= 0 ||
             (long)scope.X + scope.Width > int.MaxValue ||
             (long)scope.Y + scope.Height > int.MaxValue)) return false;
        if (scope.Kind == ImageSearchScopeKind.Window &&
            (string.IsNullOrWhiteSpace(scope.WindowProcessName) ||
             string.IsNullOrWhiteSpace(scope.WindowClassName) ||
             string.IsNullOrWhiteSpace(scope.WindowTitle))) return false;
        return scope.WindowRegion == null || IsValidRelativeRegion(scope.WindowRegion);
    }

    private static bool IsUnitValue(double value) => double.IsFinite(value) && value >= 0 && value <= 1;

    private static bool IsValidRelativeRegion(ImageRelativeRegion? region) => region != null &&
        IsUnitValue(region.X) && IsUnitValue(region.Y) && IsUnitValue(region.Width) &&
        IsUnitValue(region.Height) && region.Width > 0 && region.Height > 0 &&
        region.X + region.Width <= 1.000000001 && region.Y + region.Height <= 1.000000001;
}
