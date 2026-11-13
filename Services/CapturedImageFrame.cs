using System.Drawing;
using System.Windows.Media.Imaging;
using Emgu.CV;
using Emgu.CV.Structure;
using Ming_AutoClicker.Helpers;
using Ming_AutoClicker.Models;

namespace Ming_AutoClicker.Services;

/// <summary>A single captured frame and its physical screen coordinates. Owns Image.</summary>
public sealed class CapturedImageFrame : IDisposable
{
    public Image<Bgr, byte> Image { get; }
    public Rectangle Bounds { get; }
    public double Dpi { get; }

    public CapturedImageFrame(Image<Bgr, byte> image, Rectangle bounds, double dpi = 96)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Width != bounds.Width || image.Height != bounds.Height)
            throw new ArgumentException("Frame dimensions must agree with capture bounds.", nameof(bounds));
        Image = image;
        Bounds = bounds;
        Dpi = double.IsFinite(dpi) && dpi > 0 ? dpi : 96;
    }

    public BitmapSource CreateSnapshot()
    {
        using var bitmap = Image.ToBitmap();
        var snapshot = Win32Api.BitmapToBitmapSource(bitmap);
        snapshot.Freeze();
        return snapshot;
    }

    public void Dispose() => Image.Dispose();
}

public interface IImageFrameSource
{
    CapturedImageFrame Capture(ImageSearchScope scope);
}

/// <summary>Captures visible pixels only. A window scope never falls back to the desktop.</summary>
public sealed class DesktopImageFrameSource : IImageFrameSource
{
    private readonly ScreenCaptureService _captureService;
    public DesktopImageFrameSource(ScreenCaptureService captureService) =>
        _captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));

    public CapturedImageFrame Capture(ImageSearchScope scope)
    {
        var resolved = ImageSearchScopeService.Resolve(scope);
        if (resolved.WindowHandle != IntPtr.Zero)
            ImageSearchScopeService.EnsureWindowUnobscured(resolved.WindowHandle, resolved.Bounds);

        var image = _captureService.CaptureRegion(resolved.Bounds.X, resolved.Bounds.Y,
            resolved.Bounds.Width, resolved.Bounds.Height);
        try
        {
            // A moving/covered window invalidates coordinates even when the captured pixels look plausible.
            if (resolved.WindowHandle != IntPtr.Zero)
            {
                var afterCapture = ImageSearchScopeService.Resolve(scope);
                if (resolved.WindowHandle != afterCapture.WindowHandle || resolved.Bounds != afterCapture.Bounds)
                    throw new ImageSearchScopeException("ImageScopeMoved");
                ImageSearchScopeService.EnsureWindowUnobscured(resolved.WindowHandle, resolved.Bounds);
            }
            return new CapturedImageFrame(image, resolved.Bounds, resolved.Dpi);
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }
}
