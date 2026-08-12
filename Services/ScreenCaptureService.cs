using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Ming_AutoClicker.Helpers;

namespace Ming_AutoClicker.Services
{
    /// <summary>
    /// 屏幕截图服务 - 负责屏幕捕获
    /// </summary>
    public class ScreenCaptureService : IDisposable
    {
        private readonly string _screenshotDirectory;
        private bool _disposed;


        public ScreenCaptureService()
        {
            // 截图存储目录
            _screenshotDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "screenshots");
            
            // 确保目录存在
            if (!Directory.Exists(_screenshotDirectory))
            {
                Directory.CreateDirectory(_screenshotDirectory);
            }
        }

        /// <summary>
        /// 捕获全屏截图
        /// </summary>
        /// <returns>截图的 Emgu.CV Image 对象</returns>
        public Image<Bgr, byte> CaptureFullScreen()
        {
            // 捕获整个虚拟桌面，包含主屏和所有外接显示器。
            var (x, y, width, height) = Win32Api.GetVirtualScreenBounds();
            return CaptureRegion(x, y, width, height);
        }

        /// <summary>
        /// 捕获指定区域的截图
        /// </summary>
        /// <param name="x">起始 X 坐标</param>
        /// <param name="y">起始 Y 坐标</param>
        /// <param name="width">宽度</param>
        /// <param name="height">高度</param>
        /// <returns>截图的 Emgu.CV Image 对象</returns>
        public Image<Bgr, byte> CaptureRegion(int x, int y, int width, int height)
        {
            // 创建 Bitmap（不使用 using，因为需要在转换后保留）
            var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);

            try
            {
                // 使用 Graphics 复制屏幕内容
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height));
                }

                // 转换为 Emgu.CV Image（会复制数据）
                var image = bitmap.ToImage<Bgr, byte>();
                return image;
            }
            finally
            {
                // 转换完成后释放 Bitmap
                bitmap.Dispose();
            }
        }

        /// <summary>
        /// 捕获全屏并保存到文件
        /// </summary>
        /// <param name="fileName">文件名（不含路径）</param>
        /// <returns>保存的文件路径</returns>
        public string CaptureAndSave(string? fileName = null)
        {
            using var image = CaptureFullScreen();
            return SaveImage(image, fileName);
        }

        /// <summary>
        /// 捕获指定区域并保存到文件
        /// </summary>
        /// <param name="x">起始 X 坐标</param>
        /// <param name="y">起始 Y 坐标</param>
        /// <param name="width">宽度</param>
        /// <param name="height">高度</param>
        /// <param name="fileName">文件名（不含路径）</param>
        /// <returns>保存的文件路径</returns>
        public string CaptureRegionAndSave(int x, int y, int width, int height, string? fileName = null)
        {
            using var image = CaptureRegion(x, y, width, height);
            return SaveImage(image, fileName);
        }

        /// <summary>
        /// 保存 Bitmap 到文件（用于保存已裁剪的截图，避免重新截屏）
        /// </summary>
        /// <param name="bitmap">System.Drawing.Bitmap 对象（调用方负责释放）</param>
        /// <param name="fileName">文件名（可选）</param>
        /// <returns>保存的文件路径</returns>
        public string SaveBitmap(System.Drawing.Bitmap bitmap, string? fileName = null)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            // 生成文件名
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
            }
            else if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".png";
            }

            var filePath = Path.Combine(_screenshotDirectory, fileName);

            // 验证路径安全性
            var fullPath = Path.GetFullPath(filePath);
            var baseDir = Path.GetFullPath(_screenshotDirectory);

            if (!fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException($"不允许保存到截图目录外: {fileName}");
            }

            // 保存为 PNG 格式
            bitmap.Save(fullPath, ImageFormat.Png);

            return fullPath;
        }

        /// <summary>
        /// 将用户选择的本地图像导入截图目录，并统一保存为 PNG。
        /// </summary>
        public string ImportImage(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                throw new ArgumentException("图片路径不能为空", nameof(sourcePath));
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("图片文件不存在", sourcePath);

            // Bitmap 构造过程会校验文件确实是可读取的图像，而不仅依赖扩展名。
            using var source = new System.Drawing.Bitmap(sourcePath);
            var originalName = Path.GetFileNameWithoutExtension(sourcePath);
            foreach (var invalidChar in Path.GetInvalidFileNameChars())
            {
                originalName = originalName.Replace(invalidChar, '_');
            }
            if (string.IsNullOrWhiteSpace(originalName)) originalName = "image";

            return SaveBitmap(source, $"import_{originalName}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
        }

        /// <summary>
        /// 保存图像到文件
        /// </summary>
        /// <param name="image">图像对象</param>
        /// <param name="fileName">文件名（可选）</param>
        /// <returns>保存的文件路径</returns>
        public string SaveImage(Image<Bgr, byte> image, string? fileName = null)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));

            // 生成文件名
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
            }
            else if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".png";
            }

            var filePath = Path.Combine(_screenshotDirectory, fileName);

            // 验证路径安全性
            var fullPath = Path.GetFullPath(filePath);
            var baseDir = Path.GetFullPath(_screenshotDirectory);

            if (!fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException($"不允许保存到截图目录外: {fileName}");
            }

            // 保存为 PNG 格式
            image.Save(fullPath);

            return fullPath;
        }

        /// <summary>
        /// 从文件加载图像
        /// </summary>
        /// <param name="filePath">文件路径（可以是相对路径或绝对路径）</param>
        /// <returns>Emgu.CV Image 对象</returns>
        public Image<Bgr, byte> LoadImage(string filePath)
        {
            // 处理相对路径
            if (!Path.IsPathRooted(filePath))
            {
                filePath = Path.Combine(_screenshotDirectory, filePath);
            }

            // 验证路径安全性
            var fullPath = Path.GetFullPath(filePath);
            var baseDir = Path.GetFullPath(_screenshotDirectory);

            if (!fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException($"不允许访问截图目录外的文件: {filePath}");
            }

            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"图像文件不存在: {fullPath}");

            return new Image<Bgr, byte>(fullPath);
        }

        /// <summary>
        /// 获取截图目录路径
        /// </summary>
        public string GetScreenshotDirectory()
        {
            return _screenshotDirectory;
        }

        /// <summary>
        /// 获取图像的相对路径（相对于截图目录）
        /// </summary>
        public string GetRelativePath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
                return absolutePath;

            var fullPath = Path.GetFullPath(absolutePath);
            var basePath = Path.GetFullPath(_screenshotDirectory);

            if (fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath.Substring(basePath.Length).TrimStart(Path.DirectorySeparatorChar);
            }

            return absolutePath;
        }

        /// <summary>
        /// 删除截图文件
        /// </summary>
        /// <param name="fileName">文件名或相对路径</param>
        /// <returns>是否删除成功</returns>
        public bool DeleteScreenshot(string fileName)
        {
            var filePath = Path.IsPathRooted(fileName)
                ? fileName
                : Path.Combine(_screenshotDirectory, fileName);

            // 验证路径安全性
            var fullPath = Path.GetFullPath(filePath);
            var baseDir = Path.GetFullPath(_screenshotDirectory);

            if (!fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException($"不允许删除截图目录外的文件: {fileName}");
            }

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 获取所有截图文件
        /// </summary>
        /// <returns>截图文件路径列表</returns>
        public string[] ListScreenshots()
        {
            if (!Directory.Exists(_screenshotDirectory))
                return Array.Empty<string>();

            return Directory.GetFiles(_screenshotDirectory, "*.png");
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
