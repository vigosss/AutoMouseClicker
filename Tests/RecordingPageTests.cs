using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MahApps.Metro.IconPacks;
using Ming_AutoClicker.Models;
using Ming_AutoClicker.Services;
using Ming_AutoClicker.ViewModels;
using Ming_AutoClicker.Views.Recordings;

namespace Ming_AutoClicker.Tests;

internal static partial class Program
{
    private static void AddRecordingPageTests(List<(string Name, Func<Task> Run)> tests)
    {
        tests.Add(("Recording selection resets completed progress and ignores an earlier session", RecordingSelectionProgress));
        tests.Add(("Recording stop keeps selection locked until playback cleanup finishes", RecordingStoppingSelection));
        tests.Add(("Recording elapsed time excludes pauses and resets before restart", RecordingSessionClock));
        tests.Add(("Recording capture count stays separate from playback progress", RecordingCaptureProgress));
        tests.Add(("Recording validation rejects empty actions and invalid playback settings", RecordingOptionValidation));
        tests.Add(("Recording playback failure is visible and allows a later run", RecordingFailureRecovery));
        tests.Add(("Recording view model retains real playback progress through loops and disabled actions", RecordingRealPlaybackProgress));
        tests.Add(("Background recording progress survives completion, stop and failure dispatch ordering", RecordingBackgroundProgress));
        tests.Add(("Recording primary action starts and stops finite and infinite playback safely", RecordingPrimaryAction));
        tests.Add(("Recording speed slider quantizes values and locks during playback", RecordingSpeedSlider));
        tests.Add(("Recording footer and menus render without layout jumps in both languages", RecordingPageRendering));
    }

    private static async Task RecordingSelectionProgress()
    {
        using var fixture = new RecordingPageFixture();
        var vm = fixture.ViewModel;
        var first = vm.Recordings[0];
        var second = vm.Recordings[1];
        vm.SelectedRecording = first;
        vm.StartCommand.Execute(null);
        var earlierRun = fixture.Playback.Runs.Single();
        earlierRun.Report(22, 22, 1);
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(470));
        earlierRun.Finish(PlaybackOutcome.Completed);
        await WaitRecordingIdle(vm);
        Equal(RecordingSessionResult.Completed, vm.LastResult);
        Equal(22, vm.CompletedActionCount);
        Equal(22, vm.TotalActionCount);

        vm.SelectedRecording = second;
        Equal(0, vm.CompletedActionCount);
        Equal(159, vm.TotalActionCount);
        Equal("00:00:00.0", vm.ElapsedTime);
        Equal(RecordingSessionResult.None, vm.LastResult);
        earlierRun.Report(22, 22, 1);
        await DrainRecordingDispatcher();
        Equal(0, vm.CompletedActionCount);
        Equal(159, vm.TotalActionCount);

        vm.StartCommand.Execute(null);
        var currentRun = fixture.Playback.Runs.Last();
        currentRun.Report(3, 159, 1);
        earlierRun.Report(22, 22, 5);
        await DrainRecordingDispatcher();
        Equal(3, vm.CompletedActionCount);
        Equal(159, vm.TotalActionCount);
        Equal(1, vm.CurrentLoop);
        currentRun.Finish(PlaybackOutcome.Completed);
        await WaitRecordingIdle(vm);

        vm.ClearSelectionCommand.Execute(null);
        Check(vm.SelectedRecording == null);
        Equal(0, vm.CompletedActionCount);
        Equal("00:00:00.0", vm.ElapsedTime);
        vm.SelectedRecording = first;
        Equal(0, vm.CompletedActionCount);
        Equal(22, vm.TotalActionCount);
    }

    private static async Task RecordingStoppingSelection()
    {
        using var fixture = new RecordingPageFixture();
        var vm = fixture.ViewModel;
        var first = vm.Recordings[0];
        var second = vm.Recordings[1];
        vm.SelectedRecording = first;
        vm.StartCommand.Execute(null);
        var run = fixture.Playback.Runs.Single();
        run.Report(7, 22, 1);
        vm.SelectedRecording = second;
        Check(ReferenceEquals(first, vm.SelectedRecording), "Keyboard or programmatic selection must not switch a running recording");
        Check(!vm.ClearSelectionCommand.CanExecute(null));
        Check(!vm.StartCommand.CanExecute(null));
        vm.PauseCommand.Execute(null);
        Equal(RecordingState.PlayPaused, vm.State);
        vm.SelectedRecording = second;
        Check(ReferenceEquals(first, vm.SelectedRecording));

        vm.StopCommand.Execute(null);
        Equal(RecordingState.Stopping, vm.State);
        Check(!vm.IsIdle);
        Check(fixture.Playback.StopRequested);
        vm.SelectedRecording = second;
        Check(ReferenceEquals(first, vm.SelectedRecording));
        Check(!vm.StartCommand.CanExecute(null));
        Check(!vm.PauseCommand.CanExecute(null));
        vm.Toggle();
        Equal(1, fixture.Playback.Runs.Count);

        run.Finish(PlaybackOutcome.Stopped);
        await WaitRecordingIdle(vm);
        Equal(RecordingSessionResult.Stopped, vm.LastResult);
        Equal(7, vm.CompletedActionCount);
        vm.SelectedRecording = second;
        Check(ReferenceEquals(second, vm.SelectedRecording));
        Equal(0, vm.CompletedActionCount);
    }

    private static async Task RecordingSessionClock()
    {
        using var fixture = new RecordingPageFixture();
        var vm = fixture.ViewModel;
        vm.SelectedRecording = vm.Recordings[0];
        vm.StartCommand.Execute(null);
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(1234));
        vm.PauseCommand.Execute(null);
        Equal("00:00:01.2", vm.ElapsedTime);
        fixture.Clock.Advance(TimeSpan.FromSeconds(90));
        await Task.Delay(150);
        Equal("00:00:01.2", vm.ElapsedTime);
        vm.PauseCommand.Execute(null);
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(456));
        fixture.Playback.Runs.Last().Finish(PlaybackOutcome.Completed);
        await WaitRecordingIdle(vm);
        Equal("00:00:01.6", vm.ElapsedTime);

        vm.StartCommand.Execute(null);
        Equal("00:00:00.0", vm.ElapsedTime);
        Equal(0, vm.CompletedActionCount);
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(350));
        fixture.Playback.Runs.Last().Finish(PlaybackOutcome.Completed);
        await WaitRecordingIdle(vm);
        Equal("00:00:00.3", vm.ElapsedTime);
    }

    private static Task RecordingCaptureProgress()
    {
        using var fixture = new RecordingPageFixture();
        var vm = fixture.ViewModel;
        Check(vm.SelectedRecording == null);
        vm.StartCommand.Execute(null);
        Equal(RecordingState.Recording, vm.State);
        fixture.Capture.Report(5);
        Equal(5, vm.RecordedActionCount);
        Equal(0, vm.CompletedActionCount);
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(840));
        vm.PauseCommand.Execute(null);
        Equal(RecordingState.RecordPaused, vm.State);
        Equal("00:00:00.8", vm.ElapsedTime);
        fixture.Clock.Advance(TimeSpan.FromSeconds(10));
        vm.PauseCommand.Execute(null);
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(170));
        fixture.Capture.Actions = CreateRecordingActions(5);
        vm.StopCommand.Execute(null);
        Check(vm.IsIdle);
        Check(vm.SelectedRecording != null, "A saved recording should become selected");
        Equal(5, vm.SelectedRecording!.Model.ActionCount);
        Equal(0, vm.CompletedActionCount);
        Equal(5, vm.TotalActionCount);
        Equal("00:00:00.0", vm.ElapsedTime);
        fixture.Capture.Report(99);
        Equal(0, vm.CompletedActionCount);
        Equal(5, vm.TotalActionCount);
        return Task.CompletedTask;
    }

    private static async Task RecordingOptionValidation()
    {
        using var fixture = new RecordingPageFixture();
        var vm = fixture.ViewModel;
        var empty = new RecordingItemViewModel(new Recording { Name = "Empty" }, vm);
        var disabled = new RecordingItemViewModel(new Recording
        {
            Name = "Disabled",
            Actions = new() { new RecordedAction { IsEnabled = false, Type = RecordedActionType.MouseMove } }
        }, vm);
        vm.Recordings.Add(empty);
        vm.Recordings.Add(disabled);
        foreach (var item in new[] { empty, disabled })
        {
            vm.SelectedRecording = item;
            Check(!vm.StartCommand.CanExecute(null), "A recording without enabled actions must not start");
            Equal(0, vm.TotalActionCount);
        }

        vm.SelectedRecording = vm.Recordings[0];
        foreach (var invalid in new[] { "", "text", "NaN", "Infinity", "0", "0.4", "5.1" })
        {
            vm.SpeedText = invalid;
            Check(!vm.StartCommand.CanExecute(null), $"Speed '{invalid}' must block playback");
            Check(!string.IsNullOrEmpty(((IDataErrorInfo)vm)[nameof(vm.SpeedText)]));
        }
        vm.SpeedText = "1.5";
        foreach (var invalid in new[] { "", "0", "-1", "1.5", "2147483648" })
        {
            vm.LoopCountText = invalid;
            Check(!vm.StartCommand.CanExecute(null), $"Loop count '{invalid}' must block playback");
        }
        vm.LoopCountText = "2";
        foreach (var invalid in new[] { "", "-1", "1.5", "2147483648" })
        {
            vm.LoopIntervalText = invalid;
            Check(!vm.StartCommand.CanExecute(null), $"Loop interval '{invalid}' must block playback");
        }
        vm.LoopIntervalText = "250";
        Check(vm.StartCommand.CanExecute(null));
        vm.LoopCountText = "invalid";
        vm.IsInfiniteLoop = true;
        Check(vm.StartCommand.CanExecute(null), "An inactive finite loop field must not block infinite playback");
        vm.IsInfiniteLoop = false;
        Check(!vm.StartCommand.CanExecute(null));
        vm.LoopCountText = "2";
        vm.IsInfiniteLoop = true;
        vm.StartCommand.Execute(null);
        var run = fixture.Playback.Runs.Single();
        Equal(1.5, run.Options.SpeedFactor);
        Equal(-1, run.Options.LoopCount);
        Equal(250, run.Options.LoopInterval);
        vm.SpeedText = "4";
        vm.IsInfiniteLoop = false;
        Equal(1.5, run.Options.SpeedFactor);
        Equal(-1, run.Options.LoopCount);
        run.Report(22, 22, 12);
        Equal(12, vm.CurrentLoop);
        vm.StopCommand.Execute(null);
        run.Finish(PlaybackOutcome.Stopped);
        await WaitRecordingIdle(vm);
    }

    private static async Task RecordingFailureRecovery()
    {
        using var fixture = new RecordingPageFixture();
        var vm = fixture.ViewModel;
        vm.SelectedRecording = vm.Recordings[0];
        vm.StartCommand.Execute(null);
        fixture.Playback.Runs.Last().Fail(new InvalidOperationException("Synthetic input failure"));
        await WaitRecordingIdle(vm);
        Equal(RecordingSessionResult.Failed, vm.LastResult);
        Check(!string.IsNullOrWhiteSpace(vm.ErrorMessage));
        Check(vm.StartCommand.CanExecute(null));
        vm.StartCommand.Execute(null);
        Check(string.IsNullOrEmpty(vm.ErrorMessage));
        fixture.Playback.Runs.Last().Finish(PlaybackOutcome.Completed);
        await WaitRecordingIdle(vm);
        Equal(RecordingSessionResult.Completed, vm.LastResult);
    }

    private static async Task RecordingRealPlaybackProgress()
    {
        var sender = new FakeInputSender();
        using var fixture = new RecordingPageFixture(CreateTestPlayback(sender));
        var vm = fixture.ViewModel;
        var recording = vm.Recordings[0];
        recording.Model.Actions = Enumerable.Range(0, 40).SelectMany(_ => new[]
        {
            new RecordedAction { Type = RecordedActionType.KeyDown, VirtualKey = 0x41 },
            new RecordedAction { Type = RecordedActionType.MouseMove, IsEnabled = false },
            new RecordedAction { Type = RecordedActionType.KeyUp, VirtualKey = 0x41 }
        }).ToList();
        vm.SelectedRecording = recording;
        vm.LoopCountText = "2";
        vm.LoopIntervalText = "250";
        var firstLoopCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.PropertyChanged += (_, _) =>
        {
            if (vm.CurrentLoop == 1 && vm.CompletedActionCount == 80)
                firstLoopCompleted.TrySetResult(true);
        };

        var run = vm.StartPlaybackAsync();
        await firstLoopCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Equal(RecordingState.Playing, vm.State);
        Equal(1, vm.CurrentLoop);
        Equal(80, vm.CompletedActionCount);
        Equal(80, vm.TotalActionCount);
        Check(!vm.StartCommand.CanExecute(null), "Waiting between loops still belongs to the active session");
        await run;
        Equal(RecordingSessionResult.Completed, vm.LastResult);
        Equal(RecordingState.Idle, vm.State);
        Equal(2, vm.CurrentLoop);
        Equal(80, vm.CompletedActionCount);
        Equal(80, vm.TotalActionCount);
        Equal(160, sender.Batches.Count);
        Check(vm.StatusText.Contains("80"), "The completed status must retain the last action progress");
    }

    private static async Task RecordingBackgroundProgress()
    {
        using var fixture = new RecordingPageFixture();
        var vm = fixture.ViewModel;
        vm.SelectedRecording = vm.Recordings[1];
        foreach (var (result, completed) in new[]
        {
            (RecordingSessionResult.Completed, 159),
            (RecordingSessionResult.Stopped, 117),
            (RecordingSessionResult.Failed, 31)
        })
        {
            var execution = vm.StartPlaybackAsync();
            var run = fixture.Playback.Runs.Last();
            // Queue worker progress without allowing dispatcher callbacks to run. A user stop or
            // await continuation can then overtake a low-priority/coalesced UI progress refresh.
            using (Dispatcher.CurrentDispatcher.DisableProcessing())
            {
                Task.Run(() =>
                {
                    for (var count = 1; count <= completed; count++) run.Report(count, 159, 1);
                }).GetAwaiter().GetResult();
                if (result == RecordingSessionResult.Stopped) vm.StopCommand.Execute(null);
                if (result == RecordingSessionResult.Failed) run.Fail(new InvalidOperationException("Synthetic late failure"));
                else run.Finish(result == RecordingSessionResult.Completed ? PlaybackOutcome.Completed : PlaybackOutcome.Stopped);
            }
            await execution;
            Equal(result, vm.LastResult);
            Equal(completed, vm.CompletedActionCount);
            Equal(159, vm.TotalActionCount);
            await DrainRecordingDispatcher();
            Equal(result, vm.LastResult);
            Equal(completed, vm.CompletedActionCount);
        }

        var previous = fixture.Playback.Runs.Last();
        vm.SelectedRecording = vm.Recordings[0];
        var nextExecution = vm.StartPlaybackAsync();
        var next = fixture.Playback.Runs.Last();
        using (Dispatcher.CurrentDispatcher.DisableProcessing())
        {
            Task.Run(() =>
            {
                next.Report(4, 22, 1);
                previous.Report(159, 159, 7);
            }).GetAwaiter().GetResult();
            vm.StopCommand.Execute(null);
            next.Finish(PlaybackOutcome.Stopped);
        }
        await nextExecution;
        await DrainRecordingDispatcher();
        Equal(RecordingSessionResult.Stopped, vm.LastResult);
        Equal(4, vm.CompletedActionCount);
        Equal(22, vm.TotalActionCount);
        Equal(1, vm.CurrentLoop);
    }

    private static async Task RecordingPrimaryAction()
    {
        using var fixture = new RecordingPageFixture();
        var vm = fixture.ViewModel;
        Check(vm.PrimaryActionCommand.CanExecute(null));
        vm.PrimaryActionCommand.Execute(null);
        Equal(RecordingState.Recording, vm.State);
        Check(!vm.PrimaryActionCommand.CanExecute(null), "Recording retains its separate stop control");
        vm.PauseCommand.Execute(null);
        Check(!vm.PrimaryActionCommand.CanExecute(null));
        vm.StopCommand.Execute(null);
        Check(vm.IsIdle);

        vm.SelectedRecording = vm.Recordings[0];
        vm.LoopCountText = "2";
        Equal(LocalizationService.Current.GetString("PlaybackStart"), vm.StartButtonText);
        vm.PrimaryActionCommand.Execute(null);
        var first = fixture.Playback.Runs.Single();
        Equal(2, first.Options.LoopCount);
        Equal(RecordingState.Playing, vm.State);
        Equal(LocalizationService.Current.GetString("PlaybackStop"), vm.StartButtonText);
        Check(vm.PrimaryActionCommand.CanExecute(null), "The same primary button must now stop playback");
        first.Report(9, 22, 1);
        vm.PauseCommand.Execute(null); // Legacy/programmatic pause still exposes the primary stop action.
        Equal(RecordingState.PlayPaused, vm.State);
        Equal(LocalizationService.Current.GetString("PlaybackStop"), vm.StartButtonText);
        Check(vm.PrimaryActionCommand.CanExecute(null));
        vm.PrimaryActionCommand.Execute(null);
        Equal(RecordingState.Stopping, vm.State);
        Check(fixture.Playback.StopRequested);
        Check(!vm.PrimaryActionCommand.CanExecute(null));
        vm.PrimaryActionCommand.Execute(null);
        Equal(1, fixture.Playback.Runs.Count);
        first.Finish(PlaybackOutcome.Stopped);
        await WaitRecordingIdle(vm);
        Equal(9, vm.CompletedActionCount);
        Equal(RecordingSessionResult.Stopped, vm.LastResult);
        Equal(LocalizationService.Current.GetString("PlaybackStart"), vm.StartButtonText);

        vm.PrimaryActionCommand.Execute(null);
        Equal(0, vm.CompletedActionCount);
        Equal("00:00:00.0", vm.ElapsedTime);
        var completed = fixture.Playback.Runs.Last();
        completed.Report(22, 22, 2);
        completed.Finish(PlaybackOutcome.Completed);
        await WaitRecordingIdle(vm);
        Equal(RecordingSessionResult.Completed, vm.LastResult);
        Check(vm.PrimaryActionCommand.CanExecute(null));

        vm.IsInfiniteLoop = true;
        vm.PrimaryActionCommand.Execute(null);
        var infinite = fixture.Playback.Runs.Last();
        Equal(-1, infinite.Options.LoopCount);
        infinite.Report(5, 22, 7);
        vm.PrimaryActionCommand.Execute(null);
        Equal(RecordingState.Stopping, vm.State);
        infinite.Finish(PlaybackOutcome.Stopped);
        await WaitRecordingIdle(vm);
        Equal(7, vm.CurrentLoop);
        Equal(5, vm.CompletedActionCount);

        foreach (var actions in new[] { new List<RecordedAction>(), new List<RecordedAction> { new() { IsEnabled = false } } })
        {
            var empty = new RecordingItemViewModel(new Recording { Name = "No executable actions", Actions = actions }, vm);
            vm.Recordings.Add(empty);
            vm.SelectedRecording = empty;
            Check(!vm.PrimaryActionCommand.CanExecute(null));
            vm.PrimaryActionCommand.Execute(null);
            Equal(3, fixture.Playback.Runs.Count);
        }
    }

    private static async Task RecordingSpeedSlider()
    {
        using var fixture = new RecordingPageFixture();
        var vm = fixture.ViewModel;
        vm.SelectedRecording = vm.Recordings[0];
        var view = new RecordingPage { DataContext = vm };
        var host = new Window
        {
            Content = view,
            FontFamily = (FontFamily)Application.Current.FindResource("PixelFontFamily"),
            UseLayoutRounding = true
        };
        try
        {
            Layout(view, 644, 380);
            var slider = (Slider)view.FindName("SpeedSlider");
            var display = (TextBlock)view.FindName("SpeedValueText");
            Equal(.5, slider.Minimum);
            Equal(5d, slider.Maximum);
            Check(slider.IsEnabled);
            vm.SpeedFactor = 1.24;
            await DrainRecordingDispatcher();
            Equal(1.2, vm.SpeedFactor);
            Equal(1.2, slider.Value);
            Check(display.Text.Contains("1.2", StringComparison.Ordinal));
            Equal(vm.SpeedDisplayText, display.Text);

            slider.Value = 2.26;
            await DrainRecordingDispatcher();
            Equal(2.3, vm.SpeedFactor);
            Equal(vm.SpeedDisplayText, display.Text);
            Check(display.Text.Contains("2.3", StringComparison.Ordinal));
            vm.SpeedFactor = -2;
            await DrainRecordingDispatcher();
            Equal(.5, vm.SpeedFactor);
            Equal(.5, slider.Value);
            vm.SpeedFactor = 20;
            await DrainRecordingDispatcher();
            Equal(5d, vm.SpeedFactor);
            Equal(5d, slider.Value);

            vm.PrimaryActionCommand.Execute(null);
            Layout(view, 644, 380);
            Check(!slider.IsEnabled);
            var run = fixture.Playback.Runs.Last();
            Equal(5d, run.Options.SpeedFactor);
            vm.SpeedFactor = 1;
            Equal(5d, vm.SpeedFactor);
            vm.PauseCommand.Execute(null);
            Layout(view, 644, 380);
            Check(!slider.IsEnabled);
            vm.PrimaryActionCommand.Execute(null);
            Layout(view, 644, 380);
            Check(!slider.IsEnabled);
            run.Finish(PlaybackOutcome.Stopped);
            await WaitRecordingIdle(vm);
            Layout(view, 644, 380);
            Check(slider.IsEnabled);
            vm.SpeedFactor = 1.16;
            await DrainRecordingDispatcher();
            Equal(1.2, slider.Value);
            Equal(vm.SpeedDisplayText, display.Text);
        }
        finally
        {
            view.DataContext = null;
            host.Content = null;
            host.Close();
        }
    }

    private static async Task RecordingPageRendering()
    {
        using var fixture = new RecordingPageFixture();
        var vm = fixture.ViewModel;
        vm.Recordings[0].Model.Name = "A long recording name to verify that the permanent menu slot never overlaps the title or timing metadata 录制名称";
        vm.Recordings[0].Refresh();
        vm.LoopCountText = "9999";
        vm.LoopIntervalText = "100000";
        var view = new RecordingPage
        {
            DataContext = vm,
            Background = (Brush)Application.Current.FindResource("BackgroundBrush")
        };
        var host = new Window
        {
            Content = view,
            FontFamily = (FontFamily)Application.Current.FindResource("PixelFontFamily"),
            UseLayoutRounding = true,
            SnapsToDevicePixels = true
        };
        try
        {
            foreach (var language in new[] { AppLanguage.English, AppLanguage.SimplifiedChinese })
            {
                LocalizationService.Current.ApplyLanguage(language);
                await DrainRecordingDispatcher();
                foreach (var width in new[] { 644, 784 })
                foreach (var dpi in new[] { 96, 120, 144, 192 })
                {
                    var height = width == 644 ? 380 : 480;
                    var locale = language == AppLanguage.English ? "en" : "zh";
                    var suffix = dpi == 96 ? "" : $"-{dpi}dpi";
                    vm.SelectedRecording = null;
                    Layout(view, width, height);
                    // Materialize templates before changing DPI so every generated control
                    // participates in the new measurement, including the initially hidden options.
                    await DrainRecordingDispatcher();
                    SetRecordingDpi(view, dpi);
                    Layout(view, width, height);
                    var footer = (FrameworkElement)view.FindName("RecordingFooter");
                    var start = (Button)view.FindName("StartButton");
                    Equal(dpi / 96d, VisualTreeHelper.GetDpi(start).PixelsPerDip);
                    var baselineFooter = RecordingElementBounds(footer, view);
                    var baselineStart = RecordingElementBounds(start, view);
                    CheckRecordingFooterLayout(view, baselineFooter, baselineStart);
                    RenderRecordingDpi(view, $"recording-empty-{locale}-{width}{suffix}.png", width, height, dpi);

                    fixture.Capture.Actions.Clear();
                    vm.StartCommand.Execute(null);
                    Layout(view, width, height);
                    Equal(RecordingState.Recording, vm.State);
                    CheckRecordingFooterLayout(view, baselineFooter, baselineStart);
                    RenderRecordingDpi(view, $"recording-capture-{locale}-{width}{suffix}.png", width, height, dpi);
                    vm.PauseCommand.Execute(null);
                    Layout(view, width, height);
                    Equal(RecordingState.RecordPaused, vm.State);
                    CheckRecordingFooterLayout(view, baselineFooter, baselineStart);
                    RenderRecordingDpi(view, $"recording-capture-paused-{locale}-{width}{suffix}.png", width, height, dpi);
                    vm.StopCommand.Execute(null);
                    Check(vm.IsIdle);

                    vm.SelectedRecording = vm.Recordings[0];
                    Layout(view, width, height);
                    CheckRecordingFooterLayout(view, baselineFooter, baselineStart);
                    CheckRecordingItemMenu(view, vm.SelectedRecording, $"recording-menu-{locale}-{width}{suffix}.png", dpi);
                    RenderRecordingDpi(view, $"recording-ready-{locale}-{width}{suffix}.png", width, height, dpi);
                    vm.StartCommand.Execute(null);
                    var run = fixture.Playback.Runs.Last();
                    run.Report(12, 22, 1);
                    Layout(view, width, height);
                    CheckRecordingFooterLayout(view, baselineFooter, baselineStart);
                    Check(!((ListBox)view.FindName("RecordingsList")).IsEnabled, "The list must reject keyboard selection while running");
                    RenderRecordingDpi(view, $"recording-playing-{locale}-{width}{suffix}.png", width, height, dpi);

                    fixture.Clock.Advance(TimeSpan.FromMilliseconds(1430));
                    vm.PauseCommand.Execute(null);
                    Layout(view, width, height);
                    CheckRecordingFooterLayout(view, baselineFooter, baselineStart);
                    Check(start.Content?.ToString() == LocalizationService.Current.GetString("PlaybackStop"));
                    RenderRecordingDpi(view, $"recording-paused-{locale}-{width}{suffix}.png", width, height, dpi);

                    vm.StopCommand.Execute(null);
                    Layout(view, width, height);
                    CheckRecordingFooterLayout(view, baselineFooter, baselineStart);
                    RenderRecordingDpi(view, $"recording-stopping-{locale}-{width}{suffix}.png", width, height, dpi);
                    run.Finish(PlaybackOutcome.Stopped);
                    await WaitRecordingIdle(vm);
                    Layout(view, width, height);
                    CheckRecordingFooterLayout(view, baselineFooter, baselineStart);
                    RenderRecordingDpi(view, $"recording-stopped-{locale}-{width}{suffix}.png", width, height, dpi);
                }
            }
        }
        finally
        {
            view.DataContext = null;
            host.Content = null;
            host.Close();
            LocalizationService.Current.ApplyLanguage(AppLanguage.English);
        }
    }

    private static void RenderRecordingDpi(FrameworkElement view, string filename, int width, int height, int dpi)
    {
        Layout(view, width, height);
        Equal(dpi / 96d, VisualTreeHelper.GetDpi(view).PixelsPerDip);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * dpi / 96d),
            (int)Math.Ceiling(height * dpi / 96d), dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.Combine(AppContext.BaseDirectory, "artifacts");
        Directory.CreateDirectory(directory);
        using var output = File.Create(Path.Combine(directory, filename));
        encoder.Save(output);
    }

    private static void SetRecordingDpi(FrameworkElement view, int dpi)
    {
        VisualTreeHelper.SetRootDpi(view, new DpiScale(dpi / 96d, dpi / 96d));
        // This fixture has no HwndSource to invalidate the entire tree during WM_DPICHANGED.
        // Reusing a child's cached 96-DPI measurement would defer border rounding until the
        // first state change, so invalidate every materialized element before the baseline.
        foreach (var element in Descendants<FrameworkElement>(view))
        {
            element.InvalidateMeasure();
            element.InvalidateArrange();
        }
        view.InvalidateMeasure();
        view.InvalidateArrange();
    }

    private static void CheckRecordingItemMenu(RecordingPage view, RecordingItemViewModel item, string filename, int dpi)
    {
        // A never-opened ContextMenu has no Popup/PlacementTarget tree connection, so its cached
        // DynamicResources are not invalidated when the previous locale's dictionaries are replaced.
        // Like EditorRendering, materialize a fresh menu in each locale rather than simulating an
        // opening by changing Header values or showing a desktop Popup.
        var menuView = new RecordingPage { DataContext = view.DataContext, FontFamily = view.FontFamily };
        var menuHost = new Window
        {
            Content = menuView,
            FontFamily = view.FontFamily,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true
        };
        try
        {
            Layout(menuView, (int)view.ActualWidth, (int)view.ActualHeight);
            menuView.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            SetRecordingDpi(menuView, dpi);
            Layout(menuView, (int)view.ActualWidth, (int)view.ActualHeight);
            var button = Descendants<Button>(menuView).First(b => b.ContextMenu != null && ReferenceEquals(b.DataContext, item));
            var menu = button.ContextMenu!;
            menu.PlacementTarget = button;

            // Creating the menu's template realizes the item presenters and their inheritance
            // context. Allow their bindings to attach before inspecting commands and headers.
            Check(!menu.IsOpen);
            menu.Visibility = Visibility.Visible;
            menu.ApplyTemplate();
            menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            menu.Arrange(new Rect(new Point(), menu.DesiredSize));
            menu.UpdateLayout();
            menuView.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            SetRecordingDpi(menu, dpi);
            menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            menu.Arrange(new Rect(new Point(), menu.DesiredSize));
            menu.UpdateLayout();
            var menuItems = menu.Items.OfType<MenuItem>().ToArray();
            var commands = new[] { item.SetHotkeyCommand, item.OptimizeCommand, item.RenameCommand, item.DeleteCommand };
            var headers = new[] { item.HotkeyText, LocalizationService.Current.GetString("RecordingOptimize"),
                LocalizationService.Current.GetString("RecordingRename"), LocalizationService.Current.GetString("RecordingDelete") };
            Equal(commands.Length, menuItems.Length);
            for (var i = 0; i < commands.Length; i++)
            {
                Check(ReferenceEquals(commands[i], menuItems[i].Command), "Recording menu commands must target their own card");
                Equal(headers[i], menuItems[i].Header as string);
            }

            // ContextMenu templates have their own background and icon gutter. Render the actual
            // menu independently instead of inspecting only its item definitions.
            var frame = menu.Template.FindName("RecordingMenuFrame", menu) as Border;
            Check(frame != null, "The recording menu must replace the Windows theme's icon-gutter template");
            var surface = (SolidColorBrush)Application.Current.FindResource("SurfaceBrush");
            Equal(surface.Color, ((SolidColorBrush)frame!.Background).Color);
            foreach (var menuItem in menuItems)
            {
                var icon = menuItem.Icon as PackIconPixelartIcons;
                Check(icon != null);
                var iconBrush = ReferenceEquals(menuItem, menuItems.Last()) ? "DangerBrush" : "TextPrimaryBrush";
                Equal(((SolidColorBrush)Application.Current.FindResource(iconBrush)).Color,
                    ((SolidColorBrush)icon!.Foreground).Color);
            }
            Equal(((SolidColorBrush)Application.Current.FindResource("DangerBrush")).Color,
                ((SolidColorBrush)menuItems.Last().Foreground).Color);
            RenderRecordingDpi(menu, filename, (int)Math.Ceiling(menu.DesiredSize.Width), (int)Math.Ceiling(menu.DesiredSize.Height), dpi);
            Check(!menu.IsOpen, "Rendering a recording menu must not show an OS popup");
        }
        finally
        {
            menuView.DataContext = null;
            menuHost.Content = null;
            menuHost.Close();
        }
    }

    private static void CheckRecordingFooterLayout(RecordingPage view, Rect baselineFooter, Rect baselineStart)
    {
        // State notifications invalidate command routing asynchronously. A layout pass alone does
        // not process the queued CanExecute requery that updates Button.IsEnabled.
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        view.UpdateLayout();
        var vm = (RecordingPageViewModel)view.DataContext;
        var footer = (FrameworkElement)view.FindName("RecordingFooter");
        var start = (Button)view.FindName("StartButton");
        var startBounds = RecordingElementBounds(start, view);
        Equal(baselineFooter, RecordingElementBounds(footer, view));
        Equal(108d, baselineFooter.Height);
        Equal(baselineStart.X, startBounds.X);
        Equal(baselineStart.Y, startBounds.Y);
        Equal(baselineStart.Height, startBounds.Height);
        Check(startBounds.Width > 0 && startBounds.Right <= baselineFooter.Right,
            "The primary action must remain within the footer");
        if (vm.HasSelection)
            Check(startBounds.Width >= baselineStart.Width, "Playback should expand the primary button into the recording-control space");
        Check(ReferenceEquals(vm.PrimaryActionCommand, start.Command));
        Equal(vm.PrimaryActionCommand.CanExecute(null), start.IsEnabled);
        Check(view.FindName("SettingsButton") == null && view.FindName("PlaybackSettingsPopup") == null,
            "Playback settings must be directly available in the control row");
        Check(view.FindName("SpeedInput") == null, "The speed setting must use the slider");

        var recordingControls = (FrameworkElement)view.FindName("RecordingControlsPanel");
        if (vm.HasSelection)
        {
            Equal(Visibility.Collapsed, recordingControls.Visibility);
        }
        else
        {
            Equal(Visibility.Visible, recordingControls.Visibility);
            var pause = (Button)view.FindName("PauseButton");
            var stop = (Button)view.FindName("StopButton");
            var pauseBounds = RecordingElementBounds(pause, view);
            var stopBounds = RecordingElementBounds(stop, view);
            Check(startBounds.Right <= pauseBounds.Left + .1 && pauseBounds.Right <= stopBounds.Left + .1,
                "Recording start, pause and stop must not overlap");
            foreach (var bound in new[] { pauseBounds, stopBounds })
            {
                Check(bound.Right <= baselineFooter.Right + .1);
                Check(Math.Abs((startBounds.Top + startBounds.Bottom) - (bound.Top + bound.Bottom)) <= 2,
                    "Recording controls belong beside the primary action in the first row");
            }
            Check(pause.ToolTip is string pauseHint && !string.IsNullOrWhiteSpace(pauseHint));
            Check(stop.ToolTip is string stopHint && !string.IsNullOrWhiteSpace(stopHint));
            Check(!Equals(pause.ToolTip, stop.ToolTip), "Recording pause and stop require distinct explanations");
        }

        var status = (FrameworkElement)view.FindName("StatusPanel");
        var statusBounds = RecordingElementBounds(status, view);
        Check(statusBounds.Top >= startBounds.Bottom && statusBounds.Bottom <= baselineFooter.Bottom,
            "Status and elapsed time belong to the second footer row");
        Check(statusBounds.Width >= 140, "The compact status area must retain useful readable width");
        Check(status.ToolTip != null, "Truncated status text must retain its full tooltip");
        Equal(vm.CompactStatusText, ((TextBlock)view.FindName("CompactStatusTextBlock")).Text);
        Equal(vm.ElapsedStatusText, ((TextBlock)view.FindName("ElapsedStatusTextBlock")).Text);
        if (!vm.HasSelection) return;

        var settings = (FrameworkElement)view.FindName("PlaybackOptionsPanel");
        var settingsBounds = RecordingElementBounds(settings, view);
        Equal(Visibility.Visible, settings.Visibility);
        Check(settingsBounds.Top >= startBounds.Bottom, "Playback settings must sit below the action row");
        Check(statusBounds.Right <= settingsBounds.Left + .1, "Playback status must stay to the left of its parameters");
        Check(settingsBounds.Right <= baselineFooter.Right + .1);
        Check(settingsBounds.Bottom <= baselineFooter.Bottom);
        Check(Math.Abs((statusBounds.Top + statusBounds.Bottom) - (settingsBounds.Top + settingsBounds.Bottom)) <= 2,
            "Playback status and parameter controls belong in the same second row");
        var controls = new[]
        {
            "SpeedLabel", "SpeedSlider", "SpeedValueText", "LoopLabel", "LoopCountInput",
            "InfiniteLoopCheckBox", "IntervalLabel", "LoopIntervalInput"
        }.Select(name => (FrameworkElement)view.FindName(name)).ToArray();
        var bounds = controls.Select(control => RecordingElementBounds(control, view)).ToArray();
        foreach (var bound in bounds)
        {
            Check(bound.Width > 0 && bound.Height > 0);
            Check(bound.Left >= settingsBounds.Left && bound.Right <= baselineFooter.Right + .1,
                "Footer control must stay within the available width");
            Check(bound.Top >= startBounds.Bottom && bound.Bottom <= baselineFooter.Bottom,
                "Playback settings must fit below the main action");
        }
        for (var i = 1; i < bounds.Length; i++)
        {
            Check(bounds[i - 1].Right <= bounds[i].Left + .1, "Footer controls and labels must not overlap");
            Check(Math.Abs((bounds[0].Top + bounds[0].Bottom) - (bounds[i].Top + bounds[i].Bottom)) <= 2,
                "Speed, loop count, infinite loop and interval belong on the same second row");
        }
        foreach (var text in controls.OfType<TextBlock>())
        {
            var probe = new TextBlock
            {
                Text = text.Text, FontFamily = text.FontFamily, FontSize = text.FontSize,
                FontWeight = text.FontWeight, FontStyle = text.FontStyle,
                FontStretch = text.FontStretch, FlowDirection = text.FlowDirection,
                UseLayoutRounding = true
            };
            TextOptions.SetTextFormattingMode(probe, TextOptions.GetTextFormattingMode(text));
            VisualTreeHelper.SetRootDpi(probe, VisualTreeHelper.GetDpi(text));
            probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Check(probe.DesiredSize.Width <= text.ActualWidth + .5,
                $"The full playback label or speed value '{text.Text}' must be visible.");
        }
        var inputFailures = new List<string>();
        foreach (var box in controls.OfType<TextBox>())
            CheckInputValueFits(box, $"recording {view.ActualWidth} DIP {VisualTreeHelper.GetDpi(box).PixelsPerDip * 96} DPI {box.Name}", inputFailures);
        Check(inputFailures.Count == 0, string.Join(Environment.NewLine, inputFailures));
    }

    private static Rect RecordingElementBounds(FrameworkElement element, FrameworkElement ancestor) =>
        element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));

    private static async Task WaitRecordingIdle(RecordingPageViewModel vm)
    {
        for (var i = 0; i < 1000 && !vm.IsIdle; i++) await Task.Delay(1);
        Check(vm.IsIdle, "Recording session did not leave its active state");
    }

    private static async Task DrainRecordingDispatcher() =>
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

    private static List<RecordedAction> CreateRecordingActions(int count) =>
        Enumerable.Range(0, count).Select(i => new RecordedAction
        {
            Type = RecordedActionType.MouseMove, X = i, Y = i, DelayBeforeMs = 10
        }).ToList();

    private sealed class RecordingPageFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "MingAutoClicker-RecordingTests-" + Guid.NewGuid().ToString("N"));
        public RecordingPageViewModel ViewModel { get; }
        public RecordingTestCapture Capture { get; } = new();
        public RecordingTestPlayback Playback { get; } = new();
        public RecordingTestClock Clock { get; } = new();

        public RecordingPageFixture(IPlaybackService? playback = null)
        {
            Directory.CreateDirectory(_directory);
            var storage = new RecordingStorageService(Path.Combine(_directory, "recordings.json"));
            storage.Save(new Recording { Name = "A", Actions = CreateRecordingActions(22) });
            storage.Save(new Recording { Name = "B", Actions = CreateRecordingActions(159) });
            ViewModel = new RecordingPageViewModel(storage, Capture, playback ?? Playback, Clock);
        }

        public void Dispose()
        {
            ViewModel.Dispose();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class RecordingTestClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(_ticks);
        public void Advance(TimeSpan elapsed) => _ticks += elapsed.Ticks;
    }

    private sealed class RecordingTestCapture : IRecordingCaptureService
    {
        public event Action<int>? OnActionRecorded;
        public List<RecordedAction> Actions { get; set; } = new();
        public void StartRecording() => Actions.Clear();
        public void PauseRecording() { }
        public void ResumeRecording() { }
        public List<RecordedAction> StopRecording() => Actions.Select(action => action.Clone()).ToList();
        public void Report(int count) => OnActionRecorded?.Invoke(count);
        public void Dispose() { }
    }

    private sealed class RecordingTestPlayback : IPlaybackService
    {
        public bool IsRunning { get; private set; }
        public bool IsPaused { get; private set; }
        public bool StopRequested { get; private set; }
        public List<RecordingTestRun> Runs { get; } = new();

        public Task<PlaybackOutcome> StartPlaybackAsync(Recording recording, PlaybackOptions options,
            Action<PlaybackProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Check(!IsRunning, "The view model must not overlap playback sessions");
            IsRunning = true;
            IsPaused = false;
            StopRequested = false;
            var run = new RecordingTestRun(options, progress, () => { IsRunning = false; IsPaused = false; });
            Runs.Add(run);
            return run.Completion.Task;
        }

        public void PausePlayback() => IsPaused = true;
        public void ResumePlayback() => IsPaused = false;
        public void StopPlayback() => StopRequested = true;
        public void Dispose()
        {
            foreach (var run in Runs) run.Finish(PlaybackOutcome.Stopped);
        }
    }

    private sealed class RecordingTestRun
    {
        private readonly Action<PlaybackProgress>? _progress;
        private readonly Action _end;
        public PlaybackOptions Options { get; }
        public TaskCompletionSource<PlaybackOutcome> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public RecordingTestRun(PlaybackOptions options, Action<PlaybackProgress>? progress, Action end)
        {
            Options = options;
            _progress = progress;
            _end = end;
        }
        public void Report(int completed, int total, int loop) => _progress?.Invoke(new PlaybackProgress(completed, total, loop));
        public void Finish(PlaybackOutcome outcome)
        {
            if (Completion.Task.IsCompleted) return;
            _end();
            Completion.TrySetResult(outcome);
        }
        public void Fail(Exception error)
        {
            _end();
            Completion.TrySetException(error);
        }
    }
}
