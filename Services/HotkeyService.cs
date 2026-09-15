using System;
using System.Runtime.InteropServices;
using Ming_AutoClicker.Helpers;
using Ming_AutoClicker.Models;

namespace Ming_AutoClicker.Services
{
    /// <summary>
    /// 热键服务 - 管理全局热键注册和响应
    /// </summary>
    public class HotkeyService : IDisposable
    {
        private IntPtr _windowHandle;
        private bool _isRegistered;
        private bool _disposed;
        private static int _nextHotkeyId = 9000;
        private static readonly object _idLock = new object();

        /// <summary>
        /// 热键触发事件
        /// </summary>
        public event EventHandler<HotkeyEventArgs>? HotkeyPressed;

        /// <summary>
        /// 当前注册的热键 ID
        /// </summary>
        public int CurrentHotkeyId { get; private set; }

        /// <summary>
        /// 当前注册的修饰键
        /// </summary>
        public HotkeyModifierKeys CurrentModifiers { get; private set; }

        /// <summary>
        /// 当前注册的虚拟键码
        /// </summary>
        public uint CurrentKey { get; private set; }

        public HotkeyGesture? CurrentGesture => _isRegistered
            ? new HotkeyGesture { Modifiers = CurrentModifiers, VirtualKey = CurrentKey }
            : null;

        /// <summary>
        /// 热键是否已注册
        /// </summary>
        public bool IsRegistered => _isRegistered;

        /// <summary>
        /// 注册 F8 热键（默认）
        /// </summary>
        /// <param name="windowHandle">窗口句柄</param>
        /// <returns>是否注册成功</returns>
        public bool RegisterF8(IntPtr windowHandle)
        {
            return TryRegister(windowHandle, HotkeyGesture.Default).Success;
        }

        /// <summary>
        /// 注册自定义热键
        /// </summary>
        /// <param name="windowHandle">窗口句柄</param>
        /// <param name="modifiers">修饰键</param>
        /// <param name="key">虚拟键码</param>
        /// <param name="hotkeyId">热键 ID（可选，0 表示自动分配）</param>
        /// <returns>是否注册成功</returns>
        public bool Register(IntPtr windowHandle, Win32Api.HotkeyModifiers modifiers, Win32Api.VirtualKeyCodes key, int hotkeyId = 0)
        {
            var gesture = new HotkeyGesture
            {
                Modifiers = (HotkeyModifierKeys)(uint)modifiers,
                VirtualKey = (uint)key
            };
            return TryRegister(windowHandle, gesture, hotkeyId).Success;
        }

        /// <summary>
        /// 原子切换全局热键。新热键注册失败时，旧热键保持有效。
        /// </summary>
        public HotkeyRegistrationResult TryRegister(IntPtr windowHandle, HotkeyGesture gesture, int hotkeyId = 0)
        {
            if (!HotkeyGestureHelper.TryValidate(gesture, out var validationError))
                return HotkeyRegistrationResult.Failed(HotkeyRegistrationFailure.InvalidGesture, validationError);

            if (_isRegistered && _windowHandle == windowHandle && CurrentGesture!.Equals(gesture))
                return HotkeyRegistrationResult.Succeeded();

            // 自动分配 ID
            if (hotkeyId == 0)
            {
                lock (_idLock)
                {
                    hotkeyId = _nextHotkeyId++;
                    if (_nextHotkeyId > 0xBFFF) // Win32 热键 ID 上限
                        _nextHotkeyId = 9000;
                }
            }

            try
            {
                var registered = Win32Api.RegisterHotKey(
                    windowHandle, hotkeyId, (uint)gesture.Modifiers, gesture.VirtualKey);
                if (!registered)
                {
                    var errorCode = Marshal.GetLastWin32Error();
                    System.Diagnostics.Debug.WriteLine($"热键注册失败，错误码: {errorCode}");
                    return errorCode == 1409
                        ? HotkeyRegistrationResult.Failed(
                            HotkeyRegistrationFailure.AlreadyRegistered,
                            "该快捷键已被其他程序占用，请换一个组合",
                            errorCode)
                        : HotkeyRegistrationResult.Failed(
                            HotkeyRegistrationFailure.SystemError,
                            $"系统无法注册该快捷键（错误码 {errorCode}）",
                            errorCode);
                }

                // 新组合成功后才注销旧组合，确保切换失败时仍可停止正在进行的操作。
                if (_isRegistered && !Win32Api.UnregisterHotKey(_windowHandle, CurrentHotkeyId))
                {
                    var errorCode = Marshal.GetLastWin32Error();
                    Win32Api.UnregisterHotKey(windowHandle, hotkeyId);
                    return HotkeyRegistrationResult.Failed(
                        HotkeyRegistrationFailure.SystemError,
                        $"无法注销原快捷键（错误码 {errorCode}）",
                        errorCode);
                }

                _windowHandle = windowHandle;
                CurrentHotkeyId = hotkeyId;
                CurrentModifiers = gesture.Modifiers;
                CurrentKey = gesture.VirtualKey;
                _isRegistered = true;
                return HotkeyRegistrationResult.Succeeded();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"热键注册异常: {ex.Message}");
                return HotkeyRegistrationResult.Failed(
                    HotkeyRegistrationFailure.SystemError,
                    $"注册快捷键时发生错误：{ex.Message}");
            }
        }

        /// <summary>
        /// 注销当前热键
        /// </summary>
        /// <returns>是否注销成功</returns>
        public bool Unregister()
        {
            if (!_isRegistered || _windowHandle == IntPtr.Zero)
            {
                return true;
            }

            try
            {
                var result = Win32Api.UnregisterHotKey(_windowHandle, CurrentHotkeyId);
                
                if (result)
                {
                    System.Diagnostics.Debug.WriteLine($"热键已注销: ID {CurrentHotkeyId}");
                    _isRegistered = false;
                }
                else
                {
                    var errorCode = Marshal.GetLastWin32Error();
                    System.Diagnostics.Debug.WriteLine($"热键注销失败，错误码: {errorCode}");
                }

                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"热键注销异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 处理窗口消息（在窗口的 WndProc 中调用）
        /// </summary>
        /// <param name="message">消息 ID</param>
        /// <param name="wParam">WParam</param>
        /// <param name="lParam">LParam</param>
        /// <returns>是否处理了热键消息</returns>
        public bool HandleMessage(int message, IntPtr wParam, IntPtr lParam)
        {
            if (message == Win32Api.WM_HOTKEY)
            {
                var hotkeyId = wParam.ToInt32();
                
                if (hotkeyId == CurrentHotkeyId)
                {
                    // 触发热键事件
                    OnHotkeyPressed(new HotkeyEventArgs
                    {
                        HotkeyId = hotkeyId,
                        Modifiers = CurrentModifiers,
                        Key = CurrentKey
                    });

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 触发热键事件
        /// </summary>
        protected virtual void OnHotkeyPressed(HotkeyEventArgs e)
        {
            HotkeyPressed?.Invoke(this, e);
        }

        /// <summary>
        /// 获取当前热键的描述文本
        /// </summary>
        public string GetCurrentHotkeyDescription()
        {
            if (!_isRegistered)
                return "未注册";

            return HotkeyGestureHelper.Format(CurrentGesture);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Unregister();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// 热键事件参数
    /// </summary>
    public class HotkeyEventArgs : EventArgs
    {
        /// <summary>
        /// 热键 ID
        /// </summary>
        public int HotkeyId { get; set; }

        /// <summary>
        /// 修饰键
        /// </summary>
        public HotkeyModifierKeys Modifiers { get; set; }

        /// <summary>
        /// 虚拟键码
        /// </summary>
        public uint Key { get; set; }

        /// <summary>
        /// 获取热键描述
        /// </summary>
        public string Description
        {
            get
            {
                return HotkeyGestureHelper.Format(new HotkeyGesture
                {
                    Modifiers = Modifiers,
                    VirtualKey = Key
                });
            }
        }
    }

    public enum HotkeyRegistrationFailure
    {
        None,
        InvalidGesture,
        AlreadyRegistered,
        SystemError,
        SettingsSaveFailed
    }

    public sealed class HotkeyRegistrationResult
    {
        public bool Success { get; private init; }
        public HotkeyRegistrationFailure Failure { get; private init; }
        public string Message { get; private init; } = string.Empty;
        public int ErrorCode { get; private init; }

        public static HotkeyRegistrationResult Succeeded() => new HotkeyRegistrationResult { Success = true };

        public static HotkeyRegistrationResult Failed(
            HotkeyRegistrationFailure failure,
            string message,
            int errorCode = 0)
        {
            return new HotkeyRegistrationResult
            {
                Failure = failure,
                Message = message,
                ErrorCode = errorCode
            };
        }
    }
}
