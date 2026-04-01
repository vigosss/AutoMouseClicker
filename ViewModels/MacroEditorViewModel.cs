using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Ming_AutoClicker.Helpers;
using Ming_AutoClicker.Models;
using Ming_AutoClicker.Services;
using Ming_AutoClicker.Views;

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
        private string _statusMessage = "就绪";
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
                    OnPropertyChanged(nameof(ImagePath));
                    OnPropertyChanged(nameof(ScreenshotPreview));
                    OnPropertyChanged(nameof(HasScreenshot));
                    OnPropertyChanged(nameof(MatchThreshold));
                    OnPropertyChanged(nameof(WaitUntilFound));
                    OnPropertyChanged(nameof(Operation));
                    OnPropertyChanged(nameof(OffsetX));
                    OnPropertyChanged(nameof(OffsetY));
                    OnPropertyChanged(nameof(WaitSeconds));
                    OnPropertyChanged(nameof(MouseClickAction));
                    OnPropertyChanged(nameof(ClickX));
                    OnPropertyChanged(nameof(ClickY));
                    OnPropertyChanged(nameof(ClickOperation));
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
        public string SelectedActionType => SelectedAction?.Type.ToString() ?? "无";

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
                    _macro.LoopCount = value;
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
                    _macro.LoopIntervalMs = value;
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
                    _macro.UpdatedAt = DateTime.Now;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ScreenshotPreview));
                    OnPropertyChanged(nameof(HasScreenshot));
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
                    bitmap.DecodePixelWidth = 300; // 限制解码宽度，节省内存
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
                }
            }
        }

        #endregion

        #region 命令

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand AddFindImageActionCommand { get; }
        public ICommand AddWaitActionCommand { get; }
        public ICommand AddMouseClickActionCommand { get; }
        public ICommand RemoveActionCommand { get; }
        public ICommand MoveUpCommand { get; }
        public ICommand MoveDownCommand { get; }
        public ICommand CaptureScreenshotCommand { get; }
        public ICommand TestMatchCommand { get; }
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

            // 初始化命令
            SaveCommand = new RelayCommand(_ => Save());
            CancelCommand = new RelayCommand(_ => Cancel());
            AddFindImageActionCommand = new RelayCommand(_ => AddFindImageAction());
            AddWaitActionCommand = new RelayCommand(_ => AddWaitAction());
            AddMouseClickActionCommand = new RelayCommand(_ => AddMouseClickAction());
            RemoveActionCommand = new RelayCommand(_ => RemoveAction(), _ => CanRemoveAction());
            MoveUpCommand = new RelayCommand(_ => MoveUp(), _ => CanMoveUp());
            MoveDownCommand = new RelayCommand(_ => MoveDown(), _ => CanMoveDown());
            CaptureScreenshotCommand = new RelayCommand(_ => CaptureScreenshot());
            TestMatchCommand = new RelayCommand(_ => TestMatch(), _ => IsActionSelected);
            ClearImageCommand = new RelayCommand(_ => ClearImage(), _ => IsActionSelected);
            PickCoordinateCommand = new RelayCommand(_ => PickCoordinate());

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
        }

        #region 命令实现

        private void Save()
        {
            try
            {
                // 重新排序动作
                for (int i = 0; i < Actions.Count; i++)
                {
                    Actions[i].Order = i;
                }

                // 不在这里保存，由 MainWindow.OnEditorRequestSave 统一处理
                StatusMessage = "保存成功";
                SaveCompleted?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                StatusMessage = $"保存失败: {ex.Message}";
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
                foreach (var action in original.Actions)
                {
                    Actions.Add(action);
                }
                _macro.UpdatedAt = original.UpdatedAt;

                // 重置选中动作
                SelectedAction = null;
            }
            StatusMessage = "已取消更改";
            CancelCompleted?.Invoke(this, EventArgs.Empty);
        }

        private void AddFindImageAction()
        {
            var action = new FindImageAction
            {
                Order = Actions.Count,
                ImagePath = "",
                MatchThreshold = 0.8,
                WaitUntilFound = false,
                Operation = "Click"
            };
            Actions.Add(action);
            SelectedAction = action;
            StatusMessage = "已添加找图动作";
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
            StatusMessage = "已添加等待动作";
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
            StatusMessage = "已添加鼠标点击位置动作";
        }

        /// <summary>
        /// 准星拾取坐标 - 最小化窗口后打开全屏覆盖层，按住鼠标拖动拾取坐标
        /// </summary>
        private void PickCoordinate()
        {
            if (MouseClickAction == null) return;

            try
            {
                // 最小化主窗口
                var mainWindow = Application.Current.MainWindow;
                mainWindow?.Dispatcher.Invoke(() => mainWindow.WindowState = WindowState.Minimized);

                // 等待窗口最小化完成
                System.Threading.Thread.Sleep(200);

                // 打开坐标拾取窗口
                var pickWindow = new CoordinatePickWindow();
                pickWindow.CoordinatePicked += (x, y) =>
                {
                    ClickX = x;
                    ClickY = y;
                    StatusMessage = $"已拾取坐标: ({x}, {y})";
                };
                pickWindow.Closed += (_, _) =>
                {
                    // 恢复主窗口
                    mainWindow?.Dispatcher.Invoke(() => mainWindow.WindowState = WindowState.Normal);
                };
                pickWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                StatusMessage = $"坐标拾取失败: {ex.Message}";
                var mainWindow = Application.Current.MainWindow;
                mainWindow?.Dispatcher.Invoke(() => mainWindow.WindowState = WindowState.Normal);
            }
        }

        private bool CanRemoveAction() => IsActionSelected && Actions.Count > 0;

        private void RemoveAction()
        {
            if (SelectedAction == null) return;

            var index = _selectedActionIndex;
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

            StatusMessage = "已删除动作";
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
            StatusMessage = "已上移";
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
            StatusMessage = "已下移";
        }

        private void CaptureScreenshot()
        {
            try
            {
                // 最小化主窗口
                var mainWindow = Application.Current.MainWindow;
                mainWindow?.Dispatcher.Invoke(() => mainWindow.WindowState = WindowState.Minimized);

                // 等待窗口最小化完成
                System.Threading.Thread.Sleep(200);

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

                            if (FindImageAction != null)
                            {
                                ImagePath = _screenCaptureService.GetRelativePath(filePath);
                                StatusMessage = $"已截图: {ImagePath} ({r.Width}×{r.Height})";
                            }
                        }
                        catch (Exception ex)
                        {
                            StatusMessage = $"截图保存失败: {ex.Message}";
                        }
                    }
                    else
                    {
                        StatusMessage = "截图已取消";
                    }

                    // 恢复主窗口
                    mainWindow?.Dispatcher.Invoke(() => mainWindow.WindowState = WindowState.Normal);
                };

                selectWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                StatusMessage = $"截图失败: {ex.Message}";
                // 确保主窗口恢复
                var mainWindow = Application.Current.MainWindow;
                mainWindow?.Dispatcher.Invoke(() => mainWindow.WindowState = WindowState.Normal);
            }
        }

        private void TestMatch()
        {
            if (FindImageAction == null || string.IsNullOrEmpty(ImagePath))
            {
                ShowMessage("请先选择或截取图像");
                return;
            }

            try
            {
                // 最小化主窗口，露出屏幕内容以便截图
                var mainWindow = Application.Current.MainWindow;
                mainWindow?.Dispatcher.Invoke(() => mainWindow.WindowState = WindowState.Minimized);
                System.Threading.Thread.Sleep(200);

                var result = _imageMatchService.TestMatch(ImagePath, MatchThreshold);

                if (result.Found)
                {
                    // 找到匹配：用全屏覆盖窗口可视化展示匹配位置
                    var matchWindow = new MatchResultWindow(result);
                    matchWindow.Loaded += (_, _) =>
                    {
                        StatusMessage = $"匹配成功! 位置: ({result.X}, {result.Y}) 相似度: {result.Similarity:P}";
                    };
                    matchWindow.Closed += (_, _) =>
                    {
                        // 恢复主窗口
                        mainWindow?.Dispatcher.Invoke(() => mainWindow.WindowState = WindowState.Normal);
                    };
                    matchWindow.ShowDialog();
                }
                else
                {
                    // 未找到匹配：恢复窗口并弹窗提示
                    mainWindow?.Dispatcher.Invoke(() => mainWindow.WindowState = WindowState.Normal);
                    ShowMessage($"未找到匹配\n请尝试降低阈值或重新截图", "测试结果", MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                ShowMessage($"测试失败: {ex.Message}", "错误", MessageBoxImage.Error);
                // 确保主窗口恢复
                var mainWindow = Application.Current.MainWindow;
                mainWindow?.Dispatcher.Invoke(() => mainWindow.WindowState = WindowState.Normal);
            }
        }

        private void ClearImage()
        {
            if (FindImageAction != null)
            {
                ImagePath = "";
                StatusMessage = "已清除图像";
            }
        }

        private void UpdateActionCommands()
        {
            CommandManager.InvalidateRequerySuggested();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // 取消事件订阅
                if (_collectionChangedHandler != null)
                {
                    Actions.CollectionChanged -= _collectionChangedHandler;
                    _collectionChangedHandler = null;
                }
            }
            base.Dispose(disposing);
        }

        #endregion
    }
}