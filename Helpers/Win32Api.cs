using System;
using System.Runtime.InteropServices;

namespace Ming_AutoClicker.Helpers
{
    /// <summary>
    /// Win32 API 封装 - 用于全局热键和鼠标模拟
    /// </summary>
    public static class Win32Api
    {
        #region 热键相关

        /// <summary>
        /// 注册全局热键
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        /// <summary>
        /// 注销全局热键
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        /// <summary>
        /// 热键修饰键
        /// </summary>
        [Flags]
        public enum HotkeyModifiers : uint
        {
            None = 0,
            Alt = 1,
            Control = 2,
            Shift = 4,
            Win = 8
        }

        /// <summary>
        /// 虚拟键码
        /// </summary>
        public enum VirtualKeyCodes : uint
        {
            F1 = 0x70,
            F2 = 0x71,
            F3 = 0x72,
            F4 = 0x73,
            F5 = 0x74,
            F6 = 0x75,
            F7 = 0x76,
            F8 = 0x77,
            F9 = 0x78,
            F10 = 0x79,
            F11 = 0x7A,
            F12 = 0x7B
        }

        /// <summary>
        /// 热键消息
        /// </summary>
        public const int WM_HOTKEY = 0x0312;

        #endregion

        #region 鼠标相关

        /// <summary>
        /// 鼠标事件标志
        /// </summary>
        [Flags]
        public enum MouseEventFlags : uint
        {
            Move = 0x0001,
            LeftDown = 0x0002,
            LeftUp = 0x0004,
            RightDown = 0x0008,
            RightUp = 0x0010,
            MiddleDown = 0x0020,
            MiddleUp = 0x0040,
            XDown = 0x0080,
            XUp = 0x0100,
            Wheel = 0x0800,
            Absolute = 0x8000
        }

        /// <summary>
        /// 模拟鼠标事件
        /// </summary>
        [DllImport("user32.dll")]
        public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint cButtons, uint dwExtraInfo);

        /// <summary>
        /// 设置鼠标位置
        /// </summary>
        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int x, int y);

        /// <summary>
        /// 获取鼠标位置
        /// </summary>
        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        /// <summary>
        /// 点结构
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        /// <summary>
        /// 执行鼠标左键点击
        /// </summary>
        public static bool LeftClick(int x, int y)
        {
            if (!SetCursorPos(x, y))
            {
                return false;
            }

            mouse_event((uint)MouseEventFlags.LeftDown, 0, 0, 0, 0);
            mouse_event((uint)MouseEventFlags.LeftUp, 0, 0, 0, 0);
            return true;
        }

        /// <summary>
        /// 执行鼠标右键点击
        /// </summary>
        public static bool RightClick(int x, int y)
        {
            if (!SetCursorPos(x, y))
            {
                return false;
            }

            mouse_event((uint)MouseEventFlags.RightDown, 0, 0, 0, 0);
            mouse_event((uint)MouseEventFlags.RightUp, 0, 0, 0, 0);
            return true;
        }

        #endregion

        #region 窗口相关

        /// <summary>
        /// 获取前台窗口句柄
        /// </summary>
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        /// <summary>
        /// 获取窗口矩形
        /// </summary>
        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        /// <summary>
        /// 矩形结构
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        #endregion
    }
}