using System.Runtime.InteropServices;
using Ming_AutoClicker.Helpers;
using Ming_AutoClicker.Models;

namespace Ming_AutoClicker.Services;

public sealed class PlaybackOptions
{
    public double SpeedFactor { get; set; } = 1;
    public int LoopCount { get; set; } = 1;
    public int LoopInterval { get; set; }
}

public sealed class PlaybackService : IDisposable
{
    private CancellationTokenSource? _cts;
    private readonly ManualResetEventSlim _resume = new(true);
    private readonly HashSet<uint> _pressedKeys = new();
    private readonly HashSet<RecordedMouseButton> _pressedButtons = new();
    public bool IsRunning { get; private set; }
    public bool IsPaused { get; private set; }
    public event Action<int>? OnActionExecuted;
    public event Action? OnPlaybackFinished;

    public async Task StartPlaybackAsync(Recording recording, PlaybackOptions options)
    {
        if (IsRunning) throw new InvalidOperationException("Playback is already running.");
        options.SpeedFactor = Math.Clamp(options.SpeedFactor, .5, 5); options.LoopCount = options.LoopCount < -1 ? 1 : options.LoopCount;
        _cts?.Dispose(); _cts = new CancellationTokenSource(); var token = _cts.Token; IsRunning = true; IsPaused = false; _resume.Set();
        try
        {
            var loops = 0;
            while (options.LoopCount == -1 || loops < Math.Max(1, options.LoopCount))
            {
                for (var i=0; i<recording.Actions.Count; i++)
                {
                    token.ThrowIfCancellationRequested(); await WaitWhilePaused(token);
                    var a=recording.Actions[i]; await DelayAsync((long)(a.DelayBeforeMs/options.SpeedFactor), token);
                    if (a.IsEnabled) Send(a, recording.DesktopBounds);
                    OnActionExecuted?.Invoke(i+1);
                }
                loops++; if (options.LoopCount == -1 || loops < options.LoopCount) await DelayAsync(Math.Max(0, options.LoopInterval), token);
            }
        }
        catch (OperationCanceledException) { }
        finally { ReleasePressed(); IsRunning=false; IsPaused=false; _resume.Set(); OnPlaybackFinished?.Invoke(); }
    }
    public void PausePlayback() { if (IsRunning) { IsPaused=true; _resume.Reset(); } }
    public void ResumePlayback() { IsPaused=false; _resume.Set(); }
    public void StopPlayback() { _cts?.Cancel(); _resume.Set(); }
    private async Task DelayAsync(long ms, CancellationToken token) { var left=ms; while(left>0) { await WaitWhilePaused(token); var part=(int)Math.Min(left,50); await Task.Delay(part,token); left-=part; } }
    private Task WaitWhilePaused(CancellationToken token) => Task.Run(() => _resume.Wait(token), token);

    private void Send(RecordedAction a, DesktopBounds source)
    {
        if (a.Type is RecordedActionType.KeyDown or RecordedActionType.KeyUp) { SendKey(a.VirtualKey,a.ScanCode,a.IsExtendedKey,a.Type==RecordedActionType.KeyUp); if(a.Type==RecordedActionType.KeyDown)_pressedKeys.Add(a.VirtualKey);else _pressedKeys.Remove(a.VirtualKey); return; }
        var current=Win32Api.GetVirtualScreenBounds(); var sw=Math.Max(1,source.Width); var sh=Math.Max(1,source.Height);
        var x=source.Width>0?current.x+(int)Math.Round((a.X-source.X)*(current.width-1d)/Math.Max(1,sw-1)):a.X;
        var y=source.Height>0?current.y+(int)Math.Round((a.Y-source.Y)*(current.height-1d)/Math.Max(1,sh-1)):a.Y;
        x=Math.Clamp(x,current.x,current.x+current.width-1); y=Math.Clamp(y,current.y,current.y+current.height-1);
        var nx=(int)Math.Round((x-current.x)*65535d/Math.Max(1,current.width-1)); var ny=(int)Math.Round((y-current.y)*65535d/Math.Max(1,current.height-1));
        uint flags=Win32Api.MOUSEEVENTF_MOVE|Win32Api.MOUSEEVENTF_ABSOLUTE|Win32Api.MOUSEEVENTF_VIRTUALDESK; uint data=0;
        if(a.Type==RecordedActionType.MouseDown) flags|=a.Button switch { RecordedMouseButton.Left=>Win32Api.MOUSEEVENTF_LEFTDOWN,RecordedMouseButton.Right=>Win32Api.MOUSEEVENTF_RIGHTDOWN,_=>Win32Api.MOUSEEVENTF_MIDDLEDOWN };
        if(a.Type==RecordedActionType.MouseUp) flags|=a.Button switch { RecordedMouseButton.Left=>Win32Api.MOUSEEVENTF_LEFTUP,RecordedMouseButton.Right=>Win32Api.MOUSEEVENTF_RIGHTUP,_=>Win32Api.MOUSEEVENTF_MIDDLEUP };
        if(a.Type==RecordedActionType.MouseWheel){flags|=Win32Api.MOUSEEVENTF_WHEEL;data=unchecked((uint)a.WheelDelta);}
        SendInputs(new Win32Api.INPUT { type=Win32Api.INPUT_MOUSE,U=new Win32Api.InputUnion{mi=new Win32Api.MOUSEINPUT{dx=nx,dy=ny,mouseData=data,dwFlags=flags,dwExtraInfo=Win32Api.InputMarker}}});
        if(a.Type==RecordedActionType.MouseDown)_pressedButtons.Add(a.Button);else if(a.Type==RecordedActionType.MouseUp)_pressedButtons.Remove(a.Button);
    }
    private static void SendKey(uint vk,uint scan,bool extended,bool up) { var f=(scan!=0?Win32Api.KEYEVENTF_SCANCODE:0)|(extended?Win32Api.KEYEVENTF_EXTENDEDKEY:0)|(up?Win32Api.KEYEVENTF_KEYUP:0); SendInputs(new Win32Api.INPUT{type=Win32Api.INPUT_KEYBOARD,U=new Win32Api.InputUnion{ki=new Win32Api.KEYBDINPUT{wVk=(ushort)(scan==0?vk:0),wScan=(ushort)scan,dwFlags=f,dwExtraInfo=Win32Api.InputMarker}}}); }
    private static void SendInputs(params Win32Api.INPUT[] inputs) { if(Win32Api.SendInput((uint)inputs.Length,inputs,Marshal.SizeOf<Win32Api.INPUT>())!=(uint)inputs.Length) throw new InvalidOperationException("SendInput failed."); }
    private void ReleasePressed(){foreach(var k in _pressedKeys.ToArray())SendKey(k,0,false,true);_pressedKeys.Clear();foreach(var b in _pressedButtons.ToArray()){var flag=b switch{RecordedMouseButton.Left=>Win32Api.MOUSEEVENTF_LEFTUP,RecordedMouseButton.Right=>Win32Api.MOUSEEVENTF_RIGHTUP,_=>Win32Api.MOUSEEVENTF_MIDDLEUP};SendInputs(new Win32Api.INPUT{type=Win32Api.INPUT_MOUSE,U=new Win32Api.InputUnion{mi=new Win32Api.MOUSEINPUT{dwFlags=flag,dwExtraInfo=Win32Api.InputMarker}}});}_pressedButtons.Clear();}
    public void Dispose(){StopPlayback();if(!IsRunning)_cts?.Dispose();}
}
