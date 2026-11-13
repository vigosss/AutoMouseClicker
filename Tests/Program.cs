using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Ming_AutoClicker.Helpers;
using Ming_AutoClicker.Models;
using Ming_AutoClicker.Services;

namespace Ming_AutoClicker.Tests;

// A dependency-free Windows regression runner. All injected input is captured by FakeInputSender.
internal static partial class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--window-visibility-target", StringComparer.Ordinal)) return RunWindowVisibilityTarget();
            return Run(args.Contains("--window-visibility-only", StringComparer.Ordinal),
                args.Contains("--recording-only", StringComparer.Ordinal),
                args.Contains("--recording-popup-only", StringComparer.Ordinal),
                args.Contains("--input-layout-only", StringComparer.Ordinal),
                args.Contains("--pixel-theme-only", StringComparer.Ordinal),
                args.Contains("--docs-screenshots", StringComparer.Ordinal));
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static int Run(bool windowVisibilityOnly = false, bool recordingOnly = false, bool recordingPopupOnly = false, bool inputLayoutOnly = false, bool pixelThemeOnly = false, bool docsScreenshots = false)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var source in new[] { "Localization/Strings.en-US.xaml", "Styles/Colors.xaml",
                     "Styles/PixelPrimitives.xaml", "Styles/TextStyles.xaml", "Styles/Buttons.xaml", "Styles/Inputs.xaml", "Styles/Lists.xaml",
                     "Styles/ScrollBars.xaml", "Styles/Tabs.xaml", "Styles/Windows.xaml" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/Ming-AutoClicker;component/{source}", UriKind.Relative)
            });
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        if (docsScreenshots) return RunDocumentationScreenshots();

        var tests = new List<(string Name, Func<Task> Run)>
        {
            ("Four wheel directions and input marker", ScrollDirections),
            ("Maximum scroll amount preserves signed wheel deltas", MaximumScrollAmount),
            ("Negative screen coordinates and cursor failure", ScrollCoordinates),
            ("Cancellation after cursor movement prevents scrolling", CancelAfterMove),
            ("Scroll send failures propagate", ScrollSendFailure),
            ("Invalid actions cannot inject input", InvalidActions),
            ("Key catalog covers supported key groups", KeyCatalog),
            ("Modifiers and extended keys press/release in order", KeySequence),
            ("Every partial keyboard prefix releases pending keys", PartialKeyboardInput),
            ("Sender exceptions still release all possible keys", ThrowingKeyboardSender),
            ("Cleanup failure does not skip remaining key releases", CleanupFailure),
            ("Cancellation never skips partial-send cleanup", CancellationDuringSend),
            ("Pre-cancelled actions send no input", PreCancelledInput),
            ("Held physical keys delay keyboard input", WaitForPhysicalKeys),
            ("Waiting for physical keys remains cancellable", CancelPhysicalKeyWait),
            ("All action types survive JSON and deep cloning", RoundTripAndClone),
            ("Legacy JSON stays compatible", LegacyJson),
            ("Global, macro and recording hotkeys conflict exactly", HotkeyConflicts),
            ("Registered hotkey snapshots cannot be mutated", HotkeySnapshots),
            ("Mixed actions execute in order across loops", ExecutorLoops),
            ("Input failure prevents subsequent macro steps", ExecutorFailure),
            ("Immediate stop still completes execution", ExecutorImmediateStop),
            ("Invalid later action rejects macro before any input", ExecutorValidation)
        };
        AddEditorTests(tests);
        AddInputLayoutTests(tests);
        AddRecordingPlaybackTests(tests);
        AddRecordingPageTests(tests);
        AddReleaseLifecycleTests(tests);
        AddImageMatchingTests(tests);
        AddImageEditorTests(tests);
        AddImageScopeTests(tests);
        AddImageIntegrationTests(tests);
        AddImageRenderedTests(tests);
        AddImageTestPreviewTests(tests);
        AddPixelThemeTests(tests);
        if (windowVisibilityOnly)
        {
            tests.Clear();
            AddImageWindowVisibilityTests(tests);
        }
        else if (recordingPopupOnly)
        {
            tests.Clear();
            AddRecordingPopupTests(tests);
        }
        else if (recordingOnly)
        {
            tests.Clear();
            AddRecordingPlaybackTests(tests);
            AddRecordingPageTests(tests);
        }
        else if (inputLayoutOnly)
        {
            tests.Clear();
            AddInputLayoutTests(tests);
        }
        else if (pixelThemeOnly)
        {
            tests.Clear();
            AddPixelThemeTests(tests);
        }

        var failed = 0;
        foreach (var (name, run) in tests)
        {
            try
            {
                CompleteOnDispatcher(run().WaitAsync(TimeSpan.FromSeconds(15)));
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"FAIL {name}: {ex}");
            }
        }
        Console.WriteLine($"{tests.Count - failed}/{tests.Count} regression tests passed.");
        return failed == 0 ? 0 : 1;
    }

    private static void CompleteOnDispatcher(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            var dispatcher = Dispatcher.CurrentDispatcher;
            _ = task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)),
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }

    private static async Task ScrollDirections()
    {
        foreach (var (direction, flag, delta) in new[]
        {
            (ScrollDirection.Up, Win32Api.MOUSEEVENTF_WHEEL, 360),
            (ScrollDirection.Down, Win32Api.MOUSEEVENTF_WHEEL, -360),
            (ScrollDirection.Left, Win32Api.MOUSEEVENTF_HWHEEL, -360),
            (ScrollDirection.Right, Win32Api.MOUSEEVENTF_HWHEEL, 360)
        })
        {
            var sender = new FakeInputSender();
            await new MacroInputService(sender).ScrollAsync(new() { Direction = direction, Notches = 3 });
            Equal(0, sender.Positions.Count);
            var input = sender.Batches.Single().Single();
            Equal(Win32Api.INPUT_MOUSE, input.type);
            Equal(flag, input.U.mi.dwFlags);
            Equal(delta, unchecked((int)input.U.mi.mouseData));
            Equal(Win32Api.InputMarker, input.U.mi.dwExtraInfo);
        }
    }

    private static async Task ScrollCoordinates()
    {
        var sender = new FakeInputSender();
        var scroll = new MouseScrollAction { PositionMode = ScrollPositionMode.Coordinates, X = -1800, Y = -20 };
        await new MacroInputService(sender).ScrollAsync(scroll);
        Equal((-1800, -20), sender.Positions.Single());
        Equal(1, sender.Batches.Count);
        sender = new FakeInputSender { MoveResult = false };
        await Throws<InvalidOperationException>(() => new MacroInputService(sender).ScrollAsync(scroll));
        Equal(0, sender.Batches.Count);
    }

    private static async Task MaximumScrollAmount()
    {
        foreach (var direction in Enum.GetValues<ScrollDirection>())
        {
            var sender = new FakeInputSender();
            await new MacroInputService(sender).ScrollAsync(new()
            {
                Direction = direction, Notches = MacroActionValidator.MaximumScrollNotches
            });
            var expected = direction is ScrollDirection.Down or ScrollDirection.Left ? -2147483640 : 2147483640;
            Equal(expected, unchecked((int)sender.Batches.Single().Single().U.mi.mouseData));
        }
    }

    private static async Task CancelAfterMove()
    {
        using var cts = new CancellationTokenSource();
        var sender = new FakeInputSender { OnMove = (_, _) => cts.Cancel() };
        await Throws<OperationCanceledException>(() => new MacroInputService(sender).ScrollAsync(
            new() { PositionMode = ScrollPositionMode.Coordinates }, cts.Token));
        Equal(1, sender.Positions.Count);
        Equal(0, sender.Batches.Count);
    }

    private static async Task ScrollSendFailure()
    {
        var sender = new FakeInputSender { OnSend = (_, _) => 0 };
        await Throws<InvalidOperationException>(() => new MacroInputService(sender).ScrollAsync(new()));
        Equal(1, sender.Batches.Count);
    }

    private static async Task InvalidActions()
    {
        MacroAction[] invalid =
        {
            new MouseScrollAction { Notches = 0 }, new MouseScrollAction { Notches = -1 },
            new MouseScrollAction { Notches = MacroActionValidator.MaximumScrollNotches + 1 },
            new MouseScrollAction { Direction = (ScrollDirection)99 },
            new MouseScrollAction { PositionMode = (ScrollPositionMode)99 },
            new KeyPressAction { VirtualKey = 0 }, new KeyPressAction { VirtualKey = 0xA2 },
            new KeyPressAction { Modifiers = (KeyModifiers)16 }
        };
        var sender = new FakeInputSender();
        var service = new MacroInputService(sender);
        foreach (var action in invalid)
        {
            Check(!MacroActionValidator.TryValidate(action, out var error) && !string.IsNullOrWhiteSpace(error));
            await Throws<ArgumentException>(() => action is MouseScrollAction scroll
                ? service.ScrollAsync(scroll) : service.PressKeyAsync((KeyPressAction)action));
        }
        Equal(0, sender.Batches.Count);
        Check(MacroActionValidator.TryValidate(new MouseScrollAction { Notches = MacroActionValidator.MaximumScrollNotches }, out _));
    }

    private static Task KeyCatalog()
    {
        foreach (uint key in Enumerable.Range(0x41, 26).Concat(Enumerable.Range(0x30, 10))
                     .Concat(Enumerable.Range(0x70, 24)).Concat(Enumerable.Range(0x60, 10)))
            Check(MacroKeyCatalog.IsSupported(key), $"Missing key {key:X}");
        foreach (uint key in new uint[] { 0x0D, 9, 0x1B, 0x20, 8, 0x21, 0x22, 0x23, 0x24,
                     0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E, 0x6A, 0x6B, 0x6D, 0x6E, 0x6F })
            Check(MacroKeyCatalog.IsSupported(key));
        Equal(MacroKeyCatalog.Keys.Count, MacroKeyCatalog.Keys.Select(k => k.VirtualKey).Distinct().Count());
        Equal("Ctrl+C", MacroKeyCatalog.Format(0x43, KeyModifiers.Control));
        return Task.CompletedTask;
    }

    private static async Task KeySequence()
    {
        var sender = new FakeInputSender();
        await new MacroInputService(sender).PressKeyAsync(new()
        {
            VirtualKey = 0x25,
            Modifiers = KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Windows
        });
        var inputs = sender.Batches.Single();
        Sequence(new ushort[] { 0xA2, 0xA4, 0xA0, 0x5B, 0x25, 0x25, 0x5B, 0xA0, 0xA4, 0xA2 },
            inputs.Select(x => x.U.ki.wVk));
        for (var i = 0; i < inputs.Length; i++)
        {
            Equal(Win32Api.INPUT_KEYBOARD, inputs[i].type);
            Equal(i >= 5, IsKeyUp(inputs[i]));
            Equal(inputs[i].U.ki.wVk is 0x5B or 0x25, (inputs[i].U.ki.dwFlags & Win32Api.KEYEVENTF_EXTENDEDKEY) != 0);
            Equal(Win32Api.InputMarker, inputs[i].U.ki.dwExtraInfo);
        }
        foreach (uint key in new uint[] { 0x0D, 9, 0x26, 0x2E, 0x6F, 0x43 })
        {
            sender.Batches.Clear();
            await new MacroInputService(sender).PressKeyAsync(new() { VirtualKey = key });
            Equal(2, sender.Batches.Single().Length);
        }
    }

    private static async Task PartialKeyboardInput()
    {
        for (uint prefix = 0; prefix < 6; prefix++)
        {
            var accepted = prefix;
            var sender = new FakeInputSender { OnSend = (i, a) => i == 0 ? accepted : (uint)a.Length };
            await Throws<InvalidOperationException>(() => new MacroInputService(sender).PressKeyAsync(
                new() { VirtualKey = 0x25, Modifiers = KeyModifiers.Control | KeyModifiers.Windows }));
            var pressed = new HashSet<ushort>();
            foreach (var input in sender.Batches[0].Take((int)accepted))
            {
                if (IsKeyUp(input)) pressed.Remove(input.U.ki.wVk); else pressed.Add(input.U.ki.wVk);
            }
            var releases = sender.Batches.Skip(1).SelectMany(x => x).ToArray();
            Equal(pressed.Count, releases.Length);
            foreach (var input in releases)
            {
                Check(IsKeyUp(input));
                Check(pressed.Remove(input.U.ki.wVk));
                Equal(input.U.ki.wVk is 0x25 or 0x5B, (input.U.ki.dwFlags & Win32Api.KEYEVENTF_EXTENDEDKEY) != 0);
            }
            Equal(0, pressed.Count);
        }
    }

    private static async Task ThrowingKeyboardSender()
    {
        var sender = new FakeInputSender { OnSend = (i, a) => i == 0 ? throw new IOException("Injected failure") : (uint)a.Length };
        await Throws<IOException>(() => new MacroInputService(sender).PressKeyAsync(
            new() { VirtualKey = 0x43, Modifiers = KeyModifiers.Control }));
        Sequence(new ushort[] { 0x43, 0xA2 }, sender.Batches.Skip(1).SelectMany(x => x).Select(x => x.U.ki.wVk));
        Check(sender.Batches.Skip(1).SelectMany(x => x).All(IsKeyUp));
    }

    private static async Task CleanupFailure()
    {
        foreach (var throwDuringRelease in new[] { false, true })
        {
            var sender = new FakeInputSender { OnSend = (i, a) => i switch
            {
                0 => 3u,
                1 => throwDuringRelease ? throw new IOException("Release failed") : 0u,
                _ => (uint)a.Length
            } };
            await Throws<InvalidOperationException>(() => new MacroInputService(sender).PressKeyAsync(
                new() { Modifiers = KeyModifiers.Control | KeyModifiers.Alt }));
            Equal(4, sender.Batches.Count);
            Check(sender.Batches.Skip(1).SelectMany(x => x).All(IsKeyUp));
            Equal((ushort)0xA2, sender.Batches.Last().Single().U.ki.wVk);
        }
    }

    private static async Task CancellationDuringSend()
    {
        using var cts = new CancellationTokenSource();
        var sender = new FakeInputSender { OnSend = (i, a) => { if (i == 0) { cts.Cancel(); return 2; } return (uint)a.Length; } };
        await Throws<InvalidOperationException>(() => new MacroInputService(sender).PressKeyAsync(
            new() { Modifiers = KeyModifiers.Control }, cts.Token));
        Equal(3, sender.Batches.Count);
        Check(sender.Batches.Skip(1).SelectMany(x => x).All(IsKeyUp));

        using var completedCts = new CancellationTokenSource();
        sender = new FakeInputSender { OnSend = (_, a) => { completedCts.Cancel(); return (uint)a.Length; } };
        await Throws<OperationCanceledException>(() => new MacroInputService(sender).PressKeyAsync(new(), completedCts.Token));
        Equal(1, sender.Batches.Count);
    }

    private static async Task PreCancelledInput()
    {
        var sender = new FakeInputSender();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var service = new MacroInputService(sender);
        await Throws<OperationCanceledException>(() => service.PressKeyAsync(new(), cts.Token));
        await Throws<OperationCanceledException>(() => service.ScrollAsync(new() { PositionMode = ScrollPositionMode.Coordinates }, cts.Token));
        Equal(0, sender.Batches.Count);
        Equal(0, sender.Positions.Count);
    }

    private static async Task WaitForPhysicalKeys()
    {
        foreach (uint heldKey in new uint[] { 0xA2, 0xA3, 0xA4, 0xA5, 0xA0, 0xA1, 0x5B, 0x5C, 0x0D })
        {
            var held = 1;
            var sender = new FakeInputSender { KeyState = key => key == heldKey && Volatile.Read(ref held) != 0 };
            var task = new MacroInputService(sender).PressKeyAsync(new());
            await Task.Delay(35);
            Equal(0, sender.Batches.Count);
            Volatile.Write(ref held, 0);
            await task;
            Equal(1, sender.Batches.Count);
        }
    }

    private static async Task CancelPhysicalKeyWait()
    {
        using var cts = new CancellationTokenSource();
        var sender = new FakeInputSender { KeyState = key => key == 0xA2 };
        var task = new MacroInputService(sender).PressKeyAsync(new(), cts.Token);
        await Task.Delay(30);
        cts.Cancel();
        await Throws<OperationCanceledException>(() => task);
        Equal(0, sender.Batches.Count);
    }

    private static Task RoundTripAndClone()
    {
        var profile = new MacroProfile { Name = "Mixed", LoopEnabled = true, LoopCount = 3 };
        profile.Actions.Add(new FindImageAction { Order = 0, Note = "find", ImagePath = "image.png" });
        profile.Actions.Add(new WaitAction { Order = 1, WaitSeconds = .5 });
        profile.Actions.Add(new MouseClickAction { Order = 2, X = -12, Y = 44 });
        profile.Actions.Add(new MouseScrollAction { Order = 3, Note = "scroll", Direction = ScrollDirection.Left,
            Notches = 4, PositionMode = ScrollPositionMode.Coordinates, X = -1920, Y = -100 });
        profile.Actions.Add(new KeyPressAction { Order = 4, Note = "copy", VirtualKey = 0x43,
            Modifiers = KeyModifiers.Control | KeyModifiers.Shift });
        var options = JsonOptions();
        var json = JsonSerializer.Serialize(profile, options);
        var loaded = JsonSerializer.Deserialize<MacroProfile>(json, options)!;
        Equal(json, JsonSerializer.Serialize(loaded, options));
        var clone = loaded.DeepClone();
        Equal(json, JsonSerializer.Serialize(clone, options));
        ((MouseScrollAction)clone.Actions[3]).X = 99;
        ((KeyPressAction)clone.Actions[4]).Modifiers = KeyModifiers.None;
        Equal(-1920, ((MouseScrollAction)loaded.Actions[3]).X);
        Equal(KeyModifiers.Control | KeyModifiers.Shift, ((KeyPressAction)loaded.Actions[4]).Modifiers);
        return Task.CompletedTask;
    }

    private static Task LegacyJson()
    {
        var loaded = JsonSerializer.Deserialize<MacroProfile>("""
            {"Name":"legacy","Actions":[{"Type":"findimage","ImagePath":"sample.png"},
            {"Type":"Wait","WaitSeconds":0.2},{"Type":"MouseClick","X":-5,"Y":20,"Operation":"RightClick"}]}
            """, JsonOptions())!;
        Sequence(new[] { ActionType.FindImage, ActionType.Wait, ActionType.MouseClick }, loaded.Actions.Select(a => a.Type));
        Equal(3, loaded.DeepClone().Actions.Count);
        return Task.CompletedTask;
    }

    private static Task HotkeyConflicts()
    {
        var profile = new MacroProfile();
        profile.Actions.Add(new WaitAction());
        profile.Actions.Add(new KeyPressAction { VirtualKey = 0x43, Modifiers = KeyModifiers.Control });
        foreach (var target in new[] { null, "macro:current", "macro:other", "recording:other" })
        {
            var registered = new[] { new RegisteredHotkey(target,
                new() { VirtualKey = 0x43, Modifiers = HotkeyModifierKeys.Control }) };
            var conflict = MacroHotkeyValidator.FindConflict(profile, registered)!;
            Equal(1, conflict.ActionIndex);
            Equal(target, conflict.Hotkey.Target);
        }
        Check(MacroHotkeyValidator.FindConflict(profile, Array.Empty<RegisteredHotkey>()) == null);
        Check(MacroHotkeyValidator.FindConflict(profile, new[] { new RegisteredHotkey(null,
            new() { VirtualKey = 0x43, Modifiers = HotkeyModifierKeys.Control | HotkeyModifierKeys.Shift }) }) == null);
        Check(MacroHotkeyValidator.FindConflict(profile, new[] { new RegisteredHotkey(null,
            new() { VirtualKey = 0x56, Modifiers = HotkeyModifierKeys.Control }) }) == null);
        return Task.CompletedTask;
    }

    private static Task HotkeySnapshots()
    {
        using var service = new HotkeyService();
        Equal(0, service.GetRegisteredHotkeys().Count);
        var source = new HotkeyGesture();
        var snapshot = new RegisteredHotkey(null, source);
        source.VirtualKey = 0x41;
        snapshot.Gesture.VirtualKey = 0x42;
        Equal(HotkeyGesture.F8VirtualKey, snapshot.Gesture.VirtualKey);
        return Task.CompletedTask;
    }

    private static async Task ExecutorLoops()
    {
        var sender = new FakeInputSender();
        using var capture = new ScreenCaptureService();
        using var match = new ImageMatchService(capture);
        using var executor = new MacroExecutor(match, capture, new MacroInputService(sender));
        var profile = new MacroProfile { LoopEnabled = true, LoopCount = 2, LoopIntervalMs = 0 };
        profile.Actions.Add(new MouseScrollAction { Order = 0 });
        profile.Actions.Add(new WaitAction { Order = 1, WaitSeconds = 0 });
        profile.Actions.Add(new KeyPressAction { Order = 2 });
        var completed = Completion(executor);
        var order = new List<ActionType>();
        executor.ActionExecuted += (_, e) => order.Add(e.Action!.Type);
        Check(executor.Start(profile));
        Check((await completed).Success);
        Equal(2, executor.CompletedLoopCount);
        Sequence(Enumerable.Repeat(new[] { ActionType.MouseScroll, ActionType.Wait, ActionType.KeyPress }, 2).SelectMany(x => x), order);
        Sequence(new[] { Win32Api.INPUT_MOUSE, Win32Api.INPUT_KEYBOARD, Win32Api.INPUT_MOUSE, Win32Api.INPUT_KEYBOARD },
            sender.Batches.Select(x => x[0].type));
    }

    private static async Task ExecutorFailure()
    {
        var sender = new FakeInputSender { OnSend = (_, _) => 0 };
        using var capture = new ScreenCaptureService();
        using var match = new ImageMatchService(capture);
        using var executor = new MacroExecutor(match, capture, new MacroInputService(sender));
        var profile = new MacroProfile();
        profile.Actions.Add(new KeyPressAction());
        profile.Actions.Add(new MouseScrollAction { Order = 1 });
        var completed = Completion(executor);
        Check(executor.Start(profile));
        Check(!(await completed).Success);
        Equal(1, sender.Batches.Count);
        Equal(MacroExecutionState.Stopped, executor.State);
    }

    private static async Task ExecutorImmediateStop()
    {
        using var capture = new ScreenCaptureService();
        using var match = new ImageMatchService(capture);
        for (var i = 0; i < 10; i++)
        {
            var sender = new FakeInputSender { KeyState = _ => true };
            using var executor = new MacroExecutor(match, capture, new MacroInputService(sender));
            var profile = new MacroProfile();
            profile.Actions.Add(new KeyPressAction());
            var completed = Completion(executor);
            Check(executor.Start(profile));
            executor.Stop();
            Check(!(await completed).Success);
            Equal(0, sender.Batches.Count);
            Equal(MacroExecutionState.Stopped, executor.State);
        }
    }

    private static async Task ExecutorValidation()
    {
        var sender = new FakeInputSender();
        using var capture = new ScreenCaptureService();
        using var match = new ImageMatchService(capture);
        using var executor = new MacroExecutor(match, capture, new MacroInputService(sender));
        var profile = new MacroProfile();
        profile.Actions.Add(new KeyPressAction());
        profile.Actions.Add(new MouseScrollAction { Order = 1, Notches = 0 });
        var completed = Completion(executor);
        Check(!executor.Start(profile));
        Check(!(await completed).Success);
        Equal(0, sender.Batches.Count);
    }

    private static Task<MacroExecutionEventArgs> Completion(MacroExecutor executor)
    {
        var completion = new TaskCompletionSource<MacroExecutionEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        executor.ExecutionCompleted += (_, e) => completion.TrySetResult(e);
        return completion.Task;
    }

    private static JsonSerializerOptions JsonOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new MacroActionConverter());
        return options;
    }

    private static bool IsKeyUp(Win32Api.INPUT input) => (input.U.ki.dwFlags & Win32Api.KEYEVENTF_KEYUP) != 0;
    private static void Check(bool condition, string message = "Assertion failed")
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Equal<T>(T expected, T actual) => Check(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}, got {actual}");
    private static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual) => Check(expected.SequenceEqual(actual), "Sequences differ");
    private static async Task Throws<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}");
    }

    static partial void AddEditorTests(List<(string Name, Func<Task> Run)> tests);
    static partial void AddImageMatchingTests(List<(string Name, Func<Task> Run)> tests);
    static partial void AddImageEditorTests(List<(string Name, Func<Task> Run)> tests);
    static partial void AddImageScopeTests(List<(string Name, Func<Task> Run)> tests);
    static partial void AddImageIntegrationTests(List<(string Name, Func<Task> Run)> tests);
    static partial void AddImageRenderedTests(List<(string Name, Func<Task> Run)> tests);
    static partial void AddImageTestPreviewTests(List<(string Name, Func<Task> Run)> tests);
    static partial void AddImageWindowVisibilityTests(List<(string Name, Func<Task> Run)> tests);

    private sealed class FakeInputSender : IMacroInputSender
    {
        public List<Win32Api.INPUT[]> Batches { get; } = new();
        public List<(int X, int Y)> Positions { get; } = new();
        public Func<int, Win32Api.INPUT[], uint>? OnSend { get; init; }
        public Func<uint, bool>? KeyState { get; init; }
        public Action<int, int>? OnMove { get; init; }
        public bool MoveResult { get; init; } = true;
        public uint SendInputs(Win32Api.INPUT[] inputs)
        {
            var index = Batches.Count;
            Batches.Add(inputs.ToArray());
            return OnSend?.Invoke(index, inputs) ?? (uint)inputs.Length;
        }
        public bool SetCursorPosition(int x, int y)
        {
            Positions.Add((x, y));
            OnMove?.Invoke(x, y);
            return MoveResult;
        }
        public bool IsKeyDown(uint virtualKey) => KeyState?.Invoke(virtualKey) ?? false;
    }
}
