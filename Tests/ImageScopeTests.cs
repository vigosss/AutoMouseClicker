using System.Drawing;
using Emgu.CV;
using Emgu.CV.Structure;
using Ming_AutoClicker.Models;
using Ming_AutoClicker.Services;

namespace Ming_AutoClicker.Tests;

internal static partial class Program
{
    static partial void AddImageScopeTests(List<(string Name, Func<Task> Run)> tests)
    {
        tests.Add(("Search regions follow window client bounds and retain negative coordinates", ImageScopeCoordinates));
        tests.Add(("Explicit regions reject screen gaps and invalid geometry without widening", ImageScopeCoverage));
        tests.Add(("Preview snapshots remain frozen and independent of their disposed capture", ImageFrameSnapshot));
    }

    private static Task ImageScopeCoordinates()
    {
        var relative = new ImageRelativeRegion { X = 0.25, Y = 0.1, Width = 0.5, Height = 0.4 };
        var before = ImageSearchScopeService.ToScreenRegion(new Rectangle(-1600, -100, 1000, 600), relative);
        Equal(new Rectangle(-1350, -40, 500, 240), before);
        var after = ImageSearchScopeService.ToScreenRegion(new Rectangle(50, 100, 2000, 1200), relative);
        Equal(new Rectangle(550, 220, 1000, 480), after);
        Equal(new Rectangle(-100, 20, 300, 200),
            ImageSearchScopeService.ToScreenRegion(new Rectangle(-100, 20, 300, 200), null));
        var full = new ImageRelativeRegion { Width = 1, Height = 1 };
        Equal(new Rectangle(-100, -200, 123, 127),
            ImageSearchScopeService.ToScreenRegion(new Rectangle(-100, -200, 123, 127), full));

        foreach (var invalid in new[]
        {
            new ImageRelativeRegion { Width = 0, Height = 1 },
            new ImageRelativeRegion { X = -0.01, Width = 1, Height = 1 },
            new ImageRelativeRegion { X = 0.5, Width = 0.6, Height = 1 },
            new ImageRelativeRegion { Width = double.NaN, Height = 1 },
            new ImageRelativeRegion { Y = double.PositiveInfinity, Width = 1, Height = 1 }
        })
        {
            var rejected = false;
            try { ImageSearchScopeService.ToScreenRegion(new Rectangle(0, 0, 100, 100), invalid); }
            catch (ImageSearchScopeException) { rejected = true; }
            Check(rejected);
        }
        return Task.CompletedTask;
    }

    private static Task ImageScopeCoverage()
    {
        var displays = new[] { new Rectangle(-1920, 0, 1920, 1080), new Rectangle(0, 0, 1920, 1080) };
        Check(ImageSearchScopeService.IsCoveredByScreens(new Rectangle(-30, 50, 60, 100), displays));
        Check(ImageSearchScopeService.IsCoveredByScreens(new Rectangle(-1920, 0, 3840, 1080), displays));
        Check(!ImageSearchScopeService.IsCoveredByScreens(new Rectangle(-1930, 0, 20, 50), displays));
        Check(!ImageSearchScopeService.IsCoveredByScreens(new Rectangle(0, 0, 0, 10), displays));
        Check(!ImageSearchScopeService.IsCoveredByScreens(new Rectangle(int.MaxValue - 4, 0, 10, 10), displays));

        var staggered = new[] { new Rectangle(-1920, -540, 1920, 1080), new Rectangle(0, 0, 1920, 1080) };
        Check(!ImageSearchScopeService.IsCoveredByScreens(new Rectangle(-20, -50, 40, 100), staggered));
        Check(ImageSearchScopeService.IsCoveredByScreens(new Rectangle(-20, 50, 40, 100), staggered));
        var overlapping = new[] { new Rectangle(-100, 0, 200, 100), new Rectangle(0, 0, 200, 100) };
        Check(ImageSearchScopeService.IsCoveredByScreens(new Rectangle(-100, 0, 300, 100), overlapping));
        Check(!ImageSearchScopeService.IsCoveredByScreens(new Rectangle(0, 0, 100, 100), Array.Empty<Rectangle>()));
        return Task.CompletedTask;
    }

    private static Task ImageFrameSnapshot()
    {
        System.Windows.Media.Imaging.BitmapSource snapshot;
        var expected = new byte[4 * 4 * 4];
        using (var frame = new CapturedImageFrame(new Image<Bgr, byte>(4, 4, new Bgr(17, 61, 203)),
                   new Rectangle(-300, 80, 4, 4), 144))
        {
            snapshot = frame.CreateSnapshot();
            Check(snapshot.IsFrozen);
            Equal(new Rectangle(-300, 80, 4, 4), frame.Bounds);
            Equal(144.0, frame.Dpi);
            snapshot.CopyPixels(expected, 16, 0);
            frame.Image.SetZero();
        }
        var after = new byte[expected.Length];
        snapshot.CopyPixels(after, 16, 0);
        Check(expected.SequenceEqual(after));
        Check(after.Any(value => value != 0));
        return Task.CompletedTask;
    }
}
