namespace Ming_AutoClicker.Models
{
    /// <summary>
    /// 鼠标点击位置动作 - 在指定坐标执行点击操作
    /// </summary>
    public class MouseClickAction : MacroAction
    {
        /// <summary>
        /// 动作类型为鼠标点击位置
        /// </summary>
        public override ActionType Type => ActionType.MouseClick;

        /// <summary>
        /// 点击 X 坐标（屏幕坐标）
        /// </summary>
        public int X { get; set; } = 0;

        /// <summary>
        /// 点击 Y 坐标（屏幕坐标）
        /// </summary>
        public int Y { get; set; } = 0;

        /// <summary>
        /// 操作类型（Click / RightClick / MiddleClick）
        /// </summary>
        public string Operation { get; set; } = "Click";

        /// <summary>
        /// 获取动作描述
        /// </summary>
        public override string GetDescription()
        {
            return $"点击位置: ({X}, {Y}) - {Operation}";
        }

        public override string ToString() => GetDescription();

        /// <summary>
        /// 深拷贝
        /// </summary>
        public MouseClickAction Clone()
        {
            return new MouseClickAction
            {
                Order = Order,
                X = X,
                Y = Y,
                Operation = Operation
            };
        }
    }
}
