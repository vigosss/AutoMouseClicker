using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using System.Windows.Interop;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;

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
            // 获取屏幕尺寸
            var screenWidth = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width;
            var screenHeight = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height;

            return CaptureRegion(0, 0, screenWidth, screenHeight);
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
            // 创建 Bitmap
            using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            
            // 使用 Graphics 复制屏幕内容
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height));
            }

            // 转换为 Emgu.CV Image
            return bitmap.ToImage<Bgr, byte>();
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
                fileName = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            }
            else if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".png";
            }

            var filePath = Path.Combine(_screenshotDirectory, fileName);
            
            // 保存为 PNG 格式
            image.Save(filePath);

            return filePath;
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

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"图像文件不存在: {filePath}");

            return new Image<Bgr, byte>(filePath);
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

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
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