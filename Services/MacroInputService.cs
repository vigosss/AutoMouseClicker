using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Ming_AutoClicker.Helpers;
using Ming_AutoClicker.Models;

namespace Ming_AutoClicker.Services;

/// <summary>Separates native input from macro sequencing so tests never send desktop input.</summary>
public interface IMacroInputSender
{
    uint SendInputs(Win32Api.INPUT[] inputs);
    bool SetCursorPosition(int x, int y);
    bool IsKeyDown(uint virtualKey);
}

public sealed class Win32MacroInputSender : IMacroInputSender
{
    public uint SendInputs(Win32Api.INPUT[] inputs) =>
        Win32Api.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32Api.INPUT>());

    public bool SetCursorPosition(int x, int y) => Win32Api.SetCursorPos(x, y) &&
        Win32Api.GetCursorPos(out var actual) && actual.X == x && actual.Y == y;
    public bool IsKeyDown(uint virtualKey) => (Win32Api.GetAsyncKeyState((int)virtualKey) & 0x8000) != 0;
}

public sealed class MacroInputService
{
    private static readonly uint[] ModifierVirtualKeys = { 0xA2, 0xA3, 0xA4, 0xA5, 0xA0, 0xA1, 0x5B, 0x5C };
    private readonly IMacroInputSender _sender;

    public MacroInputService(IMacroInputSender? sender = null) => _sender = sender ?? new Win32MacroInputSender();

    /// <summary>Moves and clicks without an intervening delay; partial sends always release the button.</summary>
    public Task ClickAsync(int x, int y, bool rightButton, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_sender.SetCursorPosition(x, y))
            throw new InvalidOperationException(LocalizationService.Current.Format("MacroInputMoveFailed", x, y));
        cancellationToken.ThrowIfCancellationRequested();

        var down = new Win32Api.INPUT
        {
            type = Win32Api.INPUT_MOUSE,
            U = new Win32Api.InputUnion
            {
                mi = new Win32Api.MOUSEINPUT
                {
                    dwFlags = rightButton ? Win32Api.MOUSEEVENTF_RIGHTDOWN : Win32Api.MOUSEEVENTF_LEFTDOWN,
                    dwExtraInfo = Win32Api.InputMarker
                }
            }
        };
        var up = down;
        up.U.mi.dwFlags = rightButton ? Win32Api.MOUSEEVENTF_RIGHTUP : Win32Api.MOUSEEVENTF_LEFTUP;
        var needsRelease = true;
        Exception? failure = null;
        try
        {
            var sent = _sender.SendInputs(new[] { down, up });
            needsRelease = sent != 0 && sent != 2;
            if (sent != 2) throw InputFailure(sent, 2);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            if (needsRelease)
            {
                try
                {
                    var sent = _sender.SendInputs(new[] { up });
                    if (sent != 1) throw InputFailure(sent, 1);
                }
                catch (Exception releaseError)
                {
                    failure = new AggregateException(failure ?? releaseError, releaseError);
                }
            }
        }
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        return Task.CompletedTask;
    }

    public async Task ScrollAsync(MouseScrollAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        action = action.Clone();
        Validate(action);
        cancellationToken.ThrowIfCancellationRequested();

        if (action.PositionMode == ScrollPositionMode.Coordinates)
        {
            if (!_sender.SetCursorPosition(action.X, action.Y))
                throw new InvalidOperationException(LocalizationService.Current.Format("MacroInputMoveFailed", action.X, action.Y));
            // Give the target window time to observe the cursor position before routing the wheel event.
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        var delta = checked(action.Notches * 120);
        if (action.Direction is ScrollDirection.Down or ScrollDirection.Left) delta = -delta;
        var input = new Win32Api.INPUT
        {
            type = Win32Api.INPUT_MOUSE,
            U = new Win32Api.InputUnion
            {
                mi = new Win32Api.MOUSEINPUT
                {
                    dwFlags = action.Direction is ScrollDirection.Left or ScrollDirection.Right
                        ? Win32Api.MOUSEEVENTF_HWHEEL : Win32Api.MOUSEEVENTF_WHEEL,
                    mouseData = unchecked((uint)delta),
                    dwExtraInfo = Win32Api.InputMarker
                }
            }
        };
        // A held startup modifier must not turn wheel input into Ctrl+wheel zoom or Shift+wheel.
        await WaitForReleasedKeysAsync(null, cancellationToken).ConfigureAwait(false);
        var sent = _sender.SendInputs(new[] { input });
        if (sent != 1) throw InputFailure(sent, 1);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task PressKeyAsync(KeyPressAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        action = action.Clone();
        Validate(action);
        cancellationToken.ThrowIfCancellationRequested();

        await WaitForReleasedKeysAsync(action.VirtualKey, cancellationToken).ConfigureAwait(false);

        var downInputs = new List<Win32Api.INPUT>();
        if (action.Modifiers.HasFlag(KeyModifiers.Control)) downInputs.Add(KeyInput(0xA2));
        if (action.Modifiers.HasFlag(KeyModifiers.Alt)) downInputs.Add(KeyInput(0xA4));
        if (action.Modifiers.HasFlag(KeyModifiers.Shift)) downInputs.Add(KeyInput(0xA0));
        if (action.Modifiers.HasFlag(KeyModifiers.Windows)) downInputs.Add(KeyInput(0x5B, extended: true));
        downInputs.Add(KeyInput(action.VirtualKey, MacroKeyCatalog.IsExtended(action.VirtualKey)));

        var inputs = downInputs.Concat(downInputs.AsEnumerable().Reverse().Select(KeyUp)).ToArray();
        cancellationToken.ThrowIfCancellationRequested();

        // A throwing sender cannot report a prefix count. Until it returns, conservatively assume
        // every key may have gone down, so a failure cannot bypass release attempts.
        var pending = new List<Win32Api.INPUT>(downInputs);
        Exception? failure = null;
        var releaseFailures = new List<Exception>();
        try
        {
            var sent = _sender.SendInputs(inputs);
            if (sent <= inputs.Length)
            {
                pending.Clear();
                for (var i = 0; i < sent; i++)
                {
                    var input = inputs[i];
                    if ((input.U.ki.dwFlags & Win32Api.KEYEVENTF_KEYUP) == 0)
                        pending.Add(input);
                    else
                        pending.RemoveAll(key => key.U.ki.wVk == input.U.ki.wVk);
                }
            }
            if (sent != inputs.Length) throw InputFailure(sent, inputs.Length);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            // Cleanup deliberately ignores cancellation and attempts every remaining key even
            // if an earlier key-up fails. Retain each original key's extended/scancode flags.
            for (var i = pending.Count - 1; i >= 0; i--)
            {
                try
                {
                    var sent = _sender.SendInputs(new[] { KeyUp(pending[i]) });
                    if (sent != 1) releaseFailures.Add(InputFailure(sent, 1));
                }
                catch (Exception ex)
                {
                    releaseFailures.Add(ex);
                }
            }
        }

        if (releaseFailures.Count != 0)
        {
            if (failure != null) releaseFailures.Insert(0, failure);
            throw new InvalidOperationException(LocalizationService.Current.GetString("MacroInputReleaseFailed"),
                new AggregateException(releaseFailures));
        }
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static Win32Api.INPUT KeyInput(uint virtualKey, bool extended = false) => new()
    {
        type = Win32Api.INPUT_KEYBOARD,
        U = new Win32Api.InputUnion
        {
            ki = new Win32Api.KEYBDINPUT
            {
                wVk = (ushort)virtualKey,
                dwFlags = extended ? Win32Api.KEYEVENTF_EXTENDEDKEY : 0,
                dwExtraInfo = Win32Api.InputMarker
            }
        }
    };

    private static Win32Api.INPUT KeyUp(Win32Api.INPUT input)
    {
        input.U.ki.dwFlags |= Win32Api.KEYEVENTF_KEYUP;
        return input;
    }

    private static InvalidOperationException InputFailure(uint sent, int expected) =>
        new(LocalizationService.Current.Format("MacroInputSendFailed", sent, expected));

    private async Task WaitForReleasedKeysAsync(uint? mainKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Leave physical key state untouched, including keys held by the startup gesture.
        while (ModifierVirtualKeys.Any(_sender.IsKeyDown) ||
               (mainKey.HasValue && _sender.IsKeyDown(mainKey.Value)))
        {
            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void Validate(MacroAction action)
    {
        if (!MacroActionValidator.TryValidate(action, out var error))
            throw new ArgumentException(error, nameof(action));
    }
}
