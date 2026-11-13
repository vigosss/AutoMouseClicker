using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ming_AutoClicker.Helpers;

/// <summary>A border whose staircase corners stay on the same grid when its content resizes.</summary>
public sealed class PixelBorder : Border
{
    public static readonly DependencyProperty StepSizeProperty = DependencyProperty.Register(
        nameof(StepSize), typeof(double), typeof(PixelBorder),
        new FrameworkPropertyMetadata(4d, FrameworkPropertyMetadataOptions.AffectsRender),
        value => value is double size && double.IsFinite(size) && size >= 0);

    public double StepSize
    {
        get => (double)GetValue(StepSizeProperty);
        set => SetValue(StepSizeProperty, value);
    }

    public PixelBorder()
    {
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var outer = new Rect(RenderSize);
        if (outer.Width <= 0 || outer.Height <= 0) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        // Border's layout rounds its insets at fractional DPI; paint the same insets.
        double Snap(double value, double scale) => UseLayoutRounding ? Math.Round(value * scale) / scale : value;
        var thickness = new Thickness(Snap(BorderThickness.Left, dpi.DpiScaleX),
            Snap(BorderThickness.Top, dpi.DpiScaleY), Snap(BorderThickness.Right, dpi.DpiScaleX),
            Snap(BorderThickness.Bottom, dpi.DpiScaleY));
        var inner = new Rect(thickness.Left, thickness.Top,
            Math.Max(0, outer.Width - thickness.Left - thickness.Right),
            Math.Max(0, outer.Height - thickness.Top - thickness.Bottom));
        var outline = CreateOutline(outer, StepSize);
        var insetStep = Math.Max(0, StepSize - Math.Min(Math.Min(thickness.Left, thickness.Top),
            Math.Min(thickness.Right, thickness.Bottom)));
        // A geometry ring preserves transparent backgrounds, including selection overlays.
        var ring = new GeometryGroup { FillRule = FillRule.EvenOdd };
        ring.Children.Add(outline);
        if (inner.Width > 0 && inner.Height > 0)
            ring.Children.Add(CreateOutline(inner, insetStep));
        ring.Freeze();
        dc.DrawGeometry(Background, null, outline);
        dc.DrawGeometry(BorderBrush, null, ring);
    }

    private static StreamGeometry CreateOutline(Rect rect, double step)
    {
        var s = Math.Min(step, Math.Min(rect.Width, rect.Height) / 2);
        var h = s / 2;
        var l = rect.Left; var t = rect.Top; var r = rect.Right; var b = rect.Bottom;
        Point[] points =
        [
            new(l + s, t), new(r - s, t), new(r - s, t + h), new(r - h, t + h),
            new(r - h, t + s), new(r, t + s), new(r, b - s), new(r - h, b - s),
            new(r - h, b - h), new(r - s, b - h), new(r - s, b), new(l + s, b),
            new(l + s, b - h), new(l + h, b - h), new(l + h, b - s), new(l, b - s),
            new(l, t + s), new(l + h, t + s), new(l + h, t + h), new(l + s, t + h)
        ];
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], true, true);
            context.PolyLineTo(points.AsSpan(1).ToArray(), true, false);
        }
        geometry.Freeze();
        return geometry;
    }
}
