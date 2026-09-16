using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Ming_AutoClicker.Helpers;
using Ming_AutoClicker.Models;
using Ming_AutoClicker.Services;

namespace Ming_AutoClicker.ViewModels
{
    /// <summary>
    /// 主窗口 ViewModel - 管理宏列表和执行控制
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        private readonly MacroStorageService _storageService;
        private readonly ScreenCaptureService _screenCaptureService;
        private readonly ImageMatchService _imageMatchService;
        private readonly MacroExecutor _macroExecutor;
        private readonly HotkeyService _hotkeyService;
        private readonly AppSettingsService _appSettingsService;
        private readonly AppSettings _appSettings;

        private MacroProfile? _selectedMacro;
        private bool _isExecuting;
        private string _statusMessage = string.Empty;
        private string _executionStatus = string.Empty;
        private int _currentTabIndex;
        private int _autoClickCount;
        private bool _isStartingMacro;
        private MacroProfile? _runningMacro;
        private CancellationTokenSource? _macroStartCancellation;
        private bool _mainWindowMinimizedForMacro;
        private WindowState _windowStateBeforeMacro = WindowState.Normal;
        private IntPtr _mainWindowHandle;
        private string _activeHotkeyDescription = "F8";
        private readonly SemaphoreSlim _targetSwitchGate = new(1, 1);

        #region 属性

        /// <summary>
        /// 鼠标连点 ViewModel
        /// </summary>
        public AutoClickViewModel AutoClickViewModel { get; }
        public RecordingPageViewModel RecordingPageViewModel { get; }

        /// <summary>
        /// 当前选中的 Tab 索引（0=鼠标连点, 1=鼠标宏）
        /// </summary>
        public int CurrentTabIndex
        {
            get => _currentTabIndex;
            set
            {
                if (SetProperty(ref _currentTabIndex, value))
                {
                    UpdateExecutionStatus();
                }
            }
        }

        /// <summary>
        /// 宏配置列表
        /// </summary>
        public ObservableCollection<MacroProfile> Macros { get; }

        /// <summary>
        /// 当前选中的宏
        /// </summary>
        public MacroProfile? SelectedMacro
        {
            get => _selectedMacro;
            set
            {
                // 执行期间锁定启动时的宏，避免模拟点击改变列表选择。
                if ((_isStartingMacro || IsExecuting) && _runningMacro != null &&
                    value?.Id != _runningMacro.Id)
                {
                    return;
                }

                if (SetProperty(ref _selectedMacro, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>
        /// 是否正在执行宏
        /// </summary>
        public bool IsExecuting
        {
            get => _isExecuting;
            private set
            {
                if (SetProperty(ref _isExecuting, value))
                {
                    OnPropertyChanged(nameof(IsNotExecuting));
                    OnPropertyChanged(nameof(CanConfigureSettings));
                    OnPropertyChanged(nameof(IsNavigationEnabled));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>
        /// 是否未在执行宏
        /// </summary>
        public bool IsNotExecuting => !IsExecuting && !_isStartingMacro;

        /// <summary>
        /// 状态消息
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>
        /// 执行状态
        /// </summary>
        public string ExecutionStatus
        {
            get => _executionStatus;
            set => SetProperty(ref _executionStatus, value);
        }

        /// <summary>
        /// 宏执行器
        /// </summary>
        public MacroExecutor MacroExecutor => _macroExecutor;

        /// <summary>
        /// 鼠标连点次数（供底部状态栏显示）
        /// </summary>
        public int AutoClickCount
        {
            get => _autoClickCount;
            set => SetProperty(ref _autoClickCount, value);
        }

        /// <summary>
        /// 当前实际注册成功的全局启停热键。
        /// </summary>
        public string ActiveHotkeyDescription
        {
            get => _activeHotkeyDescription;
            private set
            {
                if (SetProperty(ref _activeHotkeyDescription, value))
                {
                    OnPropertyChanged(nameof(HotkeyStatusText));
                    OnPropertyChanged(nameof(MacroHotkeyHintText));
                }
            }
        }

        public string HotkeyStatusText => !_hotkeyService.IsRegistered
            ? LocalizationService.Current.GetString("HotkeyStatusDisabled")
            : LocalizationService.Current.Format("HotkeyStatus", ActiveHotkeyDescription);

        public string MacroHotkeyHintText => !_hotkeyService.IsRegistered
            ? LocalizationService.Current.GetString("HotkeyMacroHintDisabled")
            : LocalizationService.Current.Format("HotkeyMacroHint", ActiveHotkeyDescription);

        public bool CanConfigureSettings => IsNotExecuting && AutoClickViewModel.IsNotRunning && RecordingPageViewModel.State == RecordingState.Idle;
        public bool IsNavigationEnabled => IsNotExecuting && AutoClickViewModel.IsNotRunning && RecordingPageViewModel.State == RecordingState.Idle;

        public HotkeyGesture ConfiguredHotkey => _appSettings.Hotkeys.ToggleExecution.Clone();
        public AppLanguage ConfiguredLanguage => _appSettings.Language;

        /// <summary>
        /// 请求编辑宏事件（由 MainWindow 订阅以切换到编辑器视图）
        /// </summary>
        public event EventHandler<MacroProfile>? EditRequested;

        public event EventHandler? SettingsRequested;

        #endregion

        #region 命令

        public ICommand CreateMacroCommand { get; }
        public ICommand EditMacroCommand { get; }
        public ICommand DeleteMacroCommand { get; }
        public ICommand DuplicateMacroCommand { get; }
        public ICommand StartMacroCommand { get; }
        public ICommand StopMacroCommand { get; }
        public ICommand ToggleExecutionCommand { get; }
        public ICommand SaveAllCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ConfigureSettingsCommand { get; }

        #endregion

        public MainViewModel(
            MacroStorageService storageService,
            ScreenCaptureService screenCaptureService,
            ImageMatchService imageMatchService,
            MacroExecutor macroExecutor,
            HotkeyService hotkeyService,
            AppSettingsService appSettingsService,
            AppSettings appSettings,
            AutoClickService autoClickService,
            RecordingStorageService recordingStorageService,
            GlobalHookService globalHookService,
            PlaybackService playbackService)
        {
            _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
            _screenCaptureService = screenCaptureService ?? throw new ArgumentNullException(nameof(screenCaptureService));
            _imageMatchService = imageMatchService ?? throw new ArgumentNullException(nameof(imageMatchService));
            _macroExecutor = macroExecutor ?? throw new ArgumentNullException(nameof(macroExecutor));
            _hotkeyService = hotkeyService ?? throw new ArgumentNullException(nameof(hotkeyService));
            _appSettingsService = appSettingsService ?? throw new ArgumentNullException(nameof(appSettingsService));
            _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
            _statusMessage = LocalizationService.Current.GetString("StatusReady");
            _executionStatus = LocalizationService.Current.GetString("StatusNotRunning");

            Macros = new ObservableCollection<MacroProfile>();

            // 初始化鼠标连点 ViewModel
            AutoClickViewModel = new AutoClickViewModel(autoClickService ?? throw new ArgumentNullException(nameof(autoClickService)));
            RecordingPageViewModel = new RecordingPageViewModel(recordingStorageService, globalHookService, playbackService);
            globalHookService.IsControlKey = _hotkeyService.IsControlKey;

            // 订阅鼠标连点状态变化
            AutoClickViewModel.PropertyChanged += OnAutoClickViewModelPropertyChanged;
            RecordingPageViewModel.PropertyChanged += OnRecordingViewModelPropertyChanged;
            RecordingPageViewModel.RecordingDeleted += OnRecordingDeleted;

            // 默认选中第一个 Tab（鼠标连点）
            _currentTabIndex = 0;

            // 初始化命令
            CreateMacroCommand = new RelayCommand(_ => CreateMacro(), _ => IsNotExecuting);
            EditMacroCommand = new RelayCommand(_ => EditMacro(), _ => CanEditMacro());
            DeleteMacroCommand = new RelayCommand(_ => DeleteMacro(), _ => CanDeleteMacro());
            DuplicateMacroCommand = new RelayCommand(_ => DuplicateMacro(), _ => CanDuplicateMacro());
            StartMacroCommand = new RelayCommand(_ => _ = StartMacroAsync(), _ => CanStartMacro());
            StopMacroCommand = new RelayCommand(_ => StopMacro(), _ => CanStopMacro());
            ToggleExecutionCommand = new RelayCommand(_ => ToggleExecution());
            SaveAllCommand = new RelayCommand(_ => SaveAll());
            RefreshCommand = new RelayCommand(_ => LoadMacros());
            ConfigureSettingsCommand = new RelayCommand(
                _ => SettingsRequested?.Invoke(this, EventArgs.Empty),
                _ => CanConfigureSettings);

            // 订阅执行器事件
            _macroExecutor.ActionExecuted += OnActionExecuted;
            _macroExecutor.ExecutionCompleted += OnExecutionCompleted;
            _macroExecutor.StateChanged += OnExecutionStateChanged;

            // 订阅热键事件
            _hotkeyService.HotkeyPressed += OnHotkeyPressed;
            LocalizationService.Current.LanguageChanged += OnLanguageChanged;

            // 加载宏列表
            LoadMacros();

            // 初始化执行状态显示
            UpdateExecutionStatus();
        }

        #region 命令实现

        private void CreateMacro()
        {
            var newMacro = new MacroProfile
            {
                Name = LocalizationService.Current.Format("DefaultMacroName", Macros.Count + 1),
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            Macros.Add(newMacro);
            SelectedMacro = newMacro;
            SaveAll();

            StatusMessage = LocalizationService.Current.Format("StatusCreatedMacro", newMacro.Name);
        }

        private bool CanEditMacro() => SelectedMacro != null && IsNotExecuting;

        private void EditMacro()
        {
            if (SelectedMacro == null) return;
            StatusMessage = LocalizationService.Current.Format("StatusEditingMacro", SelectedMacro.Name);
            EditRequested?.Invoke(this, SelectedMacro);
        }

        private bool CanDeleteMacro() => SelectedMacro != null && IsNotExecuting;

        private void DeleteMacro()
        {
            if (SelectedMacro == null) return;

            var macroToDelete = SelectedMacro;
            var name = macroToDelete.Name;

            if (ShowConfirm(
                    LocalizationService.Current.Format("DeleteMacroPrompt", name),
                    LocalizationService.Current.GetString("DeleteMacroTitle")))
            {
                if (!_storageService.Delete(macroToDelete.Id))
                {
                    StatusMessage = LocalizationService.Current.Format("StatusDeleteFailed", name);
                    return;
                }

                _hotkeyService.UnregisterTarget($"macro:{macroToDelete.Id}");
                Macros.Remove(macroToDelete);
                _screenCaptureService.CleanupUnusedScreenshots(Macros);
                StatusMessage = LocalizationService.Current.Format("StatusDeletedMacro", name);
            }
        }

        private bool CanDuplicateMacro() => SelectedMacro != null && IsNotExecuting;

        private void DuplicateMacro()
        {
            if (SelectedMacro == null) return;

            var duplicated = SelectedMacro.DeepClone();
            duplicated.Id = Guid.NewGuid().ToString();
            duplicated.Name = SelectedMacro.Name + LocalizationService.Current.GetString("DuplicateMacroSuffix");
            duplicated.CreatedAt = DateTime.Now;
            duplicated.UpdatedAt = DateTime.Now;
            duplicated.Hotkey = null;

            Macros.Add(duplicated);
            SelectedMacro = duplicated;
            SaveAll();
            StatusMessage = LocalizationService.Current.Format("StatusDuplicatedMacro", duplicated.Name);
        }

        private bool CanStartMacro() => SelectedMacro != null && IsNotExecuting && SelectedMacro.Actions.Count > 0;

        private async Task StartMacroAsync()
        {
            if (!CanStartMacro() || SelectedMacro == null) return;

            var macroToRun = SelectedMacro;
            _runningMacro = macroToRun;
            SetMacroStarting(true);
            _macroStartCancellation?.Dispose();
            _macroStartCancellation = new CancellationTokenSource();
            var cancellation = _macroStartCancellation;

            try
            {
                MinimizeMainWindowForMacro();
                StatusMessage = LocalizationService.Current.Format("StatusPreparingMacro", macroToRun.Name);

                // 等待主窗口完全隐藏，避免截图或坐标点击命中程序自身。
                await Task.Delay(200, cancellation.Token);

                IsExecuting = true;
                ExecutionStatus = LocalizationService.Current.GetString("StatusStarting");
                StatusMessage = LocalizationService.Current.Format("StatusStartingMacro", macroToRun.Name);

                if (!_macroExecutor.Start(macroToRun))
                {
                    IsExecuting = false;
                    ExecutionStatus = LocalizationService.Current.GetString("StatusStartFailed");
                    StatusMessage = LocalizationService.Current.GetString("StatusStartFailed");
                    RestoreMainWindowAfterMacro();
                    _runningMacro = null;
                }
            }
            catch (OperationCanceledException)
            {
                ExecutionStatus = LocalizationService.Current.GetString("StatusStopped");
                StatusMessage = LocalizationService.Current.GetString("StatusStartCancelled");
                RestoreRunningMacroSelection();
                RestoreMainWindowAfterMacro();
                _runningMacro = null;
            }
            catch (Exception ex)
            {
                IsExecuting = false;
                ExecutionStatus = LocalizationService.Current.GetString("StatusStartFailed");
                StatusMessage = LocalizationService.Current.Format("StatusStartError", ex.Message);
                RestoreRunningMacroSelection();
                RestoreMainWindowAfterMacro();
                _runningMacro = null;
            }
            finally
            {
                if (ReferenceEquals(_macroStartCancellation, cancellation))
                {
                    _macroStartCancellation.Dispose();
                    _macroStartCancellation = null;
                }
                SetMacroStarting(false);
            }
        }

        private bool CanStopMacro() => IsExecuting || _isStartingMacro;

        private void StopMacro()
        {
            if (_isStartingMacro)
            {
                _macroStartCancellation?.Cancel();
                return;
            }

            _macroExecutor.Stop();
            // 保持执行锁定和窗口最小化，直到后台任务确实退出。
            // ExecutionCompleted 会负责恢复窗口、选择和最终状态。
            ExecutionStatus = LocalizationService.Current.GetString("StatusStopping");
            StatusMessage = LocalizationService.Current.GetString("StatusSafeStopping");
        }

        public void ToggleExecution()
        {
            if (AutoClickViewModel.IsRunning) { AutoClickViewModel.Toggle(); return; }
            if (IsExecuting || _isStartingMacro) { StopMacro(); return; }
            if (RecordingPageViewModel.State != RecordingState.Idle) { RecordingPageViewModel.Toggle(); return; }
            if (_currentTabIndex == 0)
            {
                // 鼠标连点 Tab：转发给 AutoClickViewModel
                AutoClickViewModel.Toggle();
            }
            else if (_currentTabIndex == 1)
            {
                // 鼠标宏 Tab：原有逻辑
                if (IsExecuting || _isStartingMacro)
                {
                    StopMacro();
                }
                else
                {
                    if (SelectedMacro != null && SelectedMacro.Actions.Count > 0)
                    {
                        _ = StartMacroAsync();
                    }
                }
            }
            else
            {
                RecordingPageViewModel.Toggle();
            }
        }

        /// <summary>
        /// 根据当前 Tab 更新执行状态显示
        /// </summary>
        private void UpdateExecutionStatus()
        {
            if (_currentTabIndex == 0)
            {
                ExecutionStatus = LocalizationService.Current.GetString(
                    AutoClickViewModel.IsRunning ? "StatusAutoClicking" : "StatusNotRunning");
            }
            else if (_currentTabIndex == 1)
            {
                ExecutionStatus = _macroExecutor.State switch
                {
                    MacroExecutionState.Running => LocalizationService.Current.GetString("StatusRunning"),
                    MacroExecutionState.Paused => LocalizationService.Current.GetString("StatusPaused"),
                    MacroExecutionState.Stopped => LocalizationService.Current.GetString("StatusStopped"),
                    MacroExecutionState.Completed => LocalizationService.Current.GetString("StatusCompleted"),
                    _ => LocalizationService.Current.GetString("StatusNotRunning")
                };
            }
            else
            {
                ExecutionStatus = RecordingPageViewModel.State switch
                {
                    RecordingState.Recording => LocalizationService.Current.GetString("RecordingStatusRecording"),
                    RecordingState.RecordPaused => LocalizationService.Current.GetString("StatusPaused"),
                    RecordingState.Playing => LocalizationService.Current.GetString("RecordingStatusPlaying"),
                    RecordingState.PlayPaused => LocalizationService.Current.GetString("StatusPaused"),
                    _ => LocalizationService.Current.GetString("StatusNotRunning")
                };
            }
        }

        public void SaveAll()
        {
            try
            {
                _storageService.SaveMacros(Macros.ToList());
                _screenCaptureService.CleanupUnusedScreenshots(Macros);
                StatusMessage = LocalizationService.Current.GetString("StatusSaved");
            }
            catch (Exception ex)
            {
                StatusMessage = LocalizationService.Current.Format("StatusSaveFailed", ex.Message);
            }
        }

        public void LoadMacros()
        {
            try
            {
                var selectedId = SelectedMacro?.Id;
                var macros = _storageService.LoadMacros();
                Macros.Clear();
                foreach (var macro in macros)
                {
                    Macros.Add(macro);
                }
                SelectedMacro = selectedId == null
                    ? Macros.FirstOrDefault()
                    : Macros.FirstOrDefault(m => m.Id == selectedId) ?? Macros.FirstOrDefault();
                StatusMessage = LocalizationService.Current.Format("StatusLoadedMacros", Macros.Count);
            }
            catch (Exception ex)
            {
                StatusMessage = LocalizationService.Current.Format("StatusLoadFailed", ex.Message);
            }
        }

        #endregion

        #region 事件处理

        private void OnActionExecuted(object? sender, MacroExecutionEventArgs e)
        {
            OnUIThread(() =>
            {
                var actionDesc = e.Action?.GetDescription() ?? LocalizationService.Current.GetString("ActionUnknown");
                var status = LocalizationService.Current.GetString(e.Success ? "StatusSuccess" : "StatusFailed");
                StatusMessage = LocalizationService.Current.Format(
                    "StatusActionResult", e.ActionIndex + 1, actionDesc, status);
            });
        }

        private void OnExecutionCompleted(object? sender, MacroExecutionEventArgs e)
        {
            OnUIThread(() =>
            {
                IsExecuting = false;
                ExecutionStatus = LocalizationService.Current.GetString(
                    e.Success ? "StatusCompleted" : "StatusStopped");
                StatusMessage = e.Message;
                RestoreRunningMacroSelection();
                RestoreMainWindowAfterMacro();
                _runningMacro = null;
            });
        }

        private void SetMacroStarting(bool value)
        {
            if (_isStartingMacro == value) return;
            _isStartingMacro = value;
            OnPropertyChanged(nameof(IsNotExecuting));
            OnPropertyChanged(nameof(CanConfigureSettings));
            CommandManager.InvalidateRequerySuggested();
        }

        private void MinimizeMainWindowForMacro()
        {
            var mainWindow = Application.Current?.MainWindow;
            if (mainWindow == null || mainWindow.WindowState == WindowState.Minimized)
                return;

            _windowStateBeforeMacro = mainWindow.WindowState;
            _mainWindowMinimizedForMacro = true;
            mainWindow.WindowState = WindowState.Minimized;
        }

        private void RestoreMainWindowAfterMacro()
        {
            if (!_mainWindowMinimizedForMacro) return;

            var mainWindow = Application.Current?.MainWindow;
            if (mainWindow != null)
            {
                mainWindow.WindowState = _windowStateBeforeMacro;
                mainWindow.Activate();
            }
            _mainWindowMinimizedForMacro = false;
        }

        private void RestoreRunningMacroSelection()
        {
            if (_runningMacro != null && Macros.Contains(_runningMacro))
                SelectedMacro = _runningMacro;
        }

        private void OnExecutionStateChanged(object? sender, MacroExecutionState state)
        {
            OnUIThread(() =>
            {
                ExecutionStatus = state switch
                {
                    MacroExecutionState.Idle => LocalizationService.Current.GetString("StatusIdle"),
                    MacroExecutionState.Running => LocalizationService.Current.GetString("StatusRunning"),
                    MacroExecutionState.Paused => LocalizationService.Current.GetString("StatusPaused"),
                    MacroExecutionState.Stopped => LocalizationService.Current.GetString("StatusStopped"),
                    MacroExecutionState.Completed => LocalizationService.Current.GetString("StatusCompleted"),
                    _ => LocalizationService.Current.GetString("StatusUnknown")
                };
            });
        }

        private void OnHotkeyPressed(object? sender, HotkeyEventArgs e)
        {
            OnUIThread(() =>
            {
                if (string.IsNullOrEmpty(e.Target)) ToggleExecution();
                else _ = SwitchToTargetAsync(e.Target);
            });
        }

        private async Task SwitchToTargetAsync(string target)
        {
            await _targetSwitchGate.WaitAsync();
            try
            {
                if (IsTargetRunning(target))
                {
                    ToggleExecution();
                    return;
                }

                if (AutoClickViewModel.IsRunning) AutoClickViewModel.Toggle();
                if (IsExecuting || _isStartingMacro) StopMacro();
                if (RecordingPageViewModel.State != RecordingState.Idle) RecordingPageViewModel.Toggle();

                var until = DateTime.UtcNow.AddSeconds(5);
                while (IsAnyAutomationActive && DateTime.UtcNow < until)
                    await Task.Delay(50);

                if (IsAnyAutomationActive)
                {
                    StatusMessage = LocalizationService.Current.GetString("AutomationSwitchFailed");
                    return;
                }

                if (target.StartsWith("macro:", StringComparison.Ordinal))
                {
                    var id = target[6..];
                    SelectedMacro = Macros.FirstOrDefault(x => x.Id == id);
                    if (SelectedMacro != null) await StartMacroAsync();
                }
                else if (target.StartsWith("recording:", StringComparison.Ordinal))
                {
                    var id = target[10..];
                    RecordingPageViewModel.SelectedRecording = RecordingPageViewModel.Recordings.FirstOrDefault(x => x.Id == id);
                    if (RecordingPageViewModel.SelectedRecording != null) RecordingPageViewModel.Toggle();
                }
            }
            finally
            {
                _targetSwitchGate.Release();
            }
        }

        private bool IsAnyAutomationActive => AutoClickViewModel.IsRunning || IsExecuting ||
            _isStartingMacro || RecordingPageViewModel.State != RecordingState.Idle;

        private bool IsTargetRunning(string target)
        {
            if (target.StartsWith("macro:", StringComparison.Ordinal))
                return (IsExecuting || _isStartingMacro) && _runningMacro?.Id == target[6..];

            return target.StartsWith("recording:", StringComparison.Ordinal) &&
                RecordingPageViewModel.IsPlaying &&
                RecordingPageViewModel.SelectedRecording?.Id == target[10..];
        }

        private void OnAutoClickViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AutoClickViewModel.IsRunning))
            {
                OnUIThread(() =>
                {
                    if (_currentTabIndex == 0)
                    {
                        ExecutionStatus = LocalizationService.Current.GetString(
                            AutoClickViewModel.IsRunning ? "StatusAutoClicking" : "StatusNotRunning");
                        if (AutoClickViewModel.IsRunning)
                        {
                            StatusMessage = AutoClickViewModel.StatusText;
                        }
                        else if (AutoClickViewModel.ClickCount > 0)
                        {
                            StatusMessage = LocalizationService.Current.Format(
                                "StatusStoppedClicks", AutoClickViewModel.ClickCount);
                        }
                    }

                    OnPropertyChanged(nameof(CanConfigureSettings));
                    OnPropertyChanged(nameof(IsNavigationEnabled));
                    CommandManager.InvalidateRequerySuggested();
                });
            }
            else if (e.PropertyName == nameof(AutoClickViewModel.ClickCount))
            {
                // 使用异步派发，避免高频点击时阻塞 UI 线程
                var snapshot = AutoClickViewModel.ClickCount;
                BeginOnUIThread(() => AutoClickCount = snapshot);
            }
        }

        private void OnRecordingViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if(e.PropertyName==nameof(RecordingPageViewModel.State)){OnUIThread(()=>{OnPropertyChanged(nameof(IsNavigationEnabled));OnPropertyChanged(nameof(CanConfigureSettings));UpdateExecutionStatus();});}
        }

        private void OnRecordingDeleted(string id) => _hotkeyService.UnregisterTarget($"recording:{id}");

        #endregion

        #region 热键注册

        public bool RegisterHotkey(IntPtr windowHandle)
        {
            _mainWindowHandle = windowHandle;
            var configured = _appSettings.Hotkeys.ToggleExecution;
            var result = _hotkeyService.TryRegister(windowHandle, configured);
            if (result.Success)
            {
                UpdateActiveHotkeyDescription();
                RegisterItemHotkeys();
                return true;
            }

            var configuredText = HotkeyGestureHelper.Format(configured);
            if (!configured.Equals(HotkeyGesture.Default))
            {
                var fallback = _hotkeyService.TryRegister(windowHandle, HotkeyGesture.Default);
                if (fallback.Success)
                {
                    UpdateActiveHotkeyDescription();
                    RegisterItemHotkeys();
                    StatusMessage = LocalizationService.Current.Format("HotkeyFallback", configuredText);
                    return true;
                }
            }

            ActiveHotkeyDescription = LocalizationService.Current.GetString("HotkeyDisabled");
            StatusMessage = LocalizationService.Current.Format("HotkeyUnavailable", result.Message);
            return false;
        }

        private void RegisterItemHotkeys()
        {
            var failed = false;
            foreach (var macro in Macros.Where(x => x.Hotkey != null)) failed |= !_hotkeyService.RegisterTarget(_mainWindowHandle, $"macro:{macro.Id}", macro.Hotkey!).Success;
            foreach (var recording in RecordingPageViewModel.Recordings.Where(x => x.Model.Hotkey != null)) failed |= !_hotkeyService.RegisterTarget(_mainWindowHandle, $"recording:{recording.Id}", recording.Model.Hotkey!).Success;
            if (failed) StatusMessage = LocalizationService.Current.GetString("ItemHotkeyStartupConflict");
        }

        public HotkeyRegistrationResult SetMacroHotkey(MacroProfile macro, HotkeyGesture? gesture)
        {
            var target=$"macro:{macro.Id}";var previous=macro.Hotkey?.Clone();_hotkeyService.UnregisterTarget(target);
            if(gesture!=null){var result=_hotkeyService.RegisterTarget(_mainWindowHandle,target,gesture);if(!result.Success){if(previous!=null)_hotkeyService.RegisterTarget(_mainWindowHandle,target,previous);return result;}}
            try{macro.Hotkey=gesture?.Clone();macro.UpdatedAt=DateTime.Now;_storageService.Save(macro);return HotkeyRegistrationResult.Succeeded();}
            catch(Exception ex){_hotkeyService.UnregisterTarget(target);macro.Hotkey=previous;if(previous!=null)_hotkeyService.RegisterTarget(_mainWindowHandle,target,previous);return HotkeyRegistrationResult.Failed(HotkeyRegistrationFailure.SettingsSaveFailed,LocalizationService.Current.Format("HotkeySettingsSaveFailed",ex.Message));}
        }
        public HotkeyRegistrationResult SetRecordingHotkey(RecordingItemViewModel item, HotkeyGesture? gesture)
        {
            var target=$"recording:{item.Id}";var previous=item.Model.Hotkey?.Clone();_hotkeyService.UnregisterTarget(target);
            if(gesture!=null){var result=_hotkeyService.RegisterTarget(_mainWindowHandle,target,gesture);if(!result.Success){if(previous!=null)_hotkeyService.RegisterTarget(_mainWindowHandle,target,previous);return result;}}
            try{item.Model.Hotkey=gesture?.Clone();RecordingPageViewModel.Save(item.Model);item.Refresh();return HotkeyRegistrationResult.Succeeded();}
            catch(Exception ex){_hotkeyService.UnregisterTarget(target);item.Model.Hotkey=previous;if(previous!=null)_hotkeyService.RegisterTarget(_mainWindowHandle,target,previous);item.Refresh();return HotkeyRegistrationResult.Failed(HotkeyRegistrationFailure.SettingsSaveFailed,LocalizationService.Current.Format("HotkeySettingsSaveFailed",ex.Message));}
        }

        /// <summary>
        /// 注册并保存设置。注册或保存失败时恢复原有配置。
        /// </summary>
        public HotkeyRegistrationResult TryUpdateSettings(HotkeyGesture gesture, AppLanguage language)
        {
            if (!CanConfigureSettings)
            {
                return HotkeyRegistrationResult.Failed(
                    HotkeyRegistrationFailure.SystemError,
                    LocalizationService.Current.GetString("HotkeyStopBeforeSettings"));
            }

            if (_mainWindowHandle == IntPtr.Zero)
            {
                return HotkeyRegistrationResult.Failed(
                    HotkeyRegistrationFailure.SystemError,
                    LocalizationService.Current.GetString("HotkeyWindowNotReady"));
            }

            var previousActive = _hotkeyService.CurrentGesture?.Clone();
            var previousConfigured = _appSettings.Hotkeys.ToggleExecution.Clone();
            var previousLanguage = _appSettings.Language;
            var registration = previousActive?.Equals(gesture) == true
                ? HotkeyRegistrationResult.Succeeded()
                : _hotkeyService.TryRegister(_mainWindowHandle, gesture);
            if (!registration.Success)
                return registration;

            try
            {
                _appSettings.Hotkeys.ToggleExecution = gesture.Clone();
                _appSettings.Language = language;
                _appSettingsService.Save(_appSettings);
            }
            catch (Exception ex)
            {
                _appSettings.Hotkeys.ToggleExecution = previousConfigured;
                _appSettings.Language = previousLanguage;
                if (previousActive != null)
                    _hotkeyService.TryRegister(_mainWindowHandle, previousActive);
                else
                    _hotkeyService.Unregister();

                LocalizationService.Current.ApplyLanguage(previousLanguage);
                UpdateActiveHotkeyDescription();
                return HotkeyRegistrationResult.Failed(
                    HotkeyRegistrationFailure.SettingsSaveFailed,
                    LocalizationService.Current.Format("HotkeySettingsSaveFailed", ex.Message));
            }

            LocalizationService.Current.ApplyLanguage(language);
            UpdateActiveHotkeyDescription();
            StatusMessage = LocalizationService.Current.Format("HotkeyConfigured", ActiveHotkeyDescription);
            return HotkeyRegistrationResult.Succeeded();
        }

        private void UpdateActiveHotkeyDescription()
        {
            ActiveHotkeyDescription = _hotkeyService.IsRegistered
                ? _hotkeyService.GetCurrentHotkeyDescription()
                : LocalizationService.Current.GetString("HotkeyDisabled");
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            OnUIThread(() =>
            {
                UpdateActiveHotkeyDescription();
                OnPropertyChanged(nameof(HotkeyStatusText));
                OnPropertyChanged(nameof(MacroHotkeyHintText));
                UpdateExecutionStatus();
                StatusMessage = AutoClickViewModel.IsRunning
                    ? AutoClickViewModel.StatusText
                    : LocalizationService.Current.GetString("StatusReady");
                CollectionViewSource.GetDefaultView(Macros).Refresh();
            });
        }

        public void UnregisterHotkey()
        {
            _hotkeyService.Unregister();
        }

        /// <summary>
        /// 处理热键窗口消息（由 MainWindow.WndProc 调用）
        /// 通过 HotkeyService 统一处理，避免重复触发
        /// </summary>
        public void HandleHotkeyMessage(int message, IntPtr wParam, IntPtr lParam)
        {
            _hotkeyService.HandleMessage(message, wParam, lParam);
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _macroStartCancellation?.Cancel();
                _macroStartCancellation?.Dispose();
                _macroStartCancellation = null;
                AutoClickViewModel.PropertyChanged -= OnAutoClickViewModelPropertyChanged;
                AutoClickViewModel.Dispose();
                RecordingPageViewModel.Dispose();
                RecordingPageViewModel.PropertyChanged -= OnRecordingViewModelPropertyChanged;
                RecordingPageViewModel.RecordingDeleted -= OnRecordingDeleted;
                _macroExecutor.ActionExecuted -= OnActionExecuted;
                _macroExecutor.ExecutionCompleted -= OnExecutionCompleted;
                _macroExecutor.StateChanged -= OnExecutionStateChanged;
                _hotkeyService.HotkeyPressed -= OnHotkeyPressed;
                LocalizationService.Current.LanguageChanged -= OnLanguageChanged;
                SettingsRequested = null;
                SaveAll();
            }
            base.Dispose(disposing);
        }
    }
}
