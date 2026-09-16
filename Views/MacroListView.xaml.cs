using System;
using System.Windows;
using System.Windows.Controls;
using Ming_AutoClicker.Models;

namespace Ming_AutoClicker.Views
{
    public partial class MacroListView : UserControl
    {
        /// <summary>
        /// 请求编辑宏事件
        /// </summary>
        public event EventHandler<MacroProfile>? RequestEdit;
        public event EventHandler<MacroProfile>? RequestHotkey;

        public MacroListView()
        {
            InitializeComponent();
        }
        private void OnHotkeyClick(object sender, RoutedEventArgs e) { if (sender is FrameworkElement f && f.DataContext is MacroProfile m) RequestHotkey?.Invoke(this,m); }

        private void OnEditClick(object sender, RoutedEventArgs e)
        {
            // 获取按钮所在的数据上下文（即 MacroProfile）
            if (sender is FrameworkElement element && element.DataContext is MacroProfile macro)
            {
                RequestEdit?.Invoke(this, macro);
            }
        }
    }
}
