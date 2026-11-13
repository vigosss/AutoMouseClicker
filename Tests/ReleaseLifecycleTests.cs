using System.Diagnostics;
using System.IO;
using Ming_AutoClicker.Helpers;
using Ming_AutoClicker.Models;
using Ming_AutoClicker.Services;
using Ming_AutoClicker.ViewModels;

namespace Ming_AutoClicker.Tests;

internal static partial class Program
{
    private static void AddReleaseLifecycleTests(List<(string Name, Func<Task> Run)> tests)
    {
        tests.Add(("Exit preparation waits for real playback key and mouse releases", ExitWaitsForInputCleanup));
        tests.Add(("Exit cleanup timeout allows recovery and a later exit attempt", ExitCleanupTimeoutRecovery));
        tests.Add(("Incomplete macro loads preserve screenshots during save and disposal", IncompleteMacroLoadPreservesScreenshots));
    }

    private static async Task ExitWaitsForInputCleanup()
    {
        var pressed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var allowRelease = new ManualResetEventSlim();
        var sender = new FakeInputSender
        {
            OnSend = (_, inputs) =>
            {
                var input = inputs.Single();
                if (input.type == Win32Api.INPUT_MOUSE && (input.U.mi.dwFlags & Win32Api.MOUSEEVENTF_LEFTDOWN) != 0)
                    pressed.TrySetResult();
                if (input.type == Win32Api.INPUT_KEYBOARD && (input.U.ki.dwFlags & Win32Api.KEYEVENTF_KEYUP) != 0)
                {
                    releasing.TrySetResult();
                    Check(allowRelease.Wait(TimeSpan.FromSeconds(5)), "Test cleanup gate timed out");
                }
                return (uint)inputs.Length;
            }
        };
        using var fixture = new RecordingPageFixture(CreateTestPlayback(sender));
        var vm = fixture.ViewModel;
        var item = vm.Recordings[0];
        item.Model.Actions = new()
        {
            PlaybackKey(RecordedActionType.KeyDown),
            new() { Type = RecordedActionType.MouseDown, Button = RecordedMouseButton.Left, X = 10, Y = 10 },
            PlaybackKey(RecordedActionType.KeyDown, 0x42, 60_000)
        };
        item.Refresh();
        vm.SelectedRecording = item;
        var playback = vm.StartPlaybackAsync();
        Task? preparation = null;
        try
        {
            await pressed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            preparation = vm.PrepareForExitAsync(TimeSpan.FromSeconds(3));
            await releasing.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Check(!preparation.IsCompleted, "Shutdown must wait until input releases finish");
            Check(!vm.StartCommand.CanExecute(null) && !vm.StartPlaybackCommand.CanExecute(null));
            vm.StartCommand.Execute(null);
            vm.Toggle();
            allowRelease.Set();
            await preparation;
            await playback;
            var inputs = sender.Batches.SelectMany(batch => batch).ToArray();
            Equal(4, inputs.Length);
            Equal(1, inputs.Count(input => input.type == Win32Api.INPUT_KEYBOARD &&
                (input.U.ki.dwFlags & Win32Api.KEYEVENTF_KEYUP) != 0));
            Equal(1, inputs.Count(input => input.type == Win32Api.INPUT_MOUSE &&
                (input.U.mi.dwFlags & Win32Api.MOUSEEVENTF_LEFTUP) != 0));
            Check(!vm.StartCommand.CanExecute(null), "Successful preparation must prevent a new session before shutdown");
            vm.CancelExitPreparation();
            Check(vm.StartCommand.CanExecute(null), "A failed update must be able to cancel successful exit preparation");
        }
        finally
        {
            allowRelease.Set();
            vm.StopCommand.Execute(null);
            if (preparation != null) try { await preparation; } catch { }
            await playback.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    private static async Task ExitCleanupTimeoutRecovery()
    {
        using var fixture = new RecordingPageFixture();
        var vm = fixture.ViewModel;
        vm.SelectedRecording = vm.Recordings[0];
        vm.StartCommand.Execute(null);
        var run = fixture.Playback.Runs.Single();
        var clock = Stopwatch.StartNew();
        await Throws<TimeoutException>(() => vm.PrepareForExitAsync(TimeSpan.FromMilliseconds(50)));
        Check(clock.Elapsed < TimeSpan.FromSeconds(2), "Exit must not wait indefinitely for cleanup");
        Check(fixture.Playback.StopRequested && vm.IsStopping);
        vm.Toggle();
        Equal(1, fixture.Playback.Runs.Count);
        run.Finish(PlaybackOutcome.Stopped);
        await WaitRecordingIdle(vm);
        Check(vm.StartCommand.CanExecute(null), "A timeout must release the exit gate after cleanup finishes");

        vm.StartCommand.Execute(null);
        var retry = vm.PrepareForExitAsync(TimeSpan.FromSeconds(2));
        Check(!retry.IsCompleted);
        fixture.Playback.Runs.Last().Finish(PlaybackOutcome.Stopped);
        await retry;
        await DrainRecordingDispatcher();
        Check(!vm.StartCommand.CanExecute(null));
        vm.CancelExitPreparation();
        Check(vm.StartCommand.CanExecute(null));
    }

    private static Task IncompleteMacroLoadPreservesScreenshots()
    {
        // Every writable path is beneath a fresh temporary directory. No window,
        // hook registration, screen capture or real input is started by this test.
        var directory = Path.Combine(Path.GetTempPath(), "MingAutoClicker-ReleaseTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var storage = new MacroStorageService(directory);
            using var screenshots = new ScreenCaptureService(Path.Combine(directory, "screenshots"));
            using var matching = new ImageMatchService(screenshots);
            var sender = new FakeInputSender();
            using var executor = new MacroExecutor(matching, screenshots, new MacroInputService(sender));
            using var hotkeys = new HotkeyService();
            var healthy = new MacroProfile { Name = "Healthy" };
            healthy.Actions.Add(new FindImageAction { ImagePath = "healthy.png" });
            storage.Save(healthy);
            var damaged = new MacroProfile { Name = "Damaged" };
            damaged.Actions.Add(new FindImageAction { ImagePath = "damaged.png" });
            var damagedPath = storage.Save(damaged);
            var originalJson = File.ReadAllText(damagedPath);
            File.WriteAllText(damagedPath, "{\"Name\":\"Damaged\",\"Actions\":[");
            var healthyImage = Path.Combine(directory, "screenshots", "healthy.png");
            var damagedImage = Path.Combine(directory, "screenshots", "damaged.png");
            var orphanImage = Path.Combine(directory, "screenshots", "orphan.png");
            foreach (var path in new[] { healthyImage, damagedImage, orphanImage }) File.WriteAllBytes(path, new byte[] { 1 });

            MainViewModel CreateViewModel() => new(storage, screenshots, matching, executor, hotkeys,
                new AppSettingsService(), new AppSettings(), new AutoClickService(),
                new RecordingStorageService(Path.Combine(directory, "recordings.json")), new GlobalHookService(), CreateTestPlayback(sender));
            using (var vm = CreateViewModel())
            {
                Equal(1, vm.Macros.Count);
                Check(!storage.LastLoadCompleted);
                vm.SaveAll();
                Check(File.Exists(damagedImage) && File.Exists(orphanImage), "An incomplete list cannot prove an image is unused");
            }
            Check(File.Exists(healthyImage) && File.Exists(damagedImage) && File.Exists(orphanImage),
                "Disposal must preserve templates belonging to an unreadable macro");

            File.WriteAllText(damagedPath, originalJson);
            using (var recovered = CreateViewModel())
            {
                Equal(2, recovered.Macros.Count);
                Check(storage.LastLoadCompleted);
                recovered.SaveAll();
                Check(File.Exists(healthyImage) && File.Exists(damagedImage));
                Check(!File.Exists(orphanImage), "A complete reload should restore ordinary orphan cleanup");
            }
            Equal(0, sender.Batches.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
        return Task.CompletedTask;
    }
}
