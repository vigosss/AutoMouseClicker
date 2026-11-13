using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Ming_AutoClicker.Helpers;
using Ming_AutoClicker.Models;

namespace Ming_AutoClicker.Services;

public sealed class PlaybackOptions
{
    public double SpeedFactor { get; set; } = 1;
    public int LoopCount { get; set; } = 1;
    public int LoopInterval { get; set; }
}

public sealed record PlaybackProgress(int CompletedActions, int TotalActions, int CurrentLoop);
public enum PlaybackOutcome { Completed, Stopped }

public interface IPlaybackService : IDisposable
{
    bool IsRunning { get; }
    bool IsPaused { get; }
    Task<PlaybackOutcome> StartPlaybackAsync(Recording recording, PlaybackOptions options,
        Action<PlaybackProgress>? progress = null, CancellationToken cancellationToken = default);
    void PausePlayback();
    void ResumePlayback();
    void StopPlayback();
}

public sealed class PlaybackService : IPlaybackService
{
    private readonly object _gate = new();
    private readonly IMacroInputSender _sender;
    private readonly Func<DesktopBounds> _getDesktopBounds;
    private readonly Stopwatch _activeClock = new();
    private readonly Dictionary<(uint VirtualKey, bool Extended), PressedKey> _pressedKeys = new();
    private readonly HashSet<RecordedMouseButton> _pressedButtons = new();
    private CancellationTokenSource? _cts;
    private TaskCompletionSource<bool>? _resumeSignal;
    private bool _isRunning, _isPaused, _finishing, _disposed;

    private readonly record struct PressedKey(uint VirtualKey, uint ScanCode, bool Extended);

    public PlaybackService(IMacroInputSender? sender = null, Func<DesktopBounds>? getDesktopBounds = null)
    {
        _sender = sender ?? new Win32MacroInputSender();
        _getDesktopBounds = getDesktopBounds ?? (() =>
        {
            var bounds = Win32Api.GetVirtualScreenBounds();
            return new DesktopBounds { X = bounds.x, Y = bounds.y, Width = bounds.width, Height = bounds.height };
        });
    }

    public bool IsRunning { get { lock (_gate) return _isRunning; } }
    public bool IsPaused { get { lock (_gate) return _isPaused; } }

    public async Task<PlaybackOutcome> StartPlaybackAsync(Recording recording, PlaybackOptions options,
        Action<PlaybackProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(options);
        if (recording.Actions == null || !recording.Actions.Any(a => a != null && a.IsEnabled))
            throw new ArgumentException("The recording has no enabled actions.", nameof(recording));
        if (recording.Actions.Any(a => a == null || a.DelayBeforeMs < 0))
            throw new ArgumentException("The recording contains an invalid action.", nameof(recording));
        if (!double.IsFinite(options.SpeedFactor) || options.SpeedFactor is < .5 or > 5)
            throw new ArgumentOutOfRangeException(nameof(options), "Playback speed must be between 0.5 and 5.");
        if (options.LoopCount != -1 && options.LoopCount < 1 || options.LoopInterval < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Invalid playback loop settings.");

        // A run owns its data and callback; editing an item or starting a later run cannot change it.
        var snapshot = recording.DeepClone();
        var speed = options.SpeedFactor;
        var loopCount = options.LoopCount;
        var loopInterval = options.LoopInterval;
        var total = snapshot.ActionCount;
        CancellationTokenSource session;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_isRunning) throw new InvalidOperationException("Playback is already running.");
            session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _cts = session;
            _isRunning = true;
            _isPaused = _finishing = false;
            _resumeSignal = null;
            _activeClock.Restart();
        }

        var token = session.Token;
        var outcome = PlaybackOutcome.Completed;
        Exception? failure = null;
        try
        {
            // Return control before sending input, including recordings with no action delays.
            await Task.Delay(1, token).ConfigureAwait(false);
            var currentLoop = 0;
            while (loopCount == -1 || currentLoop < loopCount)
            {
                currentLoop = currentLoop == int.MaxValue ? int.MaxValue : currentLoop + 1;
                var completed = 0;
                progress?.Invoke(new PlaybackProgress(0, total, currentLoop));
                foreach (var action in snapshot.Actions)
                {
                    await DelayAsync(action.DelayBeforeMs / speed, token).ConfigureAwait(false);
                    // Pause/stop and the send share a gate so that a request cannot slip between them.
                    while (true)
                    {
                        await WaitWhilePausedAsync(token).ConfigureAwait(false);
                        lock (_gate)
                        {
                            token.ThrowIfCancellationRequested();
                            if (_isPaused) continue;
                            if (action.IsEnabled) Send(action, snapshot.DesktopBounds);
                            break;
                        }
                    }
                    if (action.IsEnabled)
                        progress?.Invoke(new PlaybackProgress(++completed, total, currentLoop));
                    token.ThrowIfCancellationRequested();
                }
                if (loopCount == -1 || currentLoop < loopCount)
                {
                    await DelayAsync(loopInterval, token).ConfigureAwait(false);
                    // Infinite, zero-delay recordings must always yield and remain stoppable.
                    await Task.Delay(1, token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            outcome = PlaybackOutcome.Stopped;
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            lock (_gate)
            {
                _finishing = true;
                _activeClock.Stop();
            }
            try { ReleasePressed(); }
            catch (Exception ex) { failure = failure == null ? ex : new AggregateException(failure, ex); }
            finally
            {
                lock (_gate)
                {
                    _isRunning = _isPaused = _finishing = false;
                    _resumeSignal?.TrySetResult(true);
                    _resumeSignal = null;
                    _cts = null;
                    session.Dispose();
                }
            }
        }

        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        return outcome;
    }

    public void PausePlayback()
    {
        lock (_gate)
        {
            if (!_isRunning || _isPaused || _finishing || _cts!.IsCancellationRequested) return;
            _isPaused = true;
            _activeClock.Stop();
            _resumeSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void ResumePlayback()
    {
        lock (_gate)
        {
            if (!_isRunning || !_isPaused || _finishing || _cts!.IsCancellationRequested) return;
            _isPaused = false;
            _activeClock.Start();
            _resumeSignal?.TrySetResult(true);
            _resumeSignal = null;
        }
    }

    public void StopPlayback()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _resumeSignal?.TrySetResult(true);
        }
    }

    private async Task WaitWhilePausedAsync(CancellationToken token)
    {
        while (true)
        {
            Task? wait;
            lock (_gate)
            {
                token.ThrowIfCancellationRequested();
                wait = _isPaused ? _resumeSignal?.Task : null;
            }
            if (wait == null) return;
            await wait.WaitAsync(token).ConfigureAwait(false);
        }
    }

    private async Task DelayAsync(double milliseconds, CancellationToken token)
    {
        double deadline;
        lock (_gate) deadline = _activeClock.Elapsed.TotalMilliseconds + milliseconds;
        while (true)
        {
            await WaitWhilePausedAsync(token).ConfigureAwait(false);
            double remaining;
            lock (_gate) remaining = deadline - _activeClock.Elapsed.TotalMilliseconds;
            if (remaining <= 0) return;
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(remaining, 25)), token).ConfigureAwait(false);
        }
    }

    private void Send(RecordedAction action, DesktopBounds source)
    {
        if (action.Type is RecordedActionType.KeyDown or RecordedActionType.KeyUp)
        {
            var key = new PressedKey(action.VirtualKey, action.ScanCode, action.IsExtendedKey);
            var identity = (key.VirtualKey, key.Extended);
            // Retain cleanup information even if a native sender throws after injecting a key down.
            if (action.Type == RecordedActionType.KeyDown) _pressedKeys[identity] = key;
            SendKey(key, action.Type == RecordedActionType.KeyUp);
            if (action.Type == RecordedActionType.KeyUp) _pressedKeys.Remove(identity);
            return;
        }

        var current = _getDesktopBounds();
        if (current.Width <= 0 || current.Height <= 0)
            throw new InvalidOperationException("The virtual desktop is unavailable.");
        var x = source.Width > 0
            ? current.X + (int)Math.Round((action.X - source.X) * (current.Width - 1d) / Math.Max(1, source.Width - 1))
            : action.X;
        var y = source.Height > 0
            ? current.Y + (int)Math.Round((action.Y - source.Y) * (current.Height - 1d) / Math.Max(1, source.Height - 1))
            : action.Y;
        x = Math.Clamp(x, current.X, current.X + current.Width - 1);
        y = Math.Clamp(y, current.Y, current.Y + current.Height - 1);
        var nx = (int)Math.Round((x - current.X) * 65535d / Math.Max(1, current.Width - 1));
        var ny = (int)Math.Round((y - current.Y) * 65535d / Math.Max(1, current.Height - 1));
        uint flags = Win32Api.MOUSEEVENTF_MOVE | Win32Api.MOUSEEVENTF_ABSOLUTE | Win32Api.MOUSEEVENTF_VIRTUALDESK;
        uint data = 0;
        if (action.Type == RecordedActionType.MouseDown)
        {
            flags |= action.Button switch
            {
                RecordedMouseButton.Left => Win32Api.MOUSEEVENTF_LEFTDOWN,
                RecordedMouseButton.Right => Win32Api.MOUSEEVENTF_RIGHTDOWN,
                _ => Win32Api.MOUSEEVENTF_MIDDLEDOWN
            };
            _pressedButtons.Add(action.Button);
        }
        if (action.Type == RecordedActionType.MouseUp) flags |= ButtonUpFlag(action.Button);
        if (action.Type == RecordedActionType.MouseWheel)
        {
            flags |= Win32Api.MOUSEEVENTF_WHEEL;
            data = unchecked((uint)action.WheelDelta);
        }
        SendInput(new Win32Api.INPUT
        {
            type = Win32Api.INPUT_MOUSE,
            U = new Win32Api.InputUnion
            {
                mi = new Win32Api.MOUSEINPUT
                {
                    dx = nx, dy = ny, mouseData = data, dwFlags = flags, dwExtraInfo = Win32Api.InputMarker
                }
            }
        });
        if (action.Type == RecordedActionType.MouseUp) _pressedButtons.Remove(action.Button);
    }

    private void SendKey(PressedKey key, bool up)
    {
        var flags = (key.ScanCode != 0 ? Win32Api.KEYEVENTF_SCANCODE : 0)
            | (key.Extended ? Win32Api.KEYEVENTF_EXTENDEDKEY : 0)
            | (up ? Win32Api.KEYEVENTF_KEYUP : 0);
        SendInput(new Win32Api.INPUT
        {
            type = Win32Api.INPUT_KEYBOARD,
            U = new Win32Api.InputUnion
            {
                ki = new Win32Api.KEYBDINPUT
                {
                    wVk = (ushort)(key.ScanCode == 0 ? key.VirtualKey : 0), wScan = (ushort)key.ScanCode,
                    dwFlags = flags, dwExtraInfo = Win32Api.InputMarker
                }
            }
        });
    }

    private void SendInput(Win32Api.INPUT input)
    {
        if (_sender.SendInputs(new[] { input }) != 1)
            throw new InvalidOperationException("SendInput failed.");
    }

    private static uint ButtonUpFlag(RecordedMouseButton button) => button switch
    {
        RecordedMouseButton.Left => Win32Api.MOUSEEVENTF_LEFTUP,
        RecordedMouseButton.Right => Win32Api.MOUSEEVENTF_RIGHTUP,
        _ => Win32Api.MOUSEEVENTF_MIDDLEUP
    };

    private void ReleasePressed()
    {
        var failures = new List<Exception>();
        foreach (var key in _pressedKeys.Values)
        {
            try { SendKey(key, true); }
            catch (Exception ex) { failures.Add(ex); }
        }
        _pressedKeys.Clear();
        foreach (var button in _pressedButtons)
        {
            try
            {
                SendInput(new Win32Api.INPUT
                {
                    type = Win32Api.INPUT_MOUSE,
                    U = new Win32Api.InputUnion
                    {
                        mi = new Win32Api.MOUSEINPUT { dwFlags = ButtonUpFlag(button), dwExtraInfo = Win32Api.InputMarker }
                    }
                });
            }
            catch (Exception ex) { failures.Add(ex); }
        }
        _pressedButtons.Clear();
        if (failures.Count > 0) throw new AggregateException("Could not release all recorded input.", failures);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _cts?.Cancel();
            _resumeSignal?.TrySetResult(true);
        }
    }
}
