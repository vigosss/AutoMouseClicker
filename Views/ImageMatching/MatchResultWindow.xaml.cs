using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.IO.Path;
using Ming_AutoClicker.Helpers;
using Ming_AutoClicker.Services;

namespace Ming_AutoClicker.Views.ImageMatching;

public enum MatchResultHelpAction { SearchScope, AlternateTemplate, Recapture }

/// <summary>Read-only visualization of the exact captured frame used by the matcher.</summary>
public partial class MatchResultWindow : Window
{
    private MatchResult _matchResult = new();
    private string? _additionalInfo;
    private readonly System.Drawing.Rectangle _desktopBounds;
    private bool _positioningInfoPanel;
    private bool _isPending = true;
    private bool _cancellationRaised;
    public MatchResultHelpAction? RequestedHelpAction { get; private set; }
    public bool IsClosed { get; private set; }
    public bool IsMatching => _isPending && !IsClosed;
    public event EventHandler? CancellationRequested;

    public MatchResultWindow()
    {
        InitializeComponent();
        var (x, y, width, height) = Win32Api.GetVirtualScreenBounds();
        _desktopBounds = new(x, y, width, height);
        SourceInitialized += (_, _) => Win32Api.FitWindowToVirtualScreen(this);
        Loaded += (_, _) => { Focus(); UpdateView(); };
        SizeChanged += (_, _) => UpdateView();
        InfoPanel.SizeChanged += (_, _) => PositionInfoPanel();
        FooterPanel.SizeChanged += (_, _) => PositionInfoPanel();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        UpdateView();
    }

    public MatchResultWindow(MatchResult matchResult, string? additionalInfo = null) : this()
    {
        ShowResult(matchResult, additionalInfo);
    }

    /// <summary>Sets the captured background before showing the pending window. Does not capture or show a window.</summary>
    public void ShowCapturedFrame(BitmapSource snapshot, System.Drawing.Rectangle bounds)
    {
        Dispatcher.VerifyAccess();
        if (IsClosed || !_isPending) return;
        ArgumentNullException.ThrowIfNull(snapshot);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(bounds));
        _matchResult = new MatchResult { Snapshot = snapshot, CaptureBounds = bounds };
        UpdateView();
    }

    /// <summary>Completes the pending presentation in the same window; late results never reopen a closed preview.</summary>
    public void ShowResult(MatchResult result, string? additionalInfo = null)
    {
        Dispatcher.VerifyAccess();
        if (IsClosed) return;
        _matchResult = result ?? throw new ArgumentNullException(nameof(result));
        _additionalInfo = additionalInfo;
        _isPending = false;
        UpdateView();
    }

    private void UpdateView()
    {
        if (IsClosed) return;
        // Set content before layout or Show(), so a pending preview never flashes a failed result.
        UpdateStatus();
        BackgroundImage.Source = _matchResult.Snapshot;
        if (Viewport.ActualWidth <= 0 || Viewport.ActualHeight <= 0) return;
        // Keep the exit hint on the primary display; the result panel follows the matched target.
        var primary = Win32Api.GetMainScreenSize();
        var displayScaleX = Viewport.ActualWidth / _desktopBounds.Width;
        var displayScaleY = Viewport.ActualHeight / _desktopBounds.Height;
        var primaryLeft = -_desktopBounds.X * displayScaleX;
        var primaryTop = -_desktopBounds.Y * displayScaleY;
        var primaryHeight = primary.Height * displayScaleY;
        FooterPanel.Margin = new Thickness(primaryLeft + 24, primaryTop + Math.Max(24, primaryHeight - 64), 0, 0);
        DrawFrameAndCandidates();
        PositionInfoPanel();
    }

    private void UpdateStatus()
    {
        var loc = LocalizationService.Current;
        var brush = (Brush)FindResource(_isPending ? "PrimaryBrush" : _matchResult.Found ? "SuccessBrush" : "WarningBrush");
        InfoPanel.BorderBrush = brush;
        StatusTitle.Foreground = brush;
        StopTestButton.Visibility = _isPending ? Visibility.Visible : Visibility.Collapsed;
        StatusDescription.Visibility = _isPending ? Visibility.Collapsed : Visibility.Visible;
        ResultDetails.Visibility = _isPending ? Visibility.Collapsed : Visibility.Visible;
        if (_isPending)
        {
            StatusTitle.Text = loc.GetString("EditorTestingMatch");
            StatusDescription.Text = string.Empty;
            HelpActions.Visibility = Visibility.Collapsed;
            return;
        }
        StatusTitle.Text = _matchResult.Found ? loc.GetString("MatchSuccess") :
            loc.GetMatchFailureText(_matchResult.FailureReason);
        StatusDescription.Text = !string.IsNullOrWhiteSpace(_additionalInfo) ? _additionalInfo :
            _matchResult.Found ? loc.Format("MatchPreviewFound", _matchResult.ClickX, _matchResult.ClickY) :
            !string.IsNullOrWhiteSpace(_matchResult.ErrorMessage) ? _matchResult.ErrorMessage :
            _matchResult.FailureReason == MatchFailureReason.Ambiguous ?
                loc.Format("MatchPreviewAmbiguous", _matchResult.Candidates.Count) :
                loc.GetMatchFailureText(_matchResult.FailureReason);
        if (_matchResult.Snapshot == null)
            StatusDescription.Text += "\n" + loc.GetString("MatchPreviewNoSnapshot");

        HelpActions.Visibility = _matchResult.Found ? Visibility.Collapsed : Visibility.Visible;
        ScopeHelpButton.Visibility = _matchResult.FailureReason is MatchFailureReason.Ambiguous or
            MatchFailureReason.ScopeUnavailable ? Visibility.Visible : Visibility.Collapsed;
        AlternateHelpButton.Visibility = _matchResult.FailureReason is MatchFailureReason.BelowThreshold or
            MatchFailureReason.TimedOut or MatchFailureReason.TargetChanged ? Visibility.Visible : Visibility.Collapsed;
        RecaptureHelpButton.Visibility = _matchResult.FailureReason is MatchFailureReason.Cancelled ?
            Visibility.Collapsed : Visibility.Visible;

        TxtPosition.Text = _matchResult.HasCandidate ? $"({_matchResult.ClickX}, {_matchResult.ClickY})" : "—";
        TxtSimilarity.Text = $"{_matchResult.Similarity:F3}";
        TxtRegion.Text = _matchResult.HasCandidate ? $"{_matchResult.Width} × {_matchResult.Height}" : "—";
        TxtThreshold.Text = $"{_matchResult.Threshold:F3}";
        TxtScale.Text = $"{_matchResult.Scale:P1}";
        TxtMethod.Text = _matchResult.MatchMethod;
        TxtElapsed.Text = $"{_matchResult.ElapsedMilliseconds} ms";
        TxtSecondBest.Text = $"{_matchResult.SecondBestSimilarity:F3}";
        TxtTemplate.Text = Path.GetFileName(_matchResult.TemplatePath);
    }

    private void PositionInfoPanel()
    {
        if (IsClosed || _positioningInfoPanel || Viewport.ActualWidth <= 0 || Viewport.ActualHeight <= 0) return;
        _positioningInfoPanel = true;
        try
        {
            var target = _matchResult.GetRectangle();
            var monitor = _matchResult.HasCandidate
                ? System.Windows.Forms.Screen.FromRectangle(target)
                : System.Windows.Forms.Screen.PrimaryScreen!;
            var workArea = monitor.WorkingArea;
            var sx = Viewport.ActualWidth / _desktopBounds.Width;
            var sy = Viewport.ActualHeight / _desktopBounds.Height;
            const double inset = 16;
            const double gap = 12;
            var left = (workArea.Left - _desktopBounds.X) * sx + inset;
            var top = (workArea.Top - _desktopBounds.Y) * sy + inset;
            var width = Math.Max(1, workArea.Width * sx - 2 * inset);
            var height = Math.Max(1, workArea.Height * sy - 2 * inset);
            var right = left + width;
            var bottom = top + height;

            // Canvas positioning leaves measurement independent of the panel's screen location.
            InfoPanel.MaxWidth = Math.Min(460, width);
            InfoPanel.MaxHeight = height;
            InfoPanel.Measure(new Size(InfoPanel.MaxWidth, InfoPanel.MaxHeight));
            var x = right - InfoPanel.DesiredSize.Width;
            var y = top;
            if (_matchResult.HasCandidate)
            {
                var targetLeft = (target.Left - _desktopBounds.X) * sx;
                var targetTop = (target.Top - _desktopBounds.Y) * sy;
                var targetBottom = (target.Bottom - _desktopBounds.Y) * sy;
                var belowStart = Math.Clamp(targetBottom + gap, top, bottom);
                var aboveEnd = Math.Clamp(targetTop - gap, top, bottom);
                var belowSpace = bottom - belowStart;
                var aboveSpace = aboveEnd - top;
                var showBelow = InfoPanel.DesiredSize.Height <= belowSpace ||
                    (InfoPanel.DesiredSize.Height > aboveSpace && belowSpace >= aboveSpace);
                var availableHeight = showBelow ? belowSpace : aboveSpace;

                // Long details scroll in the available space instead of covering the target.
                // A target filling the screen may leave neither side with a usable panel height.
                if (availableHeight >= Math.Min(120, height))
                {
                    InfoPanel.MaxHeight = availableHeight;
                    InfoPanel.Measure(new Size(InfoPanel.MaxWidth, InfoPanel.MaxHeight));
                }
                x = targetLeft;
                y = showBelow ? belowStart : aboveEnd - InfoPanel.DesiredSize.Height;
            }
            x = Math.Clamp(x, left, Math.Max(left, right - InfoPanel.DesiredSize.Width));
            y = Math.Clamp(y, top, Math.Max(top, bottom - InfoPanel.DesiredSize.Height));
            Canvas.SetLeft(InfoPanel, x);
            Canvas.SetTop(InfoPanel, y);
            var panelBounds = new Rect(new Point(x, y), InfoPanel.DesiredSize);
            var footerBounds = new Rect(FooterPanel.Margin.Left, FooterPanel.Margin.Top,
                FooterPanel.ActualWidth, FooterPanel.ActualHeight);
            FooterPanel.Visibility = panelBounds.IntersectsWith(footerBounds) ? Visibility.Hidden : Visibility.Visible;
        }
        finally
        {
            _positioningInfoPanel = false;
        }
    }

    private void DrawFrameAndCandidates()
    {
        CandidateCanvas.Children.Clear();
        BackgroundImage.Source = _matchResult.Snapshot;
        if (_matchResult.Snapshot == null) return;
        var bounds = _matchResult.CaptureBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var sx = Viewport.ActualWidth / _desktopBounds.Width;
        var sy = Viewport.ActualHeight / _desktopBounds.Height;
        Canvas.SetLeft(BackgroundImage, (bounds.X - _desktopBounds.X) * sx);
        Canvas.SetTop(BackgroundImage, (bounds.Y - _desktopBounds.Y) * sy);
        BackgroundImage.Width = bounds.Width * sx;
        BackgroundImage.Height = bounds.Height * sy;
        if (_isPending) return;

        var candidates = _matchResult.Candidates.OrderBy(c => c.Y).ThenBy(c => c.X).ToList();
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            var isSelected = _matchResult.Found && candidate.X == _matchResult.X && candidate.Y == _matchResult.Y &&
                candidate.Width == _matchResult.Width && candidate.Height == _matchResult.Height;
            DrawCandidate(candidate.X, candidate.Y, candidate.Width, candidate.Height,
                candidate.ClickX, candidate.ClickY, i + 1, isSelected, sx, sy);
        }
        if (candidates.Count == 0 && _matchResult.HasCandidate)
            DrawCandidate(_matchResult.X, _matchResult.Y, _matchResult.Width, _matchResult.Height,
                _matchResult.ClickX, _matchResult.ClickY, 0, _matchResult.Found, sx, sy);
    }

    private void DrawCandidate(int centerX, int centerY, int width, int height,
        int clickX, int clickY, int number, bool selected, double sx, double sy)
    {
        var brush = selected ? Brushes.SpringGreen : Brushes.Gold;
        var left = (centerX - width / 2 - _desktopBounds.X) * sx;
        var top = (centerY - height / 2 - _desktopBounds.Y) * sy;
        var border = new Rectangle
        {
            Width = width * sx, Height = height * sy, Stroke = brush, StrokeThickness = selected ? 3 : 2,
            Fill = new SolidColorBrush(Color.FromArgb(24, selected ? (byte)80 : (byte)255, 209, 102))
        };
        if (!selected) border.StrokeDashArray = new DoubleCollection { 4, 3 };
        Canvas.SetLeft(border, left);
        Canvas.SetTop(border, top);
        CandidateCanvas.Children.Add(border);

        if (number > 0)
        {
            var label = new TextBlock
            {
                Text = number.ToString(), Foreground = Brushes.Black, Background = brush,
                Padding = new Thickness(4, 2, 4, 2), FontSize = 12
            };
            Canvas.SetLeft(label, left);
            Canvas.SetTop(label, Math.Max(0, top - 20));
            CandidateCanvas.Children.Add(label);
        }
        var x = (clickX - _desktopBounds.X) * sx;
        var y = (clickY - _desktopBounds.Y) * sy;
        CandidateCanvas.Children.Add(new Line { X1 = x - 9, X2 = x + 9, Y1 = y, Y2 = y, Stroke = Brushes.Black, StrokeThickness = 5 });
        CandidateCanvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = y - 9, Y2 = y + 9, Stroke = Brushes.Black, StrokeThickness = 5 });
        CandidateCanvas.Children.Add(new Line { X1 = x - 9, X2 = x + 9, Y1 = y, Y2 = y, Stroke = brush, StrokeThickness = 2 });
        CandidateCanvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = y - 9, Y2 = y + 9, Stroke = brush, StrokeThickness = 2 });
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private void StopTestClick(object sender, RoutedEventArgs e) => Close();
    private void ScopeHelpClick(object sender, RoutedEventArgs e) => RequestHelp(MatchResultHelpAction.SearchScope);
    private void AlternateHelpClick(object sender, RoutedEventArgs e) => RequestHelp(MatchResultHelpAction.AlternateTemplate);
    private void RecaptureHelpClick(object sender, RoutedEventArgs e) => RequestHelp(MatchResultHelpAction.Recapture);
    private void RequestHelp(MatchResultHelpAction action)
    {
        if (IsClosed || _isPending) return;
        RequestedHelpAction = action;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        IsClosed = true;
        try
        {
            if (_isPending && !_cancellationRaised)
            {
                _cancellationRaised = true;
                CancellationRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            base.OnClosed(e);
        }
    }
}
