using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Ming_AutoClicker.Helpers;

/// <summary>
/// Temporarily removes the application's currently visible HWNDs from screen captures without
/// changing WPF Visibility, WindowState, IsEnabled or the lifetime of a ShowDialog modal loop.
/// Create and dispose this scope on the application's UI thread. Keep it alive until the capture
/// preview closes, so the editor cannot flash back over the target between capture and preview.
/// </summary>
public sealed class CaptureWindowVisibilityScope : IDisposable
{
    private readonly Application _application;
    private readonly Dispatcher _dispatcher;
    private readonly List<WindowEntry> _windows;
    private readonly WindowEntry? _activeWindow;
    private readonly IntPtr _focusedHandle;
    private bool _applicationExiting;
    private bool _disposed;

    private CaptureWindowVisibilityScope(Application application, List<WindowEntry> windows,
        WindowEntry? activeWindow, IntPtr focusedHandle)
    {
        _application = application;
        _dispatcher = application.Dispatcher;
        _windows = windows;
        _activeWindow = activeWindow;
        _focusedHandle = focusedHandle;
        _application.Exit += OnApplicationExit;
    }

    /// <summary>
    /// Takes a fixed snapshot of Application.Current.Windows, hiding owned windows before their
    /// owners. A window with no HWND, or an HWND that was already hidden, is never included.
    /// If any still-live window cannot be hidden, restores every attempted window before throwing.
    /// </summary>
    public static CaptureWindowVisibilityScope HideVisibleWindows(Window? preferredActiveWindow = null)
    {
        var application = Application.Current ?? throw new InvalidOperationException("A WPF application is required.");
        application.Dispatcher.VerifyAccess();
        if (application.Dispatcher.HasShutdownStarted || application.Dispatcher.HasShutdownFinished)
            throw new InvalidOperationException("The application is shutting down.");

        var foreground = Native.GetForegroundWindow();
        var active = Native.GetActiveWindow();
        var focused = Native.GetFocus();
        var entries = new List<WindowEntry>();
        // Do not enumerate the live WindowCollection while changing native visibility: window
        // messages can cause reentrant application code to create or close other windows.
        foreach (var window in application.Windows.Cast<Window>().ToArray())
        {
            window.Dispatcher.VerifyAccess();
            // Handle deliberately does not call EnsureHandle: a newly constructed preview must
            // remain absent from this scope and must not acquire an HWND just for enumeration.
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero || !Native.IsWindow(handle) || !Native.IsWindowVisible(handle)) continue;
            var source = HwndSource.FromHwnd(handle);
            if (source == null) throw new InvalidOperationException("A visible application window has no presentation source.");
            entries.Add(new WindowEntry(window, source, handle, GetOwnerDepth(handle), entries.Count));
        }

        var preferred = entries.FirstOrDefault(entry => ReferenceEquals(entry.Window, preferredActiveWindow));
        var focusTarget = preferred ?? entries.FirstOrDefault(entry => entry.Handle == foreground) ??
            entries.FirstOrDefault(entry => entry.Handle == active);
        // Descendants are hidden first. Reversing this exact ordering restores owners first and
        // leaves the existing native z-order intact for otherwise unrelated application windows.
        entries = entries.OrderByDescending(entry => entry.OwnerDepth).ThenByDescending(entry => entry.Order).ToList();
        var scope = new CaptureWindowVisibilityScope(application, entries, focusTarget, focused);
        try
        {
            scope.HideCore();
            return scope;
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    private void HideCore()
    {
        foreach (var entry in _windows)
        {
            if (!IsSameWindow(entry)) continue;
            // SetWindowPos can send synchronous window messages even on failure. Record the
            // attempt first so rollback also covers a partially completed native visibility change.
            entry.HideAttempted = true;
            var succeeded = Native.SetWindowPos(entry.Handle, IntPtr.Zero, 0, 0, 0, 0,
                Native.PreserveWindowFlags | Native.SWP_HIDEWINDOW);
            var error = Marshal.GetLastWin32Error();
            if (IsSameWindow(entry) && (!succeeded || Native.IsWindowVisible(entry.Handle)))
                throw new Win32Exception(error, "Unable to hide an application window for screen capture.");
        }
        // A later owner's window message could have re-shown an earlier owned window. Verify
        // the entire original snapshot before allowing the caller to capture any pixels.
        foreach (var entry in _windows)
            if (IsSameWindow(entry) && Native.IsWindowVisible(entry.Handle))
                throw new InvalidOperationException("An application window remained visible during screen capture preparation.");
    }

    /// <summary>
    /// Best-effort restoration. Attempts every still-live original window even if another native
    /// operation fails; cleanup errors are diagnostic only so caller state cleanup can always run.
    /// Closed windows, recycled HWNDs and application shutdown are never restored.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        if (!_dispatcher.CheckAccess())
        {
            if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) return;
            try { _dispatcher.Invoke(Dispose); }
            catch (Exception ex) { Debug.WriteLine($"Unable to restore capture window visibility on the UI thread: {ex}"); }
            return;
        }

        _disposed = true;
        _application.Exit -= OnApplicationExit;
        try
        {
            if (_applicationExiting || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) return;
            for (var index = _windows.Count - 1; index >= 0; index--)
            {
                var entry = _windows[index];
                try
                {
                    if (!entry.HideAttempted || !IsSameWindow(entry) || Native.IsWindowVisible(entry.Handle)) continue;
                    var succeeded = Native.SetWindowPos(entry.Handle, IntPtr.Zero, 0, 0, 0, 0,
                        Native.PreserveWindowFlags | Native.SWP_SHOWWINDOW);
                    if (IsSameWindow(entry) && (!succeeded || !Native.IsWindowVisible(entry.Handle)))
                    {
                        // SW_SHOWNA retains the current normal/maximized/minimized state and
                        // does not activate the owner while its modal child is still hidden.
                        Native.ShowWindow(entry.Handle, Native.SW_SHOWNA);
                        if (IsSameWindow(entry) && !Native.IsWindowVisible(entry.Handle))
                            Debug.WriteLine($"Unable to restore capture window visibility for HWND {entry.Handle}.");
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"Unable to restore capture window visibility: {ex}"); }
            }
            RestoreFocus();
        }
        catch (Exception ex) { Debug.WriteLine($"Unable to complete capture window restoration: {ex}"); }
        finally { _windows.Clear(); }
    }

    private void RestoreFocus()
    {
        if (_applicationExiting || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) return;
        var target = _activeWindow;
        if (target == null || !IsSameWindow(target) || !Native.IsWindowVisible(target.Handle) ||
            !Native.IsWindowEnabled(target.Handle) || Native.IsIconic(target.Handle)) return;

        // These calls never enable the disabled modal owner or restore a minimized window.
        // Windows may deny foreground activation; preserving the native modal state takes priority.
        Native.SetForegroundWindow(target.Handle);
        Native.SetActiveWindow(target.Handle);
        if (_focusedHandle != IntPtr.Zero && Native.IsWindow(_focusedHandle) &&
            Native.IsWindowVisible(_focusedHandle) && Native.IsWindowEnabled(_focusedHandle) &&
            (_focusedHandle == target.Handle || Native.IsChild(target.Handle, _focusedHandle)))
            Native.SetFocus(_focusedHandle);
    }

    private static bool IsSameWindow(WindowEntry entry) =>
        Native.IsWindow(entry.Handle) && ReferenceEquals(HwndSource.FromHwnd(entry.Handle), entry.Source);

    private static int GetOwnerDepth(IntPtr handle)
    {
        var seen = new HashSet<IntPtr> { handle };
        var depth = 0;
        for (var owner = Native.GetWindow(handle, Native.GW_OWNER);
             owner != IntPtr.Zero && seen.Add(owner); owner = Native.GetWindow(owner, Native.GW_OWNER)) depth++;
        return depth;
    }

    private void OnApplicationExit(object sender, ExitEventArgs e) => _applicationExiting = true;

    private sealed class WindowEntry(Window window, HwndSource source, IntPtr handle, int ownerDepth, int order)
    {
        public Window Window { get; } = window;
        public HwndSource Source { get; } = source;
        public IntPtr Handle { get; } = handle;
        public int OwnerDepth { get; } = ownerDepth;
        public int Order { get; } = order;
        public bool HideAttempted { get; set; }
    }

    private static class Native
    {
        internal const uint GW_OWNER = 4;
        internal const int SW_SHOWNA = 8;
        internal const uint SWP_SHOWWINDOW = 0x0040, SWP_HIDEWINDOW = 0x0080;
        // Visibility-only native changes do not enter WPF ShowHelper(false), which would end
        // ShowDialog. WPF itself uses this flag combination when changing ShowInTaskbar.
        internal const uint PreserveWindowFlags = 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0200 | 0x0400;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindow(IntPtr handle, int command);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(IntPtr handle);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr handle);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowEnabled(IntPtr handle);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(IntPtr handle);
        [DllImport("user32.dll")]
        internal static extern IntPtr GetWindow(IntPtr handle, uint command);
        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        internal static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll")]
        internal static extern IntPtr GetFocus();
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(IntPtr handle);
        [DllImport("user32.dll")]
        internal static extern IntPtr SetActiveWindow(IntPtr handle);
        [DllImport("user32.dll")]
        internal static extern IntPtr SetFocus(IntPtr handle);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsChild(IntPtr parent, IntPtr child);
    }
}
