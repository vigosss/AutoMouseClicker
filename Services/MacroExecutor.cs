using System;
using System.Threading;
using System.Threading.Tasks;
using Ming_AutoClicker.Helpers;
using Ming_AutoClicker.Models;

namespace Ming_AutoClicker.Services
{
    /// <summary>
    /// 宏执行状态
    /// </summary>
    public enum MacroExecutionState
    {
        Idle,
        Running,
        Paused,
        Stopped,
        Completed
    }

    /// <summary>
    /// 宏执行事件参数
    /// </summary>
    public class MacroExecutionEventArgs : EventArgs
    {
        /// <summary>
        /// 当前执行的动作
        /// </summary>
        public MacroAction? Action { get; set; }

        /// <summary>
        /// 动作索引
        /// </summary>
        public int ActionIndex { get; set; }

        /// <summary>
        /// 执行结果
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 结果消息
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 匹配结果（仅找图动作有效）
        /// </summary>
        public MatchResult? MatchResult { get; set; }
    }

    /// <summary>
    /// 宏执行器 - 负责执行宏配置中的动作序列
    /// </summary>
    public class MacroExecutor : IDisposable
    {
        private readonly ImageMatchService _imageMatchService;
        private readonly ScreenCaptureService _screenCaptureService;
        
        private Task? _executionTask;
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly object _lockObject = new();
        
        private MacroProfile? _currentProfile;
        private bool _isPaused;
        private bool _disposed;

        /// <summary>
        /// 当前执行状态
        /// </summary>
        public MacroExecutionState State { get; private set; } = MacroExecutionState.Idle;

        /// <summary>
        /// 是否正在运行
        /// </summary>
        public bool IsRunning => State == MacroExecutionState.Running;

        /// <summary>
        /// 是否已暂停
        /// </summary>
        public bool IsPaused => _isPaused;

        /// <summary>
        /// 当前执行的宏配置
        /// </summary>
        public MacroProfile? CurrentProfile => _currentProfile;

        /// <summary>
        /// 当前执行的动作索引
        /// </summary>
        public int CurrentActionIndex { get; private set; }

        /// <summary>
        /// 已完成的循环次数
        /// </summary>
        public int CompletedLoopCount { get; private set; }

        /// <summary>
        /// 动作执行完成事件
        /// </summary>
        public event EventHandler<MacroExecutionEventArgs>? ActionExecuted;

        /// <summary>
        /// 宏执行完成事件
        /// </summary>
        public event EventHandler<MacroExecutionEventArgs>? ExecutionCompleted;

        /// <summary>
        /// 状态变更事件
        /// </summary>
        public event EventHandler<MacroExecutionState>? StateChanged;

        /// <summary>
        /// 找图匹配超时时间（毫秒）
        /// </summary>
        public int FindImageTimeoutMs { get; set; } = 30000;

        /// <summary>
        /// 找图检查间隔（毫秒）
        /// </summary>
        public int FindImageIntervalMs { get; set; } = 500;

        public MacroExecutor(ImageMatchService imageMatchService, ScreenCaptureService screenCaptureService)
        {
            _imageMatchService = imageMatchService ?? throw new ArgumentNullException(nameof(imageMatchService));
            _screenCaptureService = screenCaptureService ?? throw new ArgumentNullException(nameof(screenCaptureService));
        }

        /// <summary>
        /// 启动宏执行
        /// </summary>
        /// <param name="profile">宏配置</param>
        /// <returns>是否启动成功</returns>
        public bool Start(MacroProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            if (profile.Actions.Count == 0)
            {
                OnExecutionCompleted(new MacroExecutionEventArgs
                {
                    Success = false,
                    Message = "宏配置中没有动作"
                });
                return false;
            }

            lock (_lockObject)
            {
                if (IsRunning)
                {
                    return false;
                }

                _currentProfile = profile;
                _cancellationTokenSource = new CancellationTokenSource();
                _isPaused = false;
                CurrentActionIndex = 0;
                CompletedLoopCount = 0;

                _executionTask = Task.Run(() => ExecuteAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);

                SetState(MacroExecutionState.Running);
                return true;
            }
        }

        /// <summary>
        /// 停止宏执行
        /// </summary>
        public void Stop()
        {
            Task? taskToWait = null;

            lock (_lockObject)
            {
                if (State != MacroExecutionState.Running && State != MacroExecutionState.Paused)
                {
                    return;
                }

                _cancellationTokenSource?.Cancel();
                SetState(MacroExecutionState.Stopped);
                taskToWait = _executionTask;
            }

            // 在锁外等待任务完成，避免死锁
            try
            {
                taskToWait?.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // 任务被取消是预期行为
            }
        }

        /// <summary>
        /// 暂停宏执行
        /// </summary>
        public void Pause()
        {
            lock (_lockObject)
            {
                if (State != MacroExecutionState.Running)
                {
                    return;
                }

                _isPaused = true;
                SetState(MacroExecutionState.Paused);
            }
        }

        /// <summary>
        /// 恢复宏执行
        /// </summary>
        public void Resume()
        {
            lock (_lockObject)
            {
                if (State != MacroExecutionState.Paused)
                {
                    return;
                }

                _isPaused = false;
                SetState(MacroExecutionState.Running);
            }
        }

        /// <summary>
        /// 切换暂停/恢复状态
        /// </summary>
        public void TogglePause()
        {
            if (IsPaused)
            {
                Resume();
            }
            else if (IsRunning)
            {
                Pause();
            }
        }

        /// <summary>
        /// 异步执行宏
        /// </summary>
        private async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (_currentProfile == null)
                    return;

                var actions = _currentProfile.Actions;
                bool shouldLoop = _currentProfile.LoopEnabled;
                int targetLoopCount = _currentProfile.LoopCount;
                int loopInterval = _currentProfile.LoopIntervalMs;

                do
                {
                    // 按顺序执行每个动作
                    for (int i = 0; i < actions.Count; i++)
                    {
                        // 检查取消请求
                        if (cancellationToken.IsCancellationRequested)
                        {
                            OnExecutionCompleted(new MacroExecutionEventArgs
                            {
                                Success = false,
                                Message = "用户取消执行"
                            });
                            return;
                        }

                        // 等待暂停恢复
                        while (_isPaused && !cancellationToken.IsCancellationRequested)
                        {
                            await Task.Delay(100, cancellationToken);
                        }

                        if (cancellationToken.IsCancellationRequested)
                        {
                            return;
                        }

                        CurrentActionIndex = i;
                        var action = actions[i];

                        // 执行动作
                        var result = await ExecuteActionAsync(action, cancellationToken);

                        // 触发动作完成事件
                        OnActionExecuted(new MacroExecutionEventArgs
                        {
                            Action = action,
                            ActionIndex = i,
                            Success = result.Success,
                            Message = result.Message,
                            MatchResult = result.MatchResult
                        });

                        // 如果动作失败且不是"直到找到"模式，继续执行下一个
                        // 如果是"直到找到"模式但超时了，也继续执行
                        if (!result.Success)
                        {
                            System.Diagnostics.Debug.WriteLine($"动作执行失败: {result.Message}");
                        }
                    }

                    CompletedLoopCount++;

                    // 检查是否需要继续循环
                    if (shouldLoop)
                    {
                        if (targetLoopCount > 0 && CompletedLoopCount >= targetLoopCount)
                        {
                            break; // 达到目标循环次数
                        }

                        // 循环间隔
                        if (loopInterval > 0)
                        {
                            await Task.Delay(loopInterval, cancellationToken);
                        }
                    }
                } while (shouldLoop && !cancellationToken.IsCancellationRequested);

                // 执行完成
                SetState(MacroExecutionState.Completed);
                OnExecutionCompleted(new MacroExecutionEventArgs
                {
                    Success = true,
                    Message = $"宏执行完成，共 {CompletedLoopCount} 次循环"
                });
            }
            catch (OperationCanceledException)
            {
                SetState(MacroExecutionState.Stopped);
                OnExecutionCompleted(new MacroExecutionEventArgs
                {
                    Success = false,
                    Message = "执行已取消"
                });
            }
            catch (Exception ex)
            {
                SetState(MacroExecutionState.Stopped);
                OnExecutionCompleted(new MacroExecutionEventArgs
                {
                    Success = false,
                    Message = $"执行出错: {ex.Message}"
                });
                System.Diagnostics.Debug.WriteLine($"宏执行异常: {ex}");
            }
            finally
            {
                // 仅在非取消/非完成状态下回到 Idle，避免覆盖 Stopped/Completed 状态
                if (State != MacroExecutionState.Stopped && State != MacroExecutionState.Completed)
                {
                    SetState(MacroExecutionState.Idle);
                }
            }
        }

        /// <summary>
        /// 执行单个动作
        /// </summary>
        private async Task<(bool Success, string Message, MatchResult? MatchResult)> ExecuteActionAsync(MacroAction action, CancellationToken cancellationToken)
        {
            try
            {
                switch (action)
                {
                    case FindImageAction findImageAction:
                        return await ExecuteFindImageActionAsync(findImageAction, cancellationToken);

                    case WaitAction waitAction:
                        return await ExecuteWaitActionAsync(waitAction, cancellationToken);

                    case MouseClickAction mouseClickAction:
                        return await ExecuteMouseClickActionAsync(mouseClickAction, cancellationToken);

                    default:
                        return (false, $"未知的动作类型: {action.Type}", null);
                }
            }
            catch (OperationCanceledException)
            {
                return (false, "动作被取消", null);
            }
            catch (Exception ex)
            {
                return (false, $"动作执行异常: {ex.Message}", null);
            }
        }

        /// <summary>
        /// 执行找图动作
        /// </summary>
        private async Task<(bool Success, string Message, MatchResult? MatchResult)> ExecuteFindImageActionAsync(FindImageAction action, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(action.ImagePath))
            {
                return (false, "图像路径为空", null);
            }

            MatchResult? matchResult = null;

            // 如果需要等待直到找到
            if (action.WaitUntilFound)
            {
                matchResult = await _imageMatchService.WaitForImageAsync(
                    action.ImagePath,
                    action.MatchThreshold,
                    FindImageTimeoutMs,
                    FindImageIntervalMs,
                    cancellationToken);

                // 检查是否被取消
                if (cancellationToken.IsCancellationRequested)
                {
                    return (false, "等待被取消", matchResult);
                }
            }
            else
            {
                // 单次查找
                matchResult = _imageMatchService.FindImage(action.ImagePath, action.MatchThreshold);
            }

            if (!matchResult.Found)
            {
                return (false, "未找到目标图像", matchResult);
            }

            // 执行操作
            var clickX = matchResult.X + action.OffsetX;
            var clickY = matchResult.Y + action.OffsetY;

            switch (action.Operation.ToLower())
            {
                case "click":
                    if (!Win32Api.LeftClick(clickX, clickY))
                    {
                        return (false, $"点击失败: 无法移动鼠标到 ({clickX}, {clickY})", matchResult);
                    }
                    await Task.Delay(50, cancellationToken); // 短暂延迟确保点击生效
                    return (true, $"点击位置: ({clickX}, {clickY})", matchResult);

                case "rightclick":
                    if (!Win32Api.RightClick(clickX, clickY))
                    {
                        return (false, $"右键点击失败: 无法移动鼠标到 ({clickX}, {clickY})", matchResult);
                    }
                    await Task.Delay(50, cancellationToken);
                    return (true, $"右键点击位置: ({clickX}, {clickY})", matchResult);

                default:
                    return (false, $"未知的操作类型: {action.Operation}", matchResult);
            }
        }

        /// <summary>
        /// 执行等待动作
        /// </summary>
        private async Task<(bool Success, string Message, MatchResult? MatchResult)> ExecuteWaitActionAsync(WaitAction action, CancellationToken cancellationToken)
        {
            if (action.WaitSeconds <= 0)
            {
                return (true, "等待时间为0，跳过", null);
            }

            await Task.Delay((int)(action.WaitSeconds * 1000), cancellationToken);
            return (true, $"等待 {action.WaitSeconds} 秒", null);
        }

        /// <summary>
        /// 执行鼠标点击位置动作
        /// </summary>
        private async Task<(bool Success, string Message, MatchResult? MatchResult)> ExecuteMouseClickActionAsync(MouseClickAction action, CancellationToken cancellationToken)
        {
            int x = action.X;
            int y = action.Y;

            switch (action.Operation.ToLower())
            {
                case "click":
                    if (!Win32Api.LeftClick(x, y))
                    {
                        return (false, $"点击失败: 无法移动鼠标到 ({x}, {y})", null);
                    }
                    await Task.Delay(50, cancellationToken);
                    return (true, $"点击位置: ({x}, {y})", null);

                case "rightclick":
                    if (!Win32Api.RightClick(x, y))
                    {
                        return (false, $"右键点击失败: 无法移动鼠标到 ({x}, {y})", null);
                    }
                    await Task.Delay(50, cancellationToken);
                    return (true, $"右键点击位置: ({x}, {y})", null);

                default:
                    return (false, $"未知的操作类型: {action.Operation}", null);
            }
        }

        /// <summary>
        /// 设置执行状态
        /// </summary>
        private void SetState(MacroExecutionState newState)
        {
            if (State != newState)
            {
                State = newState;
                StateChanged?.Invoke(this, newState);
                System.Diagnostics.Debug.WriteLine($"宏执行状态变更: {newState}");
            }
        }

        /// <summary>
        /// 触发动作执行完成事件
        /// </summary>
        protected virtual void OnActionExecuted(MacroExecutionEventArgs e)
        {
            ActionExecuted?.Invoke(this, e);
        }

        /// <summary>
        /// 触发宏执行完成事件
        /// </summary>
        protected virtual void OnExecutionCompleted(MacroExecutionEventArgs e)
        {
            ExecutionCompleted?.Invoke(this, e);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            lock (_lockObject)
            {
                if (_disposed)
                    return;

                _disposed = true;
            }

            Stop();

            // 清理事件订阅
            ActionExecuted = null;
            ExecutionCompleted = null;
            StateChanged = null;

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }
}