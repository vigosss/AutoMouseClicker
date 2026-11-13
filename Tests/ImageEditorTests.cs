using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ming_AutoClicker.Models;
using Ming_AutoClicker.Services;
using Ming_AutoClicker.Views.ImageMatching;
using Ming_AutoClicker.Views.Macros;

namespace Ming_AutoClicker.Tests;

internal static partial class Program
{
    static partial void AddImageEditorTests(List<(string Name, Func<Task> Run)> tests)
    {
        tests.Add(("Image editor defaults and legacy fields keep their semantics", ImageEditorDefaultsAndLegacy));
        tests.Add(("Invalid image editor drafts block save across selection changes", ImageEditorInvalidDrafts));
        tests.Add(("Image replacement resets metadata and alternate removal remains isolated", ImageEditorTemplateChanges));
        tests.Add(("Template point selection respects letterboxing", ImagePreviewCoordinates));
        tests.Add(("Template editor drafts never change the original on cancel", ImageTemplateDraftIsolation));
        tests.Add(("Image editor renders basic and advanced settings in both languages", ImageEditorRendering));
    }

    private static Task ImageEditorDefaultsAndLegacy()
    {
        using var fixture = new EditorFixture();
        var vm = fixture.ViewModel;
        vm.AddFindImageActionCommand.Execute(null);
        var current = (FindImageAction)vm.SelectedAction!;
        Equal(30000, current.TimeoutMs!.Value);
        Equal("Click", current.Operation);
        Equal(0.5, current.ClickPoint!.X);
        Equal(0.5, current.ClickPoint.Y);
        Equal(ImageSelectionMode.Unique, current.SelectionMode);
        Equal("30", vm.ImageTimeoutSecondsText);
        vm.ImagePath = "current.png";

        var legacy = new FindImageAction { ImagePath = "legacy.png", WaitUntilFound = true, OffsetX = 17, OffsetY = -8 };
        vm.Actions.Add(legacy); vm.SelectedAction = legacy;
        var view = new MacroEditorView { DataContext = vm };
        Layout(view, 700, 500);
        Equal("30", vm.ImageTimeoutSecondsText);
        Check(vm.UsesLegacyImageOffset);
        Check(legacy.TimeoutMs == null && legacy.ClickPoint == null);
        var saves = 0; vm.SaveCompleted += (_, _) => saves++;
        vm.SaveCommand.Execute(null);
        Equal(1, saves);
        Check(legacy.TimeoutMs == null && legacy.ClickPoint == null);
        Equal(17, legacy.OffsetX); Equal(-8, legacy.OffsetY);
        vm.SetImageClickPoint(new() { X = .25, Y = .75 });
        Check(!vm.UsesLegacyImageOffset);
        Equal(.25, legacy.ClickPoint!.X); Equal(.75, legacy.ClickPoint.Y);
        Equal(17, legacy.OffsetX); // Retained for compatibility; the new point selects the new semantics.
        view.DataContext = null;
        return Task.CompletedTask;
    }

    private static Task ImageEditorInvalidDrafts()
    {
        using var fixture = new EditorFixture();
        var vm = fixture.ViewModel;
        vm.AddFindImageActionCommand.Execute(null); vm.ImagePath = "sample.png";
        var action = vm.SelectedAction!;
        var saves = 0; vm.SaveCompleted += (_, _) => saves++;
        foreach (var value in new[] { "", "-1", "NaN", "Infinity", "abc", "9999999999999" })
        {
            vm.ImageTimeoutSecondsText = value;
            vm.SaveCommand.Execute(null);
            Equal(0, saves);
        }
        vm.AddWaitActionCommand.Execute(null);
        vm.SaveCommand.Execute(null);
        Check(ReferenceEquals(action, vm.SelectedAction));
        Equal("9999999999999", vm.ImageTimeoutSecondsText);
        vm.ImageTimeoutSecondsText = "0";
        vm.ImageSelection = ImageSelectionMode.Ordered;
        foreach (var value in new[] { "", "0", "-1", "1.5", "abc" })
        {
            vm.ImageMatchIndexText = value; vm.SaveCommand.Execute(null); Equal(0, saves);
        }
        vm.ImageMatchIndexText = "2"; vm.SaveCommand.Execute(null); Equal(1, saves);
        Equal(0, ((FindImageAction)action).TimeoutMs!.Value);
        Equal(2, ((FindImageAction)action).MatchIndex);
        vm.ImageScopeKind = ImageSearchScopeKind.Region;
        vm.SaveCommand.Execute(null); Equal(1, saves);
        vm.ImageScopeKind = ImageSearchScopeKind.Window;
        vm.SaveCommand.Execute(null); Equal(1, saves);
        vm.ImageScopeKind = ImageSearchScopeKind.Desktop;
        vm.SaveCommand.Execute(null); Equal(2, saves);
        return Task.CompletedTask;
    }

    private static Task ImageEditorTemplateChanges()
    {
        using var fixture = new EditorFixture();
        var vm = fixture.ViewModel;
        vm.AddFindImageActionCommand.Execute(null);
        var action = (FindImageAction)vm.SelectedAction!;
        vm.ImagePath = "first.png";
        action.CaptureDpi = 144;
        action.ClickPoint = new() { X = .1, Y = .9 };
        action.IgnoredRegions.Add(new() { X = .1, Y = .1, Width = .2, Height = .2 });
        var alternate = new ImageTemplateVariant { ImagePath = "alternate.png", ClickPoint = new() { X = .8, Y = .2 } };
        action.AlternateTemplates.Add(alternate);
        vm.ImagePath = "replacement.png";
        Check(action.CaptureDpi == null);
        Equal(0, action.IgnoredRegions.Count);
        Equal(.5, action.ClickPoint!.X);
        Equal(1, action.AlternateTemplates.Count);
        Equal(.8, alternate.ClickPoint!.X);
        vm.SelectedAction = null;
        vm.SelectedAction = action;
        var view = new MacroEditorView { DataContext = vm };
        Layout(view, 700, 500);
        var alternateList = (ListBox)view.FindName("AlternateTemplatesList");
        Equal(1, alternateList.Items.Count);
        vm.SelectedAlternateTemplate = alternate;
        Check(vm.RemoveAlternateTemplateCommand.CanExecute(null));
        vm.RemoveAlternateTemplateCommand.Execute(null);
        Equal(0, action.AlternateTemplates.Count);
        Layout(view, 700, 500);
        Equal(0, alternateList.Items.Count);
        Check(vm.SelectedAlternateTemplate == null);
        Equal("replacement.png", action.ImagePath);
        view.DataContext = null;
        return Task.CompletedTask;
    }

    private static Task ImagePreviewCoordinates()
    {
        var pixels = new byte[100 * 100 * 4];
        var image = BitmapSource.Create(100, 100, 96, 96, PixelFormats.Bgra32, null, pixels, 400);
        var preview = new TemplatePreviewControl { Source = image, Width = 300, Height = 150, ClickPoint = new() };
        Layout(preview, 300, 150);
        Equal(75d, preview.ImageRectangle.X);
        Check(!preview.TryGetImagePoint(new Point(20, 75), out _));
        Check(preview.TryGetImagePoint(new Point(112.5, 37.5), out var point));
        Equal(.25, point.X); Equal(.25, point.Y);
        Check(preview.TryGetImagePoint(new Point(225, 150), out point));
        Equal(1d, point.X); Equal(1d, point.Y);
        return Task.CompletedTask;
    }

    private static Task ImageTemplateDraftIsolation()
    {
        var path = CreateEditorTemplateImage();
        var original = new ImageTemplateVariant { ImagePath = path, ClickPoint = new() { X = .2, Y = .7 },
            IgnoredRegions = new() { new() { X = .1, Y = .2, Width = .3, Height = .2 } }, CaptureDpi = 144 };
        var editor = new TemplateEditWindow(original, Path.GetDirectoryName(path)!);
        editor.EditedTemplate.ClickPoint!.X = .9;
        editor.EditedTemplate.IgnoredRegions[0].Width = .1;
        Equal(.2, original.ClickPoint.X);
        Equal(.3, original.IgnoredRegions[0].Width);
        Equal(144d, original.CaptureDpi!.Value);
        editor.Close();
        return Task.CompletedTask;
    }

    private static Task ImageEditorRendering()
    {
        using var fixture = new EditorFixture();
        var vm = fixture.ViewModel;
        vm.AddFindImageActionCommand.Execute(null); vm.ImagePath = CreateEditorTemplateImage();
        var action = (FindImageAction)vm.SelectedAction!;
        action.AlternateTemplates.Add(new() { ImagePath = action.ImagePath, ClickPoint = new() });
        action.SearchScope = new() { Kind = ImageSearchScopeKind.Window, WindowTitle = "Sample application",
            WindowProcessName = "sample", WindowClassName = "SampleWindow" };
        action.SelectionMode = ImageSelectionMode.Ordered;
        vm.SelectedAction = null; vm.SelectedAction = action;
        var view = new MacroEditorView { DataContext = vm, Background = (Brush)Application.Current.FindResource("BackgroundBrush") };
        var host = new Window { Content = view };
        var scroll = (ScrollViewer)view.FindName("ActionPropertiesScrollViewer");
        foreach (var language in new[] { AppLanguage.English, AppLanguage.SimplifiedChinese })
        {
            LocalizationService.Current.ApplyLanguage(language);
            view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var locale = language == AppLanguage.English ? "en" : "zh";
            vm.ImageAdvancedExpanded = false;
            scroll.ScrollToTop(); Layout(view, 700, 500);
            Equal(Visibility.Visible, ((StackPanel)view.FindName("FindImagePanel")).Visibility);
            Check(!Descendants<CheckBox>(view).Any(c => Equals(c.Content, LocalizationService.Current.GetString("EditorAdaptiveScale"))));
            Render(view, $"editor-image-{locale}-basic-minimum.png", 700, 500);
            scroll.ScrollToTop(); Layout(view, 800, 600);
            AssertImageControlInViewport(scroll, (FrameworkElement)view.FindName("MainTemplatePreview"));
            Render(view, $"editor-image-{locale}-basic.png", 800, 600);
            ScrollImageEditorTo(view, scroll, (FrameworkElement)view.FindName("ImageTimeoutBox"));
            AssertImageControlInViewport(scroll, (FrameworkElement)view.FindName("ImageTimeoutBox"));
            Render(view, $"editor-image-{locale}-basic-settings.png", 800, 600);

            vm.ImageAdvancedExpanded = true; Layout(view, 800, 600);
            Check(((Expander)view.FindName("ImageAdvancedExpander")).IsExpanded);
            ScrollImageEditorTo(view, scroll, (FrameworkElement)view.FindName("ImageEditTemplateButton"));
            AssertImageControlInViewport(scroll, (FrameworkElement)view.FindName("ImageEditTemplateButton"));
            AssertImageControlInViewport(scroll, (FrameworkElement)view.FindName("ImageThresholdSlider"));
            Render(view, $"editor-image-{locale}-advanced-top.png", 800, 600);

            ScrollImageEditorTo(view, scroll, (FrameworkElement)view.FindName("ImageScopeCombo"));
            foreach (var name in new[] { "ImageScopeCombo", "ImageChooseWindowButton", "ImageSelectionCombo", "ImageMatchIndexBox" })
                AssertImageControlInViewport(scroll, (FrameworkElement)view.FindName(name));
            Render(view, $"editor-image-{locale}-advanced-scope.png", 800, 600);

            ScrollImageEditorTo(view, scroll, (FrameworkElement)view.FindName("AlternateTemplatesList"));
            AssertImageControlInViewport(scroll, (FrameworkElement)view.FindName("AlternateTemplatesList"));
            AssertImageControlInViewport(scroll, (FrameworkElement)view.FindName("ImageRemoveAlternateButton"));
            Render(view, $"editor-image-{locale}-advanced.png", 800, 600);

            var notePanel = (FrameworkElement)view.FindName("ActionNotePanel");
            var noteBox = (TextBox)view.FindName("ActionNoteBox");
            var findPanel = (FrameworkElement)view.FindName("FindImagePanel");
            var viewport = ImageEditorViewport(scroll);
            var propertiesBounds = findPanel.TransformToAncestor(viewport).TransformBounds(new Rect(findPanel.RenderSize));
            var noteBounds = notePanel.TransformToAncestor(viewport).TransformBounds(new Rect(notePanel.RenderSize));
            Check(noteBounds.Top >= propertiesBounds.Bottom + 15,
                "Step notes must remain below the expanded action properties.");
            ScrollImageEditorTo(view, scroll, notePanel);
            AssertImageControlInViewport(scroll, notePanel);
            AssertImageControlInViewport(scroll, noteBox);
            var originalNote = vm.ActionNote;
            noteBox.SetCurrentValue(TextBox.TextProperty,
                language == AppLanguage.English ? "Open the selected item" : "打开选中的项目");
            noteBox.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Equal(noteBox.Text, vm.ActionNote);
            Render(view, $"editor-image-{locale}-step-note.png", 800, 600);
            vm.ActionNote = originalNote;
        }
        host.Content = null; view.DataContext = null; host.Close();
        LocalizationService.Current.ApplyLanguage(AppLanguage.English);
        return Task.CompletedTask;
    }

    private static ScrollContentPresenter ImageEditorViewport(ScrollViewer scroll) =>
        Descendants<ScrollContentPresenter>(scroll).First(p => ReferenceEquals(p.TemplatedParent, scroll));

    private static void ScrollImageEditorTo(MacroEditorView view, ScrollViewer scroll, FrameworkElement control)
    {
        var viewport = ImageEditorViewport(scroll);
        var bounds = control.TransformToAncestor(viewport).TransformBounds(new Rect(control.RenderSize));
        scroll.ScrollToVerticalOffset(scroll.VerticalOffset + bounds.Top - 8);
        Layout(view, 800, 600);
    }

    private static void AssertImageControlInViewport(ScrollViewer scroll, FrameworkElement control)
    {
        var viewport = ImageEditorViewport(scroll);
        var bounds = control.TransformToAncestor(viewport).TransformBounds(new Rect(control.RenderSize));
        Check(control.ActualWidth > 0 && control.ActualHeight > 0, $"{control.Name} is not laid out");
        Check(bounds.Left >= -1 && bounds.Right <= viewport.ActualWidth + 1,
            $"{control.Name} is horizontally clipped: {bounds}, viewport {viewport.ActualWidth}×{viewport.ActualHeight}");
        Check(bounds.Top >= -1 && bounds.Bottom <= viewport.ActualHeight + 1,
            $"{control.Name} is vertically clipped: {bounds}, viewport {viewport.ActualWidth}×{viewport.ActualHeight}");
    }

    private static string CreateEditorTemplateImage()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "artifacts");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "editor-template.png");
        var pixels = new byte[96 * 64 * 4];
        for (var y = 0; y < 64; y++)
            for (var x = 0; x < 96; x++)
            {
                var offset = (y * 96 + x) * 4;
                pixels[offset] = (byte)(x * 2); pixels[offset + 1] = (byte)(y * 3);
                pixels[offset + 2] = 180; pixels[offset + 3] = 255;
            }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(96, 64, 96, 96, PixelFormats.Bgra32, null, pixels, 96 * 4)));
        using var stream = File.Create(path); encoder.Save(stream);
        return path;
    }
}
