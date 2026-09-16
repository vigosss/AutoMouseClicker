using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Ming_AutoClicker.Models;
using Ming_AutoClicker.Services;
using Ming_AutoClicker.ViewModels;
using Ming_AutoClicker.Views;

namespace Ming_AutoClicker
{
    public partial class App : Application
    {
        public static MainViewModel? MainViewModel { get; private set; }
        public static MacroStorageService? StorageService { get; private set; }
        public static ScreenCaptureService? ScreenCaptureService { get; private set; }
        public static ImageMatchService? ImageMatchService { get; private set; }
        public static RecordingStorageService? RecordingStorageService { get; private set; }
        public static Services.LocalizationService Localization => Services.LocalizationService.Current;

        private HotkeyService? _hotkeyService;
        private MacroExecutor? _macroExecutor;
        private Mutex? _singleInstanceMutex;
        private bool _ownsSingleInstanceMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var appSettingsService = new AppSettingsService();
            var appSettings = appSettingsService.Load();
            LocalizationService.Current.ApplyLanguage(appSettings.Language);

            _singleInstanceMutex = new Mutex(
                initiallyOwned: true,
                name: @"Local\MingAutoClicker.SingleInstance",
                createdNew: out _ownsSingleInstanceMutex);
            if (!_ownsSingleInstanceMutex)
            {
                MessageBox.Show(
                    LocalizationService.Current.GetString("AppAlreadyRunning"),
                    LocalizationService.Current.GetString("CommonPrompt"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
                Shutdown();
                return;
            }

            // 注册全局异常处理，防止应用静默崩溃
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            try
            {
                // 按依赖顺序初始化服务
                StorageService = new MacroStorageService();
                ScreenCaptureService = new ScreenCaptureService();
                ImageMatchService = new ImageMatchService(ScreenCaptureService);
                _macroExecutor = new MacroExecutor(ImageMatchService, ScreenCaptureService);
                _hotkeyService = new HotkeyService();
                var autoClickService = new AutoClickService();
                RecordingStorageService = new RecordingStorageService();
                var globalHookService = new GlobalHookService();
                var playbackService = new PlaybackService();

                // 创建主 ViewModel
                MainViewModel = new MainViewModel(
                    StorageService,
                    ScreenCaptureService,
                    ImageMatchService,
                    _macroExecutor,
                    _hotkeyService,
                    appSettingsService,
                    appSettings,
                    autoClickService,
                    RecordingStorageService,
                    globalHookService,
                    playbackService);

                // 创建并显示主窗口
                var mainWindow = new MainWindow
                {
                    DataContext = MainViewModel
                };
                mainWindow.Show();

                // 首次运行时询问一次；已有快捷方式会自动校验目标路径。
                ShortcutService.EnsureDesktopShortcut(() => Dialog.ShowConfirm(
                    LocalizationService.Current.GetString("ShortcutPrompt"),
                    LocalizationService.Current.GetString("ShortcutPromptTitle")));

                // 异步检查版本更新（不阻塞启动）
                _ = CheckForUpdatesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    LocalizationService.Current.Format("AppStartupFailed", ex.Message, ex.StackTrace ?? string.Empty),
                    LocalizationService.Current.GetString("AppStartupErrorTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 按依赖逆序释放服务
            MainViewModel?.Dispose();
            _hotkeyService?.Dispose();
            _macroExecutor?.Dispose();
            ImageMatchService?.Dispose();
            ScreenCaptureService?.Dispose();

            if (_ownsSingleInstanceMutex)
            {
                try { _singleInstanceMutex?.ReleaseMutex(); }
                catch (ApplicationException) { }
            }
            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
            _ownsSingleInstanceMutex = false;

            base.OnExit(e);
        }

        #region 版本更新

        /// <summary>
        /// 异步检查版本更新
        /// 主窗口已显示后执行，不阻塞启动流程
        /// </summary>
        private async Task CheckForUpdatesAsync()
        {
            try
            {
                using var updateService = new UpdateService();
                var result = await updateService.CheckForUpdateAsync();

                if (result.HasUpdate)
                {
                    // 在 UI 线程上显示更新窗口
                    Dispatcher.Invoke(() =>
                    {
                        var updateWindow = new UpdateWindow(result, updateService)
                        {
                            Owner = MainWindow
                        };
                        updateWindow.ShowDialog();
                    });
                }
            }
            catch (Exception ex)
            {
                // 更新检查失败不影响正常使用，仅输出调试信息
                Debug.WriteLine($"[更新检查] 检查更新失败: {ex.Message}");
            }
        }

        #endregion

        #region 全局异常处理

        /// <summary>
        /// 处理 UI 线程未捕获的异常
        /// </summary>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;

            var message = e.Exception?.Message ?? LocalizationService.Current.GetString("CommonUnknownError");
            var detail = e.Exception?.ToString() ?? "";

            System.Diagnostics.Debug.WriteLine($"[UI线程异常] {detail}");

            MessageBox.Show(
                LocalizationService.Current.Format("AppUnexpectedError", message),
                LocalizationService.Current.GetString("CommonError"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        /// <summary>
        /// 处理非 UI 线程未捕获的异常
        /// </summary>
        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            var message = ex?.Message ?? LocalizationService.Current.GetString("CommonUnknownError");
            var detail = ex?.ToString() ?? "";

            System.Diagnostics.Debug.WriteLine($"[非UI线程异常] IsTerminating={e.IsTerminating}\n{detail}");

            if (!e.IsTerminating)
            {
                MessageBox.Show(
                    LocalizationService.Current.Format("AppSevereError", message),
                    LocalizationService.Current.GetString("AppSevereErrorTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 处理 Task 中未观察到的异常
        /// </summary>
        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved();

            var message = e.Exception?.InnerException?.Message ?? e.Exception?.Message ??
                LocalizationService.Current.GetString("CommonUnknownError");
            var detail = e.Exception?.ToString() ?? "";

            System.Diagnostics.Debug.WriteLine($"[Task未观察异常] {detail}");

            Dispatcher.BeginInvoke(() =>
            {
                MessageBox.Show(
                    LocalizationService.Current.Format("AppBackgroundError", message),
                    LocalizationService.Current.GetString("AppBackgroundErrorTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            });
        }

        #endregion
    }
}
