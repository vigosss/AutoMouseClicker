using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ming_AutoClicker.Models;

namespace Ming_AutoClicker.Services
{
    /// <summary>
    /// 版本更新服务
    /// 负责检查更新、下载新版本、执行替换更新
    /// </summary>
    public class UpdateService : IDisposable
    {
        private const string GitHubOwner = "vigosss";
        private const string GitHubRepo = "Ming-AutoClicker";
        private const string UpdateFolderName = "update";
        private const string UpdaterBatchFileName = "updater.bat";

        private static readonly string AppDirectory = AppDomain.CurrentDomain.BaseDirectory;
        private static readonly string UpdateDirectory = Path.Combine(AppDirectory, UpdateFolderName);

        private readonly HttpClient _httpClient;
        private bool _disposed;

        /// <summary>
        /// 下载进度变化事件
        /// </summary>
        public event EventHandler<DownloadProgressEventArgs>? DownloadProgressChanged;

        /// <summary>
        /// 下载完成事件
        /// </summary>
        public event EventHandler<bool>? DownloadCompleted;

        public UpdateService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", $"{GitHubRepo}/UpdateService");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
        }

        /// <summary>
        /// 获取当前应用版本
        /// </summary>
        public Version GetCurrentVersion()
        {
            var versionString = Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion ?? "0.1.0";

            // 去掉版本号中可能的后缀（如 "0.1.0+abc123" → "0.1.0"）
            var cleanVersion = versionString.Split('+', '-')[0];
            return Version.TryParse(cleanVersion, out var version) ? version : new Version(0, 1, 0);
        }

        /// <summary>
        /// 异步检查是否有新版本可用
        /// </summary>
        /// <returns>版本检查结果</returns>
        public async Task<UpdateCheckResult> CheckForUpdateAsync()
        {
            var result = new UpdateCheckResult
            {
                CurrentVersion = GetCurrentVersion()
            };

            try
            {
                var apiUrl = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
                var response = await _httpClient.GetAsync(apiUrl);

                if (!response.IsSuccessStatusCode)
                {
                    result.ErrorMessage = $"API 请求失败: {response.StatusCode}";
                    return result;
                }

                var json = await response.Content.ReadAsStringAsync();
                var release = JsonSerializer.Deserialize<GitHubRelease>(json);

                if (release == null || string.IsNullOrEmpty(release.TagName))
                {
                    result.ErrorMessage = "无法解析版本信息";
                    return result;
                }

                // 跳过预发布和草稿
                if (release.PreRelease || release.Draft)
                {
                    result.ErrorMessage = "最新版本尚未正式发布";
                    return result;
                }

                // 解析版本号（去掉 'v' 前缀）
                var versionTag = release.TagName.TrimStart('v', 'V');
                if (!Version.TryParse(versionTag, out var latestVersion))
                {
                    result.ErrorMessage = $"无法解析版本号: {release.TagName}";
                    return result;
                }

                result.LatestVersion = latestVersion;
                result.LatestVersionTag = release.TagName;
                result.ReleaseNotes = release.Body ?? "暂无更新说明";
                result.PublishedAt = release.PublishedAt;
                result.ReleasePageUrl = release.HtmlUrl;

                // 查找 zip 格式的下载资产
                var zipAsset = release.Assets.FirstOrDefault(a =>
                    a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

                if (zipAsset != null)
                {
                    result.DownloadUrl = zipAsset.DownloadUrl;
                    result.DownloadSize = zipAsset.Size;
                }
                else
                {
                    // 没有 zip 资产，使用 Release 页面作为备用
                    result.DownloadUrl = string.Empty;
                    result.DownloadSize = 0;
                }

                // 比较版本号
                result.HasUpdate = latestVersion > result.CurrentVersion;
            }
            catch (TaskCanceledException)
            {
                result.ErrorMessage = "请求超时，请检查网络连接";
            }
            catch (HttpRequestException ex)
            {
                result.ErrorMessage = $"网络错误: {ex.Message}";
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"检查更新失败: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// 下载新版本到临时目录
        /// </summary>
        /// <param name="updateInfo">版本检查结果</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>下载的文件路径；失败返回 null</returns>
        public async Task<string?> DownloadUpdateAsync(UpdateCheckResult updateInfo, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(updateInfo.DownloadUrl))
            {
                return null;
            }

            try
            {
                // 创建更新目录
                if (!Directory.Exists(UpdateDirectory))
                {
                    Directory.CreateDirectory(UpdateDirectory);
                }

                // 清理之前的下载
                CleanUpdateDirectory();

                var fileName = $"Ming-AutoClicker-{updateInfo.LatestVersionTag}.zip";
                var filePath = Path.Combine(UpdateDirectory, fileName);

                // 使用带进度报告的下载
                using var response = await _httpClient.GetAsync(
                    updateInfo.DownloadUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? updateInfo.DownloadSize;
                var downloadedBytes = 0L;

                await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                int bytesRead;
                var lastProgressReport = DateTime.MinValue;

                while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    downloadedBytes += bytesRead;

                    // 限制进度报告频率，避免 UI 线程压力
                    var now = DateTime.UtcNow;
                    if (now - lastProgressReport > TimeSpan.FromMilliseconds(100))
                    {
                        lastProgressReport = now;
                        var progress = totalBytes > 0 ? (double)downloadedBytes / totalBytes : 0;
                        OnDownloadProgress(downloadedBytes, totalBytes, progress);
                    }
                }

                // 最终进度报告
                OnDownloadProgress(downloadedBytes, totalBytes, 1.0);
                OnDownloadCompleted(true);

                return filePath;
            }
            catch (OperationCanceledException)
            {
                CleanUpdateDirectory();
                OnDownloadCompleted(false);
                return null;
            }
            catch (Exception)
            {
                CleanUpdateDirectory();
                OnDownloadCompleted(false);
                return null;
            }
        }

        /// <summary>
        /// 执行更新：解压下载的文件，启动更新脚本，关闭当前应用
        /// </summary>
        /// <param name="zipFilePath">下载的 zip 文件路径</param>
        /// <returns>是否成功启动更新</returns>
        public bool ApplyUpdate(string zipFilePath)
        {
            try
            {
                if (!File.Exists(zipFilePath))
                    return false;

                // 解压 zip 到更新目录的子文件夹
                var extractPath = Path.Combine(UpdateDirectory, "extracted");
                if (Directory.Exists(extractPath))
                {
                    Directory.Delete(extractPath, true);
                }
                Directory.CreateDirectory(extractPath);

                ZipFile.ExtractToDirectory(zipFilePath, extractPath, overwriteFiles: true);

                // 查找解压后的文件：可能在 extracted 直接下，也可能在子目录下
                var sourceDir = FindPublishedDirectory(extractPath);
                if (sourceDir == null)
                {
                    return false;
                }

                // 生成更新批处理脚本
                var batchPath = Path.Combine(UpdateDirectory, UpdaterBatchFileName);
                CreateUpdateBatch(batchPath, sourceDir, AppDirectory);

                // 启动更新脚本
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c \"{batchPath}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                    }
                };

                process.Start();

                // 关闭当前应用
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    System.Windows.Application.Current.Shutdown();
                });

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 清理更新临时目录
        /// </summary>
        public void CleanUpdateDirectory()
        {
            try
            {
                if (Directory.Exists(UpdateDirectory))
                {
                    // 只删除 zip 文件和解压目录，保留 updater.bat（可能正在运行）
                    foreach (var file in Directory.GetFiles(UpdateDirectory, "*.zip"))
                    {
                        try { File.Delete(file); } catch { }
                    }

                    var extractedPath = Path.Combine(UpdateDirectory, "extracted");
                    if (Directory.Exists(extractedPath))
                    {
                        try { Directory.Delete(extractedPath, true); } catch { }
                    }
                }
            }
            catch
            {
                // 清理失败不影响主流程
            }
        }

        /// <summary>
        /// 在解压目录中查找包含发布文件的实际目录
        /// zip 可能包含一个子目录，也可能直接包含文件
        /// </summary>
        private string? FindPublishedDirectory(string extractPath)
        {
            // 检查是否有 exe 文件在根目录
            if (Directory.GetFiles(extractPath, "*.exe").Any() ||
                Directory.GetFiles(extractPath, "*.dll").Any())
            {
                return extractPath;
            }

            // 检查子目录
            foreach (var dir in Directory.GetDirectories(extractPath))
            {
                if (Directory.GetFiles(dir, "*.exe").Any() ||
                    Directory.GetFiles(dir, "*.dll").Any())
                {
                    return dir;
                }
            }

            return null;
        }

        /// <summary>
        /// 创建更新替换的批处理脚本
        /// 流程：等待应用退出 → 复制文件 → 启动新版本 → 删除临时文件
        /// </summary>
        private void CreateUpdateBatch(string batchPath, string sourceDir, string targetDir)
        {
            var appName = Path.GetFileName(Environment.ProcessPath ?? "Ming-AutoClicker.exe");
            var appExePath = Path.Combine(targetDir, appName);
            var newAppExePath = Path.Combine(sourceDir, appName);

            // 使用短路径名避免路径中有空格和中文的问题
            var script = $@"@echo off
chcp 65001 >nul 2>&1
echo 正在更新 智点精灵，请稍候...

:: 等待应用进程退出（最多等 30 秒）
set /a wait_count=0
:wait_exit
tasklist /fi ""imagename eq {appName}"" 2>nul | find /i ""{appName}"" >nul
if %errorlevel%==0 (
    set /a wait_count+=1
    if %wait_count% geq 30 (
        echo 更新超时，请手动关闭应用后重试。
        pause
        goto cleanup
    )
    timeout /t 1 /nobreak >nul
    goto wait_exit
)

:: 复制所有文件（覆盖）
echo 正在替换文件...
xcopy ""{sourceDir}\*"" ""{targetDir}\"" /e /y /q >nul 2>&1

:: 启动新版本
echo 正在启动新版本...
start "" ""{appExePath}""

:: 清理临时文件
:cleanup
timeout /t 2 /nobreak >nul
rd /s /q ""{UpdateDirectory}"" 2>nul
exit
";
            File.WriteAllText(batchPath, script, System.Text.Encoding.UTF8);
        }

        protected virtual void OnDownloadProgress(long downloadedBytes, long totalBytes, double progress)
        {
            DownloadProgressChanged?.Invoke(this, new DownloadProgressEventArgs(downloadedBytes, totalBytes, progress));
        }

        protected virtual void OnDownloadCompleted(bool success)
        {
            DownloadCompleted?.Invoke(this, success);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _httpClient.Dispose();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// 下载进度事件参数
    /// </summary>
    public class DownloadProgressEventArgs : EventArgs
    {
        /// <summary>
        /// 已下载字节数
        /// </summary>
        public long DownloadedBytes { get; }

        /// <summary>
        /// 总字节数
        /// </summary>
        public long TotalBytes { get; }

        /// <summary>
        /// 下载进度百分比（0.0 ~ 1.0）
        /// </summary>
        public double Progress { get; }

        /// <summary>
        /// 格式化的已下载大小（如 "12.5 MB"）
        /// </summary>
        public string DownloadedText => FormatFileSize(DownloadedBytes);

        /// <summary>
        /// 格式化的总大小（如 "50.0 MB"）
        /// </summary>
        public string TotalText => FormatFileSize(TotalBytes);

        /// <summary>
        /// 进度百分比文本（如 "25%"）
        /// </summary>
        public string ProgressText => $"{(int)(Progress * 100)}%";

        public DownloadProgressEventArgs(long downloadedBytes, long totalBytes, double progress)
        {
            DownloadedBytes = downloadedBytes;
            TotalBytes = totalBytes;
            Progress = progress;
        }

        private static string FormatFileSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            var size = (double)bytes;
            var unitIndex = 0;

            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return $"{size:0.0} {units[unitIndex]}";
        }
    }
}