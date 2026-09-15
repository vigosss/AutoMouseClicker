using System;
using System.Diagnostics;
using System.Windows.Input;
using Ming_AutoClicker.Helpers;
using Ming_AutoClicker.Services;

namespace Ming_AutoClicker.ViewModels
{
    /// <summary>
    /// 鼠标连点 ViewModel - 管理连点设置和状态
    /// </summary>
    public class AutoClickViewModel : ViewModelBase
    {
        private readonly AutoClickService _autoClickService;

        private string _selectedButton = "left";
        private int _intervalMs = 100;
        private bool _isRunning;
        private string _statusText = string.Empty;
        private int _clickCount;
        private int _pendingClickCount;
        private readonly Stopwatch _clickCountStopwatch = new Stopwatch();
        private const int ClickCountUpdateIntervalMs = 100;

        #region 属性

        /// <summary>
        /// 选中的鼠标按钮：left, middle, right
        /// </summary>
        public string SelectedButton
        {
            get => _selectedButton;
            set => SetProperty(ref _selectedButton, value);
        }

        /// <summary>
        /// 是否选中左键
        /// </summary>
        public bool IsLeftButton
        {
            get => _selectedButton == "left";
            set { if (value) SelectedButton = "left"; OnPropertyChanged(); }
        }

        /// <summary>
        /// 是否选中中键
        /// </summary>
        public bool IsMiddleButton
        {
            get => _selectedButton == "middle";
            set { if (value) SelectedButton = "middle"; OnPropertyChanged(); }
        }

        /// <summary>
        /// 是否选中右键
        /// </summary>
        public bool IsRightButton
        {
            get => _selectedButton == "right";
            set { if (value) SelectedButton = "right"; OnPropertyChanged(); }
        }

        /// <summary>
        /// 点击间隔（毫秒）
        /// </summary>
        public int IntervalMs
        {
            get => _intervalMs;
            set
            {
                if (SetProperty(ref _intervalMs, value))
                {
                    OnPropertyChanged(nameof(IntervalText));
                }
            }
        }

        /// <summary>
        /// 间隔输入文本（用于双向绑定和校验）
        /// </summary>
        public string IntervalText
        {
            get => _intervalMs.ToString();
            set
            {
                if (int.TryParse(value, out int ms))
                {
                    IntervalMs = Math.Max(10, Math.Min(60000, ms));
                }
            }
        }

        /// <summary>
        /// 是否正在连点
        /// </summary>
        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (SetProperty(ref _isRunning, value))
                {
                    OnPropertyChanged(nameof(IsNotRunning));
                    OnPropertyChanged(nameof(HotkeyHintText));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>
        /// 是否未在连点
        /// </summary>
        public bool IsNotRunning => !_isRunning;

        /// <summary>
        /// 快捷键提示文本
        /// </summary>
        public string HotkeyHintText => LocalizationService.Current.GetString(
            _isRunning ? "AutoClickStopHint" : "AutoClickStartHint");

        /// <summary>
        /// 状态文本
        /// </summary>
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        /// <summary>
        /// 已点击次数
        /// </summary>
        public int ClickCount
        {
            get => _clickCount;
            set => SetProperty(ref _clickCount, value);
        }

        #endregion

        #region 命令

        public ICommand ToggleCommand { get; }

        #endregion

        public AutoClickViewModel(AutoClickService autoClickService)
        {
            _autoClickService = autoClickService ?? throw new ArgumentNullException(nameof(autoClickService));
            _statusText = LocalizationService.Current.GetString("StatusReady");

            ToggleCommand = new RelayCommand(_ => Toggle(), _ => true);

            // 订阅服务事件
            _autoClickService.RunningStateChanged += OnRunningStateChanged;
            _autoClickService.ClickCountChanged += OnClickCountChanged;
            LocalizationService.Current.LanguageChanged += OnLanguageChanged;
        }

        /// <summary>
        /// 切换连点开始/停止
        /// </summary>
        public void Toggle()
        {
            if (IsRunning)
            {
                Stop();
            }
            else
            {
                Start();
            }
        }

        private void Start()
        {
            StatusText = LocalizationService.Current.Format(
                "AutoClickStarted", GetButtonName(SelectedButton), IntervalMs);
            if (!_autoClickService.Start(SelectedButton, IntervalMs))
            {
                StatusText = LocalizationService.Current.GetString("StatusStartFailed");
            }
        }

        private void Stop()
        {
            _autoClickService.Stop();
            StatusText = LocalizationService.Current.GetString("AutoClickStopping");
        }

        private string GetButtonName(string button) => button switch
        {
            "left" => LocalizationService.Current.GetString("MouseButtonLeft"),
            "middle" => LocalizationService.Current.GetString("MouseButtonMiddle"),
            "right" => LocalizationService.Current.GetString("MouseButtonRight"),
            _ => button
        };

        #region 事件处理

        private void OnRunningStateChanged(object? sender, bool isRunning)
        {
            OnUIThread(() =>
            {
                IsRunning = isRunning;
                if (isRunning)
                {
                    _clickCountStopwatch.Restart();
                }
                else
                {
                    // 停止时立即刷新最终点击次数
                    ClickCount = _autoClickService.ClickCount;
                    StatusText = LocalizationService.Current.Format("StatusStoppedClicks", ClickCount);
                    _clickCountStopwatch.Reset();
                }
            });
        }

        private void OnClickCountChanged(object? sender, int count)
        {
            _pendingClickCount = count;

            // 节流：每隔 ClickCountUpdateIntervalMs 毫秒更新一次 UI
            if (!_clickCountStopwatch.IsRunning || _clickCountStopwatch.ElapsedMilliseconds >= ClickCountUpdateIntervalMs)
            {
                _clickCountStopwatch.Restart();
                var snapshot = _pendingClickCount;
                BeginOnUIThread(() => ClickCount = snapshot);
            }
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            OnUIThread(() =>
            {
                OnPropertyChanged(nameof(HotkeyHintText));
                StatusText = IsRunning
                    ? LocalizationService.Current.Format("AutoClickStarted", GetButtonName(SelectedButton), IntervalMs)
                    : ClickCount > 0
                        ? LocalizationService.Current.Format("StatusStoppedClicks", ClickCount)
                        : LocalizationService.Current.GetString("StatusReady");
            });
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _autoClickService.RunningStateChanged -= OnRunningStateChanged;
                _autoClickService.ClickCountChanged -= OnClickCountChanged;
                LocalizationService.Current.LanguageChanged -= OnLanguageChanged;
                _autoClickService.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
