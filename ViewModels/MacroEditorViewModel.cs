using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Ming_AutoClicker.Helpers;
using Ming_AutoClicker.Models;
using Ming_AutoClicker.Services;
using Ming_AutoClicker.Views.ImageMatching;
using Ming_AutoClicker.Views.Shared;

namespace Ming_AutoClicker.ViewModels
{
    /// <summary>
    /// 宏编辑器 ViewModel - 管理单个宏的编辑
    /// </summary>
    public class MacroEditorViewModel : ViewModelBase
    {
        private readonly MacroStorageService _storageService;
        private readonly ScreenCaptureService _screenCaptureService;
        private readonly ImageMatchService _imageMatchService;

        private MacroProfile _macro;
        private MacroAction? _selectedAction;
        private int _selectedActionIndex = -1;
        private string _statusMessage = string.Empty;
        private bool _isTestingMatch;
        private bool _isCapturingScreenshot;
        private bool _isPickingCoordinate;
        private CancellationTokenSource? _matchTestCancellation;
        private ImageTemplateVariant? _selectedAlternateTemplate;
        private bool _imageAdvancedExpanded;
        private readonly Dictionary<FindImageAction, ImageFields> _imageFields = new();
        private readonly Dictionary<MouseScrollAction, ScrollFields> _scrollFields = new();
        private NotifyCollectionChangedEventHandler? _collectionChangedHandler;

        /// <summary>
        /// 保存完成事件
        /// </summary>
        public event EventHandler? SaveCompleted;

        /// <summary>
        /// 取消完成事件
        /// </summary>
        public event EventHandler? CancelCompleted;

        #region 属性

        /// <summary>
        /// 正在编辑的宏
        /// </summary>
        public MacroProfile Macro
        {
            get => _macro;
            set
            {
                if (SetProperty(ref _macro, value))
                {
                    OnPropertyChanged(nameof(HasActions));
                    OnPropertyChanged(nameof(CanSave));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>
        /// 宏的动作列表
        /// </summary>
        public ObservableCollection<MacroAction> Actions => _macro.Actions;

        /// <summary>
        /// 当前选中的动作
        /// </summary>
        public MacroAction? SelectedAction
        {
            get => _selectedAction;
            set
            {
                if (SetProperty(ref _selectedAction, value))
                {
                    _selectedActionIndex = value != null ? Actions.IndexOf(value) : -1;
                    OnPropertyChanged(nameof(IsActionSelected));
                    OnPropertyChanged(nameof(SelectedActionType));
                    OnPropertyChanged(nameof(CanMoveUp));
                    OnPropertyChanged(nameof(CanMoveDown));
                    // 通知属性面板刷新所有动作相关属性
                    OnPropertyChanged(nameof(ActionNote));
                    OnPropertyChanged(nameof(ImagePath));
                    OnPropertyChanged(nameof(ScreenshotPreview));
                    OnPropertyChanged(nameof(HasScreenshot));
                    OnPropertyChanged(nameof(MatchThreshold));
                    OnPropertyChanged(nameof(WaitUntilFound));
                    OnPropertyChanged(nameof(AdaptiveScale));
                    _selectedAlternateTemplate = null;
                    RefreshImageProperties();
                    OnPropertyChanged(nameof(Operation));
                    OnPropertyChanged(nameof(OffsetX));
                    OnPropertyChanged(nameof(OffsetY));
                    OnPropertyChanged(nameof(WaitSeconds));
                    OnPropertyChanged(nameof(MouseClickAction));
                    OnPropertyChanged(nameof(ClickX));
                    OnPropertyChanged(nameof(ClickY));
                    OnPropertyChanged(nameof(ClickOperation));
                    OnPropertyChanged(nameof(ScrollDirectionValue));
                    OnPropertyChanged(nameof(ScrollPositionModeValue));
                    OnPropertyChanged(nameof(UsesScrollCoordinates));
                    OnPropertyChanged(nameof(ScrollNotchesText));
                    OnPropertyChanged(nameof(ScrollXText));
                    OnPropertyChanged(nameof(ScrollYText));
                    OnPropertyChanged(nameof(KeyVirtualKey));
                    OnPropertyChanged(nameof(KeyControl));
                    OnPropertyChanged(nameof(KeyAlt));
                    OnPropertyChanged(nameof(KeyShift));
                    OnPropertyChanged(nameof(KeyWindows));
                    OnPropertyChanged(nameof(KeyPressPreview));
                    UpdateActionCommands();
                }
            }
        }

        /// <summary>
        /// 是否选中了动作
        /// </summary>
        public bool IsActionSelected => SelectedAction != null;

        /// <summary>
        /// 选中动作的类型
        /// </summary>
        public string SelectedActionType => SelectedAction?.Type.ToString() ?? string.Empty;

        /// <summary>
        /// 宏名称
        /// </summary>
        public string MacroName
        {
            get => _macro.Name;
            set
            {
                if (_macro.Name != value)
                {
                    _macro.Name = value;
                    _macro.UpdatedAt = DateTime.Now;
                    OnPropertyChanged(nameof(MacroName));
                }
            }
        }

        /// <summary>
        /// 是否启用循环
        /// </summary>
        public bool LoopEnabled
        {
            get => _macro.LoopEnabled;
            set
            {
                if (_macro.LoopEnabled != value)
                {
                    _macro.LoopEnabled = value;
                    _macro.UpdatedAt = DateTime.Now;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 循环次数
        /// </summary>
        public int LoopCount
        {
            get => _macro.LoopCount;
            set
            {
                if (_macro.LoopCount != value)
                {
                    _macro.LoopCount = Math.Max(0, value);
                    _macro.UpdatedAt = DateTime.Now;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 循环间隔（毫秒)
        /// </summary>
        public int LoopIntervalMs
        {
            get => _macro.LoopIntervalMs;
            set
            {
                if (_macro.LoopIntervalMs != value)
                {
                    _macro.LoopIntervalMs = Math.Max(0, value);
                    _macro.UpdatedAt = DateTime.Now;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 状态消息
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>
        /// 是否有动作
        /// </summary>
        public bool HasActions => Actions.Count > 0;

        /// <summary>
        /// 是否可以保存
        /// </summary>
        public bool CanSave => !string.IsNullOrWhiteSpace(_macro.Name);

        /// <summary>
        /// 当前步骤的用户说明
        /// </summary>
        public string ActionNote
        {
            get => SelectedAction?.Note ?? string.Empty;
            set
            {
                if (SelectedAction != null && SelectedAction.Note != value)
                {
                    SelectedAction.Note = value ?? string.Empty;
                    _macro.UpdatedAt = DateTime.Now;
                    OnPropertyChanged();
                    CollectionViewSource.GetDefaultView(Actions).Refresh();
                }
            }
        }

        #endregion

        #region 找图动作属性

        /// <summary>
        /// 找图动作（如果选中的是找图动作)
        /// </summary>
        public FindImageAction? FindImageAction => SelectedAction as FindImageAction;

        /// <summary>
        /// 图像路径
        /// </summary>
        public string ImagePath
        {
            get => FindImageAction?.ImagePath ?? "";
            set
            {
                if (FindImageAction != null)
                {
                    FindImageAction.ImagePath = value;
                    FindImageAction.ClickPoint = new();
                    FindImageAction.IgnoredRegions = new();
                    FindImageAction.CaptureDpi = null;
                    _macro.UpdatedAt = DateTime.Now;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ScreenshotPreview));
                    OnPropertyChanged(nameof(HasScreenshot));
                    RefreshImageProperties();
                }
            }
        }

        /// <summary>
        /// 截图预览图像
        /// </summary>
        public BitmapImage? ScreenshotPreview
        {
            get
            {
                var path = ImagePath;
                if (string.IsNullOrEmpty(path)) return null;

                try
                {
                    // 将相对路径转换为绝对路径
                    if (!Path.IsPathRooted(path))
                    {
                        path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "screenshots", path);
                    }

                    if (!File.Exists(path)) return null;

                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(path, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad; // 立即加载，不锁定文件
                    bitmap.EndInit();
                    bitmap.Freeze(); // 冻结以便跨线程使用
                    return bitmap;
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// 是否有截图
        /// </summary>
        public bool HasScreenshot => !string.IsNullOrEmpty(ImagePath);

        public bool IsTestingMatch => _isTestingMatch;
        public bool ImageAdvancedExpanded { get => _imageAdvancedExpanded; set => SetProperty(ref _imageAdvancedExpanded, value); }
        public bool UsesLegacyImageOffset => FindImageAction is { ClickPoint: null };
        public ImageClickPoint? PreviewClickPoint
        {
            get
            {
                if (FindImageAction is not { } action) return null;
                if (action.ClickPoint != null) return action.ClickPoint;
                var source = ScreenshotPreview;
                return source == null ? new() : new ImageClickPoint
                { X = 0.5 + (double)action.OffsetX / source.PixelWidth, Y = 0.5 + (double)action.OffsetY / source.PixelHeight };
            }
        }
        public IEnumerable<ImageRelativeRegion> ImageIgnoredRegions => FindImageAction?.IgnoredRegions ?? Enumerable.Empty<ImageRelativeRegion>();
        public IEnumerable<ImageTemplateVariant> AlternateTemplates => FindImageAction?.AlternateTemplates.ToArray() ?? Array.Empty<ImageTemplateVariant>();
        public ImageTemplateVariant? SelectedAlternateTemplate
        {
            get => _selectedAlternateTemplate;
            set { SetProperty(ref _selectedAlternateTemplate, value); CommandManager.InvalidateRequerySuggested(); }
        }

        public string ImageTimeoutSecondsText
        {
            get => FindImageAction is { } action ? GetImageFields(action).Timeout : "30";
            set
            {
                if (FindImageAction is not { } action) return;
                GetImageFields(action).Timeout = value;
                if (TryParseImageTimeout(value, out var milliseconds)) action.TimeoutMs = milliseconds;
                RefreshEditedAction(nameof(ImageTimeoutSecondsText));
            }
        }

        public string ImageMatchIndexText
        {
            get => FindImageAction is { } action ? GetImageFields(action).Index : "1";
            set
            {
                if (FindImageAction is not { } action) return;
                GetImageFields(action).Index = value;
                if (int.TryParse(value, out var index) && index > 0) action.MatchIndex = index;
                RefreshEditedAction(nameof(ImageMatchIndexText));
            }
        }

        public ImageSearchScopeKind ImageScopeKind
        {
            get => FindImageAction?.SearchScope.Kind ?? ImageSearchScopeKind.Desktop;
            set
            {
                if (FindImageAction is not { } action || action.SearchScope.Kind == value) return;
                action.SearchScope = new ImageSearchScope { Kind = value };
                RefreshEditedAction(nameof(ImageScopeKind));
                RefreshImageProperties();
            }
        }
        public bool UsesImageRegion => ImageScopeKind == ImageSearchScopeKind.Region;
        public bool UsesImageWindow => ImageScopeKind == ImageSearchScopeKind.Window;
        public bool HasWindowRegion => FindImageAction?.SearchScope.WindowRegion != null;
        public string ImageScopeDescription
        {
            get
            {
                if (FindImageAction is not { } action) return string.Empty;
                var scope = action.SearchScope;
                return scope.Kind switch
                {
                    ImageSearchScopeKind.Region when scope.Width > 0 && scope.Height > 0 =>
                        $"({scope.X}, {scope.Y}) · {scope.Width} × {scope.Height}",
                    ImageSearchScopeKind.Window when !string.IsNullOrWhiteSpace(scope.WindowTitle) =>
                        $"{scope.WindowTitle} ({scope.WindowProcessName})" + (scope.WindowRegion == null ? "" : " · " + LocalizationService.Current.GetString("ImageWindowRegionSet")),
                    ImageSearchScopeKind.Desktop => LocalizationService.Current.GetString("ImageScopeDesktopHint"),
                    _ => LocalizationService.Current.GetString("ImageScopeNotSelected")
                };
            }
        }
        public ImageSelectionMode ImageSelection
        {
            get => FindImageAction?.SelectionMode ?? ImageSelectionMode.Unique;
            set
            {
                if (FindImageAction is not { } action) return;
                action.SelectionMode = value;
                RefreshEditedAction(nameof(ImageSelection));
                OnPropertyChanged(nameof(UsesImageMatchIndex));
            }
        }
        public bool UsesImageMatchIndex => ImageSelection == ImageSelectionMode.Ordered;

        public void SetImageClickPoint(ImageClickPoint point)
        {
            if (FindImageAction is not { } action || !double.IsFinite(point.X) || !double.IsFinite(point.Y)) return;
            action.ClickPoint = new() { X = Math.Clamp(point.X, 0, 1), Y = Math.Clamp(point.Y, 0, 1) };
            RefreshEditedAction(nameof(PreviewClickPoint));
            OnPropertyChanged(nameof(UsesLegacyImageOffset));
        }

        private void RefreshImageProperties()
        {
            var selectedAlternate = _selectedAlternateTemplate;
            foreach (var name in new[] { nameof(PreviewClickPoint), nameof(ImageIgnoredRegions), nameof(UsesLegacyImageOffset),
                nameof(ImageTimeoutSecondsText), nameof(ImageMatchIndexText), nameof(ImageScopeKind), nameof(UsesImageRegion),
                nameof(UsesImageWindow), nameof(HasWindowRegion), nameof(ImageScopeDescription), nameof(ImageSelection),
                nameof(UsesImageMatchIndex), nameof(AlternateTemplates) })
                OnPropertyChanged(name);
            _selectedAlternateTemplate = selectedAlternate != null && FindImageAction?.AlternateTemplates.Contains(selectedAlternate) == true
                ? selectedAlternate : null;
            OnPropertyChanged(nameof(SelectedAlternateTemplate));
            CommandManager.InvalidateRequerySuggested();
        }

        private ImageFields GetImageFields(FindImageAction action)
        {
            if (!_imageFields.TryGetValue(action, out var fields))
                _imageFields[action] = fields = new() { Timeout = (action.GetTimeoutMs() / 1000d).ToString(), Index = action.MatchIndex.ToString() };
            return fields;
        }
        private static bool TryParseImageTimeout(string text, out int milliseconds)
        {
            milliseconds = 0;
            if (!double.TryParse(text, out var seconds) || !double.IsFinite(seconds) || seconds < 0 || seconds > int.MaxValue / 1000d) return false;
            milliseconds = (int)Math.Round(seconds * 1000);
            return seconds == 0 || milliseconds > 0;
        }
        private bool TryValidateImageFields(FindImageAction action, out string error)
        {
            error = string.Empty;
            if (!_imageFields.TryGetValue(action, out var fields)) return true;
            if (!TryParseImageTimeout(fields.Timeout, out _))
                error = LocalizationService.Current.GetString("ImageInvalidTimeout");
            else if (action.SelectionMode == ImageSelectionMode.Ordered && (!int.TryParse(fields.Index, out var index) || index < 1))
                error = LocalizationService.Current.GetString("ImageInvalidMatchIndex");
            return error.Length == 0;
        }
        private sealed class ImageFields
        {
            public string Timeout { get; set; } = "30";
            public string Index { get; set; } = "1";
        }

        /// <summary>
        /// 匹配阈值
        /// </summary>
        public double MatchThreshold
        {
            get => FindImageAction?.MatchThreshold ?? 0.8;
            set
            {
                if (FindImageAction != null)
                {
                    FindImageAction.MatchThreshold = value;
                    _macro.UpdatedAt = DateTime.Now;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 是否等待直到找到
        /// </summary>
        public bool WaitUntilFound
        {
            get => FindImageAction?.WaitUntilFound ?? false;
            set
            {
                if (FindImageAction != null)
                {
                    FindImageAction.WaitUntilFound = value;
                    _macro.UpdatedAt = DateTime.Now;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 是否为当前找图动作启用宽范围缩放搜索
        /// </summary>
        public bool AdaptiveScale
        {
            get => FindImageAction?.AdaptiveScale ?? false;
            set
            {
                if (FindImageAction != null && FindImageAction.AdaptiveScale != value)
                {
                    FindImageAction.AdaptiveScale = value;
                    _macro.UpdatedAt = DateTime.Now;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 操作类型
        /// </summary>
        public string Operation
        {
            get => FindImageAction?.Operation ?? "Click";
            set
            {
                if (FindImageAction != null)
                {
                    FindImageAction.Operation = value;
                    _macro.UpdatedAt = DateTime.Now;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// X 偏移
        /// </summary>
        public int OffsetX
        {
            get => FindImageAction?.OffsetX ?? 0;
            set
            {
                if (FindImageAction != null)
                {
                    FindImageAction.OffsetX = value;
                    _macro.UpdatedAt = DateTime.Now;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PreviewClickPoint));
                }
            }
        }

        /// <summary>
        /// Y 偏移
        /// </summary>
        public int OffsetY
        {
            get => FindImageAction?.OffsetY ?? 0;
            set
            {
                if (FindImageAction != null)
                {
                    FindImageAction.OffsetY = value;
                    _macro.UpdatedAt = DateTime.Now;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PreviewClickPoint));
                }
            }
        }

        #endregion

        #region 等待动作属性

        /// <summary>
        /// 等待动作(如果选中的是等待动作)
        /// </summary>
        public WaitAction? WaitAction => SelectedAction as WaitAction;

        /// <summary>
        /// 等待秒数
        /// </summary>
        public double WaitSeconds
        {
            get => WaitAction?.WaitSeconds ?? 1.0;
            set
            {
                if (WaitAction != null)
                {
                    WaitAction.WaitSeconds = value;
                    _macro.UpdatedAt = DateTime.Now;
                    OnPropertyChanged();
                }
            }
        }

        #endregion

        #region 鼠标点击位置动作属性

        /// <summary>
        /// 鼠标点击位置动作(如果选中的是鼠标点击位置动作)
        /// </summary>
        public MouseClickAction? MouseClickAction => SelectedAction as MouseClickAction;

        /// <summary>
        /// 点击 X 坐标
        /// </summary>
        public int ClickX
        {
            get => MouseClickAction?.X ?? 0;
            set
            {
                if (MouseClickAction != null)
                {
                    MouseClickAction.X = value;
                    _macro.UpdatedAt = DateTime.Now;
                    OnPropertyChanged();
                    CollectionViewSource.GetDefaultView(Actions).Refresh();
                }
            }
        }

        /// <summary>
        /// 点击 Y 坐标
        /// </summary>
        public int ClickY
        {
            get => MouseClickAction?.Y ?? 0;
            set
            {
                if (MouseClickAction != null)
                {
                    MouseClickAction.Y = value;
                    _macro.UpdatedAt = DateTime.Now;
                    OnPropertyChanged();
                    CollectionViewSource.GetDefaultView(Actions).Refresh();
                }
            }
        }

        /// <summary>
        /// 点击操作类型
        /// </summary>
        public string ClickOperation
        {
            get => MouseClickAction?.Operation ?? "Click";
            set
            {
                if (MouseClickAction != null)
                {
                    MouseClickAction.Operation = value;
                    _macro.UpdatedAt = DateTime.Now;
                    OnPropertyChanged();
                    CollectionViewSource.GetDefaultView(Actions).Refresh();
                }
            }
        }

        #endregion

        #region 滚动与键盘动作属性

        private MouseScrollAction? ScrollAction => SelectedAction as MouseScrollAction;
        private KeyPressAction? KeyboardAction => SelectedAction as KeyPressAction;

        public ScrollDirection ScrollDirectionValue
        {
            get => ScrollAction?.Direction ?? ScrollDirection.Down;
            set
            {
                if (ScrollAction == null) return;
                ScrollAction.Direction = value;
                RefreshEditedAction(nameof(ScrollDirectionValue));
            }
        }

        public ScrollPositionMode ScrollPositionModeValue
        {
            get => ScrollAction?.PositionMode ?? ScrollPositionMode.CurrentCursor;
            set
            {
                if (ScrollAction == null) return;
                ScrollAction.PositionMode = value;
                RefreshEditedAction(nameof(ScrollPositionModeValue));
                OnPropertyChanged(nameof(UsesScrollCoordinates));
                UpdateActionCommands();
            }
        }

        public bool UsesScrollCoordinates => ScrollAction?.PositionMode == ScrollPositionMode.Coordinates;

        // Preserve text drafts so invalid input cannot silently save the previous numeric value.
        public string ScrollNotchesText
        {
            get => ScrollAction is { } action ? GetScrollFields(action).Notches : "1";
            set
            {
                if (ScrollAction is not { } action) return;
                GetScrollFields(action).Notches = value;
                if (int.TryParse(value, out var number)) action.Notches = number;
                RefreshEditedAction(nameof(ScrollNotchesText));
            }
        }

        public string ScrollXText
        {
            get => ScrollAction is { } action ? GetScrollFields(action).X : "0";
            set
            {
                if (ScrollAction is not { } action) return;
                GetScrollFields(action).X = value;
                if (int.TryParse(value, out var number)) action.X = number;
                RefreshEditedAction(nameof(ScrollXText));
            }
        }

        public string ScrollYText
        {
            get => ScrollAction is { } action ? GetScrollFields(action).Y : "0";
            set
            {
                if (ScrollAction is not { } action) return;
                GetScrollFields(action).Y = value;
                if (int.TryParse(value, out var number)) action.Y = number;
                RefreshEditedAction(nameof(ScrollYText));
            }
        }

        public System.Collections.IEnumerable KeyboardKeys => MacroKeyCatalog.Keys;

        public string KeyPressPreview => KeyboardAction is { } action
            ? LocalizationService.Current.Format("EditorKeyboardPreview",
                MacroKeyCatalog.Format(action.VirtualKey, action.Modifiers))
            : string.Empty;

        public uint KeyVirtualKey
        {
            get => KeyboardAction?.VirtualKey ?? 0x0D;
            set
            {
                if (KeyboardAction == null) return;
                KeyboardAction.VirtualKey = value;
                RefreshEditedAction(nameof(KeyVirtualKey));
                OnPropertyChanged(nameof(KeyPressPreview));
            }
        }

        public bool KeyControl
        {
            get => HasKeyModifier(KeyModifiers.Control);
            set => SetKeyModifier(KeyModifiers.Control, value, nameof(KeyControl));
        }

        public bool KeyAlt
        {
            get => HasKeyModifier(KeyModifiers.Alt);
            set => SetKeyModifier(KeyModifiers.Alt, value, nameof(KeyAlt));
        }

        public bool KeyShift
        {
            get => HasKeyModifier(KeyModifiers.Shift);
            set => SetKeyModifier(KeyModifiers.Shift, value, nameof(KeyShift));
        }

        public bool KeyWindows
        {
            get => HasKeyModifier(KeyModifiers.Windows);
            set => SetKeyModifier(KeyModifiers.Windows, value, nameof(KeyWindows));
        }

        private bool HasKeyModifier(KeyModifiers modifier) => KeyboardAction?.Modifiers.HasFlag(modifier) == true;

        private void SetKeyModifier(KeyModifiers modifier, bool enabled, string propertyName)
        {
            if (KeyboardAction == null) return;
            KeyboardAction.Modifiers = enabled
                ? KeyboardAction.Modifiers | modifier
                : KeyboardAction.Modifiers & ~modifier;
            RefreshEditedAction(propertyName);
            OnPropertyChanged(nameof(KeyPressPreview));
        }

        private void RefreshEditedAction(string propertyName)
        {
            _macro.UpdatedAt = DateTime.Now;
            OnPropertyChanged(propertyName);
            CollectionViewSource.GetDefaultView(Actions).Refresh();
        }

        private ScrollFields GetScrollFields(MouseScrollAction action)
        {
            if (!_scrollFields.TryGetValue(action, out var fields))
            {
                fields = new ScrollFields
                {
                    Notches = action.Notches.ToString(),
                    X = action.X.ToString(),
                    Y = action.Y.ToString()
                };
                _scrollFields.Add(action, fields);
            }
            return fields;
        }

        private bool TryValidateScrollFields(MouseScrollAction action, out string error)
        {
            error = string.Empty;
            if (!_scrollFields.TryGetValue(action, out var fields)) return true;
            if (!int.TryParse(fields.Notches, out var notches) || notches <= 0 ||
                notches > MacroActionValidator.MaximumScrollNotches)
            {
                error = LocalizationService.Current.GetString("EditorScrollInvalidNotches");
                return false;
            }
            if (action.PositionMode == ScrollPositionMode.Coordinates &&
                (!int.TryParse(fields.X, out _) || !int.TryParse(fields.Y, out _)))
            {
                error = LocalizationService.Current.GetString("EditorScrollInvalidCoordinates");
                return false;
            }
            return true;
        }

        private sealed class ScrollFields
        {
            public string Notches { get; set; } = "1";
            public string X { get; set; } = "0";
            public string Y { get; set; } = "0";
        }

        #endregion

        #region 命令

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand AddFindImageActionCommand { get; }
        public ICommand AddWaitActionCommand { get; }
        public ICommand AddMouseClickActionCommand { get; }
        public ICommand AddMouseScrollActionCommand { get; }
        public ICommand AddKeyPressActionCommand { get; }
        public ICommand RemoveActionCommand { get; }
        public ICommand MoveUpCommand { get; }
        public ICommand MoveDownCommand { get; }
        public ICommand CaptureScreenshotCommand { get; }
        public ICommand ImportImageCommand { get; }
        public ICommand TestMatchCommand { get; }
        public ICommand EditImageTemplateCommand { get; }
        public ICommand ResetImageClickPointCommand { get; }
        public ICommand PickImageRegionCommand { get; }
        public ICommand PickImageWindowCommand { get; }
        public ICommand PickImageWindowRegionCommand { get; }
        public ICommand ClearImageWindowRegionCommand { get; }
        public ICommand AddAlternateTemplateCommand { get; }
        public ICommand CaptureAlternateTemplateCommand { get; }
        public ICommand EditAlternateTemplateCommand { get; }
        public ICommand RemoveAlternateTemplateCommand { get; }
        public ICommand ClearImageCommand { get; }
        public ICommand PickCoordinateCommand { get; }

        #endregion

        public MacroEditorViewModel(
            MacroStorageService storageService,
            ScreenCaptureService screenCaptureService,
            ImageMatchService imageMatchService,
            MacroProfile macro)
        {
            _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
            _screenCaptureService = screenCaptureService ?? throw new ArgumentNullException(nameof(screenCaptureService));
            _imageMatchService = imageMatchService ?? throw new ArgumentNullException(nameof(imageMatchService));
            _macro = macro ?? throw new ArgumentNullException(nameof(macro));
            _statusMessage = LocalizationService.Current.GetString("StatusReady");

            // 初始化命令
            SaveCommand = new RelayCommand(_ => Save());
            CancelCommand = new RelayCommand(_ => Cancel());
            AddFindImageActionCommand = new RelayCommand(_ => AddFindImageAction());
            AddWaitActionCommand = new RelayCommand(_ => AddWaitAction());
            AddMouseClickActionCommand = new RelayCommand(_ => AddMouseClickAction());
            AddMouseScrollActionCommand = new RelayCommand(_ => AddMouseScrollAction());
            AddKeyPressActionCommand = new RelayCommand(_ => AddKeyPressAction());
            RemoveActionCommand = new RelayCommand(_ => RemoveAction(), _ => CanRemoveAction());
            MoveUpCommand = new RelayCommand(_ => MoveUp(), _ => CanMoveUp());
            MoveDownCommand = new RelayCommand(_ => MoveDown(), _ => CanMoveDown());
            CaptureScreenshotCommand = new RelayCommand(
                _ => _ = CaptureScreenshotAsync(),
                _ => FindImageAction != null && !_isCapturingScreenshot);
            ImportImageCommand = new RelayCommand(_ => ImportImage(), _ => FindImageAction != null);
            TestMatchCommand = new RelayCommand(_ => _ = TestMatchAsync(), _ => FindImageAction != null && !_isTestingMatch);
            EditImageTemplateCommand = new RelayCommand(_ => EditTemplate(false), _ => HasScreenshot && !_isTestingMatch);
            ResetImageClickPointCommand = new RelayCommand(_ => SetImageClickPoint(new()), _ => HasScreenshot);
            PickImageRegionCommand = new RelayCommand(_ => _ = PickImageScopeRegionAsync(false), _ => UsesImageRegion && !_isCapturingScreenshot);
            PickImageWindowCommand = new RelayCommand(_ => PickImageWindow(), _ => UsesImageWindow && !_isTestingMatch);
            PickImageWindowRegionCommand = new RelayCommand(_ => _ = PickImageScopeRegionAsync(true), _ => UsesImageWindow && !_isCapturingScreenshot);
            ClearImageWindowRegionCommand = new RelayCommand(_ =>
            {
                if (FindImageAction is not { } action) return;
                action.SearchScope.WindowRegion = null;
                RefreshEditedAction(nameof(ImageScopeDescription)); RefreshImageProperties();
            }, _ => HasWindowRegion);
            AddAlternateTemplateCommand = new RelayCommand(_ => ImportAlternateTemplate(), _ => FindImageAction != null);
            CaptureAlternateTemplateCommand = new RelayCommand(_ => _ = CaptureScreenshotAsync(true), _ => FindImageAction != null && !_isCapturingScreenshot);
            EditAlternateTemplateCommand = new RelayCommand(_ => EditTemplate(true), _ => SelectedAlternateTemplate != null);
            RemoveAlternateTemplateCommand = new RelayCommand(_ =>
            {
                if (FindImageAction is not { } action || SelectedAlternateTemplate == null) return;
                action.AlternateTemplates.Remove(SelectedAlternateTemplate); SelectedAlternateTemplate = null;
                RefreshEditedAction(nameof(FindImageAction)); RefreshImageProperties();
            }, _ => SelectedAlternateTemplate != null);
            ClearImageCommand = new RelayCommand(_ => ClearImage(), _ => IsActionSelected);
            PickCoordinateCommand = new RelayCommand(_ => _ = PickCoordinateAsync(),
                _ => !_isPickingCoordinate && (MouseClickAction != null || UsesScrollCoordinates));

            // 订阅动作集合变更事件
            _collectionChangedHandler = (s, e) =>
            {
                if (e.NewItems != null && e.NewItems.Count > 0)
                {
                    // 新增动作，设置 Order
                    foreach (MacroAction action in e.NewItems)
                    {
                        action.Order = Actions.Count - 1;
                    }
                }
                OnPropertyChanged(nameof(HasActions));
            };
            Actions.CollectionChanged += _collectionChangedHandler;
            LocalizationService.Current.LanguageChanged += OnLanguageChanged;
        }

        #region 命令实现

        private void Save()
        {
            try
            {
                for (int i = 0; i < Actions.Count; i++)
                {
                    var action = Actions[i];
                    string error;
                    if ((action is MouseScrollAction scroll && !TryValidateScrollFields(scroll, out error)) ||
                        (action is FindImageAction image && !TryValidateImageFields(image, out error)) ||
                        !MacroActionValidator.TryValidate(action, out error))
                    {
                        SelectedAction = action;
                        StatusMessage = LocalizationService.Current.Format("EditorInvalidAction", i + 1, error);
                        return;
                    }
                }

                _macro.UpdatedAt = DateTime.Now;

                // 重新排序动作
                for (int i = 0; i < Actions.Count; i++)
                {
                    Actions[i].Order = i;
                }

                // 不在这里保存，由 MainWindow.OnEditorRequestSave 统一处理
                StatusMessage = LocalizationService.Current.GetString("EditorSaveSuccess");
                SaveCompleted?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                StatusMessage = LocalizationService.Current.Format("StatusSaveFailed", ex.Message);
            }
        }

        private void Cancel()
        {
            // 重新加载原始数据
            var original = _storageService.LoadMacros().FirstOrDefault(m => m.Id == _macro.Id);
            if (original != null)
            {
                // 使用属性 setter 以触发 UI 通知
                MacroName = original.Name;
                LoopEnabled = original.LoopEnabled;
                LoopCount = original.LoopCount;
                LoopIntervalMs = original.LoopIntervalMs;

                Actions.Clear();
                _scrollFields.Clear();
                foreach (var action in original.Actions)
                {
                    Actions.Add(action);
                }
                _macro.UpdatedAt = original.UpdatedAt;

                // 重置选中动作
                SelectedAction = null;
            }
            StatusMessage = LocalizationService.Current.GetString("EditorCancelled");
            CancelCompleted?.Invoke(this, EventArgs.Empty);
        }

        private void AddFindImageAction()
        {
            var action = new FindImageAction
            {
                Order = Actions.Count,
                ImagePath = "",
                MatchThreshold = 0.8,
                TimeoutMs = 30000,
                ClickPoint = new(),
                Operation = "Click"
            };
            Actions.Add(action);
            SelectedAction = action;
            StatusMessage = LocalizationService.Current.GetString("EditorAddedFind");
        }

        private void AddWaitAction()
        {
            var action = new WaitAction
            {
                Order = Actions.Count,
                WaitSeconds = 1.0
            };
            Actions.Add(action);
            SelectedAction = action;
            StatusMessage = LocalizationService.Current.GetString("EditorAddedWait");
        }

        private void AddMouseClickAction()
        {
            var action = new MouseClickAction
            {
                Order = Actions.Count,
                X = 0,
                Y = 0,
                Operation = "Click"
            };
            Actions.Add(action);
            SelectedAction = action;
            StatusMessage = LocalizationService.Current.GetString("EditorAddedClick");
        }

        private void AddMouseScrollAction()
        {
            var action = new MouseScrollAction { Order = Actions.Count };
            Actions.Add(action);
            SelectedAction = action;
            StatusMessage = LocalizationService.Current.GetString("EditorAddedScroll");
        }

        private void AddKeyPressAction()
        {
            var action = new KeyPressAction { Order = Actions.Count };
            Actions.Add(action);
            SelectedAction = action;
            StatusMessage = LocalizationService.Current.GetString("EditorAddedKeyboard");
        }

        /// <summary>
        /// 为开始拾取时的动作获取屏幕坐标，避免异步切换选中项后写入错误动作。
        /// </summary>
        private async System.Threading.Tasks.Task PickCoordinateAsync()
        {
            var targetAction = SelectedAction;
            if (_isPickingCoordinate || targetAction is not (Models.MouseClickAction or MouseScrollAction)) return;
            _isPickingCoordinate = true;
            UpdateActionCommands();

            var mainWindow = Application.Current.MainWindow;
            var previousWindowState = mainWindow?.WindowState ?? WindowState.Normal;
            try
            {
                // 最小化主窗口
                if (mainWindow != null) mainWindow.WindowState = WindowState.Minimized;

                // 等待窗口最小化完成
                await System.Threading.Tasks.Task.Delay(200);

                // 打开坐标拾取窗口
                var pickWindow = new CoordinatePickWindow();
                pickWindow.CoordinatePicked += (x, y) =>
                {
                    if (!Actions.Contains(targetAction)) return;
                    if (targetAction is MouseClickAction click)
                    {
                        click.X = x;
                        click.Y = y;
                    }
                    else if (targetAction is MouseScrollAction scroll)
                    {
                        scroll.X = x;
                        scroll.Y = y;
                        var fields = GetScrollFields(scroll);
                        fields.X = x.ToString();
                        fields.Y = y.ToString();
                    }
                    _macro.UpdatedAt = DateTime.Now;
                    OnPropertyChanged(nameof(ClickX));
                    OnPropertyChanged(nameof(ClickY));
                    OnPropertyChanged(nameof(ScrollXText));
                    OnPropertyChanged(nameof(ScrollYText));
                    StatusMessage = LocalizationService.Current.Format("EditorPickedCoordinate", x, y);
                    CollectionViewSource.GetDefaultView(Actions).Refresh();
                };
                pickWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                StatusMessage = LocalizationService.Current.Format("EditorPickFailed", ex.Message);
            }
            finally
            {
                if (mainWindow != null) mainWindow.WindowState = previousWindowState;
                _isPickingCoordinate = false;
                UpdateActionCommands();
            }
        }

        private bool CanRemoveAction() => IsActionSelected && Actions.Count > 0;

        private void RemoveAction()
        {
            if (SelectedAction == null) return;

            var index = _selectedActionIndex;
            if (SelectedAction is MouseScrollAction scroll) _scrollFields.Remove(scroll);
            Actions.Remove(SelectedAction);
            
            // 重新排序
            for (int i = 0; i < Actions.Count; i++)
            {
                Actions[i].Order = i;
            }

            // 选择下一个动作
            if (Actions.Count > 0)
            {
                SelectedAction = Actions[Math.Min(index, Actions.Count - 1)];
            }
            else
            {
                SelectedAction = null;
            }

            StatusMessage = LocalizationService.Current.GetString("EditorRemovedAction");
        }

        private bool CanMoveUp() => IsActionSelected && _selectedActionIndex > 0;

        private void MoveUp()
        {
            if (SelectedAction == null || _selectedActionIndex <= 0) return;

            var index = _selectedActionIndex;
            var temp = Actions[index];
            Actions[index] = Actions[index - 1];
            Actions[index - 1] = temp;

            // 更新 Order
            Actions[index].Order = index;
            Actions[index - 1].Order = index - 1;

            SelectedAction = Actions[index - 1];
            StatusMessage = LocalizationService.Current.GetString("EditorMovedUp");
        }

        private bool CanMoveDown() => IsActionSelected && _selectedActionIndex < Actions.Count - 1;

        private void MoveDown()
        {
            if (SelectedAction == null || _selectedActionIndex >= Actions.Count - 1) return;

            var index = _selectedActionIndex;
            var temp = Actions[index];
            Actions[index] = Actions[index + 1];
            Actions[index + 1] = temp;

            // 更新 Order
            Actions[index].Order = index;
            Actions[index + 1].Order = index + 1;

            SelectedAction = Actions[index + 1];
            StatusMessage = LocalizationService.Current.GetString("EditorMovedDown");
        }

        private async System.Threading.Tasks.Task CaptureScreenshotAsync(bool alternate = false)
        {
            if (_isCapturingScreenshot || FindImageAction is not { } action) return;
            _isCapturingScreenshot = true;
            CommandManager.InvalidateRequerySuggested();

            var mainWindow = Application.Current.MainWindow;
            var previousWindowState = mainWindow?.WindowState ?? WindowState.Normal;
            try
            {
                // 最小化主窗口
                if (mainWindow != null) mainWindow.WindowState = WindowState.Minimized;

                // 等待窗口最小化完成
                await System.Threading.Tasks.Task.Delay(200);

                // 打开区域选择窗口
                var selectWindow = new RegionSelectWindow();
                selectWindow.SelectionCompleted += (region) =>
                {
                    if (region.HasValue)
                    {
                        try
                        {
                            var r = region.Value;

                            // 优先使用窗口已裁剪的截图（从原始屏幕截图裁剪，不含覆盖层）
                            // 回退到重新截屏（兼容旧调用方式）
                            string filePath;
                            if (selectWindow.CroppedScreenshot != null)
                            {
                                filePath = _screenCaptureService.SaveBitmap(selectWindow.CroppedScreenshot);
                            }
                            else
                            {
                                filePath = _screenCaptureService.CaptureRegionAndSave(
                                    r.X, r.Y, r.Width, r.Height);
                            }

                            if (ReferenceEquals(action, FindImageAction))
                            {
                                var relativePath = _screenCaptureService.GetRelativePath(filePath);
                                var dpi = ImageSearchScopeService.GetDpiAtPoint(r.X + r.Width / 2, r.Y + r.Height / 2);
                                if (alternate)
                                {
                                    var variant = new ImageTemplateVariant { ImagePath = relativePath, CaptureDpi = dpi, ClickPoint = new() };
                                    action.AlternateTemplates.Add(variant);
                                    SelectedAlternateTemplate = variant;
                                    RefreshEditedAction(nameof(FindImageAction));
                                }
                                else
                                {
                                    ImagePath = relativePath;
                                    action.CaptureDpi = dpi;
                                }
                                RefreshImageProperties();
                                StatusMessage = LocalizationService.Current.Format(
                                    "EditorCaptured", relativePath, r.Width, r.Height);
                            }
                        }
                        catch (Exception ex)
                        {
                            StatusMessage = LocalizationService.Current.Format("EditorCaptureSaveFailed", ex.Message);
                        }
                    }
                    else
                    {
                        StatusMessage = LocalizationService.Current.GetString("EditorCaptureCancelled");
                    }

                };

                selectWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                StatusMessage = LocalizationService.Current.Format("EditorCaptureFailed", ex.Message);
            }
            finally
            {
                if (mainWindow != null) mainWindow.WindowState = previousWindowState;
                _isCapturingScreenshot = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void ImportImage()
        {
            if (FindImageAction == null) return;

            var dialog = new OpenFileDialog
            {
                Title = LocalizationService.Current.GetString("OpenImageTitle"),
                Filter = LocalizationService.Current.GetString("OpenImageFilter"),
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog(Application.Current.MainWindow) != true) return;

            try
            {
                var importedPath = _screenCaptureService.ImportImage(dialog.FileName);
                ImagePath = _screenCaptureService.GetRelativePath(importedPath);
                StatusMessage = LocalizationService.Current.Format("EditorImported", Path.GetFileName(dialog.FileName));
            }
            catch (Exception ex)
            {
                var message = LocalizationService.Current.Format("EditorImportFailed", ex.Message);
                ShowMessage(message, icon: MessageBoxImage.Error);
                StatusMessage = message;
            }
        }

        private void EditTemplate(bool alternate)
        {
            if (FindImageAction is not { } action) return;
            var variant = alternate ? SelectedAlternateTemplate : new ImageTemplateVariant
            {
                ImagePath = action.ImagePath, ClickPoint = action.ClickPoint,
                IgnoredRegions = action.IgnoredRegions, CaptureDpi = action.CaptureDpi
            };
            if (variant == null) return;
            try
            {
                var editor = new TemplateEditWindow(variant, _screenCaptureService.GetScreenshotDirectory(),
                    alternate ? null : PreviewClickPoint) { Owner = Application.Current.MainWindow };
                if (editor.ShowDialog() != true) return;
                if (alternate)
                {
                    var index = action.AlternateTemplates.IndexOf(variant);
                    if (index < 0) return;
                    action.AlternateTemplates[index] = editor.EditedTemplate;
                    SelectedAlternateTemplate = editor.EditedTemplate;
                }
                else
                {
                    action.ClickPoint = editor.EditedTemplate.ClickPoint;
                    action.IgnoredRegions = editor.EditedTemplate.IgnoredRegions;
                }
                RefreshEditedAction(nameof(PreviewClickPoint));
                RefreshImageProperties();
            }
            catch (Exception ex) { StatusMessage = LocalizationService.Current.Format("EditorImportFailed", ex.Message); }
        }

        private void ImportAlternateTemplate()
        {
            if (FindImageAction is not { } action) return;
            var dialog = new OpenFileDialog { Title = LocalizationService.Current.GetString("OpenImageTitle"),
                Filter = LocalizationService.Current.GetString("OpenImageFilter"), CheckFileExists = true, Multiselect = false };
            if (dialog.ShowDialog(Application.Current.MainWindow) != true) return;
            try
            {
                var path = _screenCaptureService.ImportImage(dialog.FileName);
                var variant = new ImageTemplateVariant { ImagePath = _screenCaptureService.GetRelativePath(path), ClickPoint = new() };
                action.AlternateTemplates.Add(variant); SelectedAlternateTemplate = variant;
                RefreshEditedAction(nameof(FindImageAction)); RefreshImageProperties();
            }
            catch (Exception ex) { StatusMessage = LocalizationService.Current.Format("EditorImportFailed", ex.Message); }
        }

        private void PickImageWindow()
        {
            if (FindImageAction is not { } action) return;
            try
            {
                var dialog = new ImageWindowSelectWindow { Owner = Application.Current.MainWindow };
                if (dialog.ShowDialog() != true || dialog.SelectedWindow == null) return;
                action.SearchScope = dialog.SelectedWindow.ToScope();
                RefreshEditedAction(nameof(ImageScopeDescription)); RefreshImageProperties();
            }
            catch (Exception ex) { StatusMessage = ex.Message; }
        }

        private async Task PickImageScopeRegionAsync(bool windowRelative)
        {
            if (_isCapturingScreenshot || FindImageAction is not { } action) return;
            _isCapturingScreenshot = true;
            CommandManager.InvalidateRequerySuggested();
            var mainWindow = Application.Current.MainWindow;
            var previousState = mainWindow?.WindowState ?? WindowState.Normal;
            try
            {
                if (mainWindow != null) mainWindow.WindowState = WindowState.Minimized;
                await Task.Delay(200);
                System.Drawing.Rectangle? clientBounds = null;
                if (windowRelative)
                {
                    var scope = action.SearchScope.Clone();
                    scope.WindowRegion = null;
                    clientBounds = ImageSearchScopeService.Resolve(scope).Bounds;
                }
                var picker = new RegionSelectWindow();
                picker.SelectionCompleted += selected =>
                {
                    if (selected is not { } region) return;
                    if (clientBounds is { } bounds)
                    {
                        if (!bounds.Contains(region))
                        {
                            StatusMessage = LocalizationService.Current.GetString("ImageRegionMustBeInWindow");
                            return;
                        }
                        action.SearchScope.WindowRegion = new ImageRelativeRegion
                        {
                            X = (region.X - bounds.X) / (double)bounds.Width, Y = (region.Y - bounds.Y) / (double)bounds.Height,
                            Width = region.Width / (double)bounds.Width, Height = region.Height / (double)bounds.Height
                        };
                    }
                    else action.SearchScope = new ImageSearchScope { Kind = ImageSearchScopeKind.Region,
                        X = region.X, Y = region.Y, Width = region.Width, Height = region.Height };
                    RefreshEditedAction(nameof(ImageScopeDescription)); RefreshImageProperties();
                };
                picker.ShowDialog();
            }
            catch (Exception ex) { StatusMessage = ex.Message; }
            finally
            {
                if (mainWindow != null) mainWindow.WindowState = previousState;
                _isCapturingScreenshot = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private async System.Threading.Tasks.Task TestMatchAsync()
        {
            if (_isTestingMatch) return;
            if (FindImageAction is not { } action || string.IsNullOrEmpty(ImagePath))
            {
                ShowMessage(LocalizationService.Current.GetString("EditorSelectImageFirst"));
                return;
            }
            if (!TryValidateImageFields(action, out var validationError) || !MacroActionValidator.TryValidate(action, out validationError))
            {
                StatusMessage = validationError;
                return;
            }
            var testAction = action.Clone();
            using var testCancellation = new CancellationTokenSource();
            _matchTestCancellation = testCancellation;
            var cancellationToken = testCancellation.Token;

            _isTestingMatch = true;
            OnPropertyChanged(nameof(IsTestingMatch));
            CommandManager.InvalidateRequerySuggested();
            StatusMessage = LocalizationService.Current.GetString("EditorTestingMatch");

            var dispatcher = Application.Current.Dispatcher;
            CaptureWindowVisibilityScope? hiddenWindows = null;
            var previewClosed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            MatchResultWindow? matchWindow = null;
            var sessionFinished = false;
            CancellationTokenRegistration closeOnCancellation = default;
            string? errorMessage = null;
            MatchResultHelpAction? helpAction = null;
            try
            {
                matchWindow = new MatchResultWindow();
                matchWindow.CancellationRequested += (_, _) => testCancellation.Cancel();
                matchWindow.Closed += (_, _) => previewClosed.TrySetResult(true);
                closeOnCancellation = cancellationToken.Register(() => dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!sessionFinished && !matchWindow.IsClosed) matchWindow.Close();
                })));

                // The editor is an owned modal window, not Application.MainWindow.
                // Hide both native windows without ending ShowDialog or enabling its owner.
                hiddenWindows = CaptureWindowVisibilityScope.HideVisibleWindows();
                await System.Threading.Tasks.Task.Delay(200, cancellationToken);

                var result = await System.Threading.Tasks.Task.Run(
                    () => _imageMatchService.TestMatch(testAction, cancellationToken, (snapshot, bounds) =>
                    {
                        // Present the captured frame before matching; no second capture or editor flash.
                        dispatcher.Invoke(() =>
                        {
                            if (sessionFinished || matchWindow.IsClosed || cancellationToken.IsCancellationRequested) return;
                            matchWindow.ShowCapturedFrame(snapshot, bounds);
                            if (!matchWindow.IsVisible) matchWindow.Show();
                        }, System.Windows.Threading.DispatcherPriority.Normal, cancellationToken);
                    }), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                StatusMessage = result.Found
                    ? LocalizationService.Current.Format("EditorMatchFoundStatus", result.Similarity, result.ElapsedMilliseconds)
                    : LocalizationService.Current.GetMatchFailureText(result.FailureReason);
                if (result.Snapshot == null)
                {
                    errorMessage = LocalizationService.Current.Format("EditorTestFailed",
                        string.IsNullOrWhiteSpace(result.ErrorMessage) ? StatusMessage : result.ErrorMessage);
                }
                else if (!matchWindow.IsClosed)
                {
                    matchWindow.ShowResult(result);
                    if (!matchWindow.IsVisible) matchWindow.Show();
                    await previewClosed.Task;
                    cancellationToken.ThrowIfCancellationRequested();
                    helpAction = matchWindow.RequestedHelpAction;
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = LocalizationService.Current.GetString("MatchCancelled");
            }
            catch (Exception ex)
            {
                errorMessage = LocalizationService.Current.Format("EditorTestFailed", ex.Message);
                StatusMessage = errorMessage;
            }
            finally
            {
                sessionFinished = true;
                closeOnCancellation.Dispose();
                if (matchWindow is { IsClosed: false }) matchWindow.Close();
                hiddenWindows?.Dispose();
                _isTestingMatch = false;
                OnPropertyChanged(nameof(IsTestingMatch));
                _matchTestCancellation = null;
                CommandManager.InvalidateRequerySuggested();
            }
            if (dispatcher.HasShutdownStarted) return;
            if (errorMessage != null) ShowMessage(errorMessage, icon: MessageBoxImage.Error);
            if (helpAction != null && Actions.Contains(action))
            {
                SelectedAction = action;
                ImageAdvancedExpanded = true;
                switch (helpAction)
                {
                    case MatchResultHelpAction.SearchScope:
                        if (UsesImageWindow) PickImageWindow();
                        else { ImageScopeKind = ImageSearchScopeKind.Region; await PickImageScopeRegionAsync(false); }
                        break;
                    case MatchResultHelpAction.AlternateTemplate:
                        await CaptureScreenshotAsync(true);
                        break;
                    case MatchResultHelpAction.Recapture:
                        await CaptureScreenshotAsync();
                        break;
                }
            }
        }

        private void ClearImage()
        {
            if (FindImageAction != null)
            {
                ImagePath = "";
                StatusMessage = LocalizationService.Current.GetString("EditorImageCleared");
            }
        }

        private void UpdateActionCommands()
        {
            CommandManager.InvalidateRequerySuggested();
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            OnUIThread(() =>
            {
                StatusMessage = LocalizationService.Current.GetString("StatusReady");
                OnPropertyChanged(nameof(KeyPressPreview));
                RefreshImageProperties();
                CollectionViewSource.GetDefaultView(Actions).Refresh();
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _matchTestCancellation?.Cancel();
                // 取消事件订阅
                if (_collectionChangedHandler != null)
                {
                    Actions.CollectionChanged -= _collectionChangedHandler;
                    _collectionChangedHandler = null;
                }
                LocalizationService.Current.LanguageChanged -= OnLanguageChanged;
            }
            base.Dispose(disposing);
        }

        #endregion
    }
}
