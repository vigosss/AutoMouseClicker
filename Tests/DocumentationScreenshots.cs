using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MahApps.Metro.IconPacks;
using Ming_AutoClicker.Helpers;
using Ming_AutoClicker.Models;
using Ming_AutoClicker.Services;
using Ming_AutoClicker.ViewModels;
using Ming_AutoClicker.Views.AutoClick;
using Ming_AutoClicker.Views.Macros;
using Ming_AutoClicker.Views.Recordings;
using Ming_AutoClicker.Views.Settings;

namespace Ming_AutoClicker.Tests;

internal static partial class Program
{
    // Deliberately opt-in: the regression suite never replaces checked-in documentation images.
    private static int RunDocumentationScreenshots()
    {
        var repository = FindDocumentationRepository();
        var images = Path.Combine(repository, "docs", "images");
        var originalLanguage = LocalizationService.Current.SelectedLanguage;
        try
        {
            PixelFontResolution().GetAwaiter().GetResult();
            foreach (var language in new[] { AppLanguage.SimplifiedChinese, AppLanguage.English })
            {
                LocalizationService.Current.ApplyLanguage(language);
                var english = language == AppLanguage.English;
                var output = english ? Path.Combine(images, "en") : images;
                Directory.CreateDirectory(output);
                using var sample = new DocumentationSample(english);
                RenderDocumentationMain(sample, 0, Path.Combine(output, "auto-clicker.png"));
                RenderDocumentationMain(sample, 1, Path.Combine(output, "macro-list.png"));
                RenderDocumentationMain(sample, 2, Path.Combine(output, "recording-playback.png"));
                RenderDocumentationEditor(sample, Path.Combine(output, "macro-editor.png"));
                RenderDocumentationSettings(language, Path.Combine(output, "settings.png"));
                Check(sample.Playback.Runs.Count == 0 && sample.Recordings.IsIdle,
                    "Documentation generation must never start capture or playback.");
            }
        }
        finally { LocalizationService.Current.ApplyLanguage(originalLanguage); }
        Console.WriteLine($"Generated 10 documentation screenshots in {images}");
        return 0;
    }

    private static string FindDocumentationRepository()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "Ming-AutoClicker.csproj")))
                    return directory.FullName;
        throw new InvalidOperationException("Run --docs-screenshots from a Ming-AutoClicker source checkout.");
    }

    private static void RenderDocumentationMain(DocumentationSample sample, int tab, string destination)
    {
        var data = new DocumentationMainData(sample.Macros, tab);
        var window = new MainWindow { Width = 820, Height = 620, DataContext = data };
        try
        {
            var version = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion ?? typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "";
            ((TextBlock)window.FindName("VersionText")).Text = "v" + version.Split('+', '-')[0];
            ((AutoClickView)window.FindName("AutoClickView")).DataContext = new PixelAutoClickData
            {
                HotkeyHintText = LocalizationService.Current.GetString("AutoClickStartHint"),
                StatusText = LocalizationService.Current.GetString("StatusReady")
            };
            ((RecordingPage)window.FindName("RecordingPage")).DataContext = sample.Recordings;
            var root = PixelWindowRoot(window);
            PixelLayout(root, 820, 620, 96);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            root.UpdateLayout();
            var tabs = (TabControl)window.FindName("MainTabControl");
            Equal(tab, tabs.SelectedIndex);
            PixelCheckRow(tabs.Items.Cast<TabItem>(), root, "Documentation navigation");
            if (tab == 0)
            {
                var auto = (AutoClickView)window.FindName("AutoClickView");
                var panel = ((Grid)auto.Content).Children.OfType<Border>().Single();
                PixelCheckPanelContent(panel, "Documentation auto-click settings");
            }
            else if (tab == 1)
            {
                var macros = (MacroListView)window.FindName("MacroListView");
                var list = (ListBox)macros.FindName("MacroListBox");
                Equal(sample.Macros.Count, list.Items.Count);
                CheckDocumentationNames(list, sample.Macros.Select(macro => macro.Name));
            }
            else
            {
                var recording = (RecordingPage)window.FindName("RecordingPage");
                var list = (ListBox)recording.FindName("RecordingsList");
                CheckDocumentationNames(list, sample.Recordings.Recordings.Select(item => item.Name));
                var footer = (FrameworkElement)recording.FindName("RecordingFooter");
                PixelCheckInside(footer, recording, "Documentation recording footer");
                var inputs = new[] { "LoopCountInput", "LoopIntervalInput" }
                    .Select(name => (TextBox)recording.FindName(name)).ToArray();
                var failures = new List<string>();
                foreach (var box in inputs) CheckInputValueFits(box, "Documentation playback option", failures);
                Check(failures.Count == 0, string.Join(Environment.NewLine, failures));
                Equal("3", inputs[0].Text);
                Equal("1000", inputs[1].Text);
                Equal(1d, sample.Recordings.SpeedFactor);
            }
            SaveDocumentationWindow(window, root, destination);
        }
        finally
        {
            ((RecordingPage)window.FindName("RecordingPage")).DataContext = null;
            window.DataContext = null;
            typeof(MainWindow).GetField("_allowExit", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(window, true);
            window.Close();
        }
    }

    private static void RenderDocumentationEditor(DocumentationSample sample, string destination)
    {
        using var capture = new ScreenCaptureService();
        using var matching = new ImageMatchService(capture);
        using var model = new MacroEditorViewModel(new MacroStorageService(), capture, matching, sample.Macros[0].DeepClone());
        model.SelectedAction = model.Actions.OfType<FindImageAction>().Single();
        var view = new MacroEditorView { DataContext = model };
        var window = new Window
        {
            Title = LocalizationService.Current.Format("EditorWindowTitle", model.MacroName),
            Style = (Style)Application.Current.FindResource("PixelWindow"),
            Width = 800, Height = 600, MinWidth = 700, MinHeight = 500,
            ResizeMode = ResizeMode.CanResize,
            Background = (Brush)Application.Current.FindResource("BackgroundBrush"),
            Content = view
        };
        try
        {
            var root = PixelWindowRoot(window);
            PixelLayout(root, 800, 600, 96);
            Check(model.HasScreenshot, "Documentation must show the generated confirm-button template.");
            Equal(5, model.Actions.Select(action => action.Type).Distinct().Count());
            var header = (FrameworkElement)view.FindName("EditorHeaderFrame");
            PixelCheckInside(header, view, "Documentation macro header");
            var name = (TextBox)view.FindName("EditorNameInput");
            var fields = new List<string>();
            CheckInputValueFits(name, "Documentation macro title", fields);
            Check(fields.Count == 0, string.Join(Environment.NewLine, fields));
            SaveDocumentationWindow(window, root, destination);
        }
        finally { view.DataContext = null; window.Content = null; window.Close(); }
    }

    private static void RenderDocumentationSettings(AppLanguage language, string destination)
    {
        var window = new HotkeySettingsWindow(HotkeyGesture.Default, language,
            (_, _) => throw new InvalidOperationException("Documentation must not save application settings."));
        try
        {
            var root = PixelWindowRoot(window);
            PixelSetDpi(root, 96);
            root.Measure(new Size(460, double.PositiveInfinity));
            PixelLayout(root, 460, (int)Math.Ceiling(root.DesiredSize.Height), 96);
            PixelCheckWindowLabels(window, root, "Documentation settings");
            SaveDocumentationWindow(window, root, destination);
        }
        finally { window.Close(); }
    }

    private static void CheckDocumentationNames(ListBox list, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            var label = Descendants<TextBlock>(list).Single(text => text.Text == name);
            var probe = new TextBlock { Text = name, FontFamily = label.FontFamily, FontSize = label.FontSize };
            TextOptions.SetTextFormattingMode(probe, TextFormattingMode.Display);
            probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Check(probe.DesiredSize.Width <= label.ActualWidth + .5, $"The documentation title '{name}' must fit on one line.");
        }
    }

    private static void SaveDocumentationWindow(Window window, FrameworkElement root, string destination)
    {
        PixelCheckNeverShown(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(PixelBitmap(root, 96)));
        using var output = File.Create(destination);
        encoder.Save(output);
        Console.WriteLine($"Wrote {destination}");
    }

    private sealed class DocumentationMainData
    {
        public DocumentationMainData(ObservableCollection<MacroProfile> macros, int tab)
        {
            Macros = macros;
            SelectedMacro = macros[0];
            CurrentTabIndex = tab;
        }
        private static ICommand Disabled => new RelayCommand(_ => throw new InvalidOperationException("Documentation action invoked."), _ => false);
        private static ICommand Enabled => new RelayCommand(_ => throw new InvalidOperationException("Documentation action invoked."));
        public ObservableCollection<MacroProfile> Macros { get; }
        public MacroProfile? SelectedMacro { get; set; }
        public int CurrentTabIndex { get; set; }
        public int AutoClickCount { get; set; }
        public bool IsNotExecuting => true;
        public bool IsNavigationEnabled => true;
        public bool CanConfigureSettings => true;
        public string ActiveHotkeyDescription => "F8";
        public string ExecutionStatus => LocalizationService.Current.GetString("StatusReady");
        public string StatusMessage => LocalizationService.Current.Format("StatusLoadedMacros", Macros.Count);
        public string MacroHotkeyHintText => LocalizationService.Current.Format("HotkeyMacroHint", "F8");
        public ICommand DuplicateMacroCommand { get; } = Enabled;
        public ICommand DeleteMacroCommand { get; } = Enabled;
        public ICommand CreateMacroCommand { get; } = Enabled;
        public ICommand StartMacroCommand { get; } = Enabled;
        public ICommand StopMacroCommand { get; } = Disabled;
    }

    private sealed class DocumentationSample : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "MingAutoClicker-Documentation-" + Guid.NewGuid().ToString("N"));
        private readonly string _template;
        private readonly string _recordingsPath;
        public ObservableCollection<MacroProfile> Macros { get; }
        public RecordingPageViewModel Recordings { get; }
        public RecordingTestPlayback Playback { get; } = new();

        public DocumentationSample(bool english)
        {
            Directory.CreateDirectory(_directory);
            _template = Path.Combine(_directory, "ok.png");
            _recordingsPath = Path.Combine(_directory, "recordings.json");
            CreateDocumentationTemplate(_template, english);
            var date = new DateTime(2026, 11, 13, 10, 30, 0);
            Macros = new ObservableCollection<MacroProfile>
            {
                new()
                {
                    Name = english ? "Form submit" : "表单提交", CreatedAt = date, UpdatedAt = date,
                    LoopEnabled = true, LoopCount = 3, LoopIntervalMs = 1000,
                    Actions = new ObservableCollection<MacroAction>
                    {
                        new MouseClickAction { Order = 0, X = 640, Y = 360, Note = english ? "Focus the form" : "定位表单输入框" },
                        new KeyPressAction { Order = 1, VirtualKey = 0x56, Modifiers = KeyModifiers.Control, Note = english ? "Paste prepared text" : "粘贴已准备的内容" },
                        new WaitAction { Order = 2, WaitSeconds = .5, Note = english ? "Wait for validation" : "等待表单校验" },
                        new MouseScrollAction { Order = 3, Direction = ScrollDirection.Down, Notches = 3, Note = english ? "Scroll to the footer" : "滚动到表单底部" },
                        new FindImageAction { Order = 4, ImagePath = _template, TimeoutMs = 5000, MatchThreshold = .9, CaptureDpi = 96,
                            ClickPoint = new ImageClickPoint { X = .8, Y = .5 },
                            Note = english ? "Find and click Confirm" : "识别并点击确认按钮" }
                    }
                },
                new()
                {
                    Name = english ? "Page browse" : "页面浏览", CreatedAt = date.AddMinutes(12), UpdatedAt = date.AddMinutes(12),
                    LoopEnabled = true, LoopCount = 3, LoopIntervalMs = 1000,
                    Actions = new ObservableCollection<MacroAction>
                    {
                        new KeyPressAction { Order = 0, VirtualKey = 0x24, Modifiers = KeyModifiers.Control, Note = english ? "Go to the page top" : "回到页面顶部" },
                        new WaitAction { Order = 1, WaitSeconds = 1 },
                        new MouseScrollAction { Order = 2, Direction = ScrollDirection.Down, Notches = 4 },
                        new WaitAction { Order = 3, WaitSeconds = 1 }
                    }
                }
            };
            Recordings = new RecordingPageViewModel(new RecordingStorageService(_recordingsPath),
                new RecordingTestCapture(), Playback, new RecordingTestClock());
            Recordings.Recordings.Add(new RecordingItemViewModel(new Recording
            {
                Name = english ? "Form filling" : "表单填写", CreatedAt = date,
                DesktopBounds = new DesktopBounds { Width = 1920, Height = 1080 },
                Actions = DocumentationRecordedActions(false)
            }, Recordings));
            Recordings.Recordings.Add(new RecordingItemViewModel(new Recording
            {
                Name = english ? "Page navigation" : "页面导航", CreatedAt = date.AddMinutes(8),
                DesktopBounds = new DesktopBounds { Width = 1920, Height = 1080 },
                Actions = DocumentationRecordedActions(true)
            }, Recordings));
            Recordings.SelectedRecording = Recordings.Recordings[0];
            Recordings.SpeedFactor = 1;
            Recordings.LoopCount = 3;
            Recordings.LoopInterval = 1000;
        }

        public void Dispose()
        {
            Recordings.Dispose();
            File.Delete(_template);
            File.Delete(_recordingsPath);
            Directory.Delete(_directory); // Only these two known files are created; never delete recursively.
        }
    }

    private static List<RecordedAction> DocumentationRecordedActions(bool navigation)
    {
        var actions = new List<RecordedAction>
        {
            new() { Type = RecordedActionType.MouseMove, X = 640, Y = 360, DelayBeforeMs = 1000 },
            new() { Type = RecordedActionType.MouseDown, Button = RecordedMouseButton.Left, X = 640, Y = 360, DelayBeforeMs = 400 },
            new() { Type = RecordedActionType.MouseUp, Button = RecordedMouseButton.Left, X = 640, Y = 360, DelayBeforeMs = 100 },
            new() { Type = RecordedActionType.KeyDown, VirtualKey = 0x11, KeyName = "LeftCtrl", DelayBeforeMs = 900 },
            new() { Type = RecordedActionType.KeyDown, VirtualKey = navigation ? 0x24u : 0x56u, KeyName = navigation ? "Home" : "V", DelayBeforeMs = 80 },
            new() { Type = RecordedActionType.KeyUp, VirtualKey = navigation ? 0x24u : 0x56u, KeyName = navigation ? "Home" : "V", DelayBeforeMs = 100 },
            new() { Type = RecordedActionType.KeyUp, VirtualKey = 0x11, KeyName = "LeftCtrl", DelayBeforeMs = 80 },
            new() { Type = RecordedActionType.MouseWheel, WheelDelta = -360, X = 640, Y = 360, DelayBeforeMs = 1500 },
            new() { Type = RecordedActionType.MouseMove, X = 960, Y = 720, DelayBeforeMs = 1200 },
            new() { Type = RecordedActionType.MouseDown, Button = RecordedMouseButton.Left, X = 960, Y = 720, DelayBeforeMs = 600 },
            new() { Type = RecordedActionType.MouseUp, Button = RecordedMouseButton.Left, X = 960, Y = 720, DelayBeforeMs = 100 }
        };
        return actions;
    }

    private static void CreateDocumentationTemplate(string path, bool english)
    {
        var button = new Button
        {
            Content = english ? "Confirm" : "确认", Style = (Style)Application.Current.FindResource("PrimaryButton"),
            Width = 272, Height = 64, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        PixelIcon.SetKind(button, PackIconPixelartIconsKind.Check);
        var panel = new Grid { Background = (Brush)Application.Current.FindResource("SurfaceBrush"), UseLayoutRounding = true };
        panel.Children.Add(button);
        // Match the real preview's 110-DIP height, so the target is displayed at 1:1 pixels.
        PixelLayout(panel, 320, 110, 96);
        ((TextBlock)button.Template.FindName("ButtonContent", button)).FontSize = 24;
        PixelLayout(panel, 320, 110, 96);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(PixelBitmap(panel, 96)));
        using var output = File.Create(path);
        encoder.Save(output);
    }
}
