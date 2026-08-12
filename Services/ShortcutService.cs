using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Ming_AutoClicker.Services
{
    /// <summary>
    /// 桌面快捷方式管理服务
    /// 负责首次选择、已有快捷方式修复，以及尊重用户后续删除操作
    /// </summary>
    public static class ShortcutService
    {
        private const string ShortcutName = "智点精灵.lnk";
        private const string PreferenceDirectoryName = "Ming-AutoClicker";
        private const string PreferenceFileName = "desktop-shortcut-choice";

        /// <summary>
        /// 初始化桌面快捷方式。
        /// 已存在时校验并修复目标；从未选择过且不存在时，仅询问一次。
        /// </summary>
        public static void EnsureDesktopShortcut(Func<bool>? confirmCreate)
        {
            try
            {
                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (string.IsNullOrWhiteSpace(desktopPath))
                {
                    Debug.WriteLine("[快捷方式] 无法获取桌面目录");
                    return;
                }

                var shortcutPath = Path.Combine(desktopPath, ShortcutName);
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    Debug.WriteLine("[快捷方式] 无法获取当前 exe 路径");
                    return;
                }

                // 已存在时检查目标路径，应用移动或更新目录后自动修复。
                if (File.Exists(shortcutPath))
                {
                    CreateOrUpdateShortcut(shortcutPath, exePath);
                    RecordChoice();
                    return;
                }

                // 用户已做过选择，或创建后又主动删除时，不再强制重建。
                if (HasRecordedChoice())
                {
                    Debug.WriteLine("[快捷方式] 用户已选择过且快捷方式不存在，跳过创建");
                    return;
                }

                var shouldCreate = confirmCreate?.Invoke() == true;
                // 先记录选择，确保创建失败时也不会在每次启动时重复打扰用户。
                RecordChoice();
                if (shouldCreate)
                {
                    CreateOrUpdateShortcut(shortcutPath, exePath);
                    Debug.WriteLine($"[快捷方式] 已创建桌面快捷方式: {shortcutPath}");
                }
                else
                {
                    Debug.WriteLine("[快捷方式] 用户选择不创建桌面快捷方式");
                }

            }
            catch (Exception ex)
            {
                // 快捷方式创建失败不影响正常使用
                Debug.WriteLine($"[快捷方式] 创建桌面快捷方式失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 使用 COM 接口创建 .lnk 快捷方式
        /// </summary>
        private static void CreateOrUpdateShortcut(string shortcutPath, string targetPath)
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
                throw new InvalidOperationException("当前系统不支持 WScript.Shell");

            dynamic? shell = null;
            dynamic? shortcut = null;

            try
            {
                shell = Activator.CreateInstance(shellType);
                if (shell == null) return;

                shortcut = shell.CreateShortcut(shortcutPath);

                // 获取图标路径：优先使用 exe 内嵌图标，否则使用 Assets 目录下的 ico
                var appDir = AppContext.BaseDirectory;
                var icoPath = System.IO.Path.Combine(appDir, "Assets", "favicon.ico");
                var iconLocation = File.Exists(icoPath)
                    ? $"{icoPath},0"
                    : $"{targetPath},0";
                const string description = "智点精灵 - 自动点击工具";

                var needsUpdate = !PathsEqual((string?)shortcut.TargetPath, targetPath) ||
                    !PathsEqual((string?)shortcut.WorkingDirectory, appDir) ||
                    !string.Equals((string?)shortcut.IconLocation, iconLocation, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals((string?)shortcut.Description, description, StringComparison.Ordinal);

                if (needsUpdate)
                {
                    shortcut.IconLocation = iconLocation;
                    shortcut.TargetPath = targetPath;
                    shortcut.WorkingDirectory = appDir;
                    shortcut.Description = description;
                    shortcut.Save();
                    Debug.WriteLine($"[快捷方式] 已创建或修复快捷方式目标: {targetPath}");
                }
                else
                {
                    Debug.WriteLine("[快捷方式] 快捷方式目标正确，无需更新");
                }
            }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut))
                {
                    Marshal.FinalReleaseComObject(shortcut);
                }
                if (shell != null && Marshal.IsComObject(shell))
                {
                    Marshal.FinalReleaseComObject(shell);
                }
            }
        }

        private static bool HasRecordedChoice()
        {
            try
            {
                return File.Exists(GetPreferencePath());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[快捷方式] 读取用户选择失败: {ex.Message}");
                return false;
            }
        }

        private static bool PathsEqual(string? left, string right)
        {
            if (string.IsNullOrWhiteSpace(left)) return false;
            try
            {
                return string.Equals(
                    Path.GetFullPath(left),
                    Path.GetFullPath(right),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void RecordChoice()
        {
            try
            {
                var preferencePath = GetPreferencePath();
                var directory = Path.GetDirectoryName(preferencePath)!;
                Directory.CreateDirectory(directory);
                File.WriteAllText(preferencePath, "completed");
            }
            catch (Exception ex)
            {
                // 记录失败不影响程序或已经创建的快捷方式。
                Debug.WriteLine($"[快捷方式] 保存用户选择失败: {ex.Message}");
            }
        }

        private static string GetPreferencePath()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
                throw new InvalidOperationException("无法获取本地应用数据目录");

            return Path.Combine(localAppData, PreferenceDirectoryName, PreferenceFileName);
        }
    }
}
