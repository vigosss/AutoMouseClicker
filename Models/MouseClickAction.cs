using Ming_AutoClicker.Services;
using System.Text.Json.Serialization;

namespace Ming_AutoClicker.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter<ClickPositionSource>))]
    public enum ClickPositionSource { Fixed, Variable }

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

        public ClickPositionSource PositionSource { get; set; } = ClickPositionSource.Fixed;
        public string VariableName { get; set; } = string.Empty;

        /// <summary>
        /// 操作类型（Click / RightClick / MiddleClick）
        /// </summary>
        public string Operation { get; set; } = "Click";

        /// <summary>
        /// 获取动作描述
        /// </summary>
        public override string GetDescription()
        {
            var operation = Operation switch
            {
                "Click" => LocalizationService.Current.GetString("EditorLeftClick"),
                "RightClick" => LocalizationService.Current.GetString("EditorRightClick"),
                "MiddleClick" => LocalizationService.Current.GetString("EditorMiddleClick"),
                _ => Operation
            };
            return PositionSource == ClickPositionSource.Variable
                ? LocalizationService.Current.Format("ActionClickVariable", VariableName, operation)
                : LocalizationService.Current.Format("ActionClickPosition", X, Y, operation);
        }

        public override string ToString() => GetDescription();

        /// <summary>
        /// 深拷贝
        /// </summary>
        public override MouseClickAction Clone()
        {
            return new MouseClickAction
            {
                Order = Order,
                Note = Note,
                X = X,
                Y = Y,
                PositionSource = PositionSource,
                VariableName = VariableName,
                Operation = Operation
            };
        }
    }
}
