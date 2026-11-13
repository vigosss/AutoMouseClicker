using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Ming_AutoClicker.Helpers;
using Ming_AutoClicker.Services;
using Ming_AutoClicker.Views.ImageMatching;

namespace Ming_AutoClicker.Tests;

internal static partial class Program
{
    // Opt-in real desktop tests. The default regression suite never registers these scenarios.
    static partial void AddImageWindowVisibilityTests(List<(string Name, Func<Task> Run)> tests)
    {
        tests.Add(("Visible owned modal editor hides for capture and restores intact", () => ImageWindowVisibility(WindowState.Normal)));
        tests.Add(("Maximized owned modal editor hides for capture and restores intact", () => ImageWindowVisibility(WindowState.Maximized)));
    }

    private static int RunWindowVisibilityTarget()
    {
        var app = new Application();
        var target = new Window
        {
            Title = "Image visibility regression target", Left = 120, Top = 100, Width = 640, Height = 420,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(241, 42, 200))
        };
        var deadline = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        deadline.Tick += (_, _) => { deadline.Stop(); target.Close(); };
        target.Loaded += (_, _) => deadline.Start();
        return app.Run(target);
    }

    private static async Task ImageWindowVisibility(WindowState editorState)
    {
        using var targetProcess = StartVisibilityTarget();
        Window? owner = null;
        Window? editor = null;
        MatchResultWindow? preview = null;
        IDisposable? hiddenWindows = null;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        try
        {
            var targetHandle = await WaitForVisibilityTarget(targetProcess, deadline.Token);
            Check(VisibilityNative.GetClientRect(targetHandle, out var targetBounds));
            var sample = new VisibilityNative.NativePoint { X = (targetBounds.Right - targetBounds.Left) / 2,
                Y = (targetBounds.Bottom - targetBounds.Top) / 2 };
            Check(VisibilityNative.ClientToScreen(targetHandle, ref sample));
            owner = new Window { Title = "Visibility regression owner", Left = 140, Top = 120, Width = 600, Height = 360,
                Background = new SolidColorBrush(Color.FromRgb(30, 60, 90)) };
            Application.Current.MainWindow = owner;
            owner.Show();
            var draft = new TextBox { Text = "Unsaved macro draft — keep this text", Margin = new Thickness(20),
                Height = 36, VerticalAlignment = VerticalAlignment.Top };
            editor = new Window { Title = "Visibility regression modal editor", Owner = owner,
                Left = 180, Top = 160, Width = 520, Height = 280, WindowState = editorState,
                Background = new SolidColorBrush(Color.FromRgb(24, 54, 95)), Content = draft };
            var originalDraft = draft.Text;
            var exercise = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var modalReturned = false;
            editor.Loaded += async (_, _) =>
            {
                try
                {
                    editor.Activate();
                    await Task.Delay(200, deadline.Token);
                    var ownerHandle = new WindowInteropHelper(owner).Handle;
                    var editorHandle = new WindowInteropHelper(editor).Handle;
                    Check(VisibilityNative.GetForegroundWindow() == editorHandle, "The owned editor must be the active window before hiding.");
                    Check(VisibilityNative.IsWindowVisible(ownerHandle) && VisibilityNative.IsWindowVisible(editorHandle));
                    Check(!VisibilityNative.IsWindowEnabled(ownerHandle), "ShowDialog must disable its owner.");
                    Check(VisibilityNative.IsWindowVisible(targetHandle));
                    using var capture = new ScreenCaptureService();
                    Check(!IsTargetPixel(capture, sample), "The editor must cover the external target before the capture scope starts.");

                    // Exercise the same foreground-based selection used by the editor command.
                    hiddenWindows = CaptureWindowVisibilityScope.HideVisibleWindows();
                    await Task.Delay(200, deadline.Token); // Allow DWM to composite the newly exposed target.
                    Check(!VisibilityNative.IsWindowVisible(editorHandle), "The modal editor HWND must be hidden.");
                    Check(!VisibilityNative.IsWindowVisible(ownerHandle), "The owner HWND must also be hidden.");
                    Check(VisibilityNative.IsWindowVisible(targetHandle), "The independent target process must remain visible.");
                    Check(!VisibilityNative.IsWindowEnabled(ownerHandle), "Hiding must not end the owner's modal disabled state.");
                    Check(!modalReturned, "Native capture hiding must not exit ShowDialog.");
                    Check(IsTargetPixel(capture, sample), "Real screen capture still contains the editor instead of the target background.");

                    preview = new MatchResultWindow();
                    using var cancelled = new CancellationTokenSource();
                    var cancelCount = 0;
                    preview.CancellationRequested += (_, _) => { cancelCount++; cancelled.Cancel(); };
                    var (x, y, width, height) = Win32Api.GetVirtualScreenBounds();
                    preview.ShowCapturedFrame(ImagePreviewSnapshot(30), new System.Drawing.Rectangle(x, y, width, height));
                    preview.Show();
                    await Task.Delay(100, deadline.Token);
                    preview.UpdateLayout();
                    var previewHandle = new WindowInteropHelper(preview).Handle;
                    Check(VisibilityNative.IsWindowVisible(previewHandle) && VisibilityNative.IsWindowEnabled(previewHandle),
                        "The pending preview must be a visible, enabled native window.");
                    var stop = (Button)preview.FindName("StopTestButton");
                    Check(stop.IsVisible && stop.IsEnabled && stop.ActualWidth > 0 && stop.ActualHeight > 0);
                    var center = stop.TranslatePoint(new Point(stop.ActualWidth / 2, stop.ActualHeight / 2), preview);
                    var hit = preview.InputHitTest(center) as DependencyObject;
                    while (hit != null && !ReferenceEquals(hit, stop)) hit = VisualTreeHelper.GetParent(hit);
                    Check(ReferenceEquals(hit, stop), "The visible stop button must receive real WPF hit testing, without a covering overlay.");
                    stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(cancelled.IsCancellationRequested && preview.IsClosed);
                    Equal(1, cancelCount);

                    hiddenWindows.Dispose(); hiddenWindows = null;
                    await Task.Delay(200, deadline.Token);
                    Check(VisibilityNative.IsWindowVisible(ownerHandle) && VisibilityNative.IsWindowVisible(editorHandle),
                        "Both owner and modal editor must be restored.");
                    Check(!VisibilityNative.IsWindowEnabled(ownerHandle), "The restored owner must still be disabled by ShowDialog.");
                    Check(VisibilityNative.GetForegroundWindow() == editorHandle, "The previously active editor must regain activation.");
                    Check(!modalReturned, "The editor must remain in its original modal session after preview cancellation.");
                    Equal(editorState, editor.WindowState);
                    Equal(editorState == WindowState.Maximized, VisibilityNative.IsZoomed(editorHandle));
                    Equal(originalDraft, draft.Text);
                    exercise.TrySetResult();
                }
                catch (Exception ex) { exercise.TrySetException(ex); }
                finally
                {
                    if (preview is { IsClosed: false }) preview.Close();
                    hiddenWindows?.Dispose(); hiddenWindows = null;
                    editor.Close();
                }
            };
            editor.ShowDialog();
            modalReturned = true;
            await exercise.Task.WaitAsync(deadline.Token);
        }
        finally
        {
            if (preview is { IsClosed: false }) preview.Close();
            hiddenWindows?.Dispose();
            editor?.Close();
            owner?.Close();
            if (!targetProcess.HasExited)
            {
                targetProcess.CloseMainWindow();
                try { await targetProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
                catch (TimeoutException) { if (!targetProcess.HasExited) targetProcess.Kill(); }
            }
        }
    }

    private static Process StartVisibilityTarget()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the regression executable.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        if (string.Equals(System.IO.Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--window-visibility-target");
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start the independent target window.");
    }

    private static async Task<IntPtr> WaitForVisibilityTarget(Process process, CancellationToken cancellationToken)
    {
        while (!process.HasExited)
        {
            var found = IntPtr.Zero;
            VisibilityNative.EnumWindows((handle, _) =>
            {
                VisibilityNative.GetWindowThreadProcessId(handle, out var processId);
                if (processId != process.Id || !VisibilityNative.IsWindowVisible(handle)) return true;
                found = handle; return false;
            }, IntPtr.Zero);
            if (found != IntPtr.Zero) return found;
            await Task.Delay(25, cancellationToken);
        }
        throw new InvalidOperationException("The independent target process exited before showing its window.");
    }

    private static bool IsTargetPixel(ScreenCaptureService capture, VisibilityNative.NativePoint point)
    {
        using var pixel = capture.CaptureRegion(point.X, point.Y, 1, 1);
        var data = pixel.Data;
        return Math.Abs(data[0, 0, 2] - 241) <= 2 && Math.Abs(data[0, 0, 1] - 42) <= 2 && Math.Abs(data[0, 0, 0] - 200) <= 2;
    }

    private static class VisibilityNative
    {
        internal delegate bool EnumWindowProc(IntPtr handle, IntPtr parameter);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool EnumWindows(EnumWindowProc callback, IntPtr parameter);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(IntPtr handle);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowEnabled(IntPtr handle);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsZoomed(IntPtr handle);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetClientRect(IntPtr handle, out NativeRect rectangle);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ClientToScreen(IntPtr handle, ref NativePoint point);
        [StructLayout(LayoutKind.Sequential)] internal struct NativePoint { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
    }
}
