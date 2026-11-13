using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Ming_AutoClicker.Models;
using Ming_AutoClicker.Services;

namespace Ming_AutoClicker.Tests;

internal static partial class Program
{
    static partial void AddImageMatchingTests(List<(string Name, Func<Task> Run)> tests)
    {
        tests.Add(("Automatic matching finds original and intermediate scales", ImageAutomaticScales));
        tests.Add(("Image matching rejects same-shaped wrong-color targets", ImageColorValidation));
        tests.Add(("Multiple positions stay ambiguous after cache warming and sort by Y/X", ImageAmbiguityAndOrdering));
        tests.Add(("Template variants deduplicate one position and detect separate appearances", ImageTemplateVariants));
        tests.Add(("Transparent and ignored pixels do not influence matching", ImageMasks));
        tests.Add(("Transparent padding cannot hide adjacent independent targets", ImageMaskedNeighborAmbiguity));
        tests.Add(("Low-information and missing templates fail explicitly", ImageInvalidTemplates));
        tests.Add(("Matching cancellation and budgets cannot return partial success", ImageCancellationAndBudget));
        tests.Add(("Fresh-frame verification rejects old frames and late cancellation", ImageFreshVerification));
        tests.Add(("Fresh verification preserves the selected same-file variant and click point", ImageVerificationVariantIdentity));
        tests.Add(("Waiting reports ambiguous targets immediately and distinguishes waiting timeout", ImageWaitDiagnostics));
        tests.Add(("Template cache invalidates file and ignored-region edits", ImageCacheInvalidation));
        tests.Add(("Image matching reports physical coordinates and compatible click points", ImageClickCoordinates));
        tests.Add(("Image matching benchmark covers cold/cache/absent 1080p frames", ImageMatchingBenchmark));
        tests.Add(("Image matching benchmark covers small icons and absent 4K frames", ImageMatchingLargeDesktopBenchmark));
        tests.Add(("Image matching benchmark covers present and absent small icons at 4K", ImageMatchingSmall4KBenchmark));
    }

    private static Task ImageAutomaticScales()
    {
        using var fixture = new ImageMatchFixture();
        using var template = ImagePattern();
        var action = fixture.Action(template);
        foreach (var scale in new[] { 1.0, 0.5, 0.73, 1.17, 1.333333, 1.75, 2.0 })
        {
            using var frame = ImageBackground();
            var size = ImagePaste(frame, template, 37, 41, scale);
            var result = fixture.Matcher.MatchFrame(frame, action, new Point(-1920, -120), dpi: 144);
            Check(result.Found, $"Scale {scale}: {result.FailureReason}, {result.ErrorMessage}, score {result.Similarity}, {result.ElapsedMilliseconds}ms");
            Equal(1, result.Candidates.Count);
            Check(Math.Abs(result.X - (-1920 + 37 + size.Width / 2)) <= 2);
            Check(Math.Abs(result.Y - (-120 + 41 + size.Height / 2)) <= 2);
            Check(Math.Abs(result.Width - size.Width) <= 2, $"Expected width {size.Width}, got {result.Width}");
        }
        return Task.CompletedTask;
    }

    private static Task ImageColorValidation()
    {
        using var fixture = new ImageMatchFixture();
        using var template = ImagePattern();
        var action = fixture.Action(template);
        using var wrong = template.Copy();
        var pixels = wrong.Data;
        for (var y = 0; y < wrong.Height; y++)
            for (var x = 0; x < wrong.Width; x++)
                (pixels[y, x, 0], pixels[y, x, 2]) = (pixels[y, x, 2], pixels[y, x, 0]);
        using var frame = ImageBackground();
        ImagePaste(frame, wrong, 60, 50);
        var result = fixture.Matcher.MatchFrame(frame, action);
        Check(!result.Found, "A normalized grayscale match must not accept a different-colored button.");
        Equal(MatchFailureReason.BelowThreshold, result.FailureReason);
        ImagePaste(frame, template, 150, 100);
        result = fixture.Matcher.MatchFrame(frame, action);
        Check(result.Found);
        Equal(1, result.Candidates.Count);
        Equal(150 + template.Width / 2, result.X);
        return Task.CompletedTask;
    }

    private static Task ImageAmbiguityAndOrdering()
    {
        using var fixture = new ImageMatchFixture();
        using var template = ImagePattern();
        var action = fixture.Action(template);
        using var frame = ImageBackground(340, 220);
        ImagePaste(frame, template, 25, 30);
        Check(fixture.Matcher.MatchFrame(frame, action).Found);
        ImagePaste(frame, template, 210, 130);
        var result = fixture.Matcher.MatchFrame(frame, action);
        Equal(MatchFailureReason.Ambiguous, result.FailureReason);
        Check(!result.Found);
        Equal(2, result.Candidates.Count);
        action.SelectionMode = ImageSelectionMode.Ordered;
        action.MatchIndex = 2;
        result = fixture.Matcher.MatchFrame(frame, action);
        Check(result.Found);
        Equal(210 + template.Width / 2, result.X);
        action.MatchIndex = 3;
        Check(!fixture.Matcher.MatchFrame(frame, action).Found);
        return Task.CompletedTask;
    }

    private static Task ImageTemplateVariants()
    {
        using var fixture = new ImageMatchFixture();
        using var template = ImagePattern();
        using var other = ImagePattern(seed: 567);
        var action = fixture.Action(template);
        action.AlternateTemplates.Add(new ImageTemplateVariant { ImagePath = fixture.Save(template) });
        using var frame = ImageBackground(340, 220);
        ImagePaste(frame, template, 25, 30);
        var result = fixture.Matcher.MatchFrame(frame, action);
        Check(result.Found);
        Equal(1, result.Candidates.Count);
        action.AlternateTemplates.Add(new ImageTemplateVariant { ImagePath = fixture.Save(other) });
        ImagePaste(frame, other, 210, 130);
        result = fixture.Matcher.MatchFrame(frame, action);
        Equal(MatchFailureReason.Ambiguous, result.FailureReason);
        Equal(2, result.Candidates.Count);
        return Task.CompletedTask;
    }

    private static Task ImageMasks()
    {
        using var fixture = new ImageMatchFixture();
        using var template = ImagePattern();
        var action = fixture.Action(template);
        action.IgnoredRegions.Add(new ImageRelativeRegion { X = 0.3, Y = 0.25, Width = 0.4, Height = 0.5 });
        using var frame = ImageBackground();
        ImagePaste(frame, template, 50, 55);
        using (var region = new Mat(frame.Mat, new Rectangle(50 + 18, 55 + 12, 24, 24))) region.SetTo(new MCvScalar(240, 20, 80));
        var result = fixture.Matcher.MatchFrame(frame, action);
        Check(result.Found, $"Ignored area: {result.FailureReason}, {result.ErrorMessage}");

        using var bitmap = template.ToBitmap();
        using var transparent = bitmap.Clone(new Rectangle(0, 0, bitmap.Width, bitmap.Height), PixelFormat.Format32bppArgb);
        for (var y = 0; y < transparent.Height; y++)
            for (var x = 0; x < transparent.Width; x++)
                if (x < 8 || y < 8 || x >= transparent.Width - 8 || y >= transparent.Height - 8)
                    transparent.SetPixel(x, y, Color.Transparent);
        var alphaPath = fixture.Save(transparent);
        action = new FindImageAction { ImagePath = alphaPath, MatchThreshold = 0.8 };
        using var alphaFrame = ImageBackground();
        var data = template.Data;
        for (var y = 8; y < template.Height - 8; y++)
            for (var x = 8; x < template.Width - 8; x++)
                for (var c = 0; c < 3; c++) alphaFrame.Data[40 + y, 60 + x, c] = data[y, x, c];
        result = fixture.Matcher.MatchFrame(alphaFrame, action);
        Check(result.Found, $"Alpha mask: {result.FailureReason}, {result.ErrorMessage}");
        Equal(60 + template.Width / 2, result.X);
        return Task.CompletedTask;
    }

    private static Task ImageInvalidTemplates()
    {
        using var fixture = new ImageMatchFixture();
        using var flat = new Image<Bgr, byte>(40, 30, new Bgr(70, 70, 70));
        using var frame = ImageBackground();
        var action = fixture.Action(flat);
        Equal(MatchFailureReason.UninformativeTemplate, fixture.Matcher.MatchFrame(frame, action).FailureReason);
        action.ImagePath = "does_not_exist_" + Guid.NewGuid().ToString("N") + ".png";
        Equal(MatchFailureReason.InvalidTemplate, fixture.Matcher.MatchFrame(frame, action).FailureReason);
        action.ImagePath = Path.Combine(Path.GetTempPath(), "outside.png");
        Equal(MatchFailureReason.InvalidTemplate, fixture.Matcher.MatchFrame(frame, action).FailureReason);
        return Task.CompletedTask;
    }

    private static Task ImageMaskedNeighborAmbiguity()
    {
        using var fixture = new ImageMatchFixture();
        using var pattern = ImagePattern(40, 40);
        using var padded = new Bitmap(100, 100, PixelFormat.Format32bppArgb);
        var pixels = pattern.Data;
        for (var y = 0; y < 40; y++)
            for (var x = 0; x < 40; x++)
                padded.SetPixel(30 + x, 30 + y, Color.FromArgb(255, pixels[y, x, 2], pixels[y, x, 1], pixels[y, x, 0]));
        var action = new FindImageAction { ImagePath = fixture.Save(padded) };
        using var frame = ImageBackground();
        ImagePaste(frame, pattern, 50, 60);
        ImagePaste(frame, pattern, 95, 60);
        var result = fixture.Matcher.MatchFrame(frame, action);
        Check(!result.Found, "Transparent canvas padding must not turn two targets into a unique target.");
        Equal(MatchFailureReason.Ambiguous, result.FailureReason);
        Equal(2, result.Candidates.Count);
        return Task.CompletedTask;
    }

    private static Task ImageCancellationAndBudget()
    {
        using var fixture = new ImageMatchFixture();
        using var template = ImagePattern();
        using var frame = ImageBackground();
        var action = fixture.Action(template);
        ImagePaste(frame, template, 20, 30);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var result = fixture.Matcher.MatchFrame(frame, action, cancellationToken: cancelled.Token);
        Equal(MatchFailureReason.Cancelled, result.FailureReason);
        Check(!result.Found);
        result = fixture.Matcher.MatchFrame(frame, action, budgetMs: 0);
        Equal(MatchFailureReason.TimedOut, result.FailureReason);
        Check(!result.Found);
        return Task.CompletedTask;
    }

    private static Task ImageCacheInvalidation()
    {
        using var fixture = new ImageMatchFixture();
        using var template = ImagePattern();
        using var replacement = ImagePattern(seed: 991);
        using var frame = ImageBackground();
        var action = fixture.Action(template);
        ImagePaste(frame, template, 20, 30);
        Check(fixture.Matcher.MatchFrame(frame, action).Found);
        replacement.Save(action.ImagePath);
        File.SetLastWriteTimeUtc(action.ImagePath, DateTime.UtcNow.AddSeconds(2));
        Check(!fixture.Matcher.MatchFrame(frame, action).Found, "Changing the file must invalidate cached pixels and scales.");
        ImagePaste(frame, replacement, 20, 30);
        Check(fixture.Matcher.MatchFrame(frame, action).Found);
        action.IgnoredRegions.Add(new ImageRelativeRegion { Width = 1, Height = 1 });
        Equal(MatchFailureReason.UninformativeTemplate, fixture.Matcher.MatchFrame(frame, action).FailureReason);
        return Task.CompletedTask;
    }

    private static async Task ImageFreshVerification()
    {
        using var template = ImagePattern();
        using var frame = ImageBackground();
        ImagePaste(frame, template, 50, 60);
        var source = new ImageTestFrameSource(frame, count => { if (count == 2) Thread.Sleep(350); });
        using (var fixture = new ImageMatchFixture(source))
        {
            var result = await fixture.Matcher.WaitForImageAsync(fixture.Action(template));
            Equal(2, source.Captures);
            Check(!result.Found);
            Equal(MatchFailureReason.TargetChanged, result.FailureReason);
        }
        using var cancellation = new CancellationTokenSource();
        source = new ImageTestFrameSource(frame, count => { if (count == 2) cancellation.Cancel(); });
        using (var fixture = new ImageMatchFixture(source))
        {
            var result = await fixture.Matcher.WaitForImageAsync(fixture.Action(template), cancellation.Token);
            Equal(2, source.Captures);
            Check(!result.Found);
            Equal(MatchFailureReason.Cancelled, result.FailureReason);
        }
    }

    private static async Task ImageVerificationVariantIdentity()
    {
        using var template = ImagePattern();
        using var frame = ImageBackground();
        ImagePaste(frame, template, 50, 60);
        var data = frame.Data;
        for (var y = 12; y < 36; y++)
            for (var x = 18; x < 42; x++)
                for (var channel = 0; channel < 3; channel++) data[60 + y, 50 + x, channel] += 10;
        var source = new ImageTestFrameSource(frame, _ => { });
        using var fixture = new ImageMatchFixture(source);
        var action = fixture.Action(template);
        action.ClickPoint = new ImageClickPoint { X = 0.2, Y = 0.5 };
        action.AlternateTemplates.Add(new ImageTemplateVariant
        {
            ImagePath = action.ImagePath, ClickPoint = new ImageClickPoint { X = 0.8, Y = 0.5 },
            IgnoredRegions = new() { new() { X = 0.3, Y = 0.25, Width = 0.4, Height = 0.5 } }
        });
        var result = await fixture.Matcher.WaitForImageAsync(action);
        Check(result.Found, $"Variant identity: {result.FailureReason}, {result.ErrorMessage}");
        Equal(1, result.TemplateIndex);
        Equal(result.GetRectangle().X + (int)Math.Round((result.Width - 1) * 0.8), result.ClickX);
        return;
    }

    private static async Task ImageWaitDiagnostics()
    {
        using var template = ImagePattern(20, 20);
        using var frame = ImageBackground(100, 80);
        ImagePaste(frame, template, 10, 10);
        ImagePaste(frame, template, 60, 40);
        var source = new ImageTestFrameSource(frame, _ => { });
        using (var fixture = new ImageMatchFixture(source))
        {
            var action = fixture.Action(template);
            action.TimeoutMs = 5000;
            var result = await fixture.Matcher.WaitForImageAsync(action);
            Equal(MatchFailureReason.Ambiguous, result.FailureReason);
            Equal(1, source.Captures);
        }
        using var absent = ImageBackground(100, 80);
        source = new ImageTestFrameSource(absent, _ => { });
        using (var fixture = new ImageMatchFixture(source))
        {
            var action = fixture.Action(template);
            action.TimeoutMs = 250;
            var result = await fixture.Matcher.WaitForImageAsync(action, intervalMs: 500);
            Equal(MatchFailureReason.TimedOut, result.FailureReason);
            Equal(LocalizationService.Current.GetString("MatchTimedOut"), result.ErrorMessage);
        }
    }

    private static Task ImageClickCoordinates()
    {
        using var fixture = new ImageMatchFixture();
        using var template = ImagePattern();
        using var frame = ImageBackground();
        var action = fixture.Action(template);
        action.OffsetX = 9; action.OffsetY = -7;
        ImagePaste(frame, template, 20, 30, 1.5);
        var result = fixture.Matcher.MatchFrame(frame, action, new Point(-300, -80));
        Check(result.Found);
        Equal(result.X + 9, result.ClickX);
        Equal(result.Y - 7, result.ClickY);
        action.ClickPoint = new ImageClickPoint { X = 0.2, Y = 0.8 };
        result = fixture.Matcher.MatchFrame(frame, action, new Point(-300, -80));
        Check(result.Found);
        Equal(result.GetRectangle().Left + (int)Math.Round((result.Width - 1) * 0.2), result.ClickX);
        Equal(result.GetRectangle().Top + (int)Math.Round((result.Height - 1) * 0.8), result.ClickY);
        action.ClickPoint = null;
        action.OffsetX = int.MaxValue;
        result = fixture.Matcher.MatchFrame(frame, action);
        Check(!result.Found);
        Equal(MatchFailureReason.InvalidTemplate, result.FailureReason);
        return Task.CompletedTask;
    }

    private static Task ImageMatchingBenchmark()
    {
        using var fixture = new ImageMatchFixture();
        using var template = ImagePattern(96, 80);
        using var frame = ImageBackground(1920, 1080);
        var action = fixture.Action(template);
        ImagePaste(frame, template, 1100, 650, 1.17);
        var cold = fixture.Matcher.MatchFrame(frame, action);
        Check(cold.Found, $"1080p cold: {cold.FailureReason}, {cold.ErrorMessage}, {cold.ElapsedMilliseconds}ms");
        var warm = fixture.Matcher.MatchFrame(frame, action);
        Check(warm.Found, $"1080p cached: {warm.FailureReason}, {warm.ErrorMessage}");
        using var absent = ImageBackground(1920, 1080);
        var missing = fixture.Matcher.MatchFrame(absent, action);
        Equal(MatchFailureReason.BelowThreshold, missing.FailureReason);
        Console.WriteLine($"BENCH image 1920x1080 template96x80: cold={cold.ElapsedMilliseconds}ms cached={warm.ElapsedMilliseconds}ms absent={missing.ElapsedMilliseconds}ms");
        return Task.CompletedTask;
    }

    private static Task ImageMatchingLargeDesktopBenchmark()
    {
        using var fixture = new ImageMatchFixture();
        using var small = ImagePattern(24, 24);
        using var frame = ImageBackground(1920, 1080);
        var action = fixture.Action(small);
        ImagePaste(frame, small, 1300, 800);
        var icon = fixture.Matcher.MatchFrame(frame, action);
        Check(icon.Found, $"1080p small icon: {icon.FailureReason}, {icon.ElapsedMilliseconds}ms");
        using var large = ImagePattern(96, 80);
        using var absent = ImageBackground(3840, 2160);
        var missing = fixture.Matcher.MatchFrame(absent, fixture.Action(large));
        Equal(MatchFailureReason.BelowThreshold, missing.FailureReason);
        Console.WriteLine($"BENCH image: 1080p template24x24={icon.ElapsedMilliseconds}ms; 3840x2160 absent template96x80={missing.ElapsedMilliseconds}ms");
        return Task.CompletedTask;
    }

    private static Task ImageMatchingSmall4KBenchmark()
    {
        using var fixture = new ImageMatchFixture();
        using var icon = ImagePattern(24, 24);
        using var frame = ImageBackground(3840, 2160);
        var action = fixture.Action(icon);
        ImagePaste(frame, icon, 2600, 1700);
        var present = fixture.Matcher.MatchFrame(frame, action);
        Check(present.Found, $"4K small icon present: {present.FailureReason}, {present.ElapsedMilliseconds}ms");
        using var absent = ImageBackground(3840, 2160);
        var missing = fixture.Matcher.MatchFrame(absent, action);
        Equal(MatchFailureReason.BelowThreshold, missing.FailureReason);
        Console.WriteLine($"BENCH image 3840x2160 template24x24: present={present.ElapsedMilliseconds}ms absent={missing.ElapsedMilliseconds}ms");
        return Task.CompletedTask;
    }

    private static Image<Bgr, byte> ImagePattern(int width = 60, int height = 48, int seed = 123)
    {
        var image = new Image<Bgr, byte>(width, height);
        var random = new Random(seed);
        var data = image.Data;
        for (var cellY = 0; cellY < height; cellY += 4)
            for (var cellX = 0; cellX < width; cellX += 4)
            {
                var value = random.Next(0, 100);
                for (var y = cellY; y < Math.Min(height, cellY + 4); y++)
                    for (var x = cellX; x < Math.Min(width, cellX + 4); x++)
                    { data[y, x, 0] = (byte)(30 + value); data[y, x, 1] = (byte)(40 + value); data[y, x, 2] = (byte)(110 + value); }
            }
        return image;
    }

    private static Image<Bgr, byte> ImageBackground(int width = 280, int height = 200) =>
        new(width, height, new Bgr(215, 220, 225));

    private static Size ImagePaste(Image<Bgr, byte> frame, Image<Bgr, byte> template, int x, int y, double scale = 1)
    {
        var width = (int)Math.Round(template.Width * scale);
        var height = (int)Math.Round(template.Height * scale);
        using var scaled = template.Resize(width, height, scale < 1 ? Inter.Area : Inter.Linear);
        using var region = new Mat(frame.Mat, new Rectangle(x, y, width, height));
        scaled.Mat.CopyTo(region);
        return new Size(width, height);
    }

    private sealed class ImageMatchFixture : IDisposable
    {
        private readonly ScreenCaptureService _capture = new();
        private readonly List<string> _files = new();
        public ImageMatchService Matcher { get; }
        public ImageMatchFixture(IImageFrameSource? source = null) => Matcher = new(_capture, source);
        public FindImageAction Action(Image<Bgr, byte> template) => new() { ImagePath = Save(template), MatchThreshold = 0.8 };
        public string Save(Image<Bgr, byte> template)
        {
            var path = Path.Combine(_capture.GetScreenshotDirectory(), "matching_test_" + Guid.NewGuid().ToString("N") + ".png");
            template.Save(path); _files.Add(path); return path;
        }
        public string Save(Bitmap template)
        {
            var path = Path.Combine(_capture.GetScreenshotDirectory(), "matching_test_" + Guid.NewGuid().ToString("N") + ".png");
            template.Save(path, ImageFormat.Png); _files.Add(path); return path;
        }
        public void Dispose()
        {
            Matcher.Dispose(); _capture.Dispose();
            foreach (var path in _files) if (File.Exists(path)) File.Delete(path);
        }
    }

    private sealed class ImageTestFrameSource : IImageFrameSource
    {
        private readonly Image<Bgr, byte> _frame;
        private readonly Action<int> _onCapture;
        public int Captures { get; private set; }
        public ImageTestFrameSource(Image<Bgr, byte> frame, Action<int> onCapture)
        { _frame = frame; _onCapture = onCapture; }
        public CapturedImageFrame Capture(ImageSearchScope scope)
        {
            _onCapture(++Captures);
            return new CapturedImageFrame(_frame.Copy(), new Rectangle(Point.Empty, _frame.Size));
        }
    }
}
