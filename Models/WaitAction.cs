namespace Ming_AutoClicker.Models
{
    /// <summary>
    /// 等待动作 - 暂停执行指定时间
    /// </summary>
    public class WaitAction : MacroAction
    {
        /// <summary>
        /// 动作类型为等待
        /// </summary>
        public override ActionType Type => ActionType.Wait;

        /// <summary>
        /// 等待时间（秒）
        /// </summary>
        public double WaitSeconds { get; set; } = 1.0;

        /// <summary>
        /// 获取动作描述
        /// </summary>
        public override string GetDescription()
        {
            return $"等待: {WaitSeconds:F1} 秒";
        }
    }
}