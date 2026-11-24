using Ming_AutoClicker.Models;
using Ming_AutoClicker.Services;

namespace Ming_AutoClicker.Tests;

internal static partial class Program
{
    private static void AddMacroTransferTests(List<(string Name, Func<Task> Run)> tests)
    {
        tests.Add(("Nested and repeated calls return and use the validated snapshot", NestedMacroCalls));
        tests.Add(("Jump discards all callers and starts the target's own rounds", NestedMacroJump));
        tests.Add(("Mutual jumps and a call followed by a jump remain stoppable", StopMacroTransferLoop));
        tests.Add(("Preflight diagnoses reachable conflicts, cycles and missing targets", MacroTransferPreflight));
    }

    private static MacroExecutionPlan CheckedMacroPlan(MacroProfile entry, params MacroProfile[] targets)
    {
        Check(MacroExecutionPlan.TryCreate(entry, targets.Prepend(entry), [], _ => "", out var plan, out var error), error);
        return plan!;
    }

    private static ExecuteMacroAction RunMacro(MacroProfile target, MacroTransferMode mode = MacroTransferMode.Call) =>
        new() { TargetMacroId = target.Id, Mode = mode };

    private static async Task NestedMacroCalls()
    {
        var child = new MacroProfile { Name = "Child", Actions = [new KeyPressAction { VirtualKey = 'C' }] };
        var middle = new MacroProfile { Name = "Middle", LoopEnabled = true, LoopCount = 0,
            Actions = [new KeyPressAction { VirtualKey = 'B' }, RunMacro(child), RunMacro(child)] };
        var root = new MacroProfile { Name = "Parent", LoopEnabled = true, LoopCount = 2, LoopIntervalMs = 0,
            Actions = [RunMacro(middle), new KeyPressAction { VirtualKey = 'D' }] };
        child.Name = "Renamed child";
        var plan = CheckedMacroPlan(root, middle, child);
        child.Name = "Later edit";
        child.Actions.Clear();
        middle.Actions.Clear();
        root.Actions.Clear();

        var sender = new FakeInputSender();
        using var capture = new ScreenCaptureService();
        using var match = new ImageMatchService(capture);
        using var executor = new MacroExecutor(match, capture, new MacroInputService(sender));
        var paths = new List<string>();
        executor.ActionStarted += (_, step) =>
        {
            if (step.Action is KeyPressAction) paths.Add(step.MacroPath);
        };
        var finished = Completion(executor);
        Check(executor.Start(plan));
        var result = await finished;
        Check(result.Success, result.Message);
        Equal(2, executor.CompletedLoopCount);
        Sequence("BCCDBCCD", sender.Batches.Select(batch => (char)batch[0].U.ki.wVk));
        Equal(4, paths.Count(path => path == "Parent → Middle → Renamed child"));
    }

    private static async Task NestedMacroJump()
    {
        var destination = new MacroProfile { Name = "Destination", LoopEnabled = true, LoopCount = 2,
            LoopIntervalMs = 0, Actions = [new KeyPressAction { VirtualKey = 'C' }] };
        var relay = new MacroProfile { Name = "Relay", Actions = [RunMacro(destination, MacroTransferMode.Jump),
            new ExecuteMacroAction { TargetMacroId = "unreachable missing macro" }] };
        var root = new MacroProfile { Name = "Parent", LoopEnabled = true,
            Actions = [RunMacro(relay), new KeyPressAction { VirtualKey = 'A' }] };
        var sender = new FakeInputSender();
        using var capture = new ScreenCaptureService();
        using var match = new ImageMatchService(capture);
        using var executor = new MacroExecutor(match, capture, new MacroInputService(sender));
        var finished = Completion(executor);
        Check(executor.Start(CheckedMacroPlan(root, relay, destination)));
        var result = await finished;
        Check(result.Success, result.Message);
        Equal(destination.Id, result.RootMacroId);
        Equal(destination.Name, result.MacroPath);
        Equal(2, executor.CompletedLoopCount);
        Sequence("CC", sender.Batches.Select(batch => (char)batch[0].U.ki.wVk));
    }

    private static async Task StopMacroTransferLoop()
    {
        foreach (var firstMode in Enum.GetValues<MacroTransferMode>())
        {
            var a = new MacroProfile { Name = "A" };
            var b = new MacroProfile { Name = "B" };
            a.Actions.Add(RunMacro(b, firstMode));
            a.Actions.Add(new KeyPressAction());
            b.Actions.Add(RunMacro(a, MacroTransferMode.Jump));
            var sender = new FakeInputSender();
            using var capture = new ScreenCaptureService();
            using var match = new ImageMatchService(capture);
            using var executor = new MacroExecutor(match, capture, new MacroInputService(sender));
            var jumps = 0;
            executor.ActionExecuted += (_, step) =>
            {
                if (step.Action is ExecuteMacroAction { Mode: MacroTransferMode.Jump } && ++jumps == 5)
                    executor.Stop();
            };
            var finished = Completion(executor);
            Check(executor.Start(CheckedMacroPlan(a, b)));
            Check(!(await finished).Success);
            Equal(5, jumps);
            Equal(0, sender.Batches.Count);
            Equal(MacroExecutionState.Stopped, executor.State);
        }
    }

    private static Task MacroTransferPreflight()
    {
        var root = new MacroProfile { Name = "Outer" };
        var child = new MacroProfile { Name = "Inner" };
        root.Actions.Add(RunMacro(child));
        string Rejection(params RegisteredHotkey[] hotkeys)
        {
            Check(!MacroExecutionPlan.TryCreate(root, [root, child], hotkeys, target => target ?? "global",
                out var plan, out var message));
            Check(plan == null);
            return message;
        }

        child.Actions.Add(new KeyPressAction { VirtualKey = 'X' });
        foreach (var target in new[] { "macro:worker", "recording:sample", null })
        {
            var message = Rejection(new RegisteredHotkey(target, new() { VirtualKey = 'X' }));
            Check(message.Contains("Outer → Inner") && message.Contains(target ?? "global"));
            Equal(target == "macro:worker", message.Contains(LocalizationService.Current.GetString("MacroUseTransferHint")));
        }
        child.Actions.Add(RunMacro(root));
        Check(Rejection().Contains("Outer → Inner → Outer"));
        child.Actions[1] = new ExecuteMacroAction { TargetMacroId = "deleted-id" };
        var missing = Rejection();
        Check(missing.Contains("Outer → Inner") && missing.Contains("deleted-id") && missing.Contains("2"));
        child.Actions[1] = RunMacro(child);
        Check(Rejection().Contains(LocalizationService.Current.GetString("MacroSelfTransfer")));
        Check(!MacroActionValidator.TryValidate(new ExecuteMacroAction { TargetMacroId = "id",
            Mode = (MacroTransferMode)99 }, out _));
        return Task.CompletedTask;
    }
}
