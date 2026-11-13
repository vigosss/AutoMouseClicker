using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MahApps.Metro.IconPacks;
using Ming_AutoClicker.Models;
using Ming_AutoClicker.Services;
using Ming_AutoClicker.Views.AutoClick;
using Ming_AutoClicker.Views.Settings;
using Ming_AutoClicker.Views.Shared;
using Ming_AutoClicker.Views.Updates;

namespace Ming_AutoClicker.Tests;

internal static partial class Program
{
    private static void AddPixelThemeTests(List<(string Name, Func<Task> Run)> tests)
    {
        tests.Add(("Pixel font resources resolve to the bundled Fusion font rather than a system fallback", PixelFontResolution));
        tests.Add(("Pixel main window fits both languages, window sizes and display scales off screen", PixelMainRendering));
        tests.Add(("Pixel icons retain their original 24-unit coordinates when rasterized", PixelIconRendering));
        tests.Add(("Pixel settings window renders readable controls without overlapping actions", PixelSettingsRendering));
        tests.Add(("Pixel update and confirmation windows keep localized titles and actions visible", PixelDialogRendering));
    }

    private static Task PixelFontResolution()
    {
        foreach (var resource in new[] { "PixelFontFamily", "PixelMonoFontFamily" })
        {
            var family = (FontFamily)Application.Current.FindResource(resource);
            var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            Check(typeface.TryGetGlyphTypeface(out var glyphs), resource + " must resolve to an actual font file.");
            Check(glyphs.FamilyNames.Values.Any(name => name.Contains("Fusion Pixel", StringComparison.OrdinalIgnoreCase)),
                $"{resource} fell back to {string.Join(", ", glyphs.FamilyNames.Values)}.");
            Check(glyphs.FontUri.OriginalString.Contains("fusion-pixel-12px-proportional-zh_hans.ttf", StringComparison.OrdinalIgnoreCase),
                $"{resource} must use the bundled font, got {glyphs.FontUri}.");
            foreach (var character in "像素A0")
                Check(glyphs.CharacterToGlyphMap.ContainsKey(character), $"The pixel font lacks the '{character}' glyph.");
        }
        return Task.CompletedTask;
    }

    private static Task PixelMainRendering()
    {
        var originalLanguage = LocalizationService.Current.SelectedLanguage;
        try
        {
            foreach (var language in new[] { AppLanguage.English, AppLanguage.SimplifiedChinese })
            foreach (var size in new[] { new Size(680, 520), new Size(820, 620) })
            foreach (var dpi in new[] { 96, 120, 144, 192 })
            {
                LocalizationService.Current.ApplyLanguage(language);
                var locale = language == AppLanguage.English ? "en" : "zh";
                var scenario = $"main {locale} {size.Width} DIP {dpi} DPI";
                // No MainViewModel: its native hooks, storage and playback services are not part
                // of a visual test. Mutable properties support the view's TwoWay bindings.
                var window = new MainWindow
                {
                    Width = size.Width,
                    Height = size.Height,
                    DataContext = new PixelMainData
                    {
                        ExecutionStatus = LocalizationService.Current.GetString("StatusReady"),
                        StatusMessage = language == AppLanguage.English ? "0 macros loaded" : "已加载 0 个宏"
                    }
                };
                try
                {
                    var autoClick = (AutoClickView)window.FindName("AutoClickView");
                    autoClick.DataContext = new PixelAutoClickData
                    {
                        HotkeyHintText = LocalizationService.Current.GetString("AutoClickStartHint"),
                        StatusText = LocalizationService.Current.GetString("StatusReady")
                    };
                    var root = PixelWindowRoot(window);
                    PixelLayout(root, (int)size.Width, (int)size.Height, dpi);
                    var tabs = (TabControl)window.FindName("MainTabControl");
                    var tabItems = tabs.Items.Cast<TabItem>().ToArray();
                    Equal(3, tabItems.Length);
                    PixelCheckRow(tabItems, root, scenario + " navigation");
                    foreach (var tab in tabItems)
                    {
                        PixelCheckInside(tab, tabs, scenario + " tab");
                        var label = Descendants<TextBlock>(tab).FirstOrDefault(text => text.Text == tab.Header?.ToString());
                        if (label != null) PixelCheckText(label, scenario + " tab label");
                    }
                    var content = (Grid)window.Content;
                    var footer = content.Children.OfType<Border>().Single(border => Grid.GetRow(border) == 2);
                    PixelCheckInside(autoClick, content, scenario + " page");
                    PixelCheckInside(footer, content, scenario + " status bar");
                    Check(PixelBounds(autoClick, root).Bottom <= PixelBounds(footer, root).Top + .5,
                        scenario + ": the page overlaps the main status bar.");
                    Check(tabItems.Max(tab => PixelBounds(tab, root).Bottom) <= PixelBounds(autoClick, root).Top + .5,
                        scenario + ": the navigation overlaps the selected page.");

                    var autoGrid = (Grid)autoClick.Content;
                    var panel = autoGrid.Children.OfType<Border>().Single();
                    var pageStatus = autoGrid.Children.OfType<TextBlock>().Single();
                    PixelCheckInside(panel, autoClick, scenario + " auto-click panel");
                    PixelCheckPanelContent(panel, scenario + " auto-click content");
                    Check(PixelBounds(panel, root).Bottom <= PixelBounds(pageStatus, root).Top + .5,
                        scenario + ": the settings panel overlaps its status text.");
                    PixelCheckRow(Descendants<RadioButton>(autoClick), root, scenario + " mouse choices");
                    foreach (var text in Descendants<TextBlock>(autoClick)) PixelCheckText(text, scenario);
                    var interval = Descendants<TextBox>(autoClick).Single();
                    Equal("100", interval.Text);
                    var first = interval.GetRectFromCharacterIndex(0);
                    var last = interval.GetRectFromCharacterIndex(interval.Text.Length - 1, true);
                    Check(!first.IsEmpty && !last.IsEmpty && interval.ExtentWidth <= interval.ViewportWidth + .5,
                        scenario + ": the interval must be readable before keyboard focus.");
                    var footerGrid = (Grid)footer.Child;
                    PixelCheckRow(footerGrid.Children.OfType<FrameworkElement>(), root, scenario + " status items");
                    foreach (var child in footerGrid.Children.OfType<FrameworkElement>())
                        PixelCheckInside(child, footerGrid, scenario + " status item");
                    PixelSave(root, $"pixel-main-{locale}-{size.Width}-{dpi}dpi.png", dpi);
                    PixelCheckNeverShown(window);
                }
                finally
                {
                    window.DataContext = null;
                    typeof(MainWindow).GetField("_allowExit", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .SetValue(window, true);
                    window.Close();
                }
            }
        }
        finally { LocalizationService.Current.ApplyLanguage(originalLanguage); }
        return Task.CompletedTask;
    }

    private static Task PixelIconRendering()
    {
        foreach (var (kind, expected) in new[]
        {
            (PackIconPixelartIconsKind.AddBox, new Rect(3, 3, 18, 18)),
            (PackIconPixelartIconsKind.Close, new Rect(5, 5, 14, 14))
        })
        {
            var icon = new PackIconPixelartIcons();
            icon.BeginInit();
            icon.Kind = kind;
            icon.Foreground = Brushes.White;
            icon.Style = (Style)Application.Current.FindResource(typeof(PackIconPixelartIcons));
            icon.EndInit();
            var host = new Grid();
            host.Children.Add(icon);
            Layout(host, 24, 24);
            icon.ApplyTemplate();
            icon.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            host.UpdateLayout();
            var bitmap = PixelBitmap(host, 96);
            var pixels = new byte[24 * 24 * 4];
            bitmap.CopyPixels(pixels, 24 * 4, 0);
            var points = Enumerable.Range(0, 24 * 24).Where(index => pixels[index * 4 + 3] > 0).ToArray();
            Check(points.Length > 0, $"{kind} must paint visible pixels.");
            var bounds = new Rect(points.Min(index => index % 24), points.Min(index => index / 24),
                points.Max(index => index % 24) - points.Min(index => index % 24) + 1,
                points.Max(index => index / 24) - points.Min(index => index / 24) + 1);
            Equal(expected, bounds);
            PixelSave(host, $"pixel-icon-{kind}.png", 96);
        }
        return Task.CompletedTask;
    }

    private static Task PixelSettingsRendering()
    {
        var originalLanguage = LocalizationService.Current.SelectedLanguage;
        try
        {
            foreach (var language in new[] { AppLanguage.English, AppLanguage.SimplifiedChinese })
            foreach (var dpi in new[] { 96, 120, 144, 192 })
            {
                LocalizationService.Current.ApplyLanguage(language);
                var locale = language == AppLanguage.English ? "en" : "zh";
                var window = new HotkeySettingsWindow(HotkeyGesture.Default, language,
                    (_, _) => throw new InvalidOperationException("A visual fixture must never save settings."));
                try
                {
                    var root = PixelWindowRoot(window);
                    PixelSetDpi(root, dpi);
                    root.Measure(new Size(window.Width, double.PositiveInfinity));
                    PixelLayout(root, (int)window.Width, (int)Math.Ceiling(root.DesiredSize.Height), dpi);
                    var buttons = Descendants<Button>((FrameworkElement)window.Content).ToArray();
                    Equal(3, buttons.Length);
                    PixelCheckRow(buttons, root, $"settings {locale} {dpi} DPI actions");
                    foreach (var button in buttons) PixelCheckInside(button, root, "Settings action");
                    foreach (var text in Descendants<TextBlock>((FrameworkElement)window.Content))
                        PixelCheckText(text, $"settings {locale} {dpi} DPI");
                    PixelSave(root, $"pixel-settings-{locale}-{dpi}dpi.png", dpi);
                    PixelCheckNeverShown(window);
                }
                finally { window.Close(); }
            }
        }
        finally { LocalizationService.Current.ApplyLanguage(originalLanguage); }
        return Task.CompletedTask;
    }

    private static FrameworkElement PixelWindowRoot(Window window)
    {
        window.ApplyTemplate();
        Check(VisualTreeHelper.GetChildrenCount(window) == 1, "The actual window template must materialize off screen.");
        PixelCheckNeverShown(window);
        return (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
    }

    private static Task PixelDialogRendering()
    {
        var originalLanguage = LocalizationService.Current.SelectedLanguage;
        try
        {
            foreach (var language in new[] { AppLanguage.English, AppLanguage.SimplifiedChinese })
            foreach (var dpi in new[] { 96, 120, 144, 192 })
            {
                LocalizationService.Current.ApplyLanguage(language);
                var locale = language == AppLanguage.English ? "en" : "zh";
                using var service = new UpdateService(); // Construction does not make HTTP requests.
                var update = new UpdateWindow(new UpdateCheckResult
                {
                    HasUpdate = true,
                    CurrentVersion = new Version(1, 0, 0),
                    LatestVersion = new Version(1, 1, 0),
                    LatestVersionTag = "v1.1.0",
                    PublishedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    ReleaseNotes = language == AppLanguage.English
                        ? "Clear pixel icons and consistent controls.\nImproved layouts at different display scales."
                        : "统一像素图标与控件外观。\n改善不同显示缩放下的界面布局。"
                }, service);
                try
                {
                    var root = PixelWindowRoot(update);
                    PixelLayout(root, (int)update.Width, (int)update.Height, dpi);
                    PixelCheckWindowLabels(update, root, $"update {locale} {dpi} DPI");
                    var notes = (TextBox)update.FindName("ReleaseNotesText");
                    Check(notes.ViewportHeight >= 24, "Release notes must retain space for readable text.");
                    PixelCheckInside(notes, root, "Update release notes");
                    PixelSave(root, $"pixel-update-{locale}-{dpi}dpi.png", dpi);
                    PixelCheckNeverShown(update);
                }
                finally { update.AllowClose(); }

                var dialog = new DialogWindow(
                    language == AppLanguage.English ? "Delete this recording? This action cannot be undone." : "确定删除这段录制吗？此操作无法撤销。",
                    LocalizationService.Current.GetString("CommonConfirm"), DialogType.Question, DialogButtons.YesNo);
                try
                {
                    var root = PixelWindowRoot(dialog);
                    PixelSetDpi(root, dpi);
                    root.Measure(new Size(dialog.Width, double.PositiveInfinity));
                    PixelLayout(root, (int)dialog.Width, (int)Math.Ceiling(root.DesiredSize.Height), dpi);
                    PixelCheckWindowLabels(dialog, root, $"confirmation {locale} {dpi} DPI");
                    PixelSave(root, $"pixel-dialog-{locale}-{dpi}dpi.png", dpi);
                    PixelCheckNeverShown(dialog);
                }
                finally { dialog.Close(); }
            }
        }
        finally { LocalizationService.Current.ApplyLanguage(originalLanguage); }
        return Task.CompletedTask;
    }

    private static void PixelCheckWindowLabels(Window window, FrameworkElement root, string scenario)
    {
        var content = (FrameworkElement)window.Content;
        foreach (var text in Descendants<TextBlock>(content).Where(PixelParticipates))
        {
            PixelCheckInside(text, root, scenario + " label");
            PixelCheckText(text, scenario);
        }
        var buttons = Descendants<Button>(content).Where(PixelParticipates).ToArray();
        PixelCheckRow(buttons, root, scenario + " actions");
        foreach (var button in buttons) PixelCheckInside(button, root, scenario + " action");
    }

    private static void PixelCheckNeverShown(Window window)
    {
        Check(!window.IsLoaded && !window.IsVisible && new WindowInteropHelper(window).Handle == IntPtr.Zero,
            "Visual tests must not load/show a native window or initialize tray icons and input hooks.");
    }

    private static void PixelLayout(FrameworkElement root, int width, int height, int dpi)
    {
        Layout(root, width, height);
        PixelSetDpi(root, dpi);
        Layout(root, width, height);
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        root.UpdateLayout();
    }

    private static void PixelSetDpi(Visual visual, int dpi)
    {
        while (VisualTreeHelper.GetParent(visual) is Visual parent) visual = parent;
        VisualTreeHelper.SetRootDpi(visual, new DpiScale(dpi / 96d, dpi / 96d));
        PixelInvalidateDpiLayout(visual);
    }

    private static void PixelInvalidateDpiLayout(Visual visual)
    {
        // Off-screen roots have no HwndTarget to invalidate their previously measured templates
        // after SetRootDpi. Remeasure every realized child at the new pixels-per-DIP value.
        if (visual is UIElement element)
        {
            element.InvalidateMeasure();
            element.InvalidateArrange();
        }
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(visual); index++)
            if (VisualTreeHelper.GetChild(visual, index) is Visual child) PixelInvalidateDpiLayout(child);
    }

    private static Rect PixelBounds(FrameworkElement element, Visual root) =>
        element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));

    private static void PixelCheckInside(FrameworkElement element, FrameworkElement container, string scenario)
    {
        if (element.Visibility == Visibility.Collapsed) return;
        var bounds = PixelBounds(element, container);
        Check(bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= -.5 && bounds.Top >= -.5
            && bounds.Right <= container.ActualWidth + .5 && bounds.Bottom <= container.ActualHeight + .5,
            $"{scenario}: {element.GetType().Name} exceeds its {container.RenderSize} container: {bounds}.");
    }

    private static void PixelCheckPanelContent(Border panel, string scenario)
    {
        // Use the same physical-pixel rounding as Border.MeasureOverride. At 125%,
        // two nominal DIPs become 1.6 DIPs; subtracting the nominal values invents clipping.
        var dpi = VisualTreeHelper.GetDpi(panel);
        double Snap(double value, double scale) => panel.UseLayoutRounding ? Math.Round(value * scale) / scale : value;
        var inset = new Thickness(Snap(panel.BorderThickness.Left, dpi.DpiScaleX) + panel.Padding.Left,
            Snap(panel.BorderThickness.Top, dpi.DpiScaleY) + panel.Padding.Top,
            Snap(panel.BorderThickness.Right, dpi.DpiScaleX) + panel.Padding.Right,
            Snap(panel.BorderThickness.Bottom, dpi.DpiScaleY) + panel.Padding.Bottom);
        var available = new Rect(inset.Left, inset.Top, Math.Max(0, panel.ActualWidth - inset.Left - inset.Right),
            Math.Max(0, panel.ActualHeight - inset.Top - inset.Bottom));
        var content = (FrameworkElement)panel.Child;
        // A StackPanel can arrange at its natural size while an ancestor silently clips it.
        // Checking only the outer Border and the page status misses a cropped hotkey frame.
        Check(content.DesiredSize.Width <= available.Width + .5 && content.DesiredSize.Height <= available.Height + .5,
            $"{scenario}: content needs {content.DesiredSize}, but the panel interior is {available.Size}.");
        var tolerantBounds = available;
        tolerantBounds.Inflate(.5, .5);
        Check(tolerantBounds.Contains(PixelBounds(content, panel)),
            $"{scenario}: arranged content extends beyond the padded panel interior.");
        if (content is Panel contentPanel)
            foreach (var child in contentPanel.Children.OfType<FrameworkElement>().Where(PixelParticipates))
                Check(tolerantBounds.Contains(PixelBounds(child, panel)),
                    $"{scenario}: {child.GetType().Name} extends beyond the padded panel interior: {PixelBounds(child, panel)}.");
    }

    private static void PixelCheckRow(IEnumerable<FrameworkElement> items, Visual root, string scenario)
    {
        var bounds = items.Where(item => item.Visibility != Visibility.Collapsed)
            .Select(item => PixelBounds(item, root)).OrderBy(rect => rect.Left).ToArray();
        for (var i = 1; i < bounds.Length; i++)
            Check(bounds[i - 1].Right <= bounds[i].Left + .5,
                $"{scenario}: adjacent controls overlap: {bounds[i - 1]} / {bounds[i]}.");
    }

    private static void PixelCheckText(TextBlock text, string scenario)
    {
        if (!PixelParticipates(text) || string.IsNullOrEmpty(text.Text)) return;
        var measured = new FormattedText(text.Text, CultureInfo.CurrentUICulture, text.FlowDirection,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize,
            text.Foreground, null, TextFormattingMode.Display, VisualTreeHelper.GetDpi(text).PixelsPerDip);
        if (text.TextWrapping == TextWrapping.NoWrap && text.TextTrimming == TextTrimming.None)
            Check(measured.WidthIncludingTrailingWhitespace <= text.ActualWidth - text.Padding.Left - text.Padding.Right + 1,
                $"{scenario}: '{text.Text}' needs {measured.WidthIncludingTrailingWhitespace:F2} DIP but has {text.ActualWidth:F2}.");
        Check(text.ActualHeight > 0, scenario + ": a visible text label must have height.");
    }

    private static bool PixelParticipates(FrameworkElement element)
    {
        for (DependencyObject? node = element; node != null && node is not Window; node = VisualTreeHelper.GetParent(node))
            if (node is UIElement visual && visual.Visibility != Visibility.Visible) return false;
        return true;
    }

    private static RenderTargetBitmap PixelBitmap(FrameworkElement root, int dpi)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * dpi / 96d),
            (int)Math.Ceiling(root.ActualHeight * dpi / 96d), dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(root);
        return bitmap;
    }

    private static void PixelSave(FrameworkElement root, string filename, int dpi)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(PixelBitmap(root, dpi)));
        var directory = Path.Combine(AppContext.BaseDirectory, "artifacts");
        Directory.CreateDirectory(directory);
        using var output = File.Create(Path.Combine(directory, filename));
        encoder.Save(output);
    }

    private sealed class PixelMainData
    {
        public int CurrentTabIndex { get; set; }
        public bool IsNavigationEnabled => true;
        public bool CanConfigureSettings => true;
        public string ActiveHotkeyDescription => "F8";
        public string ExecutionStatus { get; init; } = "";
        public string StatusMessage { get; init; } = "";
        public int AutoClickCount { get; set; }
    }

    private sealed class PixelAutoClickData
    {
        public bool IsNotRunning => true;
        public bool IsLeftButton { get; set; } = true;
        public bool IsMiddleButton { get; set; }
        public bool IsRightButton { get; set; }
        public string IntervalText { get; set; } = "100";
        public string HotkeyHintText { get; init; } = "";
        public string StatusText { get; init; } = "";
    }
}
