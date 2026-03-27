using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Ming_AutoClicker.Models;
using Ming_AutoClicker.ViewModels;
using Ming_AutoClicker.Views;

namespace Ming_AutoClicker
{
    public partial class MainWindow : Window
    {
        private MainViewModel? _viewModel;
        private MacroEditorView? _editorView;
        private PropertyChangedEventHandler? _propertyChangedHandler;

        public MainWindow()
        {
            InitializeComponent();

            Loaded += OnLoaded;
            SourceInitialized += OnSourceInitialized;
            Closed += OnClosed;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _viewModel = DataContext as MainViewModel;
            if (_viewModel == null) return;

            // 监听执行状态变化，更新状态指示灯颜色
            _propertyChangedHandler = (s, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.IsExecuting))
                {
                    Dispatcher.Invoke(() =>
                    {
                        StatusIndicator.Fill = _viewModel.IsExecuting
                            ? new SolidColorBrush(Colors.LimeGreen)
                            : new SolidColorBrush(Colors.Gray);
                    });
                }
            };
            _viewModel.PropertyChanged += _propertyChangedHandler;

            // 初始化宏列表视图事件
            MacroListView.RequestEdit += OnRequestEdit;

            // 订阅 ViewModel 的编辑请求事件（支持键盘快捷键等触发）
            _viewModel.EditRequested += OnRequestEdit;

            // 注册全局热键（窗口句柄在 SourceInitialized 时已可用）
            var hwnd = new WindowInteropHelper(this).Handle;
            _viewModel.RegisterHotkey(hwnd);
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            // 窗口句柄在此事件已可用，但 ViewModel 尚未赋值，热键注册延迟到 OnLoaded
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            // 取消事件订阅
            if (_viewModel != null && _propertyChangedHandler != null)
            {
                _viewModel.PropertyChanged -= _propertyChangedHandler;
            }

            MacroListView.RequestEdit -= OnRequestEdit;
            if (_viewModel != null)
            {
                _viewModel.EditRequested -= OnRequestEdit;
            }

            _viewModel?.UnregisterHotkey();
            _viewModel?.Dispose();
        }

        /// <summary>
        /// 切换到编辑器视图
        /// </summary>
        private void OnRequestEdit(object? sender, MacroProfile macro)
        {
            if (_viewModel == null) return;

            // 深拷贝，避免编辑时污染原始数据
            var macroClone = macro.DeepClone();

            // 创建编辑器 ViewModel（复用全局服务单例）
            var editorViewModel = new MacroEditorViewModel(
                App.StorageService!,
                App.ScreenCaptureService!,
                App.ImageMatchService!,
                macroClone);

            // 创建编辑器视图
            _editorView = new MacroEditorView
            {
                DataContext = editorViewModel
            };

            _editorView.RequestClose += OnEditorRequestClose;
            _editorView.RequestSave += OnEditorRequestSave;

            // 切换内容
            ContentArea.Content = _editorView;
        }

        /// <summary>
        /// 编辑器请求保存，将编辑结果写回原始数据
        /// </summary>
        private void OnEditorRequestSave(object? sender, MacroProfile editedMacro)
        {
            if (_viewModel == null) return;

            // 找到原始宏并更新其数据
            var original = _viewModel.Macros.FirstOrDefault(m => m.Id == editedMacro.Id);
            if (original != null)
            {
                original.Name = editedMacro.Name;
                original.LoopEnabled = editedMacro.LoopEnabled;
                original.LoopCount = editedMacro.LoopCount;
                original.LoopIntervalMs = editedMacro.LoopIntervalMs;
                original.UpdatedAt = editedMacro.UpdatedAt;

                original.Actions.Clear();
                foreach (var action in editedMacro.Actions)
                {
                    switch (action)
                    {
                        case FindImageAction fia:
                            original.Actions.Add(fia.Clone());
                            break;
                        case WaitAction wa:
                            original.Actions.Add(wa.Clone());
                            break;
                    }
                }
            }

            _viewModel.SaveAll();
        }

        /// <summary>
        /// 编辑器请求关闭，切回列表视图
        /// </summary>
        private void OnEditorRequestClose(object? sender, EventArgs e)
        {
            if (_editorView != null)
            {
                _editorView.RequestClose -= OnEditorRequestClose;
                _editorView.RequestSave -= OnEditorRequestSave;

                // 释放 ViewModel
                if (_editorView.DataContext is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                _editorView = null;
            }

            // 切回列表视图
            ContentArea.Content = MacroListView;

            // 刷新宏列表
            _viewModel?.LoadMacros();
        }
    }
}