using System.Text.Json.Serialization;

namespace Ming_AutoClicker.Models
{
    /// <summary>
    /// 宏动作类型枚举
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ActionType
    {
        FindImage,
        Wait,
        MouseClick
    }

    /// <summary>
    /// 宏动作基类 - 所有宏动作的抽象基类
    /// </summary>
    public abstract class MacroAction
    {
        /// <summary>
        /// 动作执行顺序
        /// </summary>
        public int Order { get; set; }

        /// <summary>
        /// 动作类型
        /// </summary>
        public abstract ActionType Type { get; }

        /// <summary>
        /// 获取动作描述
        /// </summary>
        public abstract string GetDescription();
    }
}