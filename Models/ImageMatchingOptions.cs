using System.Drawing;

namespace Ming_AutoClicker.Models;

public sealed class ImageClickPoint
{
    public double X { get; set; } = 0.5;
    public double Y { get; set; } = 0.5;
    public ImageClickPoint Clone() => new() { X = X, Y = Y };
}

/// <summary>Coordinates relative to a template or a window client area, in [0, 1].</summary>
public sealed class ImageRelativeRegion
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public ImageRelativeRegion Clone() => new() { X = X, Y = Y, Width = Width, Height = Height };
}

public sealed class ImageTemplateVariant
{
    public string ImagePath { get; set; } = string.Empty;
    public ImageClickPoint? ClickPoint { get; set; }
    public List<ImageRelativeRegion> IgnoredRegions { get; set; } = new();
    public double? CaptureDpi { get; set; }
    public ImageTemplateVariant Clone() => new()
    {
        ImagePath = ImagePath, ClickPoint = ClickPoint?.Clone(), CaptureDpi = CaptureDpi,
        IgnoredRegions = IgnoredRegions.Select(r => r.Clone()).ToList()
    };
    public override string ToString() => System.IO.Path.GetFileName(ImagePath);
}

public enum ImageSearchScopeKind { Desktop, Region, Window }
public enum ImageSelectionMode { Unique, Ordered }

public sealed class ImageSearchScope
{
    public ImageSearchScopeKind Kind { get; set; }
    // Desktop region is always in physical screen pixels (negative coordinates are valid).
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string WindowProcessName { get; set; } = string.Empty;
    public string WindowClassName { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public ImageRelativeRegion? WindowRegion { get; set; }
    public ImageSearchScope Clone() => new()
    {
        Kind = Kind, X = X, Y = Y, Width = Width, Height = Height,
        WindowProcessName = WindowProcessName, WindowClassName = WindowClassName,
        WindowTitle = WindowTitle, WindowRegion = WindowRegion?.Clone()
    };
    public Rectangle GetRectangle() => new(X, Y, Width, Height);
}
