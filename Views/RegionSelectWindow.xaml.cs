using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ming_AutoClicker.Views
{
    /// <summary>
    /// 区域截图窗口 - 全屏覆盖，用户拖拽选择截图区域，支持调整选区
    /// </summary>
    public partial class RegionSelectWindow : Window
    {
        #region Win32 API

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;
        private const int SM_XVIRTUALSCREEN = 76;
        private const int SM_YVIRTUALSCREEN = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        #endregion

        #region 状态枚举

        /// <summary>
        /// 选区交互状态
        /// </summary>
        private enum SelectionState
        {
            /// <summary>等待/绘制中</summary>
            Drawing,
            /// <summary>调整模式（可移动/缩放选区）</summary>
            Adjusting
        }

        /// <summary>
        /// 调整操作类型
        /// </summary>
        private enum AdjustMode
        {
            None,
            Move,
            ResizeTL,
            ResizeT,
            ResizeTR,
            ResizeL,
            ResizeR,
            ResizeBL,
            ResizeB,
            ResizeBR
        }

        #endregion

        private readonly int _screenWidth;
        private readonly int _screenHeight;
        private readonly System.Drawing.Bitmap _screenBitmap;
        private System.Drawing.Rectangle? _selectedRegion;

        // 绘制状态
        private SelectionState _state = SelectionState.Drawing;
        private System.Windows.Point _startPoint;
        private bool _isSelecting;

        // 调整状态
        private AdjustMode _adjustMode = AdjustMode.None;
        private System.Windows.Point _adjustStartMouse;
        private Rect _adjustStartRect;

        // 手柄元素数组，方便批量操作
        private readonly Ellipse[] _handles;

        // 手柄命中检测半径（像素）
        private const double HandleHitRadius = 8;

        /// <summary>
        /// 用户选中的截图区域（屏幕坐标），null 表示取消
        /// </summary>
        public System.Drawing.Rectangle? SelectedRegion => _selectedRegion;

        /// <summary>
        /// 截图完成事件，参数为选中的区域（null 表示取消）
        /// </summary>
        public event Action<System.Drawing.Rectangle?>? SelectionCompleted;

        public RegionSelectWindow()
        {
            InitializeComponent();

            // 获取虚拟屏幕尺寸（支持多显示器）
            _screenWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            _screenHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            int virtualX = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int virtualY = GetSystemMetrics(SM_YVIRTUALSCREEN);

            // 如果虚拟屏幕尺寸异常，使用主屏幕
            if (_screenWidth <= 0 || _screenHeight <= 0)
            {
                _screenWidth = GetSystemMetrics(SM_CXSCREEN);
                _screenHeight = GetSystemMetrics(SM_CYSCREEN);
                virtualX = 0;
                virtualY = 0;
            }

            // 捕获当前屏幕
            _screenBitmap = new System.Drawing.Bitmap(_screenWidth, _screenHeight, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(_screenBitmap))
            {
                g.CopyFromScreen(virtualX, virtualY, 0, 0,
                    new System.Drawing.Size(_screenWidth, _screenHeight));
            }

            // 初始化手柄数组
            _handles = new[] { HandleTL, HandleT, HandleTR, HandleL, HandleR, HandleBL, HandleB, HandleBR };

            Loaded += RegionSelectWindow_Loaded;
            KeyDown += RegionSelectWindow_KeyDown;
            MouseLeftButtonDown += RegionSelectWindow_MouseLeftButtonDown;
            MouseMove += RegionSelectWindow_MouseMove;
            MouseLeftButtonUp += RegionSelectWindow_MouseLeftButtonUp;
        }

        private void RegionSelectWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 设置背景图片
            BackgroundImage.Source = BitmapToBitmapSource(_screenBitmap);
            BackgroundImage.Width = _screenWidth;
            BackgroundImage.Height = _screenHeight;

            // 初始化全屏遮罩
            UpdateOverlay(null);

            // 居中提示文字
            Canvas.SetLeft(TipText, (_screenWidth - TipText.ActualWidth) / 2);
            Canvas.SetTop(TipText, (_screenHeight - TipText.ActualHeight) / 2);
        }

        private void RegionSelectWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (_state == SelectionState.Adjusting)
                {
                    // 调整模式下 ESC 回到绘制模式
                    EnterDrawingState();
                }
                else
                {
                    CancelSelection();
                }
            }
            else if (e.Key == Key.Enter && _state == SelectionState.Adjusting)
            {
                // Enter 确认选区
                ConfirmSelection();
            }
        }

        #region 绘制阶段

        private void RegionSelectWindow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_state == SelectionState.Drawing)
            {
                _startPoint = e.GetPosition(MainCanvas);
                _isSelecting = true;

                SelectionRect.Visibility = Visibility.Visible;
                InfoPanel.Visibility = Visibility.Visible;
                TipText.Visibility = Visibility.Collapsed;

                UpdateSelection(_startPoint, _startPoint);
            }
            else if (_state == SelectionState.Adjusting)
            {
                var pos = e.GetPosition(MainCanvas);

                // 检测点击了哪个手柄
                _adjustMode = HitTestHandles(pos);

                if (_adjustMode == AdjustMode.None)
                {
                    // 检测是否在选区内部（移动）
                    var currentRect = GetCurrentSelectionRect();
                    if (currentRect.Contains(pos))
                    {
                        _adjustMode = AdjustMode.Move;
                    }
                }

                if (_adjustMode != AdjustMode.None)
                {
                    _adjustStartMouse = pos;
                    _adjustStartRect = GetCurrentSelectionRect();
                    CaptureMouse();
                }
            }
        }

        private void RegionSelectWindow_MouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(MainCanvas);

            if (_state == SelectionState.Drawing)
            {
                if (!_isSelecting) return;
                UpdateSelection(_startPoint, pos);
            }
            else if (_state == SelectionState.Adjusting)
            {
                if (_adjustMode != AdjustMode.None && IsMouseCaptured)
                {
                    // 执行调整操作
                    var newRect = ComputeAdjustedRect(pos);
                    ApplySelectionRect(newRect);
                }
                else
                {
                    // 更新光标样式
                    UpdateAdjustCursor(pos);
                }
            }
        }

        private void RegionSelectWindow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_state == SelectionState.Drawing)
            {
                if (!_isSelecting) return;

                _isSelecting = false;
                var endPoint = e.GetPosition(MainCanvas);
                var rect = GetNormalizedRect(_startPoint, endPoint);

                if (rect.Width < 5 || rect.Height < 5)
                {
                    // 选区太小，视为取消
                    CancelSelection();
                    return;
                }

                // 进入调整模式
                ApplySelectionRect(rect);
                EnterAdjustState();
            }
            else if (_state == SelectionState.Adjusting)
            {
                if (_adjustMode != AdjustMode.None)
                {
                    _adjustMode = AdjustMode.None;
                    ReleaseMouseCapture();

                    // 检查选区是否太小
                    var currentRect = GetCurrentSelectionRect();
                    if (currentRect.Width < 5 || currentRect.Height < 5)
                    {
                        EnterDrawingState();
                    }
                }
            }
        }

        #endregion

        #region 调整阶段

        /// <summary>
        /// 进入调整模式
        /// </summary>
        private void EnterAdjustState()
        {
            _state = SelectionState.Adjusting;
            _adjustMode = AdjustMode.None;

            // 显示手柄
            foreach (var handle in _handles)
            {
                handle.Visibility = Visibility.Visible;
            }

            // 显示工具栏
            ShowToolbar();

            // 隐藏提示文字
            TipText.Visibility = Visibility.Collapsed;

            // 更新光标
            Cursor = Cursors.Arrow;
        }

        /// <summary>
        /// 回到绘制模式
        /// </summary>
        private void EnterDrawingState()
        {
            _state = SelectionState.Drawing;
            _isSelecting = false;

            // 隐藏手柄
            foreach (var handle in _handles)
            {
                handle.Visibility = Visibility.Collapsed;
            }

            // 隐藏选区、信息面板、工具栏
            SelectionRect.Visibility = Visibility.Collapsed;
            InfoPanel.Visibility = Visibility.Collapsed;
            Toolbar.Visibility = Visibility.Collapsed;

            // 恢复遮罩
            UpdateOverlay(null);

            // 显示提示文字
            TipText.Visibility = Visibility.Visible;

            // 恢复十字光标
            Cursor = Cursors.Cross;
        }

        /// <summary>
        /// 确认当前选区
        /// </summary>
        private void ConfirmSelection()
        {
            var rect = GetCurrentSelectionRect();
            _selectedRegion = new System.Drawing.Rectangle(
                (int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);

            SelectionCompleted?.Invoke(_selectedRegion);
            Close();
        }

        /// <summary>
        /// 检测鼠标是否命中手柄
        /// </summary>
        private AdjustMode HitTestHandles(System.Windows.Point pos)
        {
            if (IsNearHandle(pos, HandleTL)) return AdjustMode.ResizeTL;
            if (IsNearHandle(pos, HandleTR)) return AdjustMode.ResizeTR;
            if (IsNearHandle(pos, HandleBL)) return AdjustMode.ResizeBL;
            if (IsNearHandle(pos, HandleBR)) return AdjustMode.ResizeBR;
            if (IsNearHandle(pos, HandleT))  return AdjustMode.ResizeT;
            if (IsNearHandle(pos, HandleB))  return AdjustMode.ResizeB;
            if (IsNearHandle(pos, HandleL))  return AdjustMode.ResizeL;
            if (IsNearHandle(pos, HandleR))  return AdjustMode.ResizeR;
            return AdjustMode.None;
        }

        /// <summary>
        /// 检测鼠标是否靠近某个手柄
        /// </summary>
        private bool IsNearHandle(System.Windows.Point pos, FrameworkElement handle)
        {
            if (handle.Visibility != Visibility.Visible) return false;

            var handleCenter = new System.Windows.Point(
                Canvas.GetLeft(handle) + handle.Width / 2,
                Canvas.GetTop(handle) + handle.Height / 2);

            var dx = pos.X - handleCenter.X;
            var dy = pos.Y - handleCenter.Y;
            return (dx * dx + dy * dy) <= HandleHitRadius * HandleHitRadius;
        }

        /// <summary>
        /// 根据鼠标位置计算调整后的矩形
        /// </summary>
        private Rect ComputeAdjustedRect(System.Windows.Point currentMouse)
        {
            var dx = currentMouse.X - _adjustStartMouse.X;
            var dy = currentMouse.Y - _adjustStartMouse.Y;
            var r = _adjustStartRect;

            double x = r.X, y = r.Y, w = r.Width, h = r.Height;

            switch (_adjustMode)
            {
                case AdjustMode.Move:
                    x = r.X + dx;
                    y = r.Y + dy;
                    break;
                case AdjustMode.ResizeTL:
                    x = r.X + dx;
                    y = r.Y + dy;
                    w = r.Width - dx;
                    h = r.Height - dy;
                    break;
                case AdjustMode.ResizeT:
                    y = r.Y + dy;
                    h = r.Height - dy;
                    break;
                case AdjustMode.ResizeTR:
                    y = r.Y + dy;
                    w = r.Width + dx;
                    h = r.Height - dy;
                    break;
                case AdjustMode.ResizeL:
                    x = r.X + dx;
                    w = r.Width - dx;
                    break;
                case AdjustMode.ResizeR:
                    w = r.Width + dx;
                    break;
                case AdjustMode.ResizeBL:
                    x = r.X + dx;
                    w = r.Width - dx;
                    h = r.Height + dy;
                    break;
                case AdjustMode.ResizeB:
                    h = r.Height + dy;
                    break;
                case AdjustMode.ResizeBR:
                    w = r.Width + dx;
                    h = r.Height + dy;
                    break;
            }

            // 限制在屏幕范围内
            x = Math.Max(0, x);
            y = Math.Max(0, y);

            // 确保最小尺寸
            if (w < 5) w = 5;
            if (h < 5) h = 5;

            // 确保不超出屏幕
            if (x + w > _screenWidth) w = _screenWidth - x;
            if (y + h > _screenHeight) h = _screenHeight - y;

            return new Rect(x, y, w, h);
        }

        /// <summary>
        /// 更新调整模式下的光标样式
        /// </summary>
        private void UpdateAdjustCursor(System.Windows.Point pos)
        {
            var hitHandle = HitTestHandles(pos);
            switch (hitHandle)
            {
                case AdjustMode.ResizeTL:
                case AdjustMode.ResizeBR:
                    Cursor = Cursors.SizeNWSE;
                    break;
                case AdjustMode.ResizeTR:
                case AdjustMode.ResizeBL:
                    Cursor = Cursors.SizeNESW;
                    break;
                case AdjustMode.ResizeT:
                case AdjustMode.ResizeB:
                    Cursor = Cursors.SizeNS;
                    break;
                case AdjustMode.ResizeL:
                case AdjustMode.ResizeR:
                    Cursor = Cursors.SizeWE;
                    break;
                default:
                    // 检查是否在选区内部
                    var currentRect = GetCurrentSelectionRect();
                    Cursor = currentRect.Contains(pos) ? Cursors.SizeAll : Cursors.Arrow;
                    break;
            }
        }

        #endregion

        #region UI 更新

        /// <summary>
        /// 更新选区显示（绘制阶段用）
        /// </summary>
        private void UpdateSelection(System.Windows.Point start, System.Windows.Point end)
        {
            var rect = GetNormalizedRect(start, end);
            ApplySelectionRect(rect);
        }

        /// <summary>
        /// 应用选区矩形到所有 UI 元素（选区框、遮罩、信息面板、手柄）
        /// </summary>
        private void ApplySelectionRect(Rect rect)
        {
            // 更新选区矩形
            Canvas.SetLeft(SelectionRect, rect.X);
            Canvas.SetTop(SelectionRect, rect.Y);
            SelectionRect.Width = rect.Width;
            SelectionRect.Height = rect.Height;

            // 更新遮罩
            UpdateOverlay(rect);

            // 更新尺寸信息
            SizeText.Text = $"{(int)rect.Width} × {(int)rect.Height}";
            Canvas.SetLeft(InfoPanel, rect.X);
            Canvas.SetTop(InfoPanel, rect.Y + rect.Height + 4);

            // 如果信息面板超出屏幕底部，放到选区上方
            if (rect.Y + rect.Height + InfoPanel.ActualHeight + 4 > _screenHeight)
            {
                Canvas.SetTop(InfoPanel, rect.Y - InfoPanel.ActualHeight - 4);
            }

            // 更新手柄位置
            UpdateHandles(rect);

            // 更新工具栏位置
            if (_state == SelectionState.Adjusting)
            {
                ShowToolbar();
            }
        }

        /// <summary>
        /// 更新 8 个调整手柄的位置
        /// </summary>
        private void UpdateHandles(Rect rect)
        {
            var cx = rect.X + rect.Width / 2;
            var cy = rect.Y + rect.Height / 2;
            var halfSize = 5.0; // 手柄宽度的一半

            // 上排
            SetHandlePosition(HandleTL, rect.X - halfSize, rect.Y - halfSize);
            SetHandlePosition(HandleT, cx - halfSize, rect.Y - halfSize);
            SetHandlePosition(HandleTR, rect.Right - halfSize, rect.Y - halfSize);

            // 中排
            SetHandlePosition(HandleL, rect.X - halfSize, cy - halfSize);
            SetHandlePosition(HandleR, rect.Right - halfSize, cy - halfSize);

            // 下排
            SetHandlePosition(HandleBL, rect.X - halfSize, rect.Bottom - halfSize);
            SetHandlePosition(HandleB, cx - halfSize, rect.Bottom - halfSize);
            SetHandlePosition(HandleBR, rect.Right - halfSize, rect.Bottom - halfSize);
        }

        /// <summary>
        /// 设置手柄在 Canvas 上的位置
        /// </summary>
        private static void SetHandlePosition(FrameworkElement handle, double x, double y)
        {
            Canvas.SetLeft(handle, x);
            Canvas.SetTop(handle, y);
        }

        /// <summary>
        /// 显示并定位底部工具栏
        /// </summary>
        private void ShowToolbar()
        {
            var rect = GetCurrentSelectionRect();
            Toolbar.Visibility = Visibility.Visible;

            // 确保 Toolbar 已测量
            Toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var toolbarWidth = Toolbar.DesiredSize.Width;
            var toolbarHeight = Toolbar.DesiredSize.Height;

            // 水平居中于选区
            var toolbarX = rect.X + (rect.Width - toolbarWidth) / 2;

            // 优先放在选区下方
            var toolbarY = rect.Bottom + 12;

            // 如果超出屏幕底部，放到选区上方
            if (toolbarY + toolbarHeight + 8 > _screenHeight)
            {
                toolbarY = rect.Y - toolbarHeight - 12;
            }

            // 确保不超出左右边界
            toolbarX = Math.Max(8, Math.Min(toolbarX, _screenWidth - toolbarWidth - 8));

            // 确保不超出上方
            if (toolbarY < 8) toolbarY = 8;

            Canvas.SetLeft(Toolbar, toolbarX);
            Canvas.SetTop(Toolbar, toolbarY);
        }

        /// <summary>
        /// 获取当前选区矩形
        /// </summary>
        private Rect GetCurrentSelectionRect()
        {
            if (SelectionRect.Visibility != Visibility.Visible)
                return Rect.Empty;

            return new Rect(
                Canvas.GetLeft(SelectionRect),
                Canvas.GetTop(SelectionRect),
                SelectionRect.Width,
                SelectionRect.Height);
        }

        /// <summary>
        /// 更新遮罩层（选区内透明，选区外半透明深色）
        /// </summary>
        private void UpdateOverlay(Rect? selection)
        {
            if (selection == null || selection.Value.Width <= 0 || selection.Value.Height <= 0)
            {
                // 全屏遮罩
                OverlayPath.Data = new RectangleGeometry(new Rect(0, 0, _screenWidth, _screenHeight));
                return;
            }

            var s = selection.Value;

            // 使用 CombinedGeometry 创建"挖洞"效果：选区外部有遮罩，选区内部透明
            var fullRect = new RectangleGeometry(new Rect(0, 0, _screenWidth, _screenHeight));
            var cutoutRect = new RectangleGeometry(s);
            var combined = new CombinedGeometry(GeometryCombineMode.Exclude, fullRect, cutoutRect);
            OverlayPath.Data = combined;
        }

        /// <summary>
        /// 获取标准化的矩形（确保 Width/Height 为正值）
        /// </summary>
        private static Rect GetNormalizedRect(System.Windows.Point start, System.Windows.Point end)
        {
            var x = Math.Min(start.X, end.X);
            var y = Math.Min(start.Y, end.Y);
            var width = Math.Abs(end.X - start.X);
            var height = Math.Abs(end.Y - start.Y);
            return new Rect(x, y, width, height);
        }

        #endregion

        #region 工具栏按钮事件

        private void BtnRedo_Click(object sender, RoutedEventArgs e)
        {
            EnterDrawingState();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            CancelSelection();
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            ConfirmSelection();
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 取消选择
        /// </summary>
        private void CancelSelection()
        {
            _selectedRegion = null;
            SelectionCompleted?.Invoke(null);
            Close();
        }

        /// <summary>
        /// Bitmap 转 BitmapSource
        /// </summary>
        private static BitmapSource BitmapToBitmapSource(System.Drawing.Bitmap bitmap)
        {
            var hBitmap = bitmap.GetHbitmap();
            try
            {
                return System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap, IntPtr.Zero, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _screenBitmap?.Dispose();
        }

        #endregion
    }
}