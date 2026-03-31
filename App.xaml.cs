using System;
using System.Diagnostics;
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

        private HotkeyService? _hotkeyService;
        private MacroExecutor? _macroExecutor;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

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

                // 创建主 ViewModel
                MainViewModel = new MainViewModel(
                    StorageService,
                    ScreenCaptureService,
                    ImageMatchService,
                    _macroExecutor,
                    _hotkeyService,
                    autoClickService);

                // 创建并显示主窗口
                var mainWindow = new MainWindow
                {
                    DataContext = MainViewModel
                };
                mainWindow.Show();

                // 异步检查版本更新（不阻塞启动）
                _ = CheckForUpdatesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"应用启动失败:\n\n{ex.Message}\n\n{ex.StackTrace}",
                    "启动错误",
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

            var message = e.Exception?.Message ?? "未知错误";
            var detail = e.Exception?.ToString() ?? "";

            System.Diagnostics.Debug.WriteLine($"[UI线程异常] {detail}");

            MessageBox.Show(
                $"发生了一个未预期的错误:\n\n{message}\n\n应用将继续运行，但可能出现异常行为。",
                "错误",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        /// <summary>
        /// 处理非 UI 线程未捕获的异常
        /// </summary>
        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            var message = ex?.Message ?? "未知错误";
            var detail = ex?.ToString() ?? "";

            System.Diagnostics.Debug.WriteLine($"[非UI线程异常] IsTerminating={e.IsTerminating}\n{detail}");

            if (!e.IsTerminating)
            {
                MessageBox.Show(
                    $"发生了一个严重的错误:\n\n{message}",
                    "严重错误",
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

            var message = e.Exception?.InnerException?.Message ?? e.Exception?.Message ?? "未知错误";
            var detail = e.Exception?.ToString() ?? "";

            System.Diagnostics.Debug.WriteLine($"[Task未观察异常] {detail}");

            Dispatcher.BeginInvoke(() =>
            {
                MessageBox.Show(
                    $"后台任务发生错误:\n\n{message}",
                    "任务错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            });
        }

        #endregion
    }
}
