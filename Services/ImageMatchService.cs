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
        /// 在源图像中查找模板
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
                    return MatchResult.NotFound;
                }

                // 执行模板匹配
                using var result = new Mat();
                CvInvoke.MatchTemplate(source, template, result, TemplateMatchingType.CcoeffNormed);

                // 查找最佳匹配位置
                double minVal = 0, maxVal = 0;
                Point minLoc = Point.Empty, maxLoc = Point.Empty;
                CvInvoke.MinMaxLoc(result, ref minVal, ref maxVal, ref minLoc, ref maxLoc);

                // 检查是否达到阈值
                if (maxVal >= threshold)
                {
                    return new MatchResult
                    {
                        Found = true,
                        X = maxLoc.X + template.Width / 2,  // 中心点
                        Y = maxLoc.Y + template.Height / 2,
                        Width = template.Width,
                        Height = template.Height,
                        Similarity = maxVal
                    };
                }

                return MatchResult.NotFound;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"图像匹配失败: {ex.Message}");
                return MatchResult.NotFound;
            }
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
        /// 测试图像匹配（不执行点击）
        /// </summary>
        /// <param name="templatePath">模板图像路径</param>
        /// <param name="threshold">匹配阈值</param>
        /// <returns>匹配结果，包含详细信息</returns>
        public MatchResult TestMatch(string templatePath, double threshold = DefaultThreshold)
        {
            var result = FindImage(templatePath, threshold);
            
            if (result.Found)
            {
                System.Diagnostics.Debug.WriteLine($"找到匹配: 位置({result.X}, {result.Y}), 相似度: {result.Similarity:P}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"未找到匹配: 模板 {templatePath}");
            }

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