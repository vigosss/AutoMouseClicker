using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Ming_AutoClicker.Helpers;
using Ming_AutoClicker.Models;
using Ming_AutoClicker.Services;

namespace Ming_AutoClicker.ViewModels;

public enum RecordingState { Idle, Recording, RecordPaused, Playing, PlayPaused, Stopping }
public enum RecordingSessionResult { None, Completed, Stopped, Failed }

public sealed class RecordingItemViewModel : ViewModelBase
{
    private readonly RecordingPageViewModel _owner;
    public Recording Model { get; }
    public string Id => Model.Id;
    public string Name => Model.Name;
    public string CreatedAtText => Model.CreatedAt.ToString("yyyy-MM-dd HH:mm");
    public string DurationText => TimeSpan.FromMilliseconds(Model.Duration)
        .ToString(Model.Duration >= 3600000 ? @"hh\:mm\:ss\.f" : @"mm\:ss\.f");
    public string ActionCountText => LocalizationService.Current.Format("RecordingStepCount", Model.ActionCount);
    public string HotkeyText => Model.Hotkey == null
        ? LocalizationService.Current.GetString("RecordingSetHotkey")
        : $"{LocalizationService.Current.GetString("RecordingSetHotkey")} · {HotkeyGestureHelper.Format(Model.Hotkey)}";
    public bool IsSelected => ReferenceEquals(_owner.SelectedRecording, this);
    public ICommand SelectCommand { get; }
    public ICommand CancelSelectCommand { get; }
    public ICommand RenameCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand OptimizeCommand { get; }
    public ICommand SetHotkeyCommand { get; }

    public RecordingItemViewModel(Recording model, RecordingPageViewModel owner)
    {
        Model = model;
        _owner = owner;
        SelectCommand = new RelayCommand(_ => owner.SelectedRecording = this, _ => owner.IsIdle);
        CancelSelectCommand = owner.ClearSelectionCommand;
        RenameCommand = new RelayCommand(_ => owner.RequestRename(this), _ => owner.IsIdle);
        DeleteCommand = new RelayCommand(_ => owner.Delete(this), _ => owner.IsIdle);
        OptimizeCommand = new RelayCommand(_ => owner.RequestOptimize(this), _ => owner.IsIdle);
        SetHotkeyCommand = new RelayCommand(_ => owner.RequestHotkey(this), _ => owner.IsIdle);
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(ActionCountText));
        OnPropertyChanged(nameof(HotkeyText));
    }
}

public sealed class RecordingPageViewModel : ViewModelBase, IDataErrorInfo
{
    private readonly RecordingStorageService _storage;
    private readonly IRecordingCaptureService _hook;
    private readonly IPlaybackService _playback;
    private readonly TimeProvider _timeProvider;
    private readonly DispatcherTimer _timer;
    private RecordingItemViewModel? _selected;
    private RecordingState _state;
    private RecordingSessionResult _lastResult;
    private bool _recordSession = true, _infiniteLoop, _starting, _disposed, _exitPending;
    private Task<PlaybackOutcome>? _playbackTask;
    private int _recordedCount, _completedCount, _totalCount, _currentLoop, _runLoopCount = 1;
    private long _session, _clockStarted;
    private TimeSpan _elapsed;
    private bool _clockRunning;
    private string _speedText = "1.0", _loopText = "1", _intervalText = "0";
    private string? _errorMessage;
    private Action<int>? _recordedHandler;

    public ObservableCollection<RecordingItemViewModel> Recordings { get; } = new();

    public RecordingItemViewModel? SelectedRecording
    {
        get => _selected;
        set
        {
            if (!IsIdle)
            {
                OnPropertyChanged(); // Restore a rejected keyboard or programmatic selection.
                return;
            }
            if (!SetProperty(ref _selected, value)) return;
            foreach (var item in Recordings) item.Refresh();
            ResetSession();
            OnPropertyChanged(nameof(HasSelection));
            NotifyState();
        }
    }

    public bool HasSelection => SelectedRecording != null;
    public RecordingState State => _state;
    public bool IsIdle => State == RecordingState.Idle && !_starting && !_disposed && !_exitPending;
    public bool IsPaused => State is RecordingState.RecordPaused or RecordingState.PlayPaused;
    public bool IsStopping => State == RecordingState.Stopping;
    public bool IsRecording => State is RecordingState.Recording or RecordingState.RecordPaused || IsStopping && _recordSession;
    public bool IsPlaying => State is RecordingState.Playing or RecordingState.PlayPaused || IsStopping && !_recordSession;
    public bool CanEditPlaybackOptions => IsIdle && HasSelection;
    public bool CanEditLoopCount => CanEditPlaybackOptions && !IsInfiniteLoop;
    public RecordingSessionResult LastResult => _lastResult;
    public int RecordedActionCount => _recordedCount;
    public int CompletedActionCount => _completedCount;
    public int TotalActionCount => _totalCount;
    public int CurrentLoop => _currentLoop;
    public int ActionCount => _recordSession ? RecordedActionCount : CompletedActionCount;
    public string? ErrorMessage => _errorMessage;

    public string ElapsedTime
    {
        get
        {
            var elapsed = _elapsed + (_clockRunning ? _timeProvider.GetElapsedTime(_clockStarted) : TimeSpan.Zero);
            return $"{(long)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}.{elapsed.Milliseconds / 100}";
        }
    }

    public string SpeedText { get => _speedText; set => SetOption(ref _speedText, value, nameof(SpeedText)); }
    public string LoopCountText { get => _loopText; set => SetOption(ref _loopText, value, nameof(LoopCountText)); }
    public string LoopIntervalText { get => _intervalText; set => SetOption(ref _intervalText, value, nameof(LoopIntervalText)); }
    public double SpeedFactor
    {
        get => TrySpeed(out var speed) ? speed : 1;
        set
        {
            if (!double.IsFinite(value)) return;
            SpeedText = Math.Round(Math.Clamp(value, .5, 5), 1, MidpointRounding.AwayFromZero)
                .ToString("F1", CultureInfo.InvariantCulture);
        }
    }
    public string SpeedDisplayText => SpeedFactor.ToString("F1", CultureInfo.InvariantCulture) + "×";
    public int LoopCount { get => int.TryParse(LoopCountText, out var loops) ? loops : 1; set => LoopCountText = value.ToString(CultureInfo.InvariantCulture); }
    public int LoopInterval { get => int.TryParse(LoopIntervalText, out var interval) ? interval : 0; set => LoopIntervalText = value.ToString(CultureInfo.InvariantCulture); }
    public bool IsInfiniteLoop
    {
        get => _infiniteLoop;
        set
        {
            if (!IsIdle || !SetProperty(ref _infiniteLoop, value)) return;
            OnPropertyChanged(nameof(CanEditLoopCount));
            OnPropertyChanged(nameof(LoopCountText));
            NotifyOptions();
        }
    }

    public string this[string columnName] => columnName switch
    {
        nameof(SpeedText) when !TrySpeed(out _) => Text("PlaybackInvalidSpeed"),
        nameof(LoopCountText) when !IsInfiniteLoop && (!int.TryParse(LoopCountText, out var loops) || loops < 1) => Text("PlaybackInvalidLoopCount"),
        nameof(LoopIntervalText) when !int.TryParse(LoopIntervalText, out var interval) || interval < 0 => Text("PlaybackInvalidInterval"),
        _ => string.Empty
    };
    public string Error => string.Join(" ", new[] { this[nameof(SpeedText)], this[nameof(LoopCountText)], this[nameof(LoopIntervalText)] }.Where(x => x.Length > 0));
    public bool HasValidationErrors => Error.Length > 0;
    private bool CanStart => IsIdle && (!HasSelection || SelectedRecording!.Model.ActionCount > 0 && !HasValidationErrors);

    public string StartButtonText => State switch
    {
        RecordingState.Recording => Text("RecordingStatusRecording"),
        RecordingState.RecordPaused => Text("RecordingStatusPaused"),
        RecordingState.Playing or RecordingState.PlayPaused => Text("PlaybackStop"),
        RecordingState.Stopping => Text("RecordingStatusStopping"),
        _ => Text(HasSelection ? "PlaybackStart" : "RecordingStart")
    };
    public string PauseButtonText => Text(IsPaused ? "RecordingResume" : "RecordingPause");
    public string PauseButtonTooltip => Text(_recordSession
        ? IsPaused ? "RecordingResumeTooltip" : "RecordingPauseTooltip"
        : IsPaused ? "PlaybackResumeTooltip" : "PlaybackPauseTooltip");
    public string StopButtonTooltip => Text(_recordSession ? "RecordingStopTooltip" : "PlaybackStopTooltip");
    public string StatusLabel => State switch
    {
        RecordingState.Recording => Text("RecordingStatusRecording"),
        RecordingState.RecordPaused => Text("RecordingStatusPaused"),
        RecordingState.Playing => Text("RecordingStatusPlaying"),
        RecordingState.PlayPaused => Text("PlaybackStatusPaused"),
        RecordingState.Stopping => Text("RecordingStatusStopping"),
        _ => LastResult switch
        {
            RecordingSessionResult.Completed => Text("PlaybackStatusCompleted"),
            RecordingSessionResult.Stopped => Text(_recordSession ? "RecordingStatusStopped" : "PlaybackStatusStopped"),
            RecordingSessionResult.Failed => Text(_recordSession ? "RecordingStatusFailed" : "PlaybackStatusFailed"),
            _ => HasSelection ? LocalizationService.Current.Format("PlaybackReadyStatus", TotalActionCount) : Text("RecordingReadyStatus")
        }
    };
    public string StatusText
    {
        get
        {
            if (State == RecordingState.Idle && LastResult == RecordingSessionResult.None)
            {
                if (HasSelection && TotalActionCount == 0) return Text("RecordingEmptyPlayback");
                if (HasSelection && HasValidationErrors) return Error;
                return StatusLabel;
            }
            return _recordSession
                ? LocalizationService.Current.Format("RecordingSessionStatus", StatusLabel, RecordedActionCount, ElapsedTime)
                : LocalizationService.Current.Format("PlaybackSessionStatus", StatusLabel, CompletedActionCount, TotalActionCount,
                    CurrentLoop, _runLoopCount == -1 ? Text("PlaybackInfiniteCount") : _runLoopCount.ToString(), ElapsedTime);
        }
    }

    public string CompactStatusText => State == RecordingState.Idle && LastResult == RecordingSessionResult.None
        ? StatusText
        : _recordSession
            ? LocalizationService.Current.Format("RecordingCompactStatus", StatusLabel, RecordedActionCount)
            : LocalizationService.Current.Format("PlaybackCompactStatus", StatusLabel, CompletedActionCount, TotalActionCount);
    public string ElapsedStatusText => LocalizationService.Current.Format("RecordingElapsedStatus", ElapsedTime);

    public ICommand PrimaryActionCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ClearSelectionCommand { get; }
    public ICommand StartRecordingCommand { get; }
    public ICommand StopRecordingCommand => StopCommand;
    public ICommand PauseRecordingCommand => PauseCommand;
    public ICommand StartPlaybackCommand { get; }
    public ICommand StopPlaybackCommand => StopCommand;
    public ICommand PausePlaybackCommand => PauseCommand;
    public ICommand ImportCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand HelpCommand { get; }
    public event Action<RecordingItemViewModel>? OptimizeRequested;
    public event Action<RecordingItemViewModel>? HotkeyRequested;
    public event Action<RecordingItemViewModel>? RenameRequested;
    public event Action<string>? RecordingDeleted;

    public RecordingPageViewModel(RecordingStorageService storage, IRecordingCaptureService hook,
        IPlaybackService playback, TimeProvider? timeProvider = null)
    {
        _storage = storage;
        _hook = hook;
        _playback = playback;
        _timeProvider = timeProvider ?? TimeProvider.System;
        PrimaryActionCommand = new RelayCommand(_ => ExecutePrimaryAction(),
            _ => !_disposed && !_starting && !IsStopping && (IsPlaying || CanStart));
        StartCommand = new RelayCommand(_ => Start(), _ => CanStart);
        PauseCommand = new RelayCommand(_ => TogglePause(), _ => !IsStopping && (IsRecording || IsPlaying));
        StopCommand = new RelayCommand(_ => Stop(), _ => !IsStopping && (IsRecording || IsPlaying));
        ClearSelectionCommand = new RelayCommand(_ => SelectedRecording = null, _ => IsIdle && HasSelection);
        StartRecordingCommand = new RelayCommand(_ => StartRecording(), _ => CanStart && !HasSelection);
        StartPlaybackCommand = new RelayCommand(_ => _ = StartPlaybackAsync(), _ => CanStart && HasSelection);
        ImportCommand = new RelayCommand(_ => Import(), _ => IsIdle);
        ExportCommand = new RelayCommand(_ => Export(), _ => IsIdle && HasSelection);
        HelpCommand = new RelayCommand(_ => ShowMessage(Text("RecordingHelpText"), Text("RecordingHelpTitle")));
        foreach (var recording in storage.LoadAll()) Recordings.Add(new(recording, this));
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += OnTimerTick;
        LocalizationService.Current.LanguageChanged += OnLanguageChanged;
    }

    public void Toggle()
    {
        if (IsStopping || _starting || _disposed) return;
        if (IsRecording || IsPlaying) Stop();
        else Start();
    }

    private void ExecutePrimaryAction()
    {
        if (_disposed || _starting || IsStopping) return;
        if (IsPlaying) Stop();
        else Start();
    }

    private void Start()
    {
        if (!CanStart) return;
        if (HasSelection) _ = StartPlaybackAsync();
        else StartRecording();
    }

    private void StartRecording()
    {
        if (!CanStart || HasSelection) return;
        ResetSession();
        _recordSession = true;
        var session = _session;
        _recordedHandler = count => BeginOnUIThread(() =>
        {
            if (_disposed || session != _session || !IsRecording || IsStopping) return;
            _recordedCount = count;
            NotifyProgress();
        });
        _hook.OnActionRecorded += _recordedHandler;
        SetState(RecordingState.Recording);
        try
        {
            _hook.StartRecording();
            StartClock();
        }
        catch (Exception ex)
        {
            DetachRecordingHandler();
            try { _hook.StopRecording(); } catch { }
            SetFailure(ex, "RecordingError");
            SetState(RecordingState.Idle);
        }
    }

    private void Stop()
    {
        if (IsStopping || _disposed) return;
        if (IsRecording) StopRecording();
        else if (IsPlaying)
        {
            StopClock();
            SetState(RecordingState.Stopping);
            _playback.StopPlayback();
        }
    }

    private void StopRecording()
    {
        StopClock();
        SetState(RecordingState.Stopping);
        RecordingItemViewModel? savedItem = null;
        try
        {
            var actions = _hook.StopRecording();
            _recordedCount = actions.Count;
            _lastResult = RecordingSessionResult.Stopped;
            if (actions.Count > 0)
            {
                var bounds = Win32Api.GetVirtualScreenBounds();
                var recording = new Recording
                {
                    Name = LocalizationService.Current.Format("RecordingDefaultName", DateTimeOffset.Now.ToUnixTimeSeconds()),
                    DesktopBounds = new DesktopBounds { X = bounds.x, Y = bounds.y, Width = bounds.width, Height = bounds.height },
                    KeyboardLayout = Win32Api.GetKeyboardLayout(0).ToInt64().ToString("X"),
                    Actions = actions
                };
                _storage.Save(recording);
                savedItem = new RecordingItemViewModel(recording, this);
                Recordings.Add(savedItem);
            }
        }
        catch (Exception ex) { SetFailure(ex, "RecordingError"); }
        finally
        {
            DetachRecordingHandler();
            SetState(RecordingState.Idle);
            NotifyProgress();
        }
        if (savedItem != null)
        {
            SelectedRecording = savedItem;
            if (savedItem.Model.Actions.Count >= 10000) ShowMessage(Text("RecordingLargeMessage"), Text("RecordingLargeTitle"));
        }
    }

    public async Task StartPlaybackAsync()
    {
        if (!CanStart || SelectedRecording == null) return;
        var recording = SelectedRecording.Model.DeepClone();
        var options = new PlaybackOptions { SpeedFactor = SpeedFactor, LoopCount = IsInfiniteLoop ? -1 : LoopCount, LoopInterval = LoopInterval };
        _starting = true;
        NotifyState();
        try
        {
            var current = Win32Api.GetVirtualScreenBounds();
            var old = recording.DesktopBounds;
            if (old.Width > 0 && (old.X != current.x || old.Y != current.y || old.Width != current.width || old.Height != current.height)
                && !ShowConfirm(Text("RecordingDisplayChangedMessage"), Text("RecordingDisplayChangedTitle"))) return;
            if (_exitPending || _disposed) return;
            ResetSession();
            _recordSession = false;
            _totalCount = recording.ActionCount;
            _currentLoop = 1;
            _runLoopCount = options.LoopCount;
            _starting = false;
            var session = _session;
            PlaybackProgress? latestProgress = null;
            var dispatchPending = 0;
            void ApplyLatestProgress()
            {
                if (_disposed || session != _session || !IsPlaying) return;
                var progress = Volatile.Read(ref latestProgress);
                if (progress == null) return;
                _completedCount = progress.CompletedActions;
                _totalCount = progress.TotalActions;
                _currentLoop = progress.CurrentLoop;
                NotifyProgress();
            }
            void ReportProgress(PlaybackProgress progress)
            {
                // Keep at most one queued UI update, even for large zero-delay recordings.
                Volatile.Write(ref latestProgress, progress);
                if (Interlocked.Exchange(ref dispatchPending, 1) != 0) return;
                BeginOnUIThread(() =>
                {
                    Interlocked.Exchange(ref dispatchPending, 0);
                    ApplyLatestProgress();
                });
            }
            SetState(RecordingState.Playing);
            StartClock();
            try
            {
                _playbackTask = _playback.StartPlaybackAsync(recording, options, ReportProgress);
                var outcome = await _playbackTask;
                if (!_disposed && session == _session)
                    _lastResult = outcome == PlaybackOutcome.Completed ? RecordingSessionResult.Completed : RecordingSessionResult.Stopped;
            }
            catch (Exception ex)
            {
                if (!_disposed && session == _session) SetFailure(ex, "PlaybackError");
            }
            finally
            {
                if (!_disposed && session == _session)
                {
                    // Capture the final executed action even if its UI notification was still queued.
                    ApplyLatestProgress();
                    StopClock();
                    SetState(RecordingState.Idle);
                    NotifyProgress();
                }
            }
        }
        finally { _starting = false; if (!_disposed) NotifyState(); }
    }

    /// <summary>Keep input cleanup alive before the process exits, without blocking the dispatcher.</summary>
    public async Task PrepareForExitAsync(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        _exitPending = true;
        NotifyState();
        try
        {
            var playback = _playbackTask;
            if (playback is { IsCompleted: false })
            {
                Stop();
                await playback.WaitAsync(timeout);
            }
            // Remain locked until disposal. A hotkey must not start a new session
            // after this task completes but before the caller shuts down.
        }
        catch
        {
            _exitPending = false;
            NotifyState();
            throw;
        }
    }

    public void CancelExitPreparation()
    {
        _exitPending = false;
        if (!_disposed) NotifyState();
    }

    private void TogglePause()
    {
        switch (State)
        {
            case RecordingState.Recording:
                _hook.PauseRecording(); StopClock(); SetState(RecordingState.RecordPaused); break;
            case RecordingState.RecordPaused:
                _hook.ResumeRecording(); StartClock(); SetState(RecordingState.Recording); break;
            case RecordingState.Playing:
                _playback.PausePlayback(); StopClock(); SetState(RecordingState.PlayPaused); break;
            case RecordingState.PlayPaused:
                _playback.ResumePlayback(); StartClock(); SetState(RecordingState.Playing); break;
        }
    }

    private void ResetSession()
    {
        _session++;
        _timer.Stop();
        _clockRunning = false;
        _elapsed = TimeSpan.Zero;
        _recordSession = !HasSelection;
        _recordedCount = _completedCount = _currentLoop = 0;
        _totalCount = SelectedRecording?.Model.ActionCount ?? 0;
        _lastResult = RecordingSessionResult.None;
        _errorMessage = null;
        NotifyProgress();
    }

    private void StartClock()
    {
        if (_clockRunning) return;
        _clockStarted = _timeProvider.GetTimestamp();
        _clockRunning = true;
        _timer.Start();
        NotifyProgress();
    }
    private void StopClock()
    {
        if (_clockRunning) _elapsed += _timeProvider.GetElapsedTime(_clockStarted);
        _clockRunning = false;
        _timer.Stop();
        NotifyProgress();
    }
    private void OnTimerTick(object? sender, EventArgs args)
    {
        OnPropertyChanged(nameof(ElapsedTime));
        OnPropertyChanged(nameof(ElapsedStatusText));
        OnPropertyChanged(nameof(StatusText));
    }
    private void SetState(RecordingState state) { _state = state; NotifyState(); }
    private void NotifyState()
    {
        foreach (var name in new[] { nameof(State), nameof(IsIdle), nameof(IsPaused), nameof(IsStopping), nameof(IsRecording), nameof(IsPlaying),
            nameof(CanEditPlaybackOptions), nameof(CanEditLoopCount), nameof(StartButtonText), nameof(PauseButtonText),
            nameof(PauseButtonTooltip), nameof(StopButtonTooltip), nameof(StatusLabel), nameof(StatusText), nameof(CompactStatusText),
            nameof(ElapsedStatusText), nameof(LastResult) })
            OnPropertyChanged(name);
        CommandManager.InvalidateRequerySuggested();
    }
    private void NotifyProgress()
    {
        foreach (var name in new[] { nameof(RecordedActionCount), nameof(CompletedActionCount), nameof(TotalActionCount), nameof(CurrentLoop),
            nameof(ActionCount), nameof(ElapsedTime), nameof(ElapsedStatusText), nameof(LastResult), nameof(ErrorMessage),
            nameof(StatusLabel), nameof(StatusText), nameof(CompactStatusText) })
            OnPropertyChanged(name);
    }
    private void SetOption(ref string field, string value, string name)
    {
        if (!IsIdle || !SetProperty(ref field, value, name)) return;
        if (name == nameof(SpeedText))
        {
            OnPropertyChanged(nameof(SpeedFactor));
            OnPropertyChanged(nameof(SpeedDisplayText));
        }
        NotifyOptions();
    }
    private void NotifyOptions()
    {
        OnPropertyChanged(nameof(HasValidationErrors));
        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CompactStatusText));
        CommandManager.InvalidateRequerySuggested();
    }
    private bool TrySpeed(out double speed) =>
        (double.TryParse(SpeedText, NumberStyles.Float, CultureInfo.InvariantCulture, out speed)
         || double.TryParse(SpeedText, NumberStyles.Float, CultureInfo.CurrentCulture, out speed))
        && double.IsFinite(speed) && speed >= .5 && speed <= 5;
    private static string Text(string key) => LocalizationService.Current.GetString(key);
    private void SetFailure(Exception exception, string key)
    {
        _lastResult = RecordingSessionResult.Failed;
        _errorMessage = LocalizationService.Current.Format(key, exception.Message);
        NotifyProgress();
    }
    private void DetachRecordingHandler()
    {
        if (_recordedHandler != null) _hook.OnActionRecorded -= _recordedHandler;
        _recordedHandler = null;
    }

    internal void Save(Recording recording) => _storage.Save(recording);
    internal void OnRecordingOptimized(RecordingItemViewModel item)
    {
        item.Refresh();
        if (IsIdle && ReferenceEquals(SelectedRecording, item)) ResetSession();
        CommandManager.InvalidateRequerySuggested();
    }
    internal void Delete(RecordingItemViewModel item)
    {
        if (!IsIdle || !ShowConfirm(LocalizationService.Current.Format("RecordingDeletePrompt", item.Name))) return;
        _storage.Delete(item.Id);
        RecordingDeleted?.Invoke(item.Id);
        Recordings.Remove(item);
        if (SelectedRecording == item) SelectedRecording = null;
    }
    internal void RequestOptimize(RecordingItemViewModel item) { if (IsIdle) OptimizeRequested?.Invoke(item); }
    internal void RequestHotkey(RecordingItemViewModel item) { if (IsIdle) HotkeyRequested?.Invoke(item); }
    internal void RequestRename(RecordingItemViewModel item) { if (IsIdle) RenameRequested?.Invoke(item); }
    internal bool TryRename(RecordingItemViewModel item, string name, out string error)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) { error = Text("RecordingRenameEmpty"); return false; }
        var previous = item.Model.Name;
        try { item.Model.Name = trimmed; _storage.Save(item.Model); item.Refresh(); error = string.Empty; return true; }
        catch (Exception ex)
        {
            item.Model.Name = previous;
            item.Refresh();
            error = LocalizationService.Current.Format("RecordingRenameSaveFailed", ex.Message);
            return false;
        }
    }
    private void Import()
    {
        if (!IsIdle) return;
        var dialog = new OpenFileDialog { Filter = "JSON|*.json" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var recording = _storage.ImportFromFile(dialog.FileName);
            recording.Hotkey = null;
            _storage.Save(recording);
            var item = new RecordingItemViewModel(recording, this);
            Recordings.Add(item);
            SelectedRecording = item;
        }
        catch (Exception ex) { ShowMessage(ex.Message, Text("RecordingImportErrorTitle"), System.Windows.MessageBoxImage.Error); }
    }
    private void Export()
    {
        if (!IsIdle || SelectedRecording == null) return;
        var dialog = new SaveFileDialog { Filter = "JSON|*.json", FileName = SelectedRecording.Name + ".json" };
        if (dialog.ShowDialog() == true) _storage.ExportToFile(SelectedRecording.Id, dialog.FileName);
    }
    private void OnLanguageChanged(object? sender, EventArgs args) => OnUIThread(() =>
    {
        foreach (var item in Recordings) item.Refresh();
        NotifyState();
        NotifyOptions();
    });
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _session++;
            LocalizationService.Current.LanguageChanged -= OnLanguageChanged;
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            DetachRecordingHandler();
            _hook.Dispose();
            _playback.Dispose();
        }
        base.Dispose(disposing);
    }
}
