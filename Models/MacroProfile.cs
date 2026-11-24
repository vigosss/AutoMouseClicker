using System;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Ming_AutoClicker.Services;

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
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 最后修改时间
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 宏在列表中的稳定顺序。null 表示由旧版本创建、尚未迁移。
        /// </summary>
        public int? SortOrder { get; set; }

        /// <summary>
        /// 动作列表
        /// </summary>
        public ObservableCollection<MacroAction> Actions { get; set; } = new();

        /// <summary>
        /// 是否启用循环执行
        /// </summary>
        public bool LoopEnabled { get; set; } = false;

        /// <summary>
        /// 循环次数（0表示无限循环）
        /// </summary>
        public int LoopCount { get; set; } = 0;

        [JsonIgnore]
        public string RunCountText => (LoopEnabled ? LoopCount : 1) switch
        {
            0 => LocalizationService.Current.GetString("MacroUnlimitedRuns"),
            1 => LocalizationService.Current.GetString("MacroSingleRun"),
            var count => LocalizationService.Current.Format("MacroRunCount", count)
        };

        /// <summary>
        /// 循环间隔（毫秒）
        /// </summary>
        public int LoopIntervalMs { get; set; } = 1000;

        public HotkeyGesture? Hotkey { get; set; }

        /// <summary>
        /// 深拷贝宏配置（包括所有动作）
        /// </summary>
        public MacroProfile DeepClone() => DeepClone(Actions.Count);

        internal MacroProfile DeepClone(int actionCount)
        {
            var clone = new MacroProfile
            {
                Id = Id,
                Name = Name,
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt,
                SortOrder = SortOrder,
                LoopEnabled = LoopEnabled,
                LoopCount = LoopCount,
                LoopIntervalMs = LoopIntervalMs,
                Hotkey = Hotkey?.Clone()
            };

            for (int i = 0; i < actionCount; i++)
                clone.Actions.Add(Actions[i].Clone());

            return clone;
        }
    }
}
