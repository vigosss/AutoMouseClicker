using System;
using System.Threading;
using System.Threading.Tasks;
using Ming_AutoClicker.Helpers;

namespace Ming_AutoClicker.Services
{
    /// <summary>
    /// 自动连点服务 - 在当前鼠标位置持续点击
    /// </summary>
    public class AutoClickService : IDisposable
    {
        private Task? _clickTask;
        private CancellationTokenSource? _cts;
        private readonly object _lock = new();
        private volatile bool _isRunning;
        private bool _disposed;

        /// <summary>
        /// 是否正在连点
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// 已完成的点击次数
        /// </summary>
        public int ClickCount { get; private set; }

        /// <summary>
        /// 状态变更事件
        /// </summary>
        public event EventHandler<bool>? RunningStateChanged;

        /// <summary>
        /// 点击计数更新事件
        /// </summary>
        public event EventHandler<int>? ClickCountChanged;

        /// <summary>
        /// 开始连点
        /// </summary>
        /// <param name="button">点击类型：left, middle, right</param>
        /// <param name="intervalMs">点击间隔（毫秒）</param>
        public bool Start(string button, int intervalMs)
        {
            lock (_lock)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(AutoClickService));
                if (IsRunning) return false;
                if (button is not ("left" or "right" or "middle")) return false;

                // 钳位间隔到合理范围
                intervalMs = Math.Max(10, Math.Min(60000, intervalMs));

                ClickCount = 0;
                _cts?.Dispose();
                _cts = new CancellationTokenSource();
                var token = _cts.Token;

                _isRunning = true;
                _clickTask = Task.Run(() => ClickLoopAsync(button, intervalMs, token), token);
                RunningStateChanged?.Invoke(this, true);
                return true;
            }
        }

        /// <summary>
        /// 停止连点
        /// </summary>
        public void Stop()
        {
            lock (_lock)
            {
                if (!IsRunning) return;

                _cts?.Cancel();
            }
        }

        /// <summary>
        /// 连点循环
        /// </summary>
        private async Task ClickLoopAsync(string button, int intervalMs, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    // 获取当前鼠标位置
                    Win32Api.GetCursorPos(out var point);

                    // 根据按钮类型执行点击
                    switch (button.ToLower())
                    {
                        case "left":
                            Win32Api.LeftClick(point.X, point.Y);
                            break;
                        case "right":
                            Win32Api.RightClick(point.X, point.Y);
                            break;
                        case "middle":
                            Win32Api.MiddleClick(point.X, point.Y);
                            break;
                    }

                    ClickCount++;
                    ClickCountChanged?.Invoke(this, ClickCount);

                    // 等待间隔时间
                    await Task.Delay(intervalMs, token);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常停止
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"连点异常: {ex.Message}");
            }
            finally
            {
                _isRunning = false;
                RunningStateChanged?.Invoke(this, false);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Stop();
                _cts?.Dispose();
                _cts = null;
                _clickTask = null;
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
