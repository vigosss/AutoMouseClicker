using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Ming_AutoClicker.Services
{
    /// <summary>
    /// 桌面快捷方式管理服务
    /// 在首次启动时自动在桌面创建应用快捷方式
    /// </summary>
    public static class ShortcutService
    {
        private const string ShortcutName = "智点精灵.lnk";

        /// <summary>
        /// 确保桌面快捷方式存在，若不存在则自动创建
        /// </summary>
        public static void EnsureDesktopShortcut()
        {
            try
            {
                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var shortcutPath = Path.Combine(desktopPath, ShortcutName);

                // 已存在则跳过
                if (File.Exists(shortcutPath))
                {
                    Debug.WriteLine("[快捷方式] 桌面快捷方式已存在，跳过创建");
                    return;
                }

                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    Debug.WriteLine("[快捷方式] 无法获取当前 exe 路径");
                    return;
                }

                CreateShortcut(shortcutPath, exePath);
                Debug.WriteLine($"[快捷方式] 已创建桌面快捷方式: {shortcutPath}");
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
        private static void CreateShortcut(string shortcutPath, string targetPath)
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell")!;
            dynamic? shell = null;

            try
            {
                shell = Activator.CreateInstance(shellType);
                if (shell == null) return;

                var shortcut = shell.CreateShortcut(shortcutPath);

                // 获取图标路径：优先使用 exe 内嵌图标，否则使用 Assets 目录下的 ico
                var appDir = AppContext.BaseDirectory;
                var icoPath = System.IO.Path.Combine(appDir, "Assets", "favicon.ico");
                shortcut.IconLocation = File.Exists(icoPath)
                    ? $"{icoPath},0"
                    : $"{targetPath},0";

                shortcut.TargetPath = targetPath;
                shortcut.WorkingDirectory = appDir;
                shortcut.Description = "智点精灵 - 自动点击工具";
                shortcut.Save();
            }
            finally
            {
                if (shell != null)
                {
                    Marshal.ReleaseComObject(shell);
                }
            }
        }
    }
}
</write_to_file>
<task_progress>
- [x] 创建 Services/ShortcutService.cs（快捷方式创建服务）
- [ ] 修改 App.xaml.cs，在启动时调用快捷方式创建逻辑
</task_progress>
</thinking>