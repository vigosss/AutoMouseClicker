namespace Ming_AutoClicker.Models
{
    /// <summary>
    /// 等待动作 - 暂停执行指定时间
    /// </summary>
    public class WaitAction : MacroAction
    {
        private double _waitSeconds = 1.0;

        /// <summary>
        /// 动作类型为等待
        /// </summary>
        public override ActionType Type => ActionType.Wait;

        /// <summary>
        /// 等待时间（秒）
        /// </summary>
        public double WaitSeconds
        {
            get => _waitSeconds;
            set
            {
                // 钳位到合法范围，避免反序列化时抛异常
                _waitSeconds = Math.Max(0, Math.Min(3600, value));
            }
        }

        /// <summary>
        /// 获取动作描述
        /// </summary>
        public override string GetDescription()
        {
            return $"等待: {WaitSeconds:F1} 秒";
        }

        public override string ToString() => GetDescription();

        /// <summary>
        /// 深拷贝
        /// </summary>
        public WaitAction Clone()
        {
            return new WaitAction
            {
                Order = Order,
                WaitSeconds = WaitSeconds
            };
        }
    }
}
