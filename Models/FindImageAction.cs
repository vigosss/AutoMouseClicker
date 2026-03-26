using System.IO;

namespace Ming_AutoClicker.Models
{
    /// <summary>
    /// 找图动作 - 在屏幕上查找图像并执行操作
    /// </summary>
    public class FindImageAction : MacroAction
    {
        /// <summary>
        /// 动作类型为找图
        /// </summary>
        public override ActionType Type => ActionType.FindImage;

        /// <summary>
        /// 截图文件路径（相对路径）
        /// </summary>
        public string ImagePath { get; set; } = string.Empty;

        /// <summary>
        /// 匹配度阈值 (0.0 - 1.0)
        /// </summary>
        public double MatchThreshold { get; set; } = 0.8;

        /// <summary>
        /// 是否循环等待直到找到
        /// </summary>
        public bool WaitUntilFound { get; set; } = false;

        /// <summary>
        /// 找到后的操作类型
        /// </summary>
        public string Operation { get; set; } = "Click";

        /// <summary>
        /// 点击偏移X（相对于匹配图像中心）
        /// </summary>
        public int OffsetX { get; set; } = 0;

        /// <summary>
        /// 点击偏移Y（相对于匹配图像中心）
        /// </summary>
        public int OffsetY { get; set; } = 0;

        /// <summary>
        /// 获取动作描述
        /// </summary>
        public override string GetDescription()
        {
            var fileName = Path.GetFileName(ImagePath);
            var waitText = WaitUntilFound ? " (等待)" : "";
            return $"找图: {fileName}{waitText} - {Operation}";
        }
    }
}