using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ming_AutoClicker.Models;

namespace Ming_AutoClicker.Views.ImageMatching;

public sealed class ImagePointSelectedEventArgs(ImageClickPoint point) : EventArgs
{
    public ImageClickPoint Point { get; } = point;
}

public sealed class ImageRegionSelectedEventArgs(ImageRelativeRegion region) : EventArgs
{
    public ImageRelativeRegion Region { get; } = region;
}

/// <summary>Uses the actual letterboxed image rectangle for drawing and pointer coordinates.</summary>
public sealed class TemplatePreviewControl : FrameworkElement
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(BitmapSource), typeof(TemplatePreviewControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ClickPointProperty = DependencyProperty.Register(
        nameof(ClickPoint), typeof(ImageClickPoint), typeof(TemplatePreviewControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty IgnoredRegionsProperty = DependencyProperty.Register(
        nameof(IgnoredRegions), typeof(IEnumerable<ImageRelativeRegion>), typeof(TemplatePreviewControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public BitmapSource? Source { get => (BitmapSource?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public ImageClickPoint? ClickPoint { get => (ImageClickPoint?)GetValue(ClickPointProperty); set => SetValue(ClickPointProperty, value); }
    public IEnumerable<ImageRelativeRegion>? IgnoredRegions { get => (IEnumerable<ImageRelativeRegion>?)GetValue(IgnoredRegionsProperty); set => SetValue(IgnoredRegionsProperty, value); }
    public bool DrawIgnoredRegion { get; set; }
    public event EventHandler<ImagePointSelectedEventArgs>? PointSelected;
    public event EventHandler<ImageRegionSelectedEventArgs>? RegionSelected;
    private Point? _dragStart;
    private Point? _dragEnd;

    public TemplatePreviewControl()
    {
        Cursor = Cursors.Cross;
        ClipToBounds = true;
        Focusable = true;
    }

    public Rect ImageRectangle
    {
        get
        {
            if (Source == null || ActualWidth <= 0 || ActualHeight <= 0) return Rect.Empty;
            var ratio = Math.Min(ActualWidth / Source.PixelWidth, ActualHeight / Source.PixelHeight);
            var width = Source.PixelWidth * ratio;
            var height = Source.PixelHeight * ratio;
            return new((ActualWidth - width) / 2, (ActualHeight - height) / 2, width, height);
        }
    }

    public bool TryGetImagePoint(Point position, out ImageClickPoint point)
    {
        point = new();
        var image = ImageRectangle;
        if (image.IsEmpty || !image.Contains(position)) return false;
        point = new() { X = Math.Clamp((position.X - image.X) / image.Width, 0, 1), Y = Math.Clamp((position.Y - image.Y) / image.Height, 0, 1) };
        return true;
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(28, 32, 43)), null, new Rect(RenderSize));
        var image = ImageRectangle;
        if (image.IsEmpty || Source == null) return;
        // The checkerboard makes transparent imported templates visibly different from ignored black pixels.
        drawing.PushClip(new RectangleGeometry(image));
        for (double y = image.Y; y < image.Bottom; y += 12)
            for (double x = image.X; x < image.Right; x += 12)
                drawing.DrawRectangle(((int)((x - image.X) / 12) + (int)((y - image.Y) / 12)) % 2 == 0 ? Brushes.DimGray : Brushes.Gray,
                    null, new Rect(x, y, 12, 12));
        drawing.DrawImage(Source, image);
        var mask = new SolidColorBrush(Color.FromArgb(145, 255, 170, 20));
        foreach (var region in IgnoredRegions ?? Array.Empty<ImageRelativeRegion>())
        {
            if (region == null || !double.IsFinite(region.X) || !double.IsFinite(region.Y) ||
                !double.IsFinite(region.Width) || !double.IsFinite(region.Height) || region.Width <= 0 || region.Height <= 0) continue;
            drawing.DrawRectangle(mask, new Pen(Brushes.Orange, 1), new Rect(image.X + region.X * image.Width,
                image.Y + region.Y * image.Height, region.Width * image.Width, region.Height * image.Height));
        }
        if (_dragStart.HasValue && _dragEnd.HasValue)
            drawing.DrawRectangle(mask, new Pen(Brushes.Orange, 1), new Rect(_dragStart.Value, _dragEnd.Value));
        drawing.Pop();
        if (ClickPoint is { } point && double.IsFinite(point.X) && double.IsFinite(point.Y))
        {
            var x = image.X + point.X * image.Width;
            var y = image.Y + point.Y * image.Height;
            drawing.DrawEllipse(null, new Pen(Brushes.Black, 4), new Point(x, y), 5, 5);
            drawing.DrawEllipse(null, new Pen(Brushes.Cyan, 2), new Point(x, y), 5, 5);
            drawing.DrawLine(new Pen(Brushes.Cyan, 2), new Point(x - 11, y), new Point(x + 11, y));
            drawing.DrawLine(new Pen(Brushes.Cyan, 2), new Point(x, y - 11), new Point(x, y + 11));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!TryGetImagePoint(e.GetPosition(this), out var point)) return;
        Focus();
        if (DrawIgnoredRegion)
        {
            _dragStart = e.GetPosition(this);
            _dragEnd = _dragStart;
            CaptureMouse();
        }
        else PointSelected?.Invoke(this, new(point));
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragStart.HasValue) return;
        var image = ImageRectangle;
        var point = e.GetPosition(this);
        _dragEnd = new Point(Math.Clamp(point.X, image.Left, image.Right), Math.Clamp(point.Y, image.Top, image.Bottom));
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragStart.HasValue || !_dragEnd.HasValue) return;
        var selected = new Rect(_dragStart.Value, _dragEnd.Value);
        var image = ImageRectangle;
        _dragStart = _dragEnd = null;
        ReleaseMouseCapture();
        if (selected.Width >= 2 && selected.Height >= 2 && !image.IsEmpty)
            RegionSelected?.Invoke(this, new(new ImageRelativeRegion { X = (selected.X - image.X) / image.Width,
                Y = (selected.Y - image.Y) / image.Height, Width = selected.Width / image.Width, Height = selected.Height / image.Height }));
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragStart = _dragEnd = null;
        InvalidateVisual();
    }
}
