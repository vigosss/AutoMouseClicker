using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ming_AutoClicker.Models;
using Ming_AutoClicker.ViewModels;
using Ming_AutoClicker.Views.ImageMatching;

namespace Ming_AutoClicker.Views.Macros
{
    public partial class MacroEditorView : UserControl
    {
        /// <summary>
        /// 请求关闭编辑器事件（返回列表视图）
        /// </summary>
        public event EventHandler? RequestClose;

        /// <summary>
        /// 请求保存编辑结果事件（将编辑数据写回原始宏）
        /// </summary>
        public event EventHandler<MacroProfile>? RequestSave;

        private MacroEditorViewModel? _viewModel;

        public MacroEditorView()
        {
            InitializeComponent();

            DataContextChanged += OnDataContextChanged;
        }

        private void OnAddActionClick(object sender, RoutedEventArgs e) => OpenAddActionMenu();

        private void OnImagePointSelected(object sender, ImagePointSelectedEventArgs e) =>
            _viewModel?.SetImageClickPoint(e.Point);

        private void OnAddActionPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Down) return;
            e.Handled = true;
            OpenAddActionMenu();
        }

        private void OpenAddActionMenu()
        {
            var menu = AddActionButton.ContextMenu;
            menu.PlacementTarget = AddActionButton;
            menu.IsOpen = true;
            menu.Focus();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // 取消旧订阅
            if (_viewModel != null)
            {
                _viewModel.SaveCompleted -= OnSaveCompleted;
                _viewModel.CancelCompleted -= OnCancelCompleted;
            }

            _viewModel = e.NewValue as MacroEditorViewModel;

            // 订阅新 ViewModel 的专用事件
            if (_viewModel != null)
            {
                _viewModel.SaveCompleted += OnSaveCompleted;
                _viewModel.CancelCompleted += OnCancelCompleted;
            }
        }

        private void OnSaveCompleted(object? sender, EventArgs e)
        {
            if (_viewModel == null) return;

            // 先触发保存事件，将编辑数据写回原始宏
            RequestSave?.Invoke(this, _viewModel.Macro);

            // 延迟关闭，让用户看到状态消息
            Dispatcher.BeginInvoke(() =>
            {
                RequestClose?.Invoke(this, EventArgs.Empty);
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void OnCancelCompleted(object? sender, EventArgs e)
        {
            // 取消时直接关闭，不写回
            Dispatcher.BeginInvoke(() =>
            {
                RequestClose?.Invoke(this, EventArgs.Empty);
            }, System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}
