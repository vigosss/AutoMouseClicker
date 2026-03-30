using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ming_AutoClicker.Views
{
    /// <summary>
    /// 区域截图窗口 - 全屏覆盖，用户拖拽选择截图区域
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

        private System.Windows.Point _startPoint;
        private bool _isSelecting;
        private readonly int _screenWidth;
        private readonly int _screenHeight;
        private readonly System.Drawing.Bitmap _screenBitmap;
        private System.Drawing.Rectangle? _selectedRegion;

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
                CancelSelection();
            }
        }

        private void RegionSelectWindow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _startPoint = e.GetPosition(MainCanvas);
            _isSelecting = true;

            SelectionRect.Visibility = Visibility.Visible;
            InfoPanel.Visibility = Visibility.Visible;
            TipText.Visibility = Visibility.Collapsed;

            // 初始化选区
            UpdateSelection(_startPoint, _startPoint);
        }

        private void RegionSelectWindow_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isSelecting) return;

            var currentPoint = e.GetPosition(MainCanvas);
            UpdateSelection(_startPoint, currentPoint);
        }

        private void RegionSelectWindow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isSelecting) return;

            _isSelecting = false;

            var endPoint = e.GetPosition(MainCanvas);

            // 计算选中区域
            var rect = GetNormalizedRect(_startPoint, endPoint);

            if (rect.Width < 5 || rect.Height < 5)
            {
                // 选区太小，视为取消
                CancelSelection();
                return;
            }

            _selectedRegion = new System.Drawing.Rectangle(
                (int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);

            SelectionCompleted?.Invoke(_selectedRegion);
            Close();
        }

        /// <summary>
        /// 更新选区显示
        /// </summary>
        private void UpdateSelection(System.Windows.Point start, System.Windows.Point end)
        {
            var rect = GetNormalizedRect(start, end);

            // 更新选区矩形
            Canvas.SetLeft(SelectionRect, rect.X);
            Canvas.SetTop(SelectionRect, rect.Y);
            SelectionRect.Width = rect.Width;
            SelectionRect.Height = rect.Height;

            // 更新遮罩（选区内透明，选区外半透明）
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
    }
}