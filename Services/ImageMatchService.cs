using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;

namespace Ming_AutoClicker.Services
{
    public enum MatchFailureReason
    {
        None,
        BelowThreshold,
        InvalidTemplate,
        CaptureFailed,
        MatchingError,
        Cancelled,
        TimedOut
    }

    /// <summary>
    /// 图像匹配结果。即使 Found=false，也可能包含最佳候选位置和相似度。
    /// </summary>
    public class MatchResult
    {
        public bool Found { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public double Similarity { get; set; }
        public double Threshold { get; set; }
        public double SecondBestSimilarity { get; set; }
        public double Scale { get; set; } = 1.0;
        public long ElapsedMilliseconds { get; set; }
        public string MatchMethod { get; set; } = string.Empty;
        public MatchFailureReason FailureReason { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;

        public bool HasCandidate => Width > 0 && Height > 0;

        public static MatchResult NotFound => new()
        {
            Found = false,
            FailureReason = MatchFailureReason.BelowThreshold
        };

        public Rectangle GetRectangle()
        {
            return new Rectangle(X - Width / 2, Y - Height / 2, Width, Height);
        }
    }

    /// <summary>
    /// 图像匹配服务。默认只检查原尺寸和 ±5%，需要时可启用宽范围自适应缩放。
    /// </summary>
    public class ImageMatchService : IDisposable
    {
        private sealed class ScaledTemplate : IDisposable
        {
            public Image<Bgr, byte> Color { get; }
            public Image<Gray, byte> Gray { get; }

            public ScaledTemplate(Image<Bgr, byte> color)
            {
                Color = color;
                Gray = color.Convert<Gray, byte>();
            }

            public void Dispose()
            {
                Gray.Dispose();
                Color.Dispose();
            }
        }

        private sealed class CachedTemplate : IDisposable
        {
            private readonly Dictionary<int, ScaledTemplate> _scaledTemplates = new();

            public string FullPath { get; }
            public long LastWriteTicks { get; }
            public long FileLength { get; }
            public Image<Bgr, byte> Color { get; }
            public Image<Gray, byte> Gray { get; }
            public double GrayStandardDeviation { get; }
            public long LastAccessSequence { get; set; }

            public CachedTemplate(string fullPath, long lastWriteTicks, long fileLength, Image<Bgr, byte> color)
            {
                FullPath = fullPath;
                LastWriteTicks = lastWriteTicks;
                FileLength = fileLength;
                Color = color;
                Gray = color.Convert<Gray, byte>();
                GrayStandardDeviation = CalculateStandardDeviation(Gray);
            }

            public (Image<Bgr, byte> Color, Image<Gray, byte> Gray) GetImages(double scale)
            {
                if (Math.Abs(scale - 1.0) < 0.0001)
                    return (Color, Gray);

                var key = (int)Math.Round(scale * 1000);
                if (!_scaledTemplates.TryGetValue(key, out var scaled))
                {
                    var width = Math.Max(1, (int)Math.Round(Color.Width * scale));
                    var height = Math.Max(1, (int)Math.Round(Color.Height * scale));
                    scaled = new ScaledTemplate(Color.Resize(width, height, Inter.Linear));
                    _scaledTemplates[key] = scaled;
                }

                return (scaled.Color, scaled.Gray);
            }

            private static double CalculateStandardDeviation(Image<Gray, byte> image)
            {
                var data = image.Data;
                var count = image.Width * image.Height;
                if (count <= 0) return 0;

                double sum = 0;
                double squareSum = 0;
                for (var y = 0; y < image.Height; y++)
                {
                    for (var x = 0; x < image.Width; x++)
                    {
                        var value = data[y, x, 0];
                        sum += value;
                        squareSum += value * value;
                    }
                }

                var mean = sum / count;
                return Math.Sqrt(Math.Max(0, squareSum / count - mean * mean));
            }

            public void Dispose()
            {
                foreach (var scaled in _scaledTemplates.Values)
                    scaled.Dispose();
                _scaledTemplates.Clear();
                Gray.Dispose();
                Color.Dispose();
            }
        }

        private readonly ScreenCaptureService _screenCaptureService;
        private readonly object _matchLock = new();
        private readonly Dictionary<string, CachedTemplate> _templateCache = new(StringComparer.OrdinalIgnoreCase);
        private long _cacheAccessSequence;
        private bool _disposed;

        private const int MaxCachedTemplates = 16;
        private const double LowVarianceThreshold = 5.0;
        private const double HighConfidenceEarlyExit = 0.95;

        private static readonly double[] FastScaleLevels = { 1.0, 0.95, 1.05 };
        private static readonly double[] AdaptiveScaleLevels =
        {
            1.0, 0.95, 1.05, 0.9, 1.1, 0.85, 1.15, 0.8, 1.2,
            0.75, 1.25, 0.67, 1.5, 1.75, 0.5, 2.0
        };

        public const double DefaultThreshold = 0.8;
        public int MatchTimeoutMs { get; set; } = 5000;

        public ImageMatchService(ScreenCaptureService screenCaptureService)
        {
            _screenCaptureService = screenCaptureService ?? throw new ArgumentNullException(nameof(screenCaptureService));
        }

        public MatchResult FindImage(string templatePath, double threshold = DefaultThreshold, bool adaptiveScale = false)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var screenImage = _screenCaptureService.CaptureFullScreen();
                var result = FindTemplate(screenImage, templatePath, threshold, adaptiveScale);
                if (result.HasCandidate)
                {
                    var (virtualX, virtualY, _, _) = Helpers.Win32Api.GetVirtualScreenBounds();
                    result.X += virtualX;
                    result.Y += virtualY;
                }

                result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                return result;
            }
            catch (Exception ex)
            {
                return CreateFailure(MatchFailureReason.CaptureFailed, ex.Message, stopwatch.ElapsedMilliseconds);
            }
        }

        public MatchResult FindImageInRegion(
            string templatePath,
            int x,
            int y,
            int width,
            int height,
            double threshold = DefaultThreshold,
            bool adaptiveScale = false)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var regionImage = _screenCaptureService.CaptureRegion(x, y, width, height);
                var result = FindTemplate(regionImage, templatePath, threshold, adaptiveScale);
                if (result.HasCandidate)
                {
                    result.X += x;
                    result.Y += y;
                }

                result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                return result;
            }
            catch (Exception ex)
            {
                return CreateFailure(MatchFailureReason.CaptureFailed, ex.Message, stopwatch.ElapsedMilliseconds);
            }
        }

        private MatchResult FindTemplate(
            Image<Bgr, byte> source,
            string templatePath,
            double threshold,
            bool adaptiveScale)
        {
            try
            {
                lock (_matchLock)
                {
                    ThrowIfDisposed();
                    var template = GetCachedTemplate(templatePath);
                    var scales = adaptiveScale ? AdaptiveScaleLevels : FastScaleLevels;
                    var normalizedThreshold = NormalizeThreshold(threshold);
                    var useLowVarianceMethod = template.GrayStandardDeviation < LowVarianceThreshold;
                    using var sourceGray = useLowVarianceMethod ? null : source.Convert<Gray, byte>();

                    MatchResult? best = null;
                    MatchResult? secondBest = null;

                    foreach (var scale in scales)
                    {
                        var (scaledColor, scaledGray) = template.GetImages(scale);
                        if (scaledColor.Width < 5 || scaledColor.Height < 5 ||
                            scaledColor.Width > source.Width || scaledColor.Height > source.Height)
                        {
                            continue;
                        }

                        var candidate = useLowVarianceMethod
                            ? MatchLowVariance(source, scaledColor, scale)
                            : MatchGray(sourceGray!, scaledGray, scale);

                        // 截图环境未变化时通常可接近 100%，走一次匹配即可完成。
                        if (Math.Abs(scale - 1.0) < 0.0001 &&
                            candidate.Similarity >= normalizedThreshold &&
                            candidate.Similarity >= HighConfidenceEarlyExit)
                        {
                            candidate.Threshold = normalizedThreshold;
                            candidate.Found = true;
                            candidate.FailureReason = MatchFailureReason.None;
                            return candidate;
                        }

                        if (best == null || candidate.Similarity > best.Similarity)
                        {
                            secondBest = best;
                            best = candidate;
                        }
                        else if (secondBest == null || candidate.Similarity > secondBest.Similarity)
                        {
                            secondBest = candidate;
                        }
                    }

                    if (best == null)
                    {
                        return CreateFailure(
                            MatchFailureReason.InvalidTemplate,
                            "模板在所有启用尺度下都大于屏幕，或模板尺寸小于 5 像素");
                    }

                    best.SecondBestSimilarity = secondBest?.Similarity ?? 0;
                    best.Threshold = normalizedThreshold;
                    best.Found = best.Similarity >= best.Threshold;
                    best.FailureReason = best.Found
                        ? MatchFailureReason.None
                        : MatchFailureReason.BelowThreshold;
                    return best;
                }
            }
            catch (FileNotFoundException ex)
            {
                return CreateFailure(MatchFailureReason.InvalidTemplate, ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return CreateFailure(MatchFailureReason.InvalidTemplate, ex.Message);
            }
            catch (ArgumentException ex)
            {
                return CreateFailure(MatchFailureReason.InvalidTemplate, ex.Message);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"图像匹配失败: {ex}");
                return CreateFailure(MatchFailureReason.MatchingError, ex.Message);
            }
        }

        private static MatchResult MatchGray(
            Image<Gray, byte> source,
            Image<Gray, byte> template,
            double scale)
        {
            using var result = new Mat();
            CvInvoke.MatchTemplate(source, template, result, TemplateMatchingType.CcoeffNormed);

            double minValue = 0;
            double maxValue = 0;
            var minLocation = Point.Empty;
            var maxLocation = Point.Empty;
            CvInvoke.MinMaxLoc(result, ref minValue, ref maxValue, ref minLocation, ref maxLocation);

            return CreateCandidate(maxLocation, template.Width, template.Height, maxValue, scale, "灰度相关匹配");
        }

        private static MatchResult MatchLowVariance(
            Image<Bgr, byte> source,
            Image<Bgr, byte> template,
            double scale)
        {
            using var result = new Mat();
            // 非归一化平方差不会在纯黑/纯色模板上出现除零问题。
            CvInvoke.MatchTemplate(source, template, result, TemplateMatchingType.Sqdiff);

            double minValue = 0;
            double maxValue = 0;
            var minLocation = Point.Empty;
            var maxLocation = Point.Empty;
            CvInvoke.MinMaxLoc(result, ref minValue, ref maxValue, ref minLocation, ref maxLocation);

            var sampleCount = (double)template.Width * template.Height * 3;
            var rootMeanSquareError = Math.Sqrt(Math.Max(0, minValue) / sampleCount);
            var similarity = 1.0 - Math.Clamp(rootMeanSquareError / 255.0, 0, 1);
            return CreateCandidate(minLocation, template.Width, template.Height, similarity, scale, "低纹理差异匹配");
        }

        private static MatchResult CreateCandidate(
            Point location,
            int width,
            int height,
            double similarity,
            double scale,
            string method)
        {
            if (!double.IsFinite(similarity)) similarity = 0;
            return new MatchResult
            {
                X = location.X + width / 2,
                Y = location.Y + height / 2,
                Width = width,
                Height = height,
                Similarity = Math.Clamp(similarity, 0, 1),
                Scale = scale,
                MatchMethod = method
            };
        }

        private static double NormalizeThreshold(double threshold)
        {
            return double.IsFinite(threshold)
                ? Math.Clamp(threshold, 0, 1)
                : DefaultThreshold;
        }

        private CachedTemplate GetCachedTemplate(string templatePath)
        {
            var fullPath = ResolveTemplatePath(templatePath);
            var info = new FileInfo(fullPath);
            if (!info.Exists)
                throw new FileNotFoundException($"图像文件不存在: {fullPath}", fullPath);

            if (_templateCache.TryGetValue(fullPath, out var cached))
            {
                if (cached.LastWriteTicks == info.LastWriteTimeUtc.Ticks && cached.FileLength == info.Length)
                {
                    cached.LastAccessSequence = ++_cacheAccessSequence;
                    return cached;
                }

                cached.Dispose();
                _templateCache.Remove(fullPath);
            }

            var color = _screenCaptureService.LoadImage(fullPath);
            var newEntry = new CachedTemplate(fullPath, info.LastWriteTimeUtc.Ticks, info.Length, color)
            {
                LastAccessSequence = ++_cacheAccessSequence
            };
            _templateCache[fullPath] = newEntry;
            TrimTemplateCache();
            return newEntry;
        }

        private string ResolveTemplatePath(string templatePath)
        {
            if (string.IsNullOrWhiteSpace(templatePath))
                throw new ArgumentException("模板路径不能为空", nameof(templatePath));

            return Path.GetFullPath(Path.IsPathRooted(templatePath)
                ? templatePath
                : Path.Combine(_screenCaptureService.GetScreenshotDirectory(), templatePath));
        }

        private void TrimTemplateCache()
        {
            while (_templateCache.Count > MaxCachedTemplates)
            {
                CachedTemplate? oldest = null;
                foreach (var item in _templateCache.Values)
                {
                    if (oldest == null || item.LastAccessSequence < oldest.LastAccessSequence)
                        oldest = item;
                }

                if (oldest == null) return;
                _templateCache.Remove(oldest.FullPath);
                oldest.Dispose();
            }
        }

        public async Task<MatchResult> WaitForImageAsync(
            string templatePath,
            double threshold = DefaultThreshold,
            int timeoutMs = 30000,
            int intervalMs = 200,
            CancellationToken cancellationToken = default,
            bool adaptiveScale = false)
        {
            var totalStopwatch = Stopwatch.StartNew();
            MatchResult? bestCandidate = null;

            while (totalStopwatch.ElapsedMilliseconds < timeoutMs)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    var cancelled = bestCandidate ?? MatchResult.NotFound;
                    cancelled.Found = false;
                    cancelled.FailureReason = MatchFailureReason.Cancelled;
                    cancelled.ElapsedMilliseconds = totalStopwatch.ElapsedMilliseconds;
                    return cancelled;
                }

                var iterationStopwatch = Stopwatch.StartNew();
                var result = FindImage(templatePath, threshold, adaptiveScale);
                if (result.Found)
                {
                    result.ElapsedMilliseconds = totalStopwatch.ElapsedMilliseconds;
                    return result;
                }

                if (result.FailureReason is MatchFailureReason.InvalidTemplate or
                    MatchFailureReason.MatchingError or MatchFailureReason.CaptureFailed)
                    return result;

                if (result.HasCandidate && (bestCandidate == null || result.Similarity > bestCandidate.Similarity))
                    bestCandidate = result;

                var remainingDelay = Math.Max(0, intervalMs - (int)iterationStopwatch.ElapsedMilliseconds);
                try
                {
                    if (remainingDelay > 0)
                        await Task.Delay(remainingDelay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    var cancelled = bestCandidate ?? MatchResult.NotFound;
                    cancelled.Found = false;
                    cancelled.FailureReason = MatchFailureReason.Cancelled;
                    cancelled.ElapsedMilliseconds = totalStopwatch.ElapsedMilliseconds;
                    return cancelled;
                }
            }

            var timedOut = bestCandidate ?? MatchResult.NotFound;
            timedOut.Found = false;
            timedOut.FailureReason = MatchFailureReason.TimedOut;
            timedOut.ElapsedMilliseconds = totalStopwatch.ElapsedMilliseconds;
            return timedOut;
        }

        public MatchResult[] FindAllMatches(string templatePath, double threshold = DefaultThreshold)
        {
            var results = new List<MatchResult>();
            var (virtualX, virtualY, _, _) = Helpers.Win32Api.GetVirtualScreenBounds();

            try
            {
                using var screenImage = _screenCaptureService.CaptureFullScreen();
                using var template = _screenCaptureService.LoadImage(templatePath);
                if (template.Width > screenImage.Width || template.Height > screenImage.Height)
                    return results.ToArray();

                using var result = new Mat();
                CvInvoke.MatchTemplate(screenImage, template, result, TemplateMatchingType.CcoeffNormed);
                using var resultImage = result.ToImage<Gray, float>();
                var resultData = resultImage.Data;

                for (var y = 0; y < result.Rows; y++)
                {
                    for (var x = 0; x < result.Cols; x++)
                    {
                        var value = resultData[y, x, 0];
                        if (value < threshold) continue;
                        results.Add(new MatchResult
                        {
                            Found = true,
                            X = virtualX + x + template.Width / 2,
                            Y = virtualY + y + template.Height / 2,
                            Width = template.Width,
                            Height = template.Height,
                            Similarity = value,
                            Scale = 1.0,
                            MatchMethod = "彩色相关匹配"
                        });
                        x += template.Width - 1;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"查找所有匹配失败: {ex.Message}");
            }

            return results.ToArray();
        }

        public MatchResult TestMatch(
            string templatePath,
            double threshold = DefaultThreshold,
            bool adaptiveScale = false)
        {
            var result = FindImage(templatePath, threshold, adaptiveScale);
            Debug.WriteLine(
                $"匹配测试: Found={result.Found}, Score={result.Similarity:P1}, " +
                $"Scale={result.Scale:P0}, Time={result.ElapsedMilliseconds}ms, Reason={result.FailureReason}");
            return result;
        }

        private static MatchResult CreateFailure(
            MatchFailureReason reason,
            string message,
            long elapsedMilliseconds = 0)
        {
            return new MatchResult
            {
                Found = false,
                FailureReason = reason,
                ErrorMessage = message,
                ElapsedMilliseconds = elapsedMilliseconds
            };
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ImageMatchService));
        }

        public void Dispose()
        {
            lock (_matchLock)
            {
                if (_disposed) return;
                _disposed = true;
                foreach (var cached in _templateCache.Values)
                    cached.Dispose();
                _templateCache.Clear();
            }
        }
    }
}
