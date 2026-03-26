using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Ming_AutoClicker.Models
{
    /// <summary>
    /// 宏配置文件 - 包含一组宏动作
    /// </summary>
    public class MacroProfile
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// 宏名称
        /// </summary>
        public string Name { get; set; } = "新宏";

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 最后修改时间
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 动作列表
        /// </summary>
        public List<MacroAction> Actions { get; set; } = new();

        /// <summary>
        /// 是否启用循环执行
        /// </summary>
        public bool LoopEnabled { get; set; } = false;

        /// <summary>
        /// 循环次数（0表示无限循环）
        /// </summary>
        public int LoopCount { get; set; } = 0;

        /// <summary>
        /// 循环间隔（毫秒）
        /// </summary>
        public int LoopIntervalMs { get; set; } = 1000;
    }
}