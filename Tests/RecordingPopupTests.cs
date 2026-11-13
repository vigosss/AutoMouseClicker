using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using MahApps.Metro.IconPacks;
using Ming_AutoClicker.Models;
using Ming_AutoClicker.Services;
using Ming_AutoClicker.ViewModels;
using Ming_AutoClicker.Views.Recordings;

namespace Ming_AutoClicker.Tests;

internal static partial class Program
{
    // Opt-in desktop check. The normal regression runner must not register this test.
    private static void AddRecordingPopupTests(List<(string Name, Func<Task> Run)> tests) =>
        tests.Add(("Opened recording popup retains white command icons and red delete icon/text across reopening and languages", RecordingOpenedPopup));

    private static async Task RecordingOpenedPopup()
    {
        using var fixture = new RecordingPageFixture();
        using var capture = new ScreenCaptureService();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var originalLanguage = LocalizationService.Current.SelectedLanguage;
        var view = new RecordingPage
        {
            DataContext = fixture.ViewModel,
            Background = (Brush)Application.Current.FindResource("BackgroundBrush")
        };
        var workArea = SystemParameters.WorkArea;
        var host = new Window
        {
            Title = "Recording popup regression",
            Left = workArea.Left + 30,
            Top = workArea.Top + 30,
            Width = Math.Min(760, workArea.Width - 60),
            Height = Math.Min(460, workArea.Height - 60),
            WindowStyle = WindowStyle.ToolWindow,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            FontFamily = (FontFamily)Application.Current.FindResource("PixelFontFamily"),
            Background = (Brush)Application.Current.FindResource("BackgroundBrush"),
            UseLayoutRounding = true,
            SnapsToDevicePixels = true,
            Content = view
        };
        ContextMenu? menu = null;
        try
        {
            LocalizationService.Current.ApplyLanguage(AppLanguage.English);
            host.Show();
            host.Activate();
            await DrainRecordingDispatcher();
            host.UpdateLayout();
            var firstItem = fixture.ViewModel.Recordings[0];
            var more = Descendants<Button>(view).First(button =>
                button.ContextMenu != null && ReferenceEquals(button.DataContext, firstItem));
            menu = more.ContextMenu!;
            var ownerHandle = new WindowInteropHelper(host).Handle;
            Check(ownerHandle != IntPtr.Zero && VisibilityNative.IsWindowVisible(ownerHandle));

            foreach (var language in new[] { AppLanguage.English, AppLanguage.SimplifiedChinese })
            {
                // Keep the same instantiated menu: resource changes while it is closed are a
                // different lifecycle from constructing a fresh menu in an off-screen renderer.
                Check(!menu.IsOpen);
                LocalizationService.Current.ApplyLanguage(language);
                await DrainRecordingDispatcher();
                var locale = language == AppLanguage.English ? "en" : "zh";
                for (var opening = 0; opening < 2; opening++)
                {
                    Check(ReferenceEquals(menu, more.ContextMenu));
                    more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await DrainRecordingDispatcher();
                    await Task.Delay(150, deadline.Token); // Let the actual popup HWND reach the compositor.
                    menu.UpdateLayout();
                    Check(menu.IsOpen && menu.IsVisible, "The More button must open a real, visible context menu.");
                    Check(ReferenceEquals(firstItem, fixture.ViewModel.SelectedRecording));

                    var source = PresentationSource.FromVisual(menu) as HwndSource;
                    Check(source != null && source.Handle != IntPtr.Zero && source.Handle != ownerHandle,
                        "The context menu must be hosted by its own popup HWND.");
                    Check(VisibilityNative.IsWindowVisible(source!.Handle), "The popup HWND must be visible before capture.");
                    VisibilityNative.GetWindowThreadProcessId(source.Handle, out var processId);
                    Equal((uint)Environment.ProcessId, processId);
                    var items = CheckOpenedRecordingPopup(menu, firstItem);

                    // Native menu fade runs on the compositor after dispatcher work completes.
                    // Wait for it before comparing opaque theme colors with screen pixels.
                    await Task.Delay(300, deadline.Token);
                    await DrainRecordingDispatcher();

                    // Crop only this popup's content. Check ownership and native client bounds
                    // before any screen read so unrelated desktop content cannot enter the artifact.
                    var bounds = RecordingPopupScreenBounds(menu);
                    CheckPopupCaptureBounds(source.Handle, bounds);
                    using var pixels = capture.CaptureRegion(bounds.X, bounds.Y, bounds.Width, bounds.Height);
                    var filename = opening == 0 ? $"recording-popup-{locale}.png" : $"recording-popup-{locale}-reopen.png";
                    var artifactDirectory = Path.Combine(AppContext.BaseDirectory, "artifacts");
                    Directory.CreateDirectory(artifactDirectory);
                    pixels.Save(Path.Combine(artifactDirectory, filename));
                    CheckOpenedRecordingPopupPixels(menu, items, bounds, pixels.Data);

                    menu.IsOpen = false;
                    await DrainRecordingDispatcher();
                    Check(!menu.IsOpen, "Closing the popup must allow the same menu to reopen.");
                }
            }
            Equal(0, fixture.Playback.Runs.Count);
            Equal(0, fixture.Capture.Actions.Count);
        }
        finally
        {
            if (menu != null) menu.IsOpen = false;
            view.DataContext = null;
            host.Content = null;
            host.Close();
            LocalizationService.Current.ApplyLanguage(originalLanguage);
        }
    }

    private static MenuItem[] CheckOpenedRecordingPopup(ContextMenu menu, RecordingItemViewModel item)
    {
        var items = menu.Items.OfType<MenuItem>().ToArray();
        var commands = new[] { item.SetHotkeyCommand, item.OptimizeCommand, item.RenameCommand, item.DeleteCommand };
        var headers = new[]
        {
            item.HotkeyText, LocalizationService.Current.GetString("RecordingOptimize"),
            LocalizationService.Current.GetString("RecordingRename"), LocalizationService.Current.GetString("RecordingDelete")
        };
        Equal(commands.Length, items.Length);
        var text = ((SolidColorBrush)Application.Current.FindResource("TextPrimaryBrush")).Color;
        var danger = ((SolidColorBrush)Application.Current.FindResource("DangerBrush")).Color;
        var surface = ((SolidColorBrush)Application.Current.FindResource("SurfaceBrush")).Color;
        var frame = menu.Template.FindName("RecordingMenuFrame", menu) as Border;
        Check(frame != null, "The popup must use the recording menu frame, without the native theme's icon gutter.");
        Equal(surface, ((SolidColorBrush)frame!.Background).Color);
        Equal(surface, ((SolidColorBrush)menu.Background).Color);
        for (var index = 0; index < items.Length; index++)
        {
            Check(items[index].IsVisible && items[index].IsEnabled);
            Check(ReferenceEquals(commands[index], items[index].Command), "Opened menu commands must still target the correct recording.");
            Equal(headers[index], items[index].Header as string);
            Check(items[index].Icon is PackIconPixelartIcons, "Each recording command must have an icon.");
            var icon = (PackIconPixelartIcons)items[index].Icon;
            Check(icon.IsVisible && icon.ActualWidth > 0 && icon.ActualHeight > 0);
            Equal(index == items.Length - 1 ? danger : text, ((SolidColorBrush)icon.Foreground).Color);
            Equal(index == items.Length - 1 ? danger : text, ((SolidColorBrush)items[index].Foreground).Color);
        }
        return items;
    }

    private static void CheckOpenedRecordingPopupPixels(ContextMenu menu, MenuItem[] items,
        System.Drawing.Rectangle captureBounds, byte[,,] pixels)
    {
        var text = ((SolidColorBrush)Application.Current.FindResource("TextPrimaryBrush")).Color;
        var danger = ((SolidColorBrush)Application.Current.FindResource("DangerBrush")).Color;
        var surface = ((SolidColorBrush)Application.Current.FindResource("SurfaceBrush")).Color;
        foreach (var item in items)
        {
            var icon = (PackIconPixelartIcons)item.Icon;
            var iconBounds = RecordingPopupScreenBounds(icon);
            Check(captureBounds.Contains(iconBounds), "The captured popup must contain the entire icon.");
            var relative = new System.Drawing.Rectangle(iconBounds.X - captureBounds.X, iconBounds.Y - captureBounds.Y,
                iconBounds.Width, iconBounds.Height);
            var iconColor = ReferenceEquals(item, items.Last()) ? danger : text;
            Check(CountPopupPixels(pixels, relative, iconColor, 28) >= 5,
                $"The actual popup pixels for '{item.Header}' contain no icon strokes in the expected color {iconColor}.");
        }

        var delete = items.Last();
        var header = Descendants<ContentPresenter>(delete).First(presenter => Equals(presenter.Content, delete.Header));
        var headerBounds = RecordingPopupScreenBounds(header);
        Check(captureBounds.Contains(headerBounds));
        var headerRegion = new System.Drawing.Rectangle(headerBounds.X - captureBounds.X, headerBounds.Y - captureBounds.Y,
            headerBounds.Width, headerBounds.Height);
        Check(CountPopupPixels(pixels, headerRegion, danger, 28) >= 5,
            "The actual delete label must retain the same red warning color as its icon.");
        var wholeMenu = new System.Drawing.Rectangle(0, 0, captureBounds.Width, captureBounds.Height);
        Check(CountPopupPixels(pixels, wholeMenu, surface, 3) > captureBounds.Width * captureBounds.Height / 4,
            "The visible popup must retain the dark surface background.");
        Check(menu.IsOpen, "The menu closed before its rendered pixels were verified.");
    }

    private static System.Drawing.Rectangle RecordingPopupScreenBounds(FrameworkElement element)
    {
        var topLeft = element.PointToScreen(new Point());
        var bottomRight = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
        var left = (int)Math.Ceiling(topLeft.X);
        var top = (int)Math.Ceiling(topLeft.Y);
        var right = (int)Math.Floor(bottomRight.X);
        var bottom = (int)Math.Floor(bottomRight.Y);
        Check(right > left && bottom > top, "The live popup element must have nonempty screen bounds.");
        return System.Drawing.Rectangle.FromLTRB(left, top, right, bottom);
    }

    private static void CheckPopupCaptureBounds(IntPtr popupHandle, System.Drawing.Rectangle captureBounds)
    {
        Check(VisibilityNative.GetClientRect(popupHandle, out var client));
        var origin = new VisibilityNative.NativePoint { X = client.Left, Y = client.Top };
        Check(VisibilityNative.ClientToScreen(popupHandle, ref origin));
        var clientBounds = new System.Drawing.Rectangle(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top);
        Check(clientBounds.Contains(captureBounds), "Screen capture must remain strictly inside this test popup's native client area.");
    }

    private static int CountPopupPixels(byte[,,] pixels, System.Drawing.Rectangle region, Color color, int tolerance)
    {
        var count = 0;
        for (var y = region.Top; y < region.Bottom; y++)
        for (var x = region.Left; x < region.Right; x++)
        {
            if (Math.Abs(pixels[y, x, 0] - color.B) <= tolerance
                && Math.Abs(pixels[y, x, 1] - color.G) <= tolerance
                && Math.Abs(pixels[y, x, 2] - color.R) <= tolerance) count++;
        }
        return count;
    }
}
