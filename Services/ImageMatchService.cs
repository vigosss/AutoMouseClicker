using System;
using System.Drawing;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;

namespace Ming_AutoClicker.Services
{
    /// <summary>
    /// 图像匹配结果
    /// </summary>
    public class MatchResult
    {
        /// <summary>
        /// 是否找到匹配
        /// </summary>
        public bool Found { get; set; }

        /// <summary>
        /// 匹配位置（中心点 X 坐标）
        /// </summary>
        public int X { get; set; }

        /// <summary>
        /// 匹配位置（中心点 Y 坐标）
        /// </summary>
        public int Y { get; set; }

        /// <summary>
        /// 匹配区域宽度
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// 匹配区域高度
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// 匹配相似度 (0.0 - 1.0)
        /// </summary>
        public double Similarity { get; set; }

        /// <summary>
        /// 创建未找到的结果
        /// </summary>
        public static MatchResult NotFound => new MatchResult { Found = false };

        /// <summary>
        /// 获取匹配区域的矩形
        /// </summary>
        public Rectangle GetRectangle()
        {
            return new Rectangle(X - Width / 2, Y - Height / 2, Width, Height);
        }
    }

    /// <summary>
    /// 图像匹配服务 - 使用 Emgu.CV 进行模板匹配
    /// </summary>
    public class ImageMatchService : IDisposable
    {
        private readonly ScreenCaptureService _screenCaptureService;
        private bool _disposed;

        /// <summary>
        /// 默认匹配阈值
        /// </summary>
        public const double DefaultThreshold = 0.8;

        /// <summary>
        /// 匹配超时时间（毫秒）
        /// </summary>
        public int MatchTimeoutMs { get; set; } = 5000;

        public ImageMatchService(ScreenCaptureService screenCaptureService)
        {
            _screenCaptureService = screenCaptureService ?? throw new ArgumentNullException(nameof(screenCaptureService));
        }

        /// <summary>
        /// 在全屏中查找图像
        /// </summary>
        /// <param name="templatePath">模板图像路径</param>
        /// <param name="threshold">匹配阈值 (0.0 - 1.0)</param>
        /// <returns>匹配结果</returns>
        public MatchResult FindImage(string templatePath, double threshold = DefaultThreshold)
        {
            // 获取全屏截图
            using var screenImage = _screenCaptureService.CaptureFullScreen();
            return FindTemplate(screenImage, templatePath, threshold);
        }

        /// <summary>
        /// 在指定区域中查找图像
        /// </summary>
        /// <param name="templatePath">模板图像路径</param>
        /// <param name="x">区域起始 X</param>
        /// <param name="y">区域起始 Y</param>
        /// <param name="width">区域宽度</param>
        /// <param name="height">区域高度</param>
        /// <param name="threshold">匹配阈值</param>
        /// <returns>匹配结果（坐标为屏幕绝对坐标）</returns>
        public MatchResult FindImageInRegion(string templatePath, int x, int y, int width, int height, double threshold = DefaultThreshold)
        {
            // 获取区域截图
            using var regionImage = _screenCaptureService.CaptureRegion(x, y, width, height);
            var result = FindTemplate(regionImage, templatePath, threshold);

            // 转换为屏幕绝对坐标
            if (result.Found)
            {
                result.X += x;
                result.Y += y;
            }

            return result;
        }

        /// <summary>
        /// 多尺度搜索的缩放比例列表（覆盖所有常见 DPI 缩放因子）
        /// 按优先级排序：精确匹配 > 小偏移 > 常见 DPI 缩放 > 大缩放
        /// </summary>
        private static readonly double[] _scaleLevels = 
        { 
            1.0,    // 精确匹配（最高优先级）
            0.95, 1.05,   // ±5% 轻微偏移
            0.9, 1.1,     // ±10% 偏移
            0.85, 1.15,   // ±15%
            0.8, 1.2,     // ±20%
            0.75, 1.25,   // 75%, 125% DPI
            1.5,          // 150% DPI
            1.75,         // 175% DPI
            2.0,          // 200% DPI
            0.67,         // 2/3 缩放
            0.5           // 50% 缩放
        };

        /// <summary>
        /// 在源图像中查找模板（支持多尺度搜索 + 灰度回退）
        /// 策略：先在彩色图上多尺度搜索 → 若失败，转灰度再搜一遍
        /// </summary>
        /// <param name="source">源图像</param>
        /// <param name="templatePath">模板路径</param>
        /// <param name="threshold">匹配阈值</param>
        /// <returns>匹配结果</returns>
        private MatchResult FindTemplate(Image<Bgr, byte> source, string templatePath, double threshold)
        {
            try
            {
                // 加载模板图像
                using var template = _screenCaptureService.LoadImage(templatePath);

                // 检查模板尺寸
                if (template.Width > source.Width || template.Height > source.Height)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"模板({template.Width}x{template.Height})大于源图({source.Width}x{source.Height})，尝试灰度缩放匹配...");
                    // 即使模板比源图大，也先记录，可能灰度多尺度匹配能找到
                }

                // ===== 第一轮：彩色图多尺度匹配 =====
                var colorResult = FindTemplateMultiScale(source, template, threshold);
                if (colorResult.Found)
                    return colorResult;

                // ===== 第二轮：灰度图多尺度匹配（回退方案，抗亮度/颜色变化） =====
                System.Diagnostics.Debug.WriteLine("彩色匹配未命中，尝试灰度匹配...");
                using var sourceGray = source.Convert<Gray, byte>();
                using var templateGray = template.Convert<Gray, byte>();
                var grayResult = FindTemplateMultiScaleGray(sourceGray, templateGray, threshold);
                if (grayResult.Found)
                {
                    System.Diagnostics.Debug.WriteLine($"灰度匹配成功: 位置({grayResult.X}, {grayResult.Y}), 相似度 {grayResult.Similarity:P}");
                    return grayResult;
                }

                // ===== 第三轮：如果阈值较高，尝试降低阈值再搜（仅灰度，容错） =====
                if (threshold > 0.5)
                {
                    var loweredThreshold = Math.Max(0.5, threshold - 0.15);
                    System.Diagnostics.Debug.WriteLine($"尝试降低阈值到 {loweredThreshold:P0} ...");
                    var relaxedResult = FindTemplateMultiScaleGray(sourceGray, templateGray, loweredThreshold);
                    if (relaxedResult.Found)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"降低阈值匹配成功: 位置({relaxedResult.X}, {relaxedResult.Y}), 相似度 {relaxedResult.Similarity:P} (阈值 {loweredThreshold:P0})");
                        return relaxedResult;
                    }
                }

                System.Diagnostics.Debug.WriteLine("所有匹配策略均未命中");
                return MatchResult.NotFound;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"图像匹配失败: {ex.Message}");
                return MatchResult.NotFound;
            }
        }

        /// <summary>
        /// 彩色多尺度模板匹配
        /// </summary>
        private MatchResult FindTemplateMultiScale(Image<Bgr, byte> source, Image<Bgr, byte> template, double threshold)
        {
            MatchResult? bestResult = null;

            foreach (var scale in _scaleLevels)
            {
                int scaledWidth = (int)(template.Width * scale);
                int scaledHeight = (int)(template.Height * scale);

                if (scaledWidth < 5 || scaledHeight < 5)
                    continue;

                if (scaledWidth > source.Width || scaledHeight > source.Height)
                    continue;

                using var scaledTemplate = scale == 1.0
                    ? template.Clone()
                    : template.Resize(scaledWidth, scaledHeight, Inter.Linear);

                using var result = new Mat();
                CvInvoke.MatchTemplate(source, scaledTemplate, result, TemplateMatchingType.CcoeffNormed);

                double minVal = 0, maxVal = 0;
                Point minLoc = Point.Empty, maxLoc = Point.Empty;
                CvInvoke.MinMaxLoc(result, ref minVal, ref maxVal, ref minLoc, ref maxLoc);

                if (maxVal >= threshold)
                {
                    var match = new MatchResult
                    {
                        Found = true,
                        X = maxLoc.X + scaledWidth / 2,
                        Y = maxLoc.Y + scaledHeight / 2,
                        Width = scaledWidth,
                        Height = scaledHeight,
                        Similarity = maxVal
                    };

                    if (scale == 1.0)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"彩色匹配成功: 缩放 {scale:P0}, 位置({match.X}, {match.Y}), 相似度 {match.Similarity:P}");
                        return match;
                    }

                    if (bestResult == null || match.Similarity > bestResult.Similarity)
                    {
                        bestResult = match;
                        System.Diagnostics.Debug.WriteLine(
                            $"彩色多尺度命中: 缩放 {scale:P0}, 位置({match.X}, {match.Y}), 相似度 {match.Similarity:P}");
                    }
                }
            }

            return bestResult ?? MatchResult.NotFound;
        }

        /// <summary>
        /// 灰度多尺度模板匹配（抗亮度/颜色变化）
        /// </summary>
        private MatchResult FindTemplateMultiScaleGray(Image<Gray, byte> source, Image<Gray, byte> template, double threshold)
        {
            MatchResult? bestResult = null;

            foreach (var scale in _scaleLevels)
            {
                int scaledWidth = (int)(template.Width * scale);
                int scaledHeight = (int)(template.Height * scale);

                if (scaledWidth < 5 || scaledHeight < 5)
                    continue;

                if (scaledWidth > source.Width || scaledHeight > source.Height)
                    continue;

                using var scaledTemplate = scale == 1.0
                    ? template.Clone()
                    : template.Resize(scaledWidth, scaledHeight, Inter.Linear);

                using var result = new Mat();
                CvInvoke.MatchTemplate(source, scaledTemplate, result, TemplateMatchingType.CcoeffNormed);

                double minVal = 0, maxVal = 0;
                Point minLoc = Point.Empty, maxLoc = Point.Empty;
                CvInvoke.MinMaxLoc(result, ref minVal, ref maxVal, ref minLoc, ref maxLoc);

                if (maxVal >= threshold)
                {
                    var match = new MatchResult
                    {
                        Found = true,
                        X = maxLoc.X + scaledWidth / 2,
                        Y = maxLoc.Y + scaledHeight / 2,
                        Width = scaledWidth,
                        Height = scaledHeight,
                        Similarity = maxVal
                    };

                    if (scale == 1.0)
                    {
                        return match;
                    }

                    if (bestResult == null || match.Similarity > bestResult.Similarity)
                    {
                        bestResult = match;
                    }
                }
            }

            return bestResult ?? MatchResult.NotFound;
        }

        /// <summary>
        /// 等待图像出现（异步）
        /// </summary>
        /// <param name="templatePath">模板图像路径</param>
        /// <param name="threshold">匹配阈值</param>
        /// <param name="timeoutMs">超时时间（毫秒）</param>
        /// <param name="intervalMs">检查间隔（毫秒）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>匹配结果</returns>
        public async System.Threading.Tasks.Task<MatchResult> WaitForImageAsync(string templatePath, double threshold = DefaultThreshold, int timeoutMs = 30000, int intervalMs = 500, System.Threading.CancellationToken cancellationToken = default)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return MatchResult.NotFound;
                }

                var result = FindImage(templatePath, threshold);
                if (result.Found)
                {
                    return result;
                }

                try
                {
                    await System.Threading.Tasks.Task.Delay(intervalMs, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return MatchResult.NotFound;
                }
            }

            return MatchResult.NotFound;
        }

        /// <summary>
        /// 查找所有匹配位置
        /// </summary>
        /// <param name="templatePath">模板图像路径</param>
        /// <param name="threshold">匹配阈值</param>
        /// <returns>所有匹配结果</returns>
        public MatchResult[] FindAllMatches(string templatePath, double threshold = DefaultThreshold)
        {
            var results = new System.Collections.Generic.List<MatchResult>();

            try
            {
                using var screenImage = _screenCaptureService.CaptureFullScreen();
                using var template = _screenCaptureService.LoadImage(templatePath);

                if (template.Width > screenImage.Width || template.Height > screenImage.Height)
                {
                    return results.ToArray();
                }

                // 执行模板匹配
                using var result = new Mat();
                CvInvoke.MatchTemplate(screenImage, template, result, TemplateMatchingType.CcoeffNormed);

                // 获取结果数据（使用 ToImage 转换，避免 CopyTo 类型不匹配）
                using var resultImage = result.ToImage<Gray, float>();
                var resultData = resultImage.Data;

                // 查找所有超过阈值的位置
                for (int y = 0; y < result.Rows; y++)
                {
                    for (int x = 0; x < result.Cols; x++)
                    {
                        var value = resultData[y, x, 0];
                        if (value >= threshold)
                        {
                            results.Add(new MatchResult
                            {
                                Found = true,
                                X = x + template.Width / 2,
                                Y = y + template.Height / 2,
                                Width = template.Width,
                                Height = template.Height,
                                Similarity = value
                            });

                            // 跳过重叠区域，避免重复检测
                            x += template.Width - 1;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"查找所有匹配失败: {ex.Message}");
            }

            return results.ToArray();
        }

        /// <summary>
        /// 测试图像匹配（不执行点击），输出详细诊断信息
        /// </summary>
        /// <param name="templatePath">模板图像路径</param>
        /// <param name="threshold">匹配阈值</param>
        /// <returns>匹配结果，包含详细信息</returns>
        public MatchResult TestMatch(string templatePath, double threshold = DefaultThreshold)
        {
            System.Diagnostics.Debug.WriteLine($"===== 图像匹配测试开始 =====");
            System.Diagnostics.Debug.WriteLine($"模板: {templatePath}, 阈值: {threshold:P0}");

            var result = FindImage(templatePath, threshold);
            
            if (result.Found)
            {
                System.Diagnostics.Debug.WriteLine($"✅ 匹配成功: 位置({result.X}, {result.Y}), 相似度 {result.Similarity:P}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"❌ 未找到匹配");

                // 输出最佳匹配分数（帮助用户判断是否只需要降低阈值）
                try
                {
                    using var screenImage = _screenCaptureService.CaptureFullScreen();
                    using var template = _screenCaptureService.LoadImage(templatePath);

                    double bestScore = 0;
                    string bestScale = "";

                    foreach (var scale in _scaleLevels)
                    {
                        int sw = (int)(template.Width * scale);
                        int sh = (int)(template.Height * scale);
                        if (sw < 5 || sh < 5 || sw > screenImage.Width || sh > screenImage.Height)
                            continue;

                        using var scaled = scale == 1.0 ? template.Clone() : template.Resize(sw, sh, Inter.Linear);
                        using var res = new Mat();
                        CvInvoke.MatchTemplate(screenImage, scaled, res, TemplateMatchingType.CcoeffNormed);

                        double minV = 0, maxV = 0;
                        CvInvoke.MinMaxLoc(res, ref minV, ref maxV);

                        if (maxV > bestScore)
                        {
                            bestScore = maxV;
                            bestScale = $"{scale:P0}";
                        }
                    }

                    System.Diagnostics.Debug.WriteLine($"📊 最佳匹配分数: {bestScore:P} (缩放 {bestScale})");
                    if (bestScore > 0.4)
                    {
                        System.Diagnostics.Debug.WriteLine($"💡 建议: 尝试降低阈值到 {Math.Max(0.5, Math.Floor(bestScore * 100) / 100):P0} 以下");
                    }
                    else if (bestScore > 0.2)
                    {
                        System.Diagnostics.Debug.WriteLine("💡 建议: 匹配度较低，建议重新截图");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("💡 建议: 几乎无匹配，目标图像可能已变化，请重新截图");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"诊断信息获取失败: {ex.Message}");
                }
            }

            System.Diagnostics.Debug.WriteLine($"===== 图像匹配测试结束 =====");
            return result;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }
}