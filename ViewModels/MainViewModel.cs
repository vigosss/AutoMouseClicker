using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
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

        private MacroProfile? _selectedMacro;
        private bool _isExecuting;
        private string _statusMessage = "就绪";
        private string _executionStatus = "未运行";

        #region 属性

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
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>
        /// 是否未在执行宏
        /// </summary>
        public bool IsNotExecuting => !IsExecuting;

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
        /// 请求编辑宏事件（由 MainWindow 订阅以切换到编辑器视图）
        /// </summary>
        public event EventHandler<MacroProfile>? EditRequested;

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

        #endregion

        public MainViewModel(
            MacroStorageService storageService,
            ScreenCaptureService screenCaptureService,
            ImageMatchService imageMatchService,
            MacroExecutor macroExecutor,
            HotkeyService hotkeyService)
        {
            _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
            _screenCaptureService = screenCaptureService ?? throw new ArgumentNullException(nameof(screenCaptureService));
            _imageMatchService = imageMatchService ?? throw new ArgumentNullException(nameof(imageMatchService));
            _macroExecutor = macroExecutor ?? throw new ArgumentNullException(nameof(macroExecutor));
            _hotkeyService = hotkeyService ?? throw new ArgumentNullException(nameof(hotkeyService));

            Macros = new ObservableCollection<MacroProfile>();

            // 初始化命令
            CreateMacroCommand = new RelayCommand(_ => CreateMacro(), _ => !IsExecuting);
            EditMacroCommand = new RelayCommand(_ => EditMacro(), _ => CanEditMacro());
            DeleteMacroCommand = new RelayCommand(_ => DeleteMacro(), _ => CanDeleteMacro());
            DuplicateMacroCommand = new RelayCommand(_ => DuplicateMacro(), _ => CanDuplicateMacro());
            StartMacroCommand = new RelayCommand(_ => StartMacro(), _ => CanStartMacro());
            StopMacroCommand = new RelayCommand(_ => StopMacro(), _ => CanStopMacro());
            ToggleExecutionCommand = new RelayCommand(_ => ToggleExecution());
            SaveAllCommand = new RelayCommand(_ => SaveAll());
            RefreshCommand = new RelayCommand(_ => LoadMacros());

            // 订阅执行器事件
            _macroExecutor.ActionExecuted += OnActionExecuted;
            _macroExecutor.ExecutionCompleted += OnExecutionCompleted;
            _macroExecutor.StateChanged += OnExecutionStateChanged;

            // 订阅热键事件
            _hotkeyService.HotkeyPressed += OnHotkeyPressed;

            // 加载宏列表
            LoadMacros();
        }

        #region 命令实现

        private void CreateMacro()
        {
            var newMacro = new MacroProfile
            {
                Name = $"新宏 {Macros.Count + 1}",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            Macros.Add(newMacro);
            SelectedMacro = newMacro;
            SaveAll();

            StatusMessage = $"已创建: {newMacro.Name}";
        }

        private bool CanEditMacro() => SelectedMacro != null && !IsExecuting;

        private void EditMacro()
        {
            if (SelectedMacro == null) return;
            StatusMessage = $"编辑: {SelectedMacro.Name}";
            EditRequested?.Invoke(this, SelectedMacro);
        }

        private bool CanDeleteMacro() => SelectedMacro != null && !IsExecuting;

        private void DeleteMacro()
        {
            if (SelectedMacro == null) return;

            var macroToDelete = SelectedMacro;
            var name = macroToDelete.Name;

            if (ShowConfirm($"确定要删除宏 \"{name}\" 吗？", "确认删除"))
            {
                // 注意：不删除关联的截图文件，因为复制的宏可能共享同一图片路径
                Macros.Remove(macroToDelete);
                _storageService.Delete(macroToDelete.Id);
                StatusMessage = $"已删除: {name}";
            }
        }

        private bool CanDuplicateMacro() => SelectedMacro != null && !IsExecuting;

        private void DuplicateMacro()
        {
            if (SelectedMacro == null) return;

            // 使用 DeepClone 复制，然后修改名称和时间
            var duplicated = SelectedMacro.DeepClone();
            duplicated.Id = Guid.NewGuid().ToString();
            duplicated.Name = $"{SelectedMacro.Name} (副本)";
            duplicated.CreatedAt = DateTime.Now;
            duplicated.UpdatedAt = DateTime.Now;

            Macros.Add(duplicated);
            SelectedMacro = duplicated;
            SaveAll();
            StatusMessage = $"已复制: {duplicated.Name}";
        }

        private bool CanStartMacro() => SelectedMacro != null && !IsExecuting && SelectedMacro.Actions.Count > 0;

        private void StartMacro()
        {
            if (SelectedMacro == null) return;

            if (_macroExecutor.Start(SelectedMacro))
            {
                IsExecuting = true;
                ExecutionStatus = "运行中";
                StatusMessage = $"开始执行: {SelectedMacro.Name}";
            }
            else
            {
                StatusMessage = "启动失败";
            }
        }

        private bool CanStopMacro() => IsExecuting;

        private void StopMacro()
        {
            _macroExecutor.Stop();
            IsExecuting = false;
            ExecutionStatus = "已停止";
            StatusMessage = "已停止执行";
        }

        public void ToggleExecution()
        {
            if (IsExecuting)
            {
                StopMacro();
            }
            else
            {
                if (SelectedMacro != null && SelectedMacro.Actions.Count > 0)
                {
                    StartMacro();
                }
            }
        }

        public void SaveAll()
        {
            try
            {
                _storageService.SaveMacros(Macros.ToList());
                StatusMessage = "已保存";
            }
            catch (Exception ex)
            {
                StatusMessage = $"保存失败: {ex.Message}";
            }
        }

        public void LoadMacros()
        {
            try
            {
                var macros = _storageService.LoadMacros();
                Macros.Clear();
                foreach (var macro in macros)
                {
                    Macros.Add(macro);
                }
                StatusMessage = $"已加载 {Macros.Count} 个宏";
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载失败: {ex.Message}";
            }
        }

        #endregion

        #region 事件处理

        private void OnActionExecuted(object? sender, MacroExecutionEventArgs e)
        {
            OnUIThread(() =>
            {
                var actionDesc = e.Action?.GetDescription() ?? "未知动作";
                var status = e.Success ? "成功" : "失败";
                StatusMessage = $"动作 {e.ActionIndex + 1}: {actionDesc} - {status}";
            });
        }

        private void OnExecutionCompleted(object? sender, MacroExecutionEventArgs e)
        {
            OnUIThread(() =>
            {
                IsExecuting = false;
                ExecutionStatus = e.Success ? "已完成" : "已停止";
                StatusMessage = e.Message;
            });
        }

        private void OnExecutionStateChanged(object? sender, MacroExecutionState state)
        {
            OnUIThread(() =>
            {
                ExecutionStatus = state switch
                {
                    MacroExecutionState.Idle => "空闲",
                    MacroExecutionState.Running => "运行中",
                    MacroExecutionState.Paused => "已暂停",
                    MacroExecutionState.Stopped => "已停止",
                    MacroExecutionState.Completed => "已完成",
                    _ => "未知"
                };
            });
        }

        private void OnHotkeyPressed(object? sender, HotkeyEventArgs e)
        {
            OnUIThread(() => ToggleExecution());
        }

        #endregion

        #region 热键注册

        public bool RegisterHotkey(IntPtr windowHandle)
        {
            return _hotkeyService.RegisterF8(windowHandle);
        }

        public void UnregisterHotkey()
        {
            _hotkeyService.Unregister();
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _macroExecutor.ActionExecuted -= OnActionExecuted;
                _macroExecutor.ExecutionCompleted -= OnExecutionCompleted;
                _macroExecutor.StateChanged -= OnExecutionStateChanged;
                _hotkeyService.HotkeyPressed -= OnHotkeyPressed;
                SaveAll();
            }
            base.Dispose(disposing);
        }
    }
}