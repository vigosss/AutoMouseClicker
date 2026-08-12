using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

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

        /// <summary>
        /// 执行鼠标中键点击
        /// </summary>
        public static bool MiddleClick(int x, int y)
        {
            if (!SetCursorPos(x, y))
            {
                return false;
            }

            mouse_event((uint)MouseEventFlags.MiddleDown, 0, 0, 0, 0);
            mouse_event((uint)MouseEventFlags.MiddleUp, 0, 0, 0, 0);
            return true;
        }

        #endregion

        #region 屏幕相关

        /// <summary>
        /// 获取系统度量值
        /// </summary>
        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        /// <summary>主屏幕宽度</summary>
        public const int SM_CXSCREEN = 0;
        /// <summary>主屏幕高度</summary>
        public const int SM_CYSCREEN = 1;
        /// <summary>虚拟屏幕左上角 X</summary>
        public const int SM_XVIRTUALSCREEN = 76;
        /// <summary>虚拟屏幕左上角 Y</summary>
        public const int SM_YVIRTUALSCREEN = 77;
        /// <summary>虚拟屏幕宽度</summary>
        public const int SM_CXVIRTUALSCREEN = 78;
        /// <summary>虚拟屏幕高度</summary>
        public const int SM_CYVIRTUALSCREEN = 79;

        /// <summary>
        /// 获取主屏幕尺寸
        /// </summary>
        public static System.Drawing.Size GetMainScreenSize()
        {
            return new System.Drawing.Size(
                GetSystemMetrics(SM_CXSCREEN),
                GetSystemMetrics(SM_CYSCREEN));
        }

        /// <summary>
        /// 获取虚拟屏幕（多显示器）的区域和尺寸
        /// </summary>
        public static (int x, int y, int width, int height) GetVirtualScreenBounds()
        {
            int x = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int y = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int w = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int h = GetSystemMetrics(SM_CYVIRTUALSCREEN);

            // 如果虚拟屏幕尺寸异常，回退到主屏幕
            if (w <= 0 || h <= 0)
            {
                w = GetSystemMetrics(SM_CXSCREEN);
                h = GetSystemMetrics(SM_CYSCREEN);
                x = 0;
                y = 0;
            }

            return (x, y, w, h);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_SHOWWINDOW = 0x0040;

        /// <summary>
        /// 将无边框覆盖窗口精确铺到整个虚拟桌面（而非只最大化到主屏幕）。
        /// 参数使用物理像素，因此也适用于副屏位于主屏左侧或上方的布局。
        /// </summary>
        public static void FitWindowToVirtualScreen(Window window)
        {
            var handle = new WindowInteropHelper(window).Handle;
            var (x, y, width, height) = GetVirtualScreenBounds();
            SetWindowPos(handle, HWND_TOPMOST, x, y, width, height, SWP_SHOWWINDOW);
        }

        /// <summary>
        /// 从屏幕捕获 Bitmap（虚拟屏幕，支持多显示器）
        /// </summary>
        public static System.Drawing.Bitmap CaptureVirtualScreen()
        {
            var (x, y, w, h) = GetVirtualScreenBounds();
            var bitmap = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var g = System.Drawing.Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h));
            }
            return bitmap;
        }

        /// <summary>
        /// 从屏幕捕获 Bitmap（仅主屏幕）
        /// </summary>
        public static System.Drawing.Bitmap CaptureMainScreen()
        {
            var size = GetMainScreenSize();
            var bitmap = new System.Drawing.Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var g = System.Drawing.Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(0, 0, 0, 0, size);
            }
            return bitmap;
        }

        #endregion

        #region GDI 相关

        /// <summary>
        /// 删除 GDI 对象
        /// </summary>
        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        /// <summary>
        /// 将 System.Drawing.Bitmap 转换为 WPF BitmapSource
        /// </summary>
        public static System.Windows.Media.Imaging.BitmapSource BitmapToBitmapSource(System.Drawing.Bitmap bitmap)
        {
            var hBitmap = bitmap.GetHbitmap();
            try
            {
                return System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap, IntPtr.Zero, System.Windows.Int32Rect.Empty,
                    System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DeleteObject(hBitmap);
            }
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
