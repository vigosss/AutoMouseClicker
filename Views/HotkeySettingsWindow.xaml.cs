using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using Ming_AutoClicker.Models;
using Ming_AutoClicker.Services;

namespace Ming_AutoClicker.Views
{
    public partial class HotkeySettingsWindow : Window
    {
        private readonly Func<HotkeyGesture, AppLanguage, HotkeyRegistrationResult> _save;
        private readonly AppLanguage _originalLanguage;
        private HotkeyGesture _candidate;
        private AppLanguage _candidateLanguage;
        private bool _initializing = true;
        private bool _committed;

        public HotkeySettingsWindow(
            HotkeyGesture currentGesture,
            AppLanguage currentLanguage,
            Func<HotkeyGesture, AppLanguage, HotkeyRegistrationResult> save)
        {
            InitializeComponent();
            _candidate = currentGesture?.Clone() ?? HotkeyGesture.Default;
            _originalLanguage = currentLanguage;
            _candidateLanguage = currentLanguage;
            _save = save ?? throw new ArgumentNullException(nameof(save));
            LanguageComboBox.SelectedIndex = currentLanguage switch
            {
                AppLanguage.SimplifiedChinese => 1,
                AppLanguage.English => 2,
                _ => 0
            };
            _initializing = false;
            UpdateCandidateDisplay();

            Loaded += (_, _) => HotkeyInput.Focus();
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
                ValidationText.Text = LocalizationService.Current.GetString("HotkeyContinue");
                return;
            }

            var modifiers = ToHotkeyModifiers(Keyboard.Modifiers);
            _candidate = new HotkeyGesture
            {
                VirtualKey = (uint)KeyInterop.VirtualKeyFromKey(key),
                Modifiers = modifiers
            };
            UpdateCandidateDisplay();
        }

        private void UpdateCandidateDisplay()
        {
            HotkeyInput.Text = HotkeyGestureHelper.Format(_candidate);
            var valid = HotkeyGestureHelper.TryValidate(_candidate, out var error);
            ValidationText.Text = valid ? string.Empty : error;
            SaveButton.IsEnabled = valid;
        }

        private static HotkeyModifierKeys ToHotkeyModifiers(ModifierKeys modifiers)
        {
            var result = HotkeyModifierKeys.None;
            if (modifiers.HasFlag(ModifierKeys.Control)) result |= HotkeyModifierKeys.Control;
            if (modifiers.HasFlag(ModifierKeys.Alt)) result |= HotkeyModifierKeys.Alt;
            if (modifiers.HasFlag(ModifierKeys.Shift)) result |= HotkeyModifierKeys.Shift;

            // 保留一个未知位交给统一校验器拒绝 Windows 键。
            if (modifiers.HasFlag(ModifierKeys.Windows)) result |= (HotkeyModifierKeys)8;
            return result;
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (!HotkeyGestureHelper.TryValidate(_candidate, out var error))
            {
                ValidationText.Text = error;
                return;
            }

            var result = _save(_candidate.Clone(), _candidateLanguage);
            if (!result.Success)
            {
                ValidationText.Text = result.Message;
                HotkeyInput.Focus();
                return;
            }

            _committed = true;
            DialogResult = true;
        }

        private void OnResetClick(object sender, RoutedEventArgs e)
        {
            _candidate = HotkeyGesture.Default;
            UpdateCandidateDisplay();
            HotkeyInput.Focus();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

        private void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing || LanguageComboBox.SelectedItem is not ComboBoxItem item ||
                !Enum.TryParse<AppLanguage>(item.Tag?.ToString(), out var language))
            {
                return;
            }

            _candidateLanguage = language;
            LocalizationService.Current.ApplyLanguage(language);
            UpdateCandidateDisplay();
        }

        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (!_committed)
                LocalizationService.Current.ApplyLanguage(_originalLanguage);
            base.OnClosed(e);
        }

        private void OnInputGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            HotkeyInput.SelectAll();
        }

        private void OnInputMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!HotkeyInput.IsKeyboardFocusWithin)
            {
                e.Handled = true;
                HotkeyInput.Focus();
            }
        }

        private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }
    }
}
