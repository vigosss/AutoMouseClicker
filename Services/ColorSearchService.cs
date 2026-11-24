using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using Emgu.CV.Structure;
using Ming_AutoClicker.Models;

namespace Ming_AutoClicker.Services;

public sealed class ColorSearchService(IImageFrameSource frames)
{
    private readonly IImageFrameSource _frames = frames ?? throw new ArgumentNullException(nameof(frames));

    /// <summary>Captures once. The optional preview shows the exact frame searched.</summary>
    public MatchResult FindColor(FindColorAction action, CancellationToken cancellationToken = default,
        bool includeSnapshot = false)
    {
        var timer = Stopwatch.StartNew();
        var result = new MatchResult();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!FindColorAction.TryParseColor(action.TargetColor, out var color) ||
                action.Tolerance is < 0 or > 255)
            {
                result.FailureReason = MatchFailureReason.MatchingError;
                result.ErrorMessage = Text("MacroInvalidFindColor");
                return result;
            }

            using var frame = _frames.Capture(action.SearchScope);
            result.CaptureBounds = frame.Bounds;
            cancellationToken.ThrowIfCancellationRequested();
            if (includeSnapshot)
                result.Snapshot = frame.CreateSnapshot();

            var point = FindFirstPixel(frame, color, action.Tolerance, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (point.HasValue)
            {
                result.Found = true;
                result.X = result.ClickX = point.Value.X;
                result.Y = result.ClickY = point.Value.Y;
                result.Width = result.Height = 1;
            }
            else result.FailureReason = MatchFailureReason.BelowThreshold;
        }
        catch (OperationCanceledException) { result.FailureReason = MatchFailureReason.Cancelled; }
        catch (ImageSearchScopeException ex)
        {
            result.FailureReason = MatchFailureReason.ScopeUnavailable;
            result.ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            result.FailureReason = MatchFailureReason.CaptureFailed;
            result.ErrorMessage = ex.Message;
        }
        finally { result.ElapsedMilliseconds = timer.ElapsedMilliseconds; }
        return result;
    }

    private static Point? FindFirstPixel(CapturedImageFrame frame, Color color, int tolerance,
        CancellationToken cancellationToken)
    {
        var lower = new Bgr(Math.Max(0, color.B - tolerance), Math.Max(0, color.G - tolerance),
            Math.Max(0, color.R - tolerance));
        var upper = new Bgr(Math.Min(255, color.B + tolerance), Math.Min(255, color.G + tolerance),
            Math.Min(255, color.R + tolerance));
        using var mask = frame.Image.InRange(lower, upper);
        var pixels = mask.Data;
        var regions = frame.VisibleRegions.Select(region => Rectangle.Intersect(region, frame.Bounds))
            .Where(region => region.Width > 0 && region.Height > 0)
            .OrderBy(region => region.Left).ToArray();

        for (var row = 0; row < frame.Bounds.Height; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var screenY = frame.Bounds.Top + row;
            foreach (var region in regions)
            {
                if (screenY < region.Top || screenY >= region.Bottom) continue;
                // Only search the visible span. Emgu rows may be padded to a multiple of four bytes.
                var span = MemoryMarshal.CreateReadOnlySpan(
                    ref pixels[row, region.Left - frame.Bounds.Left, 0], region.Width);
                var index = span.IndexOf(byte.MaxValue);
                if (index >= 0) return new Point(region.Left + index, screenY);
            }
        }
        return null;
    }

    /// <summary>A completed miss can follow the macro's miss policy. Capture and verification errors cannot.</summary>
    public async Task<MatchResult> WaitForColorAsync(FindColorAction action, CancellationToken cancellationToken = default,
        int intervalMs = 200, bool verifyBeforeReturn = true)
    {
        var timer = Stopwatch.StartNew();
        bool Expired() => action.TimeoutMs > 0 && timer.ElapsedMilliseconds >= action.TimeoutMs;
        MatchResult last;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var started = timer.ElapsedMilliseconds;
                last = FindColor(action, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!last.Found && !last.IsOrdinaryMiss) break;
                if (Expired()) return TimedOut(timer, afterMiss: false);

                if (last.Found)
                {
                    if (!verifyBeforeReturn) break;
                    var fresh = FindColor(action, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (fresh.IsOrdinaryMiss || (fresh.Found &&
                        (fresh.X != last.X || fresh.Y != last.Y || fresh.CaptureBounds != last.CaptureBounds)))
                    {
                        fresh.Found = false;
                        fresh.FailureReason = MatchFailureReason.TargetChanged;
                        fresh.ErrorMessage = Text("MatchTargetChanged");
                    }
                    if (fresh.Found && Expired()) return TimedOut(timer, afterMiss: false);
                    last = fresh;
                    break;
                }
                if (action.TimeoutMs <= 0) break;

                var scanDuration = Math.Max(1L, timer.ElapsedMilliseconds - started);
                var delay = (int)Math.Max(1L, intervalMs - scanDuration);
                var remaining = action.TimeoutMs - (int)timer.ElapsedMilliseconds;
                if (remaining - delay > scanDuration)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    if (action.TimeoutMs - timer.ElapsedMilliseconds > scanDuration) continue;
                }
                // Avoid authorizing a miss from an incomplete scan started at the deadline.
                while ((remaining = action.TimeoutMs - (int)timer.ElapsedMilliseconds) > 0)
                    await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return TimedOut(timer, afterMiss: true);
            }
        }
        catch (OperationCanceledException) { last = new MatchResult { FailureReason = MatchFailureReason.Cancelled }; }
        last.ElapsedMilliseconds = timer.ElapsedMilliseconds;
        return last;
    }

    private static MatchResult TimedOut(Stopwatch timer, bool afterMiss) => new()
    {
        FailureReason = MatchFailureReason.TimedOut,
        WaitExpiredAfterMiss = afterMiss,
        ErrorMessage = Text(afterMiss ? "MatchTimedOut" : "MatchSearchIncomplete"),
        ElapsedMilliseconds = timer.ElapsedMilliseconds
    };

    private static string Text(string key) => LocalizationService.Current.GetString(key);
}
