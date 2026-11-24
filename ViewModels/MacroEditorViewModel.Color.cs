using System.Drawing;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Ming_AutoClicker.Helpers;
using Ming_AutoClicker.Models;
using Ming_AutoClicker.Services;
using Ming_AutoClicker.Views.ImageMatching;
using Ming_AutoClicker.Views.Shared;

namespace Ming_AutoClicker.ViewModels;

public partial class MacroEditorViewModel
{
    private readonly Dictionary<FindColorAction, ColorFields> _colorFields = new();

    public ICommand AddFindColorActionCommand { get; }
    public ICommand PickColorCommand { get; }
    public ICommand TestColorCommand { get; }
    public FindColorAction? FindColorAction => SelectedAction as FindColorAction;
    private ImageSearchScope? SelectedSearchScope => FindColorAction?.SearchScope ?? FindImageAction?.SearchScope;

    public ImageSearchScopeKind? ColorScopeKind
    {
        get
        {
            var kind = FindColorAction?.SearchScope.Kind;
            return kind is ImageSearchScopeKind.Desktop or ImageSearchScopeKind.Region ? kind : null;
        }
        set
        {
            // A loaded window scope has no corresponding item in the color scope selector.
            if (FindColorAction is not { } action || value is not (ImageSearchScopeKind.Desktop or ImageSearchScopeKind.Region)
                || action.SearchScope.Kind == value) return;
            ReplaceSearchScope(action, new ImageSearchScope { Kind = value.Value });
        }
    }

    public string LegacyColorScopeDescription => FindColorAction?.SearchScope.Kind == ImageSearchScopeKind.Window
        ? LocalizationService.Current.Format("ColorLegacyWindowScope", ImageScopeDescription) : string.Empty;

    private void AddFindColorAction()
    {
        var action = new FindColorAction { Order = Actions.Count };
        Actions.Add(action);
        SelectedAction = action;
        StatusMessage = LocalizationService.Current.GetString("EditorAddedFindColor");
    }

    public string ColorTargetText
    {
        get => FindColorAction?.TargetColor ?? string.Empty;
        set
        {
            if (FindColorAction is not { } action || action.TargetColor == value) return;
            action.TargetColor = value;
            RefreshEditedAction(nameof(ColorTargetText));
            OnPropertyChanged(nameof(ColorSwatch));
        }
    }

    public System.Windows.Media.Brush ColorSwatch
    {
        get
        {
            if (!Models.FindColorAction.TryParseColor(ColorTargetText, out var rgb))
                return System.Windows.Media.Brushes.Transparent;
            return new SolidColorBrush(System.Windows.Media.Color.FromRgb(rgb.R, rgb.G, rgb.B));
        }
    }

    public string ColorToleranceText
    {
        get => FindColorAction is { } action ? GetColorFields(action).Tolerance : string.Empty;
        set
        {
            if (FindColorAction is not { } action) return;
            GetColorFields(action).Tolerance = value;
            if (int.TryParse(value, out var tolerance) && tolerance is >= 0 and <= 255)
                action.Tolerance = tolerance;
            RefreshEditedAction(nameof(ColorToleranceText));
        }
    }

    public string ColorTimeoutSecondsText
    {
        get => FindColorAction is { } action ? GetColorFields(action).Timeout : string.Empty;
        set
        {
            if (FindColorAction is not { } action) return;
            GetColorFields(action).Timeout = value;
            if (TryParseImageTimeout(value, out var milliseconds)) action.TimeoutMs = milliseconds;
            RefreshEditedAction(nameof(ColorTimeoutSecondsText));
        }
    }

    public sealed record ColorOperationChoice(ColorOperation Value, string Label)
    {
        public override string ToString() => Label;
    }

    public IReadOnlyList<ColorOperationChoice> ColorOperationChoices
    {
        get
        {
            var strings = LocalizationService.Current;
            var choices = new List<ColorOperationChoice>
            {
                new(ColorOperation.Click, strings.GetString("EditorLeftClick")),
                new(ColorOperation.RightClick, strings.GetString("EditorRightClick")),
                new(ColorOperation.MoveTo, strings.GetString("EditorMoveTo"))
            };
            if (SavesColorPosition)
                choices.Add(new(ColorOperation.SavePosition, strings.GetString("EditorSavePosition")));
            return choices;
        }
    }

    public ColorOperation? ColorOperationValue
    {
        get => FindColorAction?.Operation;
        set
        {
            // Refreshing the options can briefly clear the selection. Only the three
            // mouse operations can replace a saved step's existing operation.
            if (FindColorAction is not { } action || action.Operation == value ||
                value is not (ColorOperation.Click or ColorOperation.RightClick or ColorOperation.MoveTo)) return;
            bool hadSavedPosition = SavesColorPosition;
            action.Operation = value.Value;
            if (hadSavedPosition) OnPropertyChanged(nameof(ColorOperationChoices));
            RefreshEditedAction(nameof(ColorOperationValue));
            OnPropertyChanged(nameof(SavesColorPosition));
        }
    }

    public bool SavesColorPosition => FindColorAction?.Operation == ColorOperation.SavePosition;
    public string ColorVariableName
    {
        get => FindColorAction?.VariableName ?? string.Empty;
        set
        {
            if (FindColorAction is not { Operation: ColorOperation.SavePosition } action || action.VariableName == value) return;
            action.VariableName = value;
            RefreshEditedAction(nameof(ColorVariableName));
        }
    }

    public ClickPositionSource ClickPositionSourceValue
    {
        get => MouseClickAction?.PositionSource ?? ClickPositionSource.Fixed;
        set
        {
            if (MouseClickAction is not { } click || click.PositionSource == value) return;
            click.PositionSource = value;
            if (value == ClickPositionSource.Variable && string.IsNullOrWhiteSpace(click.VariableName))
                click.VariableName = AvailablePositionVariables.FirstOrDefault() ?? string.Empty;
            RefreshEditedAction(nameof(ClickPositionSourceValue));
            OnPropertyChanged(nameof(UsesFixedClickPosition));
            OnPropertyChanged(nameof(UsesVariableClickPosition));
            OnPropertyChanged(nameof(ClickVariableName));
            UpdateActionCommands();
        }
    }

    public bool UsesFixedClickPosition => MouseClickAction?.PositionSource == ClickPositionSource.Fixed;
    public bool UsesVariableClickPosition => MouseClickAction?.PositionSource == ClickPositionSource.Variable;

    private IEnumerable<string> EarlierPositionNames => Actions.TakeWhile(action => action != MouseClickAction)
        .OfType<FindColorAction>().Where(action => action.Operation == ColorOperation.SavePosition)
        .Select(action => action.VariableName).Where(name => !string.IsNullOrWhiteSpace(name));
    public bool NeedsClickVariableHint => MouseClickAction != null && !EarlierPositionNames.Any();
    public IEnumerable<string> AvailablePositionVariables
    {
        get
        {
            if (MouseClickAction is not { } click) return Array.Empty<string>();
            return EarlierPositionNames.Append(click.VariableName).Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim()).Distinct(StringComparer.Ordinal).ToArray();
        }
    }

    public string ClickVariableName
    {
        get => MouseClickAction?.VariableName?.Trim() ?? string.Empty;
        set
        {
            if (string.IsNullOrEmpty(value) || MouseClickAction is not { } click || click.VariableName == value) return;
            click.VariableName = value;
            RefreshEditedAction(nameof(ClickVariableName));
        }
    }

    private void RefreshColorProperties()
    {
        foreach (var name in new[] { nameof(ColorTargetText), nameof(ColorSwatch), nameof(ColorToleranceText),
            nameof(ColorTimeoutSecondsText), nameof(ColorOperationChoices), nameof(ColorOperationValue), nameof(SavesColorPosition), nameof(ColorVariableName),
            nameof(ColorRegionInputs), nameof(ClickPositionSourceValue), nameof(UsesFixedClickPosition),
            nameof(UsesVariableClickPosition), nameof(AvailablePositionVariables), nameof(NeedsClickVariableHint), nameof(ClickVariableName) })
            OnPropertyChanged(name);
    }

    private void ReplaceSearchScope(MacroAction action, ImageSearchScope scope)
    {
        if (action is FindImageAction image) image.SearchScope = scope;
        else if (action is FindColorAction color)
        {
            color.SearchScope = scope;
            GetColorFields(color).Region = new ColorRegionFields(scope);
            OnPropertyChanged(nameof(ColorRegionInputs));
        }
        else return;
        RefreshEditedAction(nameof(ImageScopeKind));
        RefreshImageProperties();
    }

    private ColorFields GetColorFields(FindColorAction action)
    {
        if (!_colorFields.TryGetValue(action, out var fields))
            _colorFields[action] = fields = new ColorFields(action);
        return fields;
    }

    private sealed class ColorFields(FindColorAction action)
    {
        public string Timeout { get; set; } = (action.TimeoutMs / 1000d).ToString();
        public string Tolerance { get; set; } = action.Tolerance.ToString();
        public ColorRegionFields Region { get; set; } = new(action.SearchScope);
    }

    // Keep all four text drafts until the user has supplied a complete rectangle.
    public ColorRegionFields? ColorRegionInputs => FindColorAction is { } action ? GetColorFields(action).Region : null;
    public sealed class ColorRegionFields(ImageSearchScope scope)
    {
        public string X1 { get; set; } = scope.Width > 0 ? scope.X.ToString() : "";
        public string Y1 { get; set; } = scope.Height > 0 ? scope.Y.ToString() : "";
        public string X2 { get; set; } = scope.Width > 0 ? ((long)scope.X + scope.Width - 1).ToString() : "";
        public string Y2 { get; set; } = scope.Height > 0 ? ((long)scope.Y + scope.Height - 1).ToString() : "";

        internal bool TryGetBounds(out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            if (!int.TryParse(X1, out var x1) || !int.TryParse(Y1, out var y1) ||
                !int.TryParse(X2, out var x2) || !int.TryParse(Y2, out var y2)) return false;
            long width = (long)x2 - x1 + 1, height = (long)y2 - y1 + 1;
            if (width is <= 0 or > int.MaxValue || height is <= 0 or > int.MaxValue ||
                x2 == int.MaxValue || y2 == int.MaxValue) return false;
            bounds = new Rectangle(x1, y1, (int)width, (int)height);
            return true;
        }
    }

    private bool TryValidateColorFields(FindColorAction action, out string error)
    {
        error = string.Empty;
        if (!_colorFields.TryGetValue(action, out var fields)) return true;
        if (!int.TryParse(fields.Tolerance, out var tolerance) || tolerance is < 0 or > 255)
            error = LocalizationService.Current.GetString("ColorInvalidTolerance");
        else if (!TryParseImageTimeout(fields.Timeout, out _))
            error = LocalizationService.Current.GetString("ImageInvalidTimeout");
        else if (action.SearchScope.Kind == ImageSearchScopeKind.Region)
        {
            if (fields.Region.TryGetBounds(out var bounds))
            {
                var scope = action.SearchScope;
                (scope.X, scope.Y, scope.Width, scope.Height) = (bounds.X, bounds.Y, bounds.Width, bounds.Height);
            }
            else error = LocalizationService.Current.GetString("ColorInvalidRegion");
        }
        return error.Length == 0;
    }

    private async Task PickColorAsync()
    {
        if (_isCapturingScreenshot || FindColorAction is not { } action) return;
        _isCapturingScreenshot = true;
        UpdateActionCommands();
        try
        {
            using var hidden = CaptureWindowVisibilityScope.HideVisibleWindows();
            await Task.Delay(200);
            var picker = new CoordinatePickWindow(pickColor: true);
            if (picker.ShowDialog() != true || picker.SelectedColorHex is not { } hex) return;
            action.TargetColor = hex;
            RefreshEditedAction(nameof(ColorTargetText));
            OnPropertyChanged(nameof(ColorSwatch));
        }
        catch (Exception ex) { StatusMessage = LocalizationService.Current.Format("EditorCaptureFailed", ex.Message); }
        finally
        {
            _isCapturingScreenshot = false;
            UpdateActionCommands();
        }
    }

    private async Task TestColorAsync()
    {
        if (_isTestingMatch || FindColorAction is not { } action) return;
        if (!TryValidateColorFields(action, out var error) || !MacroActionValidator.TryValidate(action, out error))
        {
            StatusMessage = error;
            return;
        }
        var testAction = action.Clone();
        using var cancellation = new CancellationTokenSource();
        _matchTestCancellation = cancellation;
        _isTestingMatch = true;
        OnPropertyChanged(nameof(IsTestingMatch));
        UpdateActionCommands();
        StatusMessage = LocalizationService.Current.GetString("EditorTestingMatch");
        try
        {
            using var hidden = CaptureWindowVisibilityScope.HideVisibleWindows();
            await Task.Delay(200, cancellation.Token);
            var search = new ColorSearchService(new DesktopImageFrameSource(_screenCaptureService));
            var result = await Task.Run(() => search.FindColor(testAction, cancellation.Token, includeSnapshot: true), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            StatusMessage = result.Found ? LocalizationService.Current.Format("ColorTestFound", result.ClickX, result.ClickY)
                : result.IsOrdinaryMiss ? LocalizationService.Current.GetString("ColorTestMiss") : result.ErrorMessage;
            var preview = new MatchResultWindow(result, colorSearch: true);
            using var closeOnCancel = cancellation.Token.Register(() => preview.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (preview.IsVisible) preview.Close();
            })));
            preview.ShowDialog();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) { StatusMessage = LocalizationService.Current.Format("EditorTestFailed", ex.Message); }
        finally
        {
            _matchTestCancellation = null;
            _isTestingMatch = false;
            OnPropertyChanged(nameof(IsTestingMatch));
            UpdateActionCommands();
        }
    }
}
