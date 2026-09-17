using System.Windows;
using System.Windows.Input;

namespace Ming_AutoClicker.Helpers
{
    /// <summary>
    /// Commands used by the custom window chrome. They operate on the Window
    /// passed as CommandParameter instead of relying on routed system bindings.
    /// </summary>
    public static class WindowCommands
    {
        public static ICommand Minimize { get; } = new RelayCommand(parameter =>
        {
            if (parameter is Window window)
            {
                window.WindowState = WindowState.Minimized;
            }
        });

        public static ICommand ToggleMaximize { get; } = new RelayCommand(parameter =>
        {
            if (parameter is Window window)
            {
                window.WindowState = window.WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
            }
        });

        public static ICommand Close { get; } = new RelayCommand(parameter =>
        {
            if (parameter is Window window)
            {
                window.Close();
            }
        });
    }
}
