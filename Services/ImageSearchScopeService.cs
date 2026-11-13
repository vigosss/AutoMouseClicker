using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using Ming_AutoClicker.Helpers;
using Ming_AutoClicker.Models;

namespace Ming_AutoClicker.Services;

public sealed class ImageSearchScopeException : InvalidOperationException
{
    public string ResourceKey { get; }
    public ImageSearchScopeException(string resourceKey)
        : base(LocalizationService.Current.GetString(resourceKey)) => ResourceKey = resourceKey;
}

public sealed class ImageWindowInfo
{
    public IntPtr Handle { get; init; }
    public string Title { get; init; } = string.Empty;
    public string ProcessName { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public Rectangle Bounds { get; init; }
    public double Dpi { get; init; } = 96;

    public ImageSearchScope ToScope() => new()
    {
        Kind = ImageSearchScopeKind.Window, WindowProcessName = ProcessName,
        WindowClassName = ClassName, WindowTitle = Title
    };

    public override string ToString() => $"{Title}  —  {ProcessName}";
}

public readonly record struct ResolvedImageSearchScope(Rectangle Bounds, double Dpi, IntPtr WindowHandle);

public static class ImageSearchScopeService
{
    public static List<ImageWindowInfo> GetWindows()
    {
        var windows = new List<ImageWindowInfo>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || IsIconic(handle) || IsCloaked(handle)) return true;
            GetWindowThreadProcessId(handle, out var processId);
            if (processId == Environment.ProcessId) return true;
            var title = GetText(handle);
            if (string.IsNullOrWhiteSpace(title)) return true;
            if (!TryGetClientBounds(handle, out var bounds)) return true;
            try
            {
                using var process = Process.GetProcessById((int)processId);
                windows.Add(new ImageWindowInfo
                {
                    Handle = handle, Title = title, ProcessName = process.ProcessName,
                    ClassName = GetClass(handle), Bounds = bounds,
                    Dpi = GetDpiAtPoint(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2)
                });
            }
            catch (ArgumentException) { } // The window may have closed during enumeration.
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            return true;
        }, IntPtr.Zero);
        return windows.OrderBy(w => w.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(w => w.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static ResolvedImageSearchScope Resolve(ImageSearchScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        Rectangle bounds;
        var handle = IntPtr.Zero;
        switch (scope.Kind)
        {
            case ImageSearchScopeKind.Desktop:
                var (x, y, width, height) = Win32Api.GetVirtualScreenBounds();
                bounds = new Rectangle(x, y, width, height);
                if (!IsValidRectangle(bounds)) throw new ImageSearchScopeException("ImageScopeUnavailable");
                break;
            case ImageSearchScopeKind.Region:
                bounds = scope.GetRectangle();
                if (!IsCoveredByScreens(bounds, GetMonitorBounds()))
                    throw new ImageSearchScopeException("ImageScopeOutsideScreen");
                break;
            case ImageSearchScopeKind.Window:
                if (string.IsNullOrWhiteSpace(scope.WindowProcessName) ||
                    string.IsNullOrWhiteSpace(scope.WindowClassName) || string.IsNullOrWhiteSpace(scope.WindowTitle))
                    throw new ImageSearchScopeException("ImageScopeSelectWindow");
                var windows = GetWindows().Where(w =>
                    string.Equals(w.ProcessName, scope.WindowProcessName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(w.ClassName, scope.WindowClassName, StringComparison.Ordinal) &&
                    string.Equals(w.Title, scope.WindowTitle, StringComparison.Ordinal)).ToList();
                if (windows.Count == 0) throw new ImageSearchScopeException("ImageScopeWindowMissing");
                if (windows.Count != 1) throw new ImageSearchScopeException("ImageScopeMultipleWindows");
                handle = windows[0].Handle;
                bounds = ToScreenRegion(windows[0].Bounds, scope.WindowRegion);
                if (!IsCoveredByScreens(bounds, GetMonitorBounds()))
                    throw new ImageSearchScopeException("ImageScopeOutsideScreen");
                break;
            default:
                throw new ImageSearchScopeException("ImageScopeUnavailable");
        }
        return new ResolvedImageSearchScope(bounds,
            GetDpiAtPoint(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2), handle);
    }

    /// <summary>Maps an optional relative client-area selection to physical pixels, without clipping it.</summary>
    public static Rectangle ToScreenRegion(Rectangle clientBounds, ImageRelativeRegion? region)
    {
        if (!IsValidRectangle(clientBounds)) throw new ImageSearchScopeException("ImageScopeUnavailable");
        if (region == null) return clientBounds;
        if (!double.IsFinite(region.X) || !double.IsFinite(region.Y) ||
            !double.IsFinite(region.Width) || !double.IsFinite(region.Height) ||
            region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0 ||
            region.X + region.Width > 1 || region.Y + region.Height > 1)
            throw new ImageSearchScopeException("ImageScopeInvalidRegion");
        var left = clientBounds.Left + (int)Math.Floor(region.X * clientBounds.Width);
        var top = clientBounds.Top + (int)Math.Floor(region.Y * clientBounds.Height);
        var right = clientBounds.Left + (int)Math.Ceiling((region.X + region.Width) * clientBounds.Width);
        var bottom = clientBounds.Top + (int)Math.Ceiling((region.Y + region.Height) * clientBounds.Height);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    /// <summary>Checks union coverage, including gaps between monitors and negative-coordinate displays.</summary>
    public static bool IsCoveredByScreens(Rectangle region, IReadOnlyList<Rectangle> screens)
    {
        if (!IsValidRectangle(region)) return false;
        var uncovered = new List<Rectangle> { region };
        foreach (var screen in screens)
        {
            if (!IsValidRectangle(screen)) continue;
            var next = new List<Rectangle>();
            foreach (var remainder in uncovered)
            {
                var intersection = Rectangle.Intersect(remainder, screen);
                if (intersection.Width <= 0 || intersection.Height <= 0)
                {
                    next.Add(remainder);
                    continue;
                }
                if (intersection.Top > remainder.Top)
                    next.Add(Rectangle.FromLTRB(remainder.Left, remainder.Top, remainder.Right, intersection.Top));
                if (intersection.Bottom < remainder.Bottom)
                    next.Add(Rectangle.FromLTRB(remainder.Left, intersection.Bottom, remainder.Right, remainder.Bottom));
                if (intersection.Left > remainder.Left)
                    next.Add(Rectangle.FromLTRB(remainder.Left, intersection.Top, intersection.Left, intersection.Bottom));
                if (intersection.Right < remainder.Right)
                    next.Add(Rectangle.FromLTRB(intersection.Right, intersection.Top, remainder.Right, intersection.Bottom));
            }
            uncovered = next;
            if (uncovered.Count == 0) return true;
        }
        return false;
    }

    public static double GetDpiAtPoint(int x, int y)
    {
        // Unlike GetDpiForWindow, this also describes DPI-unaware target applications' displayed pixels.
        var monitor = MonitorFromPoint(new NativePoint { X = x, Y = y }, 2);
        return monitor != IntPtr.Zero && GetScaleFactorForMonitor(monitor, out var scale) == 0 && scale > 0
            ? 96.0 * scale / 100.0 : 96;
    }

    public static void EnsureWindowUnobscured(IntPtr handle, Rectangle bounds)
    {
        if (!IsWindow(handle) || !IsWindowVisible(handle) || IsIconic(handle) || IsCloaked(handle))
            throw new ImageSearchScopeException("ImageScopeWindowMissing");

        // Conservatively reject any visible top-level window above the selected client region.
        // Rectangle checks may reject shaped/transparent overlays, but never identify their pixels as the target.
        var visited = new HashSet<IntPtr> { handle };
        var above = GetWindow(handle, 3); // GW_HWNDPREV
        while (above != IntPtr.Zero)
        {
            if (!visited.Add(above) || visited.Count > 4096)
                throw new ImageSearchScopeException("ImageScopeMoved");
            if (IsWindowVisible(above) && !IsIconic(above) && !IsCloaked(above) &&
                TryGetVisibleBounds(above, out var covering))
            {
                var intersection = Rectangle.Intersect(bounds, covering);
                if (intersection.Width > 0 && intersection.Height > 0)
                    throw new ImageSearchScopeException("ImageScopeObscured");
            }
            above = GetWindow(above, 3);
        }
    }

    private static bool IsValidRectangle(Rectangle rect) => rect.Width > 0 && rect.Height > 0 &&
        (long)rect.X + rect.Width <= int.MaxValue && (long)rect.Y + rect.Height <= int.MaxValue;

    private static List<Rectangle> GetMonitorBounds()
    {
        var rectangles = new List<Rectangle>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr monitor, IntPtr hdc, ref NativeRect rect, IntPtr data) =>
            {
                rectangles.Add(Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom));
                return true;
            }, IntPtr.Zero);
        return rectangles;
    }

    private static bool TryGetClientBounds(IntPtr handle, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (!GetClientRect(handle, out var client)) return false;
        var origin = new NativePoint { X = client.Left, Y = client.Top };
        if (!ClientToScreen(handle, ref origin)) return false;
        bounds = new Rectangle(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top);
        return IsValidRectangle(bounds);
    }

    private static bool TryGetVisibleBounds(IntPtr handle, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (DwmGetWindowAttribute(handle, 9, out NativeRect rect, Marshal.SizeOf<NativeRect>()) != 0 &&
            !GetWindowRect(handle, out rect)) return false;
        bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return IsValidRectangle(bounds);
    }

    private static bool IsCloaked(IntPtr handle) =>
        DwmGetWindowAttribute(handle, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    private static string GetText(IntPtr handle)
    {
        var buffer = new StringBuilder(Math.Max(1, GetWindowTextLength(handle) + 1));
        GetWindowText(handle, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static string GetClass(IntPtr handle)
    {
        var buffer = new StringBuilder(256);
        GetClassName(handle, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
    private delegate bool EnumWindowsCallback(IntPtr handle, IntPtr state);
    private delegate bool MonitorCallback(IntPtr monitor, IntPtr hdc, ref NativeRect bounds, IntPtr state);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorCallback callback, IntPtr state);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr handle);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr handle, uint command);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr handle, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr handle, ref NativePoint point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr handle, StringBuilder text, int maximum);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr handle, StringBuilder text, int maximum);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("shcore.dll")] private static extern int GetScaleFactorForMonitor(IntPtr monitor, out int scale);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr handle, uint attribute, out int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr handle, uint attribute, out NativeRect value, int size);
}
