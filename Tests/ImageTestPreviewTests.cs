using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ming_AutoClicker.Models;
using Ming_AutoClicker.Services;
using Ming_AutoClicker.Views.ImageMatching;
using DrawingRectangle = System.Drawing.Rectangle;

namespace Ming_AutoClicker.Tests;

internal static partial class Program
{
    static partial void AddImageTestPreviewTests(List<(string Name, Func<Task> Run)> tests)
    {
        tests.Add(("Image test publishes a frozen frame before matching and reuses it in the result", ImageTestPublishesSameFrame));
        tests.Add(("Image test retains its captured frame after cancellation", ImageTestCancelledSnapshot));
        tests.Add(("Image test retains its captured frame after budget exhaustion", ImageTestTimedOutSnapshot));
        tests.Add(("Failed image capture never publishes a preview frame", ImageTestCaptureFailure));
        tests.Add(("Pending image preview becomes a result without requesting cancellation", ImagePreviewCompletesWithoutCancellation));
        tests.Add(("Closing or stopping a pending image preview requests cancellation once", ImagePreviewCancelsOnce));
        tests.Add(("Queued late image frames and results cannot reopen a closed preview", ImagePreviewIgnoresLateUpdates));
    }

    private static async Task ImageTestPublishesSameFrame()
    {
        using var fixture = new ImageExecutionFixture();
        fixture.Frames.HideAfterFirst = true;
        using var releaseMatching = new ManualResetEventSlim();
        var notice = new TaskCompletionSource<(BitmapSource Snapshot, DrawingRectangle Bounds)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbacks = 0;
        var task = Task.Run(() => fixture.Matcher.TestMatch(fixture.Action, default, (snapshot, bounds) =>
        {
            Interlocked.Increment(ref callbacks);
            notice.TrySetResult((snapshot, bounds));
            if (!releaseMatching.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("The preview test did not release matching.");
        }));
        (BitmapSource Snapshot, DrawingRectangle Bounds) captured;
        try
        {
            captured = await notice.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(!task.IsCompleted, "The UI must receive the screenshot while the matching task is still pending.");
            Check(captured.Snapshot.IsFrozen, "Worker-thread previews must be frozen before crossing to WPF.");
            Equal(new DrawingRectangle(-200, 30, 240, 160), captured.Bounds);
            Equal(240, captured.Snapshot.PixelWidth);
            Equal(160, captured.Snapshot.PixelHeight);
        }
        finally { releaseMatching.Set(); }
        var result = await task;
        Check(result.Found, $"Same-frame test: {result.FailureReason}, {result.ErrorMessage}");
        Check(ReferenceEquals(captured.Snapshot, result.Snapshot), "The result must reuse the frame already shown by the pending preview.");
        Equal(captured.Bounds, result.CaptureBounds);
        Equal(1, callbacks);
        Equal(1, fixture.Frames.Captures);
        Equal(0, fixture.Sender.Positions.Count);
        Equal(0, fixture.Sender.Batches.Count);
    }

    private static async Task ImageTestCancelledSnapshot()
    {
        using var fixture = new ImageExecutionFixture();
        using var cancellation = new CancellationTokenSource();
        BitmapSource? published = null;
        DrawingRectangle bounds = default;
        var result = await Task.Run(() => fixture.Matcher.TestMatch(fixture.Action, cancellation.Token, (snapshot, captureBounds) =>
        {
            published = snapshot;
            bounds = captureBounds;
            cancellation.Cancel();
        }));
        Check(!result.Found);
        Equal(MatchFailureReason.Cancelled, result.FailureReason);
        Check(published?.IsFrozen == true);
        Check(ReferenceEquals(published, result.Snapshot), "Cancellation after capture must not erase the user's screenshot.");
        Equal(bounds, result.CaptureBounds);
        Equal(1, fixture.Frames.Captures);
    }

    private static async Task ImageTestTimedOutSnapshot()
    {
        using var fixture = new ImageExecutionFixture();
        fixture.Matcher.MatchTimeoutMs = 150;
        BitmapSource? published = null;
        DrawingRectangle bounds = default;
        var result = await Task.Run(() => fixture.Matcher.TestMatch(fixture.Action, default, (snapshot, captureBounds) =>
        {
            published = snapshot;
            bounds = captureBounds;
            Thread.Sleep(180); // Deterministically expire the total search budget after a successful capture.
        }));
        Check(!result.Found);
        Equal(MatchFailureReason.TimedOut, result.FailureReason);
        Check(published?.IsFrozen == true);
        Check(ReferenceEquals(published, result.Snapshot), "A completed capture remains useful when matching times out.");
        Equal(bounds, result.CaptureBounds);
    }

    private static async Task ImageTestCaptureFailure()
    {
        using var fixture = new ImageExecutionFixture();
        using var matcher = new ImageMatchService(fixture.Capture, new PreviewFailingFrameSource());
        var notices = 0;
        var result = await Task.Run(() => matcher.TestMatch(fixture.Action, default, (_, _) => Interlocked.Increment(ref notices)));
        Equal(MatchFailureReason.CaptureFailed, result.FailureReason);
        Equal(0, notices);
        Check(result.Snapshot == null);
        Equal(DrawingRectangle.Empty, result.CaptureBounds);
    }

    private static Task ImagePreviewCompletesWithoutCancellation()
    {
        var window = new MatchResultWindow();
        var cancellations = 0;
        window.CancellationRequested += (_, _) => cancellations++;
        try
        {
            var snapshot = ImagePreviewSnapshot(17);
            var bounds = new DrawingRectangle(-200, 30, snapshot.PixelWidth, snapshot.PixelHeight);
            Check(window.IsMatching);
            Check(!window.IsVisible, "This lifecycle test must not display a real desktop overlay.");
            window.ShowCapturedFrame(snapshot, bounds);
            Check(window.IsMatching);
            Check(ReferenceEquals(snapshot, ((System.Windows.Controls.Image)window.FindName("BackgroundImage")).Source));
            window.ShowResult(new MatchResult { FailureReason = MatchFailureReason.BelowThreshold, Snapshot = snapshot, CaptureBounds = bounds });
            Check(!window.IsMatching);
            Check(!window.IsClosed);
            Equal(0, cancellations);
            Equal(Visibility.Collapsed, ((Button)window.FindName("StopTestButton")).Visibility);
            window.Close();
            Check(window.IsClosed);
            Equal(0, cancellations);
        }
        finally { if (!window.IsClosed) window.Close(); }
        return Task.CompletedTask;
    }

    private static Task ImagePreviewCancelsOnce()
    {
        foreach (var stopButton in new[] { false, true })
        {
            var window = new MatchResultWindow();
            var cancellations = 0;
            window.CancellationRequested += (_, _) =>
            {
                cancellations++;
                Check(window.IsClosed, "Late callbacks must observe the closed state before cancellation is delivered.");
            };
            try
            {
                if (stopButton) ((Button)window.FindName("StopTestButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                else window.Close();
                Check(window.IsClosed);
                Check(!window.IsMatching);
                Equal(1, cancellations);
                window.ShowResult(new MatchResult { FailureReason = MatchFailureReason.Cancelled });
                Equal(1, cancellations);
                Check(!window.IsVisible);
            }
            finally { if (!window.IsClosed) window.Close(); }
        }
        return Task.CompletedTask;
    }

    private static async Task ImagePreviewIgnoresLateUpdates()
    {
        var window = new MatchResultWindow();
        var cancellations = 0;
        window.CancellationRequested += (_, _) => cancellations++;
        try
        {
            var first = ImagePreviewSnapshot(23);
            var later = ImagePreviewSnapshot(61);
            var bounds = new DrawingRectangle(-200, 30, first.PixelWidth, first.PixelHeight);
            window.ShowCapturedFrame(first, bounds);
            var background = (System.Windows.Controls.Image)window.FindName("BackgroundImage");
            var queuedFrame = window.Dispatcher.InvokeAsync(() => window.ShowCapturedFrame(later, bounds), DispatcherPriority.Background);
            var queuedResult = window.Dispatcher.InvokeAsync(() => window.ShowResult(new MatchResult
            {
                Found = true, Snapshot = later, CaptureBounds = bounds, X = -195, Y = 35, Width = 4, Height = 4
            }), DispatcherPriority.Background);
            window.Close();
            var closedSource = background.Source;
            await queuedFrame;
            await queuedResult;
            Check(window.IsClosed && !window.IsMatching && !window.IsVisible);
            Check(ReferenceEquals(closedSource, background.Source), "An already queued result must not mutate a closed preview.");
            Equal(1, cancellations);
        }
        finally { if (!window.IsClosed) window.Close(); }
    }

    private static BitmapSource ImagePreviewSnapshot(byte value)
    {
        const int width = 16, height = 12;
        var pixels = Enumerable.Repeat(value, width * height * 3).ToArray();
        var snapshot = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, pixels, width * 3);
        snapshot.Freeze();
        return snapshot;
    }

    private sealed class PreviewFailingFrameSource : IImageFrameSource
    {
        public CapturedImageFrame Capture(ImageSearchScope scope) => throw new InvalidOperationException("Synthetic capture failure.");
    }
}
