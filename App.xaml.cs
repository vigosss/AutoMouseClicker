using System.Windows;
using Ming_AutoClicker.Services;
using Ming_AutoClicker.ViewModels;

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

            // 按依赖顺序初始化服务
            StorageService = new MacroStorageService();
            ScreenCaptureService = new ScreenCaptureService();
            ImageMatchService = new ImageMatchService(ScreenCaptureService);
            _macroExecutor = new MacroExecutor(ImageMatchService, ScreenCaptureService);
            _hotkeyService = new HotkeyService();

            // 创建主 ViewModel
            MainViewModel = new MainViewModel(
                StorageService,
                ScreenCaptureService,
                ImageMatchService,
                _macroExecutor,
                _hotkeyService);

            // 设置主窗口 DataContext
            var mainWindow = (MainWindow)Current.MainWindow;
            mainWindow.DataContext = MainViewModel;
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
    }
}
