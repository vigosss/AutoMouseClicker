using System.Drawing;
using Emgu.CV;
using Emgu.CV.Structure;
using Ming_AutoClicker.Models;
using Ming_AutoClicker.Services;

namespace Ming_AutoClicker.Tests;

internal static partial class Program
{
    private static void AddColorSearchTests(List<(string Name, Func<Task> Run)> tests)
    {
        tests.Add(("Find color checks each RGB component and returns physical coordinates", ColorToleranceAndCoordinates));
        tests.Add(("Find color scans real monitors in screen order and skips desktop gaps", ColorDesktopGaps));
        tests.Add(("Find color verifies the chosen point and distinguishes failed captures", ColorFreshFrame));
        tests.Add(("Find color polls until a hit, timeout or cancellation", ColorWaitOutcomes));
    }

    private static Task ColorToleranceAndCoordinates()
    {
        var source = new ColorTestSource(_ =>
        {
            var frame = ColorTestFrame();
            frame.Image[0, 0] = new Bgr(111, 184, 84); // R differs by 6.
            frame.Image[0, 1] = new Bgr(78, 184, 111); // R and B interchanged.
            frame.Image[0, 2] = new Bgr(116, 179, 83); // Exactly 5 away on all three channels.
            frame.Image[1, 0] = new Bgr(111, 184, 78); // Exact target.
            return frame;
        });
        var service = new ColorSearchService(source);
        var action = new FindColorAction { TargetColor = "#4EB86F", Tolerance = 5 };
        var near = service.FindColor(action, includeSnapshot: true);
        Check(near.Found);
        Equal(-8, near.X);
        Equal(-7, near.Y);
        Equal(near.X, near.ClickX);
        Equal(near.Y, near.ClickY);
        Check(near.Snapshot?.IsFrozen == true);
        Equal(new Rectangle(-10, -7, 7, 3), near.CaptureBounds);
        Check(ReferenceEquals(action.SearchScope, source.LastScope));
        action.Tolerance = 0;
        var exact = service.FindColor(action);
        Check(exact.Found);
        Equal(-10, exact.X);
        Equal(-6, exact.Y);
        action.TargetColor = "#FFFFFF";
        Check(service.FindColor(action).IsOrdinaryMiss);
        action.Tolerance = 255;
        Equal(-10, service.FindColor(action).X);
        return Task.CompletedTask;
    }

    private static Task ColorDesktopGaps()
    {
        var source = new ColorTestSource(_ =>
        {
            var frame = ColorTestFrame([new Rectangle(-6, -7, 3, 2), new Rectangle(-10, -6, 3, 2)]);
            frame.Image[0, 0] = new Bgr(0, 0, 0); // Empty space above the left screen.
            frame.Image[0, 6] = new Bgr(0, 0, 0);
            frame.Image[1, 0] = new Bgr(0, 0, 0);
            return frame;
        });
        var result = new ColorSearchService(source).FindColor(new FindColorAction { TargetColor = "#000000", Tolerance = 0 });
        Check(result.Found);
        Equal(-4, result.X);
        Equal(-7, result.Y);
        return Task.CompletedTask;
    }

    private static async Task ColorFreshFrame()
    {
        var action = new FindColorAction { TargetColor = "#000000", TimeoutMs = 0 };
        var source = new ColorTestSource(n =>
        {
            var frame = ColorTestFrame();
            frame.Image[0, n - 1] = new Bgr(0, 0, 0);
            return frame;
        });
        var changed = await new ColorSearchService(source).WaitForColorAsync(action);
        Equal(MatchFailureReason.TargetChanged, changed.FailureReason);
        Check(!changed.Found && !changed.IsOrdinaryMiss);
        Equal(2, source.Captures);

        // Existing macros can still bind a color step to a window and its client region.
        action.SearchScope = new ImageSearchScope
        {
            Kind = ImageSearchScopeKind.Window, WindowProcessName = "target", WindowClassName = "Main",
            WindowTitle = "Document", WindowRegion = new() { X = .25, Y = .25, Width = .5, Height = .5 }
        };
        Check(MacroActionValidator.TryValidate(action, out _));
        source = new ColorTestSource(_ => throw new ImageSearchScopeException("ImageScopeWindowMissing"));
        var unavailable = await new ColorSearchService(source).WaitForColorAsync(action);
        Check(ReferenceEquals(action.SearchScope, source.LastScope));
        Equal(MatchFailureReason.ScopeUnavailable, unavailable.FailureReason);
        Check(!unavailable.IsOrdinaryMiss);
        Equal(1, source.Captures);
    }

    private static async Task ColorWaitOutcomes()
    {
        var action = new FindColorAction { TargetColor = "#000000", TimeoutMs = 3000 };
        var appears = new ColorTestSource(n =>
        {
            var frame = ColorTestFrame();
            if (n >= 2) frame.Image[1, 0] = new Bgr(0, 0, 0);
            return frame;
        });
        var found = await new ColorSearchService(appears).WaitForColorAsync(action, intervalMs: 5);
        Check(found.Found);
        Equal(3, appears.Captures); // Miss, hit, verification.

        action.TimeoutMs = 100;
        var absent = new ColorSearchService(new ColorTestSource(_ => ColorTestFrame()));
        var expired = await absent.WaitForColorAsync(action, intervalMs: 200);
        Equal(MatchFailureReason.TimedOut, expired.FailureReason);
        Check(expired.IsOrdinaryMiss);

        using var stop = new CancellationTokenSource();
        var duringVerification = new ColorTestSource(n =>
        {
            var frame = ColorTestFrame();
            frame.Image[0, 0] = new Bgr(0, 0, 0);
            if (n == 2) stop.Cancel();
            return frame;
        });
        action.TimeoutMs = 0;
        var cancelled = await new ColorSearchService(duringVerification).WaitForColorAsync(action, stop.Token);
        Equal(MatchFailureReason.Cancelled, cancelled.FailureReason);
        Check(!cancelled.Found && !cancelled.IsOrdinaryMiss);
        Equal(2, duringVerification.Captures);
    }

    private static CapturedImageFrame ColorTestFrame(IReadOnlyList<Rectangle>? visibleRegions = null) =>
        new(new Image<Bgr, byte>(7, 3, new Bgr(225, 225, 225)), new Rectangle(-10, -7, 7, 3), 144, visibleRegions);

    private sealed class ColorTestSource(Func<int, CapturedImageFrame> capture) : IImageFrameSource
    {
        public int Captures { get; private set; }
        public ImageSearchScope? LastScope { get; private set; }
        public CapturedImageFrame Capture(ImageSearchScope scope)
        {
            LastScope = scope;
            return capture(++Captures);
        }
    }
}
