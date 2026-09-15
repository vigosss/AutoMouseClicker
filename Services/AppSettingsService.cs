using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Ming_AutoClicker.Models;

namespace Ming_AutoClicker.Services
{
    /// <summary>
    /// 按 Windows 用户保存应用设置，应用更新不会覆盖此文件。
    /// </summary>
    public sealed class AppSettingsService
    {
        private const string DirectoryName = "Ming-AutoClicker";
        private const string FileName = "settings.json";
        private readonly string _settingsPath;
        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public AppSettingsService()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
                throw new InvalidOperationException("无法获取本地应用数据目录");

            _settingsPath = Path.Combine(localAppData, DirectoryName, FileName);
        }

        public AppSettings Load()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                    return CreateDefault();

                var settings = JsonSerializer.Deserialize<AppSettings>(
                    File.ReadAllText(_settingsPath), _jsonOptions);

                if (settings?.Hotkeys?.ToggleExecution == null ||
                    !HotkeyGestureHelper.TryValidate(settings.Hotkeys.ToggleExecution, out _))
                {
                    Debug.WriteLine("[设置] 热键配置无效，已使用默认 F8");
                    return CreateDefault();
                }

                return settings;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[设置] 读取失败，已使用默认 F8: {ex.Message}");
                return CreateDefault();
            }
        }

        public void Save(AppSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            var directory = Path.GetDirectoryName(_settingsPath)!;
            Directory.CreateDirectory(directory);

            var tempPath = _settingsPath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                var json = JsonSerializer.Serialize(settings, _jsonOptions);
                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(json);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }

                File.Move(tempPath, _settingsPath, overwrite: true);
            }
            catch
            {
                try { File.Delete(tempPath); } catch { }
                throw;
            }
        }

        private static AppSettings CreateDefault() => new AppSettings();
    }
}
