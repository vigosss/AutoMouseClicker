using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Features2D;
using Emgu.CV.Structure;
using Emgu.CV.Util;

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
    /// 图像匹配服务 - 使用 Emgu.CV 进行高性能模板匹配
    /// 
    /// 优化策略：
    /// 1. 模板缓存：避免重复磁盘IO和图像解码
    /// 2. 灰度单轮搜索：统一使用灰度图 + CcoeffNormed（对亮度变化鲁棒）
    /// 3. 精简缩放级别：从14级减少到9级
    /// 4. 快速路径：1:1匹配命中立即返回
    /// 5. ORB特征验证：对中等置信度匹配做二次验证，消除误匹配
    /// 6. 容错降级：仅在1:1尺度尝试降低阈值，避免全量重复搜索
    /// 7. 上次位置优先搜索：循环宏中优先搜索上次匹配位置附近，提速10倍+
    /// </summary>
    public class ImageMatchService : IDisposable
    {
        private readonly ScreenCaptureService _screenCaptureService;
        private bool _disposed;

        /// <summary>
        /// 模板缓存
        /// </summary>
        private readonly Dictionary<string, TemplateCacheEntry> _templateCache = new();
        private const int MaxCacheEntries = 20;

        /// <summary>
        /// 上次匹配位置缓存（模板路径 → 上次匹配位置）
        /// 用于循环宏中的"附近优先搜索"优化
        /// </summary>
        private readonly Dictionary<string, LastMatchPosition> _lastPositions = new();
        private const int MaxLastPositions = 30;

        /// <summary>
        /// 上次位置附近搜索的扩展半径（像素）
        /// 在上次位置 ±此值 的矩形范围内先搜索
        /// </summary>
        private const int LocalSearchRadius = 200;

        /// <summary>
        /// 上次匹配位置记录
        /// </summary>
        private sealed class LastMatchPosition
        {
            public int X;
            public int Y;
            public int Width;
            public int Height;
        }

        /// <summary>
        /// 默认匹配阈值
        /// </summary>
        public const double DefaultThreshold = 0.8;

        /// <summary>
        /// 匹配超时时间（毫秒）
        /// </summary>
        public int MatchTimeoutMs { get; set; } = 5000;

        /// <summary>
        /// 多尺度搜索的缩放比例列表（精简版，从14级减少到9级）
        /// 顺序：精确匹配 → 小偏移 → 大偏移 → 常见DPI缩放
        /// </summary>
        private static readonly double[] _scaleLevels =
        {
            1.0,           // 精确匹配（最高优先级）
            0.9, 1.1,      // ±10% 偏移
            0.8, 1.2,      // ±20% 偏移
            0.75, 1.25,    // 75%, 125% DPI
            1.5,           // 150% DPI
            2.0,           // 200% DPI
        };

        /// <summary>
        /// ORB验证：高于 阈值+此值 的匹配自动接受，不做ORB验证
        /// </summary>
        private const double HighConfidenceMargin = 0.12;

        /// <summary>
        /// ORB验证：最少需要的特征点数量
        /// </summary>
        private const int OrbMinFeatures = 8;

        /// <summary>
        /// ORB验证：最少需要的良好匹配数量
        /// </summary>
        private const int OrbMinGoodMatches = 3;

        /// <summary>
        /// ORB验证：Lowe's ratio阈值
        /// </summary>
        private const double OrbMatchRatio = 0.65;

        /// <summary>
        /// 模板缓存条目
        /// </summary>
        private sealed class TemplateCacheEntry : IDisposable
        {
            public Image<Bgr, byte>? Color;
            public Image<Gray, byte>? Gray;
            public long FileSize;
            public DateTime LastWrite;
            public int Width;
            public int Height;
            public int MinDimension;

            public void Dispose()
            {
                Color?.Dispose();
                Gray?.Dispose();
            }
        }

        public ImageMatchService(ScreenCaptureService screenCaptureService)
        {
            _screenCaptureService = screenCaptureService ?? throw new ArgumentNullException(nameof(screenCaptureService));
        }

        #region 模板缓存

        /// <summary>
        /// 加载并缓存模板图像（自动检测文件变化并刷新缓存）
        /// </summary>
        private TemplateCacheEntry GetOrCacheTemplate(string templatePath)
        {
            // 解析完整路径
            var fullPath = templatePath;
            if (!Path.IsPathRooted(fullPath))
            {
                fullPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "Data", "screenshots", fullPath);
            }

            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"图像文件不存在: {fullPath}");

            var fileInfo = new FileInfo(fullPath);

            // 检查缓存有效性
            if (_templateCache.TryGetValue(fullPath, out var cached))
            {
                if (cached.FileSize == fileInfo.Length &&
                    cached.LastWrite == fileInfo.LastWriteTimeUtc)
                {
                    return cached;
                }

                // 缓存失效，移除旧条目
                cached.Dispose();
                _templateCache.Remove(fullPath);
            }

            // 加载新模板
            var color = new Image<Bgr, byte>(fullPath);
            var gray = color.Convert<Gray, byte>();

            var entry = new TemplateCacheEntry
            {
                Color = color,
                Gray = gray,
                FileSize = fileInfo.Length,
                LastWrite = fileInfo.LastWriteTimeUtc,
                Width = color.Width,
                Height = color.Height,
                MinDimension = Math.Min(color.Width, color.Height)
            };

            // 缓存满时淘汰最旧的条目
            if (_templateCache.Count >= MaxCacheEntries)
            {
                var oldest = _templateCache.Keys.First();
                _templateCache[oldest].Dispose();
                _templateCache.Remove(oldest);
            }

            _templateCache[fullPath] = entry;

            System.Diagnostics.Debug.WriteLine(
                $"模板已缓存: {Path.GetFileName(fullPath)} ({entry.Width}x{entry.Height})");

            return entry;
        }

        /// <summary>
        /// 清除模板缓存
        /// </summary>
        public void ClearCache()
        {
            foreach (var entry in _templateCache.Values)
                entry.Dispose();
            _templateCache.Clear();
        }

        #endregion

        #region 公开API

        /// <summary>
        /// 在全屏中查找图像（支持上次位置优先搜索）
        /// 
        /// 搜索策略：
        /// 1. 如果有上次匹配位置，先在小区域(±200px)内搜索 → 命中则提速10倍+
        /// 2. 小区域未命中，回退到全屏搜索
        /// 3. 搜索完毕后更新位置缓存
        /// </summary>
        /// <param name="templatePath">模板图像路径</param>
        /// <param name="threshold">匹配阈值 (0.0 - 1.0)</param>
        /// <returns>匹配结果</returns>
        public MatchResult FindImage(string templatePath, double threshold = DefaultThreshold)
        {
            // ===== 上次位置优先搜索 =====
            if (_lastPositions.TryGetValue(templatePath, out var lastPos))
            {
                // 计算局部搜索区域（上次位置 ± 半径）
                var screenSize = Helpers.Win32Api.GetMainScreenSize();
                int localX = Math.Max(0, lastPos.X - LocalSearchRadius);
                int localY = Math.Max(0, lastPos.Y - LocalSearchRadius);
                int localRight = Math.Min(screenSize.Width, lastPos.X + lastPos.Width + LocalSearchRadius);
                int localBottom = Math.Min(screenSize.Height, lastPos.Y + lastPos.Height + LocalSearchRadius);
                int localW = localRight - localX;
                int localH = localBottom - localY;

                if (localW > 10 && localH > 10)
                {
                    try
                    {
                        using var localImage = _screenCaptureService.CaptureRegion(localX, localY, localW, localH);
                        var localResult = FindTemplate(localImage, templatePath, threshold);

                        if (localResult.Found)
                        {
                            // 转换为屏幕绝对坐标
                            localResult.X += localX;
                            localResult.Y += localY;

                            // 更新位置缓存
                            UpdateLastPosition(templatePath, localResult);

                            System.Diagnostics.Debug.WriteLine(
                                $"局部搜索命中: 位置({localResult.X}, {localResult.Y}), 区域 {localW}x{localH}");
                            return localResult;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"局部搜索异常，回退全屏: {ex.Message}");
                    }
                }
            }

            // ===== 全屏搜索（回退路径） =====
            using var screenImage = _screenCaptureService.CaptureFullScreen();
            var result = FindTemplate(screenImage, templatePath, threshold);

            // 更新位置缓存
            if (result.Found)
            {
                UpdateLastPosition(templatePath, result);
            }

            return result;
        }

        /// <summary>
        /// 更新上次匹配位置缓存
        /// </summary>
        private void UpdateLastPosition(string templatePath, MatchResult result)
        {
            if (!result.Found) return;

            // 缓存满时清理
            if (_lastPositions.Count >= MaxLastPositions)
            {
                var oldest = _lastPositions.Keys.First();
                _lastPositions.Remove(oldest);
            }

            _lastPositions[templatePath] = new LastMatchPosition
            {
                X = result.X - result.Width / 2,
                Y = result.Y - result.Height / 2,
                Width = result.Width,
                Height = result.Height
            };
        }

        /// <summary>
        /// 清除上次位置缓存（在不需要位置优化时调用）
        /// </summary>
        public void ClearLastPositions()
        {
            _lastPositions.Clear();
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
        /// 等待图像出现（异步）
        /// </summary>
        /// <param name="templatePath">模板图像路径</param>
        /// <param name="threshold">匹配阈值</param>
        /// <param name="timeoutMs">超时时间（毫秒）</param>
        /// <param name="intervalMs">检查间隔（毫秒）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>匹配结果</returns>
        public async System.Threading.Tasks.Task<MatchResult> WaitForImageAsync(
            string templatePath,
            double threshold = DefaultThreshold,
            int timeoutMs = 30000,
            int intervalMs = 500,
            System.Threading.CancellationToken cancellationToken = default)
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
                var template = GetOrCacheTemplate(templatePath);

                if (template.Width > screenImage.Width || template.Height > screenImage.Height)
                {
                    return results.ToArray();
                }

                using var sourceGray = screenImage.Convert<Gray, byte>();

                // 执行模板匹配（使用灰度图）
                using var result = new Mat();
                CvInvoke.MatchTemplate(sourceGray, template.Gray!, result, TemplateMatchingType.CcoeffNormed);

                // 获取结果数据
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
                    var template = GetOrCacheTemplate(templatePath);
                    using var sourceGray = screenImage.Convert<Gray, byte>();

                    double bestScore = 0;
                    string bestScale = "";

                    foreach (var scale in _scaleLevels)
                    {
                        int sw = (int)(template.Width * scale);
                        int sh = (int)(template.Height * scale);
                        if (sw < 5 || sh < 5 || sw > sourceGray.Width || sh > sourceGray.Height)
                            continue;

                        using var scaled = scale == 1.0
                            ? template.Gray!.Clone()
                            : template.Gray!.Resize(sw, sh, Inter.Linear);
                        using var res = new Mat();
                        CvInvoke.MatchTemplate(sourceGray, scaled, res, TemplateMatchingType.CcoeffNormed);

                        double minV = 0, maxV = 0;
                        Point minL = Point.Empty, maxL = Point.Empty;
                        CvInvoke.MinMaxLoc(res, ref minV, ref maxV, ref minL, ref maxL);

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

        #endregion

        #region 核心匹配逻辑

        /// <summary>
        /// 在源图像中查找模板（优化后的搜索策略）
        /// 
        /// 搜索流程：
        /// 1. 快速路径：1:1灰度匹配 → 高置信度直接返回 / 中置信度ORB验证
        /// 2. 多尺度搜索：遍历剩余缩放级别 → 收集候选结果
        /// 3. 对最佳候选结果做ORB验证
        /// 4. 容错降级：仅在1:1尺度降低阈值重试
        /// </summary>
        private MatchResult FindTemplate(Image<Bgr, byte> source, string templatePath, double threshold)
        {
            try
            {
                var template = GetOrCacheTemplate(templatePath);

                // 源图只转一次灰度，所有尺度复用
                using var sourceGray = source.Convert<Gray, byte>();

                // ===== 阶段1：快速路径 - 1:1精确匹配 =====
                if (template.Width <= source.Width && template.Height <= source.Height)
                {
                    var fastResult = MatchAtScale(sourceGray, template.Gray!, threshold);

                    if (fastResult.Found)
                    {
                        // 高置信度：直接返回（最常见的情况）
                        if (fastResult.Similarity >= threshold + HighConfidenceMargin)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"快速匹配成功: 位置({fastResult.X}, {fastResult.Y}), 相似度 {fastResult.Similarity:P}");
                            return fastResult;
                        }

                        // 中等置信度：用ORB特征验证是否为真匹配
                        if (VerifyWithOrb(sourceGray, template.Gray!, fastResult))
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"ORB验证通过: 位置({fastResult.X}, {fastResult.Y}), 相似度 {fastResult.Similarity:P}");
                            return fastResult;
                        }

                        System.Diagnostics.Debug.WriteLine(
                            $"1:1匹配置信度不足(ORB拒绝): 相似度 {fastResult.Similarity:P}");
                    }
                }

                // ===== 阶段2：多尺度搜索 =====
                MatchResult? bestCandidate = null;

                foreach (var scale in _scaleLevels)
                {
                    if (scale == 1.0) continue; // 已在阶段1尝试

                    int sw = (int)(template.Width * scale);
                    int sh = (int)(template.Height * scale);

                    if (sw < 5 || sh < 5 || sw > source.Width || sh > source.Height)
                        continue;

                    using var scaledGray = template.Gray!.Resize(sw, sh, Inter.Linear);
                    var match = MatchAtScale(sourceGray, scaledGray, threshold);

                    if (match.Found)
                    {
                        // 高置信度直接返回
                        if (match.Similarity >= threshold + HighConfidenceMargin)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"多尺度匹配成功: 缩放 {scale:P0}, 位置({match.X}, {match.Y}), 相似度 {match.Similarity:P}");
                            return match;
                        }

                        // 记录最佳候选
                        if (bestCandidate == null || match.Similarity > bestCandidate.Similarity)
                        {
                            bestCandidate = match;
                        }
                    }
                }

                // 对最佳候选做ORB验证
                if (bestCandidate != null)
                {
                    var bestScale = (double)bestCandidate.Width / template.Width;
                    int bsw = bestCandidate.Width;
                    int bsh = bestCandidate.Height;
                    using var bestScaledGray = template.Gray!.Resize(bsw, bsh, Inter.Linear);

                    if (VerifyWithOrb(sourceGray, bestScaledGray, bestCandidate))
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"多尺度+ORB验证: 缩放 {bestScale:P0}, 位置({bestCandidate.X}, {bestCandidate.Y}), 相似度 {bestCandidate.Similarity:P}");
                        return bestCandidate;
                    }
                }

                // ===== 阶段3：容错降级 - 仅在1:1尺度降低阈值 =====
                if (threshold > 0.5 && template.Width <= source.Width && template.Height <= source.Height)
                {
                    var loweredThreshold = Math.Max(0.5, threshold - 0.15);
                    System.Diagnostics.Debug.WriteLine($"尝试降低阈值到 {loweredThreshold:P0} ...");

                    var relaxedResult = MatchAtScale(sourceGray, template.Gray!, loweredThreshold);
                    if (relaxedResult.Found)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"降低阈值匹配成功: 位置({relaxedResult.X}, {relaxedResult.Y}), 相似度 {relaxedResult.Similarity:P}");
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
        /// 单尺度模板匹配
        /// </summary>
        /// <param name="source">源图像（灰度）</param>
        /// <param name="template">模板图像（灰度，已缩放到目标尺度）</param>
        /// <param name="threshold">匹配阈值</param>
        /// <returns>匹配结果</returns>
        private MatchResult MatchAtScale(Image<Gray, byte> source, Image<Gray, byte> template, double threshold)
        {
            if (template.Width > source.Width || template.Height > source.Height)
                return MatchResult.NotFound;

            using var result = new Mat();
            CvInvoke.MatchTemplate(source, template, result, TemplateMatchingType.CcoeffNormed);

            double minVal = 0, maxVal = 0;
            Point minLoc = Point.Empty, maxLoc = Point.Empty;
            CvInvoke.MinMaxLoc(result, ref minVal, ref maxVal, ref minLoc, ref maxLoc);

            if (maxVal >= threshold)
            {
                return new MatchResult
                {
                    Found = true,
                    X = maxLoc.X + template.Width / 2,
                    Y = maxLoc.Y + template.Height / 2,
                    Width = template.Width,
                    Height = template.Height,
                    Similarity = maxVal
                };
            }

            return MatchResult.NotFound;
        }

        /// <summary>
        /// ORB特征点验证 - 对模板匹配结果做二次确认，消除误匹配
        /// 
        /// 原理：在匹配区域内提取ORB特征点，与模板特征点进行匹配，
        /// 如果有足够多的良好匹配，则确认这是一个正确的匹配。
        /// 
        /// 对于特征点过少的模板（如纯色/渐变区域），自动跳过验证，信任模板匹配结果。
        /// </summary>
        /// <param name="source">源图像（灰度）</param>
        /// <param name="template">模板图像（灰度）</param>
        /// <param name="matchResult">模板匹配的结果</param>
        /// <returns>true 表示验证通过（是正确匹配），false 表示验证失败（可能是误匹配）</returns>
        private bool VerifyWithOrb(Image<Gray, byte> source, Image<Gray, byte> template, MatchResult matchResult)
        {
            try
            {
                // 小模板特征点太少，ORB验证不可靠，直接信任模板匹配
                if (template.Width < 32 || template.Height < 32)
                {
                    System.Diagnostics.Debug.WriteLine("模板过小，跳过ORB验证");
                    return true;
                }

                int templateMinDim = Math.Min(template.Width, template.Height);

                // 提取匹配区域
                int roiX = matchResult.X - matchResult.Width / 2;
                int roiY = matchResult.Y - matchResult.Height / 2;

                // 边界检查
                roiX = Math.Max(0, Math.Min(roiX, source.Width - matchResult.Width));
                roiY = Math.Max(0, Math.Min(roiY, source.Height - matchResult.Height));
                int roiW = Math.Min(matchResult.Width, source.Width - roiX);
                int roiH = Math.Min(matchResult.Height, source.Height - roiY);

                if (roiW <= 0 || roiH <= 0)
                    return true;

                using var roi = new Mat(source.Mat, new Rectangle(roiX, roiY, roiW, roiH));
                using var roiGray = roi.ToImage<Gray, byte>();

                // 创建ORB检测器（根据模板大小调整参数）
                int maxFeatures = Math.Max(200, template.Width * template.Height / 100);
                maxFeatures = Math.Min(maxFeatures, 1000);

                using var orb = new ORB(
                    maxFeatures,
                    1.2f,
                    8,
                    Math.Min(31, templateMinDim / 2),
                    0,
                    2,
                    ORB.ScoreType.HarrisScore,
                    Math.Min(31, templateMinDim / 2),
                    20);

                // 检测模板特征
                using var templateKp = new VectorOfKeyPoint();
                using var templateDesc = new Mat();
                orb.DetectAndCompute(template, null, templateKp, templateDesc, false);

                // 特征点太少，无法可靠验证
                if (templateKp.Size < OrbMinFeatures || templateDesc.Rows < OrbMinFeatures)
                {
                    System.Diagnostics.Debug.WriteLine($"模板特征点不足({templateKp.Size})，跳过ORB验证");
                    return true;
                }

                // 检测ROI特征
                using var roiKp = new VectorOfKeyPoint();
                using var roiDesc = new Mat();
                orb.DetectAndCompute(roiGray, null, roiKp, roiDesc, false);

                if (roiKp.Size < OrbMinFeatures || roiDesc.Rows < 2)
                {
                    System.Diagnostics.Debug.WriteLine($"ROI特征点不足({roiKp.Size})，跳过ORB验证");
                    return true;
                }

                // BFMatcher + Hamming距离（ORB的二进制描述子专用）
                using var matcher = new BFMatcher(DistanceType.Hamming);
                using var matches = new VectorOfVectorOfDMatch();
                matcher.KnnMatch(roiDesc, templateDesc, matches, 2);

                // Lowe's ratio test - 筛选良好匹配
                int goodMatches = 0;
                for (int i = 0; i < matches.Size; i++)
                {
                    if (matches[i].Size >= 2)
                    {
                        var m = matches[i][0];
                        var n = matches[i][1];
                        if (m.Distance < OrbMatchRatio * n.Distance)
                        {
                            goodMatches++;
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine(
                    $"ORB验证: 模板特征={templateKp.Size}, ROI特征={roiKp.Size}, 良好匹配={goodMatches}, 结果={goodMatches >= OrbMinGoodMatches}");

                return goodMatches >= OrbMinGoodMatches;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ORB验证异常(信任模板匹配): {ex.Message}");
                return true; // 出错时信任模板匹配结果
            }
        }

        #endregion

        public void Dispose()
        {
            if (!_disposed)
            {
                ClearCache();
                _lastPositions.Clear();
                _disposed = true;
            }
        }
    }
}