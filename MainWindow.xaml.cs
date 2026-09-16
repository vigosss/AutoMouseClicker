using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Ming_AutoClicker.Models;
using Ming_AutoClicker.Services;
using Ming_AutoClicker.ViewModels;
using Ming_AutoClicker.Views;

namespace Ming_AutoClicker
{
    public partial class MainWindow : Window
    {
        private MainViewModel? _viewModel;
        private PropertyChangedEventHandler? _propertyChangedHandler;
        private PropertyChangedEventHandler? _autoClickPropertyChangedHandler;
        private HwndSource? _hwndSource;

        public MainWindow()
        {
            InitializeComponent();

            // 显示当前版本号
            var versionString = Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion ?? "0.0.0";
            var cleanVersion = versionString.Split('+', '-')[0];
            VersionText.Text = $"v{cleanVersion}";

            Loaded += OnLoaded;
            SourceInitialized += OnSourceInitialized;
            Closed += OnClosed;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _viewModel = DataContext as MainViewModel;
            if (_viewModel == null) return;

            // 设置 AutoClickView 的 DataContext
            AutoClickView.DataContext = _viewModel.AutoClickViewModel;
            RecordingPage.DataContext = _viewModel.RecordingPageViewModel;

            // 监听执行状态变化，更新状态指示灯颜色
            _propertyChangedHandler = (s, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.IsExecuting) ||
                    args.PropertyName == nameof(MainViewModel.CurrentTabIndex))
                {
                    Dispatcher.Invoke(() => UpdateStatusIndicator());
                }
            };
            _viewModel.PropertyChanged += _propertyChangedHandler;

            // 监听 AutoClickViewModel 的运行状态
            _autoClickPropertyChangedHandler = (s, args) =>
            {
                if (args.PropertyName == nameof(AutoClickViewModel.IsRunning))
                {
                    Dispatcher.Invoke(() => UpdateStatusIndicator());
                }
            };
            _viewModel.AutoClickViewModel.PropertyChanged += _autoClickPropertyChangedHandler;
            _viewModel.RecordingPageViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(RecordingPageViewModel.State)) Dispatcher.Invoke(UpdateStatusIndicator);
            };

            // 初始化宏列表视图事件
            MacroListView.RequestEdit += OnRequestEdit;
            MacroListView.RequestHotkey += OnMacroHotkey;
            _viewModel.RecordingPageViewModel.HotkeyRequested += OnRecordingHotkey;
            _viewModel.RecordingPageViewModel.OptimizeRequested += OnOptimizeRecording;
            _viewModel.RecordingPageViewModel.RenameRequested += OnRenameRecording;

            // 订阅 ViewModel 的编辑请求事件
            _viewModel.EditRequested += OnRequestEdit;
            _viewModel.SettingsRequested += OnSettingsRequested;

            // 注册全局热键
            var hwnd = new WindowInteropHelper(this).Handle;
            _viewModel.RegisterHotkey(hwnd);
        }

        /// <summary>
        /// 更新状态指示灯颜色
        /// </summary>
        private void UpdateStatusIndicator()
        {
            if (_viewModel == null) return;

            bool isRunning;
            if (_viewModel.CurrentTabIndex == 0)
            {
                isRunning = _viewModel.AutoClickViewModel.IsRunning;
            }
            else if (_viewModel.CurrentTabIndex == 1)
            {
                isRunning = _viewModel.IsExecuting;
            }
            else
            {
                isRunning = _viewModel.RecordingPageViewModel.State != RecordingState.Idle;
            }

            StatusIndicator.Fill = isRunning
                ? FindResource("SuccessBrush") as SolidColorBrush ?? new SolidColorBrush(Colors.LimeGreen)
                : FindResource("TextDisabledBrush") as SolidColorBrush ?? new SolidColorBrush(Colors.Gray);
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            _hwndSource?.AddHook(WndProc);
        }

        /// <summary>
        /// 窗口消息处理 - 转发热键消息给 ViewModel
        /// </summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Helpers.Win32Api.WM_HOTKEY)
            {
                if (_viewModel != null)
                {
                    _viewModel.HandleHotkeyMessage(msg, wParam, lParam);
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            // 移除 WndProc 钩子
            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(WndProc);
                _hwndSource = null;
            }

            // 取消事件订阅
            if (_viewModel != null && _propertyChangedHandler != null)
            {
                _viewModel.PropertyChanged -= _propertyChangedHandler;
            }
            if (_viewModel != null && _autoClickPropertyChangedHandler != null)
            {
                _viewModel.AutoClickViewModel.PropertyChanged -= _autoClickPropertyChangedHandler;
            }

            MacroListView.RequestEdit -= OnRequestEdit;
            MacroListView.RequestHotkey -= OnMacroHotkey;
            if (_viewModel != null)
            {
                _viewModel.EditRequested -= OnRequestEdit;
                _viewModel.SettingsRequested -= OnSettingsRequested;
                _viewModel.RecordingPageViewModel.HotkeyRequested -= OnRecordingHotkey;
                _viewModel.RecordingPageViewModel.OptimizeRequested -= OnOptimizeRecording;
                _viewModel.RecordingPageViewModel.RenameRequested -= OnRenameRecording;
            }

            _viewModel?.UnregisterHotkey();
            _viewModel?.Dispose();
        }

        private void OnMacroHotkey(object? sender, MacroProfile macro) => ShowItemHotkey(macro.Hotkey, g => _viewModel!.SetMacroHotkey(macro,g));
        private void OnRecordingHotkey(RecordingItemViewModel item) => ShowItemHotkey(item.Model.Hotkey, g => _viewModel!.SetRecordingHotkey(item,g));
        private void OnRenameRecording(RecordingItemViewModel item)
        {
            var dialog = new RenameRecordingWindow(item.Name) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            if (!_viewModel!.RecordingPageViewModel.TryRename(item, dialog.ResultName, out var error))
                Dialog.ShowError(error, LocalizationService.Current.GetString("RecordingRenameTitle"));
        }
        private void OnOptimizeRecording(RecordingItemViewModel item)
        {
            var window=new OptimizeWindow(item.Model){Owner=this};if(window.ShowDialog()==true&&window.Result!=null){item.Model.Name=window.Result.Name;item.Model.Actions=window.Result.Actions;_viewModel!.RecordingPageViewModel.Save(item.Model);item.Refresh();}
        }
        private void ShowItemHotkey(HotkeyGesture? current, Func<HotkeyGesture?, HotkeyRegistrationResult> save)
        {
            var dialog=new ItemHotkeyWindow(current){Owner=this};if(dialog.ShowDialog()!=true)return;var result=save(dialog.Result);if(!result.Success)Dialog.ShowWarning(result.Message,LocalizationService.Current.GetString("ItemHotkeyUnavailableTitle"));
        }

        private void OnSettingsRequested(object? sender, EventArgs e)
        {
            if (_viewModel == null || !_viewModel.CanConfigureSettings)
                return;

            var dialog = new HotkeySettingsWindow(
                _viewModel.ConfiguredHotkey,
                _viewModel.ConfiguredLanguage,
                _viewModel.TryUpdateSettings)
            {
                Owner = this
            };
            dialog.ShowDialog();
        }

        /// <summary>
        /// 切换到编辑器视图（以弹窗方式）
        /// </summary>
        private void OnRequestEdit(object? sender, MacroProfile macro)
        {
            if (_viewModel == null) return;

            // 深拷贝，避免编辑时污染原始数据
            var macroClone = macro.DeepClone();

            // 创建编辑器 ViewModel
            var editorViewModel = new MacroEditorViewModel(
                App.StorageService!,
                App.ScreenCaptureService!,
                App.ImageMatchService!,
                macroClone);

            // 创建编辑器弹窗
            var editorWindow = new Window
            {
                Title = LocalizationService.Current.Format("EditorWindowTitle", macro.Name),
                Width = 800,
                Height = 600,
                MinWidth = 700,
                MinHeight = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.CanResize,
                Background = FindResource("BackgroundBrush") as Brush ?? Brushes.White
            };

            var editorView = new MacroEditorView
            {
                DataContext = editorViewModel
            };

            editorWindow.Content = editorView;

            // 处理保存请求
            editorView.RequestSave += (s, editedMacro) =>
            {
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
                            case MouseClickAction mca:
                                original.Actions.Add(mca.Clone());
                                break;
                        }
                    }
                }

                _viewModel.SaveAll();
                editorWindow.Close();
            };

            // 处理关闭请求
            editorView.RequestClose += (s, e) =>
            {
                editorWindow.Close();
            };

            // 窗口关闭时清理
            editorWindow.Closed += (s, e) =>
            {
                if (editorView.DataContext is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                _viewModel.LoadMacros();
            };

            editorWindow.ShowDialog();
        }
    }
}
