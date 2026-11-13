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
        tests.Add(("Image executor verifies a fresh frame and uses injected click input", ImageExecutorClick));
        tests.Add(("Disappearing targets and scope failures never click", ImageExecutorTargetChanged));
        tests.Add(("Cancellation after capture prevents image clicks", ImageExecutorCancelled));
    }

    private static Task ImageOptionsRoundTrip()
    {
        var original = new FindImageAction
        {
            ImagePath = "primary.png", MatchThreshold = .86, TimeoutMs = 9000,
            ClickPoint = new() { X = .25, Y = .75 }, CaptureDpi = 120,
            IgnoredRegions = new() { new() { X = .1, Y = .1, Width = .2, Height = .2 } },
            AlternateTemplates = new() { new() { ImagePath = "alternate.png", ClickPoint = new() { X = .7, Y = .2 } } },
            SearchScope = new() { Kind = ImageSearchScopeKind.Window, WindowProcessName = "sample",
                WindowClassName = "sampleClass", WindowTitle = "Sample", WindowRegion = new() { Width = .5, Height = .5 } },
            SelectionMode = ImageSelectionMode.Ordered, MatchIndex = 2
        };
        var json = JsonSerializer.Serialize<MacroAction>(original, JsonOptions());
        var loaded = (FindImageAction)JsonSerializer.Deserialize<MacroAction>(json, JsonOptions())!;
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
            Check(action.TimeoutMs == null && action.ClickPoint == null);
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

    private static async Task ImageExecutorClick()
    {
        using var fixture = new ImageExecutionFixture();
        var action = fixture.Action;
        action.ClickPoint = new() { X = .25, Y = .75 };
        var result = await fixture.Run(action);
        Check(result.Success, result.Message);
        Check(fixture.Frames.Captures >= 2, "Click must follow a fresh frame verification");
        Equal(1, fixture.Sender.Positions.Count);
        var point = fixture.Sender.Positions[0];
        Check(Math.Abs(point.X - (-200 + 80 + 16)) <= 2 && Math.Abs(point.Y - (30 + 55 + 30)) <= 2,
            $"Wrong scaled click anchor {point}");
        Equal(2, fixture.Sender.Batches.Single().Length);
    }

    private static async Task ImageExecutorTargetChanged()
    {
        using (var fixture = new ImageExecutionFixture())
        {
            fixture.Frames.HideAfterFirst = true;
            var result = await fixture.Run(fixture.Action);
            Check(!result.Success);
            Equal(0, fixture.Sender.Batches.Count);
        }
        using (var fixture = new ImageExecutionFixture())
        {
            fixture.Frames.ScopeFailure = true;
            var result = await fixture.Run(fixture.Action);
            Check(!result.Success);
            Equal(0, fixture.Sender.Positions.Count);
        }
    }

    private static async Task ImageExecutorCancelled()
    {
        using var fixture = new ImageExecutionFixture();
        fixture.Frames.OnCapture = () => fixture.Executor.Stop();
        Check(!(await fixture.Run(fixture.Action)).Success);
        Equal(0, fixture.Sender.Batches.Count);
        Equal(0, fixture.Sender.Positions.Count);
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
        public async Task<MacroExecutionEventArgs> Run(FindImageAction action)
        {
            var profile = new MacroProfile();
            profile.Actions.Add(action);
            var completed = Completion(Executor);
            Check(Executor.Start(profile));
            return await completed;
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
        public bool HideAfterFirst { get; set; }
        public bool ScopeFailure { get; set; }
        public Action? OnCapture { get; set; }
        public ExecutionFrameSource(Image<Bgr, byte> template) => _template = template.Clone();
        public CapturedImageFrame Capture(ImageSearchScope scope)
        {
            Captures++;
            if (ScopeFailure) throw new ImageSearchScopeException("ImageScopeUnavailable");
            var image = new Image<Bgr, byte>(240, 160, new Bgr(19, 23, 31));
            if (!HideAfterFirst || Captures == 1)
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
