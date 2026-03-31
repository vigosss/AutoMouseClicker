using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ming_AutoClicker.Helpers;
using Ming_AutoClicker.Services;

namespace Ming_AutoClicker.Views
{
    /// <summary>
    /// 匹配结果展示窗口 - 全屏覆盖显示匹配位置
    /// 
    /// 使用方式：
    ///   var result = _imageMatchService.TestMatch(path, threshold);
    ///   if (result.Found) new MatchResultWindow(result).ShowDialog();
    /// </summary>
    public partial class MatchResultWindow : Window
    {
        private readonly MatchResult _matchResult;
        private System.Drawing.Bitmap? _screenBitmap;

        /// <summary>
        /// 创建匹配结果窗口
        /// </summary>
        /// <param name="matchResult">匹配结果（必须 Found=true）</param>
        /// <param name="additionalInfo">额外显示的信息（可选）</param>
        public MatchResultWindow(MatchResult matchResult, string? additionalInfo = null)
        {
            InitializeComponent();

            _matchResult = matchResult ?? throw new ArgumentNullException(nameof(matchResult));

            Loaded += OnLoaded;
            KeyDown += OnKeyDown;

            // 存储额外信息，窗口加载后使用
            _additionalInfo = additionalInfo;
        }

        private readonly string? _additionalInfo;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // 捕获当前屏幕（使用共享的 Win32Api 方法）
                _screenBitmap = Win32Api.CaptureVirtualScreen();

                // 设置背景图片
                BackgroundImage.Source = BitmapToBitmapSource(_screenBitmap);
                BackgroundImage.Width = _screenBitmap.Width;
                BackgroundImage.Height = _screenBitmap.Height;

                // 绘制所有可视化元素
                DrawMatchHighlight();
                DrawInfoPanel();
                DrawBottomTip();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MatchResultWindow 加载失败: {ex.Message}");
                Close();
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        }

        /// <summary>
        /// 绘制匹配区域高亮：边框 + 半透明填充 + 中心十字线
        /// </summary>
        private void DrawMatchHighlight()
        {
            var rect = _matchResult.GetRectangle();
            var x = rect.X;
            var y = rect.Y;
            var w = rect.Width;
            var h = rect.Height;

            // 边框
            Canvas.SetLeft(MatchBorder, x);
            Canvas.SetTop(MatchBorder, y);
            MatchBorder.Width = w;
            MatchBorder.Height = h;
            MatchBorder.Visibility = Visibility.Visible;

            // 半透明填充
            Canvas.SetLeft(HighlightRect, x);
            Canvas.SetTop(HighlightRect, y);
            HighlightRect.Width = w;
            HighlightRect.Height = h;
            HighlightRect.Visibility = Visibility.Visible;

            // 水平十字线
            CrossH.X1 = x;
            CrossH.Y1 = _matchResult.Y;
            CrossH.X2 = x + w;
            CrossH.Y2 = _matchResult.Y;
            CrossH.Visibility = Visibility.Visible;

            // 垂直十字线
            CrossV.X1 = _matchResult.X;
            CrossV.Y1 = y;
            CrossV.X2 = _matchResult.X;
            CrossV.Y2 = y + h;
            CrossV.Visibility = Visibility.Visible;

            // 遮罩层（选区外部半透明）
            UpdateOverlay(new Rect(x, y, w, h));
        }

        /// <summary>
        /// 绘制左上角信息面板
        /// </summary>
        private void DrawInfoPanel()
        {
            var rect = _matchResult.GetRectangle();

            // 填充信息文本
            TxtPosition.Text = $"({_matchResult.X}, {_matchResult.Y})";
            TxtSimilarity.Text = $"{_matchResult.Similarity:P1}";
            TxtRegion.Text = $"{rect.Width} × {rect.Height}";
            TxtCenter.Text = $"({_matchResult.X}, {_matchResult.Y})";

            // 先测量面板尺寸
            InfoPanel.Visibility = Visibility.Visible;
            InfoPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var panelWidth = InfoPanel.DesiredSize.Width;
            var panelHeight = InfoPanel.DesiredSize.Height;

            // 定位到匹配区域左上角附近
            double panelX = rect.X;
            var panelY = rect.Y - panelHeight - 8;

            // 如果超出屏幕上方，放到匹配区域下方
            if (panelY < 8)
            {
                panelY = rect.Bottom + 8;
            }

            // 确保不超出左边界
            panelX = Math.Max(8, panelX);

            // 确保不超出屏幕右边界
            var screenW = _screenBitmap?.Width ?? 1920;
            if (panelX + panelWidth > screenW - 8)
            {
                panelX = screenW - panelWidth - 8;
            }

            Canvas.SetLeft(InfoPanel, panelX);
            Canvas.SetTop(InfoPanel, panelY);
        }

        /// <summary>
        /// 绘制底部提示
        /// </summary>
        private void DrawBottomTip()
        {
            BottomTip.Visibility = Visibility.Visible;
            BottomTip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var tipWidth = BottomTip.DesiredSize.Width;
            var tipHeight = BottomTip.DesiredSize.Height;

            var screenH = _screenBitmap?.Height ?? 1080;
            var screenW = _screenBitmap?.Width ?? 1920;

            // 水平居中，底部留 32px
            Canvas.SetLeft(BottomTip, (screenW - tipWidth) / 2);
            Canvas.SetTop(BottomTip, screenH - tipHeight - 32);
        }

        /// <summary>
        /// 更新遮罩层（匹配区域外部半透明，内部透明）
        /// </summary>
        private void UpdateOverlay(Rect selection)
        {
            if (selection.Width <= 0 || selection.Height <= 0) return;

            var screenW = _screenBitmap?.Width ?? 1920;
            var screenH = _screenBitmap?.Height ?? 1080;

            var fullRect = new RectangleGeometry(new Rect(0, 0, screenW, screenH));
            var cutoutRect = new RectangleGeometry(selection);
            var combined = new CombinedGeometry(GeometryCombineMode.Exclude, fullRect, cutoutRect);
            OverlayPath.Data = combined;
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
                Win32Api.DeleteObject(hBitmap);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _screenBitmap?.Dispose();
        }
    }
}