using System.Drawing;
using System.IO;
using System.Text.Json;
using Emgu.CV;
using Emgu.CV.Structure;
using Ming_AutoClicker.Helpers;
using Ming_AutoClicker.Models;
using Ming_AutoClicker.Services;

namespace Ming_AutoClicker.Tests;

internal static partial class Program
{
    static partial void AddImageIntegrationTests(List<(string Name, Func<Task> Run)> tests)
    {
        tests.Add(("Image options survive JSON and independent deep cloning", ImageOptionsRoundTrip));
        tests.Add(("Legacy image waits and fixed offsets remain unchanged", LegacyImageOptions));
        tests.Add(("Invalid image settings fail validation", ImageOptionsValidation));
        tests.Add(("Malformed image option containers are rejected before editor cloning", ImageNullContainers));
        tests.Add(("Mouse click sender releases partial sends and honors cancellation", ImageClickInput));
        tests.Add(("Image executor clicks the verified anchor", () => ImageExecutorTargetsPoint("Click")));
        tests.Add(("Move to image positions the pointer at the verified anchor without clicking", () => ImageExecutorTargetsPoint("MoveTo")));
        tests.Add(("Disappearing targets and scope failures never click", ImageExecutorTargetChanged));
        tests.Add(("Cancellation after capture prevents image clicks", ImageExecutorCancelled));
        tests.Add(("Ordinary single-search misses and waiting timeouts follow all three policies", ImageMissPolicies));
        tests.Add(("A target that appears while waiting still gets clicked", ImageAppearsWhileWaiting));
        tests.Add(("Ending a round in a called macro uses the root loop count", ImageEndsRootRound));
        tests.Add(("Final rounds skip the interval and stopping an interval sends no more input", ImageRoundInterval));
    }

    private static Task ImageOptionsRoundTrip()
    {
        var original = new FindImageAction
        {
            ImagePath = "primary.png", Operation = "MoveTo", MatchThreshold = .86, TimeoutMs = 9000,
            ClickPoint = new() { X = .25, Y = .75 }, CaptureDpi = 120,
            IgnoredRegions = new() { new() { X = .1, Y = .1, Width = .2, Height = .2 } },
            AlternateTemplates = new() { new() { ImagePath = "alternate.png", ClickPoint = new() { X = .7, Y = .2 } } },
            SearchScope = new() { Kind = ImageSearchScopeKind.Window, WindowProcessName = "sample",
                WindowClassName = "sampleClass", WindowTitle = "Sample", WindowRegion = new() { Width = .5, Height = .5 } },
            SelectionMode = ImageSelectionMode.Ordered, MatchIndex = 2, NotFoundBehavior = ImageNotFoundBehavior.EndRound
        };
        var json = JsonSerializer.Serialize<MacroAction>(original, JsonOptions());
        var loaded = (FindImageAction)JsonSerializer.Deserialize<MacroAction>(json, JsonOptions())!;
        Equal("MoveTo", loaded.Operation);
        Check(loaded.GetDescription().Contains(LocalizationService.Current.GetString("EditorMoveTo"), StringComparison.Ordinal));
        Equal(ImageNotFoundBehavior.EndRound, loaded.NotFoundBehavior);
        Equal(json, JsonSerializer.Serialize<MacroAction>(loaded.Clone(), JsonOptions()));
        Check(MacroActionValidator.TryValidate(loaded, out _));
        var copy = loaded.Clone();
        copy.ClickPoint!.X = .9;
        copy.IgnoredRegions[0].X = .3;
        copy.AlternateTemplates[0].ClickPoint!.X = .1;
        copy.SearchScope.WindowRegion!.Width = .8;
        Equal(.25, loaded.ClickPoint!.X);
        Equal(.1, loaded.IgnoredRegions[0].X);
        Equal(.7, loaded.AlternateTemplates[0].ClickPoint!.X);
        Equal(.5, loaded.SearchScope.WindowRegion!.Width);
        return Task.CompletedTask;
    }

    private static Task LegacyImageOptions()
    {
        foreach (var adaptive in new[] { false, true })
        foreach (var wait in new[] { false, true })
        {
            var json = "{\"Type\":\"FindImage\",\"ImagePath\":\"old.png\",\"OffsetX\":13,\"OffsetY\":-8," +
                $"\"AdaptiveScale\":{adaptive.ToString().ToLowerInvariant()},\"WaitUntilFound\":{wait.ToString().ToLowerInvariant()}" + "}";
            var action = (FindImageAction)JsonSerializer.Deserialize<MacroAction>(json, JsonOptions())!;
            Equal("Click", action.Operation);
            Check(action.TimeoutMs == null && action.ClickPoint == null);
            Equal(ImageNotFoundBehavior.Stop, action.NotFoundBehavior);
            Equal(wait ? 12345 : 0, action.GetTimeoutMs(12345));
            Equal(13, action.Clone().OffsetX);
            Equal(-8, action.Clone().OffsetY);
            Equal(ImageSearchScopeKind.Desktop, action.SearchScope.Kind);
        }
        return Task.CompletedTask;
    }

    private static Task ImageOptionsValidation()
    {
        var action = new FindImageAction { ImagePath = "valid.png" };
        Check(MacroActionValidator.TryValidate(action, out _));
        action.NotFoundBehavior = (ImageNotFoundBehavior)99;
        Check(!MacroActionValidator.TryValidate(action, out _));
        action.NotFoundBehavior = ImageNotFoundBehavior.Stop;
        action.TimeoutMs = -1;
        Check(!MacroActionValidator.TryValidate(action, out _));
        action.TimeoutMs = 0;
        action.ClickPoint = new() { X = double.NaN };
        Check(!MacroActionValidator.TryValidate(action, out _));
        action.ClickPoint = null;
        action.IgnoredRegions.Add(new() { X = .9, Width = .2, Height = .3 });
        Check(!MacroActionValidator.TryValidate(action, out _));
        action.IgnoredRegions.Clear();
        action.SearchScope = new() { Kind = ImageSearchScopeKind.Region, X = int.MaxValue, Width = 10, Height = 10 };
        Check(!MacroActionValidator.TryValidate(action, out _));
        action.SearchScope = new() { Kind = ImageSearchScopeKind.Region, X = -1920, Y = -20, Width = 300, Height = 200 };
        Check(MacroActionValidator.TryValidate(action, out _));
        return Task.CompletedTask;
    }

    private static async Task ImageNullContainers()
    {
        foreach (var field in new[] { "\"SearchScope\":null", "\"IgnoredRegions\":null",
                     "\"IgnoredRegions\":[null]", "\"AlternateTemplates\":null",
                     "\"AlternateTemplates\":[null]", "\"AlternateTemplates\":[{\"IgnoredRegions\":null}]" })
        {
            var json = "{\"Type\":\"FindImage\",\"ImagePath\":\"old.png\"," + field + "}";
            await Throws<JsonException>(() =>
            {
                JsonSerializer.Deserialize<MacroAction>(json, JsonOptions());
                return Task.CompletedTask;
            });
        }
    }

    private static async Task ImageClickInput()
    {
        foreach (var right in new[] { false, true })
        {
            var sender = new FakeInputSender();
            await new MacroInputService(sender).ClickAsync(-100, 20, right);
            Equal((-100, 20), sender.Positions.Single());
            var batch = sender.Batches.Single();
            Equal(right ? Win32Api.MOUSEEVENTF_RIGHTDOWN : Win32Api.MOUSEEVENTF_LEFTDOWN, batch[0].U.mi.dwFlags);
            Equal(right ? Win32Api.MOUSEEVENTF_RIGHTUP : Win32Api.MOUSEEVENTF_LEFTUP, batch[1].U.mi.dwFlags);
            Equal(Win32Api.InputMarker, batch[0].U.mi.dwExtraInfo);
        }
        var partial = new FakeInputSender { OnSend = (i, _) => i == 0 ? 1u : 1u };
        await Throws<InvalidOperationException>(() => new MacroInputService(partial).ClickAsync(1, 2, false));
        Equal(2, partial.Batches.Count);
        Equal(Win32Api.MOUSEEVENTF_LEFTUP, partial.Batches[1].Single().U.mi.dwFlags);
        using var cancelled = new CancellationTokenSource();
        var moved = new FakeInputSender { OnMove = (_, _) => cancelled.Cancel() };
        await Throws<OperationCanceledException>(() => new MacroInputService(moved).ClickAsync(1, 2, false, cancelled.Token));
        Equal(0, moved.Batches.Count);
    }

    private static async Task ImageExecutorTargetsPoint(string operation)
    {
        using var fixture = new ImageExecutionFixture();
        var action = fixture.Action;
        action.Operation = operation;
        action.ClickPoint = new() { X = .25, Y = .75 };
        var result = await fixture.Run(action);
        Check(result.Success, result.Message);
        Check(fixture.Frames.Captures >= 2, "Pointer movement must follow fresh frame verification");
        var point = fixture.Sender.Positions.Single();
        Check(Math.Abs(point.X - (-200 + 80 + 16)) <= 2 && Math.Abs(point.Y - (30 + 55 + 30)) <= 2,
            $"Pointer moved to {point} instead of the template anchor");
        if (operation == "MoveTo")
            Equal(0, fixture.Sender.Batches.Count);
        else
            Equal(2, fixture.Sender.Batches.Single().Length);
    }

    private static async Task ImageExecutorTargetChanged()
    {
        foreach (var behavior in Enum.GetValues<ImageNotFoundBehavior>())
        {
            using var fixture = new ImageExecutionFixture();
            fixture.Frames.HideAfterFirst = true;
            fixture.Action.TimeoutMs = 5000;
            fixture.Action.NotFoundBehavior = behavior;
            var result = await fixture.Run(new MacroProfile { Actions = [fixture.Action, new KeyPressAction()] });
            Check(!result.Success);
            Equal(MatchFailureReason.TargetChanged, result.MatchResult?.FailureReason);
            Equal(0, fixture.Sender.Batches.Count);
        }
        using (var fixture = new ImageExecutionFixture())
        {
            fixture.Frames.ScopeFailure = true;
            fixture.Action.NotFoundBehavior = ImageNotFoundBehavior.SkipStep;
            var result = await fixture.Run(new MacroProfile { Actions = [fixture.Action, new KeyPressAction()] });
            Check(!result.Success);
            Equal(MatchFailureReason.ScopeUnavailable, result.MatchResult?.FailureReason);
            Equal(0, fixture.Sender.Batches.Count);
            Equal(0, fixture.Sender.Positions.Count);
        }
    }

    private static async Task ImageExecutorCancelled()
    {
        using var fixture = new ImageExecutionFixture();
        fixture.Frames.OnCapture = () => fixture.Executor.Stop();
        fixture.Action.NotFoundBehavior = ImageNotFoundBehavior.SkipStep;
        Check(!(await fixture.Run(new MacroProfile { Actions = [fixture.Action, new KeyPressAction()] })).Success);
        Equal(0, fixture.Sender.Batches.Count);
        Equal(0, fixture.Sender.Positions.Count);
    }

    private static async Task ImageMissPolicies()
    {
        foreach (var timeout in new[] { 0, 500 })
        foreach (var behavior in Enum.GetValues<ImageNotFoundBehavior>())
        {
            using var fixture = new ImageExecutionFixture();
            fixture.Frames.ShowTarget = false;
            fixture.Executor.FindImageIntervalMs = 5000;
            fixture.Action.TimeoutMs = timeout;
            fixture.Action.NotFoundBehavior = behavior;
            var steps = new List<MacroExecutionEventArgs>();
            fixture.Executor.ActionExecuted += (_, e) => steps.Add(e);
            var result = await fixture.Run(new MacroProfile { Actions = [fixture.Action, new KeyPressAction()] });
            var image = steps.Single(step => step.Action is FindImageAction && step.Outcome != MacroStepOutcome.Started);
            Equal(timeout == 0 ? MatchFailureReason.BelowThreshold : MatchFailureReason.TimedOut, image.MatchResult?.FailureReason);
            Equal(behavior switch
            {
                ImageNotFoundBehavior.SkipStep => MacroStepOutcome.Skipped,
                ImageNotFoundBehavior.EndRound => MacroStepOutcome.RoundEnded,
                _ => MacroStepOutcome.Failed
            }, image.Outcome);
            Equal(behavior != ImageNotFoundBehavior.Stop, result.Success);
            Equal(behavior == ImageNotFoundBehavior.SkipStep ? 1 : 0, fixture.Sender.Batches.Count);
            Equal(0, fixture.Sender.Positions.Count);
            if (behavior == ImageNotFoundBehavior.SkipStep)
                Equal(Win32Api.INPUT_KEYBOARD, fixture.Sender.Batches.Single()[0].type);
        }
    }

    private static async Task ImageAppearsWhileWaiting()
    {
        using var fixture = new ImageExecutionFixture();
        fixture.Frames.ShowTarget = false;
        fixture.Frames.OnCapture = () => fixture.Frames.ShowTarget = true;
        fixture.Action.TimeoutMs = 5000;
        fixture.Action.NotFoundBehavior = ImageNotFoundBehavior.EndRound;
        var result = await fixture.Run(new MacroProfile { Actions = [fixture.Action, new KeyPressAction()] });
        Check(result.Success, result.Message);
        Equal(3, fixture.Frames.Captures);
        Sequence(new[] { Win32Api.INPUT_MOUSE, Win32Api.INPUT_KEYBOARD }, fixture.Sender.Batches.Select(batch => batch[0].type));
    }

    private static async Task ImageEndsRootRound()
    {
        using var fixture = new ImageExecutionFixture();
        fixture.Frames.ShowTarget = false;
        fixture.Action.NotFoundBehavior = ImageNotFoundBehavior.EndRound;
        var child = new MacroProfile { Name = "Child", Actions = [fixture.Action, new MouseClickAction()] };
        var root = new MacroProfile
        {
            Name = "Root", LoopEnabled = true, LoopCount = 2, LoopIntervalMs = 0,
            Actions = [new KeyPressAction(), new ExecuteMacroAction { TargetMacroId = child.Id }, new MouseClickAction()]
        };
        Check(MacroExecutionPlan.TryCreate(root, [root, child], [], _ => string.Empty, out var plan, out var error), error);
        var completed = Completion(fixture.Executor);
        Check(fixture.Executor.Start(plan!));
        var result = await completed;
        Check(result.Success, result.Message);
        Equal(2, fixture.Executor.CompletedLoopCount);
        Equal(2, fixture.Frames.Captures);
        Sequence(new[] { Win32Api.INPUT_KEYBOARD, Win32Api.INPUT_KEYBOARD }, fixture.Sender.Batches.Select(batch => batch[0].type));
        Equal(0, fixture.Sender.Positions.Count);
    }

    private static async Task ImageRoundInterval()
    {
        foreach (var count in new[] { 1, 2 })
        {
            using var fixture = new ImageExecutionFixture();
            fixture.Frames.ShowTarget = false;
            fixture.Action.NotFoundBehavior = ImageNotFoundBehavior.EndRound;
            var endedRound = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Executor.ActionExecuted += (_, e) =>
            {
                if (e.Outcome == MacroStepOutcome.RoundEnded) endedRound.TrySetResult();
            };
            var run = fixture.Run(new MacroProfile
            {
                Actions = [fixture.Action, new KeyPressAction()], LoopEnabled = true, LoopCount = count, LoopIntervalMs = 60000
            });
            await endedRound.Task.WaitAsync(TimeSpan.FromSeconds(3));
            if (count == 2)
            {
                await Task.Delay(50);
                Check(!run.IsCompleted);
                fixture.Executor.Stop();
            }
            var result = await run.WaitAsync(TimeSpan.FromSeconds(3));
            Equal(count == 1, result.Success);
            Equal(1, fixture.Executor.CompletedLoopCount);
            Equal(1, fixture.Frames.Captures);
            Equal(0, fixture.Sender.Batches.Count);
        }
    }

    private sealed class ImageExecutionFixture : IDisposable
    {
        public ScreenCaptureService Capture { get; } = new();
        public FakeInputSender Sender { get; } = new();
        public ExecutionFrameSource Frames { get; }
        public ImageMatchService Matcher { get; }
        public MacroExecutor Executor { get; }
        public FindImageAction Action { get; }
        private readonly string _templatePath;

        public ImageExecutionFixture()
        {
            using var bitmap = new Bitmap(64, 40);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.FromArgb(30, 85, 145));
                graphics.FillRectangle(Brushes.White, 5, 5, 6, 29);
                graphics.FillEllipse(Brushes.Gold, 18, 6, 23, 23);
                graphics.FillRectangle(Brushes.Coral, 47, 11, 11, 23);
                graphics.DrawLine(Pens.Black, 12, 35, 53, 3);
            }
            _templatePath = Capture.SaveBitmap(bitmap, "execution_" + Guid.NewGuid().ToString("N"));
            using var template = bitmap.ToImage<Bgr, byte>();
            Frames = new ExecutionFrameSource(template);
            Matcher = new ImageMatchService(Capture, Frames);
            Executor = new MacroExecutor(Matcher, Capture, new MacroInputService(Sender));
            Action = new() { ImagePath = Capture.GetRelativePath(_templatePath), TimeoutMs = 0, ClickPoint = new() };
        }
        public Task<MacroExecutionEventArgs> Run(FindImageAction action) => Run(new MacroProfile { Actions = [action] });
        public Task<MacroExecutionEventArgs> Run(MacroProfile profile)
        {
            var completed = Completion(Executor);
            Check(Executor.Start(profile));
            return completed;
        }
        public void Dispose()
        {
            Executor.Dispose(); Matcher.Dispose(); Frames.Dispose(); Capture.Dispose();
            File.Delete(_templatePath);
        }
    }

    private sealed class ExecutionFrameSource : IImageFrameSource, IDisposable
    {
        private readonly Image<Bgr, byte> _template;
        public int Captures { get; private set; }
        public bool ShowTarget { get; set; } = true;
        public bool HideAfterFirst { get; set; }
        public bool ScopeFailure { get; set; }
        public Action? OnCapture { get; set; }
        public ExecutionFrameSource(Image<Bgr, byte> template) => _template = template.Clone();
        public CapturedImageFrame Capture(ImageSearchScope scope)
        {
            Captures++;
            if (ScopeFailure) throw new ImageSearchScopeException("ImageScopeUnavailable");
            var image = new Image<Bgr, byte>(240, 160, new Bgr(19, 23, 31));
            if (ShowTarget && (!HideAfterFirst || Captures == 1))
            {
                using var roi = new Mat(image.Mat, new Rectangle(80, 55, _template.Width, _template.Height));
                _template.Mat.CopyTo(roi);
            }
            OnCapture?.Invoke();
            return new(image, new Rectangle(-200, 30, 240, 160), 96);
        }
        public void Dispose() => _template.Dispose();
    }
}
