using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Ming_AutoClicker.Helpers;
using Ming_AutoClicker.Models;
using Ming_AutoClicker.Services;

namespace Ming_AutoClicker.ViewModels;

public enum RecordingState { Idle, Recording, RecordPaused, Playing, PlayPaused }

public sealed class RecordingItemViewModel : ViewModelBase
{
    private readonly RecordingPageViewModel _owner;
    public Recording Model { get; }
    public string Id=>Model.Id; public string Name=>Model.Name; public string CreatedAtText=>Model.CreatedAt.ToString("yyyy-MM-dd HH:mm");
    public string DurationText=>TimeSpan.FromMilliseconds(Model.Duration).ToString(Model.Duration>=3600000?@"hh\:mm\:ss":@"mm\:ss");
    public string ActionCountText=>LocalizationService.Current.Format("RecordingStepCount",Model.ActionCount); public string HotkeyText=>Model.Hotkey==null?LocalizationService.Current.GetString("RecordingSetHotkey"):HotkeyGestureHelper.Format(Model.Hotkey);
    public bool IsSelected=>ReferenceEquals(_owner.SelectedRecording,this);
    public ICommand SelectCommand{get;} public ICommand CancelSelectCommand{get;} public ICommand RenameCommand{get;} public ICommand DeleteCommand{get;} public ICommand OptimizeCommand{get;} public ICommand SetHotkeyCommand{get;}
    public RecordingItemViewModel(Recording model,RecordingPageViewModel owner){Model=model;_owner=owner;
        SelectCommand=new RelayCommand(_=>owner.SelectedRecording=this,_=>owner.IsIdle);CancelSelectCommand=new RelayCommand(_=>owner.SelectedRecording=null,_=>owner.IsIdle);
        RenameCommand=new RelayCommand(_=>owner.RequestRename(this),_=>owner.IsIdle);
        DeleteCommand=new RelayCommand(_=>owner.Delete(this),_=>owner.IsIdle);OptimizeCommand=new RelayCommand(_=>owner.RequestOptimize(this),_=>owner.IsIdle);SetHotkeyCommand=new RelayCommand(_=>owner.RequestHotkey(this),_=>owner.IsIdle);}
    public void Refresh(){OnPropertyChanged(nameof(Name));OnPropertyChanged(nameof(IsSelected));OnPropertyChanged(nameof(DurationText));OnPropertyChanged(nameof(ActionCountText));OnPropertyChanged(nameof(HotkeyText));}
}

public sealed class RecordingPageViewModel : ViewModelBase
{
    private readonly RecordingStorageService _storage; private readonly GlobalHookService _hook; private readonly PlaybackService _playback; private readonly DispatcherTimer _timer; private DateTime _started;
    private RecordingItemViewModel? _selected; private RecordingState _state; private string _elapsed="00:00:00"; private int _actionCount; private double _speed=1; private int _loop=1,_interval;
    public ObservableCollection<RecordingItemViewModel> Recordings{get;}=new();
    public RecordingItemViewModel? SelectedRecording { get=>_selected; set { if(SetProperty(ref _selected,value)){foreach(var i in Recordings)i.Refresh();OnPropertyChanged(nameof(HasSelection));CommandManager.InvalidateRequerySuggested();}} }
    public bool HasSelection=>SelectedRecording!=null; public bool IsIdle=>State==RecordingState.Idle;
    public RecordingState State {get=>_state;private set{if(SetProperty(ref _state,value)){OnPropertyChanged(nameof(IsIdle));OnPropertyChanged(nameof(IsRecording));OnPropertyChanged(nameof(IsPlaying));CommandManager.InvalidateRequerySuggested();}}}
    public bool IsRecording=>State is RecordingState.Recording or RecordingState.RecordPaused; public bool IsPlaying=>State is RecordingState.Playing or RecordingState.PlayPaused;
    public string ElapsedTime{get=>_elapsed;private set=>SetProperty(ref _elapsed,value);} public int ActionCount{get=>_actionCount;private set=>SetProperty(ref _actionCount,value);}
    public double SpeedFactor{get=>_speed;set=>SetProperty(ref _speed,Math.Clamp(value,.5,5));} public int LoopCount{get=>_loop;set=>SetProperty(ref _loop,Math.Max(1,value));} public bool IsInfiniteLoop{get;set;} public int LoopInterval{get=>_interval;set=>SetProperty(ref _interval,Math.Max(0,value));}
    public ICommand StartRecordingCommand{get;} public ICommand StopRecordingCommand{get;} public ICommand PauseRecordingCommand{get;} public ICommand StartPlaybackCommand{get;} public ICommand StopPlaybackCommand{get;} public ICommand PausePlaybackCommand{get;} public ICommand ImportCommand{get;} public ICommand ExportCommand{get;} public ICommand HelpCommand{get;}
    public event Action<RecordingItemViewModel>? OptimizeRequested; public event Action<RecordingItemViewModel>? HotkeyRequested; public event Action<RecordingItemViewModel>? RenameRequested; public event Action<string>? RecordingDeleted;
    public RecordingPageViewModel(RecordingStorageService storage,GlobalHookService hook,PlaybackService playback){_storage=storage;_hook=hook;_playback=playback;
        foreach(var r in storage.LoadAll())Recordings.Add(new(r,this));
        StartRecordingCommand=new RelayCommand(_=>StartRecording(),_=>IsIdle&&!HasSelection);StopRecordingCommand=new RelayCommand(_=>StopRecording(),_=>IsRecording);PauseRecordingCommand=new RelayCommand(_=>ToggleRecordPause(),_=>IsRecording);
        StartPlaybackCommand=new RelayCommand(_=>_ = StartPlaybackAsync(),_=>IsIdle&&HasSelection);StopPlaybackCommand=new RelayCommand(_=>_playback.StopPlayback(),_=>IsPlaying);PausePlaybackCommand=new RelayCommand(_=>TogglePlayPause(),_=>IsPlaying);
        ImportCommand=new RelayCommand(_=>Import(),_=>IsIdle);ExportCommand=new RelayCommand(_=>Export(),_=>IsIdle&&HasSelection);HelpCommand=new RelayCommand(_=>ShowMessage(LocalizationService.Current.GetString("RecordingHelpText"),LocalizationService.Current.GetString("RecordingHelpTitle")));
        _timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};_timer.Tick+=(_,_)=>ElapsedTime=(DateTime.Now-_started).ToString(@"hh\:mm\:ss");
        _hook.OnActionRecorded+=c=>BeginOnUIThread(()=>ActionCount=c);_playback.OnActionExecuted+=c=>BeginOnUIThread(()=>ActionCount=c);_playback.OnPlaybackFinished+=()=>BeginOnUIThread(()=>{State=RecordingState.Idle;_timer.Stop();});
        LocalizationService.Current.LanguageChanged+=OnLanguageChanged;}
    public void Toggle(){if(IsRecording)StopRecording();else if(IsPlaying)_playback.StopPlayback();else if(HasSelection)_=StartPlaybackAsync();else StartRecording();}
    private void StartRecording(){ActionCount=0;ElapsedTime="00:00:00";_started=DateTime.Now;_hook.StartRecording();State=RecordingState.Recording;_timer.Start();}
    private void StopRecording(){var actions=_hook.StopRecording();_timer.Stop();State=RecordingState.Idle;if(actions.Count==0)return;var b=Win32Api.GetVirtualScreenBounds();var r=new Recording{Name=LocalizationService.Current.Format("RecordingDefaultName",DateTimeOffset.Now.ToUnixTimeSeconds()),DesktopBounds=new DesktopBounds{X=b.x,Y=b.y,Width=b.width,Height=b.height},KeyboardLayout=Win32Api.GetKeyboardLayout(0).ToInt64().ToString("X"),Actions=actions};Save(r);var item=new RecordingItemViewModel(r,this);Recordings.Add(item);SelectedRecording=item;if(actions.Count>=10000)ShowMessage(LocalizationService.Current.GetString("RecordingLargeMessage"),LocalizationService.Current.GetString("RecordingLargeTitle"));}
    private void ToggleRecordPause(){if(State==RecordingState.Recording){_hook.PauseRecording();State=RecordingState.RecordPaused;_timer.Stop();}else{_hook.ResumeRecording();State=RecordingState.Recording;_started=DateTime.Now-TimeSpan.Parse(ElapsedTime);_timer.Start();}}
    private async Task StartPlaybackAsync(){if(SelectedRecording==null)return;var b=Win32Api.GetVirtualScreenBounds();var old=SelectedRecording.Model.DesktopBounds;if(old.Width>0&&(old.X!=b.x||old.Y!=b.y||old.Width!=b.width||old.Height!=b.height)&&!ShowConfirm(LocalizationService.Current.GetString("RecordingDisplayChangedMessage"),LocalizationService.Current.GetString("RecordingDisplayChangedTitle")))return;ActionCount=0;_started=DateTime.Now;State=RecordingState.Playing;_timer.Start();try{await _playback.StartPlaybackAsync(SelectedRecording.Model,new PlaybackOptions{SpeedFactor=SpeedFactor,LoopCount=IsInfiniteLoop?-1:LoopCount,LoopInterval=LoopInterval});}catch(Exception ex){State=RecordingState.Idle;_timer.Stop();ShowMessage(LocalizationService.Current.Format("PlaybackError",ex.Message),LocalizationService.Current.GetString("PlaybackErrorTitle"),System.Windows.MessageBoxImage.Error);}}
    private void TogglePlayPause(){if(State==RecordingState.Playing){_playback.PausePlayback();State=RecordingState.PlayPaused;_timer.Stop();}else{_playback.ResumePlayback();State=RecordingState.Playing;_timer.Start();}}
    internal void Save(Recording r)=>_storage.Save(r); internal void Delete(RecordingItemViewModel i){if(ShowConfirm(LocalizationService.Current.Format("RecordingDeletePrompt",i.Name))){_storage.Delete(i.Id);RecordingDeleted?.Invoke(i.Id);Recordings.Remove(i);if(SelectedRecording==i)SelectedRecording=null;}}
    internal void RequestOptimize(RecordingItemViewModel i)=>OptimizeRequested?.Invoke(i); internal void RequestHotkey(RecordingItemViewModel i)=>HotkeyRequested?.Invoke(i); internal void RequestRename(RecordingItemViewModel i)=>RenameRequested?.Invoke(i);
    internal bool TryRename(RecordingItemViewModel item,string name,out string error)
    {
        var trimmed=name.Trim();
        if(trimmed.Length==0){error=LocalizationService.Current.GetString("RecordingRenameEmpty");return false;}
        var previous=item.Model.Name;
        try{item.Model.Name=trimmed;_storage.Save(item.Model);item.Refresh();error=string.Empty;return true;}
        catch(Exception ex){item.Model.Name=previous;item.Refresh();error=LocalizationService.Current.Format("RecordingRenameSaveFailed",ex.Message);return false;}
    }
    private void Import(){var d=new OpenFileDialog{Filter="JSON|*.json"};if(d.ShowDialog()!=true)return;try{var r=_storage.ImportFromFile(d.FileName);r.Hotkey=null;_storage.Save(r);var i=new RecordingItemViewModel(r,this);Recordings.Add(i);SelectedRecording=i;}catch(Exception e){ShowMessage(e.Message,LocalizationService.Current.GetString("RecordingImportErrorTitle"),System.Windows.MessageBoxImage.Error);}}
    private void Export(){if(SelectedRecording==null)return;var d=new SaveFileDialog{Filter="JSON|*.json",FileName=SelectedRecording.Name+".json"};if(d.ShowDialog()==true)_storage.ExportToFile(SelectedRecording.Id,d.FileName);}
    private void OnLanguageChanged(object? sender,EventArgs e){OnUIThread(()=>{foreach(var item in Recordings)item.Refresh();});}
    protected override void Dispose(bool disposing){if(disposing){LocalizationService.Current.LanguageChanged-=OnLanguageChanged;_timer.Stop();_hook.Dispose();_playback.Dispose();}base.Dispose(disposing);}
}
