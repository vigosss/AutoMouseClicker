using System.Collections.Generic;
using Ming_AutoClicker.Models;

namespace Ming_AutoClicker.Services
{
    /// <summary>
    /// 热键范围校验与面向用户的显示格式。
    /// </summary>
    public static class HotkeyGestureHelper
    {
        private const uint Key0 = 0x30;
        private const uint Key9 = 0x39;
        private const uint KeyA = 0x41;
        private const uint KeyZ = 0x5A;
        private const uint NumPad0 = 0x60;
        private const uint NumPad9 = 0x69;
        private const uint F1 = 0x70;
        private const uint F12 = 0x7B;
        private const HotkeyModifierKeys SupportedModifiers =
            HotkeyModifierKeys.Control | HotkeyModifierKeys.Alt | HotkeyModifierKeys.Shift;

        public static bool TryValidate(HotkeyGesture? gesture, out string errorMessage)
        {
            if (gesture == null)
            {
                errorMessage = "请按下一个快捷键组合";
                return false;
            }

            if ((gesture.Modifiers & ~SupportedModifiers) != 0)
            {
                errorMessage = "不支持 Windows 键，请使用 Ctrl、Alt 或 Shift";
                return false;
            }

            var isFunctionKey = gesture.VirtualKey is >= F1 and <= F12;
            var isLetter = gesture.VirtualKey is >= KeyA and <= KeyZ;
            var isTopRowNumber = gesture.VirtualKey is >= Key0 and <= Key9;
            var isNumPadNumber = gesture.VirtualKey is >= NumPad0 and <= NumPad9;

            if (!isFunctionKey && !isLetter && !isTopRowNumber && !isNumPadNumber)
            {
                errorMessage = "仅支持 F1–F12，或带 Ctrl/Alt/Shift 的字母和数字";
                return false;
            }

            if (!isFunctionKey && gesture.Modifiers == HotkeyModifierKeys.None)
            {
                errorMessage = "字母和数字必须搭配 Ctrl、Alt 或 Shift，以避免打字时误触";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        public static string Format(HotkeyGesture? gesture)
        {
            if (gesture == null)
                return "未启用";

            var parts = new List<string>();
            if (gesture.Modifiers.HasFlag(HotkeyModifierKeys.Control)) parts.Add("Ctrl");
            if (gesture.Modifiers.HasFlag(HotkeyModifierKeys.Alt)) parts.Add("Alt");
            if (gesture.Modifiers.HasFlag(HotkeyModifierKeys.Shift)) parts.Add("Shift");
            parts.Add(FormatKey(gesture.VirtualKey));
            return string.Join(" + ", parts);
        }

        private static string FormatKey(uint virtualKey)
        {
            if (virtualKey is >= KeyA and <= KeyZ)
                return ((char)virtualKey).ToString();
            if (virtualKey is >= Key0 and <= Key9)
                return ((char)virtualKey).ToString();
            if (virtualKey is >= NumPad0 and <= NumPad9)
                return $"Num {virtualKey - NumPad0}";
            if (virtualKey is >= F1 and <= F12)
                return $"F{virtualKey - F1 + 1}";

            return $"0x{virtualKey:X2}";
        }
    }
}
