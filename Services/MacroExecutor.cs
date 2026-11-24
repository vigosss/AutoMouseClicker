using System;
using System.Drawing;
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

    public enum MacroStepOutcome
    {
        Completed,
        Started,
        Skipped,
        RoundEnded,
        Failed
    }

    /// <summary>
    /// 宏执行事件参数
    /// </summary>
    public class MacroExecutionEventArgs : EventArgs
    {
        public string RootMacroId { get; set; } = string.Empty;
        public string MacroName { get; set; } = string.Empty;
        public string MacroPath { get; set; } = string.Empty;
        public MacroStepOutcome Outcome { get; set; }

        internal MacroExecutionEventArgs WithResult(MacroStepOutcome outcome, string message,
            MatchResult? matchResult = null, string? rootMacroId = null)
        {
            var result = (MacroExecutionEventArgs)MemberwiseClone();
            result.Outcome = outcome;
            result.Success = outcome is MacroStepOutcome.Completed or MacroStepOutcome.Skipped or MacroStepOutcome.RoundEnded;
            result.Message = message;
            result.MatchResult = matchResult ?? MatchResult;
            result.RootMacroId = rootMacroId ?? RootMacroId;
            return result;
        }

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
        /// 找图或找色步骤的匹配结果
        /// </summary>
        public MatchResult? MatchResult { get; set; }
    }

    /// <summary>
    /// 宏执行器 - 负责执行宏配置中的动作序列
    /// </summary>
    public class MacroExecutor : IDisposable
    {
        private readonly ImageMatchService _imageMatchService;
        private readonly ColorSearchService _colorSearchService;
        private readonly MacroInputService _inputService;
        
        private Task? _executionTask;
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly object _lockObject = new();
        
        private volatile MacroProfile? _currentProfile;
        private volatile MacroProfile? _rootProfile;
        private volatile bool _isPaused;
        private bool _disposed;

        /// <summary>
        /// 当前执行状态
        /// </summary>
        public MacroExecutionState State { get; private set; } = MacroExecutionState.Idle;

        /// <summary>
        /// 是否正在运行
        /// </summary>
        public bool IsRunning => State is MacroExecutionState.Running or MacroExecutionState.Paused;

        /// <summary>
        /// 是否已暂停
        /// </summary>
        public bool IsPaused => _isPaused;

        /// <summary>
        /// 当前执行的宏配置
        /// </summary>
        public MacroProfile? CurrentProfile => _currentProfile;
        public string? RootMacroId => _rootProfile?.Id;

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
        public event EventHandler<MacroExecutionEventArgs>? ActionStarted;

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
        public int FindImageIntervalMs { get; set; } = 200;

        public MacroExecutor(ImageMatchService imageMatchService, ScreenCaptureService screenCaptureService,
            MacroInputService? inputService = null, ColorSearchService? colorSearchService = null)
        {
            _imageMatchService = imageMatchService ?? throw new ArgumentNullException(nameof(imageMatchService));
            ArgumentNullException.ThrowIfNull(screenCaptureService);
            _colorSearchService = colorSearchService ?? new ColorSearchService(new DesktopImageFrameSource(screenCaptureService));
            _inputService = inputService ?? new MacroInputService();
        }

        /// <summary>
        /// 启动宏执行
        /// </summary>
        /// <param name="profile">宏配置</param>
        /// <returns>是否启动成功</returns>
        public bool Start(MacroProfile profile)
        {
            ArgumentNullException.ThrowIfNull(profile);
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!MacroExecutionPlan.TryPrepare(profile, [profile], [], _ => string.Empty, out var plan, out var failure))
            {
                OnExecutionCompleted(failure);
                return false;
            }
            return Start(plan);
        }

        public bool Start(MacroExecutionPlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);
            lock (_lockObject)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_executionTask != null && !_executionTask.IsCompleted)
                {
                    return false;
                }

                _cancellationTokenSource?.Dispose();
                _rootProfile = _currentProfile = plan.Entry;
                _cancellationTokenSource = new CancellationTokenSource();
                var cancellationToken = _cancellationTokenSource.Token;
                _isPaused = false;
                CurrentActionIndex = 0;
                CompletedLoopCount = 0;

                SetState(MacroExecutionState.Running);
                // Always enter ExecuteAsync so an immediate stop still publishes completion.
                _executionTask = Task.Run(() => ExecuteAsync(plan, cancellationToken));
                return true;
            }
        }

        /// <summary>
        /// 停止宏执行
        /// </summary>
        public void Stop()
        {
            lock (_lockObject)
            {
                if (State != MacroExecutionState.Running && State != MacroExecutionState.Paused)
                {
                    return;
                }

                _cancellationTokenSource?.Cancel();
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
        private async Task ExecuteAsync(MacroExecutionPlan plan, CancellationToken cancellationToken)
        {
            var strings = LocalizationService.Current;
            var root = plan.Entry;
            var stack = new List<ExecutionFrame> { new(root) };
            var lastStep = new MacroExecutionEventArgs
            {
                RootMacroId = root.Id,
                MacroName = MacroExecutionPlan.DisplayName(root),
                MacroPath = MacroExecutionPlan.DisplayName(root)
            };
            MacroExecutionEventArgs? earlyEnd = null;
            try
            {
                while (true)
                {
                    await WaitForResumeAsync(cancellationToken);

                    var frame = stack[^1];
                    _currentProfile = frame.Profile;
                    if (frame.NextStep == frame.Profile.Actions.Count)
                    {
                        if (stack.Count > 1)
                        {
                            stack.RemoveAt(stack.Count - 1);
                            var caller = stack[^1];
                            _currentProfile = caller.Profile;
                            CurrentActionIndex = caller.NextStep - 1;
                            lastStep = caller.StartedCall!.WithResult(MacroStepOutcome.Completed,
                                caller.StartedCall.Action!.GetDescription());
                            caller.StartedCall = null;
                            OnActionExecuted(lastStep);
                            continue;
                        }

                        CompletedLoopCount++;
                        if (!root.LoopEnabled || root.LoopCount > 0 && CompletedLoopCount >= root.LoopCount)
                        {
                            var message = strings.Format(earlyEnd == null ? "ExecutorCompleted" : "ExecutorCompletedEarly",
                                CompletedLoopCount);
                            var completion = earlyEnd == null ? lastStep.WithResult(MacroStepOutcome.Completed, message)
                                : earlyEnd.WithResult(MacroStepOutcome.RoundEnded,
                                    strings.Format("MacroExecutionContext", earlyEnd.MacroPath, message));
                            Finish(completion, cancellationToken);
                            return;
                        }

                        if (root.LoopIntervalMs > 0)
                            await Task.Delay(root.LoopIntervalMs, cancellationToken);
                        frame.NextStep = 0;
                        frame.Positions.Clear();
                        earlyEnd = null;
                        continue;
                    }

                    CurrentActionIndex = frame.NextStep++;
                    var action = frame.Profile.Actions[CurrentActionIndex];
                    lastStep = new MacroExecutionEventArgs
                    {
                        RootMacroId = root.Id,
                        MacroName = MacroExecutionPlan.DisplayName(frame.Profile),
                        MacroPath = string.Join(" → ", stack.Select(item => MacroExecutionPlan.DisplayName(item.Profile))),
                        ActionIndex = CurrentActionIndex,
                        Action = action,
                        Outcome = MacroStepOutcome.Started
                    };
                    ActionStarted?.Invoke(this, lastStep);
                    await WaitForResumeAsync(cancellationToken);
                    if (action is ExecuteMacroAction transfer)
                    {
                        var target = plan.GetTarget(transfer);
                        if (transfer.Mode == MacroTransferMode.Call)
                        {
                            frame.StartedCall = lastStep;
                            stack.Add(new ExecutionFrame(target));
                            continue;
                        }

                        stack.Clear();
                        stack.Add(new ExecutionFrame(target));
                        _rootProfile = _currentProfile = root = target;
                        CompletedLoopCount = 0;
                        CurrentActionIndex = 0;
                        earlyEnd = null;
                        lastStep = lastStep.WithResult(MacroStepOutcome.Completed, transfer.GetDescription(), rootMacroId: target.Id);
                        OnActionExecuted(lastStep);
                        // Yield between jumps even for two macros consisting only of a jump.
                        await Task.Delay(10, cancellationToken);
                        continue;
                    }

                    var result = await ExecuteActionAsync(action, frame.Positions, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    var outcome = result.Success ? MacroStepOutcome.Completed : MacroStepOutcome.Failed;
                    var detail = result.Message;
                    if (!result.Success && result.MatchResult?.IsOrdinaryMiss == true)
                    {
                        var behavior = action switch
                        {
                            FindImageAction image => image.NotFoundBehavior,
                            FindColorAction color => color.NotFoundBehavior,
                            _ => ImageNotFoundBehavior.Stop
                        };
                        if (behavior == ImageNotFoundBehavior.SkipStep)
                        {
                            outcome = MacroStepOutcome.Skipped;
                            detail = strings.GetString(action is FindColorAction ? "ExecutorColorSkipped" : "ExecutorImageSkipped");
                        }
                        else if (behavior == ImageNotFoundBehavior.EndRound)
                        {
                            outcome = MacroStepOutcome.RoundEnded;
                            detail = strings.GetString(action is FindColorAction ? "ExecutorColorRoundEnded" : "ExecutorRoundEnded");
                        }
                    }
                    lastStep = lastStep.WithResult(outcome, detail, result.MatchResult);
                    OnActionExecuted(lastStep);
                    if (outcome == MacroStepOutcome.Failed)
                    {
                        var message = strings.Format("ExecutorStepFailed", lastStep.ActionIndex + 1, detail);
                        Finish(lastStep.WithResult(outcome, strings.Format("MacroExecutionContext", lastStep.MacroPath, message)),
                            cancellationToken);
                        return;
                    }
                    if (outcome == MacroStepOutcome.RoundEnded)
                    {
                        earlyEnd = lastStep;
                        stack.RemoveRange(1, stack.Count - 1);
                        stack[0].StartedCall = null;
                        stack[0].NextStep = root.Actions.Count;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Finish(lastStep.WithResult(MacroStepOutcome.Failed, strings.GetString("ExecutorCancelled")), cancellationToken);
            }
            catch (Exception ex)
            {
                var message = strings.Format("ExecutorStepFailed", lastStep.ActionIndex + 1,
                    strings.Format("ExecutorError", ex.Message));
                Finish(lastStep.WithResult(MacroStepOutcome.Failed,
                    strings.Format("MacroExecutionContext", lastStep.MacroPath, message)), cancellationToken);
            }
        }

        private sealed class ExecutionFrame(MacroProfile profile)
        {
            public MacroProfile Profile { get; } = profile;
            public int NextStep { get; set; }
            public MacroExecutionEventArgs? StartedCall { get; set; }
            public Dictionary<string, Point> Positions { get; } = new(StringComparer.Ordinal);
        }

        private async Task WaitForResumeAsync(CancellationToken cancellationToken)
        {
            while (_isPaused)
                await Task.Delay(100, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }

        private void Finish(MacroExecutionEventArgs result, CancellationToken cancellationToken)
        {
            MacroExecutionState finalState;
            bool cancelled;
            lock (_lockObject)
            {
                // Order the final transition with Stop/Pause. A stop accepted while
                // the last step finishes must still report a cancelled session.
                cancelled = cancellationToken.IsCancellationRequested;
                finalState = result.Success && !cancelled ? MacroExecutionState.Completed : MacroExecutionState.Stopped;
                State = finalState;
                _isPaused = false;
            }
            if (cancelled)
                result = result.WithResult(MacroStepOutcome.Failed, LocalizationService.Current.GetString("ExecutorCancelled"));
            StateChanged?.Invoke(this, finalState);
            OnExecutionCompleted(result);
        }

        /// <summary>
        /// 执行单个动作
        /// </summary>
        private async Task<(bool Success, string Message, MatchResult? MatchResult)> ExecuteActionAsync(
            MacroAction action, Dictionary<string, Point> positions, CancellationToken cancellationToken)
        {
            try
            {
                switch (action)
                {
                    case FindImageAction findImageAction:
                        return await ExecuteFindImageActionAsync(findImageAction, cancellationToken);

                    case FindColorAction findColorAction:
                        return await ExecuteFindColorActionAsync(findColorAction, positions, cancellationToken);

                    case WaitAction waitAction:
                        return await ExecuteWaitActionAsync(waitAction, cancellationToken);

                    case MouseClickAction mouseClickAction:
                        return await ExecuteMouseClickActionAsync(mouseClickAction, positions, cancellationToken);

                    case MouseScrollAction mouseScrollAction:
                        await _inputService.ScrollAsync(mouseScrollAction, cancellationToken);
                        return (true, mouseScrollAction.GetDescription(), null);

                    case KeyPressAction keyPressAction:
                        await _inputService.PressKeyAsync(keyPressAction, cancellationToken);
                        return (true, keyPressAction.GetDescription(), null);

                    default:
                        return (false, LocalizationService.Current.Format("ActionUnknownType", action.Type), null);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return (false, LocalizationService.Current.Format("ExecutorActionError", ex.Message), null);
            }
        }

        /// <summary>
        /// 执行找图动作
        /// </summary>
        private async Task<(bool Success, string Message, MatchResult? MatchResult)> ExecuteFindImageActionAsync(FindImageAction action, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(action.ImagePath))
            {
                return (false, LocalizationService.Current.GetString("ExecutorEmptyImagePath"), null);
            }

            action = action.Clone();
            var matchResult = await _imageMatchService.WaitForImageAsync(
                action, cancellationToken, FindImageTimeoutMs, FindImageIntervalMs,
                verifyBeforeReturn: true);

            // FindImage 是同步 CPU 操作，停止请求可能发生在匹配过程中。
            // 匹配返回后必须再次检查，绝不能在已停止的情况下操作鼠标。
            cancellationToken.ThrowIfCancellationRequested();

            if (!matchResult.Found)
            {
                var reason = string.IsNullOrWhiteSpace(matchResult.ErrorMessage)
                    ? LocalizationService.Current.GetMatchFailureText(matchResult.FailureReason)
                    : matchResult.ErrorMessage;
                return (false, LocalizationService.Current.Format("ExecutorImageNotFound", reason, matchResult.Similarity), matchResult);
            }

            return await ExecuteMatchedPointActionAsync(action.Operation, matchResult, cancellationToken);
        }

        private async Task<(bool Success, string Message, MatchResult? MatchResult)> ExecuteFindColorActionAsync(
            FindColorAction action, Dictionary<string, Point> positions, CancellationToken cancellationToken)
        {
            var strings = LocalizationService.Current;
            var save = action.Operation == ColorOperation.SavePosition;
            // A later failed search for the same variable must invalidate its previous point.
            var name = save ? action.VariableName.Trim() : string.Empty;
            if (save) positions.Remove(name);

            var result = await _colorSearchService.WaitForColorAsync(action, cancellationToken, verifyBeforeReturn: !save);
            cancellationToken.ThrowIfCancellationRequested();
            if (!result.Found)
            {
                var message = result.IsOrdinaryMiss ? strings.Format("ExecutorColorNotFound", action.TargetColor)
                    : string.IsNullOrWhiteSpace(result.ErrorMessage) ? strings.GetMatchFailureText(result.FailureReason)
                    : result.ErrorMessage;
                return (false, message, result);
            }
            if (save)
            {
                positions[name] = new Point(result.ClickX, result.ClickY);
                return (true, strings.Format("ExecutorPositionSaved", name, result.ClickX, result.ClickY), result);
            }
            return await ExecuteMatchedPointActionAsync(action.Operation.ToString(), result, cancellationToken);
        }

        private async Task<(bool Success, string Message, MatchResult? MatchResult)> ExecuteMatchedPointActionAsync(
            string operation, MatchResult matchResult, CancellationToken cancellationToken)
        {
            var clickX = matchResult.ClickX;
            var clickY = matchResult.ClickY;

            cancellationToken.ThrowIfCancellationRequested();

            switch (operation.ToLowerInvariant())
            {
                case "click":
                    cancellationToken.ThrowIfCancellationRequested();
                    await _inputService.ClickAsync(clickX, clickY, false, cancellationToken);
                    await Task.Delay(50, cancellationToken); // 短暂延迟确保点击生效
                    return (true, LocalizationService.Current.Format("ExecutorClicked", clickX, clickY), matchResult);

                case "rightclick":
                    cancellationToken.ThrowIfCancellationRequested();
                    await _inputService.ClickAsync(clickX, clickY, true, cancellationToken);
                    await Task.Delay(50, cancellationToken);
                    return (true, LocalizationService.Current.Format("ExecutorRightClicked", clickX, clickY), matchResult);

                case "moveto":
                    _inputService.MoveTo(clickX, clickY, cancellationToken);
                    return (true, LocalizationService.Current.Format("ExecutorMovedTo", clickX, clickY), matchResult);

                default:
                    return (false, LocalizationService.Current.Format("ExecutorUnknownOperation", operation), matchResult);
            }
        }

        /// <summary>
        /// 执行等待动作
        /// </summary>
        private async Task<(bool Success, string Message, MatchResult? MatchResult)> ExecuteWaitActionAsync(WaitAction action, CancellationToken cancellationToken)
        {
            if (action.WaitSeconds <= 0)
            {
                return (true, LocalizationService.Current.GetString("ExecutorZeroWait"), null);
            }

            await Task.Delay((int)(action.WaitSeconds * 1000), cancellationToken);
            return (true, LocalizationService.Current.Format("ExecutorWaited", action.WaitSeconds), null);
        }

        /// <summary>
        /// 执行鼠标点击位置动作
        /// </summary>
        private async Task<(bool Success, string Message, MatchResult? MatchResult)> ExecuteMouseClickActionAsync(
            MouseClickAction action, Dictionary<string, Point> positions, CancellationToken cancellationToken)
        {
            int x = action.X;
            int y = action.Y;
            if (action.PositionSource == ClickPositionSource.Variable)
            {
                if (!positions.TryGetValue(action.VariableName.Trim(), out var point))
                    return (false, LocalizationService.Current.Format("ExecutorPositionMissing", action.VariableName), null);
                x = point.X;
                y = point.Y;
            }

            cancellationToken.ThrowIfCancellationRequested();

            switch (action.Operation.ToLower())
            {
                case "click":
                    cancellationToken.ThrowIfCancellationRequested();
                    await _inputService.ClickAsync(x, y, false, cancellationToken);
                    await Task.Delay(50, cancellationToken);
                    return (true, LocalizationService.Current.Format("ExecutorClicked", x, y), null);

                case "rightclick":
                    cancellationToken.ThrowIfCancellationRequested();
                    await _inputService.ClickAsync(x, y, true, cancellationToken);
                    await Task.Delay(50, cancellationToken);
                    return (true, LocalizationService.Current.Format("ExecutorRightClicked", x, y), null);

                default:
                    return (false, LocalizationService.Current.Format("ExecutorUnknownOperation", action.Operation), null);
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
            ActionStarted = null;
            ActionExecuted = null;
            ExecutionCompleted = null;
            StateChanged = null;

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }
}
