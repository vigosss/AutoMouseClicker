using System.Drawing;
using System.IO;
using System.Text.Json;
using Emgu.CV;
using Emgu.CV.Structure;
using Ming_AutoClicker.Helpers;
using Ming_AutoClicker.Models;
using Ming_AutoClicker.Services;
using Ming_AutoClicker.ViewModels;
using Ming_AutoClicker.Views.Macros;

namespace Ming_AutoClicker.Tests;

internal static partial class Program
{
    private static void AddColorMacroTests(List<(string Name, Func<Task> Run)> tests)
    {
        tests.Add(("Color and variable click settings survive JSON and clone independently", ColorMacroPersistence));
        tests.Add(("An old saved-position macro survives editor changes and executes its original click", LegacyColorMacroEditorExecution));
        tests.Add(("Color actions verify pointer input and save positions without moving", ColorMacroOperations));
        tests.Add(("Color misses follow each policy while scope failures stop execution", ColorMacroMissPolicies));
        tests.Add(("A skipped color save invalidates its previous position", ColorMacroFailedSave));
        tests.Add(("Remembered positions are local to each call and jump", ColorMacroTransfers));
        tests.Add(("Each macro round requires a fresh position", ColorMacroLoop));
    }

    private static FindColorAction ColorStep(ColorOperation operation = ColorOperation.SavePosition) => new()
    {
        TargetColor = "#0A141E", Tolerance = 0, TimeoutMs = 0, Operation = operation, VariableName = "按钮",
        SearchScope = new() { Kind = ImageSearchScopeKind.Region, X = -90, Y = 20, Width = 4, Height = 2 }
    };

    private static MouseClickAction ClickColorVariable() => new()
    {
        PositionSource = ClickPositionSource.Variable, VariableName = "按钮", X = 400, Y = 600
    };

    private static async Task LegacyColorMacroEditorExecution()
    {
        var directory = Directory.CreateTempSubdirectory("color-macro-");
        try
        {
            var storage = new MacroStorageService(directory.FullName);
            var file = Path.Combine(directory.FullName, "Legacy_legacy-position.json");
            File.WriteAllText(file, """
                {
                  "Id": "legacy-position", "Name": "Legacy",
                  "Actions": [
                    { "Type": "FindColor", "Operation": "SavePosition", "VariableName": "确认按钮",
                      "TargetColor": "#0A141E", "Tolerance": 0, "TimeoutMs": 0,
                      "SearchScope": { "Kind": 1, "X": -90, "Y": 20, "Width": 4, "Height": 2 } },
                    { "Type": "Wait", "WaitSeconds": 0.01 },
                    { "Type": "MouseClick", "Operation": "RightClick", "PositionSource": "Variable",
                      "VariableName": "确认按钮", "X": 400, "Y": 600 }
                  ]
                }
                """);
            using var capture = new ScreenCaptureService(Path.Combine(directory.FullName, "screenshots"));
            using var matcher = new ImageMatchService(capture);
            using (var editor = new MacroEditorViewModel(storage, capture, matcher, storage.Load(file).DeepClone()))
            {
                var view = new MacroEditorView { DataContext = editor };
                foreach (var action in editor.Actions)
                {
                    editor.SelectedAction = action;
                    Layout(view, 700, 500);
                    if (action is WaitAction) editor.ActionNote = "edited note";
                }
                editor.SaveCompleted += (_, _) => storage.Save(editor.Macro);
                editor.SaveCommand.Execute(null);
                view.DataContext = null;
            }

            var saved = storage.LoadById("legacy-position")!;
            using var reopened = new MacroEditorViewModel(storage, capture, matcher, saved.DeepClone());
            var find = (FindColorAction)reopened.Actions[0];
            var wait = (WaitAction)reopened.Actions[1];
            var click = (MouseClickAction)reopened.Actions[2];
            Equal(ColorOperation.SavePosition, find.Operation);
            Equal("确认按钮", find.VariableName);
            Equal("edited note", wait.Note);
            Equal(.01, wait.WaitSeconds);
            Equal(ClickPositionSource.Variable, click.PositionSource);
            Equal(find.VariableName, click.VariableName);

            using var runtime = new ColorMacroFixture(_ => 2);
            var executed = new List<ActionType>();
            runtime.Executor.ActionExecuted += (_, e) => executed.Add(e.Action!.Type);
            var result = await runtime.Run(reopened.Macro);
            Check(result.Success, result.Message);
            Sequence(new[] { ActionType.FindColor, ActionType.Wait, ActionType.MouseClick }, executed);
            Equal((-88, 21), runtime.Sender.Positions.Single());
            var input = runtime.Sender.Batches.Single();
            Sequence(new[] { Win32Api.MOUSEEVENTF_RIGHTDOWN, Win32Api.MOUSEEVENTF_RIGHTUP },
                input.Select(item => item.U.mi.dwFlags));
        }
        finally { directory.Delete(recursive: true); }
    }

    private static async Task ColorMacroPersistence()
    {
        var color = ColorStep();
        color.Order = 2;
        color.Note = "tile";
        color.NotFoundBehavior = ImageNotFoundBehavior.EndRound;
        foreach (var step in new MacroAction[] { color, ClickColorVariable() })
        {
            var json = JsonSerializer.Serialize(step, JsonOptions());
            var loaded = JsonSerializer.Deserialize<MacroAction>(json, JsonOptions())!;
            Equal(json, JsonSerializer.Serialize<MacroAction>(loaded.Clone(), JsonOptions()));
            Check(MacroActionValidator.TryValidate(loaded, out _));
            Check(loaded.GetDescription().Contains("按钮"));
        }
        color.Clone().SearchScope.X = 100;
        Equal(-90, color.SearchScope.X);
        var legacy = (MouseClickAction)JsonSerializer.Deserialize<MacroAction>(
            """{"Type":"MouseClick","X":-33,"Y":17,"Operation":"RightClick"}""", JsonOptions())!;
        Equal(ClickPositionSource.Fixed, legacy.PositionSource);
        Equal(-33, legacy.X);
        await Throws<JsonException>(() =>
            Task.FromResult(JsonSerializer.Deserialize<MacroAction>("""{"Type":"FindColor","SearchScope":null}""", JsonOptions())));
        foreach (var invalid in new[] { new FindColorAction { Tolerance = -1 }, new FindColorAction { Tolerance = 256 },
                     new FindColorAction { TargetColor = "#00000G" }, new FindColorAction { TimeoutMs = -1 },
                     new FindColorAction { Operation = ColorOperation.SavePosition, VariableName = "  " },
                     new FindColorAction { SearchScope = new() { Kind = ImageSearchScopeKind.Region, Width = 0 } } })
            Check(!MacroActionValidator.TryValidate(invalid, out _));
        Check(!MacroActionValidator.TryValidate(new MouseClickAction { PositionSource = ClickPositionSource.Variable }, out _));
        Check(!MacroActionValidator.TryValidate(new FindColorAction { Operation = ColorOperation.SavePosition }, out var error));
        Equal(LocalizationService.Current.GetString("MacroMissingPositionName"), error);
        Check(MacroActionValidator.TryValidate(new FindColorAction(), out _));
        using var fixture = new ColorMacroFixture(_ => 0);
        var rejected = Completion(fixture.Executor);
        Check(!fixture.Executor.Start(new MacroProfile { Actions = [new FindColorAction { SearchScope = null! }] }));
        Check(!(await rejected).Success);
        Equal(0, fixture.Frames.Captures);
    }

    private static async Task ColorMacroOperations()
    {
        foreach (var operation in Enum.GetValues<ColorOperation>())
        {
            using var fixture = new ColorMacroFixture(_ => 2);
            var result = await fixture.Run(new() { Actions = [ColorStep(operation)] });
            Check(result.Success, result.Message);
            if (operation == ColorOperation.SavePosition)
            {
                Equal(0, fixture.Sender.Positions.Count);
                Equal(0, fixture.Sender.Batches.Count);
                continue;
            }
            Check(fixture.Frames.Captures >= 2);
            Equal((-88, 21), fixture.Sender.Positions.Single());
            if (operation == ColorOperation.MoveTo)
                Equal(0, fixture.Sender.Batches.Count);
            else
                Equal(operation == ColorOperation.RightClick ? Win32Api.MOUSEEVENTF_RIGHTDOWN : Win32Api.MOUSEEVENTF_LEFTDOWN,
                    fixture.Sender.Batches.Single()[0].U.mi.dwFlags);
        }
    }

    private static async Task ColorMacroMissPolicies()
    {
        foreach (var policy in Enum.GetValues<ImageNotFoundBehavior>())
        {
            using var fixture = new ColorMacroFixture(_ => null);
            var step = ColorStep();
            step.NotFoundBehavior = policy;
            var result = await fixture.Run(new() { Actions = [step, new KeyPressAction()] });
            Equal(policy != ImageNotFoundBehavior.Stop, result.Success);
            Equal(policy == ImageNotFoundBehavior.SkipStep ? 1 : 0, fixture.Sender.Batches.Count);
            Equal(0, fixture.Sender.Positions.Count);
            if (policy == ImageNotFoundBehavior.EndRound)
                Equal(MacroStepOutcome.RoundEnded, result.Outcome);
        }
        using var failed = new ColorMacroFixture(_ => throw new ImageSearchScopeException("ImageScopeUnavailable"));
        var search = ColorStep();
        search.NotFoundBehavior = ImageNotFoundBehavior.SkipStep;
        var failure = await failed.Run(new() { Actions = [search, new KeyPressAction()] });
        Check(!failure.Success);
        Equal(MatchFailureReason.ScopeUnavailable, failure.MatchResult!.FailureReason);
        Equal(0, failed.Sender.Batches.Count);

        using var changed = new ColorMacroFixture(capture => capture == 1 ? 0 : null);
        var click = ColorStep(ColorOperation.Click);
        click.NotFoundBehavior = ImageNotFoundBehavior.SkipStep;
        var lost = await changed.Run(new() { Actions = [click, new KeyPressAction()] });
        Check(!lost.Success);
        Equal(MatchFailureReason.TargetChanged, lost.MatchResult!.FailureReason);
        Equal(0, changed.Sender.Positions.Count);
        Equal(0, changed.Sender.Batches.Count);

        MacroExecutor? running = null;
        using var cancelled = new ColorMacroFixture(capture =>
        {
            if (capture == 2) running!.Stop();
            return 0;
        });
        running = cancelled.Executor;
        var stopped = await cancelled.Run(new() { Actions = [click] });
        Check(!stopped.Success);
        Equal(0, cancelled.Sender.Positions.Count);
        Equal(0, cancelled.Sender.Batches.Count);
    }

    private static async Task ColorMacroFailedSave()
    {
        using var fixture = new ColorMacroFixture(capture => capture == 1 ? 1 : null);
        var retry = ColorStep();
        retry.NotFoundBehavior = ImageNotFoundBehavior.SkipStep;
        var result = await fixture.Run(new() { Actions = [ColorStep(), retry, ClickColorVariable()] });
        Check(!result.Success && result.Action is MouseClickAction);
        Check(result.Message.Contains("按钮"));
        Equal(0, fixture.Sender.Positions.Count);
        Equal(0, fixture.Sender.Batches.Count);
    }

    private static async Task ColorMacroTransfers()
    {
        using (var fixture = new ColorMacroFixture(capture => capture == 1 ? 1 : 3))
        {
            var child = new MacroProfile { Actions = [ColorStep(), ClickColorVariable()] };
            var parent = new MacroProfile { Actions = [ColorStep(), RunMacro(child), ClickColorVariable()] };
            var result = await fixture.Run(parent, child);
            Check(result.Success, result.Message);
            Sequence(new[] { (-87, 21), (-89, 21) }, fixture.Sender.Positions);
        }
        foreach (var transfer in Enum.GetValues<MacroTransferMode>())
        {
            using var fixture = new ColorMacroFixture(_ => 1);
            var child = new MacroProfile { Actions = [ClickColorVariable()] };
            var parent = new MacroProfile { Actions = [ColorStep(), RunMacro(child, transfer)] };
            var result = await fixture.Run(parent, child);
            Check(!result.Success && result.Action is MouseClickAction);
            Equal(0, fixture.Sender.Positions.Count);
        }
    }

    private static async Task ColorMacroLoop()
    {
        using var fixture = new ColorMacroFixture(capture => capture == 1 ? 1 : null);
        var step = ColorStep();
        step.NotFoundBehavior = ImageNotFoundBehavior.SkipStep;
        var result = await fixture.Run(new()
        {
            Actions = [step, ClickColorVariable()], LoopEnabled = true, LoopCount = 2, LoopIntervalMs = 0
        });
        Check(!result.Success && result.Action is MouseClickAction);
        Equal(1, fixture.Executor.CompletedLoopCount);
        Equal((-89, 21), fixture.Sender.Positions.Single());
    }

    private sealed class ColorMacroFrames(Func<int, int?> selectPixel) : IImageFrameSource
    {
        public int Captures { get; private set; }
        public CapturedImageFrame Capture(ImageSearchScope scope)
        {
            var pixel = selectPixel(++Captures);
            var image = new Image<Bgr, byte>(4, 2, new Bgr(0, 0, 0));
            if (pixel is int x) image[1, x] = new Bgr(30, 20, 10);
            return new(image, scope.GetRectangle());
        }
    }

    private sealed class ColorMacroFixture : IDisposable
    {
        private readonly ScreenCaptureService _capture = new();
        private readonly ImageMatchService _image;
        public FakeInputSender Sender { get; } = new();
        public ColorMacroFrames Frames { get; }
        public MacroExecutor Executor { get; }

        public ColorMacroFixture(Func<int, int?> selectPixel)
        {
            Frames = new(selectPixel);
            _image = new(_capture);
            Executor = new(_image, _capture, new MacroInputService(Sender), new ColorSearchService(Frames));
        }

        public Task<MacroExecutionEventArgs> Run(MacroProfile profile, params MacroProfile[] targets)
        {
            var plan = CheckedMacroPlan(profile, targets);
            var completion = Completion(Executor);
            Check(Executor.Start(plan));
            return completion;
        }

        public void Dispose()
        {
            Executor.Dispose();
            _image.Dispose();
            _capture.Dispose();
        }
    }
}
