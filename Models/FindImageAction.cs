using System.IO;
using Ming_AutoClicker.Services;

namespace Ming_AutoClicker.Models
{
    /// <summary>
    /// 找图动作 - 在屏幕上查找图像并执行操作
    /// </summary>
    public class FindImageAction : MacroAction
    {
        private double _matchThreshold = 0.8;

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
        public double MatchThreshold
        {
            get => _matchThreshold;
            set
            {
                // 钳位到合法范围，避免反序列化时抛异常
                _matchThreshold = double.IsFinite(value) ? Math.Clamp(value, 0.0, 1.0) : 0.8;
            }
        }

        /// <summary>
        /// 是否循环等待直到找到
        /// </summary>
        public bool WaitUntilFound { get; set; } = false;

        /// <summary>
        /// 是否启用宽范围自适应缩放匹配
        /// </summary>
        public bool AdaptiveScale { get; set; } = false;

        /// <summary>Null preserves legacy WaitUntilFound semantics; zero means one bounded search.</summary>
        public int? TimeoutMs { get; set; }
        public ImageClickPoint? ClickPoint { get; set; }
        public List<ImageRelativeRegion> IgnoredRegions { get; set; } = new();
        public double? CaptureDpi { get; set; }
        public List<ImageTemplateVariant> AlternateTemplates { get; set; } = new();
        public ImageSearchScope SearchScope { get; set; } = new();
        public ImageSelectionMode SelectionMode { get; set; }
        /// <summary>One based, ordered by screen Y then X after spatial deduplication.</summary>
        public int MatchIndex { get; set; } = 1;

        public IEnumerable<ImageTemplateVariant> GetTemplates()
        {
            yield return new ImageTemplateVariant
            {
                ImagePath = ImagePath, ClickPoint = ClickPoint,
                IgnoredRegions = IgnoredRegions, CaptureDpi = CaptureDpi
            };
            foreach (var template in AlternateTemplates) yield return template;
        }

        public int GetTimeoutMs(int legacyTimeoutMs = 30000) =>
            TimeoutMs ?? (WaitUntilFound ? legacyTimeoutMs : 0);

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
            var waitText = GetTimeoutMs() > 0
                ? LocalizationService.Current.GetString("ActionWaitingSuffix")
                : string.Empty;
            var operation = Operation switch
            {
                "Click" => LocalizationService.Current.GetString("EditorLeftClick"),
                "RightClick" => LocalizationService.Current.GetString("EditorRightClick"),
                _ => Operation
            };
            return LocalizationService.Current.Format("ActionFindImage", fileName, waitText, operation);
        }

        public override string ToString() => GetDescription();

        /// <summary>
        /// 深拷贝
        /// </summary>
        public FindImageAction Clone()
        {
            return new FindImageAction
            {
                Order = Order,
                Note = Note,
                ImagePath = ImagePath,
                MatchThreshold = MatchThreshold,
                WaitUntilFound = WaitUntilFound,
                AdaptiveScale = AdaptiveScale,
                TimeoutMs = TimeoutMs,
                ClickPoint = ClickPoint?.Clone(),
                IgnoredRegions = IgnoredRegions.Select(r => r.Clone()).ToList(),
                CaptureDpi = CaptureDpi,
                AlternateTemplates = AlternateTemplates.Select(t => t.Clone()).ToList(),
                SearchScope = SearchScope.Clone(),
                SelectionMode = SelectionMode,
                MatchIndex = MatchIndex,
                Operation = Operation,
                OffsetX = OffsetX,
                OffsetY = OffsetY
            };
        }
    }
}
