using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Ming_AutoClicker.Views;

namespace Ming_AutoClicker.ViewModels
{
    /// <summary>
    /// ViewModel 基类 - 实现 INotifyPropertyChanged
    /// </summary>
    public class ViewModelBase : INotifyPropertyChanged, IDisposable
    {
        private bool _disposed;

        /// <summary>
        /// 属性变更事件
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// 触发属性变更通知
        /// </summary>
        /// <param name="propertyName">属性名称（自动获取）</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// 设置属性值并触发通知
        /// </summary>
        /// <typeparam name="T">属性类型</typeparam>
        /// <param name="field">字段引用</param>
        /// <param name="value">新值</param>
        /// <param name="propertyName">属性名称</param>
        /// <returns>是否发生了变更</returns>
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        /// <summary>
        /// 在 UI 线程执行操作（同步阻塞）
        /// </summary>
        protected void OnUIThread(Action action)
        {
            if (Application.Current == null)
            {
                action();
            }
            else if (Application.Current.Dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                Application.Current.Dispatcher.Invoke(action);
            }
        }

        /// <summary>
        /// 在 UI 线程异步执行操作（非阻塞，适合高频更新场景）
        /// </summary>
        protected void BeginOnUIThread(Action action)
        {
            if (Application.Current == null)
            {
                action();
            }
            else if (Application.Current.Dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                Application.Current.Dispatcher.BeginInvoke(action);
            }
        }

        /// <summary>
        /// 显示消息框（使用自定义对话框）
        /// </summary>
        protected void ShowMessage(string message, string title = "提示", MessageBoxImage icon = MessageBoxImage.Information)
        {
            switch (icon)
            {
                case MessageBoxImage.Warning:
                    Dialog.ShowWarning(message, title);
                    break;
                case MessageBoxImage.Error:
                    Dialog.ShowError(message, title);
                    break;
                default:
                    Dialog.ShowInfo(message, title);
                    break;
            }
        }

        /// <summary>
        /// 显示确认对话框（使用自定义对话框）
        /// </summary>
        protected bool ShowConfirm(string message, string title = "确认")
        {
            return Dialog.ShowConfirm(message, title);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Dispose(true);
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            // 子类重写以释放资源
        }
    }
}
