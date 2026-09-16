using System.Windows;
using System.Windows.Input;
using Ming_AutoClicker.Models;
using Ming_AutoClicker.Services;

namespace Ming_AutoClicker.Views;

public partial class ItemHotkeyWindow : Window
{
    private HotkeyGesture? _candidate;

    public HotkeyGesture? Result { get; private set; }

    public ItemHotkeyWindow(HotkeyGesture? current)
    {
        InitializeComponent();
        _candidate = current?.Clone();
        UpdateCandidateDisplay();
        Loaded += (_, _) => Input.Focus();
    }

    private void OnHotkeyPreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or
            Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or
            Key.LWin or Key.RWin)
        {
            Error.Text = LocalizationService.Current.GetString("HotkeyContinue");
            return;
        }

        var modifiers = HotkeyModifierKeys.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= HotkeyModifierKeys.Control;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= HotkeyModifierKeys.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= HotkeyModifierKeys.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= (HotkeyModifierKeys)8;

        _candidate = new HotkeyGesture
        {
            VirtualKey = (uint)KeyInterop.VirtualKeyFromKey(key),
            Modifiers = modifiers
        };
        UpdateCandidateDisplay();
    }

    private void UpdateCandidateDisplay()
    {
        Input.Text = HotkeyGestureHelper.Format(_candidate);
        var valid = HotkeyGestureHelper.TryValidate(_candidate, out var error);
        Error.Text = valid ? string.Empty : error;
        ConfirmButton.IsEnabled = valid;
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        Result = null;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (!HotkeyGestureHelper.TryValidate(_candidate, out var error))
        {
            Error.Text = error;
            Input.Focus();
            return;
        }

        Result = _candidate?.Clone();
        DialogResult = true;
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        DialogResult = false;
    }

    private void OnInputGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => Input.SelectAll();

    private void OnInputMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Input.IsKeyboardFocusWithin) return;
        e.Handled = true;
        Input.Focus();
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }
}
