using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ming_AutoClicker.Helpers;
using Drawing = System.Drawing;

namespace Ming_AutoClicker.Views.Shared
{
    /// <summary>
    /// 坐标拾取窗口 - 全屏覆盖屏幕截图背景，按住鼠标拖动拾取屏幕坐标
    /// </summary>
    public partial class CoordinatePickWindow : Window
    {
        private readonly int _screenWidth;
        private readonly int _screenHeight;
        private readonly int _screenX;
        private readonly int _screenY;
        private readonly System.Drawing.Bitmap _screenBitmap;
        private readonly bool _pickColor;
        private readonly SolidColorBrush _swatchBrush = new();
        private bool _isDragging;
        public string? SelectedColorHex { get; private set; }

        /// <summary>
        /// 坐标拾取完成事件
        /// </summary>
        public event Action<int, int>? CoordinatePicked;

        public CoordinatePickWindow() : this(false) { }

        public CoordinatePickWindow(bool pickColor)
            : this(pickColor, Win32Api.GetVirtualScreenBounds(), Win32Api.CaptureVirtualScreen) { }

        private CoordinatePickWindow(bool pickColor, (int X, int Y, int Width, int Height) desktop, Func<Drawing.Bitmap> capture)
        {
            InitializeComponent();
            _pickColor = pickColor;
            if (pickColor)
            {
                SetResourceReference(TitleProperty, "ColorPickScreen");
                TipText.SetResourceReference(TextBlock.TextProperty, "ColorPickInstruction");
                RenderOptions.SetBitmapScalingMode(BackgroundImage, BitmapScalingMode.NearestNeighbor);
                OverlayPath.Visibility = Visibility.Collapsed;
                ColorSwatch.Background = _swatchBrush;
                ColorSwatch.Visibility = Visibility.Visible;
            }

            (_screenX, _screenY, _screenWidth, _screenHeight) = desktop;
            _screenBitmap = capture();

            Loaded += CoordinatePickWindow_Loaded;
            SourceInitialized += (_, _) => Win32Api.FitWindowToVirtualScreen(this);
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    DialogResult = false;
                    Close();
                }
            };
        }

        private void CoordinatePickWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 设置背景截图
            BackgroundImage.Source = Win32Api.BitmapToBitmapSource(_screenBitmap);
            BackgroundImage.Width = CanvasWidth;
            BackgroundImage.Height = CanvasHeight;

            // 全屏半透明遮罩
            OverlayPath.Data = new RectangleGeometry(new Rect(0, 0, CanvasWidth, CanvasHeight));

            // 居中提示文字
            Canvas.SetLeft(TipText, (CanvasWidth - TipText.ActualWidth) / 2);
            Canvas.SetTop(TipText, (CanvasHeight - TipText.ActualHeight) / 2);
            if (_pickColor) UpdateCrosshair(Mouse.GetPosition(MainCanvas));
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _isDragging = true;
                TipText.Visibility = Visibility.Collapsed;
                UpdateCrosshair(e.GetPosition(MainCanvas));
                e.Handled = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_isDragging || _pickColor)
            {
                UpdateCrosshair(e.GetPosition(MainCanvas));
                e.Handled = true;
            }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            if (_isDragging && e.LeftButton == MouseButtonState.Released)
            {
                _isDragging = false;

                ConfirmSelection(e.GetPosition(MainCanvas));

                DialogResult = true;
                Close();
                e.Handled = true;
            }
        }

        private void ConfirmSelection(Point position)
        {
            var (screenX, screenY) = ToScreenCoordinates(position);
            if (_pickColor) SelectedColorHex = UpdateColorPreview(screenX, screenY);
            CoordinatePicked?.Invoke(screenX, screenY);
        }

        /// <summary>
        /// 更新十字准星和坐标信息面板
        /// </summary>
        private void UpdateCrosshair(Point pos)
        {
            double x = pos.X;
            double y = pos.Y;

            // 更新竖线（全屏高度）
            CrossV.X1 = x;
            CrossV.Y1 = 0;
            CrossV.X2 = x;
            CrossV.Y2 = CanvasHeight;

            // 更新横线（全屏宽度）
            CrossH.X1 = 0;
            CrossH.Y1 = y;
            CrossH.X2 = CanvasWidth;
            CrossH.Y2 = y;

            // 更新坐标信息
            var (screenX, screenY) = ToScreenCoordinates(pos);
            if (_pickColor) UpdateColorPreview(screenX, screenY);
            else CoordText.Text = $"X: {screenX}  Y: {screenY}";
            InfoPanel.Visibility = Visibility.Visible;

            var monitor = _pickColor
                ? System.Windows.Forms.Screen.FromPoint(new Drawing.Point(screenX, screenY)).Bounds
                : new Drawing.Rectangle(_screenX, _screenY, _screenBitmap.Width, _screenBitmap.Height);
            PlaceInfoPanel(pos, monitor);
        }

        private void PlaceInfoPanel(Point cursor, Drawing.Rectangle monitor)
        {
            var scaleX = CanvasWidth / _screenBitmap.Width;
            var scaleY = CanvasHeight / _screenBitmap.Height;
            var bounds = new Rect((monitor.X - _screenX) * scaleX, (monitor.Y - _screenY) * scaleY,
                monitor.Width * scaleX, monitor.Height * scaleY);
            InfoPanel.MaxWidth = bounds.Width;
            InfoPanel.MaxHeight = bounds.Height;
            InfoPanel.Measure(bounds.Size);
            static double Fit(double position, double size, double start, double end)
            {
                var offset = position + 16 + size <= end ? position + 16 : position - 16 - size;
                return Math.Clamp(offset, start, Math.Max(start, end - size));
            }
            Canvas.SetLeft(InfoPanel, Fit(cursor.X, InfoPanel.DesiredSize.Width, bounds.Left, bounds.Right));
            Canvas.SetTop(InfoPanel, Fit(cursor.Y, InfoPanel.DesiredSize.Height, bounds.Top, bounds.Bottom));
        }

        private double CanvasWidth => MainCanvas.ActualWidth > 0 ? MainCanvas.ActualWidth : _screenWidth;
        private double CanvasHeight => MainCanvas.ActualHeight > 0 ? MainCanvas.ActualHeight : _screenHeight;

        private string UpdateColorPreview(int x, int y)
        {
            var color = _screenBitmap.GetPixel(x - _screenX, y - _screenY);
            _swatchBrush.Color = Color.FromRgb(color.R, color.G, color.B);
            var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            CoordText.Text = hex;
            return hex;
        }

        /// <summary>
        /// 将 WPF 的 DIP 坐标换算成虚拟桌面的物理像素坐标。
        /// </summary>
        private (int X, int Y) ToScreenCoordinates(System.Windows.Point position)
        {
            int ToPixel(double scaled) => _pickColor ? (int)Math.Floor(scaled) : (int)Math.Round(scaled);
            var pixelX = Math.Clamp(
                ToPixel(position.X * _screenBitmap.Width / CanvasWidth),
                0,
                _screenBitmap.Width - 1);
            var pixelY = Math.Clamp(
                ToPixel(position.Y * _screenBitmap.Height / CanvasHeight),
                0,
                _screenBitmap.Height - 1);
            return (_screenX + pixelX, _screenY + pixelY);
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _screenBitmap?.Dispose();
        }
    }
}
