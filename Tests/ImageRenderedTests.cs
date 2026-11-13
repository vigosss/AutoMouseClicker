using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Ming_AutoClicker.Models;
using Ming_AutoClicker.Services;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;
using WpfPath = System.Windows.Shapes.Path;

namespace Ming_AutoClicker.Tests;

internal static partial class Program
{
    static partial void AddImageRenderedTests(List<(string Name, Func<Task> Run)> tests)
    {
        tests.Add(("WPF controls rerendered at 96/120/144/192 DPI match automatically", ImageRenderedDpiMatching));
        tests.Add(("Image benchmark compares original fast/wide matching with automatic matching", ImageOriginalAlgorithmBenchmark));
    }

    private static Task ImageRenderedDpiMatching()
    {
        // Each pair is independently laid out and rasterized at its actual DPI. No scaled
        // bitmap is used to create the target, including when matching back down to 96 DPI.
        foreach (var templateDpi in new[] { 96, 120, 144, 192 })
        {
            using var fixture = new ImageMatchFixture();
            using var template = RenderDpiButton(templateDpi);
            var action = fixture.Action(template);
            action.CaptureDpi = templateDpi;
            action.ClickPoint = new() { X = .25, Y = .75 };
            foreach (var targetDpi in new[] { 96, 120, 144, 192 })
            {
                using var target = RenderDpiButton(targetDpi);
                using var frame = ImageBackground(640, 360);
                const int left = 173, top = 119;
                using (var destination = new Mat(frame.Mat, new DrawingRectangle(left, top, target.Width, target.Height)))
                    target.Mat.CopyTo(destination);
                var origin = new DrawingPoint(-1280, -100);
                var result = fixture.Matcher.MatchFrame(frame, action, origin, dpi: targetDpi);
                var label = $"WPF DPI {templateDpi}->{targetDpi}";
                Check(result.Found, $"{label}: {result.FailureReason}, score={result.Similarity:F3}, {result.ErrorMessage}");
                Equal(0.8, result.Threshold);
                Equal(1, result.Candidates.Count);
                Check(Math.Abs(result.X - (origin.X + left + target.Width / 2)) <= 2, $"{label}: incorrect X");
                Check(Math.Abs(result.Y - (origin.Y + top + target.Height / 2)) <= 2, $"{label}: incorrect Y");
                Check(Math.Abs(result.Width - target.Width) <= 3, $"{label}: width {result.Width}, expected {target.Width}");
                Check(Math.Abs(result.Height - target.Height) <= 3, $"{label}: height {result.Height}, expected {target.Height}");
                Check(Math.Abs(result.Scale - targetDpi / (double)templateDpi) <= 0.04, $"{label}: scale {result.Scale:F3}");
                var expectedClickX = origin.X + left + (int)Math.Round((target.Width - 1) * .25);
                var expectedClickY = origin.Y + top + (int)Math.Round((target.Height - 1) * .75);
                Check(Math.Abs(result.ClickX - expectedClickX) <= 3 && Math.Abs(result.ClickY - expectedClickY) <= 3,
                    $"{label}: click point did not follow target scaling");
            }
        }
        // Imported templates have no capture DPI metadata and must still discover scaling.
        using (var fixture = new ImageMatchFixture())
        using (var template = RenderDpiButton(96))
        using (var target = RenderDpiButton(144))
        using (var frame = ImageBackground(640, 360))
        {
            var action = fixture.Action(template);
            using (var destination = new Mat(frame.Mat, new DrawingRectangle(173, 119, target.Width, target.Height)))
                target.Mat.CopyTo(destination);
            var result = fixture.Matcher.MatchFrame(frame, action, dpi: 144);
            Check(result.Found, $"WPF imported template: {result.FailureReason}, {result.Similarity:F3}");
            Check(Math.Abs(result.Scale - 1.5) <= .04);
        }
        return Task.CompletedTask;
    }

    private static Image<Bgr, byte> RenderDpiButton(int dpi)
    {
        const int widthDip = 144, heightDip = 52;
        var background = new SolidColorBrush(Color.FromRgb(225, 220, 215));
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new WpfPath
        {
            Data = Geometry.Parse("M 1,8 L 5,12 L 14,2 M 14,10 L 14,16 L 0,16 L 0,2 L 7,2"),
            Stroke = new SolidColorBrush(Color.FromRgb(245, 250, 255)), StrokeThickness = 1.6,
            Width = 16, Height = 18, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(icon);
        var label = new TextBlock
        {
            Text = "Apply 42", FontFamily = new FontFamily("Segoe UI"), FontSize = 13,
            FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 1); content.Children.Add(label);
        var button = new Button
        {
            Content = content, Background = new SolidColorBrush(Color.FromRgb(35, 100, 170)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(18, 52, 95)), BorderThickness = new Thickness(1),
            Padding = new Thickness(9, 5, 9, 5), HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center, Focusable = false
        };
        // Use an explicit Border template so OS theme changes do not change the test corpus.
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.SetValue(Border.BackgroundProperty, button.Background);
        borderFactory.SetValue(Border.BorderBrushProperty, button.BorderBrush);
        borderFactory.SetValue(Border.BorderThicknessProperty, button.BorderThickness);
        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        borderFactory.SetValue(Border.PaddingProperty, button.Padding);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetBinding(ContentPresenter.ContentProperty, new Binding(nameof(ContentControl.Content))
        { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        borderFactory.AppendChild(presenter);
        button.Template = new ControlTemplate(typeof(Button)) { VisualTree = borderFactory };
        var root = new Border
        {
            Width = widthDip, Height = heightDip, Background = background,
            Padding = new Thickness(3), Child = button, UseLayoutRounding = true, SnapsToDevicePixels = true
        };
        VisualTreeHelper.SetRootDpi(root, new DpiScale(dpi / 96d, dpi / 96d));
        TextOptions.SetTextFormattingMode(root, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(root, TextRenderingMode.Grayscale);
        root.Measure(new Size(widthDip, heightDip));
        root.Arrange(new Rect(0, 0, widthDip, heightDip));
        root.UpdateLayout();
        var width = (int)Math.Round(widthDip * dpi / 96d);
        var height = (int)Math.Round(heightDip * dpi / 96d);
        var bitmap = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var directory = Path.Combine(AppContext.BaseDirectory, "artifacts");
        Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(directory, $"wpf-button-{dpi}dpi.png"))) encoder.Save(stream);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        var image = new Image<Bgr, byte>(width, height);
        var data = image.Data;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                for (var channel = 0; channel < 3; channel++)
                    data[y, x, channel] = pixels[(y * width + x) * 4 + channel];
        return image;
    }

    private static Task ImageOriginalAlgorithmBenchmark()
    {
        // Fixed in-memory frames; excludes screenshot capture. Baselines already have their
        // original template decoded, while the automatic cold run includes its file cache load.
        // These measurements are observations, not a hardware-independent speed requirement.
        foreach (var scale in new[] { 1.0, 1.333333, 0.0 })
        {
            using var fixture = new ImageMatchFixture();
            using var template = ImagePattern(96, 80);
            using var frame = ImageBackground(1920, 1080);
            if (scale > 0) ImagePaste(frame, template, 1100, 650, scale);
            var action = fixture.Action(template);
            using var fast = new OriginalTemplateMatcher(template, new[] { 1.0, .95, 1.05 });
            using var wide = new OriginalTemplateMatcher(template, new[]
                { 1.0, .95, 1.05, .9, 1.1, .85, 1.15, .8, 1.2, .75, 1.25, .67, 1.5, 1.75, .5, 2.0 });
            var fastCold = fast.Match(frame); var fastWarm = fast.Match(frame);
            var wideCold = wide.Match(frame); var wideWarm = wide.Match(frame);
            var autoCold = fixture.Matcher.MatchFrame(frame, action);
            var autoWarm = fixture.Matcher.MatchFrame(frame, action);
            var scenario = scale == 0 ? "absent" : $"scale{scale:F2}";
            Console.WriteLine($"BENCH comparison 1920x1080 template96x80 {scenario}: " +
                $"original-fast cold={fastCold.Milliseconds:F1}ms cached={fastWarm.Milliseconds:F1}ms found={fastWarm.Found} scale={fastWarm.Scale:F3} score={fastWarm.Score:F3}; " +
                $"original-wide cold={wideCold.Milliseconds:F1}ms cached={wideWarm.Milliseconds:F1}ms found={wideWarm.Found} scale={wideWarm.Scale:F3} score={wideWarm.Score:F3}; " +
                $"automatic cold={autoCold.ElapsedMilliseconds}ms cached={autoWarm.ElapsedMilliseconds}ms found={autoWarm.Found} scale={autoWarm.Scale:F3} score={autoWarm.Similarity:F3}");
        }
        return Task.CompletedTask;
    }

    /// <summary>Reproduces the previous textured-template gray CcoeffNormed path and its cache.</summary>
    private sealed class OriginalTemplateMatcher : IDisposable
    {
        private readonly Image<Bgr, byte> _template;
        private readonly double[] _scales;
        private readonly Dictionary<double, Image<Gray, byte>> _gray = new();
        public OriginalTemplateMatcher(Image<Bgr, byte> template, double[] scales)
        {
            _template = template; _scales = scales;
            _gray[1] = template.Convert<Gray, byte>();
        }

        public (bool Found, double Score, double Scale, double Milliseconds) Match(Image<Bgr, byte> frame)
        {
            var watch = Stopwatch.StartNew();
            using var source = frame.Convert<Gray, byte>();
            var best = 0d; var bestScale = 1d;
            foreach (var scale in _scales)
            {
                if (!_gray.TryGetValue(scale, out var template))
                {
                    using var color = _template.Resize((int)Math.Round(_template.Width * scale),
                        (int)Math.Round(_template.Height * scale), Inter.Linear);
                    _gray[scale] = template = color.Convert<Gray, byte>();
                }
                using var scores = new Mat();
                CvInvoke.MatchTemplate(source, template, scores, TemplateMatchingType.CcoeffNormed);
                double min = 0, max = 0;
                var minPoint = DrawingPoint.Empty; var maxPoint = DrawingPoint.Empty;
                CvInvoke.MinMaxLoc(scores, ref min, ref max, ref minPoint, ref maxPoint);
                if (max > best) { best = max; bestScale = scale; }
                // Preserve the old exact-size high-confidence shortcut; it matters to comparison.
                if (scale == 1.0 && max >= .95 && max >= .8) break;
            }
            return (best >= .8, best, bestScale, watch.Elapsed.TotalMilliseconds);
        }

        public void Dispose()
        {
            foreach (var image in _gray.Values) image.Dispose();
        }
    }
}
