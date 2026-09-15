using System;

namespace Ming_AutoClicker.Models
{
    [Flags]
    public enum HotkeyModifierKeys : uint
    {
        None = 0,
        Alt = 1,
        Control = 2,
        Shift = 4
    }

    /// <summary>
    /// 与界面和持久化共用的热键描述，不依赖 WPF Key 枚举。
    /// </summary>
    public sealed class HotkeyGesture : IEquatable<HotkeyGesture>
    {
        public const uint F8VirtualKey = 0x77;

        public uint VirtualKey { get; set; } = F8VirtualKey;
        public HotkeyModifierKeys Modifiers { get; set; } = HotkeyModifierKeys.None;

        public static HotkeyGesture Default => new HotkeyGesture();

        public HotkeyGesture Clone()
        {
            return new HotkeyGesture
            {
                VirtualKey = VirtualKey,
                Modifiers = Modifiers
            };
        }

        public bool Equals(HotkeyGesture? other)
        {
            return other != null && VirtualKey == other.VirtualKey && Modifiers == other.Modifiers;
        }

        public override bool Equals(object? obj) => Equals(obj as HotkeyGesture);

        public override int GetHashCode() => HashCode.Combine(VirtualKey, Modifiers);
    }

    public sealed class HotkeySettings
    {
        /// <summary>
        /// 当前按所在页签切换连点或宏执行的全局热键。
        /// 后续可在此类型中增加暂停、停止等独立动作。
        /// </summary>
        public HotkeyGesture ToggleExecution { get; set; } = HotkeyGesture.Default;
    }

    public sealed class AppSettings
    {
        public int SchemaVersion { get; set; } = 1;
        public HotkeySettings Hotkeys { get; set; } = new HotkeySettings();
    }
}
