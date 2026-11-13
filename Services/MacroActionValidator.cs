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
            !Enum.IsDefined(action.SelectionMode) || action.MatchIndex < 1 ||
            action.SearchScope == null || !Enum.IsDefined(action.SearchScope.Kind) ||
            action.AlternateTemplates == null || action.IgnoredRegions == null ||
            !(string.Equals(action.Operation, "Click", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(action.Operation, "RightClick", StringComparison.OrdinalIgnoreCase))) return false;

        foreach (var template in action.GetTemplates())
        {
            if (template == null || string.IsNullOrWhiteSpace(template.ImagePath) ||
                template.IgnoredRegions == null ||
                template.IgnoredRegions.Any(region => !IsValidRelativeRegion(region))) return false;
            if (template.CaptureDpi is double dpi && (!double.IsFinite(dpi) || dpi <= 0)) return false;
            if (template.ClickPoint is { } point &&
                (!IsUnitValue(point.X) || !IsUnitValue(point.Y))) return false;
        }

        var scope = action.SearchScope;
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
