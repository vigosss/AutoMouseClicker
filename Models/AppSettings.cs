using System;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace Ming_AutoClicker.Models
{
    [JsonConverter(typeof(AppLanguageJsonConverter))]
    public enum AppLanguage
    {
        System,
        SimplifiedChinese,
        English
    }

    public sealed class AppLanguageJsonConverter : JsonConverter<AppLanguage>
    {
        public override AppLanguage Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String &&
                Enum.TryParse<AppLanguage>(reader.GetString(), ignoreCase: true, out var value) &&
                Enum.IsDefined(value))
            {
                return value;
            }

            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var numeric) &&
                Enum.IsDefined(typeof(AppLanguage), numeric))
            {
                return (AppLanguage)numeric;
            }

            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                reader.Skip();

            return AppLanguage.System;
        }

        public override void Write(Utf8JsonWriter writer, AppLanguage value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(Enum.IsDefined(value) ? value.ToString() : AppLanguage.System.ToString());
        }
    }

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
        public int SchemaVersion { get; set; } = 2;
        public AppLanguage Language { get; set; } = AppLanguage.System;
        public HotkeySettings Hotkeys { get; set; } = new HotkeySettings();
    }
}
