using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Ming_AutoClicker.Models;

namespace Ming_AutoClicker.Services;

public enum MatchFailureReason
{
    None, BelowThreshold, InvalidTemplate, CaptureFailed, MatchingError, Cancelled, TimedOut,
    Ambiguous, ScopeUnavailable, UninformativeTemplate, TargetChanged
}

public class MatchCandidate
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double Similarity { get; set; }
    public double Scale { get; set; } = 1;
    public int ClickX { get; set; }
    public int ClickY { get; set; }
    public string TemplatePath { get; set; } = string.Empty;
    public int TemplateIndex { get; set; }
    internal Rectangle SupportBounds { get; set; }
    public Rectangle GetRectangle() => new(X - Width / 2, Y - Height / 2, Width, Height);
}

/// <summary>Coordinates are physical desktop pixels; a score is correlation, not probability.</summary>
public class MatchResult : MatchCandidate
{
    public bool Found { get; set; }
    public double Threshold { get; set; }
    public double SecondBestSimilarity { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public string MatchMethod { get; set; } = string.Empty;
    public MatchFailureReason FailureReason { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public List<MatchCandidate> Candidates { get; set; } = new();
    public BitmapSource? Snapshot { get; set; }
    public Rectangle CaptureBounds { get; set; }
    public bool HasCandidate => Width > 0 && Height > 0;
    public static MatchResult NotFound => new() { FailureReason = MatchFailureReason.BelowThreshold };
}

/// <summary>Automatic bounded matching. History never bypasses the scope's competing-target search.</summary>
public sealed class ImageMatchService : IDisposable
{
    public const double DefaultThreshold = 0.8;
    private const int MaxTemplates = 12, MaxScaledImages = 40, MaxTemplatePixels = 4_000_000;
    private const int MaxPeaksPerScale = 128, MaxHypotheses = 128;
    private const long MaxCacheBytes = 128L * 1024 * 1024, MaxTemplateCacheBytes = 24L * 1024 * 1024;
    private const double MinimumScale = 0.5, MaximumScale = 2;
    private readonly ScreenCaptureService _screenshots;
    private readonly IImageFrameSource _frames;
    private readonly object _matchLock = new();
    private readonly Dictionary<string, PreparedTemplate> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MatchCandidate> _history = new(StringComparer.OrdinalIgnoreCase);
    private long _access;
    private bool _disposed;
    public int MatchTimeoutMs { get; set; } = 5000;

    public ImageMatchService(ScreenCaptureService service, IImageFrameSource? frameSource = null)
    {
        _screenshots = service ?? throw new ArgumentNullException(nameof(service));
        _frames = frameSource ?? new DesktopImageFrameSource(service);
    }

    // Compatibility overloads: AdaptiveScale is retained on disk, but both values now use Auto.
    public MatchResult FindImage(string templatePath, double threshold = DefaultThreshold, bool adaptiveScale = false) =>
        FindImage(new FindImageAction { ImagePath = templatePath, MatchThreshold = threshold });
    public MatchResult TestMatch(string templatePath, double threshold = DefaultThreshold, bool adaptiveScale = false) =>
        TestMatch(new FindImageAction { ImagePath = templatePath, MatchThreshold = threshold });
    public MatchResult TestMatch(FindImageAction action, CancellationToken cancellationToken = default,
        Action<BitmapSource, Rectangle>? captureCompleted = null) =>
        CaptureAndMatch(action, cancellationToken, true, null, captureCompleted);
    public MatchResult FindImageInRegion(string templatePath, int x, int y, int width, int height,
        double threshold = DefaultThreshold, bool adaptiveScale = false) => FindImage(new FindImageAction
        {
            ImagePath = templatePath, MatchThreshold = threshold,
            SearchScope = new() { Kind = ImageSearchScopeKind.Region, X = x, Y = y, Width = width, Height = height }
        });
    public MatchResult FindImage(FindImageAction action, CancellationToken cancellationToken = default,
        bool includeSnapshot = false, int? budgetMs = null) =>
        CaptureAndMatch(action, cancellationToken, includeSnapshot, budgetMs, null);

    private MatchResult CaptureAndMatch(FindImageAction action, CancellationToken token,
        bool includeSnapshot, int? budgetMs, Action<BitmapSource, Rectangle>? captureCompleted)
    {
        var budget = new SearchBudget(budgetMs ?? MatchTimeoutMs, token);
        MatchResult result;
        BitmapSource? snapshot = null;
        var captureBounds = Rectangle.Empty;
        try
        {
            budget.Check();
            using var frame = _frames.Capture(action.SearchScope);
            captureBounds = frame.Bounds;
            snapshot = includeSnapshot ? frame.CreateSnapshot() : null;
            budget.Check();
            if (snapshot != null) captureCompleted?.Invoke(snapshot, captureBounds);
            result = MatchFrameCore(frame.Image, action, frame.Bounds.Location, frame.Dpi, budget);
            if (result.Found) budget.Check();
        }
        catch (ImageSearchScopeException ex) { result = Failure(MatchFailureReason.ScopeUnavailable, ex.Message); }
        catch (OperationCanceledException) { result = Failure(MatchFailureReason.Cancelled); }
        catch (SearchBudgetExceededException) { result = Failure(MatchFailureReason.TimedOut, Text("MatchSearchIncomplete")); }
        catch (Exception ex) { result = Failure(MatchFailureReason.CaptureFailed, ex.Message); }
        result.CaptureBounds = captureBounds;
        result.Snapshot = snapshot;
        result.ElapsedMilliseconds = budget.ElapsedMilliseconds;
        return result;
    }

    /// <summary>No desktop capture or mouse input. Source is borrowed for the duration of the call.</summary>
    public MatchResult MatchFrame(Image<Bgr, byte> source, FindImageAction action, Point origin = default,
        double dpi = 96, CancellationToken cancellationToken = default, int? budgetMs = null)
    {
        var budget = new SearchBudget(budgetMs ?? MatchTimeoutMs, cancellationToken);
        var result = MatchFrameCore(source, action, origin, dpi, budget);
        result.CaptureBounds = new Rectangle(origin, source.Size);
        result.ElapsedMilliseconds = budget.ElapsedMilliseconds;
        return result;
    }

    private MatchResult MatchFrameCore(Image<Bgr, byte> source, FindImageAction action, Point origin,
        double dpi, SearchBudget budget)
    {
        var entered = false;
        var accepted = new List<MatchCandidate>();
        MatchCandidate? bestRejected = null;
        var threshold = double.IsFinite(action.MatchThreshold) ? Math.Clamp(action.MatchThreshold, 0, 1) : DefaultThreshold;
        try
        {
            while (!(entered = Monitor.TryEnter(_matchLock, 20))) budget.Check();
            budget.Check();
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var gray = source.Convert<Gray, byte>();
            using var pyramid = new SourcePyramid(gray);
            var colorData = source.Data;
            var informative = false;
            var fits = false;
            var templateIndex = -1;
            foreach (var variant in action.GetTemplates())
            {
                templateIndex++;
                budget.Check();
                var template = GetTemplate(variant);
                if (!template.Original.Informative) continue;
                informative = true;
                if (template.Original.Width * MinimumScale > source.Width ||
                    template.Original.Height * MinimumScale > source.Height) continue;
                fits = true;
                var historyKey = template.Key + "|" + origin + "|" + source.Size + "|" + dpi.ToString("R", CultureInfo.InvariantCulture);
                _history.TryGetValue(historyKey, out var previous);
                var hypotheses = new List<Hypothesis>();
                var dimensions = new HashSet<(int, int, double)>();
                foreach (var scale in GetScaleSchedule(variant.CaptureDpi, dpi, previous?.Scale))
                {
                    budget.Check();
                    // Always retain an original-resolution pass. Other scales use the coarsest level
                    // keeping twelve template pixels across, rather than penalizing every level for tiny scales.
                    var pyramidFactor = Math.Abs(scale - 1) < 0.0001 ? 1 : ChoosePyramidFactor(source.Size,
                        new Size((int)Math.Round(template.Original.ValidBounds.Width * scale), (int)Math.Round(template.Original.ValidBounds.Height * scale)));
                    var searchGray = pyramid.Get(pyramidFactor);
                    var factorX = (double)searchGray.Width / source.Width;
                    var factorY = (double)searchGray.Height / source.Height;
                    var width = Math.Max(1, (int)Math.Round(template.Original.Width * scale * factorX));
                    var height = Math.Max(1, (int)Math.Round(template.Original.Height * scale * factorY));
                    if (!dimensions.Add((width, height, pyramidFactor)) || width < 5 || height < 5 ||
                        width > searchGray.Width || height > searchGray.Height) continue;
                    var scaled = template.GetSize(width, height);
                    if (!scaled.Informative) continue;
                    foreach (var peak in FindPeaks(searchGray, scaled, Math.Max(0.35, threshold - 0.25), budget))
                    {
                        MergeHypothesis(hypotheses, new Hypothesis(
                            (peak.Location.X + width / 2.0) / factorX,
                            (peak.Location.Y + height / 2.0) / factorY,
                            scale, peak.Score,
                            (peak.Location.X + scaled.ValidBounds.X + scaled.ValidBounds.Width / 2.0) / factorX,
                            (peak.Location.Y + scaled.ValidBounds.Y + scaled.ValidBounds.Height / 2.0) / factorY,
                            scaled.ValidBounds.Width / factorX, scaled.ValidBounds.Height / factorY, pyramidFactor));
                        if (hypotheses.Count > MaxHypotheses) throw new SearchBudgetExceededException();
                    }
                }
                // Previous position changes order only; all locations and scales have been searched above.
                var ordered = hypotheses.OrderBy(h => previous == null ? 0 :
                    Math.Abs(h.X - (previous.X - origin.X)) + Math.Abs(h.Y - (previous.Y - origin.Y)))
                    .ThenByDescending(h => h.Score);
                MatchCandidate? templateBest = null;
                foreach (var hypothesis in ordered)
                {
                    budget.Check();
                    var candidate = Refine(gray, colorData, template, variant, action, hypothesis,
                        origin, hypothesis.PyramidFactor, threshold, templateIndex, budget, ref bestRejected);
                    if (candidate == null) continue;
                    MergeCandidate(accepted, candidate);
                    if (templateBest == null || candidate.Similarity > templateBest.Similarity) templateBest = candidate;
                }
                if (templateBest != null)
                {
                    if (_history.Count >= 64 && !_history.ContainsKey(historyKey)) _history.Remove(_history.Keys.First());
                    _history[historyKey] = templateBest;
                }
                TrimCache(template);
            }
            budget.Check();
            if (!informative) return Failure(MatchFailureReason.UninformativeTemplate, Text("MatchTemplateUninformative"));
            if (!fits) return Failure(MatchFailureReason.InvalidTemplate, Text("MatchTemplateTooLarge"));
            var candidates = accepted.OrderBy(c => c.Y).ThenBy(c => c.X).ToList();
            var result = new MatchResult
            {
                Threshold = threshold, Candidates = candidates, MatchMethod = Text("MatchMethodAutomatic"),
                FailureReason = MatchFailureReason.BelowThreshold
            };
            var strongest = candidates.OrderByDescending(c => c.Similarity).FirstOrDefault() ?? bestRejected;
            if (strongest != null) CopyCandidate(strongest, result);
            if (candidates.Count > 1)
                result.SecondBestSimilarity = candidates.OrderByDescending(c => c.Similarity).Skip(1).First().Similarity;
            if (action.SelectionMode == ImageSelectionMode.Unique && candidates.Count > 1)
            {
                result.FailureReason = MatchFailureReason.Ambiguous;
                result.ErrorMessage = Text("MatchMultipleCandidates");
                return result;
            }
            var index = action.SelectionMode == ImageSelectionMode.Ordered ? action.MatchIndex - 1 : 0;
            if (index < 0 || index >= candidates.Count) return result;
            CopyCandidate(candidates[index], result);
            result.Found = true;
            result.FailureReason = MatchFailureReason.None;
            return result;
        }
        catch (OperationCanceledException) { return Failure(MatchFailureReason.Cancelled); }
        catch (SearchBudgetExceededException) { return Failure(MatchFailureReason.TimedOut, Text("MatchSearchIncomplete")); }
        catch (Exception ex) when (ex is FileNotFoundException or UnauthorizedAccessException or ArgumentException or OverflowException)
        { return Failure(MatchFailureReason.InvalidTemplate, ex.Message); }
        catch (Exception ex) { Debug.WriteLine(ex); return Failure(MatchFailureReason.MatchingError, ex.Message); }
        finally
        {
            if (entered) { TrimCache(null); Monitor.Exit(_matchLock); }
        }
    }

    private MatchCandidate? Refine(Image<Gray, byte> source, byte[,,] sourceColor,
        PreparedTemplate template, ImageTemplateVariant variant, FindImageAction action,
        Hypothesis hypothesis, Point origin, double pyramidFactor, double threshold, int templateIndex,
        SearchBudget budget, ref MatchCandidate? bestRejected)
    {
        MatchCandidate? best = null;
        var dimensions = new HashSet<(int, int)>();
        var scales = new List<double> { hypothesis.Scale };
        for (var step = -6; step <= 6; step++) scales.Add(Math.Clamp(hypothesis.Scale + step * 0.01, MinimumScale, MaximumScale));
        foreach (var scale in scales)
        {
            budget.Check();
            var width = Math.Max(1, (int)Math.Round(template.Original.Width * scale));
            var height = Math.Max(1, (int)Math.Round(template.Original.Height * scale));
            if (!dimensions.Add((width, height)) || width < 5 || height < 5 || width > source.Width || height > source.Height) continue;
            var scaled = template.GetSize(width, height);
            if (!scaled.Informative) continue;
            var margin = Math.Max(4, (int)Math.Ceiling(3 / pyramidFactor + Math.Max(width, height) * 0.04));
            var left = Math.Max(0, (int)Math.Floor(hypothesis.X - width / 2.0) - margin);
            var top = Math.Max(0, (int)Math.Floor(hypothesis.Y - height / 2.0) - margin);
            var right = Math.Min(source.Width, (int)Math.Ceiling(hypothesis.X + width / 2.0) + margin);
            var bottom = Math.Min(source.Height, (int)Math.Ceiling(hypothesis.Y + height / 2.0) + margin);
            if (right - left < width || bottom - top < height) continue;
            using var region = source.Copy(new Rectangle(left, top, right - left, bottom - top));
            var peaks = FindPeaks(region, scaled, Math.Max(0.2, threshold - 0.3), budget, 1, requireComplete: false);
            if (peaks.Count == 0) continue;
            var peak = peaks[0];
            var location = new Point(left + peak.Location.X, top + peak.Location.Y);
            var candidate = MakeCandidate(location, width, height, peak.Score, scale, origin, variant, action, templateIndex, scaled.ValidBounds);
            if (bestRejected == null || candidate.Similarity > bestRejected.Similarity) bestRejected = candidate;
            if (candidate.Similarity < threshold || !VerifyAppearance(sourceColor, location, scaled, threshold, budget)) continue;
            if (best == null || candidate.Similarity > best.Similarity) best = candidate;
        }
        return best;
    }

    private static bool VerifyAppearance(byte[,,] source, Point location, TemplateImage template,
        double threshold, SearchBudget budget)
    {
        var colors = template.Color.Data;
        var mask = template.Mask.Data;
        double squaredError = 0, edgeDot = 0, sourceEnergy = 0, templateEnergy = 0;
        var count = 0;
        for (var y = 0; y < template.Height; y++)
        {
            if (y % 32 == 0) budget.Check();
            for (var x = 0; x < template.Width; x++)
            {
                if (mask[y, x, 0] == 0) continue;
                count++;
                for (var channel = 0; channel < 3; channel++)
                {
                    var difference = source[location.Y + y, location.X + x, channel] - colors[y, x, channel];
                    squaredError += difference * difference;
                    if (x > 0 && mask[y, x - 1, 0] != 0)
                    {
                        var a = source[location.Y + y, location.X + x, channel] - source[location.Y + y, location.X + x - 1, channel];
                        var b = colors[y, x, channel] - colors[y, x - 1, channel];
                        edgeDot += a * b; sourceEnergy += a * a; templateEnergy += b * b;
                    }
                    if (y > 0 && mask[y - 1, x, 0] != 0)
                    {
                        var a = source[location.Y + y, location.X + x, channel] - source[location.Y + y - 1, location.X + x, channel];
                        var b = colors[y, x, channel] - colors[y - 1, x, channel];
                        edgeDot += a * b; sourceEnergy += a * a; templateEnergy += b * b;
                    }
                }
            }
        }
        if (count < 25 || sourceEnergy <= 0 || templateEnergy <= 0) return false;
        var colorError = Math.Sqrt(squaredError / (count * 3)) / 255;
        var edgeCorrelation = edgeDot / Math.Sqrt(sourceEnergy * templateEnergy);
        return colorError <= Math.Max(0.08, (1 - threshold) * 0.65) &&
               edgeCorrelation >= Math.Max(0.5, threshold - 0.2);
    }

    private static List<Peak> FindPeaks(Image<Gray, byte> source, TemplateImage template, double floor,
        SearchBudget budget, int maximum = MaxPeaksPerScale, bool requireComplete = true)
    {
        budget.Check();
        using var result = new Mat();
        CvInvoke.MatchTemplate(source, template.Gray, result, TemplateMatchingType.CcoeffNormed,
            template.HasMask ? template.Mask : null);
        if (template.HasMask)
        {
            // Unmasked CCOEFF handles constant source patches in OpenCV. Masked normalization can
            // generate NaN/Inf; native operations avoid copying/scanning millions of scores in managed code.
            CvInvoke.PatchNaNs(result, -1);
            CvInvoke.Threshold(result, result, 1.001, 0, ThresholdType.ToZeroInv);
        }
        var peaks = new List<Peak>();
        for (var i = 0; i <= maximum; i++)
        {
            budget.Check();
            double minimum = 0, maximumValue = 0;
            var minimumLocation = Point.Empty;
            var location = Point.Empty;
            CvInvoke.MinMaxLoc(result, ref minimum, ref maximumValue, ref minimumLocation, ref location);
            if (!double.IsFinite(maximumValue) || maximumValue < floor) break;
            if (i == maximum)
            {
                if (requireComplete) throw new SearchBudgetExceededException();
                break;
            }
            peaks.Add(new Peak(location, Math.Clamp(maximumValue, 0, 1)));
            // Transparent/ignored padding is not part of the target and must not hide a neighbor.
            var radiusX = Math.Max(2, template.ValidBounds.Width / 2);
            var radiusY = Math.Max(2, template.ValidBounds.Height / 2);
            var suppression = Rectangle.Intersect(new Rectangle(0, 0, result.Cols, result.Rows),
                new Rectangle(location.X - radiusX, location.Y - radiusY, radiusX * 2 + 1, radiusY * 2 + 1));
            using var region = new Mat(result, suppression);
            region.SetTo(new MCvScalar(-1));
        }
        return peaks;
    }

    private static double ChoosePyramidFactor(Size source, Size template)
    {
        var factor = 1.0;
        // This is evaluated separately at each scale. Small templates retain full resolution.
        while (Math.Max(source.Width, source.Height) * factor > 600 &&
               Math.Min(template.Width, template.Height) * factor / 2 >= 12) factor /= 2;
        return factor;
    }

    private static IEnumerable<double> GetScaleSchedule(double? capturedDpi, double dpi, double? previous)
    {
        var seen = new HashSet<int>();
        var scales = new List<double>();
        void Add(double value)
        {
            if (!double.IsFinite(value) || value < MinimumScale || value > MaximumScale) return;
            if (seen.Add((int)Math.Round(value * 10000))) scales.Add(value);
        }
        if (previous.HasValue) Add(previous.Value);
        Add(1);
        if (capturedDpi is > 0 && dpi > 0) Add(dpi / capturedDpi.Value);
        foreach (var step in Enumerable.Range(10, 31).OrderBy(i => Math.Abs(i - 20))) Add(step * 0.05);
        return scales;
    }

    private static void MergeHypothesis(List<Hypothesis> hypotheses, Hypothesis candidate)
    {
        for (var i = 0; i < hypotheses.Count; i++)
        {
            var current = hypotheses[i];
            if (Math.Abs(candidate.SupportX - current.SupportX) <= Math.Min(candidate.Width, current.Width) * 0.3 &&
                Math.Abs(candidate.SupportY - current.SupportY) <= Math.Min(candidate.Height, current.Height) * 0.3)
            {
                if (candidate.Score > current.Score) hypotheses[i] = candidate;
                return;
            }
        }
        hypotheses.Add(candidate);
    }

    private static void MergeCandidate(List<MatchCandidate> candidates, MatchCandidate candidate)
    {
        for (var i = 0; i < candidates.Count; i++)
        {
            var current = candidates[i];
            var first = current.SupportBounds;
            var second = candidate.SupportBounds;
            var intersection = Rectangle.Intersect(first, second);
            var intersectionArea = (double)Math.Max(0, intersection.Width) * Math.Max(0, intersection.Height);
            var smallerArea = Math.Min((double)first.Width * first.Height, (double)second.Width * second.Height);
            if (intersectionArea / smallerArea >= 0.65 &&
                Math.Abs((first.Left + first.Width / 2.0) - (second.Left + second.Width / 2.0)) <= Math.Min(first.Width, second.Width) * 0.3 &&
                Math.Abs((first.Top + first.Height / 2.0) - (second.Top + second.Height / 2.0)) <= Math.Min(first.Height, second.Height) * 0.3)
            {
                if (candidate.Similarity > current.Similarity) candidates[i] = candidate;
                return;
            }
        }
        candidates.Add(candidate);
    }

    private static MatchCandidate MakeCandidate(Point location, int width, int height, double score,
        double scale, Point origin, ImageTemplateVariant variant, FindImageAction action, int templateIndex, Rectangle support)
    {
        var x = checked(origin.X + location.X);
        var y = checked(origin.Y + location.Y);
        var click = variant.ClickPoint;
        return new MatchCandidate
        {
            X = checked(x + width / 2), Y = checked(y + height / 2), Width = width, Height = height,
            Similarity = Math.Clamp(score, 0, 1), Scale = scale, TemplatePath = variant.ImagePath, TemplateIndex = templateIndex,
            SupportBounds = new Rectangle(checked(x + support.X), checked(y + support.Y), support.Width, support.Height),
            ClickX = checked(click == null ? x + width / 2 + action.OffsetX : x + Math.Clamp((int)Math.Round(click.X * (width - 1)), 0, width - 1)),
            ClickY = checked(click == null ? y + height / 2 + action.OffsetY : y + Math.Clamp((int)Math.Round(click.Y * (height - 1)), 0, height - 1))
        };
    }

    public Task<MatchResult> WaitForImageAsync(string templatePath, double threshold = DefaultThreshold,
        int timeoutMs = 30000, int intervalMs = 200, CancellationToken cancellationToken = default,
        bool adaptiveScale = false) => WaitForImageAsync(new FindImageAction
        { ImagePath = templatePath, MatchThreshold = threshold, TimeoutMs = timeoutMs }, cancellationToken,
            timeoutMs, intervalMs, verifyBeforeReturn: true);

    /// <summary>The total deadline includes captures, retries and a final fresh-frame verification.</summary>
    public async Task<MatchResult> WaitForImageAsync(FindImageAction action,
        CancellationToken cancellationToken = default, int legacyTimeoutMs = 30000,
        int intervalMs = 200, bool verifyBeforeReturn = true)
    {
        var timeout = action.GetTimeoutMs(legacyTimeoutMs);
        var oneShot = timeout <= 0;
        var budget = new SearchBudget(oneShot ? MatchTimeoutMs : timeout, cancellationToken);
        MatchResult? last = null;
        try
        {
            do
            {
                budget.Check();
                var iteration = Stopwatch.StartNew();
                var result = FindImage(action, cancellationToken, budgetMs: budget.RemainingMilliseconds);
                if (result.Found && verifyBeforeReturn)
                {
                    budget.Check();
                    var fresh = VerifyFreshFrame(action, result, cancellationToken, Math.Min(300, budget.RemainingMilliseconds));
                    if (fresh.Found && IsSameTarget(result, fresh)) result = fresh;
                    else
                    {
                        result = fresh;
                        if (result.Found || result.FailureReason == MatchFailureReason.BelowThreshold)
                        {
                            result.Found = false;
                            result.FailureReason = MatchFailureReason.TargetChanged;
                            result.ErrorMessage = Text("MatchTargetChanged");
                        }
                    }
                }
                last = result;
                budget.Check();
                if (result.Found || oneShot || result.FailureReason is MatchFailureReason.InvalidTemplate or
                    MatchFailureReason.CaptureFailed or MatchFailureReason.MatchingError or
                    MatchFailureReason.UninformativeTemplate or MatchFailureReason.ScopeUnavailable or
                    MatchFailureReason.Ambiguous or MatchFailureReason.Cancelled)
                {
                    result.ElapsedMilliseconds = budget.ElapsedMilliseconds;
                    return result;
                }
                var delay = Math.Min(budget.RemainingMilliseconds, Math.Max(1, intervalMs - (int)iteration.ElapsedMilliseconds));
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            } while (!oneShot);
        }
        catch (OperationCanceledException) { last = Failure(MatchFailureReason.Cancelled); }
        catch (SearchBudgetExceededException)
        {
            var incompleteSearch = last?.FailureReason == MatchFailureReason.TimedOut;
            last ??= Failure(MatchFailureReason.TimedOut);
            last.Found = false;
            last.FailureReason = MatchFailureReason.TimedOut;
            last.ErrorMessage = Text(incompleteSearch ? "MatchSearchIncomplete" : "MatchTimedOut");
        }
        last ??= Failure(MatchFailureReason.TimedOut);
        last.ElapsedMilliseconds = budget.ElapsedMilliseconds;
        return last;
    }

    private static bool IsSameTarget(MatchResult previous, MatchResult current)
    {
        var tolerance = Math.Max(3, Math.Min(previous.Width, previous.Height) * 0.08);
        return Math.Abs(previous.X - current.X) <= tolerance && Math.Abs(previous.Y - current.Y) <= tolerance &&
               Math.Abs(previous.Width - current.Width) <= Math.Max(3, previous.Width * 0.08) &&
               Math.Abs(previous.Height - current.Height) <= Math.Max(3, previous.Height * 0.08);
    }

    private MatchResult VerifyFreshFrame(FindImageAction action, MatchResult previous,
        CancellationToken token, int budgetMs)
    {
        var budget = new SearchBudget(budgetMs, token);
        var entered = false;
        try
        {
            budget.Check();
            using var frame = _frames.Capture(action.SearchScope);
            budget.Check();
            // A moved/resized bound window changes the meaning of the old position; do a full search again.
            if (frame.Bounds != previous.CaptureBounds) return Failure(MatchFailureReason.TargetChanged, Text("MatchTargetChanged"));
            while (!(entered = Monitor.TryEnter(_matchLock, 10))) budget.Check();
            budget.Check();
            ObjectDisposedException.ThrowIf(_disposed, this);
            var variant = action.GetTemplates().ElementAtOrDefault(previous.TemplateIndex);
            if (variant == null || !string.Equals(variant.ImagePath, previous.TemplatePath, StringComparison.OrdinalIgnoreCase))
                return Failure(MatchFailureReason.TargetChanged, Text("MatchTargetChanged"));
            var prepared = GetTemplate(variant);
            var template = prepared.GetSize(previous.Width, previous.Height);
            var rectangle = previous.GetRectangle();
            rectangle.Offset(-frame.Bounds.X, -frame.Bounds.Y);
            rectangle.Inflate(3, 3);
            rectangle.Intersect(new Rectangle(Point.Empty, frame.Image.Size));
            if (rectangle.Width < template.Width || rectangle.Height < template.Height)
                return Failure(MatchFailureReason.TargetChanged, Text("MatchTargetChanged"));
            using var color = frame.Image.Copy(rectangle);
            using var gray = color.Convert<Gray, byte>();
            var peaks = FindPeaks(gray, template, previous.Threshold, budget);
            if (peaks.Count != 1 || !VerifyAppearance(color.Data, peaks[0].Location, template, previous.Threshold, budget))
                return Failure(MatchFailureReason.TargetChanged, Text("MatchTargetChanged"));
            var origin = new Point(checked(frame.Bounds.X + rectangle.X), checked(frame.Bounds.Y + rectangle.Y));
            var candidate = MakeCandidate(peaks[0].Location, template.Width, template.Height, peaks[0].Score,
                previous.Scale, origin, variant, action, previous.TemplateIndex, template.ValidBounds);
            var result = new MatchResult
            {
                Found = true, Threshold = previous.Threshold, MatchMethod = previous.MatchMethod,
                CaptureBounds = frame.Bounds, Candidates = previous.Candidates,
                SecondBestSimilarity = previous.SecondBestSimilarity
            };
            CopyCandidate(candidate, result);
            budget.Check();
            return result;
        }
        catch (OperationCanceledException) { return Failure(MatchFailureReason.Cancelled); }
        catch (SearchBudgetExceededException) { return Failure(MatchFailureReason.TargetChanged, Text("MatchTargetChanged")); }
        catch (ImageSearchScopeException ex) { return Failure(MatchFailureReason.ScopeUnavailable, ex.Message); }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or UnauthorizedAccessException or OverflowException)
        { return Failure(MatchFailureReason.InvalidTemplate, ex.Message); }
        catch (Exception ex) { return Failure(MatchFailureReason.CaptureFailed, ex.Message); }
        finally { if (entered) Monitor.Exit(_matchLock); }
    }

    public MatchResult[] FindAllMatches(string templatePath, double threshold = DefaultThreshold)
    {
        var result = FindImage(new FindImageAction
        { ImagePath = templatePath, MatchThreshold = threshold, SelectionMode = ImageSelectionMode.Ordered });
        if (!result.Found) return Array.Empty<MatchResult>();
        return result.Candidates.Select(candidate =>
        {
            var match = new MatchResult { Found = true, Threshold = threshold, MatchMethod = result.MatchMethod };
            CopyCandidate(candidate, match);
            return match;
        }).ToArray();
    }

    private PreparedTemplate GetTemplate(ImageTemplateVariant variant)
    {
        if (string.IsNullOrWhiteSpace(variant.ImagePath)) throw new ArgumentException(Text("TemplatePathEmpty"));
        var fullPath = Path.GetFullPath(Path.IsPathRooted(variant.ImagePath) ? variant.ImagePath :
            Path.Combine(_screenshots.GetScreenshotDirectory(), variant.ImagePath));
        var root = Path.GetFullPath(_screenshots.GetScreenshotDirectory());
        var relative = Path.GetRelativePath(root, fullPath);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new UnauthorizedAccessException(LocalizationService.Current.Format("ImageOutsideScreenshots", fullPath));
        var info = new FileInfo(fullPath);
        if (!info.Exists) throw new FileNotFoundException(LocalizationService.Current.Format("ImageFileNotFound", fullPath), fullPath);
        var regions = variant.IgnoredRegions ?? new();
        var regionKey = string.Join(";", regions.Select(r => string.Join(",", new[] { r.X, r.Y, r.Width, r.Height }
            .Select(v => v.ToString("R", CultureInfo.InvariantCulture)))));
        var key = fullPath + "|" + regionKey;
        if (_cache.TryGetValue(key, out var cached))
        {
            if (cached.LastWriteTicks == info.LastWriteTimeUtc.Ticks && cached.FileLength == info.Length)
            { cached.LastAccess = ++_access; return cached; }
            cached.Dispose();
            _cache.Remove(key);
        }
        var color = _screenshots.LoadImage(fullPath);
        Image<Gray, byte>? mask = null;
        try
        {
            if ((long)color.Width * color.Height > MaxTemplatePixels) throw new ArgumentException(Text("MatchTemplateTooLarge"));
            mask = ReadMask(fullPath, color.Size, regions);
            var original = new TemplateImage(color, mask);
            color = null!;
            mask = null;
            cached = new PreparedTemplate(key, info.LastWriteTimeUtc.Ticks, info.Length, original) { LastAccess = ++_access };
            _cache.Add(key, cached);
            TrimCache(cached);
            return cached;
        }
        finally { color?.Dispose(); mask?.Dispose(); }
    }

    private static Image<Gray, byte> ReadMask(string path, Size size, List<ImageRelativeRegion> ignored)
    {
        using var bitmap = new Bitmap(path);
        using var argb = bitmap.Clone(new Rectangle(Point.Empty, bitmap.Size), PixelFormat.Format32bppArgb);
        var locked = argb.LockBits(new Rectangle(Point.Empty, argb.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var mask = new Image<Gray, byte>(size);
        try
        {
            var row = new byte[Math.Abs(locked.Stride)];
            var data = mask.Data;
            for (var y = 0; y < size.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(locked.Scan0, y * locked.Stride), row, 0, row.Length);
                for (var x = 0; x < size.Width; x++) data[y, x, 0] = row[x * 4 + 3] >= 250 ? (byte)255 : (byte)0;
            }
            foreach (var region in ignored)
            {
                if (!double.IsFinite(region.X) || !double.IsFinite(region.Y) || !double.IsFinite(region.Width) ||
                    !double.IsFinite(region.Height) || region.Width <= 0 || region.Height <= 0) continue;
                var left = Math.Clamp((int)Math.Floor(region.X * size.Width), 0, size.Width);
                var top = Math.Clamp((int)Math.Floor(region.Y * size.Height), 0, size.Height);
                var right = Math.Clamp((int)Math.Ceiling((region.X + region.Width) * size.Width), 0, size.Width);
                var bottom = Math.Clamp((int)Math.Ceiling((region.Y + region.Height) * size.Height), 0, size.Height);
                for (var y = top; y < bottom; y++)
                    for (var x = left; x < right; x++) data[y, x, 0] = 0;
            }
            return mask;
        }
        catch { mask.Dispose(); throw; }
        finally { argb.UnlockBits(locked); }
    }

    private void TrimCache(PreparedTemplate? keep)
    {
        while (_cache.Count > MaxTemplates || _cache.Values.Sum(t => t.Bytes) > MaxCacheBytes)
        {
            var oldest = _cache.Values.Where(t => t != keep).OrderBy(t => t.LastAccess).FirstOrDefault();
            if (oldest == null) break;
            _cache.Remove(oldest.Key);
            oldest.Dispose();
        }
    }

    private static void CopyCandidate(MatchCandidate source, MatchCandidate target)
    {
        target.X = source.X; target.Y = source.Y; target.Width = source.Width; target.Height = source.Height;
        target.Similarity = source.Similarity; target.Scale = source.Scale;
        target.ClickX = source.ClickX; target.ClickY = source.ClickY; target.TemplatePath = source.TemplatePath;
        target.TemplateIndex = source.TemplateIndex;
        target.SupportBounds = source.SupportBounds;
    }
    private static string Text(string key) => LocalizationService.Current.GetString(key);
    private static MatchResult Failure(MatchFailureReason reason, string message = "") =>
        new() { FailureReason = reason, ErrorMessage = message };
    public void Dispose()
    {
        lock (_matchLock)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var entry in _cache.Values) entry.Dispose();
            _cache.Clear(); _history.Clear();
        }
    }

    private sealed record Peak(Point Location, double Score);
    private sealed record Hypothesis(double X, double Y, double Scale, double Score, double SupportX, double SupportY,
        double Width, double Height, double PyramidFactor);
    private sealed class SourcePyramid : IDisposable
    {
        private readonly Image<Gray, byte> _original;
        private readonly Dictionary<double, Image<Gray, byte>> _levels = new();
        public SourcePyramid(Image<Gray, byte> original) => _original = original;
        public Image<Gray, byte> Get(double factor)
        {
            if (factor >= 1) return _original;
            if (_levels.TryGetValue(factor, out var cached)) return cached;
            var resized = _original.Resize(Math.Max(1, (int)Math.Round(_original.Width * factor)),
                Math.Max(1, (int)Math.Round(_original.Height * factor)), Inter.Area);
            _levels.Add(factor, resized);
            return resized;
        }
        public void Dispose() { foreach (var image in _levels.Values) image.Dispose(); }
    }
    private sealed class SearchBudgetExceededException : Exception { }
    private sealed class SearchBudget
    {
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private readonly int _milliseconds;
        private readonly CancellationToken _token;
        public SearchBudget(int milliseconds, CancellationToken token)
        { _milliseconds = Math.Max(0, milliseconds); _token = token; }
        public long ElapsedMilliseconds => _watch.ElapsedMilliseconds;
        public int RemainingMilliseconds => Math.Max(0, _milliseconds - (int)Math.Min(int.MaxValue, _watch.ElapsedMilliseconds));
        public void Check()
        {
            _token.ThrowIfCancellationRequested();
            if (_watch.ElapsedMilliseconds >= _milliseconds) throw new SearchBudgetExceededException();
        }
    }

    private sealed class TemplateImage : IDisposable
    {
        public Image<Bgr, byte> Color { get; }
        public Image<Gray, byte> Gray { get; }
        public Image<Gray, byte> Mask { get; }
        public bool HasMask { get; }
        public bool Informative { get; }
        public Rectangle ValidBounds { get; }
        public int Width => Color.Width;
        public int Height => Color.Height;
        public long Bytes => (long)Width * Height * 5;
        public long LastAccess { get; set; }
        public TemplateImage(Image<Bgr, byte> color, Image<Gray, byte> mask)
        {
            Color = color; Mask = mask; Gray = color.Convert<Gray, byte>();
            var gray = Gray.Data; var valid = Mask.Data;
            double sum = 0, squares = 0;
            var count = 0;
            var left = Width; var top = Height; var right = -1; var bottom = -1;
            for (var y = 0; y < Height; y++)
                for (var x = 0; x < Width; x++)
                {
                    // Resized mask edges are excluded if interpolation touched an ignored pixel.
                    valid[y, x, 0] = valid[y, x, 0] >= 254 ? (byte)255 : (byte)0;
                    if (valid[y, x, 0] != 0)
                    {
                        count++; sum += gray[y, x, 0]; squares += gray[y, x, 0] * gray[y, x, 0];
                        left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
                    }
                }
            HasMask = count < Width * Height;
            ValidBounds = count == 0 ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
            var variance = count == 0 ? 0 : squares / count - Math.Pow(sum / count, 2);
            Informative = count >= 25 && count >= Width * Height * 0.1 && variance >= 16;
        }
        public void Dispose() { Gray.Dispose(); Mask.Dispose(); Color.Dispose(); }
    }

    private sealed class PreparedTemplate : IDisposable
    {
        private readonly Dictionary<(int, int), TemplateImage> _sizes = new();
        private long _access;
        public string Key { get; }
        public long LastWriteTicks { get; }
        public long FileLength { get; }
        public long LastAccess { get; set; }
        public TemplateImage Original { get; }
        public long Bytes => Original.Bytes + _sizes.Values.Sum(t => t.Bytes);
        public PreparedTemplate(string key, long ticks, long length, TemplateImage original)
        { Key = key; LastWriteTicks = ticks; FileLength = length; Original = original; }
        public TemplateImage GetSize(int width, int height)
        {
            if (width == Original.Width && height == Original.Height) return Original;
            if (_sizes.TryGetValue((width, height), out var image)) { image.LastAccess = ++_access; return image; }
            if ((long)width * height > MaxTemplatePixels) throw new ArgumentException(Text("MatchTemplateTooLarge"));
            var shrinking = width < Original.Width || height < Original.Height;
            var interpolation = shrinking ? Inter.Area : Inter.Linear;
            var color = Original.Color.Resize(width, height, interpolation);
            var mask = Original.Mask.Resize(width, height, interpolation);
            image = new TemplateImage(color, mask) { LastAccess = ++_access };
            _sizes.Add((width, height), image);
            while (_sizes.Count > MaxScaledImages || Bytes > MaxTemplateCacheBytes)
            {
                var oldest = _sizes.Where(p => p.Value != image).OrderBy(p => p.Value.LastAccess).FirstOrDefault();
                if (oldest.Value == null) break;
                _sizes.Remove(oldest.Key); oldest.Value.Dispose();
            }
            return image;
        }
        public void Dispose()
        { foreach (var size in _sizes.Values) size.Dispose(); _sizes.Clear(); Original.Dispose(); }
    }
}
