using System;
using System.Windows;
using System.Windows.Input;

namespace Ming_AutoClicker.Views
{
    /// <summary>
    /// 坐标拾取窗口 - 全屏覆盖，按住鼠标拖动拾取屏幕坐标
    /// </summary>
    public partial class CoordinatePickWindow : Window
    {
        private bool _isDragging = false;

        /// <summary>
        /// 坐标拾取完成事件
        /// </summary>
        public event Action<int, int>? CoordinatePicked;

        public CoordinatePickWindow()
        {
            InitializeComponent();

            // 居中提示文字
            Loaded += (_, _) =>
            {
                Canvas.SetLeft(TipText, (MainCanvas.ActualWidth - TipText.ActualWidth) / 2);
                Canvas.SetTop(TipText, MainCanvas.ActualHeight / 2 - TipText.ActualHeight / 2);
            };

            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    DialogResult = false;
                    Close();
                }
            };
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _isDragging = true;
                TipText.Visibility = Visibility.Collapsed;
                UpdateCrosshair(e);
                e.Handled = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_isDragging)
            {
                UpdateCrosshair(e);
                e.Handled = true;
            }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            if (_isDragging && e.LeftButton == MouseButtonState.Released)
            {
                _isDragging = false;

                // 获取屏幕坐标
                var pos = e.GetPosition(this);
                int screenX = (int)pos.X;
                int screenY = (int)pos.Y;

                // 触发事件
                CoordinatePicked?.Invoke(screenX, screenY);

                DialogResult = true;
                Close();
                e.Handled = true;
            }
        }

        /// <summary>
        /// 更新十字准星和坐标信息面板
        /// </summary>
        private void UpdateCrosshair(MouseEventArgs e)
        {
            var pos = e.GetPosition(this);
            double x = pos.X;
            double y = pos.Y;

            // 更新竖线（全屏高度）
            CrossV.X1 = x;
            CrossV.Y1 = 0;
            CrossV.X2 = x;
            CrossV.Y2 = MainCanvas.ActualHeight;

            // 更新横线（全屏宽度）
            CrossH.X1 = 0;
            CrossH.Y1 = y;
            CrossH.X2 = MainCanvas.ActualWidth;
            CrossH.Y2 = y;

            // 更新坐标信息
            CoordText.Text = $"X: {(int)x}  Y: {(int)y}";
            InfoPanel.Visibility = Visibility.Visible;

            // 信息面板位置：跟随鼠标，偏移避免遮挡
            double panelX = x + 16;
            double panelY = y + 16;

            // 防止超出屏幕右侧
            if (panelX + InfoPanel.ActualWidth > MainCanvas.ActualWidth)
            {
                panelX = x - InfoPanel.ActualWidth - 16;
            }
            // 防止超出屏幕底部
            if (panelY + InfoPanel.ActualHeight > MainCanvas.ActualHeight)
            {
                panelY = y - InfoPanel.ActualHeight - 16;
            }

            Canvas.SetLeft(InfoPanel, panelX);
            Canvas.SetTop(InfoPanel, panelY);
        }
    }
}