using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ming_AutoClicker.Models;
using Ming_AutoClicker.Services;
using Ming_AutoClicker.Views.Macros;

namespace Ming_AutoClicker.Tests;

internal static partial class Program
{
    private static void AddInputLayoutTests(List<(string Name, Func<Task> Run)> tests) =>
        tests.Add(("Localized editor loop controls fit before focus and after caret changes at common sizes and DPIs", InputLayout));

    private static Task InputLayout()
    {
        var failures = new List<string>();
        var originalLanguage = LocalizationService.Current.SelectedLanguage;
        try
        {
            foreach (var language in new[] { AppLanguage.English, AppLanguage.SimplifiedChinese })
            foreach (var width in new[] { 700, 800 })
            foreach (var dpi in new[] { 96, 120, 144, 192 })
            {
                LocalizationService.Current.ApplyLanguage(language);
                var locale = language == AppLanguage.English ? "en" : "zh";
                var scenario = $"{locale} {width} DIP {dpi} DPI";
                using var fixture = new EditorFixture();
                fixture.ViewModel.LoopEnabled = false;
                fixture.ViewModel.LoopCount = 0;
                fixture.ViewModel.LoopIntervalMs = 1000;
                var view = new MacroEditorView
                {
                    DataContext = fixture.ViewModel,
                    Background = (Brush)Application.Current.FindResource("BackgroundBrush"),
                    UseLayoutRounding = true,
                    SnapsToDevicePixels = true
                };
                Layout(view, width, 560);
                view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                // Materialize templates first so the DPI change reaches their generated
                // children as well as the editor root before it is measured again.
                VisualTreeHelper.SetRootDpi(view, new DpiScale(dpi / 96d, dpi / 96d));
                foreach (var element in Descendants<UIElement>(view))
                {
                    element.InvalidateMeasure();
                    element.InvalidateArrange();
                }
                view.InvalidateMeasure();
                view.InvalidateArrange();
                Layout(view, width, 560);
                CheckInputHeaderFits(view, scenario + " loop disabled", failures);
                var initialHeaderHeight = ((Border)view.FindName("EditorHeaderFrame")).ActualHeight;
                SaveInputLayout(view, locale + "-loop-off", width, dpi);
                fixture.ViewModel.LoopEnabled = true;
                Layout(view, width, 560);
                Equal(initialHeaderHeight, ((Border)view.FindName("EditorHeaderFrame")).ActualHeight);
                var boxes = Descendants<TextBox>(view).Where(box =>
                    BindingOperations.GetBinding(box, TextBox.TextProperty)?.Path.Path is "LoopCount" or "LoopIntervalMs").ToArray();
                Equal(2, boxes.Length);
                CheckInputHeaderFits(view, scenario, failures);
                SaveInputLayout(view, locale, width, dpi);
                foreach (var box in boxes)
                {
                    var label = $"{scenario} {box.Text}";
                    Equal(dpi / 96d, VisualTreeHelper.GetDpi(box).PixelsPerDip);
                    Check(!box.IsKeyboardFocused, "Initial text must be checked before keyboard focus.");
                    CheckInputValueFits(box, label + " initial", failures);
                    foreach (var caretIndex in new[] { box.Text.Length, 0 })
                    {
                        // Exercise caret-driven scrolling without opening a window or injecting input.
                        box.CaretIndex = caretIndex;
                        view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                        Layout(view, width, 560);
                        CheckInputValueFits(box, label + $" caret {caretIndex}", failures);
                    }
                }
                view.DataContext = null;
            }
        }
        finally { LocalizationService.Current.ApplyLanguage(originalLanguage); }
        Check(failures.Count == 0, string.Join(Environment.NewLine, failures));
        return Task.CompletedTask;
    }

    private static void CheckInputHeaderFits(MacroEditorView view, string scenario, List<string> failures)
    {
        var options = (StackPanel)view.FindName("EditorLoopOptions");
        var header = (Grid)view.FindName("EditorHeaderGrid");
        var frame = (Border)view.FindName("EditorHeaderFrame");
        var dpi = VisualTreeHelper.GetDpi(frame);
        var inset = new Thickness(frame.Padding.Left + Math.Round(frame.BorderThickness.Left * dpi.DpiScaleX) / dpi.DpiScaleX,
            frame.Padding.Top + Math.Round(frame.BorderThickness.Top * dpi.DpiScaleY) / dpi.DpiScaleY,
            frame.Padding.Right + Math.Round(frame.BorderThickness.Right * dpi.DpiScaleX) / dpi.DpiScaleX,
            frame.Padding.Bottom + Math.Round(frame.BorderThickness.Bottom * dpi.DpiScaleY) / dpi.DpiScaleY);
        var headerBounds = frame.TransformToAncestor(view).TransformBounds(new Rect(inset.Left, inset.Top,
            frame.ActualWidth - inset.Left - inset.Right, frame.ActualHeight - inset.Top - inset.Bottom));
        var controls = header.Children.OfType<FrameworkElement>()
            .Where(control => control.Visibility == Visibility.Visible)
            .SelectMany(control => ReferenceEquals(control, options)
                ? options.Children.OfType<FrameworkElement>() : new[] { control });
        var rowCenter = headerBounds.Top + headerBounds.Height / 2;
        var right = headerBounds.Left;
        foreach (var control in controls)
        {
                var bounds = control.TransformToAncestor(view).TransformBounds(new Rect(control.RenderSize));
                var label = control is TextBlock text ? text.Text : control.GetType().Name;
                if (Math.Abs(bounds.Top + bounds.Height / 2 - rowCenter) > 1)
                    failures.Add($"{scenario}: header '{label}' must stay on the single header row.");
                if (bounds.Left < headerBounds.Left - .5 || bounds.Right > Math.Min(view.ActualWidth, headerBounds.Right) + .5
                    || bounds.Top < headerBounds.Top - .5 || bounds.Bottom > headerBounds.Bottom + .5)
                    failures.Add($"{scenario}: header '{label}' exceeds its visible content bounds {headerBounds}: {bounds}.");
                if (bounds.Left < right - .5)
                    failures.Add($"{scenario}: header '{label}' overlaps the preceding control: left {bounds.Left:F2}, preceding right {right:F2}.");
                right = bounds.Right;
        }
        Check(frame.ActualHeight <= 64.5, "The editor header must retain its compact single-row height.");
    }

    private static void CheckInputValueFits(TextBox box, string label, List<string> failures)
    {
        var host = (ScrollViewer)box.Template.FindName("PART_ContentHost", box);
        var start = box.GetRectFromCharacterIndex(0, false);
        var end = box.GetRectFromCharacterIndex(box.Text.Length - 1, true);
        var details = $"{label}: width={box.ActualWidth:F2}, margin={host.Margin}, padding={host.Padding}, " +
            $"extent={box.ExtentWidth:F2}, viewport={box.ViewportWidth:F2}, offset={box.HorizontalOffset:F2}, " +
            $"characters={start.Left:F2}..{end.Right:F2}";
        if (box.ExtentWidth > box.ViewportWidth + .5 || box.HorizontalOffset > .5)
            failures.Add("The full numeric value should fit without horizontal scrolling. " + details);
        if (start.IsEmpty || end.IsEmpty)
            failures.Add("Numeric glyphs should have layout before focus. " + details);
    }

    private static void SaveInputLayout(MacroEditorView view, string locale, int width, int dpi)
    {
        var bitmap = new RenderTargetBitmap((int)(width * dpi / 96d), (int)(560 * dpi / 96d), dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.Combine(AppContext.BaseDirectory, "artifacts");
        Directory.CreateDirectory(directory);
        using var output = File.Create(Path.Combine(directory, $"editor-input-initial-{locale}-{width}-{dpi}dpi.png"));
        encoder.Save(output);
    }
}
