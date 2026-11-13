using System.Diagnostics;
using Ming_AutoClicker.Helpers;
using Ming_AutoClicker.Models;
using Ming_AutoClicker.Services;

namespace Ming_AutoClicker.Tests;

internal static partial class Program
{
    private static void AddRecordingPlaybackTests(List<(string Name, Func<Task> Run)> tests)
    {
        tests.Add(("Recording playback snapshots input and counts enabled actions per loop", RecordingPlaybackProgress));
        tests.Add(("Recording playback rejects empty and invalid options before input", RecordingPlaybackValidation));
        tests.Add(("Recording stop prevents the next zero-delay action and preserves release metadata", RecordingPlaybackStop));
        tests.Add(("Paused recording playback sends no subsequent input and can stop", RecordingPlaybackPausedStop));
        tests.Add(("Recording playback pause does not consume action delays", RecordingPlaybackPauseDelay));
        tests.Add(("Infinite zero-delay recording playback remains cancellable", RecordingPlaybackInfinite));
        tests.Add(("Recording cleanup attempts every release even after a failure", RecordingPlaybackCleanupFailure));
        tests.Add(("Recording send exceptions still clean up and allow a later run", RecordingPlaybackSendFailure));
        tests.Add(("Recording playback rejects overlapping runs and disposal cancels paused runs", RecordingPlaybackLifecycle));
        tests.Add(("Recording cancellation before start sends no input", RecordingPlaybackPreCancelled));
    }

    private static RecordedAction PlaybackKey(RecordedActionType type, uint key = 0x41, long delay = 0) =>
        new() { Type = type, VirtualKey = key, DelayBeforeMs = delay };

    private static PlaybackService CreateTestPlayback(FakeInputSender sender) =>
        new(sender, () => new DesktopBounds { X = -1920, Y = 0, Width = 3840, Height = 1080 });

    private static async Task RecordingPlaybackProgress()
    {
        var sender = new FakeInputSender();
        using var playback = CreateTestPlayback(sender);
        var recording = new Recording
        {
            Actions = new()
            {
                PlaybackKey(RecordedActionType.KeyDown),
                new RecordedAction { Type = RecordedActionType.MouseMove, IsEnabled = false, DelayBeforeMs = 80 },
                PlaybackKey(RecordedActionType.KeyUp)
            }
        };
        var options = new PlaybackOptions { LoopCount = 2 };
        var updates = new List<PlaybackProgress>();
        var clock = Stopwatch.StartNew();
        var result = await playback.StartPlaybackAsync(recording, options, progress =>
        {
            updates.Add(progress);
            if (progress is { CurrentLoop: 1, CompletedActions: 1 })
            {
                recording.Actions.Clear();
                options.LoopCount = 10;
                options.SpeedFactor = 5;
            }
        });
        Equal(PlaybackOutcome.Completed, result);
        Equal(4, sender.Batches.Count);
        Check(clock.ElapsedMilliseconds >= 120, "Disabled actions must retain their recorded delays.");
        Sequence(new[]
        {
            new PlaybackProgress(0, 2, 1), new PlaybackProgress(1, 2, 1), new PlaybackProgress(2, 2, 1),
            new PlaybackProgress(0, 2, 2), new PlaybackProgress(1, 2, 2), new PlaybackProgress(2, 2, 2)
        }, updates);
        Check(!playback.IsRunning && !playback.IsPaused);
    }

    private static async Task RecordingPlaybackValidation()
    {
        var sender = new FakeInputSender();
        using var playback = CreateTestPlayback(sender);
        await Throws<ArgumentException>(() => playback.StartPlaybackAsync(new Recording(), new PlaybackOptions { LoopCount = -1 }));
        var disabled = new Recording { Actions = new() { new RecordedAction { IsEnabled = false } } };
        await Throws<ArgumentException>(() => playback.StartPlaybackAsync(disabled, new PlaybackOptions { LoopCount = -1 }));
        var recording = new Recording { Actions = new() { PlaybackKey(RecordedActionType.KeyUp) } };
        foreach (var speed in new[] { double.NaN, double.PositiveInfinity, 0, 6 })
            await Throws<ArgumentOutOfRangeException>(() => playback.StartPlaybackAsync(recording, new PlaybackOptions { SpeedFactor = speed }));
        await Throws<ArgumentOutOfRangeException>(() => playback.StartPlaybackAsync(recording, new PlaybackOptions { LoopCount = 0 }));
        await Throws<ArgumentOutOfRangeException>(() => playback.StartPlaybackAsync(recording, new PlaybackOptions { LoopInterval = -1 }));
        Equal(0, sender.Batches.Count);
        Check(!playback.IsRunning);
    }

    private static async Task RecordingPlaybackStop()
    {
        var sender = new FakeInputSender();
        using var playback = CreateTestPlayback(sender);
        var key = PlaybackKey(RecordedActionType.KeyDown, 0xA3);
        key.ScanCode = 0x1D;
        key.IsExtendedKey = true;
        var recording = new Recording { Actions = new() { key, PlaybackKey(RecordedActionType.KeyDown, 0x42) } };
        var progress = new List<PlaybackProgress>();
        Equal(PlaybackOutcome.Stopped, await playback.StartPlaybackAsync(recording, new PlaybackOptions(), update =>
        {
            progress.Add(update);
            if (update.CompletedActions == 1) playback.StopPlayback();
        }));
        Equal(2, sender.Batches.Count);
        var release = sender.Batches[1].Single();
        Check(IsKeyUp(release));
        Equal((ushort)0x1D, release.U.ki.wScan);
        Equal((ushort)0, release.U.ki.wVk);
        Check((release.U.ki.dwFlags & Win32Api.KEYEVENTF_EXTENDEDKEY) != 0);
        Check((release.U.ki.dwFlags & Win32Api.KEYEVENTF_SCANCODE) != 0);
        Equal(Win32Api.InputMarker, release.U.ki.dwExtraInfo);
        Equal(1, progress.Last().CompletedActions);
        Check(!playback.IsRunning);
    }

    private static async Task RecordingPlaybackPausedStop()
    {
        var sender = new FakeInputSender();
        using var playback = CreateTestPlayback(sender);
        var paused = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var recording = new Recording
        {
            Actions = new() { PlaybackKey(RecordedActionType.KeyDown), PlaybackKey(RecordedActionType.KeyDown, 0x42) }
        };
        var run = playback.StartPlaybackAsync(recording, new PlaybackOptions(), update =>
        {
            if (update.CompletedActions != 1) return;
            playback.PausePlayback();
            paused.TrySetResult(true);
        });
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Check(playback.IsPaused);
        await Task.Delay(60);
        Equal(1, sender.Batches.Count);
        playback.StopPlayback();
        Equal(PlaybackOutcome.Stopped, await run);
        Equal(2, sender.Batches.Count);
        Check(IsKeyUp(sender.Batches[1].Single()));
        Check(!playback.IsRunning && !playback.IsPaused);
    }

    private static async Task RecordingPlaybackPauseDelay()
    {
        var sender = new FakeInputSender();
        using var playback = CreateTestPlayback(sender);
        var recording = new Recording { Actions = new() { PlaybackKey(RecordedActionType.KeyUp, delay: 160) } };
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = playback.StartPlaybackAsync(recording, new PlaybackOptions(), update =>
        {
            if (update.CompletedActions != 0) return;
            playback.PausePlayback();
            started.TrySetResult(true);
        });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(200);
        Equal(0, sender.Batches.Count);
        var resumed = Stopwatch.StartNew();
        playback.ResumePlayback();
        Equal(PlaybackOutcome.Completed, await run);
        Check(resumed.ElapsedMilliseconds >= 100, "The paused interval consumed the pending action delay.");
        Equal(1, sender.Batches.Count);
    }

    private static async Task RecordingPlaybackInfinite()
    {
        var sender = new FakeInputSender();
        using var playback = CreateTestPlayback(sender);
        var recording = new Recording { Actions = new() { PlaybackKey(RecordedActionType.KeyUp) } };
        var run = playback.StartPlaybackAsync(recording, new PlaybackOptions { LoopCount = -1 }, update =>
        {
            if (update is { CurrentLoop: 5, CompletedActions: 1 }) playback.StopPlayback();
        });
        Equal(PlaybackOutcome.Stopped, await run.WaitAsync(TimeSpan.FromSeconds(2)));
        Equal(5, sender.Batches.Count);
    }

    private static async Task RecordingPlaybackCleanupFailure()
    {
        var sender = new FakeInputSender { OnSend = (index, _) => index == 3 ? 0u : 1u };
        using var playback = CreateTestPlayback(sender);
        var recording = new Recording
        {
            Actions = new()
            {
                PlaybackKey(RecordedActionType.KeyDown), PlaybackKey(RecordedActionType.KeyDown, 0x42),
                new RecordedAction { Type = RecordedActionType.MouseDown, Button = RecordedMouseButton.Left }
            }
        };
        await Throws<AggregateException>(() => playback.StartPlaybackAsync(recording, new PlaybackOptions(), update =>
        {
            if (update.CompletedActions == 3) playback.StopPlayback();
        }));
        Equal(6, sender.Batches.Count);
        Check(IsKeyUp(sender.Batches[3].Single()));
        Check(IsKeyUp(sender.Batches[4].Single()));
        Equal(Win32Api.MOUSEEVENTF_LEFTUP, sender.Batches[5].Single().U.mi.dwFlags);
        Check(!playback.IsRunning && !playback.IsPaused);
        Equal(PlaybackOutcome.Completed, await playback.StartPlaybackAsync(
            new Recording { Actions = new() { PlaybackKey(RecordedActionType.KeyUp, 0x43) } }, new PlaybackOptions()));
        Equal(7, sender.Batches.Count);
    }

    private static async Task RecordingPlaybackSendFailure()
    {
        var sender = new FakeInputSender
        {
            OnSend = (index, _) => index == 1 ? throw new InvalidOperationException("Synthetic native failure") : 1u
        };
        using var playback = CreateTestPlayback(sender);
        var recording = new Recording
        {
            Actions = new()
            {
                PlaybackKey(RecordedActionType.KeyDown), PlaybackKey(RecordedActionType.KeyDown, 0x42),
                PlaybackKey(RecordedActionType.KeyDown, 0x43)
            }
        };
        await Throws<InvalidOperationException>(() => playback.StartPlaybackAsync(recording, new PlaybackOptions()));
        Equal(4, sender.Batches.Count);
        Check(sender.Batches.Skip(2).All(batch => IsKeyUp(batch.Single())));
        Check(!playback.IsRunning);
        Equal(PlaybackOutcome.Completed, await playback.StartPlaybackAsync(
            new Recording { Actions = new() { PlaybackKey(RecordedActionType.KeyUp, 0x43) } }, new PlaybackOptions()));
        Equal(5, sender.Batches.Count);
    }

    private static async Task RecordingPlaybackLifecycle()
    {
        var sender = new FakeInputSender();
        var playback = CreateTestPlayback(sender);
        var recording = new Recording { Actions = new() { PlaybackKey(RecordedActionType.KeyUp, delay: 500) } };
        var run = playback.StartPlaybackAsync(recording, new PlaybackOptions());
        playback.PausePlayback();
        await Throws<InvalidOperationException>(() => playback.StartPlaybackAsync(recording, new PlaybackOptions()));
        playback.Dispose();
        Equal(PlaybackOutcome.Stopped, await run);
        Equal(0, sender.Batches.Count);
        Check(!playback.IsRunning && !playback.IsPaused);
        await Throws<ObjectDisposedException>(() => playback.StartPlaybackAsync(recording, new PlaybackOptions()));
        playback.Dispose();
    }

    private static async Task RecordingPlaybackPreCancelled()
    {
        var sender = new FakeInputSender();
        using var playback = CreateTestPlayback(sender);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var recording = new Recording { Actions = new() { PlaybackKey(RecordedActionType.KeyDown) } };
        Equal(PlaybackOutcome.Stopped, await playback.StartPlaybackAsync(recording, new PlaybackOptions(), cancellationToken: cancellation.Token));
        Equal(0, sender.Batches.Count);
        Check(!playback.IsRunning);
    }
}
