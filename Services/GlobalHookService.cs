using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Ming_AutoClicker.Helpers;
using Ming_AutoClicker.Models;

namespace Ming_AutoClicker.Services;

public sealed class GlobalHookService : IDisposable
{
    private IntPtr _mouseHook, _keyboardHook;
    private readonly Win32Api.HookProc _mouseProc, _keyboardProc;
    private readonly Stopwatch _clock = new();
    private readonly List<RecordedAction> _actions = new();
    private long _lastTime;
    private int _lastX, _lastY;
    private bool _hasMouse, _paused;
    public bool RecordMouseMove { get; set; } = true;
    public bool RecordKeyboard { get; set; } = true;
    public int MoveThreshold { get; set; } = 5;
    public Func<uint, bool>? IsControlKey { get; set; }
    public event Action<int>? OnActionRecorded;

    public GlobalHookService() { _mouseProc = MouseCallback; _keyboardProc = KeyboardCallback; }
    public void StartRecording()
    {
        if (_mouseHook != IntPtr.Zero) return;
        _actions.Clear(); _lastTime = 0; _hasMouse = false; _paused = false; _clock.Restart();
        var module = Win32Api.GetModuleHandle(null);
        _mouseHook = Win32Api.SetWindowsHookEx(Win32Api.WH_MOUSE_LL, _mouseProc, module, 0);
        _keyboardHook = Win32Api.SetWindowsHookEx(Win32Api.WH_KEYBOARD_LL, _keyboardProc, module, 0);
        if (_mouseHook == IntPtr.Zero || _keyboardHook == IntPtr.Zero) { StopRecording(); throw new InvalidOperationException("Unable to install global input hooks."); }
    }
    public void PauseRecording() => _paused = true;
    public void ResumeRecording() { _lastTime = _clock.ElapsedMilliseconds; _paused = false; }
    public List<RecordedAction> StopRecording()
    {
        if (_mouseHook != IntPtr.Zero) Win32Api.UnhookWindowsHookEx(_mouseHook);
        if (_keyboardHook != IntPtr.Zero) Win32Api.UnhookWindowsHookEx(_keyboardHook);
        _mouseHook = _keyboardHook = IntPtr.Zero; _clock.Stop();
        return _actions.Select(a => a.Clone()).ToList();
    }
    private void Add(RecordedAction a) { var now = _clock.ElapsedMilliseconds; a.DelayBeforeMs = Math.Max(0, now - _lastTime); _lastTime = now; _actions.Add(a); OnActionRecorded?.Invoke(_actions.Count); }
    private IntPtr MouseCallback(int code, IntPtr wp, IntPtr lp)
    {
        if (code >= 0 && !_paused) { var d = Marshal.PtrToStructure<Win32Api.MSLLHOOKSTRUCT>(lp); if (d.dwExtraInfo != Win32Api.InputMarker && (d.flags & 1) == 0) {
            var msg = wp.ToInt32(); var type = msg switch { Win32Api.WM_MOUSEMOVE => RecordedActionType.MouseMove, Win32Api.WM_MOUSEWHEEL => RecordedActionType.MouseWheel, Win32Api.WM_LBUTTONDOWN or Win32Api.WM_RBUTTONDOWN or Win32Api.WM_MBUTTONDOWN => RecordedActionType.MouseDown, _ => RecordedActionType.MouseUp };
            if (msg is Win32Api.WM_MOUSEMOVE or Win32Api.WM_MOUSEWHEEL or Win32Api.WM_LBUTTONDOWN or Win32Api.WM_LBUTTONUP or Win32Api.WM_RBUTTONDOWN or Win32Api.WM_RBUTTONUP or Win32Api.WM_MBUTTONDOWN or Win32Api.WM_MBUTTONUP) {
                if (type != RecordedActionType.MouseMove || RecordMouseMove && (!_hasMouse || Math.Abs(d.pt.X-_lastX) >= MoveThreshold || Math.Abs(d.pt.Y-_lastY) >= MoveThreshold)) {
                    _lastX=d.pt.X; _lastY=d.pt.Y; _hasMouse=true;
                    Add(new RecordedAction { Type=type, X=d.pt.X, Y=d.pt.Y, WheelDelta=type==RecordedActionType.MouseWheel?(short)(d.mouseData>>16):0, Button=msg is Win32Api.WM_LBUTTONDOWN or Win32Api.WM_LBUTTONUP?RecordedMouseButton.Left:msg is Win32Api.WM_RBUTTONDOWN or Win32Api.WM_RBUTTONUP?RecordedMouseButton.Right:msg is Win32Api.WM_MBUTTONDOWN or Win32Api.WM_MBUTTONUP?RecordedMouseButton.Middle:RecordedMouseButton.None });
                }
            }
        }} return Win32Api.CallNextHookEx(_mouseHook, code, wp, lp);
    }
    private IntPtr KeyboardCallback(int code, IntPtr wp, IntPtr lp)
    {
        if (code >= 0 && !_paused && RecordKeyboard) { var d=Marshal.PtrToStructure<Win32Api.KBDLLHOOKSTRUCT>(lp); if (d.dwExtraInfo != Win32Api.InputMarker && (d.flags&0x10)==0 && IsControlKey?.Invoke(d.vkCode)!=true) { var m=wp.ToInt32(); if (m is Win32Api.WM_KEYDOWN or Win32Api.WM_SYSKEYDOWN or Win32Api.WM_KEYUP or Win32Api.WM_SYSKEYUP) Add(new RecordedAction { Type=m is Win32Api.WM_KEYDOWN or Win32Api.WM_SYSKEYDOWN?RecordedActionType.KeyDown:RecordedActionType.KeyUp, VirtualKey=d.vkCode, ScanCode=d.scanCode, IsExtendedKey=(d.flags&1)!=0, KeyName=KeyInterop.KeyFromVirtualKey((int)d.vkCode).ToString() }); }}
        return Win32Api.CallNextHookEx(_keyboardHook, code, wp, lp);
    }
    public void Dispose() => StopRecording();
}
