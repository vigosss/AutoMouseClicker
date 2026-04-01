using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Ming_AutoClicker.Views
{
    /// <summary>
    /// 自定义对话框类型
    /// </summary>
    public enum DialogType
    {
        /// <summary>信息提示（蓝色）</summary>
        Info,
        /// <summary>警告提示（橙色）</summary>
        Warning,
        /// <summary>错误提示（红色）</summary>
        Error,
        /// <summary>确认询问（蓝色）</summary>
        Question
    }

    /// <summary>
    /// 对话框按钮组合
    /// </summary>
    public enum DialogButtons
    {
        /// <summary>仅"确定"按钮</summary>
        OK,
        /// <summary>"确定"和"取消"按钮</summary>
        OKCancel,
        /// <summary>"是"和"否"按钮</summary>
        YesNo
    }

    /// <summary>
    /// 自定义对话框窗口 — 替代系统 MessageBox，风格统一
    /// 
    /// 使用方式：
    ///   Dialog.ShowInfo("保存成功");
    ///   Dialog.ShowWarning("未找到匹配");
    ///   Dialog.ShowError("文件读取失败");
    ///   var result = Dialog.ShowConfirm("确定要删除吗？");
    /// </summary>
    public partial class DialogWindow : Window
    {
        /// <summary>
        /// 对话框返回结果
        /// </summary>
        public bool? DialogResultValue { get; private set; }

        private static readonly SolidColorBrush InfoBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xD9));
        private static readonly SolidColorBrush WarningBrush = new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12));
        private static readonly SolidColorBrush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));

        public DialogWindow(string message, string title, DialogType type, DialogButtons buttons)
        {
            InitializeComponent();

            // 设置标题
            TitleText.Text = title;

            // 设置消息
            MessageText.Text = message;

            // 根据类型设置标题栏颜色和图标
            ConfigureType(type);

            // 创建按钮
            ConfigureButtons(buttons);

            // 如果没有 Owner，尝试设置
            if (Owner == null && Application.Current?.MainWindow != null)
            {
                Owner = Application.Current.MainWindow;
            }
        }

        /// <summary>
        /// 根据对话框类型配置标题栏颜色和图标
        /// </summary>
        private void ConfigureType(DialogType type)
        {
            switch (type)
            {
                case DialogType.Info:
                    TitleBar.Background = InfoBrush;
                    IconText.Text = "💡";
                    break;
                case DialogType.Warning:
                    TitleBar.Background = WarningBrush;
                    IconText.Text = "⚠️";
                    break;
                case DialogType.Error:
                    TitleBar.Background = ErrorBrush;
                    IconText.Text = "❌";
                    break;
                case DialogType.Question:
                    TitleBar.Background = InfoBrush;
                    IconText.Text = "❓";
                    break;
            }
        }

        /// <summary>
        /// 根据按钮组合创建按钮
        /// </summary>
        private void ConfigureButtons(DialogButtons buttons)
        {
            ButtonPanel.Children.Clear();

            switch (buttons)
            {
                case DialogButtons.OK:
                    AddButton("确定", true, "PrimaryButton");
                    break;
                case DialogButtons.OKCancel:
                    AddButton("取消", false, "BaseButton", 12);
                    AddButton("确定", true, "PrimaryButton");
                    break;
                case DialogButtons.YesNo:
                    AddButton("否", false, "BaseButton", 12);
                    AddButton("是", true, "PrimaryButton");
                    break;
            }
        }

        /// <summary>
        /// 添加一个按钮到按钮面板
        /// </summary>
        private void AddButton(string content, bool isAffirmative, string styleKey, double marginLeft = 0)
        {
            var button = new Button
            {
                Content = content,
                Style = (Style)FindResource(styleKey),
                MinWidth = 80,
                Padding = new Thickness(20, 8, 20, 8),
                FontSize = 13,
                FontWeight = FontWeights.Medium
            };

            if (marginLeft > 0)
            {
                button.Margin = new Thickness(marginLeft, 0, 0, 0);
            }

            button.Click += (_, _) =>
            {
                DialogResultValue = isAffirmative;
                Close();
            };

            ButtonPanel.Children.Add(button);
        }

        /// <summary>
        /// 标题栏拖动
        /// </summary>
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }
    }

    /// <summary>
    /// 对话框静态快捷方法类 — 一行调用
    /// </summary>
    public static class Dialog
    {
        /// <summary>
        /// 显示信息提示对话框
        /// </summary>
        public static void ShowInfo(string message, string title = "提示")
        {
            ShowDialog(message, title, DialogType.Info, DialogButtons.OK);
        }

        /// <summary>
        /// 显示警告提示对话框
        /// </summary>
        public static void ShowWarning(string message, string title = "警告")
        {
            ShowDialog(message, title, DialogType.Warning, DialogButtons.OK);
        }

        /// <summary>
        /// 显示错误提示对话框
        /// </summary>
        public static void ShowError(string message, string title = "错误")
        {
            ShowDialog(message, title, DialogType.Error, DialogButtons.OK);
        }

        /// <summary>
        /// 显示确认对话框，返回用户是否确认
        /// </summary>
        public static bool ShowConfirm(string message, string title = "确认")
        {
            return ShowDialog(message, title, DialogType.Question, DialogButtons.YesNo) == true;
        }

        /// <summary>
        /// 通用显示方法，在 UI 线程以模态方式显示对话框
        /// </summary>
        private static bool? ShowDialog(string message, string title, DialogType type, DialogButtons buttons)
        {
            bool? result = null;

            void Show()
            {
                var dialog = new DialogWindow(message, title, type, buttons);
                dialog.ShowDialog();
                result = dialog.DialogResultValue;
            }

            if (Application.Current == null)
            {
                Show();
            }
            else if (Application.Current.Dispatcher.CheckAccess())
            {
                Show();
            }
            else
            {
                Application.Current.Dispatcher.Invoke(Show);
            }

            return result;
        }
    }
}