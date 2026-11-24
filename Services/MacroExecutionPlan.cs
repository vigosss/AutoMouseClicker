using System.Diagnostics.CodeAnalysis;
using Ming_AutoClicker.Models;

namespace Ming_AutoClicker.Services;

/// <summary>The checked, independent snapshots used throughout one execution session.</summary>
public sealed class MacroExecutionPlan
{
    private readonly Dictionary<string, MacroProfile> _profiles;
    internal MacroProfile Entry { get; }
    internal MacroProfile GetTarget(ExecuteMacroAction action) => _profiles[action.TargetMacroId];

    private MacroExecutionPlan(string entryId, Dictionary<string, MacroProfile> profiles)
    {
        _profiles = profiles;
        Entry = profiles[entryId];
    }

    public static bool TryCreate(MacroProfile entry, IEnumerable<MacroProfile> library,
        IReadOnlyList<RegisteredHotkey> hotkeys, Func<string?, string> describeHotkeyTarget,
        [NotNullWhen(true)] out MacroExecutionPlan? plan, out string error)
    {
        if (TryPrepare(entry, library, hotkeys, describeHotkeyTarget, out plan, out var failure))
        {
            error = string.Empty;
            return true;
        }
        error = failure.Message;
        return false;
    }

    internal static bool TryPrepare(MacroProfile entry, IEnumerable<MacroProfile> library,
        IReadOnlyList<RegisteredHotkey> hotkeys, Func<string?, string> describeHotkeyTarget,
        [NotNullWhen(true)] out MacroExecutionPlan? plan, [NotNullWhen(false)] out MacroExecutionEventArgs? failure)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var strings = LocalizationService.Current;
        plan = null;
        var byId = library.ToLookup(profile => profile.Id);

        var reached = new Dictionary<string, (MacroProfile Profile, int Count)>();
        var returning = new HashSet<string>();
        var transferred = new HashSet<string>();
        var active = new HashSet<string> { entry.Id };
        var stack = new List<Frame> { new(entry) };
        var jumpHistory = new List<string>();

        MacroExecutionEventArgs Failure(string message, MacroAction? action = null)
        {
            var path = string.Join(" → ", jumpHistory.Concat(stack.Select(item => DisplayName(item.Profile))));
            var frame = stack[^1];
            return new MacroExecutionEventArgs
            {
                RootMacroId = stack[0].Profile.Id,
                MacroName = DisplayName(frame.Profile),
                MacroPath = path,
                Action = action,
                ActionIndex = Math.Max(0, frame.NextStep - 1),
                Outcome = MacroStepOutcome.Failed,
                Message = strings.Format("MacroExecutionContext", path, message)
            };
        }
        if (string.IsNullOrWhiteSpace(entry.Id) || byId[entry.Id].Skip(1).Any())
        {
            failure = Failure(strings.Format("MacroInvalidIdentifier", entry.Id));
            return false;
        }

        // Follow the possible continuation of each step. An image miss may end a round
        // sooner. Only an unconditional jump makes the remaining steps unreachable.
        while (stack.Count > 0)
        {
            var frame = stack[^1];
            var profile = frame.Profile;
            if (profile.Actions == null || profile.Actions.Count == 0)
            {
                failure = Failure(strings.GetString("ExecutorNoActions"));
                return false;
            }
            if (frame.NextStep == 0 && (profile.LoopIntervalMs < 0 || profile.LoopEnabled && profile.LoopCount < 0))
            {
                failure = Failure(strings.GetString(profile.LoopIntervalMs < 0
                    ? "ExecutorNegativeLoopInterval" : "ExecutorNegativeLoopCount"));
                return false;
            }

            if (frame.NextStep == profile.Actions.Count)
            {
                returning.Add(profile.Id);
                active.Remove(profile.Id);
                stack.RemoveAt(stack.Count - 1);
                continue;
            }

            var action = profile.Actions[frame.NextStep++];
            reached[profile.Id] = (profile, frame.NextStep);
            MacroExecutionEventArgs InvalidStep(string reason) =>
                Failure(strings.Format("ExecutorInvalidAction", frame.NextStep, reason), action);
            if (action == null)
            {
                failure = InvalidStep(strings.GetString("ActionUnknown"));
                return false;
            }
            if (!MacroActionValidator.TryValidate(action, out var invalidAction))
            {
                failure = InvalidStep(invalidAction);
                return false;
            }
            if (action is KeyPressAction key && MacroHotkeyValidator.FindConflict(key, hotkeys) is { } conflict)
            {
                var message = strings.Format("MacroHotkeyConflict", frame.NextStep,
                    HotkeyGestureHelper.Format(conflict.Gesture), describeHotkeyTarget(conflict.Target));
                if (conflict.Target?.StartsWith("macro:", StringComparison.Ordinal) == true &&
                    conflict.Target[6..] != profile.Id)
                    message += " " + strings.GetString("MacroUseTransferHint");
                failure = Failure(message, action);
                return false;
            }
            if (action is not ExecuteMacroAction transfer)
                continue;

            var targets = byId[transfer.TargetMacroId];
            var target = transfer.TargetMacroId == entry.Id ? entry : targets.FirstOrDefault();
            if (target == null)
            {
                failure = InvalidStep(strings.Format("MacroTargetMissing", transfer.TargetMacroId));
                return false;
            }
            if (targets.Skip(1).Any())
            {
                failure = InvalidStep(strings.Format("MacroInvalidIdentifier", target.Id));
                return false;
            }
            if (target.Id == profile.Id)
            {
                failure = InvalidStep(strings.GetString("MacroSelfTransfer"));
                return false;
            }

            if (transfer.Mode == MacroTransferMode.Jump)
            {
                // Every caller on this stack loses its continuation. Re-entering one
                // by a jump is an intentional loop, with no growth of the call stack.
                transferred.UnionWith(active);
                jumpHistory.AddRange(stack.Select(item => DisplayName(item.Profile)));
                stack.Clear();
                active.Clear();
            }
            if (transferred.Contains(target.Id))
                break;
            if (returning.Contains(target.Id))
                continue;
            if (!active.Add(target.Id))
            {
                var cycle = stack.SkipWhile(item => item.Profile.Id != target.Id)
                    .Select(item => DisplayName(item.Profile)).Append(DisplayName(target));
                failure = InvalidStep(strings.Format("MacroCallCycle", string.Join(" → ", cycle)));
                return false;
            }
            stack.Add(new Frame(target));
        }

        var snapshots = reached.ToDictionary(pair => pair.Key,
            pair => pair.Value.Profile.DeepClone(pair.Value.Count));
        foreach (var snapshot in snapshots.Values)
            foreach (var transfer in snapshot.Actions.OfType<ExecuteMacroAction>())
                transfer.TargetMacroName = DisplayName(snapshots[transfer.TargetMacroId]);
        plan = new MacroExecutionPlan(entry.Id, snapshots);
        failure = null;
        return true;
    }

    internal static string DisplayName(MacroProfile profile) =>
        string.IsNullOrWhiteSpace(profile.Name) ? profile.Id : profile.Name;

    private sealed class Frame(MacroProfile profile)
    {
        public MacroProfile Profile { get; } = profile;
        public int NextStep { get; set; }
    }
}
