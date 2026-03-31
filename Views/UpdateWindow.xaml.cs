using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Ming_AutoClicker.Models;
using Ming_AutoClicker.Services;

namespace Ming_AutoClicker.Views
{
    public partial class UpdateWindow : Window
    {
        private readonly UpdateCheckResult _updateInfo;
        private readonly UpdateService _updateService;
        private CancellationTokenSource? _downloadCts;

        /// <summary>
        /// 是否正在下载更新中
        /// </summary>
        public bool IsDownloading { get; private set; }

        public UpdateWindow(UpdateCheckResult updateInfo, UpdateService updateService)
        {
            InitializeComponent();

            _updateInfo = updateInfo ?? throw new ArgumentNullException(nameof(updateInfo));
            _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));

            InitializeDisplay();
        }

        /// <summary>
        /// 初始化界面显示
        /// </summary>
        private void InitializeDisplay()
        {
            // 版本号
            CurrentVersionRun.Text = $"v{_updateInfo.CurrentVersion}";
            NewVersionRun.Text = $"v{_updateInfo.LatestVersionTag}";

            // 发布时间
            PublishDateText.Text = _updateInfo.PublishedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

            // 更新说明
            ReleaseNotesText.Text = string.IsNullOrWhiteSpace(_updateInfo.ReleaseNotes)
                ? "暂无更新说明"
                : _updateInfo.ReleaseNotes;

            // 订阅下载进度事件
            _updateService.DownloadProgressChanged += OnDownloadProgressChanged;
            _updateService.DownloadCompleted += OnDownloadCompleted;
        }

        #region 标题栏拖动

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        #endregion

        #region 按钮事件

        /// <summary>
        /// 立即更新按钮
        /// </summary>
        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            await StartDownloadAsync();
        }

        #endregion

        #region 下载逻辑

        /// <summary>
        /// 开始下载更新
        /// </summary>
        private async Task StartDownloadAsync()
        {
            IsDownloading = true;

            // 切换到下载状态 UI
            UpdateButton.IsEnabled = false;
            UpdateButton.Content = "下载中...";

            // 显示进度区域
            ProgressArea.Visibility = Visibility.Visible;

            _downloadCts = new CancellationTokenSource();

            try
            {
                var filePath = await _updateService.DownloadUpdateAsync(_updateInfo, _downloadCts.Token);

                if (filePath != null)
                {
                    // 下载成功，执行更新
                    UpdateButton.Content = "正在更新...";
                    ProgressPercentText.Text = "正在安装更新...";

                    // 稍微延迟以确保文件写入完成
                    await Task.Delay(500);

                    var success = _updateService.ApplyUpdate(filePath);
                    if (!success)
                    {
                        MessageBox.Show(
                            "更新失败，请重新启动应用再试。",
                            "更新错误",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);

                        // 更新失败，允许重试
                        ResetToInitialState();
                    }
                    // 如果成功，ApplyUpdate 会关闭应用
                }
                else
                {
                    // 下载失败，允许重试
                    MessageBox.Show(
                        "下载失败，请检查网络连接后重试。",
                        "下载失败",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    ResetToInitialState();
                }
            }
            catch (OperationCanceledException)
            {
                ResetToInitialState();
            }
        }

        /// <summary>
        /// 重置为初始状态（允许重试）
        /// </summary>
        private void ResetToInitialState()
        {
            IsDownloading = false;

            // 恢复按钮
            UpdateButton.IsEnabled = true;
            UpdateButton.Content = "立即更新";

            // 隐藏进度区域
            ProgressArea.Visibility = Visibility.Collapsed;
            DownloadProgress.Value = 0;
            ProgressPercentText.Text = "0%";
            ProgressSizeText.Text = string.Empty;
        }

        #endregion

        #region 下载进度回调

        private void OnDownloadProgressChanged(object? sender, DownloadProgressEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                DownloadProgress.Value = e.Progress * 100;
                ProgressPercentText.Text = e.ProgressText;
                ProgressSizeText.Text = $"{e.DownloadedText} / {e.TotalText}";
            });
        }

        private void OnDownloadCompleted(object? sender, bool success)
        {
            Dispatcher.Invoke(() =>
            {
                if (!success)
                {
                    ResetToInitialState();
                }
            });
        }

        #endregion

        #region 窗口关闭（强制更新 - 禁止关闭）

        /// <summary>
        /// 强制更新：阻止窗口被关闭
        /// 窗口只能在更新成功后由程序自动关闭
        /// </summary>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // 如果更新已完成（应用正在退出），允许关闭
            if (_updateApplied)
            {
                _downloadCts?.Dispose();
                _updateService.DownloadProgressChanged -= OnDownloadProgressChanged;
                _updateService.DownloadCompleted -= OnDownloadCompleted;
                base.OnClosing(e);
                return;
            }

            // 否则阻止关闭
            e.Cancel = true;
        }

        private bool _updateApplied;

        /// <summary>
        /// 允许关闭窗口（仅在更新成功应用后调用）
        /// </summary>
        public void AllowClose()
        {
            _updateApplied = true;
            Close();
        }

        #endregion
    }
}